using System.IO;

namespace RevReel.Models
{
    /// <summary>Everything the user can change in the Option menu. Saved between runs.</summary>
    public class AppSettings
    {
        public string OutputFolder { get; set; } = DefaultOutputFolder;

        /// <summary>Frames per second. Only 30 or 60 are offered.</summary>
        public int Framerate { get; set; } = 30;

        public OutputSizePreset OutputSize { get; set; } = OutputSizePreset.Automatic;

        public bool AlwaysOnTop { get; set; } = false;

        public static string DefaultOutputFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "RevReel");
    }
}
