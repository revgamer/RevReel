namespace RevReel.Models
{
    /// <summary>The picture size the video is saved at.</summary>
    public enum OutputSizePreset
    {
        /// <summary>Same size as the screen being recorded.</summary>
        Automatic,
        Hd720,
        FullHd1080,
        Qhd1440,
        Uhd2160
    }

    public static class OutputSizePresetExtensions
    {
        /// <summary>Fixed pixel size, or null for Automatic.</summary>
        public static (int Width, int Height)? PixelSize(this OutputSizePreset preset) => preset switch
        {
            OutputSizePreset.Hd720 => (1280, 720),
            OutputSizePreset.FullHd1080 => (1920, 1080),
            OutputSizePreset.Qhd1440 => (2560, 1440),
            OutputSizePreset.Uhd2160 => (3840, 2160),
            _ => null
        };

        public static string DisplayName(this OutputSizePreset preset) => preset switch
        {
            OutputSizePreset.Hd720 => "720p (1280 x 720)",
            OutputSizePreset.FullHd1080 => "1080p (1920 x 1080)",
            OutputSizePreset.Qhd1440 => "1440p (2560 x 1440)",
            OutputSizePreset.Uhd2160 => "4K (3840 x 2160)",
            _ => "Automatic (same as the screen)"
        };
    }
}
