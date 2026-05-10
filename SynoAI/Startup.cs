using Microsoft.OpenApi;
using SynoAI.Hubs;
using SynoAI.Services;
using SynoAI.Settings;

namespace SynoAI
{
    /// <summary>
    /// Configures the services for the application.
    /// </summary>
    public class Startup
    {
        /// <summary>
        /// Configures the services for the application.
        /// </summary>
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddScoped<IAIService, AIService>();
            services.AddScoped<ISynologyService, SynologyService>();

            // #15: Replace blocking lifetime callbacks with proper hosted services
            services.AddHostedService<SynoAIStartupService>();
            services.AddHostedService<CaptureCleanupService>();

            // #38/#3: Register IHttpClientFactory so SynologyService can avoid socket exhaustion
            services.AddHttpClient();

            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "SynoAI", Version = "v1" });
            });

            services.AddRazorPages();

            // euquiq: Needed for realtime update from each camera valid snapshot into client's web browser
            services.AddSignalR();
        }

        /// <summary>
        /// Configures the application.
        /// </summary>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IConfiguration configuration, ILogger<Startup> logger)
        {
            // Config.Generate() MUST be called here (before hosted services start) because static
            // helpers (SnapshotManager, NotifierBase, Camera) and the hosted services all read
            // from Config.*. The IOptions<AppSettings> registration makes settings available via
            // DI for future migration away from the static Config class (#5).
            Config.Generate(logger, configuration);

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SynoAI v1"));
            }

            // euquiq: Allows /wwwroot's static files (mainly our Javascript code for RT monitoring the cameras)
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                // euquiq: Used by SignalR to contact each online client with snapshot updates
                endpoints.MapHub<SynoAIHub>("/synoaiHub");

                // euquiq: Web interface mapped inside HomeController.cs
                endpoints.MapControllers();
            });

            // NOTE: The ApplicationStarted / ApplicationStopping lifetime callbacks have been
            // replaced by SynoAIStartupService and CaptureCleanupService (fixes #15 / #28).
        }
    }
}
