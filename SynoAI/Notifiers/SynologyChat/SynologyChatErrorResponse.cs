using System.Text.Json.Serialization;

namespace SynoAI.Notifiers.SynologyChat
{
    internal class SynologyChatErrorResponse
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;
        [JsonPropertyName("errors")]
        public SynologyChatErrorReasonResponse? Errors { get; set; }
    }
}
