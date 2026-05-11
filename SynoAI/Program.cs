using Microsoft.OpenApi;
using SynoAI;
using SynoAI.Hubs;
using SynoAI.Services;
using SynoAI.Settings;
using System.Net.Security;

var builder = WebApplication.CreateBuilder(args);

// Strongly-typed settings (IOptions<AppSettings> available throughout the app)
builder.Services.Configure<AppSettings>(builder.Configuration);

// Named HttpClient for AI calls (DeepStack / CodeProject.AI).
builder.Services.AddHttpClient("AI");

// Named HttpClient for Synology API — pooled handler avoids socket exhaustion.
// Handler is created lazily so Config.AllowInsecureUrl is populated by then.
builder.Services.AddHttpClient("Synology").ConfigurePrimaryHttpMessageHandler(() =>
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
builder.Services.AddSingleton<ISynologyService, SynologyService>();
builder.Services.AddScoped<IAIService, AIService>();
builder.Services.AddScoped<ICameraProcessingService, CameraProcessingService>();

builder.Services.AddControllers();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SynoAI", Version = "v1" });
});

builder.Services.AddRazorPages();
builder.Services.AddSignalR();

// Background services
builder.Services.AddHostedService<SynoAIStartupService>();
builder.Services.AddHostedService<CaptureCleanupService>();

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
Config.Generate(logger, builder.Configuration);

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SynoAI v1"));
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapHub<SynoAIHub>("/synoaiHub");
app.MapControllers();

app.Run();
