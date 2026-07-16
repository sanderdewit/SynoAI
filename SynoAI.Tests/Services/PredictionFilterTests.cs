using SynoAI.Models;
using SynoAI.Services;
using Xunit;

namespace SynoAI.Tests.Services
{
    public class PredictionFilterTests
    {
        private static AIPrediction Prediction(string label = "person", int minX = 100, int minY = 100, int maxX = 200, int maxY = 200)
            => new() { Label = label, MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY };

        private static Zone Zone(int startX, int startY, int endX, int endY, OverlapMode mode)
            => new()
            {
                Start = new Point { X = startX, Y = startY },
                End = new Point { X = endX, Y = endY },
                Mode = mode
            };

        #region IsTypeOfInterest

        [Fact]
        public void IsTypeOfInterest_WhenTypesNull_ReturnsTrue()
        {
            Camera camera = new() { Types = null };
            Assert.True(PredictionFilter.IsTypeOfInterest(camera, Prediction("person")));
        }

        [Fact]
        public void IsTypeOfInterest_WhenLabelInList_ReturnsTrue()
        {
            Camera camera = new() { Types = new[] { "person", "car" } };
            Assert.True(PredictionFilter.IsTypeOfInterest(camera, Prediction("car")));
        }

        [Fact]
        public void IsTypeOfInterest_IsCaseInsensitive()
        {
            Camera camera = new() { Types = new[] { "Person" } };
            Assert.True(PredictionFilter.IsTypeOfInterest(camera, Prediction("person")));
        }

        [Fact]
        public void IsTypeOfInterest_WhenLabelNotInList_ReturnsFalse()
        {
            Camera camera = new() { Types = new[] { "person" } };
            Assert.False(PredictionFilter.IsTypeOfInterest(camera, Prediction("dog")));
        }

        #endregion

        #region MeetsMinimumSize

        [Fact]
        public void MeetsMinimumSize_WhenExactlyAtMinimum_ReturnsTrue()
        {
            // Prediction is 100x100.
            Assert.True(PredictionFilter.MeetsMinimumSize(Prediction(), 100, 100));
        }

        [Fact]
        public void MeetsMinimumSize_WhenNarrowerThanMinimum_ReturnsFalse()
        {
            Assert.False(PredictionFilter.MeetsMinimumSize(Prediction(), 101, 100));
        }

        [Fact]
        public void MeetsMinimumSize_WhenShorterThanMinimum_ReturnsFalse()
        {
            Assert.False(PredictionFilter.MeetsMinimumSize(Prediction(), 100, 101));
        }

        #endregion

        #region MeetsMaximumSize

        [Fact]
        public void MeetsMaximumSize_WhenExactlyAtMaximum_ReturnsTrue()
        {
            Assert.True(PredictionFilter.MeetsMaximumSize(Prediction(), 100, 100));
        }

        [Fact]
        public void MeetsMaximumSize_WhenWiderThanMaximum_ReturnsFalse()
        {
            Assert.False(PredictionFilter.MeetsMaximumSize(Prediction(), 99, 100));
        }

        [Fact]
        public void MeetsMaximumSize_WhenTallerThanMaximum_ReturnsFalse()
        {
            Assert.False(PredictionFilter.MeetsMaximumSize(Prediction(), 100, 99));
        }

        [Fact]
        public void MeetsMaximumSize_WhenMaximumIsIntMaxValue_TreatsAsNoLimit()
        {
            AIPrediction huge = Prediction(minX: 0, minY: 0, maxX: 100_000, maxY: 100_000);
            Assert.True(PredictionFilter.MeetsMaximumSize(huge, int.MaxValue, int.MaxValue));
        }

        #endregion

        #region FindExclusionZone

        [Fact]
        public void FindExclusionZone_WhenExclusionsNull_ReturnsNull()
        {
            Assert.Null(PredictionFilter.FindExclusionZone(null, Prediction()));
        }

        [Fact]
        public void FindExclusionZone_WhenNoZones_ReturnsNull()
        {
            Assert.Null(PredictionFilter.FindExclusionZone(new List<Zone>(), Prediction()));
        }

        [Fact]
        public void FindExclusionZone_ReturnsFirstMatchingZone()
        {
            Zone missing = Zone(0, 0, 10, 10, OverlapMode.Intersect);
            Zone matching = Zone(150, 150, 300, 300, OverlapMode.Intersect);
            List<Zone> zones = new() { missing, matching };

            Assert.Same(matching, PredictionFilter.FindExclusionZone(zones, Prediction()));
        }

        #endregion

        #region IsExcludedBy - Contains mode

        [Fact]
        public void IsExcludedBy_Contains_WhenPredictionFullyInside_ReturnsTrue()
        {
            // Prediction 100..200 fully inside 50..250.
            Zone zone = Zone(50, 50, 250, 250, OverlapMode.Contains);
            Assert.True(PredictionFilter.IsExcludedBy(zone, Prediction()));
        }

        [Fact]
        public void IsExcludedBy_Contains_WhenExactlyMatchingBounds_ReturnsTrue()
        {
            Zone zone = Zone(100, 100, 200, 200, OverlapMode.Contains);
            Assert.True(PredictionFilter.IsExcludedBy(zone, Prediction()));
        }

        [Fact]
        public void IsExcludedBy_Contains_WhenPartiallyOutside_ReturnsFalse()
        {
            // Zone does not reach the prediction's right/bottom edge.
            Zone zone = Zone(50, 50, 150, 150, OverlapMode.Contains);
            Assert.False(PredictionFilter.IsExcludedBy(zone, Prediction()));
        }

        [Fact]
        public void IsExcludedBy_Contains_NormalisesInvertedCorners()
        {
            // Same zone as the "fully inside" test but with start/end swapped.
            Zone zone = Zone(250, 250, 50, 50, OverlapMode.Contains);
            Assert.True(PredictionFilter.IsExcludedBy(zone, Prediction()));
        }

        #endregion

        #region IsExcludedBy - Intersect mode

        [Fact]
        public void IsExcludedBy_Intersect_WhenOverlapping_ReturnsTrue()
        {
            Zone zone = Zone(150, 150, 300, 300, OverlapMode.Intersect);
            Assert.True(PredictionFilter.IsExcludedBy(zone, Prediction()));
        }

        [Fact]
        public void IsExcludedBy_Intersect_WhenCompletelyOutside_ReturnsFalse()
        {
            Zone zone = Zone(300, 300, 400, 400, OverlapMode.Intersect);
            Assert.False(PredictionFilter.IsExcludedBy(zone, Prediction()));
        }

        [Fact]
        public void IsExcludedBy_Intersect_WhenOnlyTouchingEdge_ReturnsFalse()
        {
            // Zone starts exactly at the prediction's right edge (200); edge-touching is not an overlap.
            Zone zone = Zone(200, 100, 300, 200, OverlapMode.Intersect);
            Assert.False(PredictionFilter.IsExcludedBy(zone, Prediction()));
        }

        [Fact]
        public void IsExcludedBy_Intersect_NormalisesInvertedCorners()
        {
            Zone zone = Zone(300, 300, 150, 150, OverlapMode.Intersect);
            Assert.True(PredictionFilter.IsExcludedBy(zone, Prediction()));
        }

        #endregion
    }
}
