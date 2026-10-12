using System.IO;
using System.Text.Json;

namespace RevReel.Services
{
    /// <summary>
    /// Saves bookmark times next to the video, in a small file named
    /// "(video name).mp4.bookmarks.json". It is written every time a
    /// bookmark is added, so bookmarks are not lost if RevReel closes
    /// unexpectedly, and they are there again when the video is opened later.
    /// </summary>
    public static class BookmarkStore
    {
        public sealed record BookmarkFile(List<double> Seconds);

        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

        public static string PathFor(string videoPath) => videoPath + ".bookmarks.json";

        /// <summary>Returns true if the file was written.</summary>
        public static bool Save(string videoPath, IReadOnlyList<double> seconds)
        {
            try
            {
                string json = JsonSerializer.Serialize(new BookmarkFile(seconds.ToList()), Options);
                File.WriteAllText(PathFor(videoPath), json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Bookmark times in seconds, oldest first. Empty if there is no file or it cannot be read.</summary>
        public static List<double> Load(string videoPath)
        {
            try
            {
                string path = PathFor(videoPath);
                if (!File.Exists(path))
                {
                    return new List<double>();
                }

                BookmarkFile? data = JsonSerializer.Deserialize<BookmarkFile>(File.ReadAllText(path));
                return data?.Seconds?.Where(s => s >= 0).OrderBy(s => s).ToList() ?? new List<double>();
            }
            catch
            {
                return new List<double>();
            }
        }
    }
}
