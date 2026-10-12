using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace RevReel.Services
{
    /// <summary>
    /// System-wide keyboard shortcuts that work even when another program
    /// (a game, for example) has the focus. Uses the Windows RegisterHotKey
    /// call. Keys are added once with <see cref="Add"/>, then switched on
    /// and off with <see cref="RegisterAll"/> and <see cref="UnregisterAll"/>.
    /// </summary>
    public sealed class HotkeyService : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private sealed record Hotkey(int Id, uint Modifiers, uint VirtualKey, string Label, bool Always, Action Pressed);

        private readonly List<Hotkey> _hotkeys = new();
        private readonly HashSet<int> _active = new();
        private HwndSource? _source;
        private IntPtr _hwnd;

        /// <summary>Connects to the window that receives the key messages. Call once, after the window has a handle.</summary>
        public void Attach(Window window)
        {
            _hwnd = new WindowInteropHelper(window).EnsureHandle();
            _source = HwndSource.FromHwnd(_hwnd);
            _source?.AddHook(WndProc);
        }

        /// <summary>
        /// Defines a shortcut. It does nothing until <see cref="RegisterAll"/> is called.
        /// A shortcut marked <paramref name="always"/> stays on when <see cref="UnregisterAll"/> is called
        /// (used for Record and Screenshot, which must work while idle).
        /// </summary>
        public void Add(ModifierKeys modifiers, Key key, string label, Action pressed, bool always = false)
        {
            uint mods = 0;
            if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= MOD_ALT;
            if (modifiers.HasFlag(ModifierKeys.Control)) mods |= MOD_CONTROL;
            if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= MOD_SHIFT;
            if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= MOD_WIN;

            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            _hotkeys.Add(new Hotkey(_hotkeys.Count + 1, mods, vk, label, always, pressed));
        }

        /// <summary>
        /// Switches the shortcuts on. Returns the labels of any that another program already uses.
        /// With <paramref name="alwaysOnly"/> only the "always on" shortcuts are switched on (used at start-up).
        /// </summary>
        public IReadOnlyList<string> RegisterAll(bool alwaysOnly = false)
        {
            var failed = new List<string>();
            if (_hwnd == IntPtr.Zero)
            {
                return failed;
            }

            foreach (Hotkey hotkey in _hotkeys)
            {
                if (_active.Contains(hotkey.Id) || (alwaysOnly && !hotkey.Always))
                {
                    continue;
                }

                if (RegisterHotKey(_hwnd, hotkey.Id, hotkey.Modifiers | MOD_NOREPEAT, hotkey.VirtualKey))
                {
                    _active.Add(hotkey.Id);
                }
                else
                {
                    failed.Add(hotkey.Label);
                }
            }

            return failed;
        }

        /// <summary>Switches off the shortcuts that only matter while recording, so other programs can use those keys again.</summary>
        public void UnregisterAll() => Unregister(includeAlways: false);

        private void Unregister(bool includeAlways)
        {
            foreach (int id in _active.ToList())
            {
                Hotkey? hotkey = _hotkeys.FirstOrDefault(h => h.Id == id);
                if (hotkey != null && hotkey.Always && !includeAlways)
                {
                    continue;
                }

                UnregisterHotKey(_hwnd, id);
                _active.Remove(id);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                Hotkey? hotkey = _hotkeys.FirstOrDefault(h => h.Id == id);
                if (hotkey != null && _active.Contains(id))
                {
                    handled = true;
                    hotkey.Pressed();
                }
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            Unregister(includeAlways: true);
            _source?.RemoveHook(WndProc);
            _source = null;
        }
    }
}
