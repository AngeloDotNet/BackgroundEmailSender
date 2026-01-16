namespace BackgroundEmailSenderSample;

public class Startup(IConfiguration configuration)
{
    public IConfiguration Configuration { get; } = configuration;

    public void ConfigureServices(IServiceCollection services)
    {
        //services.AddMvc();
        //services.AddControllers();
        services.AddControllersWithViews();

        services.AddSingleton<EmailSenderHostedService>();
        services.AddSingleton<IHostedService>(serviceProvider => serviceProvider.GetService<EmailSenderHostedService>());
        services.AddSingleton<IEmailSender>(serviceProvider => serviceProvider.GetService<EmailSenderHostedService>());

        services.AddTransient<IDatabaseAccessor, SqliteDatabaseAccessor>();

        services.Configure<ConnectionStringsOptions>(Configuration.GetSection("ConnectionStrings"));
        services.Configure<SmtpOptions>(Configuration.GetSection("Smtp"));
    }

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
    }
}