namespace BackgroundEmailSenderSample;

public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddMvc();
        services.AddSingleton<EmailSenderHostedService>();
        services.AddSingleton<IHostedService>(serviceProvider => serviceProvider.GetService<EmailSenderHostedService>());
        services.AddSingleton<IEmailSender>(serviceProvider => serviceProvider.GetService<EmailSenderHostedService>());
        
        services.AddTransient<IDatabaseAccessor, SqliteDatabaseAccessor>();
        services.Configure<ConnectionStringsOptions>(Configuration.GetSection("ConnectionStrings"));
        services.Configure<SmtpOptions>(Configuration.GetSection("Smtp"));
    }
    
    public void Configure(WebApplication app)
    {
        IHostEnvironment env = app.Environment;

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Error");
        }
        app.UseStaticFiles();
        app.UseRouting();
        app.UseEndpoints(routeBuilder =>
        {
            routeBuilder.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        });
    }
}