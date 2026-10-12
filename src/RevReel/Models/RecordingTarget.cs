namespace RevReel.Models
{
    /// <summary>
    /// Exactly what the next recording will capture, worked out from the
    /// settings and the monitors that are connected right now.
    /// </summary>
    public sealed class RecordingTarget
    {
        public RecordingSourceKind Kind { get; init; }

        /// <summary>The monitors being captured (one, or all of them). Empty only if Windows listed none.</summary>
        public IReadOnlyList<MonitorInfo> Monitors { get; init; } = Array.Empty<MonitorInfo>();

        /// <summary>Set only when <see cref="Kind"/> is Area.</summary>
        public AreaSelection? Area { get; init; }

        /// <summary>Size of the video in pixels before any output-size preset. 0 if unknown.</summary>
        public int Width { get; init; }
        public int Height { get; init; }

        /// <summary>Plain-words description for the status line.</summary>
        public string Description { get; init; } = "the main monitor";

        /// <summary>The monitor where pop-up messages should appear.</summary>
        public MonitorInfo? ToastMonitor { get; init; }

        /// <summary>
        /// Works out the target. If the saved choice cannot be used (a monitor
        /// was unplugged, or no area has been chosen), the main monitor is
        /// used and <paramref name="note"/> explains why.
        /// </summary>
        public static RecordingTarget Resolve(AppSettings settings, AreaSelection? area,
            IReadOnlyList<MonitorInfo> monitors, out string? note)
        {
            note = null;

            if (monitors.Count == 0)
            {
                return new RecordingTarget { Kind = RecordingSourceKind.Monitor };
            }

            MonitorInfo main = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];

            if (settings.SourceKind == RecordingSourceKind.Area)
            {
                MonitorInfo? areaMonitor = area == null
                    ? null
                    : monitors.FirstOrDefault(m => string.Equals(m.DeviceName, area.DeviceName, StringComparison.OrdinalIgnoreCase));

                if (area != null && areaMonitor != null)
                {
                    return new RecordingTarget
                    {
                        Kind = RecordingSourceKind.Area,
                        Monitors = new[] { areaMonitor },
                        Area = area,
                        Width = area.Width,
                        Height = area.Height,
                        Description = $"an area ({area.DisplayText})",
                        ToastMonitor = areaMonitor
                    };
                }

                note = area == null
                    ? "No area has been chosen yet, so the main monitor is being recorded. Use Option > Record source > Select an area."
                    : "The monitor with your chosen area is not connected, so the main monitor is being recorded.";
            }
            else if (settings.SourceKind == RecordingSourceKind.AllMonitors && monitors.Count > 1)
            {
                int left = monitors.Min(m => m.X);
                int top = monitors.Min(m => m.Y);
                int right = monitors.Max(m => m.X + m.Width);
                int bottom = monitors.Max(m => m.Y + m.Height);

                return new RecordingTarget
                {
                    Kind = RecordingSourceKind.AllMonitors,
                    Monitors = monitors,
                    Width = right - left,
                    Height = bottom - top,
                    Description = $"all {monitors.Count} monitors ({right - left} x {bottom - top})",
                    ToastMonitor = main
                };
            }
            else if (settings.SourceKind == RecordingSourceKind.Monitor && !string.IsNullOrEmpty(settings.MonitorDeviceName))
            {
                MonitorInfo? chosen = monitors.FirstOrDefault(m =>
                    string.Equals(m.DeviceName, settings.MonitorDeviceName, StringComparison.OrdinalIgnoreCase));

                if (chosen != null)
                {
                    return SingleMonitor(chosen);
                }

                note = "The monitor you chose is not connected, so the main monitor is being recorded.";
            }

            return SingleMonitor(main);
        }

        private static RecordingTarget SingleMonitor(MonitorInfo monitor) => new()
        {
            Kind = RecordingSourceKind.Monitor,
            Monitors = new[] { monitor },
            Width = monitor.Width,
            Height = monitor.Height,
            Description = monitor.DisplayText,
            ToastMonitor = monitor
        };
    }
}
