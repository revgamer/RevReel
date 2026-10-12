using System.Globalization;

namespace RevReel.Models
{
    /// <summary>
    /// One piece of text drawn on top of the picture in a GIF.
    /// X and Y are in the video's own pixels (the top-left corner of the text), so the text keeps
    /// its place when you crop. Size is a font size from 12 to 64 as it would look on a GIF that
    /// is 800 pixels wide; on a smaller or larger GIF it is scaled to look the same.
    /// </summary>
    public sealed class TextOverlay
    {
        public string Text { get; set; } = "";
        public string FontFamilyName { get; set; } = "Segoe UI";
        public const double MinSize = 12;
        public const double MaxSize = 64;

        public double Size { get; set; } = 32;
        public byte R { get; set; } = 255;
        public byte G { get; set; } = 255;
        public byte B { get; set; } = 255;
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }

        /// <summary>A dark outline around the letters so the text can be read on any picture.</summary>
        public bool Outline { get; set; } = true;

        /// <summary>True: the text is shown for the whole GIF. False: only from FromSeconds to ToSeconds.</summary>
        public bool AlwaysShown { get; set; } = true;

        /// <summary>Times on the recording's own timeline (the same as the timeline in the GIF maker).</summary>
        public double FromSeconds { get; set; }
        public double ToSeconds { get; set; }

        public TextAnimation Animation { get; set; } = TextAnimation.None;

        /// <summary>0.5 = slow, 1 = normal, 2 = fast.</summary>
        public double AnimationSpeed { get; set; } = 1.0;

        /// <summary>
        /// Seconds since the animation began: since the start of the GIF for a text shown the whole time,
        /// or since the moment the text appears.
        /// </summary>
        public double AnimationTime(double timeSeconds, double gifStartSeconds) =>
            Math.Max(0, timeSeconds - (AlwaysShown ? gifStartSeconds : FromSeconds));

        public bool IsVisibleAt(double seconds) => AlwaysShown || (seconds >= FromSeconds && seconds <= ToSeconds);

        public double X { get; set; }
        public double Y { get; set; }

        public string ColorHex => $"#{R:X2}{G:X2}{B:X2}";

        public TextOverlay Clone() => (TextOverlay)MemberwiseClone();

        /// <summary>One line of the text for lists and status messages.</summary>
        public string ShortText
        {
            get
            {
                string one = Text.Replace("\r", "").Replace("\n", " ").Trim();
                return one.Length <= 30 ? one : one.Substring(0, 30) + "...";
            }
        }

        /// <summary>Reads "#RRGGBB" or "RRGGBB". Returns false if it is not a colour.</summary>
        public bool TrySetColor(string hex)
        {
            string value = (hex ?? "").Trim().TrimStart('#');
            if (value.Length != 6
                || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            {
                return false;
            }

            R = (byte)((rgb >> 16) & 0xFF);
            G = (byte)((rgb >> 8) & 0xFF);
            B = (byte)(rgb & 0xFF);
            return true;
        }
    }
}
