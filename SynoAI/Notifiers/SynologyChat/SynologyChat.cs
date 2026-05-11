using System.Text.Json;
using SynoAI.App;
using SynoAI.Models;

namespace SynoAI.Notifiers.SynologyChat
{
    /// <summary>
    /// Calls a third party API.
    /// </summary>
    internal class SynologyChat : NotifierBase
    {
        /// <summary>
        /// The URL to send the request to including the token.
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Sends a notification to the Webhook.
        /// </summary>
        /// <param name="camera">The camera that triggered the notification.</param>
        /// <param name="notification">The notification data to process.</param>
        /// <param name="logger">A logger.</param>
        public override async Task SendAsync(Camera camera, Notification notification, ILogger logger)
        {
            logger.LogInformation("{cameraName}: SynologyChat: Processing",
                camera.Name);
            using HttpClient client = new();
            IEnumerable<string> foundTypes = notification.FoundTypes;
            string message = GetMessage(camera, notification.FoundTypes, notification.ValidPredictions.ToList());


            var request = new
            {
                text = message,
                file_url = new Uri(new Uri(Config.Url), new Uri($"Image/{camera.Name}/{notification.ProcessedImage.FileName}", UriKind.Relative))
            };

            string requestJson = JsonSerializer.Serialize(request, Shared.JsonOptions);
            Dictionary<string, string> payload = new()
                {
                    { "payload", requestJson },
                };

            using FormUrlEncodedContent content = new(payload);
            content.Headers.Clear();
            content.Headers.Add("Content-Type", "application/x-www-form-urlencoded");

            logger.LogInformation("{CameraName}: SynologyChat: POSTing message.",
                camera.Name);

            HttpResponseMessage response = await client.PostAsync(Url, content);
            if (response.IsSuccessStatusCode)
            {
                // Check that it's actually successful, because Synology like to make things awkward
                string responseString = await response.Content.ReadAsStringAsync();
                SynologyChatResponse actualResponse = JsonSerializer.Deserialize<SynologyChatResponse>(responseString, Shared.JsonOptions)!;
                if (actualResponse.Success)
                {
                    logger.LogInformation("{cameraName}: SynologyChat: Success.",
                        camera.Name);
                }
                else
                {
                    string errorCode = actualResponse.Error?.Code ?? string.Empty;
                    string errorErrors = actualResponse.Error?.Errors?.ToString() ?? string.Empty;
                    logger.LogInformation("{CameraName}: SynologyChat: Failed with error '{ErrorCode}': {ErrorErrors}.",
                        camera.Name,
                        errorCode,
                        errorErrors);
                }
            }
            else
            {
                logger.LogWarning("{cameraName}: SynologyChat: The end point responded with HTTP status code '{responseStatusCode}'.",
                    camera.Name,
                    response.StatusCode);
            }
        }
    }
}
