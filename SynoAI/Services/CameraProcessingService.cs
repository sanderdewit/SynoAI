using Microsoft.Extensions.Options;
using SkiaSharp;
using SynoAI.Models;
using SynoAI.Notifiers;
using SynoAI.Settings;
using System.Diagnostics;

namespace SynoAI.Services
{
    internal sealed class CameraProcessingService : ICameraProcessingService
    {
        private readonly IAIService _aiService;
        private readonly ISynologyService _synologyService;
        private readonly SnapshotManager _snapshotManager;
        private readonly IReadOnlyList<INotifier> _notifiers;
        private readonly IOptionsMonitor<AppSettings> _options;
        private readonly ILogger<CameraProcessingService> _logger;

        public CameraProcessingService(
            IAIService aiService,
            ISynologyService synologyService,
            SnapshotManager snapshotManager,
            IReadOnlyList<INotifier> notifiers,
            IOptionsMonitor<AppSettings> options,
            ILogger<CameraProcessingService> logger)
        {
            _aiService = aiService;
            _synologyService = synologyService;
            _snapshotManager = snapshotManager;
            _notifiers = notifiers;
            _options = options;
            _logger = logger;
        }

        private AppSettings Settings => _options.CurrentValue;

        public async Task<bool> ProcessAsync(string id, Camera camera)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int snapshotCount = 1; snapshotCount <= Settings.MaxSnapshots; snapshotCount++)
            {
                _logger.LogInformation("{Id}: Snapshot {Count} of {Max} requested at {Ms}ms.", id, snapshotCount, Settings.MaxSnapshots, stopwatch.ElapsedMilliseconds);

                byte[]? snapshot = await GetSnapshotAsync(id);
                if (snapshot == null)
                    continue;

                _logger.LogInformation("{Id}: Snapshot {Count} of {Max} received at {Ms}ms.", id, snapshotCount, Settings.MaxSnapshots, stopwatch.ElapsedMilliseconds);

                (byte[] processedBytes, SKBitmap? processedBitmap) = PreProcessSnapshot(camera, snapshot);

                List<AIPrediction> predictions = await GetAIPredictionsAsync(camera, processedBytes) ?? new();

                _logger.LogInformation("{Id}: Snapshot {Count} of {Max} contains {PredCount} objects at {Ms}ms.", id, snapshotCount, Settings.MaxSnapshots, predictions.Count, stopwatch.ElapsedMilliseconds);

                List<AIPrediction> validPredictions = FilterPredictions(id, camera, predictions, stopwatch);

                if (Settings.SaveOriginalSnapshot == SaveSnapshotMode.Always ||
                    (Settings.SaveOriginalSnapshot == SaveSnapshotMode.WithPredictions && predictions.Count > 0) ||
                    (Settings.SaveOriginalSnapshot == SaveSnapshotMode.WithValidPredictions && validPredictions.Count > 0))
                {
                    _logger.LogInformation("{Id}: Saving original image", id);
                    _snapshotManager.SaveOriginalImage(_logger, camera, processedBytes);
                }

                if (validPredictions.Count > 0)
                {
                    ProcessedImage processedImage = _snapshotManager.DressImage(camera, processedBytes, predictions, validPredictions, _logger, processedBitmap);

                    Notification notification = new()
                    {
                        ProcessedImage = processedImage,
                        ValidPredictions = validPredictions,
                        Settings = Settings
                    };

                    await SendNotificationsAsync(camera, notification);

                    _logger.LogInformation("{Id}: Valid object found in snapshot {Count} of {Max} at {Ms}ms.", id, snapshotCount, Settings.MaxSnapshots, stopwatch.ElapsedMilliseconds);
                    return true;
                }

                if (predictions.Count > 0)
                    _logger.LogInformation("{Id}: No valid objects at {Ms}ms.", id, stopwatch.ElapsedMilliseconds);
                else
                    _logger.LogInformation("{Id}: Nothing detected by the AI at {Ms}ms.", id, stopwatch.ElapsedMilliseconds);

                _logger.LogInformation("{Id}: Finished ({Ms}ms).", id, stopwatch.ElapsedMilliseconds);
            }

