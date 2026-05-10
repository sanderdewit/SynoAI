namespace SynoAI.Services
{
    /// <summary>
    /// Background service that periodically deletes captured images older than
    /// <see cref="Config.DaysToKeepCaptures"/> days. Runs once per hour.
    /// Replaces the per-webhook cleanup that was previously triggered in
    /// <see cref="SynoAI.Controllers.CameraController"/> (fixes #28 / #8).
    /// </summary>
    internal sealed class CaptureCleanupService : BackgroundService
    {
        private readonly ILogger<CaptureCleanupService> _logger;

        public CaptureCleanupService(ILogger<CaptureCleanupService> logger)
        {
            _logger = logger;
        }

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
            if (Config.DaysToKeepCaptures <= 0)
                return;

            if (!Directory.Exists(Constants.DIRECTORY_CAPTURES))
                return;

            _logger.LogInformation("Captures Clean Up: Cleaning up images older than {days} day(s).", Config.DaysToKeepCaptures);
            try
            {
                DirectoryInfo directory = new(Constants.DIRECTORY_CAPTURES);
                foreach (FileInfo file in directory.GetFiles("*", new EnumerationOptions { RecurseSubdirectories = true }))
                {
                    double age = (DateTime.Now - file.CreationTime).TotalDays;
                    if (age > Config.DaysToKeepCaptures)
                    {
                        _logger.LogInformation("Captures Clean Up: Deleting {file} ({age:F1} days old).", file.FullName, age);
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
