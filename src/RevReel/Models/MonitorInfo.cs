namespace RevReel.Models
{
    /// <summary>
    /// One physical monitor. X, Y, Width and Height are real screen pixels
    /// on the combined desktop (a monitor left of the main one has a negative X).
    /// </summary>
    public sealed record MonitorInfo(string DeviceName, int X, int Y, int Width, int Height, bool IsPrimary)
    {
        /// <summary>The number Windows shows in Settings > Display, taken from the device name (\\.\DISPLAY2 = 2).</summary>
        public int Number
        {
            get
            {
                string digits = new string(DeviceName.Where(char.IsDigit).ToArray());
                return int.TryParse(digits, out int n) ? n : 0;
            }
        }

        public string DisplayText =>
            $"Monitor {Number} ({Width} x {Height}){(IsPrimary ? ", main" : "")}";
    }
}
