using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using RevReel.Models;

namespace RevReel.Services
{
    /// <summary>Lists the open windows and finds where a window is on the screen, using Windows directly.</summary>
    public static class WindowService
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const int DWMWA_CLOAKED = 14;

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out RECT rect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out RECT value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

        /// <summary>Open, visible program windows with a title, in the order Windows lists them (front first).</summary>
        public static IReadOnlyList<WindowSelection> GetOpenWindows()
        {
            var result = new List<WindowSelection>();
            uint ownProcess = (uint)Environment.ProcessId;

            try
            {
                EnumWindows((window, _) =>
                {
                    if (!IsRecordable(window))
                    {
                        return true;
                    }

                    GetWindowThreadProcessId(window, out uint processId);
                    if (processId == ownProcess)
                    {
                        return true; // not RevReel's own windows
                    }

                    int length = GetWindowTextLength(window);
                    if (length <= 0)
                    {
                        return true;
                    }

                    var title = new StringBuilder(length + 1);
                    GetWindowText(window, title, title.Capacity);
                    string text = title.ToString().Trim();
                    if (text.Length == 0 || text == "Program Manager")
                    {
                        return true;
                    }

                    result.Add(new WindowSelection(window, text, ProcessNameOf(processId)));
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception)
            {
                // Return what was found so far.
            }

            return result;
        }

        /// <summary>True if the window still exists and can be recorded.</summary>
        public static bool IsStillOpen(IntPtr window) => IsWindow(window) && IsRecordable(window);

        /// <summary>Where the window is on the combined desktop, in real pixels, without the invisible resize border.</summary>
        public static Rectangle? GetBounds(IntPtr window)
        {
            if (!IsWindow(window))
            {
                return null;
            }

            RECT rect;
            if (DwmGetWindowAttribute(window, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>()) != 0
                && !GetWindowRect(window, out rect))
            {
                return null;
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            return width > 0 && height > 0 ? new Rectangle(rect.Left, rect.Top, width, height) : null;
        }

        /// <summary>The monitor that holds the middle of the window, or null if none does.</summary>
        public static MonitorInfo? MonitorOf(IntPtr window, IReadOnlyList<MonitorInfo> monitors)
        {
            Rectangle? bounds = GetBounds(window);
            if (bounds == null)
            {
                return null;
            }

            int centerX = bounds.Value.X + bounds.Value.Width / 2;
            int centerY = bounds.Value.Y + bounds.Value.Height / 2;
            return monitors.FirstOrDefault(m =>
                centerX >= m.X && centerX < m.X + m.Width && centerY >= m.Y && centerY < m.Y + m.Height);
        }

        private static bool IsRecordable(IntPtr window)
        {
            if (!IsWindowVisible(window) || IsIconic(window))
            {
                return false;
            }

            // Tool windows (floating palettes) are skipped unless the program marks them as app windows.
            int style = GetWindowLong(window, GWL_EXSTYLE);
            if ((style & WS_EX_TOOLWINDOW) != 0 && (style & WS_EX_APPWINDOW) == 0)
            {
                return false;
            }

            // Windows on other virtual desktops and hidden Store app shells are "cloaked".
            if (DwmGetWindowAttribute(window, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return false;
            }

            return true;
        }

        private static string ProcessNameOf(uint processId)
        {
            try
            {
                using Process process = Process.GetProcessById((int)processId);
                return process.ProcessName;
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
