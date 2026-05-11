using System.Text.Json.Serialization;

namespace SynoAI.Notifiers.Pushbullet
{
    internal class PushbulletErrorResponse
    {
        public PushbulletError? Error { get; set; }
        [JsonPropertyName("error_code")]
        public string ErrorCode { get; set; } = string.Empty;
    }
}
