using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RevReel.Models;
using RevReel.Services;
using Drawing = System.Drawing;

namespace RevReel.Views
{
    /// <summary>
    /// Works on a finished recording: play it, pick a bookmark or a start and
    /// end time, drag a box and click Crop to zoom into that part, then make a
    /// GIF (or save a trimmed MP4). Files are saved next to the video.
    /// </summary>
    public partial class GifEditorWindow : Window
    {
        private const double SecondsBeforeBookmark = 5;
        private const double SecondsAfterBookmark = 3;
        private const double MaxLengthSeconds = 60;
        private const int GifFramesPerSecond = 10;
        private const int GifMaxWidth = (int)TextOverlayRenderer.ReferenceWidth; // GIFs are never wider than this
        private const double MinBoxSize = 32; // video pixels

        private sealed record BookmarkItem(string Text, TimeSpan Time);

        // Waits for the slider to settle before jumping the video to that time.
        private readonly DispatcherTimer _seekTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };

        // If the player never reports that the video opened, tell the user instead of waiting forever.
        private readonly DispatcherTimer _openWatchdog = new() { Interval = TimeSpan.FromSeconds(8) };

        // While playing, moves the timeline along with the video.
        private readonly DispatcherTimer _playTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

        private string _videoPath;
        private VideoFrameService? _video;       // used for the picture size and for building GIFs
        private bool _playerReady;
        private TimeSpan? _pendingPosition;
        private bool _isPlaying;

        private TimeSpan _start;
        private TimeSpan _end;

        private Rect? _crop;                     // applied crop, in the video's own pixels (null = full screen)
        private Rect? _selection;                // box being dragged, not applied yet, in the video's own pixels
        private Point? _dragStart;               // screen position (PreviewHost) where the drag began

        // Text added to the GIF. Positions and sizes are in the video's own pixels.
        private readonly List<TextOverlay> _texts = new();
        private readonly Dictionary<TextOverlay, UIElement> _textFrames = new();
        private TextOverlay? _selectedText;
        private TimeSpan _previewTime;           // the moment shown in the preview, used to show or fade texts
        private Canvas? _dragText;               // the text being dragged
        private Point _dragGrab;                 // where inside that text the mouse took hold

        private List<double> _bookmarkSeconds = new();
        private bool _suppressBookmarkSelection;
        private bool _updatingUi;
        private bool _building;
        private CancellationTokenSource? _cancel;
        private string? _lastFile;

        public GifEditorWindow(string videoPath)
        {
            InitializeComponent();
            _videoPath = videoPath;

            _seekTimer.Tick += (_, _) =>
            {
                _seekTimer.Stop();
                SeekTo(TimeSpan.FromSeconds(PositionSlider.Value));
            };
            _playTimer.Tick += (_, _) => OnPlayTick();
            _openWatchdog.Tick += (_, _) =>
            {
                _openWatchdog.Stop();
                if (!_playerReady)
                {
                    PreviewMessage.Text = "The video player did not open this recording. You can still type start and end times and make a GIF.";
                    SetStatus("The video player did not open the recording. GIFs and trimmed videos can still be made from the start and end times.");
                }
            };

            Loaded += async (_, _) => await OpenVideoAsync(_videoPath);
            Closing += (_, e) =>
            {
                if (_building)
                {
                    e.Cancel = true;
                    SetStatus("Something is being built. Wait for it to finish (or click 'Stop building' for a GIF).");
                }
            };
            Closed += (_, _) =>
            {
                _playTimer.Stop();
                _seekTimer.Stop();
                _openWatchdog.Stop();
                try
                {
                    Player.Stop();
                    Player.Close();
                }
                catch
                {
                    // The player is already gone. Nothing to do.
                }
            };
        }

        // ------------------------------------------------------------
        // Opening a recording
        // ------------------------------------------------------------

