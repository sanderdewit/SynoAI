namespace SynoAI.Notifiers.Webhook
{
    internal class WebhookFactory : NotifierFactory
    {
        public override INotifier Create(ILogger logger, IConfigurationSection section)
        {
            using (logger.BeginScope(nameof(WebhookFactory)))
            {
                logger.LogInformation("Processing Webhook Config");

                string url = section.GetValue<string>("Url") ?? string.Empty;
                AuthorizationMethod authentication = section.GetValue<AuthorizationMethod>("Authorization", AuthorizationMethod.None);
                string? username = section.GetValue<string>("Username");
                string? password = section.GetValue<string>("Password");
                string? token = section.GetValue<string>("Token");
                string imageField = section.GetValue<string>("ImageField", "image") ?? "image";
                string method = section.GetValue<string>("Method", "POST") ?? "POST";
                bool sendImage = section.GetValue<bool>("SendImage", true);
                bool allowInsecureUrl = section.GetValue("AllowInsecureUrl", false);

                Webhook webhook = new()
                {
                    Url = url,
                    Authentication = authentication,
                    Username = username,
                    Password = password,
                    Token = token,
                    SendImage = sendImage,
                    AllowInsecureUrl = allowInsecureUrl
                };

                if (!string.IsNullOrWhiteSpace(imageField))
                {
                    webhook.ImageField = imageField.Trim();
                }

                if (!string.IsNullOrWhiteSpace(method))
                {
                    webhook.Method = method.ToUpper().Trim();
                }

                return webhook;
            }
        }
    }
}
