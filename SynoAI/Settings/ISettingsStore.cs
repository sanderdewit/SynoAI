namespace SynoAI.Settings
{
    /// <summary>
    /// Reads and persists the application settings. Writes go to a dedicated, editable JSON file that is
    /// layered on top of <c>appsettings.json</c> and reloaded automatically, so saved changes take effect
    /// without a restart.
    /// </summary>
    public interface ISettingsStore
    {
        /// <summary>
        /// The absolute path of the writable settings file.
        /// </summary>
        string FilePath { get; }

        /// <summary>
        /// Gets the current effective settings (the merged, live configuration).
        /// </summary>
        AppSettings Get();

        /// <summary>
        /// Persists the supplied settings to the writable settings file.
        /// </summary>
        Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
    }
}
