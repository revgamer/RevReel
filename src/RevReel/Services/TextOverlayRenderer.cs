using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using RevReel.Models;

namespace RevReel.Services
{
    /// <summary>
    /// Draws text overlays (with their animation) onto a picture. The GIF and the preview in the
    /// GIF maker both use this, so what you see in the preview is what the GIF gets.
    /// </summary>
    public static class TextOverlayRenderer
    {
        /// <summary>A text size of 12 to 64 is measured on a GIF this wide, and scaled for other widths.</summary>
        public const double ReferenceWidth = 800;

        public const double OutlineWidthEm = 0.12;
        public const double UnderlineOffsetEm = 0.10;
        public const double UnderlineThicknessEm = 0.06;

        /// <summary>
        /// Draws every text that is shown at <paramref name="timeSeconds"/> onto <paramref name="frame"/>.
        /// <paramref name="region"/> is the part of the video (in video pixels) that the frame shows;
        /// the frame may be smaller than that region.
        /// </summary>
        public static void Draw(Bitmap frame, IReadOnlyList<TextOverlay> texts, Rectangle region,
            double timeSeconds, double gifStartSeconds)
        {
            if (texts.Count == 0 || region.Width < 1)
            {
                return;
            }

            double factor = frame.Width / (double)region.Width;

            using Graphics g = Graphics.FromImage(frame);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            foreach (TextOverlay t in texts)
            {
                if (string.IsNullOrEmpty(t.Text) || !t.IsVisibleAt(timeSeconds))
                {
                    continue;
                }

                DrawOne(g, t, (t.X - region.X) * factor, (t.Y - region.Y) * factor,
                    t.Size * frame.Width / ReferenceWidth, t.AnimationTime(timeSeconds, gifStartSeconds));
            }
        }

        /// <summary>
        /// Draws one text on its own transparent picture (for the preview). The text starts
        /// <paramref name="pad"/> pixels in from the top-left, which leaves room for shaking and outlines.
        /// <paramref name="baseSize"/> is the size of the text without any animation.
        /// </summary>
        public static Bitmap RenderPreview(TextOverlay t, double em, double animTime, out SizeF baseSize, out int pad)
        {
            using (var probe = new Bitmap(1, 1))
            using (Graphics measure = Graphics.FromImage(probe))
            {
                baseSize = Measure(measure, t, em);
            }

            pad = (int)Math.Ceiling(em * 0.5) + 2;
            int width = (int)Math.Ceiling(baseSize.Width) + pad * 2;
            int height = (int)Math.Ceiling(baseSize.Height) + pad * 2;

            var bitmap = new Bitmap(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format32bppArgb);
            using Graphics g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawOne(g, t, pad, pad, em, animTime);
            return bitmap;
        }

        // ------------------------------------------------------------
        // Font and measuring
        // ------------------------------------------------------------

        private sealed class Face : IDisposable
        {
            private readonly bool _ownsFamily;

            public System.Drawing.FontFamily Family { get; }
            public FontStyle Style { get; }
            public Font Font { get; }
            public double Ascent { get; }
            public double LineHeight { get; }

            public Face(TextOverlay t, double em)
            {
                System.Drawing.FontFamily family;
                try
                {
                    family = new System.Drawing.FontFamily(t.FontFamilyName);
                    _ownsFamily = true;
                }
                catch (ArgumentException)
                {
                    family = System.Drawing.FontFamily.GenericSansSerif;
                }

                FontStyle style = FontStyle.Regular;
                if (t.Bold) style |= FontStyle.Bold;
                if (t.Italic) style |= FontStyle.Italic;
                if (!family.IsStyleAvailable(style))
                {
                    style = FontStyle.Regular;
                }

                Family = family;
                Style = style;
                Font = new Font(family, (float)Math.Max(1, em), style, GraphicsUnit.Pixel);

                double emHeight = family.GetEmHeight(style);
                Ascent = family.GetCellAscent(style) * em / emHeight;
                LineHeight = family.GetLineSpacing(style) * em / emHeight;
            }

            public void Dispose()
            {
                Font.Dispose();
                if (_ownsFamily)
                {
                    Family.Dispose();
                }
            }
        }

        private static StringFormat MakeFormat()
        {
            var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
            return format;
        }

        private static float LineWidth(Graphics g, Face face, StringFormat format, string line) =>
            g.MeasureString(line.Length == 0 ? " " : line, face.Font, PointF.Empty, format).Width;

        /// <summary>Size of the whole text, ignoring animation.</summary>
        private static SizeF Measure(Graphics g, TextOverlay t, double em)
        {
            using var face = new Face(t, em);
            using StringFormat format = MakeFormat();

            string[] lines = t.Text.Replace("\r", "").Split('\n');
            float width = 1;
            foreach (string line in lines)
            {
                width = Math.Max(width, LineWidth(g, face, format, line));
            }

            return new SizeF(width, (float)Math.Max(1, lines.Length * face.LineHeight));
        }

        // ------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------

        private static string FirstLetters(string text, int count)
        {
            var result = new System.Text.StringBuilder();
            int shown = 0;
            foreach (char c in text)
            {
                if (c == '\n')
                {
                    result.Append(c);
                    continue;
                }

                if (shown >= count)
                {
                    break;
                }

                result.Append(c);
                shown++;
            }

            return result.ToString();
        }

