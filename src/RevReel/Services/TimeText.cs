using System.Globalization;

namespace RevReel.Services
{
    /// <summary>Reads and writes times like 00:00:05.5 for text boxes.</summary>
    public static class TimeText
    {
        /// <summary>For example 02:03:12.5</summary>
        public static string Format(TimeSpan t) =>
            $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}";

        /// <summary>Accepts hours:minutes:seconds, minutes:seconds, or seconds. Seconds may have decimals.</summary>
        public static bool TryParse(string text, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            string[] parts = (text ?? "").Trim().Split(':');
            if (parts.Length is < 1 or > 3)
            {
                return false;
            }

            double total = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (i == parts.Length - 1)
                {
                    if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) || seconds < 0)
                    {
                        return false;
                    }

                    total = total * 60 + seconds;
                }
                else
                {
                    if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int whole))
                    {
                        return false;
                    }

                    total = total * 60 + whole;
                }
            }

            time = TimeSpan.FromSeconds(total);
            return true;
        }
    }
}
