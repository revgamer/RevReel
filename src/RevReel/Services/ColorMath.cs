namespace RevReel.Services
{
    /// <summary>Converts between red/green/blue and hue/saturation/brightness (HSB).</summary>
    public static class ColorMath
    {
        /// <summary>Hue 0 to 360, saturation and brightness 0 to 1.</summary>
        public static void FromHsv(double h, double s, double v, out byte r, out byte g, out byte b)
        {
            h = ((h % 360) + 360) % 360;
            s = Math.Min(Math.Max(s, 0), 1);
            v = Math.Min(Math.Max(v, 0), 1);

            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;
            double r1, g1, b1;

            switch ((int)(h / 60))
            {
                case 0: r1 = c; g1 = x; b1 = 0; break;
                case 1: r1 = x; g1 = c; b1 = 0; break;
                case 2: r1 = 0; g1 = c; b1 = x; break;
                case 3: r1 = 0; g1 = x; b1 = c; break;
                case 4: r1 = x; g1 = 0; b1 = c; break;
                default: r1 = c; g1 = 0; b1 = x; break;
            }

            r = (byte)Math.Round((r1 + m) * 255);
            g = (byte)Math.Round((g1 + m) * 255);
            b = (byte)Math.Round((b1 + m) * 255);
        }

        public static void ToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
        {
            double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
            double max = Math.Max(rf, Math.Max(gf, bf));
            double min = Math.Min(rf, Math.Min(gf, bf));
            double delta = max - min;

            v = max;
            s = max <= 0 ? 0 : delta / max;

            if (delta <= 0)
            {
                h = 0;
            }
            else if (max == rf)
            {
                h = 60 * (((gf - bf) / delta) % 6);
            }
            else if (max == gf)
            {
                h = 60 * (((bf - rf) / delta) + 2);
            }
            else
            {
                h = 60 * (((rf - gf) / delta) + 4);
            }

            if (h < 0)
            {
                h += 360;
            }
        }
    }
}
