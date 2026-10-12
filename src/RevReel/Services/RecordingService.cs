using System.IO;
using System.Runtime.InteropServices;
using RevReel.Models;
using ScreenRecorderLib;

namespace RevReel.Services
{
    /// <summary>
    /// Wraps ScreenRecorderLib. The rest of the app only calls Start() and
    /// Stop() and listens to the events below, so every ScreenRecorderLib
    /// detail lives in this one file.
    ///
    /// Records the whole primary monitor to an MP4 (H.264), no audio.
    /// Save folder, frame rate and output size come from AppSettings.
    ///
    /// ScreenRecorderLib raises its events on its own background threads.
    /// The events below are therefore raised on background threads too, so
    /// the window must switch to the UI thread before touching any control.
    /// </summary>
    public sealed class RecordingService
    {
        // Bitrate scales with the picture size so 1440p/4K are not starved.
        // 0.12 bits per pixel per frame gives about 7.5 Mbps at 1080p/30 and
        // about 30 Mbps at 4K/30, kept inside the min/max below.
        private const double BitsPerPixelPerFrame = 0.12;
        private const int MinBitrate = 4_000_000;
        private const int MaxBitrate = 60_000_000;

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private readonly object _lock = new();
        private Recorder? _recorder;
        private RecordingState _state = RecordingState.Idle;

        public RecordingState State
        {
            get { lock (_lock) { return _state; } }
        }

        /// <summary>Full path of the file being recorded, or null when idle.</summary>
        public string? CurrentFilePath { get; private set; }

        /// <summary>Plain-text progress messages for the status line.</summary>
        public event EventHandler<string>? StatusMessage;

        /// <summary>Recording finished and the file is complete. Argument: file path.</summary>
        public event EventHandler<string>? Completed;

        /// <summary>Recording could not start or failed while running. Argument: reason.</summary>
        public event EventHandler<string>? Failed;

        /// <summary>
        /// Starts recording the whole primary monitor. The file name comes
        /// from the current date and time, so recordings never overwrite
        /// each other. Does nothing if a recording is already running.
        /// Problems are reported through <see cref="Failed"/>.
        /// </summary>
        public void Start(AppSettings settings)
        {
            lock (_lock)
            {
                if (_state != RecordingState.Idle)
                {
                    return;
                }

                _state = RecordingState.Recording;
            }

            try
            {
                Directory.CreateDirectory(settings.OutputFolder);

                string fileName = $"RevReel_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4";
                string filePath = System.IO.Path.Combine(settings.OutputFolder, fileName);

                // Automatic leaves OutputFrameSize unset, so the video has the
                // same size as the screen. A fixed size keeps the picture's
                // shape and adds black bars if the shapes do not match.
                (int Width, int Height)? fixedSize = settings.OutputSize.PixelSize();
                var outputOptions = new OutputOptions
                {
                    RecorderMode = RecorderMode.Video
                };
                if (fixedSize != null)
                {
                    outputOptions.OutputFrameSize = new ScreenSize(fixedSize.Value.Width, fixedSize.Value.Height);
                }

                var options = new RecorderOptions
                {
                    SourceOptions = new SourceOptions
                    {
                        RecordingSources = new List<RecordingSourceBase>
                        {
                            new DisplayRecordingSource(DisplayRecordingSource.MainMonitor)
                        }
                    },
                    OutputOptions = outputOptions,
                    AudioOptions = new AudioOptions
                    {
                        IsAudioEnabled = false
                    },
                    VideoEncoderOptions = new VideoEncoderOptions
                    {
                        Bitrate = ChooseBitrate(settings.Framerate, fixedSize),
                        Framerate = settings.Framerate,
                        IsFixedFramerate = true,
                        IsHardwareEncodingEnabled = true,
                        Encoder = new H264VideoEncoder
                        {
                            BitrateMode = H264BitrateControlMode.CBR,
                            EncoderProfile = H264Profile.High
                        }
                    }
                };

                _recorder = Recorder.CreateRecorder(options);
                _recorder.OnRecordingComplete += OnRecordingComplete;
                _recorder.OnRecordingFailed += OnRecordingFailed;

                CurrentFilePath = filePath;
                _recorder.Record(filePath);
                StatusMessage?.Invoke(this, $"Recording started. Saving to: {filePath}");
            }
            catch (Exception ex)
            {
                ResetToIdle();
                Failed?.Invoke(this, $"Could not start recording: {ex.Message}");
            }
        }

        /// <summary>
        /// Asks the recorder to stop. The file is only ready once
        /// <see cref="Completed"/> is raised, because finishing and closing
        /// the MP4 takes a moment.
        /// </summary>
        public void Stop()
        {
            Recorder? recorder;
            lock (_lock)
            {
                if (_state != RecordingState.Recording || _recorder == null)
                {
                    return;
                }

                _state = RecordingState.Stopping;
                recorder = _recorder;
            }

            try
            {
                recorder.Stop();
            }
            catch (Exception ex)
            {
                ResetToIdle();
                Failed?.Invoke(this, $"Could not stop recording cleanly: {ex.Message}");
            }
        }

        private static int ChooseBitrate(int framerate, (int Width, int Height)? fixedSize)
        {
            int width = fixedSize?.Width ?? GetSystemMetrics(SM_CXSCREEN);
            int height = fixedSize?.Height ?? GetSystemMetrics(SM_CYSCREEN);
            if (width <= 0 || height <= 0)
            {
                return 8_000_000;
            }

            double bitrate = width * (double)height * framerate * BitsPerPixelPerFrame;
            return (int)Math.Clamp(bitrate, MinBitrate, MaxBitrate);
        }

        private void OnRecordingComplete(object? sender, RecordingCompleteEventArgs e)
        {
            ResetToIdle();
            Completed?.Invoke(this, e.FilePath);
        }

        private void OnRecordingFailed(object? sender, RecordingFailedEventArgs e)
        {
            ResetToIdle();
            Failed?.Invoke(this, e.Error);
        }

        private void ResetToIdle()
        {
            lock (_lock)
            {
                if (_recorder != null)
                {
                    _recorder.OnRecordingComplete -= OnRecordingComplete;
                    _recorder.OnRecordingFailed -= OnRecordingFailed;
                    _recorder = null;
                }

                CurrentFilePath = null;
                _state = RecordingState.Idle;
            }
        }
    }
}
