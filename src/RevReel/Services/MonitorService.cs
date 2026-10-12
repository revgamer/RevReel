using System.Runtime.InteropServices;
using RevReel.Models;

namespace RevReel.Services
{
    /// <summary>Lists the monitors that are connected right now, using Windows directly.</summary>
    public static class MonitorService
    {
        private const uint MONITORINFOF_PRIMARY = 1;

        private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public int Size;
            public RECT Monitor;
            public RECT Work;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

        /// <summary>All connected monitors, in the same order as the numbers in Windows Display settings.</summary>
        public static IReadOnlyList<MonitorInfo> GetAll()
        {
            var result = new List<MonitorInfo>();

            try
            {
                MonitorEnumProc callback = (IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
                {
                    var info = new MONITORINFOEX { Size = Marshal.SizeOf<MONITORINFOEX>() };
                    if (GetMonitorInfo(monitor, ref info))
                    {
                        result.Add(new MonitorInfo(
                            info.DeviceName,
                            info.Monitor.Left,
                            info.Monitor.Top,
                            info.Monitor.Right - info.Monitor.Left,
                            info.Monitor.Bottom - info.Monitor.Top,
                            (info.Flags & MONITORINFOF_PRIMARY) != 0));
                    }

                    return true;
                };

                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
                GC.KeepAlive(callback);
            }
            catch
            {
                // Windows would not list the monitors. The caller falls back to the main monitor.
            }

            return result.OrderBy(m => m.Number).ToList();
        }
    }
}
