using System.IO;
using System.Runtime.InteropServices;
using RevReel.Models;
using System.Threading.Tasks;
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
        private ReplayBuffer? _replay;

        public RecordingState State
        {
            get { lock (_lock) { return _state; } }
        }

        /// <summary>True when the current recording is keeping pictures for replay GIFs.</summary>
        public bool ReplayAvailable
        {
            get { lock (_lock) { return _replay != null && _state == RecordingState.Recording; } }
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
        public void Start(AppSettings settings, RecordingTarget target)
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

                // Replay GIFs: small JPEG pictures go to a temporary folder while
                // recording. If the folder cannot be made, recording still works,
                // just without replay GIFs.
                ReplayBuffer? replay = null;
                if (settings.ReplayGifEnabled)
                {
                    try
                    {
                        replay = new ReplayBuffer();
                    }
                    catch (Exception)
                    {
                        replay = null;
                        StatusMessage?.Invoke(this, "Replay GIFs are unavailable (no temporary folder). Recording continues without them.");
                    }
                }

                var options = new RecorderOptions
                {
                    SnapshotOptions = replay == null
                        ? null
                        : new SnapshotOptions
                        {
                            SnapshotsWithVideo = true,
                            SnapshotsIntervalMillis = ReplayBuffer.IntervalMillis,
                            SnapshotFormat = ScreenRecorderLib.ImageFormat.JPEG,
                            SnapshotsDirectory = replay.Folder
                        },
                    SourceOptions = new SourceOptions
                    {
                        RecordingSources = BuildSources(target)
                    },
                    OutputOptions = outputOptions,
                    AudioOptions = new AudioOptions
                    {
                        IsAudioEnabled = false
                    },
                    VideoEncoderOptions = new VideoEncoderOptions
                    {
                        Bitrate = ChooseBitrate(settings.Framerate, fixedSize, target),
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
                if (replay != null)
                {
                    _recorder.OnSnapshotSaved += OnSnapshotSaved;
                }

                lock (_lock) { _replay = replay; }
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

        /// <summary>
        /// Saves a GIF of the last <paramref name="seconds"/> seconds of the
        /// recording in progress. Returns the GIF's path. Throws
        /// InvalidOperationException with a plain message if it cannot be done.
        /// </summary>
        public async Task<string> SaveReplayGifAsync(int seconds, string outputFolder)
        {
            ReplayBuffer? replay;
            lock (_lock)
            {
                replay = _state == RecordingState.Recording ? _replay : null;
            }

            if (replay == null)
            {
                throw new InvalidOperationException("Replay GIFs only work while recording, and need 'Enable replay GIFs' switched on.");
            }

            if (replay.IsBuilding)
            {
                throw new InvalidOperationException("A GIF is already being made. Wait for it to finish.");
            }

            string path = System.IO.Path.Combine(outputFolder, $"RevReel_replay_{seconds}s_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.gif");
            await replay.SaveGifAsync(seconds, path);
            return path;
        }

        private void OnSnapshotSaved(object? sender, SnapshotSavedEventArgs e)
        {
            ReplayBuffer? replay;
            lock (_lock) { replay = _replay; }
            if (replay != null && !string.IsNullOrEmpty(e.SnapshotPath))
            {
                replay.AddFrame(e.SnapshotPath);
            }
        }

        /// <summary>
        /// Turns the chosen target into ScreenRecorderLib sources: one per
        /// monitor, or one monitor cropped to the chosen area.
        /// </summary>
        private static List<RecordingSourceBase> BuildSources(RecordingTarget target)
        {
            var sources = new List<RecordingSourceBase>();

            if (target.Kind == RecordingSourceKind.Window && target.Window != null)
            {
                sources.Add(new WindowRecordingSource(target.Window.Handle));
            }
            else if (target.Kind == RecordingSourceKind.Area && target.Area != null)
            {
                AreaSelection area = target.Area;
                sources.Add(new DisplayRecordingSource(area.DeviceName)
                {
                    // X and Y are measured from that monitor's top-left corner.
                    SourceRect = new ScreenRect(area.X, area.Y, area.Width, area.Height)
                });
            }
            else if (target.Monitors.Count > 0)
            {
                foreach (MonitorInfo monitor in target.Monitors)
                {
                    sources.Add(new DisplayRecordingSource(monitor.DeviceName));
                }
            }
            else
            {
                sources.Add(new DisplayRecordingSource(DisplayRecordingSource.MainMonitor));
            }

            return sources;
        }

        private static int ChooseBitrate(int framerate, (int Width, int Height)? fixedSize, RecordingTarget target)
        {
            int width = fixedSize?.Width ?? (target.Width > 0 ? target.Width : GetSystemMetrics(SM_CXSCREEN));
            int height = fixedSize?.Height ?? (target.Height > 0 ? target.Height : GetSystemMetrics(SM_CYSCREEN));
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
                    _recorder.OnSnapshotSaved -= OnSnapshotSaved;
                    _recorder = null;
                }

                _replay?.Release();
                _replay = null;

                CurrentFilePath = null;
                _state = RecordingState.Idle;
            }
        }
    }
}
