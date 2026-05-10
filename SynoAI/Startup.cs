using Microsoft.OpenApi;
using SynoAI.Hubs;
using SynoAI.Services;
using SynoAI.Settings;
using System.Net.Security;

namespace SynoAI
{
    public class Startup
    {
        private readonly IConfiguration _configuration;

        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            // Strongly-typed settings (IOptions<AppSettings> available throughout the app)
            services.Configure<AppSettings>(_configuration);

            // Named HttpClient for Synology API — pooled handler avoids socket exhaustion.
            // Handler is created lazily so Config.AllowInsecureUrl is populated by then.
            services.AddHttpClient("Synology").ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                    UseCookies = false
                };
                if (Config.AllowInsecureUrl)
                {
                    handler.SslOptions = new SslClientAuthenticationOptions
                    {
                        RemoteCertificateValidationCallback = (_, _, _, _) => true
                    };
                }
                return handler;
            });

            // Singleton so SynoAIStartupService (IHostedService) can inject it without scope issues
            services.AddSingleton<ISynologyService, SynologyService>();
            services.AddScoped<IAIService, AIService>();

            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "SynoAI", Version = "v1" });
            });

            services.AddRazorPages();
            services.AddSignalR();

            // Background services
            services.AddHostedService<SynoAIStartupService>();
            services.AddHostedService<CaptureCleanupService>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IConfiguration configuration, ILogger<Startup> logger)
        {
            Config.Generate(logger, configuration);

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SynoAI v1"));
            }

            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHub<SynoAIHub>("/synoaiHub");
                endpoints.MapControllers();
            });
        }
    }
}
