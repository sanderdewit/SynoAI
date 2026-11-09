namespace SynoAI
{
    internal static class Constants
    {
        public const string DIRECTORY_CAPTURES = "Captures";

        /// <summary>
        /// JPEG quality for every image SynoAI encodes: rotated snapshots sent to the AI and the dressed
        /// images saved to disk and sent to notifiers. 90 is visually lossless for this purpose and
        /// much smaller and faster to encode than 100.
        /// </summary>
        public const int JPEG_QUALITY = 90;
    }
}