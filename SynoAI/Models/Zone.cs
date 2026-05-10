namespace SynoAI.Models
{
    /// <summary>
    /// Represents the Zone
    /// </summary>
    public class Zone
    {
        /// <summary>
        /// Gets or sets the start for the exclusion zone.
        /// </summary>
        public Point Start { get; set; } = new();
        /// <summary>
        /// Gets or sets the end for the exclusion zone.
        /// </summary>
        public Point End { get; set; } = new();
        /// <summary>
        /// Gets or sets the overlap mode for the exclusion zone.
        /// </summary>
        public OverlapMode Mode { get; set; }
    }
}