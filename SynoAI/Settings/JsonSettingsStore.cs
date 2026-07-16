using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SynoAI.Settings
{
    /// <summary>
    /// <see cref="ISettingsStore"/> implementation that persists settings to a JSON file
    /// (<see cref="FileName"/>) in the content root. That file is registered as a reloadable configuration
    /// source, so writing it updates <see cref="IOptionsMonitor{TOptions}"/> consumers live.
    /// </summary>
    internal sealed class JsonSettingsStore : ISettingsStore
    {
        /// <summary>The name of the writable settings file, layered on top of appsettings.json.</summary>
        public const string FileName = "synoai.settings.json";

        // Serialise with the same key names the configuration binder expects (e.g. JsonPropertyName on
        // AppSettings maps Username -> "User"), so the file round-trips through configuration binding.
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly SemaphoreSlim WriteLock = new(1, 1);

        private readonly IOptionsMonitor<AppSettings> _options;

        public JsonSettingsStore(IOptionsMonitor<AppSettings> options, string filePath)
        {
            _options = options;
            FilePath = filePath;
        }

        /// <inheritdoc />
        public string FilePath { get; }

        /// <inheritdoc />
        public AppSettings Get() => _options.CurrentValue;

        /// <inheritdoc />
        public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);

            string json = JsonSerializer.Serialize(settings, JsonOptions);

            // Serialise writes so a save can't interleave with another and corrupt the file.
            await WriteLock.WaitAsync(cancellationToken);
            try
            {
                await File.WriteAllTextAsync(FilePath, json, cancellationToken);
            }
            finally
            {
                WriteLock.Release();
            }
        }
    }
}
