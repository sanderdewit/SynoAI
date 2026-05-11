using System.Text.Json;

namespace SynoAI.App
{
    internal static class Shared
    {
        public static IHttpClient HttpClient = new HttpClientWrapper();
        public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    }
}
