using Microsoft.Extensions.Options;
using SynoAI.Settings;

namespace SynoAI.Services
{
    internal sealed class CaptureCleanupService : BackgroundService
    {
        private readonly ILogger<CaptureCleanupService> _logger;
        private readonly IOptionsMonitor<AppSettings> _options;

        public CaptureCleanupService(ILogger<CaptureCleanupService> logger, IOptionsMonitor<AppSettings> options)
        {
            _logger = logger;
            _options = options;
        }

        private AppSettings Settings => _options.CurrentValue;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using PeriodicTimer timer = new(TimeSpan.FromHours(1));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                CleanupOldImages();
            }
        }

        private void CleanupOldImages()
        {
            if (Settings.DaysToKeepCaptures <= 0)
                return;
            if (!Directory.Exists(Constants.DIRECTORY_CAPTURES))
                return;

            _logger.LogInformation("Captures Clean Up: Cleaning up images older than {Days} day(s).", Settings.DaysToKeepCaptures);
            try
            {
                DirectoryInfo directory = new(Constants.DIRECTORY_CAPTURES);
                foreach (FileInfo file in directory.GetFiles("*", new EnumerationOptions { RecurseSubdirectories = true }))
                {
                    double age = (DateTime.Now - file.CreationTime).TotalDays;
                    if (age > Settings.DaysToKeepCaptures)
                    {
                        _logger.LogInformation("Captures Clean Up: Deleting '{FullName}' ({Age:F1} days old).", file.FullName, age);
                        file.Delete();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Captures Clean Up Failed");
            }
        }
    }
}
