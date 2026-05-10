using Newtonsoft.Json;

namespace SynoAI.Notifiers.SynologyChat
{
    internal class SynologyChatErrorReasonResponse
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        [JsonProperty("reason")]
        public string Reason { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{Name} ({Reason})";
        }
    }
}
