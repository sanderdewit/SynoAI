using SynoAI.AIs;
using SynoAI.Models;

namespace SynoAI.Settings
{
    internal class AppSettings
    {
        public string Url { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool AllowInsecureUrl { get; set; }
        public int ApiVersionAuth { get; set; } = 6;
        public int ApiVersionCamera { get; set; } = 9;
        public CameraQuality Quality { get; set; } = CameraQuality.Balanced;
        public DrawMode DrawMode { get; set; } = DrawMode.Matches;
        public bool DrawExclusions { get; set; }
        public int StrokeWidth { get; set; } = 2;
        public string BoxColor { get; set; } = "008000FF";
        public string FontColor { get; set; } = "008000FF";
        public string ExclusionBoxColor { get; set; } = "FF0000FF";
        public string TextBoxColor { get; set; } = "00000000";
        public string Font { get; set; } = "Tahoma";
        public int FontSize { get; set; } = 12;
        public int TextOffsetX { get; set; } = 4;
        public int TextOffsetY { get; set; } = 2;
        public int MinSizeX { get; set; } = 50;
        public int MinSizeY { get; set; } = 50;
        public int MaxSizeX { get; set; }
        public int MaxSizeY { get; set; }
        public int Delay { get; set; }
        public int? DelayAfterSuccess { get; set; }
        public bool LabelBelowBox { get; set; }
        public bool AlternativeLabelling { get; set; }
        public int MaxSnapshots { get; set; } = 1;
        public SaveSnapshotMode SaveOriginalSnapshot { get; set; }
        public int DaysToKeepCaptures { get; set; }
        public string? SynoAIUrl { get; set; }
        public AISettings AI { get; set; } = new();
    }

    internal class AISettings
    {
        public AIType Type { get; set; } = AIType.CodeProjectAIServer;
        public string Url { get; set; } = string.Empty;
        public string Path { get; set; } = "v1/vision/detection";
    }
}
