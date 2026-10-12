using System.Runtime.InteropServices;

namespace RevReel.Services
{
    /// <summary>Keeps RevReel's own windows (pop-ups, outlines, the main window) out of recordings and screenshots.</summary>
    public static class CaptureVisibility
    {
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        /// <summary>
        /// Hides the window from screen capture. It stays visible on screen.
        /// Needs Windows 10 version 2004 or newer; on older versions the window is simply not hidden.
        /// </summary>
        public static void ExcludeFromCapture(IntPtr windowHandle) => SetExcluded(windowHandle, true);

        /// <summary>Hides the window from capture (true) or shows it in captures again (false).</summary>
        public static void SetExcluded(IntPtr windowHandle, bool excluded)
        {
            try
            {
                SetWindowDisplayAffinity(windowHandle, excluded ? WDA_EXCLUDEFROMCAPTURE : 0u);
            }
            catch (Exception)
            {
                // Not supported here. Nothing useful to do.
            }
        }
    }
}
