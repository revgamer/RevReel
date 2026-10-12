using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using RevReel.Models;
using RevReel.Services;

namespace RevReel
{
    public partial class MainWindow : Window
    {
        private readonly RecordingService _recording = new();
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private readonly Stopwatch _stopwatch = new();

        // Recordings go to Videos\RevReel until a Settings window exists.
        private readonly string _outputFolder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "RevReel");

        private string? _lastFile;
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
            Loaded += (_, _) => RecordButton.Focus();
        }

        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            _recording.Start(_outputFolder);

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

        private void OnCompleted(string filePath)
        {
            _timer.Stop();
            _stopwatch.Stop();
            _lastFile = filePath;
            OpenFolderButton.IsEnabled = true;
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

        /// <summary>
        /// Sets every part of the window that shows the state in one place,
        /// so the dot, the text, the title bar and the buttons always agree.
        /// </summary>
        private void ShowState(bool isRecording)
        {
            RecordButton.IsEnabled = !isRecording;
            StopButton.IsEnabled = isRecording;

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
    }
}
