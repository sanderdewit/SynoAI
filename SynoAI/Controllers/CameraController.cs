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
using System.Drawing;
using System.Text;

namespace SynoAI.Controllers
{
    /// <summary>
    /// Controller triggered on a motion alert from synology, which will act as a bridge between the Synology camera and DeepStack AI.
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class CameraController : ControllerBase
    {
        // euquiq: Needed for connecting into the SignalR hub and send valid Snapshot for rt web monitoring
        private readonly IHubContext<SynoAIHub> _hubContext;

        private readonly IAIService _aiService;
        private readonly ISynologyService _synologyService;
        private readonly ILogger<CameraController> _logger;

        private static readonly ConcurrentDictionary<string, bool> _runningCameraChecks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> _delayedCameraChecks = new(StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, bool> _enabledCameras = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Initializes a new instance of the <see cref="CameraController"/> class.
        /// </summary>
        /// <param name="aiService">The AI service.</param>
        /// <param name="synologyService">The Synology service.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="hubContext">The SignalR hub context.</param>

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
        /// <param name="id">The name of the camera.</param>
        [HttpGet]
        [Route("{id}")]
        public async Task<IActionResult> Get(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return BadRequest("Camera ID must not be empty.");
            }

            if (_enabledCameras.TryGetValue(id, out bool enabled))
            {
                if (!enabled)
                {
                    // The camera has been disabled, so don't process any requests
                    _logger.LogInformation("{id}: Requests for this camera will not be processed as it is currently disabled.",
                        id);
                    return Ok();
                }
            }

            // Fetch the camera
            Camera? camera = Config.Cameras.FirstOrDefault(x => x.Name.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (camera == null)
            {
                _logger.LogError("{id}: The camera was not found.",
                    id);
                return NotFound($"Camera '{id}' was not found in the configuration.");
            }


            // Ensure the camera isn't under a delay
            lock (_delayedCameraChecks)
            {
                if (_delayedCameraChecks.TryGetValue(id, out DateTime ignoreUntil) && ignoreUntil >= DateTime.UtcNow)
                {
                    // The camera is under a detection delay for the period specified, so ignore this request
                    _logger.LogInformation("{id}: Requests for this camera will not be processed until {ignoreUntil}.",
                        id,
                        ignoreUntil);
                    return Ok();
                }
            }

            // Ensure the camera isn't currently processing
            lock (_runningCameraChecks)
            {
                if (_runningCameraChecks.TryGetValue(id, out bool running) && running)
                {
                    // The camera is already running, so ignore this request
                    _logger.LogInformation("{id}: The request for this camera is already running and was ignored.",
                        id);
                    return Ok();
                }
                else
                {
                    // The camera isn't running, so mark it as running
                    _runningCameraChecks.AddOrUpdate(id, true, (key, oldValue) => true);
                    _logger.LogDebug("{id}: The camera is currently running; no other camera requests will be processed while this request is ongoing.",
                        id);
                }
            }

            try

            {
                // Wait if the camera has a wait
                if (camera.Wait > 0)
                {
                    _logger.LogInformation("{id}: Waiting for {camera.Wait}ms before fetching snapshot.",
                        id, camera.Wait);
                    await Task.Delay(camera.Wait);
                }

                // Create the stopwatches for reporting timings
                Stopwatch overallStopwatch = Stopwatch.StartNew();

                // Start loop for requesting snapshots until a valid prediction is found or MaxSnapshots is reached
                for (int snapshotCount = 1; snapshotCount <= Config.MaxSnapshots; snapshotCount++)
                {
                    // Take the snapshot from Surveillance Station
                    _logger.LogInformation("{id}: Snapshot {snapshotCount} of {ConfigMaxSnapshots} requested at EVENT TIME {overallStopwatchElapsedMilliseconds}ms.",
                        id,
                        snapshotCount,
                        Config.MaxSnapshots,
                        overallStopwatch.ElapsedMilliseconds);
                    byte[]? snapshot = await GetSnapshot(id);
                    if (snapshot == null)
                    {
                        // Failed to get any result, so skip over this snapshot
                        continue;
                    }

                    _logger.LogInformation("{id}: Snapshot {snapshotCount} of {ConfigMaxSnapshots} received at EVENT TIME {overallStopwatchElapsedMilliseconds}ms.",
                        id,
                        snapshotCount,
                        Config.MaxSnapshots,
                        overallStopwatch.ElapsedMilliseconds);

                    // See if the image needs to be rotated (or further processing in the future ?) before being analyzed by the AI
                    // #19: PreProcessSnapshot now returns the encoded bytes AND, when rotation occurred,
                    // the already-decoded SKBitmap to avoid a redundant decode in DressImage.
                    var (processedBytes, processedBitmap) = PreProcessSnapshot(camera, snapshot);

                    // Use the AI to get the valid predictions and then get all the valid predictions, where the result from the AI is
                    // in the list of types and where the size of the object is bigger than the defined value.
                    IEnumerable<AIPrediction>? predictions = await GetAIPredications(camera, processedBytes);
                    if (predictions == null)
                    {
                        // An error occured fetching predictions, so bail-out early.
                        return Ok();
                    }

                    _logger.LogInformation("{id}: Snapshot {snapshotCount} of {ConfigMaxSnapshots} contains {predictionsCount} objects at EVENT TIME {overallStopwatchElapsedMilliseconds}ms.",
                        id,
                        snapshotCount,
                        Config.MaxSnapshots,
                        predictions.Count(),
                        overallStopwatch.ElapsedMilliseconds);

                    int minSizeX = camera.GetMinSizeX();
                    int minSizeY = camera.GetMinSizeY();
                    int maxSizeX = camera.GetMaxSizeX();
                    int maxSizeY = camera.GetMaxSizeY();

                    List<AIPrediction> validPredictions = new();
                    foreach (AIPrediction prediction in predictions)
                    {
                        // Check if the prediction label is in the list of types the camera is looking for
                        if (camera.Types != null && !camera.Types.Contains(prediction.Label, StringComparer.OrdinalIgnoreCase))
                        {
                            _logger.LogDebug("{id}: Ignored '{predictionLabel}' ([{predictionMinX},{predictionMinY}],[{predictionMaxX},{predictionMaxY}]) as it's not in the valid type list ({camtypes}) at EVENT TIME {overallStopwatchElapsedMilliseconds}ms.",
                                id,
                                prediction.Label,
                                prediction.MinX,
                                prediction.MinY,
                                prediction.MaxX,
                                prediction.MaxY,
                                string.Join(",", camera.Types),
                                overallStopwatch.ElapsedMilliseconds);

                        }
                        else
                        {
                            // Ensure that the prediction is bigger than the minimum size
                            if (prediction.SizeX < minSizeX || prediction.SizeY < minSizeY)
                            {
                                // The prediction is under the minimum specified size
                                _logger.LogDebug("{id}: Ignored '{predictionLabel}' ([{predictionMinX},{predictionMinY}],[{predictionMaxX},{predictionMaxY}]) as it's under the minimum specified size ({minSizeX}x{minSizeY}) at EVENT TIME {overallStopwatchElapsedMilliseconds}ms.",
                                    id,
                                    prediction.Label,
                                    prediction.MinX,
                                    prediction.MinY,
                                    prediction.MaxX,
                                    prediction.MaxY,
                                    minSizeX,
                                    minSizeY,
                                    overallStopwatch.ElapsedMilliseconds);

                            }
                            else if (prediction.SizeX > maxSizeX || prediction.SizeY > maxSizeY)
                            {
                                // The prediction has exceeded the maximum specified size
                                _logger.LogDebug("{id}: Ignored '{predictionLabel}' ([{predictionMinX},{predictionMinY}],[{predictionMaxX},{predictionMaxY}]) as it exceeds the maximum specified size ({maxSizeX}x{maxSizeY}) at EVENT TIME {overallStopwatchElapsedMilliseconds}ms.",
                                    id,
                                    prediction.Label,
                                    prediction.MinX,
                                    prediction.MinY,
                                    prediction.MaxX,
                                    prediction.MaxY,
                                    maxSizeX,
                                    maxSizeY,
                                    overallStopwatch.ElapsedMilliseconds);

                            }
                            else
                            {
                                bool include = ShouldIncludePrediction(id, camera, overallStopwatch, prediction);
                                if (include)
                                {
                                    validPredictions.Add(prediction);
                                    _logger.LogDebug("{id}: Found valid prediction '{predictionLabel}' ([{predictionMinX},{predictionMinY}],[{predictionMaxX},{predictionMaxY}]) at EVENT TIME {overallStopwatchElapsedMilliseconds}ms.",
                                        id,
                                        prediction.Label,
                                        prediction.MinX,
                                        prediction.MinY,
                                        prediction.MaxX,
                                        prediction.MaxY,
                                        overallStopwatch.ElapsedMilliseconds);
                                }
                            }
                        }
                    }

                    // Save the original unprocessed image if required
                    if (Config.SaveOriginalSnapshot == SaveSnapshotMode.Always ||
                        (Config.SaveOriginalSnapshot == SaveSnapshotMode.WithPredictions && predictions.Any()) ||
                        (Config.SaveOriginalSnapshot == SaveSnapshotMode.WithValidPredictions && validPredictions.Count > 0))
                    {
                        _logger.LogInformation("{id}: Saving original image",
                            id);
                        SnapshotManager.SaveOriginalImage(_logger, camera, processedBytes);
                    }

                    if (validPredictions.Count > 0)
                    {
                        // Process and save the snapshot, passing the pre-decoded bitmap if available (#19)
                        ProcessedImage processedImage = SnapshotManager.DressImage(camera, processedBytes, predictions, validPredictions, _logger, processedBitmap);

                        // Send Notifications
                        Notification notification = new()
                        {
                            ProcessedImage = processedImage,
                            ValidPredictions = validPredictions
                        };

                        await SendNotifications(camera, notification);

                        // Inform eventual web users about this new Snapshot, for the "realtime" option thru Web
                        await _hubContext.Clients.All.SendAsync("ReceiveSnapshot", camera.Name, processedImage.FileName);
                        _logger.LogInformation("{id}: Valid object found in snapshot {snapshotCount} of {ConfigMaxSnapshots} at EVENT TIME {overallStopwatchElapsedMilliseconds}ms.",
                            id,
                            snapshotCount,
                            Config.MaxSnapshots,
                            overallStopwatch.ElapsedMilliseconds);

                        // Extend the delay until the next motion detection will be run if a delay after success is specified
                        int successDelay = camera.GetDelayAfterSuccess();
                        AddCameraDelay(id, successDelay);
                        return Ok();
                    }
                    else if (predictions.Any())
                    {
                        // We got predictions back from the AI, but nothing that should trigger an alert
                        _logger.LogInformation("{id}: No valid objects at EVENT TIME {overallStopwatchElapsedMilliseconds}ms.",
                            id,
                            overallStopwatch.ElapsedMilliseconds);
                    }

                    else
                    {
                        // We didn't get any predictions whatsoever from the AI
                        _logger.LogInformation("{id}: Nothing detected by the AI at EVENT TIME {overallStopwatchElapsedMilliseconds}ms.",
                            id,
                            overallStopwatch.ElapsedMilliseconds);


                        StringBuilder nothingFoundOutput = new($"{id}: No objects ");
                        if (camera.Types != null && camera.Types.Any())
                        {
                            nothingFoundOutput.Append($"in the specified list ({string.Join(", ", camera.Types)}) ");
                        }
                        nothingFoundOutput.Append($"were detected by the AI exceeding the confidence level ({camera.Threshold}%) and/or minimum size ({minSizeX}x{minSizeY} and/or maximum size ({maxSizeX},{maxSizeY}))");

                        _logger.LogDebug("{Output}",
                        nothingFoundOutput.ToString());

                    }

                    _logger.LogInformation("{id}: Finished ({overallStopwatchElapsedMilliseconds}ms).",
                        id,
                        overallStopwatch.ElapsedMilliseconds);

                }

                // Add the delay (if any)
                int delay = camera.GetDelay();
                AddCameraDelay(id, delay);
            }
            finally
            {
                // Ensure the camera is unflagged as running
                lock (_runningCameraChecks)
                {
                    _logger.LogDebug("{id}: Removing running camera block.",
                        id);
                    _runningCameraChecks.Remove(id, out _);
                }
            }

            return Ok();
        }
        /// <summary>
        /// POSTs camera options with the specified ID.
        /// </summary>
        /// <param name="id">The ID of the camera.</param>
        /// <param name="options">The camera options to be posted.</param>
        [HttpPost]
        [Route("{id}")]

        public IActionResult Post(string id, [FromBody] CameraOptionsDto options)
        {
            if (options.HasChanged(x => x.Enabled))
            {
                _enabledCameras.AddOrUpdate(id, options.Enabled, (key, oldValue) => options.Enabled);
            }
            return Ok();
        }

        /// <summary>
        /// Checks to ensure the prediction falls within the boundaries to be considered a valid prediction.
        /// </summary>
        /// <param name="id">The ID of the camera.</param>
        /// <param name="camera">The camera object.</param>
        /// <param name="overallStopwatch">The current stopwatch for logging.</param>
        /// <param name="prediction">The prediction to validate.</param>
        /// <returns>True if the prediction is valid.</returns>
        private bool ShouldIncludePrediction(string id, Camera camera, Stopwatch overallStopwatch, AIPrediction prediction)
        {
            // Check if the prediction falls within the exclusion zones
            if (camera.Exclusions != null && camera.Exclusions.Count > 0)
            {
                Rectangle boundary = new(prediction.MinX, prediction.MinY, prediction.SizeX, prediction.SizeY);
                foreach (Zone exclusion in camera.Exclusions)
                {
                    int startX = Math.Min(exclusion.Start.X, exclusion.End.X);
                    int startY = Math.Min(exclusion.Start.Y, exclusion.End.Y);
                    int endX = Math.Max(exclusion.Start.X, exclusion.End.X);
                    int endY = Math.Max(exclusion.Start.Y, exclusion.End.Y);
                    Rectangle exclusionZoneBoundary = new(startX, startY, endX - startX, endY - startY);
                    bool exclude = exclusion.Mode == OverlapMode.Contains ? exclusionZoneBoundary.Contains(boundary) : exclusionZoneBoundary.IntersectsWith(boundary);
                    if (exclude)
                    {
                        // The prediction boundary is contained within or intersects and exclusion zone, so ignore it    ;
                        _logger.LogDebug("{id}: Ignored matching '{predictionLabel}' ([{predictionMinX},{predictionMinY}],[{predictionMaxX},{predictionMaxY}]) as it fell within the exclusion zone ([{exclusionStartX},{exclusionStartY}],[{exclusionEndX},{exclusionEndY}]) with exclusion mode '{exclusionMode}' at EVENT TIME {overallStopwatchElapsedMilliseconds}ms.",
                            id,
                            prediction.Label,
                            prediction.MinX,
                            prediction.MinY,
                            prediction.MaxX,
                            prediction.MaxY,
                            exclusion.Start.X,
                            exclusion.Start.Y,
                            exclusion.End.X,
                            exclusion.End.Y,
                            exclusion.Mode,
                            overallStopwatch.ElapsedMilliseconds
                            );
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Adds a delay for the specified camera.
        /// </summary>
        /// <param name="id">The ID of the camera to add a delay for.</param>
        /// <param name="delay">The delay to add.</param>
        private void AddCameraDelay(string id, int delay)
        {
            if (delay == 0)
            {
                return;
            }

            lock (_delayedCameraChecks)
            {
                DateTime ignoreUntil = DateTime.UtcNow.AddMilliseconds(delay);
                _delayedCameraChecks.AddOrUpdate(id, ignoreUntil, (key, oldValue) => ignoreUntil);
                _logger.LogDebug("{id}: Added delay of {delay} until the next request will be processed.",
                    id,
                    delay);
            }
        }

        /// <summary>
        /// Handles any required preprocessing of the captured image.
        /// When rotation is applied, the already-decoded <see cref="SKBitmap"/> is returned
        /// alongside the JPEG bytes so that <see cref="SnapshotManager.DressImage"/> can
        /// skip a redundant decode (fixes #19 — triple JPEG encode/decode on rotation).
        /// </summary>
        /// <param name="camera">The camera that the snapshot is from.</param>
        /// <param name="snapshot">The image data.</param>
        /// <returns>
        /// A tuple of the processed JPEG bytes and, if rotation was applied, the decoded
        /// <see cref="SKBitmap"/>; otherwise <c>null</c> for the bitmap.
        /// </returns>
        private (byte[] bytes, SKBitmap? bitmap) PreProcessSnapshot(Camera camera, byte[] snapshot)
        {
            if (camera.Rotate != 0)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();

                // Load the bitmap & rotate the image
                SKBitmap bitmap = SKBitmap.Decode(snapshot);

                _logger.LogInformation("{cameraName}: Rotating image {cameraRotate} degrees.",
                    camera.Name,
                    camera.Rotate);
                bitmap = Rotate(bitmap, camera.Rotate);

                using SKPixmap pixmap = bitmap.PeekPixels();
                using SKData? data = pixmap.Encode(SKEncodedImageFormat.Jpeg, 100);
                _logger.LogInformation("{cameraName}: Image preprocessing complete ({stopwatchElapsedMilliseconds}ms).",
                    camera.Name,
                    stopwatch.ElapsedMilliseconds);
                return (data?.ToArray() ?? snapshot, bitmap);
            }
            else
            {
                return (snapshot, null);
            }
        }

        /// <summary>
        /// Rotates the image to the specified angle.
        /// </summary>
        /// <param name="bitmap">The bitmap to rotate.</param>
        /// <param name="angle">The angle to rotate to.</param>
        /// <returns>The rotated bitmap.</returns>
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


        /// <summary>
        /// Sends notifications, if there is any configured
        /// </summary>
        /// <param name="camera">The camera responsible for this snapshot.</param>
        /// <param name="notification">The notification data to process.</param>
        private async Task SendNotifications(Camera camera, Notification notification)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            IEnumerable<string> labels = notification.ValidPredictions.Select(x => x.Label).Distinct().ToList();

            IEnumerable<INotifier> notifiers = Config.Notifiers
                .Where(x =>
                    (x.Cameras == null || !x.Cameras.Any() || x.Cameras.Any(c => c.Equals(camera.Name, StringComparison.OrdinalIgnoreCase))) &&
                    (x.Types == null || !x.Types.Any() || x.Types.Any(t => labels.Contains(t, StringComparer.OrdinalIgnoreCase)))
                ).ToList();

            List<Task> tasks = new();
            foreach (INotifier notifier in notifiers)
            {
                tasks.Add(notifier.SendAsync(camera, notification, _logger));
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();
            _logger.LogInformation("{camera.Name}: Notifications sent ({stopwatchElapsedMilliseconds}ms).",
                camera.Name,
                stopwatch.ElapsedMilliseconds);
        }

        /// <summary>
        /// Gets an image snapshot (in memory) from Surveillation Station.
        /// </summary>
        /// <param name="cameraName">The name of the camera to get the snapshot for.</param>
        /// <returns>A byte array for the image, or null on failure.</returns>
        private async Task<byte[]?> GetSnapshot(string cameraName)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            byte[]? imageBytes = await _synologyService.TakeSnapshotAsync(cameraName);
            stopwatch.Stop();

            if (imageBytes == null)
            {
                _logger.LogError("{cameraName}: Failed to get snapshot.",
                    cameraName);
            }
            else
            {
                _logger.LogInformation("{cameraName}: Snapshot received in {stopwatchElapsedMilliseconds}ms.",
                    cameraName,
                    stopwatch.ElapsedMilliseconds);
            }
            return imageBytes;
        }

        /// <summary>
        /// Passes the provided image to the AI and gets the predictions back.
        /// </summary>
        /// <param name="camera">The camera that the image is from.</param>
        /// <param name="imageBytes">The in-memory image for processing.</param>
        /// <returns>A list of predictions, or null on failure.</returns>
        private async Task<IEnumerable<AIPrediction>?> GetAIPredications(Camera camera, byte[] imageBytes)
        {
            IEnumerable<AIPrediction>? predictions = await _aiService.ProcessAsync(camera, imageBytes);
            if (predictions == null)
            {
                _logger.LogError("{camera}: Failed to get get predictions.",
                    camera);
                return null;
            }

            foreach (AIPrediction prediction in predictions)
            {
                _logger.LogInformation("AI Detected '{camera}': {prediction.Label} ({prediction.Confidence}%) [Size: {prediction.SizeX}x{prediction.SizeY}] [Start: {prediction.MinX},{prediction.MinY} | End: {prediction.MaxX},{prediction.MaxY}]",
                    camera,
                    prediction.Label,
                    prediction.Confidence,
                    prediction.SizeX,
                    prediction.SizeY,
                    prediction.MinX,
                    prediction.MinY,
                    prediction.MaxX,
                    prediction.MaxY);
            }

            return predictions;
        }
    }
}
