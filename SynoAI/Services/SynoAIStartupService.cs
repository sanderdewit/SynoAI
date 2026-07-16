using Microsoft.Extensions.Options;
using SynoAI.Notifiers;
using SynoAI.Settings;

namespace SynoAI.Services
{
    internal sealed class SynoAIStartupService : IHostedService
    {
        private readonly ISynologyService _synologyService;
        private readonly IReadOnlyList<INotifier> _notifiers;
        private readonly IOptionsMonitor<AppSettings> _options;
        private readonly ILogger<SynoAIStartupService> _logger;

        private IDisposable? _onSettingsChanged;

        public SynoAIStartupService(
            ISynologyService synologyService,
            IReadOnlyList<INotifier> notifiers,
            IOptionsMonitor<AppSettings> options,
            ILogger<SynoAIStartupService> logger)
        {
            _synologyService = synologyService;
            _notifiers = notifiers;
            _options = options;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (_notifiers.Count == 0)
                _logger.LogWarning("No notifications were specified in the config; detections will not be sent anywhere.");

            // When configuration is reloaded (e.g. saved from the settings UI): refresh each notifier's
            // settings and re-initialise the Synology connection so credential/URL/camera changes take
            // effect without a restart. Adding/removing notifier definitions still needs a restart.
            _onSettingsChanged = _options.OnChange(updated =>
            {
                foreach (INotifier notifier in _notifiers)
                    notifier.Settings = updated;

                _logger.LogInformation("Configuration changed; re-initialising the Synology connection.");
                _ = _synologyService.InitialiseAsync();
            });

            await _synologyService.InitialiseAsync();
            await Task.WhenAll(_notifiers.Select(n => n.InitializeAsync(_logger)));
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _onSettingsChanged?.Dispose();
            await Task.WhenAll(_notifiers.Select(n => n.CleanupAsync(_logger)));
        }
    }
}
