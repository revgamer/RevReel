using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RevReel.Services;

namespace RevReel.Views
{
    /// <summary>
    /// A colour picker with a colour field, a hue bar and number boxes (hue, saturation, brightness,
    /// red, green, blue and a colour code). Everything is also shown as numbers.
    /// </summary>
    public partial class ColorPickerWindow : Window
    {
        private const double FieldSize = 256; // the colour field and the hue bar are 256 x 256 units

        private double _h;   // hue, 0 to 360
        private double _s;   // saturation, 0 to 1
        private double _v;   // brightness, 0 to 1
        private bool _updating;
        private bool _dragField;
        private bool _dragHue;

        /// <summary>The chosen colour. Valid when ShowDialog returned true.</summary>
        public byte Red { get; private set; }
        public byte Green { get; private set; }
        public byte Blue { get; private set; }

        public ColorPickerWindow(byte red, byte green, byte blue)
        {
            InitializeComponent();

            OldSwatch.Background = new SolidColorBrush(Color.FromRgb(red, green, blue));
            SetFromRgb(red, green, blue);
            Loaded += (_, _) => Sync();
        }

        private void SetFromRgb(byte r, byte g, byte b)
        {
            ColorMath.ToHsv(r, g, b, out double h, out double s, out double v);
            _s = s;
            _v = v;
            if (s > 0 && v > 0)
            {
                _h = h; // grey, white and black have no hue, so keep the one we had
            }
        }

        /// <summary>Updates every part of the window from hue, saturation and brightness.</summary>
        private void Sync()
        {
            ColorMath.FromHsv(_h, _s, _v, out byte r, out byte g, out byte b);
            Red = r;
            Green = g;
            Blue = b;

            _updating = true;

            ColorMath.FromHsv(_h, 1, 1, out byte hr, out byte hg, out byte hb);
            SvArea.Background = new SolidColorBrush(Color.FromRgb(hr, hg, hb));
            NewSwatch.Background = new SolidColorBrush(Color.FromRgb(r, g, b));

            Canvas.SetLeft(SvMarker, _s * FieldSize - 6);
            Canvas.SetTop(SvMarker, (1 - _v) * FieldSize - 6);
            Canvas.SetLeft(SvMarkerOuter, _s * FieldSize - 7);
            Canvas.SetTop(SvMarkerOuter, (1 - _v) * FieldSize - 7);
            Canvas.SetTop(HueMarker, _h / 360 * FieldSize - 2);

            HBox.Text = ((int)Math.Round(_h)).ToString(CultureInfo.InvariantCulture);
            SBox.Text = ((int)Math.Round(_s * 100)).ToString(CultureInfo.InvariantCulture);
            VBox.Text = ((int)Math.Round(_v * 100)).ToString(CultureInfo.InvariantCulture);
            RBox.Text = r.ToString(CultureInfo.InvariantCulture);
            GBox.Text = g.ToString(CultureInfo.InvariantCulture);
            BlueBox.Text = b.ToString(CultureInfo.InvariantCulture);
            HexBox.Text = $"{r:X2}{g:X2}{b:X2}";

            _updating = false;
        }

        // ---- mouse on the colour field and the hue bar ----

        private void SvArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragField = true;
            SvArea.CaptureMouse();
            MoveInField(e.GetPosition(SvArea));
        }

        private void SvArea_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragField && e.LeftButton == MouseButtonState.Pressed)
            {
                MoveInField(e.GetPosition(SvArea));
            }
        }

        private void SvArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragField = false;
            SvArea.ReleaseMouseCapture();
        }

        private void MoveInField(Point at)
        {
            _s = Math.Min(Math.Max(at.X / FieldSize, 0), 1);
            _v = 1 - Math.Min(Math.Max(at.Y / FieldSize, 0), 1);
            Sync();
        }

        private void HueArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragHue = true;
            HueArea.CaptureMouse();
            MoveInHue(e.GetPosition(HueArea));
        }

        private void HueArea_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragHue && e.LeftButton == MouseButtonState.Pressed)
            {
                MoveInHue(e.GetPosition(HueArea));
            }
        }

        private void HueArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragHue = false;
            HueArea.ReleaseMouseCapture();
        }

        private void MoveInHue(Point at)
        {
            _h = Math.Min(Math.Max(at.Y / FieldSize, 0), 0.9999) * 360;
            Sync();
        }

        // ---- typing numbers ----

        private void Field_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Commit(sender);
                e.Handled = true;
            }
        }

        private void Field_LostFocus(object sender, RoutedEventArgs e) => Commit(sender);

        private void Commit(object sender)
        {
            if (_updating)
            {
                return;
            }

            if (sender == HBox && TryNumber(HBox.Text, out double h))
            {
                _h = Math.Min(Math.Max(h, 0), 359.99);
            }
            else if (sender == SBox && TryNumber(SBox.Text, out double s))
            {
                _s = Math.Min(Math.Max(s, 0), 100) / 100;
            }
            else if (sender == VBox && TryNumber(VBox.Text, out double v))
            {
                _v = Math.Min(Math.Max(v, 0), 100) / 100;
            }
            else if ((sender == RBox || sender == GBox || sender == BlueBox)
                     && TryNumber(RBox.Text, out double r) && TryNumber(GBox.Text, out double g) && TryNumber(BlueBox.Text, out double b))
            {
                SetFromRgb(ToByte(r), ToByte(g), ToByte(b));
            }
            else if (sender == HexBox && TryHex(HexBox.Text, out byte hr, out byte hg, out byte hb))
            {
                SetFromRgb(hr, hg, hb);
            }

            Sync(); // also puts back a good number if the typed one could not be read
        }

        private static bool TryNumber(string text, out double value) =>
            double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private static byte ToByte(double value) => (byte)Math.Round(Math.Min(Math.Max(value, 0), 255));

        private static bool TryHex(string text, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            string hex = text.Trim().TrimStart('#');
            if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            {
                return false;
            }

            r = (byte)((rgb >> 16) & 0xFF);
            g = (byte)((rgb >> 8) & 0xFF);
            b = (byte)(rgb & 0xFF);
            return true;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // Keep a number that is still being typed.
            if (Keyboard.FocusedElement is TextBox typing)
            {
                Commit(typing);
            }

            DialogResult = true;
        }
    }
}
