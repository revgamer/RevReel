using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RevReel.Models;
using RevReel.Services;
using Drawing = System.Drawing;

namespace RevReel.Views
{
    /// <summary>Asks for the words, font, size, colour and style of one text on the GIF.</summary>
    public partial class TextEditWindow : Window
    {
        private bool _ready;
        private readonly DispatcherTimer _lookTimer = new() { Interval = TimeSpan.FromMilliseconds(70) };
        private readonly Stopwatch _lookClock = Stopwatch.StartNew();
        private readonly TimeSpan _videoLength;

        /// <summary>The edited text. Only valid when ShowDialog returned true.</summary>
        public TextOverlay Result { get; private set; }

        public TextEditWindow(TextOverlay overlay, TimeSpan videoLength)
        {
            InitializeComponent();
            Result = overlay.Clone();

            WordsBox.Text = overlay.Text;

            var fonts = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
            FontBox.ItemsSource = fonts;
            int fontIndex = fonts.FindIndex(n => string.Equals(n, overlay.FontFamilyName, StringComparison.OrdinalIgnoreCase));
            FontBox.SelectedIndex = fontIndex >= 0 ? fontIndex : Math.Max(0, fonts.FindIndex(n => n == "Segoe UI"));

            foreach (int size in new[] { 12, 16, 20, 24, 28, 32, 40, 48, 56, 64 })
            {
                SizeBox.Items.Add(size.ToString());
            }

            SizeBox.Text = ((int)Math.Round(overlay.Size)).ToString();
            SizeHint.Text = "Choose a size from 12 to 64, or type one. The text keeps the same look however large the recording is."; 

            _videoLength = videoLength;
            AlwaysBox.IsChecked = overlay.AlwaysShown;
            FromBox.Text = TimeText.Format(TimeSpan.FromSeconds(overlay.FromSeconds));
            ToBox.Text = TimeText.Format(TimeSpan.FromSeconds(overlay.ToSeconds));
            UpdateTimeBoxes();
            AlwaysBox.Click += (_, _) => UpdateTimeBoxes();

            HexBox.Text = overlay.ColorHex;

            AnimBox.SelectedIndex = (int)overlay.Animation;
            SpeedBox.SelectedIndex = overlay.AnimationSpeed < 0.9 ? 0 : overlay.AnimationSpeed > 1.1 ? 2 : 1;

            BoldBox.IsChecked = overlay.Bold;
            ItalicBox.IsChecked = overlay.Italic;
            UnderlineBox.IsChecked = overlay.Underline;
            OutlineBox.IsChecked = overlay.Outline;

            // Connected after the values are set, so loading them does not trigger the handlers.
            WordsBox.TextChanged += (_, _) => UpdatePreview();
            FontBox.SelectionChanged += (_, _) => UpdatePreview();
            AnimBox.SelectionChanged += (_, _) => UpdatePreview();
            SpeedBox.SelectionChanged += (_, _) => UpdatePreview();
            SizeBox.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(new Action(UpdatePreview)); // after the box has its new text
            SizeBox.KeyUp += (_, _) => UpdatePreview();
            SizeBox.LostFocus += (_, _) => UpdatePreview();
            _lookTimer.Tick += (_, _) => RenderLook();
            Closed += (_, _) => _lookTimer.Stop();
            HexBox.TextChanged += (_, _) => UpdatePreview();
            foreach (CheckBox box in new[] { BoldBox, ItalicBox, UnderlineBox, OutlineBox })
            {
                box.Click += (_, _) => UpdatePreview();
            }

            _ready = true;
            UpdatePreview();
            Loaded += (_, _) =>
            {
                WordsBox.Focus();
                WordsBox.SelectAll();
            };
        }

        private void PickColor_Click(object sender, RoutedEventArgs e)
        {
            var current = new TextOverlay();
            if (!current.TrySetColor(HexBox.Text))
            {
                current.TrySetColor(Result.ColorHex);
            }

            var picker = new ColorPickerWindow(current.R, current.G, current.B) { Owner = this };
            if (picker.ShowDialog() == true)
            {
                HexBox.Text = $"#{picker.Red:X2}{picker.Green:X2}{picker.Blue:X2}";
            }
        }

        private void UpdateTimeBoxes()
        {
            bool timed = AlwaysBox.IsChecked != true;
            FromBox.IsEnabled = timed;
            ToBox.IsEnabled = timed;
            TimePanel.Opacity = timed ? 1.0 : 0.5;
        }

