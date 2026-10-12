using RevReel.Services;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using RevReel.Models;

namespace RevReel.Views
{
    /// <summary>
    /// A thin red outline drawn just outside the area being recorded, so it is
    /// always clear on screen which part is captured. It ignores the mouse,
    /// never takes the focus, and is hidden from the recording.
    /// </summary>
    public partial class AreaBorderWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int OutlineGap = 4; // pixels drawn outside the recorded area

        private static readonly IntPtr HWND_TOPMOST = new(-1);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);


        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        private readonly AreaSelection _area;

        public AreaBorderWindow(AreaSelection area)
        {
            InitializeComponent();
            _area = area;

            SourceInitialized += (_, _) =>
            {
                IntPtr handle = new WindowInteropHelper(this).EnsureHandle();
                int style = GetWindowLong(handle, GWL_EXSTYLE);
                SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

                // Not in the video. Needs Windows 10 version 2004 or newer.
                // Because the outline sits outside the recorded area, it is not in the video on older versions either.
                CaptureVisibility.ExcludeFromCapture(handle);
                Place();
            };
            Loaded += (_, _) => Place();
        }

        /// <summary>Positions the outline in real pixels (twice, in case a different monitor scale resized it).</summary>
        private void Place()
        {
            IntPtr handle = new WindowInteropHelper(this).EnsureHandle();
            SetWindowPos(handle, HWND_TOPMOST,
                _area.ScreenX - OutlineGap, _area.ScreenY - OutlineGap,
                _area.Width + OutlineGap * 2, _area.Height + OutlineGap * 2,
                SWP_NOACTIVATE);
        }
    }
}
