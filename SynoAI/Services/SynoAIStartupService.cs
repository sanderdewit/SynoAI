namespace SynoAI.Services
{
    /// <summary>
    /// Hosted service responsible for initialising and tearing down application-level
    /// resources (Synology session, notifier connections). Replaces the blocking
    /// <c>IHostApplicationLifetime.ApplicationStarted.Register(…)</c> / <c>ApplicationStopping</c>
    /// callbacks that previously blocked a thread-pool thread with <c>.Wait()</c> (fixes #15).
    /// </summary>
    internal sealed class SynoAIStartupService : IHostedService
    {
        private readonly ISynologyService _synologyService;
        private readonly ILogger<SynoAIStartupService> _logger;

        public SynoAIStartupService(ISynologyService synologyService, ILogger<SynoAIStartupService> logger)
        {
            _synologyService = synologyService;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _synologyService.InitialiseAsync();
            await Task.WhenAll(Config.Notifiers.Select(n => n.InitializeAsync(_logger)));
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await Task.WhenAll(Config.Notifiers.Select(n => n.CleanupAsync(_logger)));
        }
    }
}
