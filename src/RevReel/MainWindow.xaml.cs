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
        private readonly HotkeyService _hotkeys = new();
        private readonly ToastService _toast = new();

        private AppSettings _settings = SettingsService.Load();
        private string? _lastFile;
        private string? _updateUrl;
        private bool _closeAfterStop;
        private readonly List<double> _bookmarks = new(); // bookmark times (seconds) in the current recording
        private AreaSelection? _area;          // chosen with Option > Record source > Select an area (kept until RevReel closes)
        private AreaBorderWindow? _areaBorder; // red outline shown while an area is being recorded

        public MainWindow()
        {
            InitializeComponent();

            // The recorder raises these events on background threads, so each
            // handler is passed to the UI thread before touching any control.
            _recording.StatusMessage += (_, message) => Dispatcher.BeginInvoke(() => SetStatus(message));
            _recording.Completed += (_, path) => Dispatcher.BeginInvoke(() => OnCompleted(path));
            _recording.Failed += (_, reason) => Dispatcher.BeginInvoke(() => OnFailed(reason));

            _timer.Tick += (_, _) => UpdateTimer();

            // An area is only kept while RevReel is open, so start on the main monitor.
            if (_settings.SourceKind == RecordingSourceKind.Area)
            {
                _settings.SourceKind = RecordingSourceKind.Monitor;
            }

            RebuildSourceMenu();
            Topmost = _settings.AlwaysOnTop;
            UpdateMenuChecks();
            PreviewKeyDown += Window_PreviewKeyDown;

            // Update check: once at start-up (quietly, in the background), then every 6 hours.
            _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync(manual: false);
            // Global keys for replay GIFs (work while a game has the focus).
            // They are only switched on while recording.
            SourceInitialized += (_, _) =>
            {
                _hotkeys.Attach(this);
                _hotkeys.Add(ModifierKeys.Control | ModifierKeys.Alt, Key.F8, "Ctrl+Alt+F8", AddBookmark);
                _hotkeys.Add(ModifierKeys.Control | ModifierKeys.Alt, Key.F5, "Ctrl+Alt+F5", () => _ = SaveReplayAsync(3));
                _hotkeys.Add(ModifierKeys.Control | ModifierKeys.Alt, Key.F6, "Ctrl+Alt+F6", () => _ = SaveReplayAsync(5));
                _hotkeys.Add(ModifierKeys.Control | ModifierKeys.Alt, Key.F7, "Ctrl+Alt+F7", () => _ = SaveReplayAsync(10));
            };
            Closed += (_, _) =>
            {
                _hotkeys.Dispose();
                _toast.Close();
                CloseAreaBorder();
            };

            ReplayBuffer.CleanOldFolders();

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
            RecordingTarget target = RecordingTarget.Resolve(_settings, _area, MonitorService.GetAll(), out string? targetNote);
            _toast.Monitor = target.ToastMonitor;
            _bookmarks.Clear();
            _recording.Start(_settings, target);

            if (_recording.State == RecordingState.Recording)
            {
                _stopwatch.Restart();
                _timer.Start();
                UpdateTimer();
                ShowState(isRecording: true);

                if (target.Kind == RecordingSourceKind.Area && target.Area != null)
                {
                    _areaBorder = new AreaBorderWindow(target.Area);
                    _areaBorder.Show();
                }

                EnableReplayKeys($"Recording {target.Description}." + (targetNote != null ? " " + targetNote : ""));
            }
        }

        private void EnableReplayKeys(string prefix)
        {
            // The keys are always switched on while recording (bookmark works even with replay GIFs off).
            IReadOnlyList<string> failed = _hotkeys.RegisterAll();
            string keys = "Bookmark: Ctrl+Alt+F8."
                + (_recording.ReplayAvailable ? " Replay GIF: Ctrl+Alt+F5 = 3 s, F6 = 5 s, F7 = 10 s." : "");

            SetStatus(failed.Count > 0
                ? $"{prefix} These keys are used by another program: {string.Join(", ", failed)}. Use the File menu instead. {keys}"
                : $"{prefix} {keys}");
        }

        /// <summary>Marks the current moment so it can be turned into a GIF after the recording.</summary>
        private void AddBookmark()
        {
            string? file = _recording.CurrentFilePath;
            if (_recording.State != RecordingState.Recording || file == null)
            {
                return;
            }

            TimeSpan at = _stopwatch.Elapsed;
            _bookmarks.Add(at.TotalSeconds);
            bool saved = BookmarkStore.Save(file, _bookmarks);

            string time = at.ToString(@"hh\:mm\:ss");
            string message = $"Bookmark {_bookmarks.Count} added at {time}";
            SetStatus(saved ? message + "." : message + ", but the bookmark file could not be saved.");
            _toast.Show(message, isError: !saved);
        }

        private void MenuBookmark_Click(object sender, RoutedEventArgs e) => AddBookmark();

        // ---- GIF editor ----

        private void MakeGif_Click(object sender, RoutedEventArgs e)
        {
            if (_recording.State != RecordingState.Idle)
            {
                SetStatus("Stop the recording first. The GIF maker works on a finished recording.");
                return;
            }

            string initialFolder = Directory.Exists(_settings.OutputFolder)
                ? _settings.OutputFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose the recording to make a GIF from",
                Filter = "Video (*.mp4)|*.mp4",
                InitialDirectory = _lastFile != null ? System.IO.Path.GetDirectoryName(_lastFile) : initialFolder,
                FileName = _lastFile != null ? System.IO.Path.GetFileName(_lastFile) : ""
            };

            if (dialog.ShowDialog(this) == true)
            {
                new GifEditorWindow(dialog.FileName) { Owner = this }.ShowDialog();
            }
        }

        private async Task SaveReplayAsync(int seconds)
        {
            try
            {
                SetStatus($"Making a GIF of the last {seconds} seconds...");
                _toast.Show($"Making {seconds}-second GIF...");
                string path = await _recording.SaveReplayGifAsync(seconds, _settings.OutputFolder);
                _lastFile = path;
                SetStatus($"GIF saved: {path}");
                _toast.Show($"GIF saved ({seconds} s)");
            }
            catch (Exception ex)
            {
                SetStatus($"Could not save the GIF: {ex.Message}");
                _toast.Show("GIF not saved: " + ex.Message, isError: true);
            }
        }

        private async void MenuReplay_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is string tag && int.TryParse(tag, out int seconds))
            {
                await SaveReplayAsync(seconds);
            }
        }

        private void MenuReplayEnabled_Click(object sender, RoutedEventArgs e)
        {
            _settings.ReplayGifEnabled = MenuReplayEnabled.IsChecked;
            PersistSettings(_settings.ReplayGifEnabled
                ? "Replay GIFs are on. They apply to the next recording."
                : "Replay GIFs are off.");
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

            _hotkeys.UnregisterAll();
            CloseAreaBorder();
            MenuReplay.IsEnabled = false;
            _timer.Stop();
            _stopwatch.Stop();
            StopButton.IsEnabled = false;
            StateText.Text = "Saving...";
            SetStatus("Finishing up and saving your recording...");
            _recording.Stop();
        }

        private void OnCompleted(string filePath)
        {
            CloseAreaBorder();
            _timer.Stop();
            _stopwatch.Stop();
            _lastFile = filePath;
            OpenFolderButton.IsEnabled = true;
            MenuShowLast.IsEnabled = true;
            MenuPlayLast.IsEnabled = true;
            ShowState(isRecording: false);
            SetStatus(_bookmarks.Count > 0
                ? $"Recording saved: {filePath}. {_bookmarks.Count} bookmark(s) saved. Click 'Make GIF...' to turn a moment into a GIF."
                : $"Recording saved: {filePath}");
            CloseIfRequested();
        }

        private void OnFailed(string reason)
        {
            _hotkeys.UnregisterAll();
            CloseAreaBorder();
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

        // ---- Record source (monitor / all monitors / area) ----

        private void MenuOption_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            // Only react to the Option menu itself, not its sub-menus.
            if (ReferenceEquals(e.OriginalSource, MenuOption))
            {
                RebuildSourceMenu();
            }
        }

        /// <summary>Lists the monitors connected right now, so plugging one in or out is picked up.</summary>
        private void RebuildSourceMenu()
        {
            MenuSource.Items.Clear();
            IReadOnlyList<MonitorInfo> monitors = MonitorService.GetAll();
            RecordingTarget current = RecordingTarget.Resolve(_settings, _area, monitors, out string? sourceNote);
            SourceText.Text = "Will record: " + current.Description + (sourceNote != null ? " - " + sourceNote : "");

            foreach (MonitorInfo monitor in monitors)
            {
                MonitorInfo captured = monitor;
                var item = new MenuItem
                {
                    Header = monitor.DisplayText.Replace("_", "__"),
                    IsCheckable = true,
                    IsChecked = current.Kind == RecordingSourceKind.Monitor && current.Monitors.Count == 1
                                && current.Monitors[0].DeviceName == monitor.DeviceName
                };
                item.Click += (_, _) => ChooseMonitor(captured);
                MenuSource.Items.Add(item);
            }

            if (monitors.Count > 1)
            {
                var all = new MenuItem
                {
                    Header = "_All monitors (one wide video)",
                    IsCheckable = true,
                    IsChecked = current.Kind == RecordingSourceKind.AllMonitors
                };
                all.Click += (_, _) => ChooseAllMonitors();
                MenuSource.Items.Add(all);
            }

            MenuSource.Items.Add(new Separator());

            var area = new MenuItem
            {
                Header = _area == null ? "Select an _area..." : $"Select an _area... (now: {_area.DisplayText})",
                IsCheckable = true,
                IsChecked = current.Kind == RecordingSourceKind.Area
            };
            area.Click += (_, _) => ChooseArea();
            MenuSource.Items.Add(area);
        }

        private void ChooseMonitor(MonitorInfo monitor)
        {
            _settings.SourceKind = RecordingSourceKind.Monitor;
            _settings.MonitorDeviceName = monitor.IsPrimary ? "" : monitor.DeviceName;
            PersistSettings($"Recording source: {monitor.DisplayText}. It applies to the next recording.");
            RebuildSourceMenu();
        }

        private void ChooseAllMonitors()
        {
            _settings.SourceKind = RecordingSourceKind.AllMonitors;
            PersistSettings("Recording source: all monitors in one wide video. It applies to the next recording.");
            RebuildSourceMenu();
        }

        private void AreaButton_Click(object sender, RoutedEventArgs e) => ChooseArea();

        private bool _choosingArea;

        private async void ChooseArea()
        {
            SetStatus("Choose an area: drag a box on any monitor, then press Enter or 'Use this area'. Esc cancels.");

            if (_choosingArea)
            {
                return;
            }

            _choosingArea = true;

            // Hide this window so it is not in the way of the area being chosen.
            WindowState previous = WindowState;
            Hide();
            AreaSelection? chosen;
            try
            {
                chosen = await AreaSelector.SelectAsync();
            }
            finally
            {
                Show();
                WindowState = previous;
                Activate();
                _choosingArea = false;
            }

            if (chosen != null)
            {
                _area = chosen;
                _settings.SourceKind = RecordingSourceKind.Area;
                PersistSettings($"Recording source: area {chosen.DisplayText}. It applies to the next recording.");
            }
            else
            {
                SetStatus("No area chosen. The recording source has not changed.");
            }

            RebuildSourceMenu();
        }

        private void CloseAreaBorder()
        {
            _areaBorder?.Close();
            _areaBorder = null;
        }

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
            MenuReplayEnabled.IsChecked = _settings.ReplayGifEnabled;
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
                "Ctrl+Alt+F8\tAdd a bookmark (while recording, works in games)\n" +
                "Ctrl+Alt+F5\tSave a GIF of the last 3 seconds (while recording, works in games)\n" +
                "Ctrl+Alt+F6\tSave a GIF of the last 5 seconds\n" +
                "Ctrl+Alt+F7\tSave a GIF of the last 10 seconds\n" +
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
            MenuReplayEnabled.IsEnabled = !isRecording;
            MenuSource.IsEnabled = !isRecording;
            AreaButton.IsEnabled = !isRecording;
            MakeGifButton.IsEnabled = !isRecording;
            MenuMakeGif.IsEnabled = !isRecording;
            MenuBookmark.IsEnabled = isRecording;
            MenuReplay.IsEnabled = isRecording && _recording.ReplayAvailable;

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
