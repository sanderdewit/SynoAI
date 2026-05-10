namespace SynoAI.Notifiers.Telegram
{
    internal class TelegramFactory : NotifierFactory
    {
        public override INotifier Create(ILogger logger, IConfigurationSection section)
        {
            using (logger.BeginScope(nameof(TelegramFactory)))
            {
                logger.LogInformation("Processing Telegram Config");

                string token = section.GetValue<string>("Token") ?? string.Empty;
                string chatId = section.GetValue<string>("ChatID") ?? string.Empty;
                string? photoBaseURL = section.GetValue<string>("PhotoBaseURL");

                return new Telegram()
                {
                    ChatID = chatId,
                    Token = token,
                    PhotoBaseURL = photoBaseURL
                };
            }
        }
    }
}
