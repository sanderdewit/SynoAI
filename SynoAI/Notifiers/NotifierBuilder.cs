using SynoAI.Settings;

namespace SynoAI.Notifiers
{
    /// <summary>
    /// Builds the configured notifiers from the "Notifiers" configuration section. Replaces the notifier
    /// construction that previously lived in the static <c>Config</c> class.
    /// </summary>
    internal static class NotifierBuilder
    {
        /// <summary>
        /// Builds the list of notifiers declared in configuration.
        /// </summary>
        /// <param name="logger">Logger for reporting configuration issues.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="settings">The bound application settings to attach to each notifier.</param>
        public static IReadOnlyList<INotifier> Build(ILogger logger, IConfiguration configuration, AppSettings settings)
        {
            logger.LogInformation("Processing notifier config.");

            List<INotifier> notifiers = new();

            IConfigurationSection section = configuration.GetSection("Notifiers");
            foreach (IConfigurationSection child in section.GetChildren())
            {
                string type = child.GetValue<string>("Type") ?? string.Empty;

                if (!Enum.TryParse(type, out NotifierType notifierType))
                {
                    logger.LogError("Notifier Type '{Type}' is not supported.", type);
                    throw new NotImplementedException(type);
                }

                notifiers.Add(NotifierFactory.Create(notifierType, logger, child, settings));
            }

            return notifiers;
        }
    }
}
