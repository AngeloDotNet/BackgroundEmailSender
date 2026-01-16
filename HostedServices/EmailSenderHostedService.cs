namespace BackgroundEmailSenderSample.HostedServices;

public class EmailSenderHostedService : IEmailSender, IHostedService, IDisposable
{
    private const int DefaultQueueCapacity = 1024;

    private readonly IDatabaseAccessor db;
    private readonly IOptionsMonitor<SmtpOptions> optionsMonitor;
    private readonly ILogger<EmailSenderHostedService> logger;
    private readonly BufferBlock<MimeMessage> mailMessages;
    private readonly Lock clientLock = new();

    private CancellationTokenSource? deliveryCancellationTokenSource;
    private Task? deliveryTask;
    private SmtpClient smtpClient;
    private SmtpOptions? connectedOptions;
    private bool disposed;

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

    /// <inheritdoc />
    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(htmlMessage);

        var message = CreateMessage(email, subject, htmlMessage);

        var affectedRows = await db
            .CommandAsync($@"INSERT INTO EmailMessages (Id, Recipient, Subject, Message, SenderCount, Status)
                            VALUES ({message.MessageId}, {email}, {subject}, {htmlMessage}, 0, {nameof(MailStatus.InProgress)})")
            .ConfigureAwait(false);

        if (affectedRows != 1)
        {
            throw new InvalidOperationException($"Could not persist email message to {email}");
        }

        // Await so backpressure is respected when the queue is bounded.
        await mailMessages.SendAsync(message).ConfigureAwait(false);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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
    /// Main delivery loop. Reuses a single SMTP connection while it is valid to reduce connect/auth overhead.
    /// </summary>
    public async Task DeliverAsync(CancellationToken token)
    {
        logger.LogInformation("E-mail background delivery started");

        while (!token.IsCancellationRequested)
        {
            MimeMessage? message = null;

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

    private MimeMessage CreateMessage(string email, string subject, string htmlMessage, string? messageId = null)
    {
        var message = new MimeMessage();

        var sender = optionsMonitor.CurrentValue.Sender;
        message.From.Add(MailboxAddress.Parse(sender));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = subject;

        // If messageId not provided generate one.
        message.MessageId = messageId ?? SequentialGuidGenerator.Instance.NewGuid().ToString();
        message.Body = new TextPart("html") { Text = htmlMessage };

        return message;
    }
}