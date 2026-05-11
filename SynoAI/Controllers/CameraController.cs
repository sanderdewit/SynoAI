using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SkiaSharp;
using SynoAI.Hubs;
using SynoAI.Models;
using SynoAI.Models.DTOs;
using SynoAI.Notifiers;
using SynoAI.Services;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace SynoAI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class CameraController : ControllerBase
    {
        private readonly IHubContext<SynoAIHub> _hubContext;
        private readonly IAIService _aiService;
        private readonly ISynologyService _synologyService;
        private readonly ILogger<CameraController> _logger;

        private static readonly ConcurrentDictionary<string, bool> _runningCameraChecks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> _delayedCameraChecks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, bool> _enabledCameras = new(StringComparer.OrdinalIgnoreCase);

        public CameraController(IAIService aiService, ISynologyService synologyService, ILogger<CameraController> logger, IHubContext<SynoAIHub> hubContext)
        {
            _hubContext = hubContext;
            _aiService = aiService;
            _synologyService = synologyService;
            _logger = logger;
        }

        /// <summary>
        /// Called by the Synology motion alert hook.
        /// </summary>
        [HttpGet]
        [Route("{id}")]
        public async Task<IActionResult> Get(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest("Camera id is required.");

            if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return BadRequest("Camera id contains invalid characters.");

            if (_enabledCameras.TryGetValue(id, out bool enabled) && !enabled)
            {
                _logger.LogInformation("{Id}: Requests for this camera will not be processed as it is currently disabled.", id);
                return Ok();
            }

            Camera? camera = Config.Cameras.FirstOrDefault(x => x.Name.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (camera == null)
            {
                _logger.LogError("{Id}: The camera was not found.", id);
                return NotFound();
            }

            lock (_delayedCameraChecks)
            {
                if (_delayedCameraChecks.TryGetValue(id, out DateTime ignoreUntil) && ignoreUntil >= DateTime.UtcNow)
                {
                    _logger.LogInformation("{Id}: Requests for this camera will not be processed until {IgnoreUntil}.", id, ignoreUntil);
                    return Ok();
                }
            }

            lock (_runningCameraChecks)
            {
                if (_runningCameraChecks.TryGetValue(id, out bool running) && running)
                {
                    _logger.LogInformation("{Id}: The request for this camera is already running and was ignored.", id);
                    return Ok();
                }
                _runningCameraChecks.AddOrUpdate(id, true, (_, _) => true);
                _logger.LogDebug("{Id}: Processing started.", id);
            }

            try
            {
                if (camera.Wait > 0)
                {
                    _logger.LogInformation("{Id}: Waiting {Wait}ms before fetching snapshot.", id, camera.Wait);
                    await Task.Delay(camera.Wait);
                }

                Stopwatch overallStopwatch = Stopwatch.StartNew();

                for (int snapshotCount = 1; snapshotCount <= Config.MaxSnapshots; snapshotCount++)
                {
                    _logger.LogInformation("{Id}: Snapshot {Count} of {Max} requested at {Ms}ms.", id, snapshotCount, Config.MaxSnapshots, overallStopwatch.ElapsedMilliseconds);

                    byte[]? snapshot = await GetSnapshot(id);
                    if (snapshot == null)
                        continue;

                    _logger.LogInformation("{Id}: Snapshot {Count} of {Max} received at {Ms}ms.", id, snapshotCount, Config.MaxSnapshots, overallStopwatch.ElapsedMilliseconds);

                    // Rotate if required; keep the SKBitmap to avoid re-decoding later
                    (byte[] processedBytes, SKBitmap? processedBitmap) = PreProcessSnapshot(camera, snapshot);

                    List<AIPrediction> predictions = await GetAIPredications(camera, processedBytes) ?? new();

                    _logger.LogInformation("{Id}: Snapshot {Count} of {Max} contains {PredCount} objects at {Ms}ms.", id, snapshotCount, Config.MaxSnapshots, predictions.Count, overallStopwatch.ElapsedMilliseconds);

                    int minSizeX = camera.GetMinSizeX();
                    int minSizeY = camera.GetMinSizeY();
                    int maxSizeX = camera.GetMaxSizeX();
                    int maxSizeY = camera.GetMaxSizeY();

                    List<AIPrediction> validPredictions = new();
                    foreach (AIPrediction prediction in predictions)
                    {
                        if (camera.Types != null && !camera.Types.Contains(prediction.Label, StringComparer.OrdinalIgnoreCase))
                        {
                            _logger.LogDebug("{Id}: Ignored '{Label}' as it's not in the valid type list at {Ms}ms.", id, prediction.Label, overallStopwatch.ElapsedMilliseconds);
                        }
                        else if (prediction.SizeX < minSizeX || prediction.SizeY < minSizeY)
                        {
                            _logger.LogDebug("{Id}: Ignored '{Label}' as it's under the minimum size ({MinX}x{MinY}) at {Ms}ms.", id, prediction.Label, minSizeX, minSizeY, overallStopwatch.ElapsedMilliseconds);
                        }
                        else if ((maxSizeX < int.MaxValue && prediction.SizeX > maxSizeX) || (maxSizeY < int.MaxValue && prediction.SizeY > maxSizeY))
                        {
                            _logger.LogDebug("{Id}: Ignored '{Label}' as it exceeds the maximum size ({MaxX}x{MaxY}) at {Ms}ms.", id, prediction.Label, maxSizeX, maxSizeY, overallStopwatch.ElapsedMilliseconds);
                        }
                        else if (ShouldIncludePrediction(id, camera, overallStopwatch, prediction))
                        {
                            validPredictions.Add(prediction);
                            _logger.LogDebug("{Id}: Valid prediction '{Label}' at {Ms}ms.", id, prediction.Label, overallStopwatch.ElapsedMilliseconds);
                        }
                    }

                    if (Config.SaveOriginalSnapshot == SaveSnapshotMode.Always ||
                        (Config.SaveOriginalSnapshot == SaveSnapshotMode.WithPredictions && predictions.Count > 0) ||
                        (Config.SaveOriginalSnapshot == SaveSnapshotMode.WithValidPredictions && validPredictions.Count > 0))
                    {
                        _logger.LogInformation("{Id}: Saving original image", id);
                        SnapshotManager.SaveOriginalImage(_logger, camera, processedBytes);
                    }

                    if (validPredictions.Count > 0)
                    {
                        // Pass the pre-decoded bitmap (if available) so DressImage skips an extra decode
                        ProcessedImage processedImage = SnapshotManager.DressImage(camera, processedBytes, predictions, validPredictions, _logger, processedBitmap);

                        Notification notification = new()
                        {
                            ProcessedImage = processedImage,
                            ValidPredictions = validPredictions
                        };

                        await SendNotifications(camera, notification);
                        await _hubContext.Clients.All.SendAsync("ReceiveSnapshot", camera.Name, processedImage.FileName);

                        _logger.LogInformation("{Id}: Valid object found in snapshot {Count} of {Max} at {Ms}ms.", id, snapshotCount, Config.MaxSnapshots, overallStopwatch.ElapsedMilliseconds);

                        AddCameraDelay(id, camera.GetDelayAfterSuccess());
                        return Ok();
                    }

                    if (predictions.Count > 0)
                        _logger.LogInformation("{Id}: No valid objects at {Ms}ms.", id, overallStopwatch.ElapsedMilliseconds);
                    else
                        _logger.LogInformation("{Id}: Nothing detected by the AI at {Ms}ms.", id, overallStopwatch.ElapsedMilliseconds);

                    _logger.LogInformation("{Id}: Finished ({Ms}ms).", id, overallStopwatch.ElapsedMilliseconds);
                }

                AddCameraDelay(id, camera.GetDelay());
            }
            finally
            {
                lock (_runningCameraChecks)
                {
                    _logger.LogDebug("{Id}: Removing running camera block.", id);
                    _runningCameraChecks.Remove(id, out _);
                }
            }

            return Ok();
        }

        /// <summary>
        /// Enable or disable processing for a camera.
        /// </summary>
        [HttpPost]
        [Route("{id}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        public IActionResult Post(string id, [FromBody] CameraOptionsDto options)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest("Camera id is required.");

            if (options == null)
                return BadRequest("Request body is required.");

            if (options.HasChanged(x => x.Enabled))
                _enabledCameras.AddOrUpdate(id, options.Enabled, (_, _) => options.Enabled);

            return Ok();
        }

        private bool ShouldIncludePrediction(string id, Camera camera, Stopwatch overallStopwatch, AIPrediction prediction)
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
                        id, prediction.Label, modeStr, overallStopwatch.ElapsedMilliseconds);
                    return false;
                }
            }
            return true;
        }

        private void AddCameraDelay(string id, int delay)
        {
            if (delay == 0)
                return;

            lock (_delayedCameraChecks)
            {
                DateTime ignoreUntil = DateTime.UtcNow.AddMilliseconds(delay);
                _delayedCameraChecks.AddOrUpdate(id, ignoreUntil, (_, _) => ignoreUntil);
                _logger.LogDebug("{Id}: Added delay of {Delay}ms.", id, delay);
            }
        }

        /// <summary>
        /// Rotates the image if configured. Returns the bytes for the AI and the SKBitmap
        /// to avoid re-decoding inside DressImage (#19).
        /// </summary>
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

        private async Task SendNotifications(Camera camera, Notification notification)
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

        private async Task<byte[]?> GetSnapshot(string cameraName)
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

        private async Task<List<AIPrediction>?> GetAIPredications(Camera camera, byte[] imageBytes)
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
    }
}
