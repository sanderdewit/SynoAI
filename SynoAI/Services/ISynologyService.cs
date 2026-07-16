namespace SynoAI.Services
{
    /// <summary>
    /// Handles authentication against the Synology Surveillance Station API and snapshot retrieval.
    /// </summary>
    public interface ISynologyService
    {
        /// <summary>
        /// Authenticates with the Synology API and resolves the configured cameras.
        /// </summary>
        Task InitialiseAsync();

        /// <summary>
        /// Takes a snapshot from the named camera, returning the raw image bytes, or <c>null</c> on failure.
        /// </summary>
        /// <param name="cameraName">The name of the camera to snapshot.</param>
        Task<byte[]?> TakeSnapshotAsync(string cameraName);
    }
}