            return false;
        }

        private List<AIPrediction> FilterPredictions(string id, Camera camera, List<AIPrediction> predictions, Stopwatch stopwatch)
        {
            int minSizeX = camera.GetMinSizeX(Settings);
            int minSizeY = camera.GetMinSizeY(Settings);
            int maxSizeX = camera.GetMaxSizeX(Settings);
            int maxSizeY = camera.GetMaxSizeY(Settings);

            List<AIPrediction> valid = new();
            foreach (AIPrediction prediction in predictions)
            {
                if (!PredictionFilter.IsTypeOfInterest(camera, prediction))
                {
                    _logger.LogDebug("{Id}: Ignored '{Label}' as it's not in the valid type list at {Ms}ms.", id, prediction.Label, stopwatch.ElapsedMilliseconds);
                }
                else if (!PredictionFilter.MeetsMinimumSize(prediction, minSizeX, minSizeY))
                {
                    _logger.LogDebug("{Id}: Ignored '{Label}' as it's under the minimum size ({MinX}x{MinY}) at {Ms}ms.", id, prediction.Label, minSizeX, minSizeY, stopwatch.ElapsedMilliseconds);
                }
                else if (!PredictionFilter.MeetsMaximumSize(prediction, maxSizeX, maxSizeY))
                {
                    _logger.LogDebug("{Id}: Ignored '{Label}' as it exceeds the maximum size ({MaxX}x{MaxY}) at {Ms}ms.", id, prediction.Label, maxSizeX, maxSizeY, stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    Zone? exclusionZone = PredictionFilter.FindExclusionZone(camera.Exclusions, prediction);
                    if (exclusionZone != null)
                    {
                        _logger.LogDebug("{Id}: Ignored '{Label}' as it fell within an exclusion zone (mode '{Mode}') at {Ms}ms.",
                            id, prediction.Label, exclusionZone.Mode, stopwatch.ElapsedMilliseconds);
                    }
                    else
                    {
                        valid.Add(prediction);
                        _logger.LogDebug("{Id}: Valid prediction '{Label}' at {Ms}ms.", id, prediction.Label, stopwatch.ElapsedMilliseconds);
                    }
                }
            }
            return valid;
        }

        private (byte[] bytes, SKBitmap? bitmap) PreProcessSnapshot(Camera camera, byte[] snapshot)
        {
            if (camera.Rotate == 0)
                return (snapshot, null);

            Stopwatch stopwatch = Stopwatch.StartNew();
            SKBitmap bitmap = SKBitmap.Decode(snapshot) ?? throw new InvalidOperationException($"{camera.Name}: Failed to decode snapshot for rotation.");

            _logger.LogInformation("{CameraName}: Rotating image {Degrees} degrees.", camera.Name, camera.Rotate);
            bitmap = Rotate(bitmap, camera.Rotate);

            using SKPixmap pixmap = bitmap.PeekPixels() ?? throw new InvalidOperationException($"{camera.Name}: Failed to get pixel data from rotated bitmap.");
            using SKData data = pixmap.Encode(SKEncodedImageFormat.Jpeg, 100) ?? throw new InvalidOperationException($"{camera.Name}: Failed to encode rotated image.");
            _logger.LogInformation("{CameraName}: Image preprocessing complete ({Ms}ms).", camera.Name, stopwatch.ElapsedMilliseconds);

            return (data.ToArray(), bitmap);
        }

        private static SKBitmap Rotate(SKBitmap bitmap, double angle)
        {
            double radians = Math.PI * angle / 180;
            float sine = (float)Math.Abs(Math.Sin(radians));
            float cosine = (float)Math.Abs(Math.Cos(radians));
            int originalWidth = bitmap.Width;
            int originalHeight = bitmap.Height;
            int rotatedWidth = (int)(cosine * originalWidth + sine * originalHeight);
            int rotatedHeight = (int)(cosine * originalHeight + sine * originalWidth);

            SKBitmap rotatedBitmap = new(rotatedWidth, rotatedHeight);
            using (SKCanvas canvas = new(rotatedBitmap))
            {
                canvas.Clear();
                canvas.Translate(rotatedWidth / 2, rotatedHeight / 2);
                canvas.RotateDegrees((float)angle);
                canvas.Translate(-originalWidth / 2, -originalHeight / 2);
                canvas.DrawBitmap(bitmap, new SKPoint(), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            }
            return rotatedBitmap;
        }

        private async Task<byte[]?> GetSnapshotAsync(string cameraName)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            byte[]? imageBytes = await _synologyService.TakeSnapshotAsync(cameraName);
            stopwatch.Stop();

            if (imageBytes == null)
                _logger.LogError("{CameraName}: Failed to get snapshot.", cameraName);
            else
                _logger.LogInformation("{CameraName}: Snapshot received in {Ms}ms.", cameraName, stopwatch.ElapsedMilliseconds);

            return imageBytes;
        }

        private async Task<List<AIPrediction>?> GetAIPredictionsAsync(Camera camera, byte[] imageBytes)
        {
            IEnumerable<AIPrediction>? rawPredictions = await _aiService.ProcessAsync(camera, imageBytes);
            if (rawPredictions == null)
            {
                _logger.LogError("{CameraName}: Failed to get predictions.", camera.Name);
                return null;
            }

            List<AIPrediction> predictions = rawPredictions.ToList();
            foreach (AIPrediction prediction in predictions)
            {
                _logger.LogInformation("AI Detected '{Camera}': {Label} ({Confidence}%) [Size: {SizeX}x{SizeY}] [Start: {MinX},{MinY} | End: {MaxX},{MaxY}]",
                    camera.Name, prediction.Label, prediction.Confidence,
                    prediction.SizeX, prediction.SizeY,
                    prediction.MinX, prediction.MinY, prediction.MaxX, prediction.MaxY);
            }

            return predictions;
        }

        private async Task SendNotificationsAsync(Camera camera, Notification notification)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            IEnumerable<string> labels = notification.ValidPredictions.Select(x => x.Label).Distinct().ToList();

            IEnumerable<INotifier> notifiers = _notifiers
                .Where(x =>
                    (x.Cameras == null || !x.Cameras.Any() || x.Cameras.Any(c => c.Equals(camera.Name, StringComparison.OrdinalIgnoreCase))) &&
                    (x.Types == null || !x.Types.Any() || x.Types.Any(t => labels.Contains(t, StringComparer.OrdinalIgnoreCase)))
                ).ToList();

            await Task.WhenAll(notifiers.Select(n => n.SendAsync(camera, notification, _logger)));

            stopwatch.Stop();
            _logger.LogInformation("{CameraName}: Notifications sent ({Ms}ms).", camera.Name, stopwatch.ElapsedMilliseconds);
        }
    }
}
