using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BackgroundEmailSenderSample.Models.Options;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using background_email_sender_master.Models.Services.Infrastructure;
using MimeKit;
using System.Data;
using Background_Email_Sender.Models.Enums;

namespace BackgroundEmailSenderSample.HostedServices
{
    public class EmailSenderHostedService : IEmailSender, IHostedService, IDisposable
    {
        private readonly BufferBlock<MimeMessage> mailMessages;
        private readonly ILogger logger;
        private Task sendTask;
        private CancellationTokenSource cancellationTokenSource;
        private readonly IOptionsMonitor<SmtpOptions> optionsMonitor;
        private readonly IDatabaseAccessor db;
        
        public EmailSenderHostedService(IConfiguration configuration, IDatabaseAccessor db, IOptionsMonitor<SmtpOptions> optionsMonitor, ILogger<EmailSenderHostedService> logger)
        {
            this.optionsMonitor = optionsMonitor;
            this.logger = logger;
            this.mailMessages = new BufferBlock<MimeMessage>();
            this.db = db;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            var messageId = Guid.NewGuid().ToString();
            var message = CreateMessage(email, subject, htmlMessage, messageId);

            int affectedRows = await db.CommandAsync($@"INSERT INTO EmailMessages (Id, Recipient, Subject, Message, SenderCount, Status) 
                                                        VALUES ({message.MessageId}, {email}, {subject}, {htmlMessage}, 0, {nameof(MailStatus.InProgress)})");

            if (affectedRows != 1)
            {
                throw new InvalidOperationException("Could not persist email message");
            }

            await this.mailMessages.SendAsync(message);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Starting background e-mail delivery");

            FormattableString query = $@"SELECT Id, Recipient, Subject, Message FROM EmailMessages WHERE Status<>{nameof(MailStatus.Sended)} AND Status<>{nameof(MailStatus.Deleted)}";
            DataSet dataSet = await db.QueryAsync(query);

            foreach (DataRow row in dataSet.Tables[0].Rows)
            {
                var message = CreateMessage(Convert.ToString(row["Email"]), 
                                            Convert.ToString(row["Subject"]), 
                                            Convert.ToString(row["Message"]), 
                                            Convert.ToString(row["Id"]));

                await this.mailMessages.SendAsync(message);
            }

            cancellationTokenSource = new CancellationTokenSource();
            sendTask = DeliverAsync(cancellationTokenSource.Token);
            
            await Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            CancelSendTask();
            await Task.WhenAny(sendTask, Task.Delay(Timeout.Infinite, cancellationToken));
        }

        private void CancelSendTask()
        {
            try
            {
                if (cancellationTokenSource != null)
                {
                    logger.LogInformation("Stopping e-mail background delivery");
                    cancellationTokenSource.Cancel();
                    cancellationTokenSource = null;
                }
            }
            catch
            {

            }
        }

        public async Task DeliverAsync(CancellationToken token)
        {
            logger.LogInformation("E-mail background delivery started");
            while (!token.IsCancellationRequested)
            {
                MimeMessage message = null;
                try
                {
                    message = await mailMessages.ReceiveAsync(token);

                    var options = this.optionsMonitor.CurrentValue;
                    using var client = new SmtpClient();

                    await client.ConnectAsync(options.Host, options.Port, options.Security);
                    if (!string.IsNullOrEmpty(options.Username))
                    {
                        await client.AuthenticateAsync(options.Username, options.Password);
                    }

                    await client.SendAsync(message);
                    await client.DisconnectAsync(true);

                    await db.CommandAsync($"UPDATE EmailMessages SET Status={nameof(MailStatus.Sended)} WHERE Id={message.MessageId}");
                    logger.LogInformation($"E-mail sent successfully to {message.To}");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exc)
                {
                    logger.LogError(exc, "Couldn't send an e-mail to {recipient}", message.To[0]);

                    FormattableString query = $@"SELECT SenderCount FROM EmailMessages WHERE Id={message.MessageId}";
                    DataSet dataSet = await db.QueryAsync(query);

                    var MessageTable = dataSet.Tables[0];
                    int counter = 0;

                    if (MessageTable.Rows.Count == 1)
                    {
                        var messageRow = MessageTable.Rows[0];
                        counter = Convert.ToInt32(messageRow["SenderCount"]);

                        if (counter == 25)
                        {
                            //Il contatore dei tentativi di invio ha raggiunto la quota di 25, imposto lo stato di Deleted alla mail, in modo che non venga riaccodata per nuovi tentativi.
                            await db.CommandAsync($"UPDATE EmailMessages SET Status={nameof(MailStatus.Deleted)} WHERE Id={message.MessageId}");
                        }
                        else
                        {
                            //Aggiungo +1 al contatore dei tentativi di invio
                            counter = counter + 1;
                            await db.CommandAsync($"UPDATE EmailMessages SET SenderCount={counter} WHERE Id={message.MessageId}");
                        }
                    }

                    //Modificato il tempo di delay a 10 secondi
                    await Task.Delay(10000); 
                    await mailMessages.SendAsync(message);
                }
            }

            logger.LogInformation("E-mail background delivery stopped");
        }

        public void Dispose()
        {
            CancelSendTask();
        }

        private MimeMessage CreateMessage(string email, string subject, string htmlMessage, string messageId = null)
        {
            var message = new MimeMessage();
            
            message.From.Add(MailboxAddress.Parse(optionsMonitor.CurrentValue.Sender));
            message.To.Add(MailboxAddress.Parse(email));
            message.Subject = subject;
            
            // Se il messageId non è stato fornito, allora lo genero.
            message.MessageId = messageId ?? Guid.NewGuid().ToString();
            message.Body = new TextPart("html") { Text = htmlMessage };

            return message;
        }
    }
}