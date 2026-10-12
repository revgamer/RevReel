using System.IO;

namespace RevReel.Services
{
    /// <summary>
    /// Holds the last few seconds of the recording as small pictures so a
    /// replay GIF can be made at any moment.
    ///
    /// While recording, ScreenRecorderLib saves a JPEG into <see cref="Folder"/>
    /// every <see cref="IntervalMillis"/> milliseconds and reports each one to
    /// <see cref="AddFrame"/>. Only the newest few seconds are kept: older
    /// pictures are deleted as new ones arrive, so disk use stays small
    /// however long the recording runs.
    /// </summary>
    public sealed class ReplayBuffer
    {
        /// <summary>Time between pictures. 100 ms = 10 pictures per second.</summary>
        public const int IntervalMillis = 100;

        /// <summary>Longest replay that can be saved, in seconds.</summary>
        public const int MaxSeconds = 10;

        // Pictures kept beyond MaxSeconds, so a request never runs short.
        private const int ExtraSeconds = 3;

        // GIFs are scaled down to this width (pixels) to keep them small.
        private const int MaxGifWidth = 800;

        private readonly object _lock = new();
        private readonly List<string> _frames = new();
        private int _building;
        private bool _released;

        /// <summary>Temporary folder the pictures are written to.</summary>
        public string Folder { get; }

        public ReplayBuffer()
        {
            Folder = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "RevReel",
                $"replay_{DateTime.Now:yyyyMMdd_HHmmss}_{Environment.ProcessId}");
            Directory.CreateDirectory(Folder);
        }

        /// <summary>True while a GIF is being built.</summary>
        public bool IsBuilding
        {
            get { lock (_lock) { return _building > 0; } }
        }

        /// <summary>Called for every new picture. Keeps the newest ones and deletes the rest.</summary>
        public void AddFrame(string path)
        {
            lock (_lock)
            {
                if (_released)
                {
                    TryDeleteFile(path);
                    return;
                }

                _frames.Add(path);
                Trim();
            }
        }

        /// <summary>
        /// Builds a GIF of the last <paramref name="seconds"/> seconds and
        /// saves it to <paramref name="outputPath"/>. Runs on a background
        /// thread. If fewer pictures exist than requested (recording only
        /// just started), the GIF is built from what there is.
        /// </summary>
        public async Task SaveGifAsync(int seconds, string outputPath)
        {
            string[] chosen;
            lock (_lock)
            {
                if (_released)
                {
                    throw new InvalidOperationException("The recording has already ended.");
                }

                int wanted = Math.Clamp(seconds, 1, MaxSeconds) * 1000 / IntervalMillis;
                chosen = _frames.Skip(Math.Max(0, _frames.Count - wanted)).ToArray();
                if (chosen.Length == 0)
                {
                    throw new InvalidOperationException("No pictures have been captured yet. Try again in a moment.");
                }

                // While a GIF is being built nothing is deleted, so the chosen pictures stay in place.
                _building++;
            }

            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath) ?? ".");
                await Task.Run(() => GifExporter.Create(chosen, outputPath, IntervalMillis, MaxGifWidth));
            }
            finally
            {
                bool deleteFolderNow;
                lock (_lock)
                {
                    _building--;
                    deleteFolderNow = _released && _building == 0;
                    if (!deleteFolderNow)
                    {
                        Trim();
                    }
                }

                if (deleteFolderNow)
                {
                    TryDeleteFolder();
                }
            }
        }

        /// <summary>Recording has ended: no more pictures are needed. Deletes the temporary folder.</summary>
        public void Release()
        {
            bool deleteFolderNow;
            lock (_lock)
            {
                _released = true;
                deleteFolderNow = _building == 0;
            }

            if (deleteFolderNow)
            {
                TryDeleteFolder();
            }
        }

        /// <summary>
        /// Removes temporary folders left behind by an earlier crash
        /// (anything older than one day). Safe to call at start-up.
        /// </summary>
        public static void CleanOldFolders()
        {
            try
            {
                string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevReel");
                if (!Directory.Exists(root))
                {
                    return;
                }

                foreach (string dir in Directory.GetDirectories(root, "replay_*"))
                {
                    if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(dir) > TimeSpan.FromDays(1))
                    {
                        try { Directory.Delete(dir, recursive: true); } catch { }
                    }
                }
            }
            catch
            {
                // Cleaning up is a courtesy. It must never stop the app.
            }
        }

        // Caller must hold _lock.
        private void Trim()
        {
            if (_building > 0)
            {
                return;
            }

            int keep = (MaxSeconds + ExtraSeconds) * 1000 / IntervalMillis;
            while (_frames.Count > keep)
            {
                TryDeleteFile(_frames[0]);
                _frames.RemoveAt(0);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try { File.Delete(path); } catch { }
        }

        private void TryDeleteFolder()
        {
            try { Directory.Delete(Folder, recursive: true); } catch { }
        }
    }
}
