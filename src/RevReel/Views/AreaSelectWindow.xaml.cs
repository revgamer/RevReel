using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using RevReel.Models;
using RevReel.Services;

namespace RevReel.Views
{
    /// <summary>
    /// Opens one dimmed overlay on every monitor so the user can drag out the
    /// area to record on any of them. Returns null if cancelled.
    /// An area stays inside one monitor.
    /// </summary>
    public static class AreaSelector
    {
        /// <summary>
        /// All overlays are ordinary (not modal) windows. A modal window would
        /// disable the overlays on the other monitors, so only one monitor
        /// would accept the mouse.
        /// </summary>
        public static async Task<AreaSelection?> SelectAsync()
        {
            IReadOnlyList<MonitorInfo> monitors = MonitorService.GetAll();
            if (monitors.Count == 0)
            {
                return null;
            }

            var coordinator = new AreaSelectCoordinator();
            var windows = monitors.Select(m => new AreaSelectWindow(m, coordinator)).ToList();
            foreach (AreaSelectWindow w in windows)
            {
                coordinator.Register(w);
            }

            foreach (AreaSelectWindow w in windows)
            {
                w.Show();
            }

            // Keyboard focus starts on the main monitor.
            (windows.FirstOrDefault(w => w.Monitor.IsPrimary) ?? windows[0]).Activate();

            return await coordinator.Completion;
        }
    }

    /// <summary>Keeps the overlays on all monitors working as one: only one has a selection, and any can finish.</summary>
    public sealed class AreaSelectCoordinator
    {
        private readonly List<AreaSelectWindow> _windows = new();
        private readonly TaskCompletionSource<AreaSelection?> _done = new();
        private bool _finished;

        /// <summary>Completes when the user has chosen an area or cancelled.</summary>
        public Task<AreaSelection?> Completion => _done.Task;

        public AreaSelection? Result { get; private set; }

        public void Register(AreaSelectWindow window) => _windows.Add(window);

        /// <summary>A drag started in <paramref name="source"/>, so clear any selection on the other monitors.</summary>
        public void SelectionStarted(AreaSelectWindow source)
        {
            foreach (AreaSelectWindow w in _windows.Where(w => w != source))
            {
                w.ClearSelection();
            }
        }

        public void Finish(AreaSelection? result)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            Result = result;
            foreach (AreaSelectWindow w in _windows.ToList())
            {
                w.Close();
            }

