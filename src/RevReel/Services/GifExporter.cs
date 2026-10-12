using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using Openize.Animated.GIF;
using RevReel.Models;

namespace RevReel.Services
{
    /// <summary>Builds one looping animated GIF from a list of picture files.</summary>
    public static class GifExporter
    {
        /// <summary>
        /// Encodes <paramref name="frameFiles"/> (oldest first) into a GIF.
        /// Pictures wider than <paramref name="maxWidth"/> are scaled down,
        /// keeping their shape, so the GIF stays a sensible size.
        /// If anything fails, the half-written GIF is deleted.
        /// </summary>
        public static void Create(IReadOnlyList<string> frameFiles, string outputGifPath, int frameDelayMillis, int maxWidth)
        {
            if (frameFiles.Count == 0)
            {
                throw new InvalidOperationException("There are no pictures to build a GIF from.");
            }

            try
            {
                var encoder = new AnimatedGifEncoder();
                encoder.Start(outputGifPath);
                encoder.SetDelay(frameDelayMillis);
                encoder.SetRepeat(0); // 0 = loop forever

                foreach (string file in frameFiles)
                {
                    // One picture is held in memory at a time.
                    using var source = new Bitmap(file);

                    if (source.Width <= maxWidth)
                    {
                        encoder.AddFrame(source);
                        continue;
                    }

                    int width = maxWidth;
                    int height = Math.Max(1, (int)Math.Round(source.Height * (double)width / source.Width));
                    using var scaled = new Bitmap(width, height);
                    using (Graphics g = Graphics.FromImage(scaled))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(source, 0, 0, width, height);
                    }

                    encoder.AddFrame(scaled);
                }

                encoder.Finish();
            }
            catch
            {
                try { File.Delete(outputGifPath); } catch { }
                throw;
            }
        }

        /// <summary>
        /// Builds a GIF from part of a finished video: from <paramref name="start"/> to
        /// <paramref name="end"/>, optionally cropped, at <paramref name="framesPerSecond"/>.
        /// Reports progress from 0 to 1. If cancelled or if anything fails, the
        /// half-written GIF is deleted.
        /// </summary>
        public static async Task CreateFromVideoAsync(VideoFrameService video, TimeSpan start, TimeSpan end,
            Rectangle? crop, int framesPerSecond, int maxWidth, string outputGifPath,
            IProgress<double>? progress, CancellationToken cancel, IReadOnlyList<TextOverlay>? texts = null, bool loop = true)
        {
            if (end <= start)
            {
                throw new InvalidOperationException("The end time must be after the start time.");
            }

            int frameCount = Math.Max(1, (int)Math.Ceiling((end - start).TotalSeconds * framesPerSecond));
            int delayMillis = 1000 / framesPerSecond;

            // The part of the video the frames show, in video pixels (the text positions use the same pixels).
            Rectangle region = crop ?? new Rectangle(0, 0, video.NativeWidth, video.NativeHeight);
            region.Intersect(new Rectangle(0, 0, video.NativeWidth, video.NativeHeight));

            try
            {
                var encoder = new AnimatedGifEncoder();
                encoder.Start(outputGifPath);
                encoder.SetDelay(delayMillis);
                if (loop)
                {
                    encoder.SetRepeat(0); // 0 = loop forever
                }
                // Without SetRepeat the GIF has no loop instruction, so it plays once and stops on the last frame.

                for (int i = 0; i < frameCount; i++)
                {
                    cancel.ThrowIfCancellationRequested();

                    TimeSpan time = start + TimeSpan.FromSeconds((double)i / framesPerSecond);
                    using Bitmap frame = await video.GetFrameAsync(time, crop, maxWidth).ConfigureAwait(false);

                    if (texts is { Count: > 0 })
                    {
                        TextOverlayRenderer.Draw(frame, texts, region, time.TotalSeconds, start.TotalSeconds);
                    }

                    encoder.AddFrame(frame);

                    progress?.Report((i + 1) / (double)frameCount);
                }

                encoder.Finish();
            }
            catch
            {
                try { File.Delete(outputGifPath); } catch { }
                throw;
            }
        }
    }
}
