using System.Text.Json.Serialization;

namespace SynoAI.Notifiers.Pushbullet
{
    internal class PushbulletPush
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;
        [JsonPropertyName("url")]
        public string? Url { get; set; }
        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }
        [JsonPropertyName("file_type")]
        public string? FileType { get; set; }
        [JsonPropertyName("file_url")]
        public string? FileUrl { get; set; }
        [JsonPropertyName("source_device_iden")]
        public string? SourceDeviceIdentifier { get; set; }
        [JsonPropertyName("device_iden")]
        public string? DeviceIdentifier { get; set; }
        [JsonPropertyName("client_iden")]
        public string? ClientIdentifier { get; set; }
        [JsonPropertyName("channel_tag")]
        public string? ChannelTag { get; set; }
        [JsonPropertyName("email")]
        public string? Email { get; set; }
        [JsonPropertyName("guid")]
        public string? Guid { get; set; }

        [JsonPropertyName("image_url")]
        public string? ImageUrl { get; set; }
    }
}
