using System.Text.Json.Serialization;

namespace SynoAI.Notifiers.Pushbullet
{
    /// <summary>
    /// Class for uploading the response to PushBUllet
    /// </summary>
    public class PushbulletUploadRequestResponse
    {
        /// <summary>
        /// Gets or sets the FileName.
        /// </summary>
        [JsonPropertyName("file_name")]
        public string FileName { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets the FileType.
        /// </summary>
        [JsonPropertyName("file_type")]
        public string FileType { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets the FileUrl.
        /// </summary>
        [JsonPropertyName("file_url")]
        public string FileUrl { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets the UploadUrl.
        /// </summary>
        [JsonPropertyName("upload_url")]
        public string UploadUrl { get; set; } = string.Empty;
    }
}