        private static void DrawOne(Graphics g, TextOverlay t, double x, double y, double em, double animTime)
        {
            if (em < 1 || string.IsNullOrEmpty(t.Text))
            {
                return;
            }

            using var face = new Face(t, em);
            using StringFormat format = MakeFormat();

            double time = animTime * Math.Max(0.1, t.AnimationSpeed);
            int alpha = 255;
            byte red = t.R, green = t.G, blue = t.B;
            double shiftX = 0, shiftY = 0;
            string text = t.Text.Replace("\r", "");

            switch (t.Animation)
            {
                case TextAnimation.Flash:
                    if ((long)Math.Floor(time * 4) % 2 != 0)
                    {
                        return; // the "off" half of the blink
                    }

                    break;

                case TextAnimation.Fade:
                    alpha = (int)Math.Round(255 * (0.575 + 0.425 * Math.Cos(2 * Math.PI * time * 0.7)));
                    break;

                case TextAnimation.Shake:
                {
                    var random = new Random((int)Math.Floor(time * 14) + 17);
                    shiftX = (random.NextDouble() - 0.5) * em * 0.16;
                    shiftY = (random.NextDouble() - 0.5) * em * 0.16;
                    break;
                }

                case TextAnimation.Rainbow:
                    ColorMath.FromHsv(time * 180, 1, 1, out red, out green, out blue);
                    break;

                case TextAnimation.Typewriter:
                    text = FirstLetters(text, (int)Math.Floor(time * 10));
                    if (text.Length == 0)
                    {
                        return; // no letter yet
                    }

                    break;
            }

            using var path = new GraphicsPath(FillMode.Winding);
            using var ghostOne = new GraphicsPath(FillMode.Winding);
            using var ghostTwo = new GraphicsPath(FillMode.Winding);

            double left = x + shiftX;
            double top = y + shiftY;
            string[] lines = text.Split('\n');

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                if (line.Length > 0)
                {
                    if (t.Animation == TextAnimation.Broken)
                    {
                        AddBrokenLine(g, face, format, path, ghostOne, ghostTwo, line, lineIndex, time, em, left, top);
                    }
                    else
                    {
                        path.AddString(line, face.Family, (int)face.Style, (float)em,
                            new PointF((float)left, (float)top), format);
                    }

                    if (t.Underline)
                    {
                        path.AddRectangle(new RectangleF(
                            (float)left,
                            (float)(top + face.Ascent + em * UnderlineOffsetEm),
                            LineWidth(g, face, format, line),
                            (float)Math.Max(1, em * UnderlineThicknessEm)));
                    }
                }

                top += face.LineHeight;
            }

            // Colour-split "ghost" copies behind the displaced letters.
            if (ghostOne.PointCount > 0)
            {
                using var brush = new SolidBrush(Color.FromArgb((int)(alpha * 0.7), 0, 255, 255));
                g.FillPath(brush, ghostOne);
            }

            if (ghostTwo.PointCount > 0)
            {
                using var brush = new SolidBrush(Color.FromArgb((int)(alpha * 0.7), 255, 0, 80));
                g.FillPath(brush, ghostTwo);
            }

            if (path.PointCount == 0)
            {
                return;
            }

            if (t.Outline)
            {
                using var pen = new Pen(Color.FromArgb((int)(230 * alpha / 255.0), 0, 0, 0), (float)(em * OutlineWidthEm))
                {
                    LineJoin = LineJoin.Round,
                };
                g.DrawPath(pen, path);
            }

            using var fill = new SolidBrush(Color.FromArgb(alpha, red, green, blue));
            g.FillPath(fill, path);
        }

        /// <summary>
        /// Draws a line letter by letter. Every eighth of a second a few letters jump out of place,
        /// some disappear for a moment, and the jumped letters get a cyan and a red ghost.
        /// </summary>
        private static void AddBrokenLine(Graphics g, Face face, StringFormat format,
            GraphicsPath path, GraphicsPath ghostOne, GraphicsPath ghostTwo,
            string line, int lineIndex, double time, double em, double left, double top)
        {
            int frame = (int)Math.Floor(time * 8);

            for (int i = 0; i < line.Length; i++)
            {
                char letter = line[i];
                if (letter == ' ')
                {
                    continue;
                }

                double offset = i == 0
                    ? 0
                    : g.MeasureString(line.Substring(0, i), face.Font, PointF.Empty, format).Width;

                var random = new Random(unchecked(frame * 7919 + i * 104729 + lineIndex * 31 + 1));
                double roll = random.NextDouble();
                if (roll < 0.06)
                {
                    continue; // this letter is missing for a moment
                }

                double px = left + offset;
                double py = top;
                string one = letter.ToString();

                if (roll < 0.22)
                {
                    px += (random.NextDouble() - 0.5) * em * 0.35;
                    py += (random.NextDouble() - 0.5) * em * 0.5;

                    ghostOne.AddString(one, face.Family, (int)face.Style, (float)em,
                        new PointF((float)(px - em * 0.05), (float)py), format);
                    ghostTwo.AddString(one, face.Family, (int)face.Style, (float)em,
                        new PointF((float)(px + em * 0.05), (float)py), format);
                }

                path.AddString(one, face.Family, (int)face.Style, (float)em,
                    new PointF((float)px, (float)py), format);
            }
        }
    }
}
