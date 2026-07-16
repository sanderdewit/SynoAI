using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SynoAI.Models;
using SynoAI.Models.DTOs;
using SynoAI.Services;
using SynoAI.Settings;
using System.Collections.Concurrent;

namespace SynoAI.Controllers
{
    /// <summary>
    /// Entry point for Synology motion-alert webhooks and camera enable/disable requests.
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class CameraController : ControllerBase
    {
        private readonly ICameraProcessingService _processingService;
        private readonly ILogger<CameraController> _logger;
        private readonly IOptionsMonitor<AppSettings> _options;

        private static readonly ConcurrentDictionary<string, bool> _runningCameraChecks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, DateTime> _delayedCameraChecks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, bool> _enabledCameras = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initialises a new instance of the <see cref="CameraController"/> class.
        /// </summary>
        public CameraController(ICameraProcessingService processingService, ILogger<CameraController> logger, IOptionsMonitor<AppSettings> options)
        {
            _processingService = processingService;
            _logger = logger;
            _options = options;
        }

        private AppSettings Settings => _options.CurrentValue;

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

            Camera? camera = Settings.Cameras.FirstOrDefault(x => x.Name.Equals(id, StringComparison.OrdinalIgnoreCase));
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

            if (camera.Wait > 0)
            {
                _logger.LogInformation("{Id}: Waiting {Wait}ms before fetching snapshot.", id, camera.Wait);
                await Task.Delay(camera.Wait);
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
                bool found = await _processingService.ProcessAsync(id, camera);
                AddCameraDelay(id, found ? camera.GetDelayAfterSuccess(Settings) : camera.GetDelay(Settings));
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
    }
}