        private async Task OpenVideoAsync(string path)
        {
            _videoPath = path;
            FileText.Text = "Recording: " + System.IO.Path.GetFileName(path);
            PreviewMessage.Text = "Opening the recording...";
            PreviewMessage.Visibility = Visibility.Visible;

            StopPlayback();
            _playerReady = false;
            _pendingPosition = null;
            MakeButton.IsEnabled = false;
            TrimButton.IsEnabled = false;
            AddBookmarkButton.IsEnabled = false;
            ShowGifButton.IsEnabled = false;
            SetTransportEnabled(false);
            _crop = null;
            _selection = null;
            _texts.Clear();
            _selectedText = null;
            TextLayer.Children.Clear();
            _textFrames.Clear();
            UpdateTextButtons();
            SetStatus("Opening the recording...");

            try
            {
                _video = await VideoFrameService.OpenAsync(path);
            }
            catch (Exception ex)
            {
                _video = null;
                PreviewMessage.Text = "This recording could not be opened.";
                SetStatus($"Could not open the recording: {ex.Message}");
                return;
            }

            // The video player shows the whole picture at its real size; the view
            // (zoom for a crop) is applied by scaling it. See ApplyView.
            Player.Width = _video.NativeWidth;
            Player.Height = _video.NativeHeight;
            ApplyView();
            UpdateOverlay();

            // The timeline covers the whole recording.
            _updatingUi = true;
            PositionSlider.Minimum = 0;
            PositionSlider.Maximum = Math.Max(1, _video.Duration.TotalSeconds);
            PositionSlider.Value = 0;
            PositionText.Text = TimeText.Format(TimeSpan.Zero);
            _updatingUi = false;

            _bookmarkSeconds = BookmarkStore.Load(path).Where(sec => sec <= _video.Duration.TotalSeconds).ToList();
            RefreshBookmarks(null);

            // A new recording starts with its whole length selected, from 00:00:00 to the end.
            // Bookmarks are only used when the person clicks one.
            SetRange(TimeSpan.Zero, _video.Duration);
            SeekTo(TimeSpan.Zero);

            // Start loading the video into the player. MediaOpened continues from here.
            // In Manual mode the player only opens the file once it is told to Pause or Play.
            Player.Source = new Uri(path);
            Player.Pause();
            _openWatchdog.Stop();
            _openWatchdog.Start();

            AddBookmarkButton.IsEnabled = true;
            TrimButton.IsEnabled = true;
            MakeButton.IsEnabled = true;
            UpdateTextButtons();
            SetStatus($"Recording length {TimeText.Format(_video.Duration)}, picture size {_video.NativeWidth} x {_video.NativeHeight}. "
                      + "Press Play or use the timeline, choose a bookmark or type a start and end time, then click Make GIF.");
        }

        private async void OpenOtherButton_Click(object sender, RoutedEventArgs e)
        {
            if (_building)
            {
                return;
            }

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose a recording",
                Filter = "Video (*.mp4)|*.mp4",
                InitialDirectory = System.IO.Path.GetDirectoryName(_videoPath)
            };

