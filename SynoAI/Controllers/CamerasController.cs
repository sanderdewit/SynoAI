using Microsoft.AspNetCore.Mvc;
using SynoAI.Models;
using SynoAI.Services;
using SynoAI.Settings;

namespace SynoAI.Controllers
{
    /// <summary>
    /// CRUD for the configured cameras plus a live calibration snapshot, used by the settings UI to draw
    /// exclusion zones and size boxes on the actual camera image. Guarded by the admin API key.
    /// </summary>
    [ApiController]
    [Route("api/cameras")]
    [AdminApiKey]
    public class CamerasController : ControllerBase
    {
        private readonly ISettingsStore _store;
        private readonly ISynologyService _synologyService;

        /// <summary>Initialises a new instance of the <see cref="CamerasController"/> class.</summary>
        public CamerasController(ISettingsStore store, ISynologyService synologyService)
        {
            _store = store;
            _synologyService = synologyService;
        }

        /// <summary>Lists all configured cameras.</summary>
        [HttpGet]
        public ActionResult<IEnumerable<Camera>> List() => _store.Get().Cameras;

        /// <summary>Gets a single camera by name.</summary>
        [HttpGet("{name}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public ActionResult<Camera> Get(string name)
        {
            Camera? camera = Find(_store.Get(), name);
            return camera == null ? NotFound() : camera;
        }

        /// <summary>Creates a new camera.</summary>
        [HttpPost]
        [ProducesResponseType(201)]
        [ProducesResponseType(400)]
        [ProducesResponseType(409)]
        public async Task<IActionResult> Create([FromBody] Camera camera)
        {
            if (camera == null || string.IsNullOrWhiteSpace(camera.Name))
                return BadRequest("A camera name is required.");

            AppSettings settings = _store.Get().Clone();
            if (Find(settings, camera.Name) != null)
                return Conflict($"A camera named '{camera.Name}' already exists.");

            settings.Cameras.Add(camera);
            await _store.SaveAsync(settings);
            return CreatedAtAction(nameof(Get), new { name = camera.Name }, camera);
        }

        /// <summary>Updates an existing camera.</summary>
        [HttpPut("{name}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Update(string name, [FromBody] Camera camera)
        {
            if (camera == null)
                return BadRequest("Request body is required.");

            AppSettings settings = _store.Get().Clone();
            int index = settings.Cameras.FindIndex(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return NotFound();

            // Keep the route name authoritative to avoid silent renames colliding with another camera.
            camera.Name = settings.Cameras[index].Name;
            settings.Cameras[index] = camera;
            await _store.SaveAsync(settings);
            return NoContent();
        }

        /// <summary>Deletes a camera.</summary>
        [HttpDelete("{name}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Delete(string name)
        {
            AppSettings settings = _store.Get().Clone();
            int removed = settings.Cameras.RemoveAll(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                return NotFound();

            await _store.SaveAsync(settings);
            return NoContent();
        }

        /// <summary>
        /// Returns a live JPEG snapshot from the camera, for use as the calibration background in the UI.
        /// </summary>
        [HttpGet("{name}/snapshot")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(502)]
        public async Task<IActionResult> Snapshot(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return BadRequest("Invalid camera name.");

            byte[]? snapshot = await _synologyService.TakeSnapshotAsync(name);
            if (snapshot == null)
                return StatusCode(StatusCodes.Status502BadGateway, "Failed to fetch a snapshot from Surveillance Station.");

            return File(snapshot, "image/jpeg");
        }

        private static Camera? Find(AppSettings settings, string name)
            => settings.Cameras.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