        private void UpdatePreview()
        {
            if (!_ready)
            {
                return;
            }

            var probe = new TextOverlay();
            if (probe.TrySetColor(HexBox.Text))
            {
                ColorSwatch.Background = new SolidColorBrush(Color.FromRgb(probe.R, probe.G, probe.B));
            }

            ErrorText.Text = "";

            // The look box only needs a steady timer while something moves.
            if (AnimBox.SelectedIndex > 0)
            {
                _lookTimer.Start();
            }
            else
            {
                _lookTimer.Stop();
            }

            RenderLook();
        }

        /// <summary>Draws the text in the Look box with the same code that makes the GIF, including the animation.</summary>
        private void RenderLook()
        {
            if (!_ready)
            {
                return;
            }

            var text = new TextOverlay
            {
                Text = string.IsNullOrWhiteSpace(WordsBox.Text) ? "Your text" : WordsBox.Text,
                FontFamilyName = FontBox.SelectedItem as string ?? "Segoe UI",
                Bold = BoldBox.IsChecked == true,
                Italic = ItalicBox.IsChecked == true,
                Underline = UnderlineBox.IsChecked == true,
                Outline = OutlineBox.IsChecked == true,
                Animation = (TextAnimation)Math.Max(0, AnimBox.SelectedIndex),
                AnimationSpeed = SpeedBox.SelectedIndex switch { 0 => 0.5, 2 => 2.0, _ => 1.0 },
            };

            var color = new TextOverlay();
            if (color.TrySetColor(HexBox.Text))
            {
                text.R = color.R;
                text.G = color.G;
                text.B = color.B;
            }

            double size = double.TryParse(SizeBox.Text.Trim(), out double typed)
                ? Math.Min(Math.Max(typed, TextOverlay.MinSize), TextOverlay.MaxSize)
                : 32;
            double em = size * 0.9;
            double dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;

            // Loop the animation. A typewriter needs long enough to type everything and then hold for a moment.
            double cycle = text.Animation == TextAnimation.Typewriter
                ? Math.Max(3, text.Text.Length / (10 * text.AnimationSpeed) + 1.5)
                : 6;
            double animTime = _lookClock.Elapsed.TotalSeconds % cycle;

            using Drawing.Bitmap bitmap = TextOverlayRenderer.RenderPreview(text, em * dpiScale, animTime, out _, out _);
            LookImage.Source = TextOverlayVisual.ToSource(bitmap);
            LookImage.Width = bitmap.Width / dpiScale;
            LookImage.Height = bitmap.Height / dpiScale;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(WordsBox.Text))
            {
                ErrorText.Text = "Type some text first.";
                WordsBox.Focus();
                return;
            }

            if (!double.TryParse(SizeBox.Text.Trim(), out double size) || size < TextOverlay.MinSize || size > TextOverlay.MaxSize)
            {
                ErrorText.Text = "The size must be a number from 12 to 64.";
                SizeBox.Focus();
                return;
            }

            var color = new TextOverlay();
            if (!color.TrySetColor(HexBox.Text))
            {
                ErrorText.Text = "The colour code must look like FF8A00 (six letters or numbers 0 to 9 and A to F).";
                HexBox.Focus();
                return;
            }

            TimeSpan from = TimeSpan.Zero;
            TimeSpan until = TimeSpan.Zero;
            bool always = AlwaysBox.IsChecked == true;
            if (!always)
            {
                if (!TimeText.TryParse(FromBox.Text, out from) || !TimeText.TryParse(ToBox.Text, out until))
                {
                    ErrorText.Text = "Type the times like 00:00:05 and 00:00:08 (hours:minutes:seconds).";
                    FromBox.Focus();
                    return;
                }

                if (until <= from)
                {
                    ErrorText.Text = "The text must disappear after it appears. Make the second time bigger than the first.";
                    ToBox.Focus();
                    return;
                }

                if (from > _videoLength)
                {
                    ErrorText.Text = $"The recording is only {TimeText.Format(_videoLength)} long. Choose an earlier time to show the text.";
                    FromBox.Focus();
                    return;
                }
            }

            Result.AlwaysShown = always;
            Result.FromSeconds = from.TotalSeconds;
            Result.ToSeconds = until.TotalSeconds;
            Result.Text = WordsBox.Text;
            Result.FontFamilyName = FontBox.SelectedItem as string ?? "Segoe UI";
            Result.Size = size;
            Result.R = color.R;
            Result.G = color.G;
            Result.B = color.B;
            Result.Bold = BoldBox.IsChecked == true;
            Result.Italic = ItalicBox.IsChecked == true;
            Result.Underline = UnderlineBox.IsChecked == true;
            Result.Outline = OutlineBox.IsChecked == true;
            Result.Animation = (TextAnimation)Math.Max(0, AnimBox.SelectedIndex);
            Result.AnimationSpeed = SpeedBox.SelectedIndex switch { 0 => 0.5, 2 => 2.0, _ => 1.0 };

            DialogResult = true;
        }
    }
}
