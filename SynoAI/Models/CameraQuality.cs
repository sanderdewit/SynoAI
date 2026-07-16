namespace SynoAI.Models
{
    /// <summary>
    /// The profile/quality of the image requested from the Synology camera.
    /// </summary>
    public enum CameraQuality
    {
        /// <summary>High quality.</summary>
        High = 0,
        /// <summary>Balanced quality.</summary>
        Balanced = 1,
        /// <summary>Low bandwidth.</summary>
        Low = 2
    }
}