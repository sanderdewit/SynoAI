using System.Text.Json.Serialization;

namespace SynoAI.Notifiers.SynologyChat
{
    internal class SynologyChatResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        [JsonPropertyName("error")]
        public SynologyChatErrorResponse? Error { get; set; }
    }
}
