using SynoAI.Models;

namespace SynoAI.Services
{
    public interface ICameraProcessingService
    {
        /// <summary>
        /// Runs the full motion-processing pipeline for a camera and returns whether a valid prediction was found and notified.
        /// </summary>
        Task<bool> ProcessAsync(string id, Camera camera);
    }
}
