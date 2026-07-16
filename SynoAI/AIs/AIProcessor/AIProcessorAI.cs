using System.Text.Json;
using SynoAI.App;
using SynoAI.Models;
using SynoAI.Settings;
using System.Diagnostics;
using System.Net.Http.Headers;

namespace SynoAI.AIs.AIProcessor
{
    internal class AIProcessorAI
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AppSettings _settings;

        public AIProcessorAI(IHttpClientFactory httpClientFactory, AppSettings settings)
        {
            _httpClientFactory = httpClientFactory;
            _settings = settings;
        }

        public async Task<IEnumerable<AIPrediction>?> Process(ILogger logger, Camera camera, byte[] image)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            decimal minConfidence = camera.Threshold / 100m;

            var imageContent = new ByteArrayContent(image);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

            MultipartFormDataContent multipartContent = new()
            {
                { imageContent, "image", "image" },
                { new StringContent(minConfidence.ToString()), "min_confidence" } // From face detection example - using JSON with MinConfidence didn't always work
            };

            logger.LogDebug("{CameraName}: {AIType}: POSTing image with minimum confidence of {MinConfidence} ({CameraThreshold}%) to {BaseUrl}/{Path}.",
                camera.Name,
                _settings.AI.Type.ToString(),
                minConfidence,
                camera.Threshold,
                _settings.AI.Url,
                _settings.AI.Path);

            Uri uri = GetUri(_settings.AI.Url, _settings.AI.Path);

            try
            {
                HttpClient httpClient = _httpClientFactory.CreateClient("AI");
                HttpResponseMessage response = await httpClient.PostAsync(uri, multipartContent);
                if (response.IsSuccessStatusCode)
                {
                    AIProcessorResponse aiResponse = await GetResponse(logger, camera, response, _settings.AI.Type);
                    if (aiResponse.Success)
                    {
                        // The AI already filtered by min_confidence server-side; map results directly.
                        IEnumerable<AIPrediction> predictions = aiResponse.Predictions.Select(x => new AIPrediction()
                        {
                            Confidence = x.Confidence * 100,
                            Label = x.Label,
                            MaxX = x.MaxX,
                            MaxY = x.MaxY,
                            MinX = x.MinX,
                            MinY = x.MinY
                        }).ToList();

                        stopwatch.Stop();
                        string aiTypeName = _settings.AI.Type.ToString();
                        logger.LogInformation("{CameraName}: {AIType}: Processed successfully ({ElapsedMilliseconds}ms).",
                            camera.Name,
                            aiTypeName,
                            stopwatch.ElapsedMilliseconds);

                        return predictions;
                    }
                    else
                    {
                        logger.LogWarning("{cameraName}: {AIType}: Failed with unknown error.",
                            camera.Name,
                            _settings.AI.Type);
                    }
                }
                else
                {
                    logger.LogWarning("{cameraName}: {AIType}: Failed to call API with HTTP status code '{responseStatusCode}'.",
                        camera.Name,
                        _settings.AI.Type,
                        response.StatusCode);
                }
            }
            catch (HttpRequestException ex)
            {
                logger.LogError("{camera.Name}: {AIType}: Failed to call API error '{ex}'.",
                    camera.Name,
                    _settings.AI.Type,
                    ex
                    );
            }

            return null;
        }

        /// <summary>
        /// Builds a <see cref="Uri"/> from the provided base and resource.
        /// </summary>
        /// <param name="basePath"></param>
        /// <param name="resourcePath"></param>
        /// <returns>A <see cref="Uri"/> for the combined base and resource.</returns>
        protected static Uri GetUri(string basePath, string resourcePath)
        {
            Uri baseUri = new(basePath);
            return new Uri(baseUri, resourcePath);
        }

        /// <summary>
        /// Fetches the response content and parses it a DeepStack object.
        /// </summary>
        /// <param name="camera"></param>
        /// <param name="message">The message to parse.</param>
        /// <param name="logger"></param>
        /// <param name="aiType"></param>
        /// <returns>A usable object.</returns>
        private static async Task<AIProcessorResponse> GetResponse(ILogger logger, Camera camera, HttpResponseMessage message, AIType aiType)
        {
            string content = await message.Content.ReadAsStringAsync();
            string aiTypeStr = aiType.ToString();
            logger.LogDebug("{cameraName}: {AIType}: Responded with {content}.",
                camera.Name,
                aiTypeStr,
                content);

            return JsonSerializer.Deserialize<AIProcessorResponse>(content, Shared.JsonOptions)!;
        }
    }
}