            if (dialog.ShowDialog(this) == true)
            {
                await OpenVideoAsync(dialog.FileName);
            }
        }

        // ------------------------------------------------------------
        // Player: play, pause, stop, seek
        // ------------------------------------------------------------

        private void Player_MediaOpened(object sender, RoutedEventArgs e)
        {
            _openWatchdog.Stop();
            _playerReady = true;
            ApplyView();
            UpdateOverlay();

            Player.Pause(); // keeps the first picture on screen
            PreviewMessage.Visibility = Visibility.Collapsed;
            SetTransportEnabled(true);
            SeekTo(_pendingPosition ?? _start);
            _pendingPosition = null;
        }

        private void Player_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            _openWatchdog.Stop();
            _playerReady = false;
            PreviewMessage.Text = "The video could not be played here. You can still type start and end times and make a GIF.";
            PreviewMessage.Visibility = Visibility.Visible;
            SetTransportEnabled(false);
            SetStatus($"The video could not be played: {e.ErrorException?.Message}");
        }

        private void Player_MediaEnded(object sender, RoutedEventArgs e)
        {
            PausePlayback("Reached the end of the recording.");
        }

        private void SeekTo(TimeSpan time)
        {
            if (!_playerReady)
            {
                _pendingPosition = time; // applied as soon as the video has opened
                return;
            }

            Player.Position = time;
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_playerReady)
            {
                return;
            }

            // Pressing Play at or after the End time starts again from the Start time.
            if (StopAtEndBox.IsChecked == true && Player.Position >= _end - TimeSpan.FromMilliseconds(200))
            {
                SeekTo(_start);
            }

            Player.Play();
            _isPlaying = true;
            _playTimer.Start();
            PlayStateText.Text = "Playing";
            UpdateTransportButtons();
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e) => PausePlayback(null);

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopPlayback();
            SeekTo(TimeSpan.Zero);
            SetTimeline(TimeSpan.Zero);
            PlayStateText.Text = "Stopped (back at 00:00:00)";
        }

        private void PausePlayback(string? message)
        {
            if (_playerReady)
            {
                Player.Pause();
            }

            _isPlaying = false;
            _playTimer.Stop();
            if (_playerReady)
            {
                SetTimeline(Player.Position);
            }

            PlayStateText.Text = "Paused";
            UpdateTransportButtons();
            if (message != null)
            {
                SetStatus(message);
            }
        }

        private void StopPlayback()
        {
            if (_playerReady)
            {
                Player.Pause();
            }

            _isPlaying = false;
            _playTimer.Stop();
            UpdateTransportButtons();
        }

        private void OnPlayTick()
        {
            TimeSpan position = Player.Position;
            SetTimeline(position);

            if (StopAtEndBox.IsChecked == true && position >= _end)
            {
                PausePlayback("Reached the End time.");
            }
        }

        /// <summary>Moves the slider and the time text without causing a jump of the video.</summary>
        private void SetTimeline(TimeSpan position)
        {
            _previewTime = position;
            UpdateTextTimes();
            _updatingUi = true;
            PositionSlider.Value = Math.Min(Math.Max(position.TotalSeconds, PositionSlider.Minimum), PositionSlider.Maximum);
            PositionText.Text = TimeText.Format(position);
            _updatingUi = false;
        }

        private void SetTransportEnabled(bool enabled)
        {
            PlayButton.IsEnabled = enabled;
            StopButton.IsEnabled = enabled;
            PauseButton.IsEnabled = enabled && _isPlaying;
            if (!enabled)
            {
                PlayStateText.Text = "Stopped";
            }
        }

        private void UpdateTransportButtons()
        {
            PlayButton.IsEnabled = _playerReady && !_isPlaying;
            PauseButton.IsEnabled = _playerReady && _isPlaying;
            StopButton.IsEnabled = _playerReady;
        }

        private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingUi)
            {
                return;
            }

            PositionText.Text = TimeText.Format(TimeSpan.FromSeconds(PositionSlider.Value));
            _previewTime = TimeSpan.FromSeconds(PositionSlider.Value);
            UpdateTextTimes();
            _seekTimer.Stop();
            _seekTimer.Start(); // jump once the slider stops moving
        }

        private void StepBack_Click(object sender, RoutedEventArgs e) =>
            PositionSlider.Value = Math.Max(PositionSlider.Minimum, PositionSlider.Value - 1);

        private void StepForward_Click(object sender, RoutedEventArgs e) =>
            PositionSlider.Value = Math.Min(PositionSlider.Maximum, PositionSlider.Value + 1);

        // ------------------------------------------------------------
        // Bookmarks
        // ------------------------------------------------------------

        private void BookmarkList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RemoveBookmarkButton.IsEnabled = BookmarkList.SelectedItem is BookmarkItem;

            if (_suppressBookmarkSelection || _video == null || BookmarkList.SelectedItem is not BookmarkItem item)
            {
                return;
            }

            TimeSpan start = TimeSpan.FromSeconds(Math.Max(0, item.Time.TotalSeconds - SecondsBeforeBookmark));
            TimeSpan end = TimeSpan.FromSeconds(Math.Min(_video.Duration.TotalSeconds, item.Time.TotalSeconds + SecondsAfterBookmark));
            SetRange(start, end);

            // Jump to the start of that range, so Play shows the whole moment.
            SetTimeline(start);
            SeekTo(start);
        }

        /// <summary>Rebuilds the list from the saved times. If <paramref name="select"/> is given, that bookmark is highlighted without changing the start and end.</summary>
        private void RefreshBookmarks(double? select)
        {
            _bookmarkSeconds = _bookmarkSeconds.OrderBy(sec => sec).ToList();
            var items = _bookmarkSeconds
                .Select((sec, i) => new BookmarkItem($"Bookmark {i + 1} - {TimeText.Format(TimeSpan.FromSeconds(sec))}", TimeSpan.FromSeconds(sec)))
                .ToList();

            _suppressBookmarkSelection = true;
            BookmarkList.ItemsSource = items;
            if (select != null)
            {
                BookmarkList.SelectedItem = items.FirstOrDefault(it => Math.Abs(it.Time.TotalSeconds - select.Value) < 0.05);
            }

            _suppressBookmarkSelection = false;
            NoBookmarksText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RemoveBookmarkButton.IsEnabled = BookmarkList.SelectedItem is BookmarkItem;
        }

        private void AddBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (_video == null)
            {
                return;
            }

            double at = Math.Round(PositionSlider.Value, 1);
            if (_bookmarkSeconds.Any(sec => Math.Abs(sec - at) < 0.5))
            {
                SetStatus("There is already a bookmark at this moment.");
                return;
            }

            _bookmarkSeconds.Add(at);
            bool saved = BookmarkStore.Save(_videoPath, _bookmarkSeconds);
            RefreshBookmarks(at);
            SetStatus($"Bookmark added at {TimeText.Format(TimeSpan.FromSeconds(at))}" + (saved ? "." : ", but the bookmark file could not be saved."));
        }

        private void RemoveBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (BookmarkList.SelectedItem is not BookmarkItem item)
            {
                return;
            }

            _bookmarkSeconds.RemoveAll(sec => Math.Abs(sec - item.Time.TotalSeconds) < 0.05);
            bool saved = BookmarkStore.Save(_videoPath, _bookmarkSeconds);
            RefreshBookmarks(null);
            SetStatus("Bookmark removed" + (saved ? "." : ", but the bookmark file could not be saved."));
        }

        // ------------------------------------------------------------
        // Start and end
        // ------------------------------------------------------------

        private void SetRange(TimeSpan start, TimeSpan end)
        {
            _start = start;
            _end = end > start ? end : start + TimeSpan.FromSeconds(1);

            _updatingUi = true;
            StartBox.Text = TimeText.Format(_start);
            EndBox.Text = TimeText.Format(_end);
            _updatingUi = false;

            UpdateTextTimes(); // animations of texts shown the whole time start at the GIF start
            UpdateLengthStatus();
        }

        private void UpdateLengthStatus()
        {
            double length = (_end - _start).TotalSeconds;
            int fps = GifFramesPerSecond;
            string text = $"GIF length {length:0.0} seconds, about {(int)Math.Ceiling(length * fps)} frames.";
            if (length > 20)
            {
                text += " That is long for a GIF and the file will be large.";
            }

            if (length > MaxLengthSeconds)
            {
                text += $" The most RevReel makes is {MaxLengthSeconds:0} seconds.";
            }

            SetStatus(text);
        }

        private void TimeBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyTimeBoxes();
                e.Handled = true;
            }
        }

        private void TimeBox_LostFocus(object sender, RoutedEventArgs e) => ApplyTimeBoxes();

        private void ApplyTimeBoxes()
        {
            if (_updatingUi || _video == null)
            {
                return;
            }

            if (!TimeText.TryParse(StartBox.Text, out TimeSpan start) || !TimeText.TryParse(EndBox.Text, out TimeSpan end))
            {
                SetStatus("Type times as hours:minutes:seconds, for example 02:03:12 or 1:05 for one minute five seconds.");
                return;
            }

            start = Clamp(start, TimeSpan.Zero, _video.Duration);
            end = Clamp(end, TimeSpan.Zero, _video.Duration);
            if (end <= start)
            {
                SetStatus("The end time must be after the start time.");
                return;
            }

            if (start == _start && end == _end)
            {
                return; // nothing changed
            }

            SetRange(start, end);
            SetTimeline(start);
            SeekTo(start);
        }

        private void SetStartHere_Click(object sender, RoutedEventArgs e)
        {
            TimeSpan here = TimeSpan.FromSeconds(PositionSlider.Value);
            if (here >= _end)
            {
                SetStatus("The start must be before the end.");
                return;
            }

            SetRange(here, _end);
        }

        private void SetEndHere_Click(object sender, RoutedEventArgs e)
        {
            TimeSpan here = TimeSpan.FromSeconds(PositionSlider.Value);
            if (here <= _start)
            {
                SetStatus("The end must be after the start.");
                return;
            }

            SetRange(_start, here);
        }

        // ------------------------------------------------------------
        // View, box and crop
        //
        // The video player always has the real picture size. A "view" (the whole
        // picture, or the cropped part) is shown by scaling and moving the player
        // so the view fills the preview area. Boxes are stored in video pixels.
        // ------------------------------------------------------------

        private Rect ViewRect() =>
            _crop ?? (_video == null ? Rect.Empty : new Rect(0, 0, _video.NativeWidth, _video.NativeHeight));

        /// <summary>Scale from video pixels to screen, and where the view is drawn in the preview area.</summary>
        private bool TryGetViewMetrics(out Rect view, out double scale, out Rect shown)
        {
            view = ViewRect();
            scale = 0;
            shown = Rect.Empty;

            if (view.IsEmpty || view.Width <= 0 || view.Height <= 0
                || PreviewHost.ActualWidth <= 0 || PreviewHost.ActualHeight <= 0)
            {
                return false;
            }

            scale = Math.Min(PreviewHost.ActualWidth / view.Width, PreviewHost.ActualHeight / view.Height);
            double w = view.Width * scale;
            double h = view.Height * scale;
            shown = new Rect((PreviewHost.ActualWidth - w) / 2, (PreviewHost.ActualHeight - h) / 2, w, h);
            return true;
        }

        private void ApplyView()
        {
            if (_video == null || !TryGetViewMetrics(out Rect view, out double scale, out Rect shown))
            {
                return;
            }

            // screen = video pixel * scale + offset, so that the view's top-left lands on "shown".
            double offsetX = shown.X - view.X * scale;
            double offsetY = shown.Y - view.Y * scale;
            Player.RenderTransform = new MatrixTransform(scale, 0, 0, scale, offsetX, offsetY);
            // Show only the view: anything outside the crop box is cut off.
            Player.Clip = new RectangleGeometry(view);
            RenderTexts();
        }

        private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyView();
            UpdateOverlay();
        }

        private void CropHit_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!TryGetViewMetrics(out _, out _, out Rect shown) || Player.Source == null)
            {
                return;
            }

            if (_selectedText != null)
            {
                _selectedText = null;
                ShowTextSelection();
                UpdateTextButtons();
            }

            _dragStart = ClampTo(shown, e.GetPosition(PreviewHost));
            _selection = null;
            CropHit.CaptureMouse();
            UpdateOverlay();
        }

        private void CropHit_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragStart == null || e.LeftButton != MouseButtonState.Pressed
                || !TryGetViewMetrics(out Rect view, out double scale, out Rect shown))
            {
                return;
            }

            var dragged = new Rect(_dragStart.Value, ClampTo(shown, e.GetPosition(PreviewHost)));
            _selection = new Rect(
                view.X + (dragged.X - shown.X) / scale,
                view.Y + (dragged.Y - shown.Y) / scale,
                dragged.Width / scale,
                dragged.Height / scale);
            UpdateOverlay();
        }

        private void CropHit_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragStart = null;
            CropHit.ReleaseMouseCapture();

            // A click without a real drag, or a tiny box, means no box.
            if (_selection is { Width: < MinBoxSize } or { Height: < MinBoxSize })
            {
                _selection = null;
            }
            else if (_selection != null)
            {
                SetStatus($"Box drawn: {(int)_selection.Value.Width} x {(int)_selection.Value.Height} pixels. Click Crop to use it.");
            }

            UpdateOverlay();
        }

        private static Point ClampTo(Rect area, Point p) =>
            new(Math.Min(Math.Max(area.Left, p.X), area.Right), Math.Min(Math.Max(area.Top, p.Y), area.Bottom));

        /// <summary>Applies the dragged box: the preview and the GIF then show only that part.</summary>
        private void Crop_Click(object sender, RoutedEventArgs e)
        {
            if (_video == null)
            {
                return;
            }

            if (_selection == null)
            {
                SetStatus("Drag a box on the picture first, then click Crop.");
                return;
            }

            var whole = new Rect(0, 0, _video.NativeWidth, _video.NativeHeight);
            Rect box = Rect.Intersect(_selection.Value, whole);
            if (box.IsEmpty || box.Width < MinBoxSize || box.Height < MinBoxSize)
            {
                SetStatus("That box is too small. Drag a bigger box.");
                return;
            }

            _crop = box;
            _selection = null;
            ApplyView();
            UpdateOverlay();
            SetStatus($"Cropped to {(int)box.Width} x {(int)box.Height} pixels. The preview and the GIF now show only this part. Click Full screen to go back.");
        }

        /// <summary>Back to the whole picture.</summary>
        private void FullScreen_Click(object sender, RoutedEventArgs e)
        {
            _crop = null;
            _selection = null;
            ApplyView();
            UpdateOverlay();
            SetStatus("Showing the whole picture. GIFs now use the whole picture.");
        }

        /// <summary>Draws the dragged box (dimming the rest) and writes the crop state in words.</summary>
        private void UpdateOverlay()
        {
            CropText.Text = _crop != null
                ? $"Cropped to {(int)_crop.Value.Width} x {(int)_crop.Value.Height}"
                : "Full screen (no crop)";

            if (_selection == null || !TryGetViewMetrics(out Rect view, out double scale, out Rect shown))
            {
                DimMask.Data = null;
                SelectionRect.Visibility = Visibility.Collapsed;
                return;
            }

            var box = new Rect(
                shown.X + (_selection.Value.X - view.X) * scale,
                shown.Y + (_selection.Value.Y - view.Y) * scale,
                _selection.Value.Width * scale,
                _selection.Value.Height * scale);

            DimMask.Data = new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(0, 0, PreviewHost.ActualWidth, PreviewHost.ActualHeight)),
                new RectangleGeometry(box));

            SelectionRect.Margin = new Thickness(box.X, box.Y, 0, 0);
            SelectionRect.Width = Math.Max(1, box.Width);
            SelectionRect.Height = Math.Max(1, box.Height);
            SelectionRect.Visibility = Visibility.Visible;
        }

        // ------------------------------------------------------------
        // Text on the GIF
        //
        // Texts are drawn over the preview and into every GIF frame. Each text keeps
        // its position in video pixels, so it stays in place when you crop or resize.
        // ------------------------------------------------------------

        private void AddText_Click(object sender, RoutedEventArgs e)
        {
            if (_video == null || _building)
            {
                return;
            }

            Rect view = ViewRect();
            var text = new TextOverlay
            {
                X = view.X + view.Width * 0.1,
                Y = view.Y + view.Height * 0.7,
                FromSeconds = _start.TotalSeconds,
                ToSeconds = _end.TotalSeconds,
            };

            var dialog = new TextEditWindow(text, _video.Duration) { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            _texts.Add(dialog.Result);
            _selectedText = dialog.Result;
            RenderTexts();
            UpdateTextButtons();
            SetStatus("Text added. Drag it in the picture to move it. Double-click it to change it. "
                      + "A faded text in the preview is not shown at the moment you are looking at.");
        }

        private void EditText_Click(object sender, RoutedEventArgs e) => EditSelectedText();

        private void EditSelectedText()
        {
            TextOverlay? current = _selectedText ?? (_texts.Count == 1 ? _texts[0] : null);
            if (_video == null || _building || current == null)
            {
                return;
            }

            var dialog = new TextEditWindow(current, _video.Duration) { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            int index = _texts.IndexOf(current);
            if (index < 0)
            {
                return;
            }

            _texts[index] = dialog.Result;
            _selectedText = dialog.Result;
            RenderTexts();
            UpdateTextButtons();
            SetStatus("Text changed.");
        }

        private void RemoveText_Click(object sender, RoutedEventArgs e)
        {
            TextOverlay? current = _selectedText ?? (_texts.Count == 1 ? _texts[0] : null);
            if (current == null || _building)
            {
                return;
            }

            _texts.Remove(current);
            _selectedText = null;
            RenderTexts();
            UpdateTextButtons();
            SetStatus("Text removed.");
        }

        /// <summary>Redraws every text in the preview. Called whenever the view, the size or the texts change.</summary>
        private void RenderTexts()
        {
            TextLayer.Children.Clear();
            _textFrames.Clear();
            _dragText = null;

            if (_video == null || !TryGetViewMetrics(out Rect view, out double scale, out Rect shown))
            {
                TextLayer.Clip = null;
                return;
            }

            // Only the picture itself shows text, not the black bars around it.
            TextLayer.Clip = new RectangleGeometry(shown);
            double dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;

            foreach (TextOverlay text in _texts)
            {
                double em = text.Size * shown.Width / TextOverlayRenderer.ReferenceWidth;
                Canvas visual = TextOverlayVisual.Build(
                    text, em, dpiScale, text.AnimationTime(_previewTime.TotalSeconds, _start.TotalSeconds), out UIElement frame);
                Canvas.SetLeft(visual, shown.X + (text.X - view.X) * scale);
                Canvas.SetTop(visual, shown.Y + (text.Y - view.Y) * scale);
                visual.MouseLeftButtonDown += TextVisual_MouseLeftButtonDown;
                visual.MouseMove += TextVisual_MouseMove;
                visual.MouseLeftButtonUp += TextVisual_MouseLeftButtonUp;
                TextLayer.Children.Add(visual);
                _textFrames[text] = frame;
            }

            ShowTextSelection();
            UpdateTextTimes();
        }

        /// <summary>Texts that are not shown at the previewed moment are faded (still visible, so they can be moved).</summary>
        private void UpdateTextTimes()
        {
            if (_video == null || !TryGetViewMetrics(out _, out _, out Rect shown))
            {
                return;
            }

            double dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            double seconds = _previewTime.TotalSeconds;

            foreach (UIElement child in TextLayer.Children)
            {
                if (child is Canvas { Tag: TextOverlay text } visual)
                {
                    visual.Opacity = text.IsVisibleAt(seconds) ? 1.0 : 0.3;

                    // Animated texts are drawn again for the moment shown in the preview.
                    if (text.Animation != TextAnimation.None)
                    {
                        double em = text.Size * shown.Width / TextOverlayRenderer.ReferenceWidth;
                        TextOverlayVisual.Refresh(visual, text, em, dpiScale, text.AnimationTime(seconds, _start.TotalSeconds));
                    }
                }
            }
        }

        private void ShowTextSelection()
        {
            foreach (KeyValuePair<TextOverlay, UIElement> item in _textFrames)
            {
                item.Value.Visibility = item.Key == _selectedText ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void TextVisual_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Canvas visual || visual.Tag is not TextOverlay text)
            {
                return;
            }

            _selectedText = text;
            ShowTextSelection();
            UpdateTextButtons();

            if (e.ClickCount >= 2)
            {
                e.Handled = true;
                EditSelectedText();
                return;
            }

            _dragText = visual;
            _dragGrab = e.GetPosition(visual);
            visual.CaptureMouse();
            e.Handled = true;
        }

        private void TextVisual_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not Canvas visual || visual != _dragText || visual.Tag is not TextOverlay text
                || e.LeftButton != MouseButtonState.Pressed
                || !TryGetViewMetrics(out Rect view, out double scale, out Rect shown))
            {
                return;
            }

            Point at = e.GetPosition(TextLayer);
            double x = view.X + (at.X - _dragGrab.X - shown.X) / scale;
            double y = view.Y + (at.Y - _dragGrab.Y - shown.Y) / scale;

            // Keep the top-left corner of the text inside the picture.
            text.X = Math.Min(Math.Max(view.Left, x), view.Right - 10);
            text.Y = Math.Min(Math.Max(view.Top, y), view.Bottom - 10);

            Canvas.SetLeft(visual, shown.X + (text.X - view.X) * scale);
            Canvas.SetTop(visual, shown.Y + (text.Y - view.Y) * scale);
        }

        private void TextVisual_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Canvas visual && visual == _dragText)
            {
                visual.ReleaseMouseCapture();
                _dragText = null;
                e.Handled = true;
            }
        }

        /// <summary>Enables the text buttons and describes the text state in words.</summary>
        private void UpdateTextButtons()
        {
            bool canChange = _video != null && !_building;
            bool haveTarget = _selectedText != null || _texts.Count == 1;

            AddTextButton.IsEnabled = canChange;
            EditTextButton.IsEnabled = canChange && haveTarget;
            RemoveTextButton.IsEnabled = canChange && haveTarget;

            string count = _texts.Count == 1 ? "1 text" : $"{_texts.Count} texts";
            string when = _selectedText == null ? "" : _selectedText.AlwaysShown
                ? " Shown the whole GIF."
                : $" Shown from {TimeText.Format(TimeSpan.FromSeconds(_selectedText.FromSeconds))} to {TimeText.Format(TimeSpan.FromSeconds(_selectedText.ToSeconds))}.";
            TextInfoText.Text = _texts.Count == 0
                ? "No text added."
                : _selectedText != null
                    ? $"{count}. Selected: \"{_selectedText.ShortText}\".{when} Drag it to move it."
                    : $"{count}. Click one in the picture to select it.";
        }

        // ------------------------------------------------------------
        // Building the GIF and the trimmed video
        // ------------------------------------------------------------

        private async void MakeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_video == null || _building)
            {
                return;
            }

            ApplyTimeBoxes();

            if (_selection != null)
            {
                SetStatus("You drew a box but did not apply it. Click Crop to use it, or click Full screen to clear it.");
                return;
            }

            double length = (_end - _start).TotalSeconds;
            if (length <= 0)
            {
                SetStatus("The end time must be after the start time.");
                return;
            }

            if (length > MaxLengthSeconds)
            {
                SetStatus($"That is {length:0} seconds. The most RevReel makes is {MaxLengthSeconds:0} seconds. Shorten the start and end.");
                return;
            }

            StopPlayback();

            int fps = GifFramesPerSecond;
            int width = GifMaxWidth;

            Drawing.Rectangle? crop = null;
            if (_crop != null)
            {
                crop = new Drawing.Rectangle((int)_crop.Value.X, (int)_crop.Value.Y,
                    (int)_crop.Value.Width, (int)_crop.Value.Height);
            }

            string gifPath = BuildOutputPath("gif");

            SetBuilding(true);
            BuildProgress.Value = 0;
            SetStatus("Building the GIF. This can take a minute for long or large GIFs. You can keep using other programs.");

            _cancel = new CancellationTokenSource();
            var progress = new Progress<double>(p => BuildProgress.Value = p);

            VideoFrameService video = _video;
            bool loop = LoopRadio.IsChecked == true;
            List<TextOverlay> texts = _texts.Select(t => t.Clone()).ToList(); // copy, so edits during the build change nothing
            TimeSpan start = _start;
            TimeSpan end = _end;
            CancellationToken token = _cancel.Token;

            try
            {
                await Task.Run(() => GifExporter.CreateFromVideoAsync(video, start, end, crop, fps, width, gifPath, progress, token, texts, loop));

                _lastFile = gifPath;
                long bytes = new FileInfo(gifPath).Length;
                ShowGifButton.IsEnabled = true;
                SetStatus($"GIF saved ({(loop ? "loops" : "plays once")}): {gifPath} ({bytes / 1024.0 / 1024.0:0.0} MB).");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Building the GIF was stopped. Nothing was saved.");
            }
            catch (Exception ex)
            {
                SetStatus($"The GIF could not be made: {ex.Message}");
            }
            finally
            {
                _cancel?.Dispose();
                _cancel = null;
                SetBuilding(false);
            }
        }

        private async void TrimButton_Click(object sender, RoutedEventArgs e)
        {
            if (_video == null || _building)
            {
                return;
            }

            ApplyTimeBoxes();
            if (_end <= _start)
            {
                SetStatus("The end time must be after the start time.");
                return;
            }

            StopPlayback();
            string clipPath = BuildOutputPath("mp4", "clip");

            SetBuilding(true);
            CancelBuildButton.IsEnabled = false; // a trim cannot be stopped part-way
            BuildProgress.IsIndeterminate = true;
            SetStatus("Saving the trimmed video. This can take a minute. The whole picture is kept; the crop only applies to GIFs.");

            TimeSpan start = _start;
            TimeSpan end = _end;
            try
            {
                await ClipExporter.CreateClipAsync(_videoPath, clipPath, start, end);
                _lastFile = clipPath;
                ShowGifButton.IsEnabled = true;
                long bytes = new FileInfo(clipPath).Length;
                SetStatus($"Trimmed video saved: {clipPath} ({bytes / 1024.0 / 1024.0:0.0} MB).");
            }
            catch (Exception ex)
            {
                SetStatus($"The trimmed video could not be saved: {ex.Message}");
            }
            finally
            {
                BuildProgress.IsIndeterminate = false;
                SetBuilding(false);
            }
        }

        private string BuildOutputPath(string extension, string label = "gif")
        {
            string folder = System.IO.Path.GetDirectoryName(_videoPath) ?? ".";
            string name = System.IO.Path.GetFileNameWithoutExtension(_videoPath);
            return System.IO.Path.Combine(folder,
                $"{name}_{label}_{(int)_start.TotalHours:00}-{_start.Minutes:00}-{_start.Seconds:00}_{DateTime.Now:HHmmss}.{extension}");
        }

        private void CancelBuildButton_Click(object sender, RoutedEventArgs e) => _cancel?.Cancel();

        private void ShowGifButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lastFile == null)
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastFile}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetStatus($"Could not open the folder: {ex.Message}");
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void SetBuilding(bool building)
        {
            _building = building;
            MakeButton.IsEnabled = !building && _video != null;
            UpdateTextButtons();
            TrimButton.IsEnabled = !building && _video != null;
            AddBookmarkButton.IsEnabled = !building && _video != null;
            CancelBuildButton.IsEnabled = building;
            BuildProgress.Visibility = building ? Visibility.Visible : Visibility.Collapsed;
            BookmarkList.IsEnabled = !building;
        }

        // ------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------

        private void SetStatus(string text) => StatusText.Text = text;

        private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) =>
            value < min ? min : value > max ? max : value;
    }
}
