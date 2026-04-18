namespace BackgroundEmailSenderSample;

/// <summary>
/// Application startup configuration.
/// </summary>
public class Startup(IConfiguration configuration)

{
    /// <summary>
    /// The application configuration.
    /// </summary>
    public IConfiguration Configuration { get; } = configuration ?? throw new ArgumentNullException(nameof(configuration));

    /// <summary>
    /// Register services required by the application.
    /// </summary>
    public void ConfigureServices(IServiceCollection services)

    {
        // MVC + Razor Pages support (Razor Pages project - keep both to support controllers if present)
        services.AddControllersWithViews();
        services.AddRazorPages();

        // Register the hosted service as a concrete singleton and expose the same instance
        // as both IHostedService and IEmailSender to ensure a single instance is used.
        services.AddSingleton<EmailSenderHostedService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<EmailSenderHostedService>());
        services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<EmailSenderHostedService>());

        // Database accessor - transient by design
        services.AddTransient<IDatabaseAccessor, SqliteDatabaseAccessor>();

        // Configure named options from configuration
        services.Configure<ConnectionStringsOptions>(Configuration.GetSection("ConnectionStrings"));
        services.Configure<SmtpOptions>(Configuration.GetSection("Smtp"));
    }

    /// <summary>
    /// Configure the HTTP request pipeline.
    /// </summary>
    public void Configure(WebApplication app)

    {
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseStaticFiles();
        app.UseRouting();

        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        app.MapRazorPages();
    }
}
