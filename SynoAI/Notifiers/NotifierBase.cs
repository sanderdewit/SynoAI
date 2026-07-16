using System.Text.Json;
using SynoAI.App;
using SynoAI.Models;
using SynoAI.Settings;
using System.Dynamic;

namespace SynoAI.Notifiers
{
    internal abstract class NotifierBase : INotifier
    {
        public AppSettings Settings { get; set; } = new();
        public IEnumerable<string>? Cameras { get; set; }
        public IEnumerable<string>? Types { get; set; }


        public virtual Task InitializeAsync(ILogger logger) { return Task.CompletedTask; }

        public abstract Task SendAsync(Camera camera, Notification notification, ILogger logger);

        public virtual Task CleanupAsync(ILogger logger) { return Task.CompletedTask; }

        protected string GetMessage(Camera camera, IEnumerable<string> foundTypes, List<AIPrediction> predictions, string? errorMessage = null)
        {
            string result;

            if (Settings.AlternativeLabelling && Settings.DrawMode == DrawMode.Matches)
            {
                // Defaulting into a generic label type
                string typeLabel = foundTypes.Count() == 1 ? foundTypes.First() : "objects";

                if (foundTypes.Count() > 1)
                {
                    // Several objects detected
                    result = $"{camera.Name}: {foundTypes.Count()} {typeLabel}s\n{string.Join("\n", foundTypes)}";
                }
                else
                {
                    // Just one object detected
                    result = $"{camera.Name}: {foundTypes.First()}";
                }
            }
            else
            {
                // Standard (old) labeling
                result = $"Motion detected on {camera.Name}\n\nDetected {foundTypes.Count()} objects:\n";
            }

            // Include prediction confidence for each detected object
            foreach (var prediction in predictions)
            {
                result += $"\n{prediction.Label}: {prediction.Confidence}%";
            }

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                result += $"\nAn error occurred during the creation of the notification: {errorMessage}";
            }

            return result;
        }

        protected string? GetImageUrl(Camera camera, Notification notification)
        {
            if (Settings.SynoAIUrl == null)
            {
                return null;
            }

            UriBuilder builder = new(Settings.SynoAIUrl);
            builder.Path += $"{camera.Name}/{notification.ProcessedImage.FileName}";

            return builder.Uri.ToString();
        }

        /// <summary>
        /// Generates a JSON representation of the notification.
        /// </summary>
        protected string GenerateJSON(Camera camera, Notification notification, bool sendImage)
        {
            dynamic jsonObject = new ExpandoObject();

            jsonObject.camera = camera.Name;
            jsonObject.foundTypes = notification.FoundTypes;

            List<AIPrediction> validPredictions = notification.ValidPredictions.ToList();
            jsonObject.predictions = validPredictions.Select(prediction => new
            {
                prediction.Confidence,
                prediction.Label,
                // Add other properties as needed
            }).ToList();

            jsonObject.message = GetMessage(camera, notification.FoundTypes, validPredictions);

            if (sendImage)
            {
                jsonObject.image = ToBase64String(notification.ProcessedImage.GetReadonlyStream());
            }

            string? imageUrl = GetImageUrl(camera, notification);
            if (imageUrl != null)
            {
                jsonObject.imageUrl = imageUrl;
            }

            return JsonSerializer.Serialize(jsonObject, Shared.JsonOptions);
        }


        /// <summary>
        /// Returns FileStream data as a base64-encoded string
        /// </summary>
        private static string ToBase64String(FileStream fileStream)
        {
            byte[] buffer = new byte[fileStream.Length];
            fileStream.ReadExactly(buffer, 0, (int)fileStream.Length);

            return Convert.ToBase64String(buffer);
        }

        /// <summary>
        /// Fetches the response content and parses it as the specified type.
        /// </summary>
        /// <param name="message">The message to parse.</param>
        /// <returns>A usable object.</returns>
        protected static async Task<T> GetResponse<T>(HttpResponseMessage message)
        {
            string content = await message.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(content, Shared.JsonOptions)!;
        }
    }
}