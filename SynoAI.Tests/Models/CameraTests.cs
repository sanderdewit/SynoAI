using SynoAI.Models;
using SynoAI.Settings;
using Xunit;

namespace SynoAI.Tests.Models
{
    public class CameraTests
    {
        // Global defaults used only for the fall-back branches; the "WhenSet" tests set explicit
        // camera values so these are not consulted.
        private static readonly AppSettings Defaults = new();

        [Fact]
        public void GetMinSizeX_WhenSet_ReturnsCameraValue()
        {
            Camera camera = new() { MinSizeX = 123 };
            Assert.Equal(123, camera.GetMinSizeX(Defaults));
        }

        [Fact]
        public void GetMinSizeX_WhenNotSet_FallsBackToGlobalDefault()
        {
            Camera camera = new() { MinSizeX = null };
            AppSettings settings = new() { MinSizeX = 77 };
            Assert.Equal(77, camera.GetMinSizeX(settings));
        }

        [Fact]
        public void GetMinSizeY_WhenSet_ReturnsCameraValue()
        {
            Camera camera = new() { MinSizeY = 234 };
            Assert.Equal(234, camera.GetMinSizeY(Defaults));
        }

        [Fact]
        public void GetMaxSizeX_WhenSet_ReturnsCameraValue()
        {
            Camera camera = new() { MaxSizeX = 345 };
            Assert.Equal(345, camera.GetMaxSizeX(Defaults));
        }

        [Fact]
        public void GetMaxSizeX_WhenNotSet_FallsBackToGlobalMaxValueDefault()
        {
            Camera camera = new() { MaxSizeX = null };
            Assert.Equal(int.MaxValue, camera.GetMaxSizeX(Defaults));
        }

        [Fact]
        public void GetMaxSizeY_WhenSet_ReturnsCameraValue()
        {
            Camera camera = new() { MaxSizeY = 456 };
            Assert.Equal(456, camera.GetMaxSizeY(Defaults));
        }

        [Fact]
        public void GetMaxSnapshots_WhenSet_ReturnsCameraValue()
        {
            Camera camera = new() { MaxSnapshots = 7 };
            Assert.Equal(7, camera.GetMaxSnapshots(Defaults));
        }

        [Fact]
        public void GetDelay_WhenSet_ReturnsCameraValue()
        {
            Camera camera = new() { Delay = 5000 };
            Assert.Equal(5000, camera.GetDelay(Defaults));
        }

        [Fact]
        public void GetDelayAfterSuccess_WhenSet_ReturnsCameraValue()
        {
            Camera camera = new() { DelayAfterSuccess = 9000, Delay = 5000 };
            Assert.Equal(9000, camera.GetDelayAfterSuccess(Defaults));
        }

        [Fact]
        public void GetDelayAfterSuccess_WhenNotSet_FallsBackToDelay()
        {
            Camera camera = new() { DelayAfterSuccess = null, Delay = 5000 };
            Assert.Equal(camera.GetDelay(Defaults), camera.GetDelayAfterSuccess(Defaults));
        }

        [Fact]
        public void GetDelayAfterSuccess_WhenCameraAndGlobalUnset_FallsBackToGlobalDelay()
        {
            Camera camera = new() { DelayAfterSuccess = null, Delay = null };
            AppSettings settings = new() { Delay = 1234, DelayAfterSuccess = null };
            Assert.Equal(1234, camera.GetDelayAfterSuccess(settings));
        }

        [Fact]
        public void ToString_ReturnsCameraName()
        {
            Camera camera = new() { Name = "Driveway" };
            Assert.Equal("Driveway", camera.ToString());
        }
    }
}
