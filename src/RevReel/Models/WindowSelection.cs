namespace RevReel.Models
{
    /// <summary>One open program window that can be recorded.</summary>
    public sealed record WindowSelection(IntPtr Handle, string Title, string ProcessName)
    {
        /// <summary>For lists and status messages, for example "Untitled - Notepad (notepad)".</summary>
        public string DisplayText =>
            string.IsNullOrEmpty(ProcessName) ? Title : $"{Title} ({ProcessName})";

        /// <summary>A shorter title for the status line.</summary>
        public string ShortTitle => Title.Length <= 40 ? Title : Title.Substring(0, 40) + "...";
    }
}
