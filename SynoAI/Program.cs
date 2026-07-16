using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using SynoAI.Controllers;
using SynoAI.Notifiers;
using SynoAI.Services;
using SynoAI.Settings;
using System.Net.Security;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Writable, reloadable settings layer, laid on top of appsettings.json. ISettingsStore writes to this
// file; reloadOnChange means saved changes flow through to IOptionsMonitor<AppSettings> consumers live.
// The path is configurable via "SynoAI:SettingsPath" (default: synoai.settings.json in the content root),
// so it can point at a mounted data directory.
string settingsPath = JsonSettingsStore.ResolvePath(builder.Configuration, builder.Environment.ContentRootPath);
string settingsDirectory = Path.GetDirectoryName(settingsPath) ?? builder.Environment.ContentRootPath;
Directory.CreateDirectory(settingsDirectory);
builder.Configuration.AddJsonFile(new PhysicalFileProvider(settingsDirectory), Path.GetFileName(settingsPath), optional: true, reloadOnChange: true);

// Strongly-typed settings, bound from configuration and validated at startup. Consumed via
// IOptionsMonitor<AppSettings> (for live reload). Replaces the former static Config class.
builder.Services.AddOptions<AppSettings>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<ISettingsStore>(sp =>
    new JsonSettingsStore(sp.GetRequiredService<IOptionsMonitor<AppSettings>>(), settingsPath));

// The configured notifiers, built once from the "Notifiers" section.
builder.Services.AddSingleton<IReadOnlyList<INotifier>>(sp => NotifierBuilder.Build(
    sp.GetRequiredService<ILoggerFactory>().CreateLogger("SynoAI.Notifiers"),
    builder.Configuration,
    sp.GetRequiredService<IOptionsMonitor<AppSettings>>().CurrentValue));

// Named HttpClient for AI calls (CodeProject.AI).
builder.Services.AddHttpClient("AI");

// Named HttpClient for Synology API — pooled handler avoids socket exhaustion.
builder.Services.AddHttpClient("Synology").ConfigurePrimaryHttpMessageHandler(sp =>
{
    var settings = sp.GetRequiredService<IOptionsMonitor<AppSettings>>().CurrentValue;
    var handler = new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        UseCookies = false
    };
    if (settings.AllowInsecureUrl)
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
builder.Services.AddSingleton<SnapshotManager>();
builder.Services.AddScoped<IAIService, AIService>();
builder.Services.AddScoped<ICameraProcessingService, CameraProcessingService>();

// PascalCase JSON so the API payloads match the C# property names and the settings-file keys, and string
// enums so values are human-readable and match the settings UI's option lists. Input binding stays
// case-insensitive.
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = null;
    o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SynoAI", Version = "v1" });

    // Surface the XML doc comments (GenerateDocumentationFile is enabled) in the Swagger UI.
    string xmlPath = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xmlPath))
        c.IncludeXmlComments(xmlPath);

    // Let Swagger UI send the admin API key (via the Authorize button) for the guarded endpoints.
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Name = AdminApiKeyAttribute.HeaderName,
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Admin API key (AppSettings.AdminApiKey)."
    });
});

// Background services
builder.Services.AddHostedService<SynoAIStartupService>();
builder.Services.AddHostedService<CaptureCleanupService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SynoAI v1"));
}

// Serve the settings UI from wwwroot (index.html at "/").
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthorization();

app.MapControllers();

app.Run();
