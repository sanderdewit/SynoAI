using SynoAI.Models;

namespace SynoAI.Services
{
    /// <summary>
    /// Pure, side-effect free helpers that decide whether an <see cref="AIPrediction"/> should be
    /// considered a valid object of interest for a given <see cref="Camera"/>. Kept separate from
    /// <see cref="CameraProcessingService"/> so the geometry can be unit tested in isolation.
    /// </summary>
    internal static class PredictionFilter
    {
        /// <summary>
        /// Determines whether the prediction's label is one of the types the camera is interested in.
        /// A camera with no configured types is interested in everything.
        /// </summary>
        public static bool IsTypeOfInterest(Camera camera, AIPrediction prediction)
        {
            return camera.Types == null || camera.Types.Contains(prediction.Label, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether the prediction is at least as large as the configured minimum size.
        /// </summary>
        public static bool MeetsMinimumSize(AIPrediction prediction, int minSizeX, int minSizeY)
        {
            return prediction.SizeX >= minSizeX && prediction.SizeY >= minSizeY;
        }

        /// <summary>
        /// Determines whether the prediction is no larger than the configured maximum size. A maximum
        /// of <see cref="int.MaxValue"/> is treated as "no maximum" for that axis.
        /// </summary>
        public static bool MeetsMaximumSize(AIPrediction prediction, int maxSizeX, int maxSizeY)
        {
            if (maxSizeX < int.MaxValue && prediction.SizeX > maxSizeX)
                return false;
            if (maxSizeY < int.MaxValue && prediction.SizeY > maxSizeY)
                return false;
            return true;
        }

        /// <summary>
        /// Returns the first exclusion zone that the prediction falls within (according to the zone's
        /// <see cref="OverlapMode"/>), or <c>null</c> if the prediction is not excluded by any zone.
        /// </summary>
        public static Zone? FindExclusionZone(IEnumerable<Zone>? exclusions, AIPrediction prediction)
        {
            if (exclusions == null)
                return null;

            foreach (Zone exclusion in exclusions)
            {
                if (IsExcludedBy(exclusion, prediction))
                    return exclusion;
            }
            return null;
        }

        /// <summary>
        /// Determines whether a single exclusion zone excludes the prediction. The zone corners are
        /// normalised, so the start point does not have to be the top-left.
        /// </summary>
        public static bool IsExcludedBy(Zone exclusion, AIPrediction prediction)
        {
            int startX = Math.Min(exclusion.Start.X, exclusion.End.X);
            int startY = Math.Min(exclusion.Start.Y, exclusion.End.Y);
            int endX = Math.Max(exclusion.Start.X, exclusion.End.X);
            int endY = Math.Max(exclusion.Start.Y, exclusion.End.Y);

            return exclusion.Mode == OverlapMode.Contains
                ? startX <= prediction.MinX && startY <= prediction.MinY && endX >= prediction.MaxX && endY >= prediction.MaxY
                : prediction.MinX < endX && prediction.MaxX > startX && prediction.MinY < endY && prediction.MaxY > startY;
        }
    }
}