            _done.TrySetResult(result);
        }
    }

    public partial class AreaSelectWindow : Window
    {
        private const double MinimumSize = 40;   // device-independent pixels
        private const double KeyboardStep = 10;
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        private readonly AreaSelectCoordinator _coordinator;
        private Point? _dragStart;
        private Rect _selection = Rect.Empty;

        public MonitorInfo Monitor { get; }

        public AreaSelectWindow(MonitorInfo monitor, AreaSelectCoordinator coordinator)
        {
            InitializeComponent();
            Monitor = monitor;
            _coordinator = coordinator;
            MonitorLabel.Text = $"{monitor.DisplayText}";

            // Closing any overlay another way (for example Alt+F4) cancels the whole selection.
            Closed += (_, _) => _coordinator.Finish(null);
            SourceInitialized += (_, _) => PlaceOnMonitor();
            Loaded += (_, _) =>
            {
                // Placed a second time: moving to a monitor with a different
                // scale can resize the window, so this puts it back exactly.
                PlaceOnMonitor();
                if (Monitor.IsPrimary)
                {
                    Activate();
                    Focus();
                }
            };
        }

        /// <summary>Covers the whole monitor, using real pixels.</summary>
        private void PlaceOnMonitor()
        {
            IntPtr handle = new WindowInteropHelper(this).EnsureHandle();
            SetWindowPos(handle, HWND_TOPMOST, Monitor.X, Monitor.Y, Monitor.Width, Monitor.Height, SWP_SHOWWINDOW);
        }

        public void ClearSelection()
        {
            _selection = Rect.Empty;
            _dragStart = null;
            UpdateVisual();
        }

        // -------- Mouse --------

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _coordinator.SelectionStarted(this);
            _dragStart = Clamp(e.GetPosition(this));
            _selection = new Rect(_dragStart.Value, new Size(0, 0));
            CaptureMouse();
            UpdateVisual();
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragStart == null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            _selection = new Rect(_dragStart.Value, Clamp(e.GetPosition(this)));
            UpdateVisual();
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragStart = null;
            ReleaseMouseCapture();
            UpdateVisual();
        }

        private Point Clamp(Point p) =>
            new(Math.Min(Math.Max(0, p.X), ActualWidth), Math.Min(Math.Max(0, p.Y), ActualHeight));

        // -------- Keyboard --------

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _coordinator.Finish(null);
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (HasValidSelection())
                {
                    Confirm();
                }

                return;
            }

            if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))
            {
                return;
            }

            if (_selection.IsEmpty)
            {
                // Start a default box in the middle so keyboard-only use works.
                double w = Math.Min(800, ActualWidth * 0.6);
                double h = Math.Min(450, ActualHeight * 0.6);
                _selection = new Rect((ActualWidth - w) / 2, (ActualHeight - h) / 2, w, h);
                _coordinator.SelectionStarted(this);
            }
            else if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                double w = _selection.Width;
                double h = _selection.Height;
                switch (e.Key)
                {
                    case Key.Right: w += KeyboardStep; break;
                    case Key.Left: w = Math.Max(MinimumSize, w - KeyboardStep); break;
                    case Key.Down: h += KeyboardStep; break;
                    case Key.Up: h = Math.Max(MinimumSize, h - KeyboardStep); break;
                }

                _selection = new Rect(_selection.X, _selection.Y, w, h);
            }
            else
            {
                double x = _selection.X;
                double y = _selection.Y;
                switch (e.Key)
                {
                    case Key.Right: x += KeyboardStep; break;
                    case Key.Left: x -= KeyboardStep; break;
                    case Key.Down: y += KeyboardStep; break;
                    case Key.Up: y -= KeyboardStep; break;
                }

                _selection = new Rect(x, y, _selection.Width, _selection.Height);
            }

            // Keep the box inside this monitor.
            double width = Math.Min(_selection.Width, ActualWidth);
            double height = Math.Min(_selection.Height, ActualHeight);
            double left = Math.Min(Math.Max(0, _selection.X), ActualWidth - width);
            double top = Math.Min(Math.Max(0, _selection.Y), ActualHeight - height);
            _selection = new Rect(left, top, width, height);

            UpdateVisual();
            e.Handled = true;
        }

        // -------- Drawing --------

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateVisual();

        private bool HasValidSelection() =>
            !_selection.IsEmpty && _selection.Width >= MinimumSize && _selection.Height >= MinimumSize;

        private void UpdateVisual()
        {
            var screen = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));

            if (_selection.IsEmpty || _selection.Width < 1 || _selection.Height < 1)
            {
                DimMask.Data = screen;
                SelectionRect.Visibility = Visibility.Collapsed;
                SizeTag.Visibility = Visibility.Collapsed;
                ConfirmButton.IsEnabled = false;
                ReadoutText.Text = "No area chosen yet.";
                return;
            }

            // Dim everything except the chosen area, which stays bright.
            DimMask.Data = new CombinedGeometry(GeometryCombineMode.Exclude, screen, new RectangleGeometry(_selection));

            SelectionRect.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionRect, _selection.X);
            Canvas.SetTop(SelectionRect, _selection.Y);
            SelectionRect.Width = _selection.Width;
            SelectionRect.Height = _selection.Height;

            (int w, int h) = PhysicalSize();
            string text = $"Recording area: {w} x {h} pixels";
            SizeTagText.Text = text;
            ReadoutText.Text = HasValidSelection() ? text : text + " (too small)";
            SizeTag.Visibility = Visibility.Visible;
            SizeTag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            // Tag sits just above the box, or inside its top edge if there is no room.
            double tagTop = _selection.Y - SizeTag.DesiredSize.Height - 6;
            if (tagTop < 0)
            {
                tagTop = _selection.Y + 6;
            }

            Canvas.SetLeft(SizeTag, Math.Min(_selection.X, Math.Max(0, ActualWidth - SizeTag.DesiredSize.Width - 4)));
            Canvas.SetTop(SizeTag, tagTop);

            ConfirmButton.IsEnabled = HasValidSelection();
        }

        // -------- Result --------

        /// <summary>Width and height in real pixels, rounded down to even numbers (video needs even sizes).</summary>
        private (int Width, int Height) PhysicalSize()
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            int w = (int)Math.Floor(_selection.Width * dpi.DpiScaleX) & ~1;
            int h = (int)Math.Floor(_selection.Height * dpi.DpiScaleY) & ~1;
            return (w, h);
        }

        private void Confirm()
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            (int w, int h) = PhysicalSize();
            int x = (int)Math.Round(_selection.X * dpi.DpiScaleX);
            int y = (int)Math.Round(_selection.Y * dpi.DpiScaleY);

            // Stay inside the monitor after rounding.
            w = Math.Min(w, Monitor.Width - x) & ~1;
            h = Math.Min(h, Monitor.Height - y) & ~1;
            if (w < 16 || h < 16)
            {
                return;
            }

            _coordinator.Finish(new AreaSelection(Monitor.DeviceName, Monitor.Number, Monitor.X, Monitor.Y, x, y, w, h));
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (HasValidSelection())
            {
                Confirm();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => _coordinator.Finish(null);
    }
}
