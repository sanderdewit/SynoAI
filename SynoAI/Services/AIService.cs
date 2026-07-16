using Microsoft.Extensions.Options;
using SynoAI.AIs.AIProcessor;
using SynoAI.Models;
using SynoAI.Settings;

namespace SynoAI.Services
{
    internal class AIService : IAIService
    {
        private readonly ILogger<AIService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptionsMonitor<AppSettings> _options;

        public AIService(ILogger<AIService> logger, IHttpClientFactory httpClientFactory, IOptionsMonitor<AppSettings> options)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _options = options;
        }

        public async Task<IEnumerable<AIPrediction>?> ProcessAsync(Camera camera, byte[] image)
        {
            AIProcessorAI ai = new(_httpClientFactory, _options.CurrentValue);
            return await ai.Process(_logger, camera, image);
        }
    }
}
