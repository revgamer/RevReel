using System.Windows.Threading;
using RevReel.Models;
using RevReel.Views;

namespace RevReel.Services
{
    /// <summary>
    /// Shows short on-screen messages (for example "GIF saved") that stay
    /// visible even when a game is in front. Must be used from the UI thread.
    /// </summary>
    public sealed class ToastService
    {
        private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(4) };
        private ToastWindow? _window;

        /// <summary>Pop-ups appear on this monitor. Null = the main monitor.</summary>
        public MonitorInfo? Monitor { get; set; }

        public ToastService()
        {
            _hideTimer.Tick += (_, _) =>
            {
                _hideTimer.Stop();
                _window?.Hide();
            };
        }

        public void Show(string message, bool isError = false)
        {
            _window ??= new ToastWindow();
            _window.SetMessage(message, isError, Monitor);
            if (!_window.IsVisible)
            {
                _window.Show();
            }

            _hideTimer.Stop();
            _hideTimer.Start();
        }

        public void Close()
        {
            _hideTimer.Stop();
            _window?.Close();
            _window = null;
        }
    }
}
