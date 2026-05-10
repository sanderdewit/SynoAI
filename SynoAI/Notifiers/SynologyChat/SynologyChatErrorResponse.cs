using Newtonsoft.Json;

namespace SynoAI.Notifiers.SynologyChat
{
    internal class SynologyChatErrorResponse
    {
        [JsonProperty("code")]
        public string Code { get; set; } = string.Empty;
        [JsonProperty("errors")]
        public SynologyChatErrorReasonResponse? Errors { get; set; }
    }
}
