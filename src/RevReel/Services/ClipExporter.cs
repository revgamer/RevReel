using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace RevReel.Services
{
    /// <summary>
    /// Saves part of a finished recording as a new MP4 (a trimmed clip), using
    /// Windows' own media-editing feature. The picture size of the original
    /// is kept. The clip is re-encoded, so building it takes a little while.
    /// The original recording is never changed.
    /// </summary>
    public static class ClipExporter
    {
        public static async Task CreateClipAsync(string videoPath, string outputPath, TimeSpan start, TimeSpan end)
        {
            StorageFile input = await StorageFile.GetFileFromPathAsync(videoPath).AsTask();
            MediaClip clip = await MediaClip.CreateFromFileAsync(input).AsTask();

            if (start > TimeSpan.Zero)
            {
                clip.TrimTimeFromStart = start;
            }

            TimeSpan trimFromEnd = clip.OriginalDuration - end;
            if (trimFromEnd > TimeSpan.Zero)
            {
                clip.TrimTimeFromEnd = trimFromEnd;
            }

            var composition = new MediaComposition();
            composition.Clips.Add(clip);

            // The output file must exist before Windows can write into it.
            File.Create(outputPath).Dispose();
            try
            {
                StorageFile output = await StorageFile.GetFileFromPathAsync(outputPath).AsTask();

                // Same size and settings as the original recording.
                MediaEncodingProfile profile = await MediaEncodingProfile.CreateFromFileAsync(input).AsTask();
                await composition.RenderToFileAsync(output, MediaTrimmingPreference.Precise, profile).AsTask();
            }
            catch
            {
                try { File.Delete(outputPath); } catch { }
                throw;
            }
        }
    }
}
