using System.Drawing;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Editing;
using Windows.Storage;

namespace RevReel.Services
{
    /// <summary>
    /// Reads single pictures out of a finished video at any time, using
    /// Windows' own media-editing feature (Windows.Media.Editing). Jumping
    /// to a moment 2 hours into a long recording is quick: the whole video
    /// is not read, only the part around that moment.
    /// </summary>
    public sealed class VideoFrameService
    {
        private MediaComposition? _composition;

        public int NativeWidth { get; private set; }
        public int NativeHeight { get; private set; }
        public TimeSpan Duration { get; private set; }

        private VideoFrameService() { }

        public static async Task<VideoFrameService> OpenAsync(string videoPath)
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(videoPath).AsTask();
            MediaClip clip = await MediaClip.CreateFromFileAsync(file).AsTask();

            var properties = clip.GetVideoEncodingProperties();
            var service = new VideoFrameService
            {
                NativeWidth = (int)properties.Width,
                NativeHeight = (int)properties.Height,
                Duration = clip.OriginalDuration
            };

            service._composition = new MediaComposition();
            service._composition.Clips.Add(clip);
            return service;
        }

        /// <summary>
        /// The picture at <paramref name="time"/>. If <paramref name="crop"/> is given
        /// (in the video's own pixels) only that part is returned. The result is never
        /// wider than <paramref name="maxWidth"/>; it is made smaller, never bigger.
        /// The caller must dispose the bitmap.
        /// </summary>
        public async Task<Bitmap> GetFrameAsync(TimeSpan time, Rectangle? crop, int maxWidth)
        {
            if (_composition == null)
            {
                throw new InvalidOperationException("The video is not open.");
            }

            Rectangle region = crop ?? new Rectangle(0, 0, NativeWidth, NativeHeight);
            region.Intersect(new Rectangle(0, 0, NativeWidth, NativeHeight));
            if (region.Width < 2 || region.Height < 2)
            {
                region = new Rectangle(0, 0, NativeWidth, NativeHeight);
            }

            // Scale so the wanted part ends up about maxWidth wide, never above the original size.
            double scale = Math.Min(1.0, (double)maxWidth / region.Width);
            int thumbWidth = Math.Max(2, (int)Math.Round(NativeWidth * scale));
            int thumbHeight = Math.Max(2, (int)Math.Round(NativeHeight * scale));

            TimeSpan safeTime = time < TimeSpan.Zero ? TimeSpan.Zero
                : time >= Duration ? Duration - TimeSpan.FromMilliseconds(50)
                : time;
            if (safeTime < TimeSpan.Zero)
            {
                safeTime = TimeSpan.Zero;
            }

            using var stream = await _composition
                .GetThumbnailAsync(safeTime, thumbWidth, thumbHeight, VideoFramePrecision.NearestFrame)
                .AsTask();
            using Stream net = stream.AsStreamForRead();
            using var decoded = new Bitmap(net);

            // Cut out the wanted part into a bitmap that does not depend on the stream.
            var source = new Rectangle(
                (int)Math.Round(region.X * scale),
                (int)Math.Round(region.Y * scale),
                Math.Max(1, (int)Math.Round(region.Width * scale)),
                Math.Max(1, (int)Math.Round(region.Height * scale)));
            source.Intersect(new Rectangle(0, 0, decoded.Width, decoded.Height));
            if (source.Width < 1 || source.Height < 1)
            {
                source = new Rectangle(0, 0, decoded.Width, decoded.Height);
            }

            var result = new Bitmap(source.Width, source.Height);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.DrawImage(decoded, new Rectangle(0, 0, source.Width, source.Height), source, GraphicsUnit.Pixel);
            }

            return result;
        }
    }
}
