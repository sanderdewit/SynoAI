using Microsoft.AspNetCore.Mvc;
using SynoAI.Settings;
using System.ComponentModel.DataAnnotations;

namespace SynoAI.Controllers
{
    /// <summary>
    /// Reads and updates the application settings (excluding cameras, which have their own endpoints).
    /// Guarded by the admin API key.
    /// </summary>
    [ApiController]
    [Route("api/settings")]
    [AdminApiKey]
    public class SettingsController : ControllerBase
    {
        private readonly ISettingsStore _store;

        /// <summary>Initialises a new instance of the <see cref="SettingsController"/> class.</summary>
        public SettingsController(ISettingsStore store)
        {
            _store = store;
        }

        /// <summary>
        /// Gets the current settings. Secret values (<see cref="AppSettings.Password"/>,
        /// <see cref="AppSettings.AdminApiKey"/>) are blanked and never returned.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(200)]
        public ActionResult<AppSettings> Get()
        {
            AppSettings settings = _store.Get().Clone();
            settings.Password = string.Empty;
            settings.AdminApiKey = string.Empty;
            return settings;
        }

        /// <summary>
        /// Updates the settings. Cameras are preserved (managed via the cameras endpoints). Blank secret
        /// fields keep their current values, so the UI never needs to re-send them.
        /// </summary>
        [HttpPut]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> Put([FromBody] AppSettings incoming)
        {
            if (incoming == null)
                return BadRequest("Request body is required.");

            AppSettings current = _store.Get();

            // Preserve secrets left blank, and the camera list (edited through /api/cameras).
            if (string.IsNullOrEmpty(incoming.Password))
                incoming.Password = current.Password;
            if (string.IsNullOrEmpty(incoming.AdminApiKey))
                incoming.AdminApiKey = current.AdminApiKey;
            incoming.Cameras = current.Cameras;

            List<ValidationResult> errors = new();
            if (!Validator.TryValidateObject(incoming, new ValidationContext(incoming), errors, validateAllProperties: true))
                return BadRequest(new { errors = errors.Select(e => e.ErrorMessage) });

            await _store.SaveAsync(incoming);
            return NoContent();
        }
    }
}
