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

        /// <summary>Which part of the screen(s) to record.</summary>
        public RecordingSourceKind SourceKind { get; set; } = RecordingSourceKind.Monitor;

        /// <summary>Windows device name of the chosen monitor (for example \\.\DISPLAY2). Empty = the main monitor.</summary>
        public string MonitorDeviceName { get; set; } = "";

        /// <summary>
        /// Keeps a short rolling set of pictures while recording so replay
        /// GIFs can be made. Uses some extra disk space and CPU.
        /// </summary>
        public bool ReplayGifEnabled { get; set; } = true;

        public static string DefaultOutputFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "RevReel");
    }
}
