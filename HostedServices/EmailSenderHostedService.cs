namespace BackgroundEmailSenderSample.HostedServices;

public class EmailSenderHostedService : IEmailSender, IHostedService, IDisposable
{
    private const int DefaultQueueCapacity = 1024;

    private readonly IDatabaseAccessor db;
    private readonly IOptionsMonitor<SmtpOptions> optionsMonitor;
    private readonly ILogger<EmailSenderHostedService> logger;
    private readonly BufferBlock<MimeMessage> mailMessages;
    private readonly Lock clientLock = new();

    private CancellationTokenSource deliveryCancellationTokenSource;
    private Task deliveryTask;
    private SmtpClient smtpClient;
    private SmtpOptions connectedOptions;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailSenderHostedService"/> class.
    /// </summary>
    /// <param name="db">Persistent store accessor used to persist and resume e-mail messages across restarts.</param>
    /// <param name="optionsMonitor">Options monitor providing <see cref="SmtpOptions"/> which can change at runtime; this service reacts to changes on next send.</param>
    /// <param name="logger">Logger used to record operational events and errors.</param>
    /// <remarks>
    /// The service maintains a bounded in-memory queue to provide backpressure under bursts and uses a single <see cref="MailKit.Net.Smtp.SmtpClient"/> instance
    /// that is reused while the configured <see cref="SmtpOptions"/> remain the same. Persistence via <paramref name="db"/> ensures messages survive process restarts.
    /// </remarks>
    public EmailSenderHostedService(IDatabaseAccessor db, IOptionsMonitor<SmtpOptions> optionsMonitor, ILogger<EmailSenderHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(logger);

        this.db = db;
        this.optionsMonitor = optionsMonitor;
        this.logger = logger;

        // Bounded queue prevents unbounded memory growth under bursts.
        mailMessages = new BufferBlock<MimeMessage>(new DataflowBlockOptions
        {
            BoundedCapacity = DefaultQueueCapacity
        });

        smtpClient = new SmtpClient();
    }

    /// <summary>
    /// Enqueues an e-mail for background delivery.
    /// </summary>
    /// <param name="email">Recipient e-mail address. Must not be <see langword="null" /> or empty.</param>
    /// <param name="subject">E-mail subject. Must not be <see langword="null" />.</param>
    /// <param name="htmlMessage">HTML body of the e-mail. Must not be <see langword="null" />.</param>
    /// <returns>A <see cref="Task"/> that completes when the message has been persisted and accepted into the in-memory queue.</returns>
    /// <remarks>
    /// This method persists the message to the configured <see cref="IDatabaseAccessor"/> before enqueuing to guarantee delivery even if the process restarts.
    /// The in-memory queue is bounded to DefaultQueueCapacity to provide backpressure; callers may experience latency when the queue is full.
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// await emailSender.SendEmailAsync("user@example.com", "Hello", "&lt;h1&gt;Welcome&lt;/h1&gt;");
    /// </code>
    /// </example>
    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(htmlMessage);

        var message = CreateMessage(email, subject, htmlMessage);

        var affectedRows = await db
            .CommandAsync($@"INSERT INTO EmailMessages (Id, Recipient, Subject, Message, SenderCount, Status) VALUES ({message.MessageId}, {email}, {subject}, {htmlMessage}, 0, {nameof(MailStatus.InProgress)})")
            .ConfigureAwait(false);

        if (affectedRows != 1)
        {
            throw new InvalidOperationException($"Could not persist email message to {email}");
        }

