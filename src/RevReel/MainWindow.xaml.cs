using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading.Tasks;
using System.Windows.Threading;
using RevReel.Models;
using RevReel.Services;
using RevReel.Views;

namespace RevReel
{
    public partial class MainWindow : Window
    {
        private readonly RecordingService _recording = new();
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };
        private readonly Stopwatch _stopwatch = new();

        private AppSettings _settings = SettingsService.Load();
        private string? _lastFile;
        private string? _updateUrl;
        private bool _closeAfterStop;

        public MainWindow()
        {
            InitializeComponent();

            // The recorder raises these events on background threads, so each
            // handler is passed to the UI thread before touching any control.
            _recording.StatusMessage += (_, message) => Dispatcher.BeginInvoke(() => SetStatus(message));
            _recording.Completed += (_, path) => Dispatcher.BeginInvoke(() => OnCompleted(path));
            _recording.Failed += (_, reason) => Dispatcher.BeginInvoke(() => OnFailed(reason));

            _timer.Tick += (_, _) => UpdateTimer();

            Topmost = _settings.AlwaysOnTop;
            UpdateMenuChecks();
            PreviewKeyDown += Window_PreviewKeyDown;

            // Update check: once at start-up (quietly, in the background), then every 6 hours.
            _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync(manual: false);
            Loaded += async (_, _) =>
            {
                RecordButton.Focus();
                _updateTimer.Start();
                await CheckForUpdatesAsync(manual: false);
            };
        }

        // ---------------------------------------------------------------
        // Recording
        // ---------------------------------------------------------------

        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            _recording.Start(_settings);

            if (_recording.State == RecordingState.Recording)
            {
                _stopwatch.Restart();
                _timer.Start();
                UpdateTimer();
                ShowState(isRecording: true);
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopRecording();
        }

        private void StopRecording()
        {
            if (_recording.State != RecordingState.Recording)
            {
                return;
            }

            _timer.Stop();
            _stopwatch.Stop();
            StopButton.IsEnabled = false;
            StateText.Text = "Saving...";
            SetStatus("Finishing up and saving your recording...");
            _recording.Stop();
        }

        private void OnCompleted(string filePath)
        {
            _timer.Stop();
            _stopwatch.Stop();
            _lastFile = filePath;
            OpenFolderButton.IsEnabled = true;
            MenuShowLast.IsEnabled = true;
            MenuPlayLast.IsEnabled = true;
            ShowState(isRecording: false);
            SetStatus($"Recording saved: {filePath}");
            CloseIfRequested();
        }

        private void OnFailed(string reason)
        {
            _timer.Stop();
            _stopwatch.Stop();
            ShowState(isRecording: false);
            SetStatus($"Something went wrong: {reason}");
            CloseIfRequested();
        }

        private void CloseIfRequested()
        {
            if (_closeAfterStop)
            {
                _closeAfterStop = false;
                Close();
            }
        }

        /// <summary>
        /// Closing the window while recording would leave an unfinished MP4,
        /// so closing is held back until the recording has been stopped and saved.
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            RecordingState state = _recording.State;

            if (state == RecordingState.Recording)
            {
                e.Cancel = true;
                MessageBoxResult answer = MessageBox.Show(this,
                    "A recording is in progress. Stop and save it before closing?",
                    "RevReel", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (answer == MessageBoxResult.Yes)
                {
                    _closeAfterStop = true;
                    StopRecording();
                }

                return;
            }

            if (state == RecordingState.Stopping)
            {
                e.Cancel = true;
                _closeAfterStop = true;
                SetStatus("Saving your recording. The window will close when it is saved.");
                return;
            }

            base.OnClosing(e);
        }

        // ---------------------------------------------------------------
        // File menu
        // ---------------------------------------------------------------

