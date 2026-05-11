using Microsoft.AspNetCore.SignalR;
using SkiaSharp;
using SynoAI.Hubs;
using SynoAI.Models;
using SynoAI.Notifiers;
using System.Diagnostics;

namespace SynoAI.Services
{
    internal sealed class CameraProcessingService : ICameraProcessingService
    {
        private readonly IAIService _aiService;
        private readonly ISynologyService _synologyService;
        private readonly IHubContext<SynoAIHub> _hubContext;
        private readonly ILogger<CameraProcessingService> _logger;

        public CameraProcessingService(
            IAIService aiService,
            ISynologyService synologyService,
            IHubContext<SynoAIHub> hubContext,
            ILogger<CameraProcessingService> logger)
        {
            _aiService = aiService;
            _synologyService = synologyService;
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task<bool> ProcessAsync(string id, Camera camera)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int snapshotCount = 1; snapshotCount <= Config.MaxSnapshots; snapshotCount++)
            {
                _logger.LogInformation("{Id}: Snapshot {Count} of {Max} requested at {Ms}ms.", id, snapshotCount, Config.MaxSnapshots, stopwatch.ElapsedMilliseconds);

                byte[]? snapshot = await GetSnapshotAsync(id);
                if (snapshot == null)
                    continue;

                _logger.LogInformation("{Id}: Snapshot {Count} of {Max} received at {Ms}ms.", id, snapshotCount, Config.MaxSnapshots, stopwatch.ElapsedMilliseconds);

                (byte[] processedBytes, SKBitmap? processedBitmap) = PreProcessSnapshot(camera, snapshot);

                List<AIPrediction> predictions = await GetAIPredictionsAsync(camera, processedBytes) ?? new();

                _logger.LogInformation("{Id}: Snapshot {Count} of {Max} contains {PredCount} objects at {Ms}ms.", id, snapshotCount, Config.MaxSnapshots, predictions.Count, stopwatch.ElapsedMilliseconds);

                List<AIPrediction> validPredictions = FilterPredictions(id, camera, predictions, stopwatch);

                if (Config.SaveOriginalSnapshot == SaveSnapshotMode.Always ||
                    (Config.SaveOriginalSnapshot == SaveSnapshotMode.WithPredictions && predictions.Count > 0) ||
                    (Config.SaveOriginalSnapshot == SaveSnapshotMode.WithValidPredictions && validPredictions.Count > 0))
                {
                    _logger.LogInformation("{Id}: Saving original image", id);
                    SnapshotManager.SaveOriginalImage(_logger, camera, processedBytes);
                }

                if (validPredictions.Count > 0)
                {
                    ProcessedImage processedImage = SnapshotManager.DressImage(camera, processedBytes, predictions, validPredictions, _logger, processedBitmap);

                    Notification notification = new()
                    {
                        ProcessedImage = processedImage,
                        ValidPredictions = validPredictions
                    };

                    await SendNotificationsAsync(camera, notification);
                    await _hubContext.Clients.All.SendAsync("ReceiveSnapshot", camera.Name, processedImage.FileName);

                    _logger.LogInformation("{Id}: Valid object found in snapshot {Count} of {Max} at {Ms}ms.", id, snapshotCount, Config.MaxSnapshots, stopwatch.ElapsedMilliseconds);
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
            int minSizeX = camera.GetMinSizeX();
            int minSizeY = camera.GetMinSizeY();
            int maxSizeX = camera.GetMaxSizeX();
            int maxSizeY = camera.GetMaxSizeY();

            List<AIPrediction> valid = new();
            foreach (AIPrediction prediction in predictions)
            {
                if (camera.Types != null && !camera.Types.Contains(prediction.Label, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("{Id}: Ignored '{Label}' as it's not in the valid type list at {Ms}ms.", id, prediction.Label, stopwatch.ElapsedMilliseconds);
                }
                else if (prediction.SizeX < minSizeX || prediction.SizeY < minSizeY)
                {
                    _logger.LogDebug("{Id}: Ignored '{Label}' as it's under the minimum size ({MinX}x{MinY}) at {Ms}ms.", id, prediction.Label, minSizeX, minSizeY, stopwatch.ElapsedMilliseconds);
                }
                else if ((maxSizeX < int.MaxValue && prediction.SizeX > maxSizeX) || (maxSizeY < int.MaxValue && prediction.SizeY > maxSizeY))
                {
                    _logger.LogDebug("{Id}: Ignored '{Label}' as it exceeds the maximum size ({MaxX}x{MaxY}) at {Ms}ms.", id, prediction.Label, maxSizeX, maxSizeY, stopwatch.ElapsedMilliseconds);
                }
                else if (ShouldIncludePrediction(id, camera, stopwatch, prediction))
                {
                    valid.Add(prediction);
                    _logger.LogDebug("{Id}: Valid prediction '{Label}' at {Ms}ms.", id, prediction.Label, stopwatch.ElapsedMilliseconds);
                }
            }
            return valid;
        }

        private bool ShouldIncludePrediction(string id, Camera camera, Stopwatch stopwatch, AIPrediction prediction)
        {
            if (camera.Exclusions == null || camera.Exclusions.Count == 0)
                return true;

            int predMinX = prediction.MinX;
            int predMinY = prediction.MinY;
            int predMaxX = prediction.MaxX;
            int predMaxY = prediction.MaxY;

            foreach (Zone exclusion in camera.Exclusions)
            {
                int startX = Math.Min(exclusion.Start.X, exclusion.End.X);
                int startY = Math.Min(exclusion.Start.Y, exclusion.End.Y);
                int endX = Math.Max(exclusion.Start.X, exclusion.End.X);
                int endY = Math.Max(exclusion.Start.Y, exclusion.End.Y);

                bool exclude = exclusion.Mode == OverlapMode.Contains
                    ? startX <= predMinX && startY <= predMinY && endX >= predMaxX && endY >= predMaxY
                    : predMinX < endX && predMaxX > startX && predMinY < endY && predMaxY > startY;

                if (exclude)
                {
                    string modeStr = exclusion.Mode.ToString();
                    _logger.LogDebug("{Id}: Ignored '{Label}' as it fell within an exclusion zone (mode '{Mode}') at {Ms}ms.",
                        id, prediction.Label, modeStr, stopwatch.ElapsedMilliseconds);
                    return false;
                }
            }
            return true;
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
                canvas.DrawBitmap(bitmap, new SKPoint());
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

            IEnumerable<INotifier> notifiers = Config.Notifiers
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
