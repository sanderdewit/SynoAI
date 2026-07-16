using SynoAI.Models;
using Xunit;

namespace SynoAI.Tests.Models
{
    public class AIPredictionTests
    {
        [Theory]
        [InlineData(10, 40, 30)]
        [InlineData(0, 100, 100)]
        [InlineData(50, 50, 0)]
        public void SizeX_IsTheDifferenceBetweenMaxAndMinX(int minX, int maxX, int expected)
        {
            AIPrediction prediction = new() { MinX = minX, MaxX = maxX };
            Assert.Equal(expected, prediction.SizeX);
        }

        [Theory]
        [InlineData(10, 40, 30)]
        [InlineData(0, 100, 100)]
        [InlineData(50, 50, 0)]
        public void SizeY_IsTheDifferenceBetweenMaxAndMinY(int minY, int maxY, int expected)
        {
            AIPrediction prediction = new() { MinY = minY, MaxY = maxY };
            Assert.Equal(expected, prediction.SizeY);
        }

        [Fact]
        public void Label_DefaultsToEmptyString()
        {
            AIPrediction prediction = new();
            Assert.Equal(string.Empty, prediction.Label);
        }
    }
}