        private void MenuOpenLocation_Click(object sender, RoutedEventArgs e) => OpenSaveLocation();

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lastFile == null)
            {
                return;
            }

            try
            {
                // Opens File Explorer with the recording selected.
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastFile}\"")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                SetStatus($"Could not open the folder: {ex.Message}");
            }
        }

        private void MenuPlayLast_Click(object sender, RoutedEventArgs e)
        {
            if (_lastFile == null)
            {
                return;
            }

            try
            {
                // Opens the recording in the default video player.
                Process.Start(new ProcessStartInfo(_lastFile) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetStatus($"Could not play the recording: {ex.Message}");
            }
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            // Goes through OnClosing, so a running recording is saved first.
            Close();
        }

        private void OpenSaveLocation()
        {
            try
            {
                Directory.CreateDirectory(_settings.OutputFolder);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settings.OutputFolder}\"")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                SetStatus($"Could not open the save location: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------
        // Option menu
        // ---------------------------------------------------------------

        private void MenuChangeFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Choose where RevReel saves recordings",
                InitialDirectory = _settings.OutputFolder
            };

            if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                _settings.OutputFolder = dialog.FolderName;
                PersistSettings($"Recordings will be saved to: {_settings.OutputFolder}");
            }
        }

        private void MenuFrameRate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is string tag && int.TryParse(tag, out int fps))
            {
                _settings.Framerate = fps;
                PersistSettings($"Frame rate set to {fps} frames per second. It applies to the next recording.");
            }

            UpdateMenuChecks();
        }

        private void MenuOutputSize_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is string tag
                && Enum.TryParse(tag, out OutputSizePreset preset))
            {
                _settings.OutputSize = preset;
                PersistSettings($"Output size set to {preset.DisplayName()}. It applies to the next recording.");
            }

            UpdateMenuChecks();
        }

        private void MenuAlwaysOnTop_Click(object sender, RoutedEventArgs e)
        {
            _settings.AlwaysOnTop = MenuAlwaysOnTop.IsChecked;
            Topmost = _settings.AlwaysOnTop;
            PersistSettings(_settings.AlwaysOnTop
                ? "RevReel will stay on top of other windows."
                : "RevReel will no longer stay on top.");
        }

        private void PersistSettings(string successMessage)
        {
            SetStatus(SettingsService.Save(_settings)
                ? successMessage
                : "Settings are in use for now, but could not be saved. They may reset next time.");
        }

        /// <summary>Puts a tick next to the current choice in each menu group.</summary>
        private void UpdateMenuChecks()
        {
            foreach (MenuItem item in MenuFrameRate.Items.OfType<MenuItem>())
            {
                item.IsChecked = item.Tag is string tag && tag == _settings.Framerate.ToString();
            }

            foreach (MenuItem item in MenuOutputSize.Items.OfType<MenuItem>())
            {
                item.IsChecked = item.Tag is string tag && tag == _settings.OutputSize.ToString();
            }

            MenuAlwaysOnTop.IsChecked = _settings.AlwaysOnTop;
        }

        // ---------------------------------------------------------------
        // Help menu
        // ---------------------------------------------------------------

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            new AboutWindow { Owner = this }.ShowDialog();
        }

        private async void MenuCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync(manual: true);
        }

        private void MenuShortcuts_Click(object sender, RoutedEventArgs e) => ShowShortcuts();

        private void MenuGitHub_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.RepositoryUrl);

        private void MenuReportProblem_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.IssuesUrl);

        private void ShowShortcuts()
        {
            MessageBox.Show(this,
                "Alt+R\tStart recording\n" +
                "Alt+S\tStop recording\n" +
                "Alt+W\tShow the last recording in its folder\n" +
                "Ctrl+O\tOpen the save location\n" +
                "F1\tThis list\n" +
                "Alt+F4\tExit (a running recording is saved first)\n\n" +
                "Alt+F, Alt+O, Alt+H open the File, Option and Help menus.",
                "RevReel keyboard shortcuts", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
            {
                OpenSaveLocation();
                e.Handled = true;
            }
            else if (e.Key == Key.F1 && Keyboard.Modifiers == ModifierKeys.None)
            {
                ShowShortcuts();
                e.Handled = true;
            }
        }

        // ---------------------------------------------------------------
        // Updates
        // ---------------------------------------------------------------

        /// <summary>
        /// Looks for a newer version on GitHub. When one is found a banner
        /// is shown at the top of the window. An automatic check stays
        /// silent if there is nothing new; a manual check always answers.
        /// </summary>
        private async Task CheckForUpdatesAsync(bool manual)
        {
            if (manual)
            {
                SetStatus("Checking for updates...");
            }

            UpdateResult result = await UpdateService.CheckAsync();

            if (result.Status == UpdateStatus.UpdateAvailable)
            {
                _updateUrl = result.ReleaseUrl;
                UpdateBannerText.Text = result.Message;
                UpdateBanner.Visibility = Visibility.Visible;
                SetStatus(result.Message);
                return;
            }

            if (manual)
            {
                SetStatus(result.Message);
                MessageBox.Show(this, result.Message, "RevReel updates",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void UpdateDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl(_updateUrl ?? AppInfo.ReleasesUrl);
        }

        private void UpdateDismissButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateBanner.Visibility = Visibility.Collapsed;
        }

        // ---------------------------------------------------------------
        // Shared helpers
        // ---------------------------------------------------------------

        /// <summary>
        /// Sets every part of the window that shows the state in one place,
        /// so the dot, the text, the title bar, the buttons and the settings
        /// menus always agree. Settings cannot change while recording.
        /// </summary>
        private void ShowState(bool isRecording)
        {
            RecordButton.IsEnabled = !isRecording;
            StopButton.IsEnabled = isRecording;
            MenuChangeFolder.IsEnabled = !isRecording;
            MenuFrameRate.IsEnabled = !isRecording;
            MenuOutputSize.IsEnabled = !isRecording;

            if (isRecording)
            {
                IndicatorDot.Fill = (Brush)FindResource("RecordBrush");
                StateText.Text = "Recording";
                Title = "● REC - RevReel";
            }
            else
            {
                IndicatorDot.Fill = (Brush)FindResource("MutedTextBrush");
                StateText.Text = "Not recording";
                Title = "RevReel";
                RecordButton.Focus();
            }
        }

        private void UpdateTimer()
        {
            TimerText.Text = _stopwatch.Elapsed.ToString(@"hh\:mm\:ss");
        }

        private void SetStatus(string message)
        {
            StatusText.Text = message;
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // No browser available. Nothing useful to do here.
            }
        }
    }
}
