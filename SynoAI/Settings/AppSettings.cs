using Microsoft.Extensions.Configuration;
using SkiaSharp;
using SynoAI.AIs;
using SynoAI.Models;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SynoAI.Settings
{
    /// <summary>
    /// Strongly-typed application configuration, bound from configuration at startup and consumed via
    /// dependency injection (<see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/> or the registered
    /// singleton). Replaces the former static <c>Config</c> class.
    /// </summary>
    /// <remarks>
    /// Property defaults below intentionally mirror the historical defaults so behaviour is unchanged when a
    /// value is absent from configuration. Two properties bind from a differently-named configuration key via
    /// <see cref="ConfigurationKeyNameAttribute"/> (<c>User</c> and <c>ApiVersionInfo</c>).
    /// </remarks>
    public class AppSettings
    {
        /// <summary>The URL to the Synology API.</summary>
        public string Url { get; set; } = string.Empty;
        /// <summary>The username to log in to the Synology API with (configuration key <c>User</c>).</summary>
        [ConfigurationKeyName("User")]
        [JsonPropertyName("User")]
        public string Username { get; set; } = string.Empty;
        /// <summary>The password to log in to the Synology API with.</summary>
        public string Password { get; set; } = string.Empty;
        /// <summary>Allow insecure (self-signed certificate) access to the Synology API.</summary>
        public bool AllowInsecureUrl { get; set; }

        /// <summary>The version of the SYNO.API.Auth API to use (configuration key <c>ApiVersionInfo</c>).</summary>
        [ConfigurationKeyName("ApiVersionInfo")]
        [JsonPropertyName("ApiVersionInfo")]
        [Range(1, 100)]
        public int ApiVersionAuth { get; set; } = 6;
        /// <summary>The version of the SYNO.SurveillanceStation.Camera API to use.</summary>
        [Range(1, 100)]
        public int ApiVersionCamera { get; set; } = 9;

        /// <summary>The profile/quality of the image taken by the camera.</summary>
        public CameraQuality Quality { get; set; } = CameraQuality.Balanced;

        /// <summary>Whether to draw all predictions, only matches, or nothing.</summary>
        public DrawMode DrawMode { get; set; } = DrawMode.Matches;
        /// <summary>Whether to draw the exclusion zone boxes on images (useful for testing box locations).</summary>
        public bool DrawExclusions { get; set; }

        /// <summary>The stroke width of the box drawn around detected objects.</summary>
        [Range(0, 100)]
        public int StrokeWidth { get; set; } = 2;

        /// <summary>The hex colour of the box drawn around image matches.</summary>
        public string BoxColor { get; set; } = SKColors.Green.ToString();
        /// <summary>The hex colour of the exclusion boxes.</summary>
        public string ExclusionBoxColor { get; set; } = SKColors.Red.ToString();
        /// <summary>The hex colour drawn behind the label text on the image outputs.</summary>
        public string TextBoxColor { get; set; } = SKColors.Transparent.ToString();
        /// <summary>The hex colour of the label text on the image outputs.</summary>
        public string FontColor { get; set; } = SKColors.Green.ToString();

        /// <summary>The font to use on the image labels.</summary>
        public string Font { get; set; } = "Tahoma";
        /// <summary>The font size to use on the image labels.</summary>
        [Range(1, 400)]
        public int FontSize { get; set; } = 12;

        /// <summary>The horizontal offset of the label text from the boundary box.</summary>
        public int TextOffsetX { get; set; } = 4;
        /// <summary>The vertical offset of the label text from the boundary box.</summary>
        public int TextOffsetY { get; set; } = 2;

        /// <summary>When <c>true</c> (and <see cref="DrawMode"/> is <see cref="DrawMode.Matches"/>), labels use sequential reference numbers.</summary>
        public bool AlternativeLabelling { get; set; }
        /// <summary>When <c>true</c>, label text is placed below the detection box rather than inside/above it.</summary>
        public bool LabelBelowBox { get; set; }

        /// <summary>The default minimum width an object must be to be considered valid.</summary>
        [Range(0, int.MaxValue)]
        public int MinSizeX { get; set; } = 50;
        /// <summary>The default minimum height an object must be to be considered valid.</summary>
        [Range(0, int.MaxValue)]
        public int MinSizeY { get; set; } = 50;
        /// <summary>The default maximum width an object may be to be considered valid. <see cref="int.MaxValue"/> means "no maximum".</summary>
        [Range(0, int.MaxValue)]
        public int MaxSizeX { get; set; } = int.MaxValue;
        /// <summary>The default maximum height an object may be to be considered valid. <see cref="int.MaxValue"/> means "no maximum".</summary>
        [Range(0, int.MaxValue)]
        public int MaxSizeY { get; set; } = int.MaxValue;

        /// <summary>The maximum number of snapshots to sequentially retrieve per motion event.</summary>
        [Range(1, 100)]
        public int MaxSnapshots { get; set; } = 1;
        /// <summary>Whether/when the original unprocessed snapshot should be saved.</summary>
        public SaveSnapshotMode SaveOriginalSnapshot { get; set; }

        /// <summary>The number of days to keep captured images before they are automatically deleted (0 = keep forever).</summary>
        [Range(0, int.MaxValue)]
        public int DaysToKeepCaptures { get; set; }

        /// <summary>The default delay (ms) between the last motion detection and the next time it will be processed.</summary>
        [Range(0, int.MaxValue)]
        public int Delay { get; set; }
        /// <summary>The default delay (ms) after a successful detection. When null, falls back to <see cref="Delay"/>.</summary>
        [Range(0, int.MaxValue)]
        public int? DelayAfterSuccess { get; set; }

        /// <summary>The URL to the SynoAI web front end, used to build image links in notifications. Optional.</summary>
        public string? SynoAIUrl { get; set; }

        /// <summary>
        /// The API key that guards the admin/config endpoints and the settings UI. When empty, the admin
        /// API and UI are disabled (safe default) so settings — including the Synology password — can never
        /// be edited unauthenticated. Set this to enable the configuration UI.
        /// </summary>
        public string AdminApiKey { get; set; } = string.Empty;

        /// <summary>The AI configuration.</summary>
        public AiSettings AI { get; set; } = new();

        /// <summary>The configured cameras.</summary>
        public List<Camera> Cameras { get; set; } = new();

        /// <summary>
        /// Creates a deep copy, so callers (e.g. the settings API) can mutate settings without touching the
        /// live options instance shared across the application.
        /// </summary>
        public AppSettings Clone() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this))!;
    }

    /// <summary>
    /// Configuration for the AI detection backend.
    /// </summary>
    public class AiSettings
    {
        /// <summary>The AI system to process images with.</summary>
        public AIType Type { get; set; } = AIType.CodeProjectAIServer;
        /// <summary>The URL to access the AI.</summary>
        public string Url { get; set; } = string.Empty;
        /// <summary>The resource path to call on the AI.</summary>
        public string Path { get; set; } = "v1/vision/detection";
    }
}
