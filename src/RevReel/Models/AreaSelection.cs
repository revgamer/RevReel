namespace RevReel.Models
{
    /// <summary>
    /// A rectangle chosen on one monitor. X and Y are measured from that
    /// monitor's top-left corner (this is what ScreenRecorderLib expects).
    /// All values are real pixels.
    /// </summary>
    public sealed record AreaSelection(string DeviceName, int MonitorNumber, int MonitorX, int MonitorY,
        int X, int Y, int Width, int Height)
    {
        /// <summary>Position of the area on the combined desktop.</summary>
        public int ScreenX => MonitorX + X;
        public int ScreenY => MonitorY + Y;

        public string DisplayText => $"{Width} x {Height} on Monitor {MonitorNumber}";
    }
}