        // Await so backpressure is respected when the queue is bounded.
        await mailMessages.SendAsync(message).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the hosted service and resumes delivering any persisted but unsent messages.
    /// </summary>
    /// <param name="token">Cancellation token provided by the host. This token is observed while starting operations.</param>
    /// <returns>A <see cref="Task"/> that completes when startup work is finished and background delivery has been scheduled.</returns>
    /// <remarks>
    /// On start, the service queries the persistent store for messages whose status is not <see cref="MailStatus.Sent"/> or <see cref="MailStatus.Deleted"/>
    /// and enqueues them for delivery. Delivery runs on a background task started within this method; this method completes once the background task has been scheduled.
    /// </remarks>
    public async Task StartAsync(CancellationToken token)
    {
        logger.LogInformation("Starting background e-mail delivery");

        FormattableString query = $@"SELECT Id, Recipient, Subject, Message FROM EmailMessages WHERE Status NOT IN ({nameof(MailStatus.Sent)}, {nameof(MailStatus.Deleted)})";
        var dataSet = await db.QueryAsync(query, token).ConfigureAwait(false);

        try
        {
            var rows = dataSet.Tables[0].Rows;

            foreach (DataRow row in rows)
            {
                var message = CreateMessage(Convert.ToString(row["Recipient"])!,
                                                        Convert.ToString(row["Subject"])!,
                                                        Convert.ToString(row["Message"])!,
                                                        Convert.ToString(row["Id"]));
                await mailMessages.SendAsync(message, token).ConfigureAwait(false);
            }

            logger.LogInformation("Email delivery started: {count} message(s) were resumed for delivery", rows.Count);

            deliveryCancellationTokenSource = new CancellationTokenSource();
            deliveryTask = Task.Run(() => DeliverAsync(deliveryCancellationTokenSource.Token), CancellationToken.None);
        }
        catch (Exception startException)
        {
            logger.LogError(startException, "Couldn't start email delivery");
            throw;
        }
    }

    /// <summary>
    /// Stops the hosted service and waits for the background delivery task to finish or for the host-provided token to cancel waiting.
    /// </summary>
    /// <param name="token">Cancellation token provided by the host which may shorten the time the host waits for graceful shutdown.</param>
    /// <returns>A <see cref="Task"/> that completes once stop handling is finished or the host stops waiting.</returns>
    /// <remarks>
    /// This method requests cancellation of the delivery loop and then awaits either the completion of the delivery task or the host cancellation token.
    /// Exceptions thrown during cancellation are logged but not rethrown to avoid throwing during shutdown.
    /// </remarks>
    public async Task StopAsync(CancellationToken token)
    {
        CancelDeliveryTask();

        // Wait for the delivery task to finish, or for the host to stop waiting via the provided token.
        var taskToAwait = deliveryTask ?? Task.CompletedTask;
        await Task.WhenAny(taskToAwait, Task.Delay(Timeout.Infinite, token)).ConfigureAwait(false);
    }

    private void CancelDeliveryTask()
    {
        try
        {
            if (deliveryCancellationTokenSource is not null)
            {
                logger.LogInformation("Stopping e-mail background delivery");
                deliveryCancellationTokenSource.Cancel();
                deliveryCancellationTokenSource.Dispose();
                deliveryCancellationTokenSource = null;
            }
        }
        catch (Exception ex)
        {
            // Swallowing is intentional to avoid throwing during shutdown; log for visibility.
            logger.LogWarning(ex, "Exception while cancelling delivery task");
        }
    }

