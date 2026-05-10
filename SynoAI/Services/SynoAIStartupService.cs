namespace SynoAI.Services
{
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
