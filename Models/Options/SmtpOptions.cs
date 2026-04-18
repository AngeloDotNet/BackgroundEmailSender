namespace BackgroundEmailSenderSample.Models.Options;

/// <summary>
/// Configuration options for connecting to an SMTP server used to send email messages.
/// </summary>
/// <remarks>
/// This class is typically bound from configuration (for example, using <c>IConfiguration.GetSection("Smtp")</c>)
/// and supplied to services that perform email sending. It contains network and authentication
/// settings required by SMTP clients such as MailKit's <c>SmtpClient</c>.
/// 
/// Important security note: avoid storing plaintext credentials in source control. Prefer secure
/// stores such as environment variables, Azure Key Vault, or user-secrets during development.
/// </remarks>
/// <example>
/// <code>
/// var options = new SmtpOptions
/// {
///     Host = "smtp.example.com",
///     Port = 587,
///     Security = MailKit.Security.SecureSocketOptions.StartTls,
///     Username = "service-account@example.com",
///     Password = "<retrieve-from-secure-store>",
///     Sender = "no-reply@example.com",
///     MaxSenderCount = 4,
///     DelayOnError = TimeSpan.FromSeconds(30)
/// };
/// </code>
/// </example>
public class SmtpOptions
{
    /// <summary>
    /// Gets or sets the SMTP server host name or IP address.
    /// </summary>
    /// <value>
    /// Typical values are host names like <c>smtp.example.com</c> or an IP address.
    /// </value>
    public string Host { get; set; }

    /// <summary>
    /// Gets or sets the network port used to connect to the SMTP server.
    /// </summary>
    /// <value>
    /// Common ports are <c>25</c> (plain SMTP), <c>465</c> (implicit TLS), and <c>587</c> (submission with STARTTLS).
    /// </value>
    public int Port { get; set; }

    /// <summary>
    /// Gets or sets the TLS/SSL negotiation mode to use when connecting to the SMTP server.
    /// </summary>
    /// <remarks>
    /// This property typically maps to <see cref="MailKit.Security.SecureSocketOptions"/> when using MailKit.
    /// Choose a value appropriate for your server (for example, <c>StartTls</c> for submission on port 587).
    /// </remarks>
    public SecureSocketOptions Security { get; set; }

    /// <summary>
    /// Gets or sets the username used to authenticate with the SMTP server.
    /// </summary>
    /// <remarks>
    /// Some SMTP servers permit anonymous or IP-restricted sending; in those cases this value may be <see langword="null" /> or empty.
    /// If credentials are required, use a dedicated service account rather than a personal mailbox.
    /// </remarks>
    public string Username { get; set; }

    /// <summary>
    /// Gets or sets the password used to authenticate with the SMTP server.
    /// </summary>
    /// <remarks>
    /// This is sensitive information. Retrieve it from a secure configuration source at runtime
    /// (for example, secret manager, environment variable, or a vault). Do not hard-code passwords
    /// in source code or commit them to version control.
    /// </remarks>
    public string Password { get; set; }

    /// <summary>
    /// Gets or sets the default sender (From) address used when sending messages.
    /// </summary>
    /// <value>
    /// A valid email address such as <c>no-reply@example.com</c>. Mail sending code should validate the format
    /// and may override this value per-message when needed.
    /// </value>
    public string Sender { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of concurrent send operations allowed.
    /// </summary>
    /// <remarks>
    /// Use this to limit parallel connections or message throughput to the SMTP server to avoid throttling.
    /// A value of <c>1</c> forces sequential sending; larger values allow more concurrency depending on server policy.
    /// </remarks>
    public int MaxSenderCount { get; set; }

    /// <summary>
    /// Gets or sets the delay to wait before retrying after an error during sending.
    /// </summary>
    /// <remarks>
    /// When a transient or recoverable error occurs, the sending logic can wait for <see cref="DelayOnError"/>
    /// before attempting a retry. Use an appropriate backoff value to avoid rapid retry loops.
    /// </remarks>
    public TimeSpan DelayOnError { get; set; }
}