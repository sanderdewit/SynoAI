using System.Text.Json.Serialization;

namespace SynoAI.Notifiers.Pushbullet
{
    /// <summary>
    /// Represents the PusbBullet Upload Request
    /// </summary>
    public class PushbulletUploadRequest
    {
        /// <summary>
        /// Gets or sets the FileName
        /// </summary>
        [JsonPropertyName("file_name")]
        public string FileName { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets the filetype.
        /// </summary>
        [JsonPropertyName("file_type")]
        public string FileType { get; set; } = string.Empty;
    }
}
