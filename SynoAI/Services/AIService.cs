using SynoAI.AIs;
using SynoAI.AIs.AIProcessor;
using SynoAI.Models;

namespace SynoAI.Services
{
    internal class AIService : IAIService
    {
        private readonly ILogger<AIService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public AIService(ILogger<AIService> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<IEnumerable<AIPrediction>?> ProcessAsync(Camera camera, byte[] image)
        {
            AIProcessorAI ai = GetAI(_httpClientFactory);
            return await ai.Process(_logger, camera, image);
        }

        private static AIProcessorAI GetAI(IHttpClientFactory httpClientFactory)
        {
            return Config.AI switch
            {
                AIType.DeepStack or AIType.CodeProjectAIServer => new AIProcessorAI(httpClientFactory),
                _ => throw new NotImplementedException(Config.AI.ToString()),
            };
        }
    }
}
