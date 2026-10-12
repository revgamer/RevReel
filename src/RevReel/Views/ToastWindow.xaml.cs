using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using RevReel.Models;
using RevReel.Services;

namespace RevReel.Views
{
    public partial class ToastWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private static readonly IntPtr HWND_TOPMOST = new(-1);

        private MonitorInfo? _monitor;


        public ToastWindow()
        {
            InitializeComponent();

            IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();

            // Never takes the focus, ignores the mouse, stays out of Alt+Tab.
            int style = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

            // Hidden from screen capture, so the pop-up is not in the video.
            // Needs Windows 10 version 2004 or newer; on older versions it is simply not hidden.
            CaptureVisibility.ExcludeFromCapture(hwnd);

            SizeChanged += (_, _) => PlaceInCorner();
            Loaded += (_, _) => PlaceInCorner();
        }

        public void SetMessage(string text, bool isError, MonitorInfo? monitor)
        {
            _monitor = monitor;
            MessageText.Text = text;
            StatusDot.Fill = (Brush)FindResource(isError ? "RecordBrush" : "AccentBrush");
            PlaceInCorner();
        }

        /// <summary>Top-right corner of the chosen monitor (the main one if none was chosen), in real pixels.</summary>
        private void PlaceInCorner()
        {
            MonitorInfo? monitor = _monitor ?? MonitorService.GetAll().FirstOrDefault(m => m.IsPrimary);
            if (monitor == null)
            {
                return;
            }

            IntPtr handle = new WindowInteropHelper(this).EnsureHandle();
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            int widthPx = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
            int x = monitor.X + monitor.Width - widthPx - 16;
            int y = monitor.Y + 16;
            SetWindowPos(handle, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }
}
