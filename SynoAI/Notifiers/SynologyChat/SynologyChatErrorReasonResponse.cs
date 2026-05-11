using System.Text.Json.Serialization;

namespace SynoAI.Notifiers.SynologyChat
{
    internal class SynologyChatErrorReasonResponse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{Name} ({Reason})";
        }
    }
}