    /// <summary>
    /// Main delivery loop executed on a background thread that receives enqueued <see cref="MimeMessage"/> instances and sends them.
    /// </summary>
    /// <param name="token">Cancellation token used to stop the delivery loop.</param>
    /// <returns>A <see cref="Task"/> that completes when the delivery loop exits due to cancellation or an unrecoverable failure.</returns>
    /// <remarks>
    /// Behavior notes:
    /// - Reuses a single SMTP connection while the <see cref="SmtpOptions"/> instance remains the same to reduce connect/auth overhead.
    /// - On transient failures, increments a SenderCount in persistent storage and requeues the message if retry limit hasn't been reached.
    /// - When an error occurs, waits for <see cref="SmtpOptions.DelayOnError"/> before processing the next message and recreates the SMTP client to ensure clean reconnects.
    /// - Observes <paramref name="token"/> for cooperative cancellation.
    /// </remarks>
    public async Task DeliverAsync(CancellationToken token)
    {
        logger.LogInformation("E-mail background delivery started");

        while (!token.IsCancellationRequested)
        {
            MimeMessage message = null;

            try
            {
                message = await mailMessages.ReceiveAsync(token).ConfigureAwait(false);

                // Capture current options once for this iteration.
                var options = optionsMonitor.CurrentValue;

                // Ensure SMTP client is connected and authenticated for the current options.
                await EnsureConnectedAsync(options, token).ConfigureAwait(false);

                // Send
                await smtpClient.SendAsync(message, token).ConfigureAwait(false);

                // Persist status as Sent
                await db.CommandAsync($"UPDATE EmailMessages SET Status={nameof(MailStatus.Sent)} WHERE Id={message.MessageId}", token).ConfigureAwait(false);

                logger.LogInformation("E-mail sent successfully to {recipient}", message.To);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception sendException)
            {
                var recipient = message?.To.OfType<MailboxAddress>().FirstOrDefault()?.Address;
                logger.LogError(sendException, "Couldn't send an e-mail to {recipient}", recipient);

                try
                {
                    var options = optionsMonitor.CurrentValue;
                    var shouldRequeue = await db.QueryScalarAsync<bool>($@"UPDATE EmailMessages SET SenderCount = SenderCount + 1, Status=CASE WHEN SenderCount < {options.MaxSenderCount} THEN Status ELSE {nameof(MailStatus.Deleted)} END WHERE Id={message?.MessageId};
                                                                            SELECT COUNT(*) FROM EmailMessages WHERE Id={message?.MessageId} AND Status NOT IN ({nameof(MailStatus.Deleted)}, {nameof(MailStatus.Sent)})", token).ConfigureAwait(false);

                    if (shouldRequeue && message is not null)
                    {
                        // Respect cancellation while requeueing
                        await mailMessages.SendAsync(message, token).ConfigureAwait(false);
                    }
                }
                catch (Exception requeueException)
                {
                    logger.LogError(requeueException, "Couldn't requeue message to {recipient}", recipient);
                }

                // If sending failed, wait before processing the next message to avoid tight retry loops.
                try
                {
                    await Task.Delay(optionsMonitor.CurrentValue.DelayOnError, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // On error, reset the SMTP connection so next iteration reconnects cleanly.
                ResetSmtpClient();
            }
        }

        logger.LogInformation("E-mail background delivery stopped");
    }

    private async Task EnsureConnectedAsync(SmtpOptions options, CancellationToken token)
    {
        // Fast-path: already connected and options haven't changed (reference equality is sufficient for IOptionsMonitor)
        if (smtpClient.IsConnected && ReferenceEquals(options, connectedOptions))
        {
            return;
        }

        // Single-threaded consumer but guard mutations just in case.
        lock (clientLock)
        {
            // If caller races, avoid double-connect; checked again inside lock before connecting.
            if (smtpClient.IsConnected && ReferenceEquals(options, connectedOptions))
            {
                return;
            }

            // If connected but options changed, disconnect first.
            if (smtpClient.IsConnected)
            {
                try
                {
                    smtpClient.Disconnect(true);
                }
                catch
                {
                    // Ignored here; we'll recreate the client below.
                }

                smtpClient.Dispose();
                smtpClient = new SmtpClient();
                connectedOptions = null;
            }
        }

        // Connect/authenticate outside lock to avoid blocking other operations for long.
        await smtpClient.ConnectAsync(options.Host, options.Port, options.Security == SecureSocketOptions.SslOnConnect ? SecureSocketOptions.SslOnConnect : options.Security, token).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(options.Username))
        {
            await smtpClient.AuthenticateAsync(options.Username, options.Password, token).ConfigureAwait(false);
        }

        // Only set connectedOptions after successful connect/authenticate.
        connectedOptions = options;
    }

    private void ResetSmtpClient()
    {
        lock (clientLock)
        {
            try
            {
                if (smtpClient.IsConnected)
                {
                    smtpClient.Disconnect(true);
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                smtpClient.Dispose();
            }
            catch
            {
                // ignore
            }

            smtpClient = new SmtpClient();
            connectedOptions = null;
        }
    }

    /// <summary>
    /// Disposes resources used by the hosted service.
    /// </summary>
    /// <remarks>
    /// This method is idempotent. It cancels the delivery task, disposes the SMTP client, and completes the in-memory queue.
    /// Callers should not use the instance after disposal.
    /// </remarks>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        CancelDeliveryTask();

        try
        {
            smtpClient?.Dispose();
        }
        catch
        {
            // ignored
        }

        mailMessages.Complete();
        disposed = true;
    }

    private MimeMessage CreateMessage(string email, string subject, string htmlMessage, string messageId = null)
    {
        var message = new MimeMessage();

        var sender = optionsMonitor.CurrentValue.Sender;
        message.From.Add(MailboxAddress.Parse(sender));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = subject;

        // If messageId not provided generate one.
        message.MessageId = messageId ?? GuidV8Time.NewGuid().ToString();
        message.Body = new TextPart("html") { Text = htmlMessage };

        return message;
    }
}