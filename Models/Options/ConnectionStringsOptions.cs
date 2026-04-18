namespace BackgroundEmailSenderSample.Models.Options;

/// <summary>
/// Holds named connection-string values used by the application.
/// </summary>
/// <remarks>
/// This options type is intended to be bound from configuration (for example, from the
/// <c>ConnectionStrings</c> configuration section). It centralizes connection-string values
/// so they can be injected using the options pattern (<c>IOptions{ConnectionStringsOptions}</c>).
/// 
/// Important security note: connection strings often contain sensitive information (passwords,
/// secrets). Avoid committing them to source control. Prefer secure sources such as environment
/// variables, the Secret Manager during development, or a secret store like Azure Key Vault.
/// </remarks>
/// <example>
/// <code language="csharp">
/// // Bind using the options pattern in Program.cs / Startup:
/// builder.Services.Configure&lt;ConnectionStringsOptions&gt;(builder.Configuration.GetSection("ConnectionStrings"));
///
/// // Retrieve via IOptions&lt;ConnectionStringsOptions&gt; in a service:
/// public class MyService
/// {
///     private readonly string _defaultConnection;
///     public MyService(IOptions&lt;ConnectionStringsOptions&gt; options)
///     {
///         _defaultConnection = options.Value.Default;
///     }
/// }
///
/// // Or get the named connection directly:
/// var defaultCs = builder.Configuration.GetConnectionString("Default");
/// </code>
/// </example>
public class ConnectionStringsOptions
{
    /// <summary>
    /// Gets or sets the default connection string used by the application.
    /// </summary>
    /// <value>
    /// A full ADO.NET/EF-style connection string (for example, "Server=...;Database=...;User Id=...;Password=...;")
    /// or another provider-specific connection string. May also be <see langword="null" /> or empty if not configured.
    /// </value>
    /// <remarks>
    /// Use this value to configure database contexts or other connection-requiring services.
    /// If the connection string contains credentials, retrieve it from a secure configuration source
    /// at runtime rather than embedding it in source code or checked-in configuration files.
    /// </remarks>
    public string Default { get; set; }
}