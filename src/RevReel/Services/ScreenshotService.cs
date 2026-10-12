using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using RevReel.Models;

namespace RevReel.Services
{
    /// <summary>Saves a picture of exactly what the next recording would capture.</summary>
    public static class ScreenshotService
    {
        /// <summary>Takes the screenshot and returns the saved PNG path. Throws if it cannot be saved.</summary>
        public static string Capture(RecordingTarget target, string folder)
        {
            Rectangle bounds = BoundsOf(target);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                throw new InvalidOperationException("Nothing to capture: the screen size is unknown.");
            }

            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"RevReel_screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");

            using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }

            bitmap.Save(path, ImageFormat.Png);
            return path;
        }

        /// <summary>The part of the combined desktop to copy, in real pixels.</summary>
        private static Rectangle BoundsOf(RecordingTarget target)
        {
            if (target.Kind == RecordingSourceKind.Window && target.Window != null)
            {
                Rectangle? window = WindowService.GetBounds(target.Window.Handle);
                if (window != null)
                {
                    return window.Value;
                }
            }

            if (target.Kind == RecordingSourceKind.Area && target.Area != null && target.Monitors.Count > 0)
            {
                MonitorInfo monitor = target.Monitors[0];
                return new Rectangle(monitor.X + target.Area.X, monitor.Y + target.Area.Y,
                    target.Area.Width, target.Area.Height);
            }

            if (target.Monitors.Count > 0)
            {
                int left = target.Monitors.Min(m => m.X);
                int top = target.Monitors.Min(m => m.Y);
                int right = target.Monitors.Max(m => m.X + m.Width);
                int bottom = target.Monitors.Max(m => m.Y + m.Height);
                return new Rectangle(left, top, right - left, bottom - top);
            }

            return Rectangle.Empty;
        }
    }
}
