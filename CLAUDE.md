# CLAUDE.md - RevReel

Guide for Claude (and any developer) working on **RevReel**, a Windows screen recorder with bookmarks, instant replay GIFs, screenshots and a GIF maker.
Read this file fully before changing code.

---

## 1. Project at a glance

| Item | Value |
| --- | --- |
| Name | RevReel (formerly "RevLens") |
| Author | **RevGamer** (the person) |
| Business / brand | **Rev Creative Developer** (RevGamer's side home business) |
| Repository | https://github.com/revgamer/RevReel |
| Licence | MIT (see `LICENSE`) |
| Version | 0.1.0 (set in `src/RevReel/RevReel.csproj`, shown by `AppInfo`) |
| Platform | Windows 10 version 1809+ (target `net8.0-windows10.0.19041.0`), **x64 only** |
| UI | WPF, .NET 8, C# with nullable enabled and implicit usings |
| Main project | `src/RevReel/RevReel.csproj` |
| Solution | `RevReel.sln` (repo root) |
| Other folders | `Installer/` (empty, planned), `docs/` (empty, planned) |

### What the app does
- Records a **whole monitor**, **all monitors in one wide video**, **one program window**, or a **custom area** to MP4.
- **Record** toggles start/stop. Global hotkeys work inside games.
- **Bookmarks** mark moments while recording (saved to `<video>.mp4.bookmarks.json`).
- **Replay GIFs**: GIF of the last 3, 5 or 10 seconds, taken from a rolling buffer while recording.
- **Screenshot** of exactly what would be recorded (PNG).
- **GIF Maker**: open a finished MP4, trim, crop, add animated text, make a looping or play-once GIF.
- Update check against GitHub releases.

---

## 2. How the user works (IMPORTANT)

The owner (RevGamer) is **Deaf** and builds with copy-paste terminal commands.

### Communication and accessibility rules
1. **No feature may depend on sound.** No beeps, spoken prompts or audio-only cues.
2. **State is always shown in words**, never by colour alone. Example: "Recording" next to the red dot; the chosen mode button has an outline **and** the line "Recording: Whole screen (...)".
3. Every control needs `AutomationProperties.Name` (and `HelpText` when helpful). Status text uses `AutomationProperties.LiveSetting="Polite"`.
4. Everything must be keyboard-usable (Alt mnemonics, tab order, Enter/Esc in dialogs).
5. Messages and pop-ups are written out in plain, short English.
6. Replies to the user: structured, written step-by-step, complete copy-paste code, name exactly where files go, say what was wrong and show the corrected version. Keep it terse and direct. Flag mistakes immediately.
7. Prefer **surgical edits** for small changes, complete files for new files. Preserve existing names and structure unless there is a clear reason.
8. Do not invent APIs, class names or options. If unsure, say so and ask for the file or code.

### Build workflow
Claude's container has **no .NET SDK**, so Claude cannot compile. The user builds and reports errors/screenshots. Always finish a change with these commands:

```
cd G:\11_Coding\RevReel
dotnet build RevReel.sln -c Debug -p:Platform=x64
dotnet run --project src\RevReel\RevReel.csproj -c Debug -p:Platform=x64
```

Never claim something "works" unless the user confirmed it. Say "not built yet" when it has not been.
Do not commit or push unless asked. Commit attribution lines come from the session reminder.

### Paths
- Project on the user's PC: `G:\11_Coding\RevReel` (code in `src\RevReel`).
- Settings: `%AppData%\RevReel\settings.json`.
- Default recordings: `Videos\RevReel` (`AppSettings.DefaultOutputFolder`).
- Replay picture buffer: `%Temp%\RevReel\...` (deleted on stop; old folders cleaned at start by `ReplayBuffer.CleanOldFolders`).

---

## 3. Tech stack and packages

| Package | Version | Licence | Used for |
| --- | --- | --- | --- |
| ScreenRecorderLib | 7.0.1 | MIT | Capture + H.264 encoding, snapshots for replay |
| Openize.Animated-GIF | 25.5.0 | Apache-2.0 | `AnimatedGifEncoder` (Start, SetDelay, SetRepeat, AddFrame, Finish) |
| System.Drawing.Common | 8.0.10 | MIT | GDI+ drawing (text overlays, screenshots, frames) |

Other APIs used: Windows.Media.Editing (`MediaComposition`, trimmed MP4 export in `ClipExporter`), Win32 via P/Invoke (see section 7).

ScreenRecorderLib facts that matter:
- `DisplayRecordingSource(deviceName)` with `SourceRect` for areas (X/Y relative to that monitor's top-left).
- `WindowRecordingSource(IntPtr handle)` for windows.
- `Recorder.CreateRecorder(options)`; events `OnRecordingComplete`, `OnRecordingFailed`, `OnSnapshotSaved` arrive on **background threads**.
- Bitrate is chosen by `RecordingService.ChooseBitrate` from size x framerate.

---

## 4. Repository layout

```
RevReel/
  CLAUDE.md                 <- this file
  README.md                 <- user-facing docs (keep in sync with features)
  LICENSE
  RevReel.sln
  Installer/  docs/         <- empty placeholders
  src/RevReel/
    App.xaml(.cs)           app start, resources
    AppInfo.cs              name, version, URLs, copyright (single source of truth)
    MainWindow.xaml(.cs)    the compact toolbar window (see section 5)
    app.manifest            PerMonitorV2 DPI awareness
    Resources/Styles.xaml   brushes + styles (dark theme)
    Models/                 plain data types
    Services/               logic with no UI
    Views/                  secondary windows and visual helpers
```

### Models (`src/RevReel/Models`)
| File | Purpose |
| --- | --- |
| `AppSettings.cs` | Saved options: `OutputFolder`, `Framerate` (30/60), `OutputSize`, `AlwaysOnTop`, `SourceKind`, `MonitorDeviceName`, `ReplayGifEnabled` |
| `RecordingSourceKind.cs` | `Monitor`, `AllMonitors`, `Area`, `Window` |
| `RecordingTarget.cs` | Exactly what the next recording/screenshot captures. `Resolve(settings, area, monitors, out note, window)` falls back to the main monitor and fills `note` when a saved choice is unusable |
| `RecordingState.cs` | `Idle`, `Recording`, `Stopping` |
| `MonitorInfo.cs` | Device name, X/Y/Width/Height (real pixels, can be negative), `IsPrimary`, `DisplayText` |
| `AreaSelection.cs` | Area on one monitor (X/Y relative to monitor) |
| `WindowSelection.cs` | `Handle`, `Title`, `ProcessName`, `DisplayText`, `ShortTitle` |
| `OutputSizePreset.cs` | Automatic, 720p, 1080p, 1440p, 4K |
| `TextOverlay.cs` | One GIF text: text, font, size 12-64, RGB, bold/italic/underline/outline, timing, animation, speed, X/Y |
| `TextAnimation.cs` | None, Flash, Fade, Shake, Broken, Typewriter, Rainbow (order = list order in the dialog) |

### Services (`src/RevReel/Services`)
| File | Purpose |
| --- | --- |
| `RecordingService.cs` | Wraps ScreenRecorderLib. `Start(settings, target)`, `Stop()`, `BuildSources(target)`, replay GIF saving, events `StatusMessage`, `Completed`, `Failed` |
| `ReplayBuffer.cs` | Rolling temp pictures for replay GIFs |
| `HotkeyService.cs` | `RegisterHotKey`/`WM_HOTKEY`. `Add(mods, key, label, action, always: false)`, `RegisterAll(alwaysOnly)`, `UnregisterAll()` (keeps "always" keys), `Dispose()` (removes everything) |
| `MonitorService.cs` | `EnumDisplayMonitors` list, ordered like Windows Display settings |
| `WindowService.cs` | `EnumWindows` list of recordable windows (visible, titled, not minimised, not cloaked, not tool windows, not RevReel), `GetBounds` (DWM extended frame bounds), `MonitorOf`, `IsStillOpen` |
| `ScreenshotService.cs` | `Capture(target, folder)` -> `RevReel_screenshot_yyyy-MM-dd_HH-mm-ss.png` via `Graphics.CopyFromScreen` |
| `CaptureVisibility.cs` | `SetExcluded(hwnd, bool)` / `ExcludeFromCapture(hwnd)` using `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` |
| `ToastService.cs` | Pop-up messages on the recorded monitor |
| `BookmarkStore.cs` | Save/load bookmark seconds as JSON next to the video |
| `ClipExporter.cs` | Trimmed MP4 export (Windows.Media.Editing) |
| `VideoFrameService.cs` | Reads frames from an MP4 for the GIF maker |
| `GifExporter.cs` | `CreateFromVideoAsync(video, start, end, crop, fps, maxWidth, output, progress, cancel, texts, loop)` |
| `TextOverlayRenderer.cs` | One GDI+ renderer used by BOTH preview and GIF output (guarantees WYSIWYG) |
| `ColorMath.cs` | HSV <-> RGB for the colour picker |
| `TimeText.cs` | `Format`/`TryParse` for `hh:mm:ss.f` |
| `SettingsService.cs` | `settings.json` load/save (enum-as-string, `Sanitize`) |
| `UpdateService.cs` | GitHub latest-release check |

### Views (`src/RevReel/Views`)
| File | Purpose |
| --- | --- |
| `GifEditorWindow` | GIF maker: preview, range sliders, crop box, text list, loop/play-once, Make GIF |
| `TextEditWindow` | Text dialog with live animated "Look" preview |
| `ColorPickerWindow` | Illustrator-style picker (saturation/brightness field, hue bar, H/S/B/R/G/B/hex) |
| `TextOverlayVisual.cs` | Builds the draggable text image + selection frame on the preview |
| `WindowPickerWindow` | List of open windows to record |
| `AreaSelectWindow` | Dimmed full-screen area chooser (all monitors) |
| `AreaBorderWindow` | Red outline around the area while recording (hidden from capture) |
| `ToastWindow` | The pop-up itself (hidden from capture) |
| `AboutWindow` | Name, version, links, licence |

---

## 5. Main window (`MainWindow.xaml` / `.xaml.cs`)

Compact "Simple Record"-style bar, 600 px wide, `SizeToContent="Height"`, custom title bar via `WindowChrome` (`WindowStyle="None"`, `ResizeMode="CanMinimize"`, caption height 44).

### Layout (top to bottom)
1. **Title bar**: "RevReel", recording dot + state word + timer, then menu button (three lines), minimise, close. Title buttons use `WindowChrome.IsHitTestVisibleInChrome="True"` and Segoe MDL2 Assets glyphs (E700 menu, E921 minimise, E8BB close).
2. **Update banner** (hidden until an update is found).
3. **Six icon buttons** in a `UniformGrid Columns="6"`:

| Button | Control | Mnemonic | Action |
| --- | --- | --- | --- |
| Whole Screen | `WholeScreenRadio` | Alt+W | One monitor. With 2+ monitors a menu lists each monitor and "All monitors" |
| A Window | `WindowRadio` | Alt+N | Opens `WindowPickerWindow` |
| Custom Area | `AreaRadio` | Alt+A | Opens the dimmed area chooser (`AreaSelector.SelectAsync`) |
| Record / Stop | `RecordButton` | Alt+R | `ToggleRecording()`; label/icon swap while recording |
| Screenshot | button | Alt+C | `TakeScreenshot()` |
| GIF Maker | `MakeGifButton` | Alt+G | Pick an MP4, open `GifEditorWindow` |

4. **`SourceText`**: "Recording: Whole screen (Monitor 1 (1920 x 1080), main)" plus any fallback note.
5. **Footer**: `StatusText` (every message goes here) and **Show file** button (Alt+L).

The menu button opens `MainMenu` (a `ContextMenu`) containing **File**, **Option**, **Help**. The x:Names of those items (`MenuReplay`, `MenuSource`, `MenuFrameRate`, ...) are used by code-behind.

### Important behaviours
- The three mode buttons are `RadioButton`s styled `ToolRadio`. **`UpdateModeButtons()` is the single place that decides which one is outlined** (from the *resolved* target, not the raw setting). Call it after every choice, including cancelled ones. `RebuildSourceMenu()` calls it.
- `ResolveTarget(out note)` is the one helper for "what will be captured".
- `ShowState(isRecording)` is the single place that enables/disables controls and swaps Record/Stop. It also excludes the main window from capture while recording.
- Area and window choices are **not remembered between runs**; at start-up `Area`/`Window` fall back to `Monitor`.
- Closing while recording asks to stop and save first (`OnClosing`); never leave an unfinished MP4.
- Recorder events come from background threads: always `Dispatcher.BeginInvoke` before touching controls.
- Settings cannot change while recording (menus and mode buttons are disabled).

### Hotkeys (global, work while a game has focus)
| Keys | Action | Registered |
| --- | --- | --- |
| Ctrl+Alt+F10 | Start/stop recording | always |
| Ctrl+Alt+F9 | Screenshot | always |
| Ctrl+Alt+F8 | Add bookmark | only while recording |
| Ctrl+Alt+F5 / F6 / F7 | Replay GIF 3 s / 5 s / 10 s | only while recording |

Other keys inside the window: F1 shortcuts list, Ctrl+O open save folder, Alt+F4 exit.
If a key is already used by another program, `RegisterAll` returns its label and the status line tells the user to use the buttons.

---

## 6. GIF maker details

- Opens an MP4, shows it in a `MediaElement` placed in a **Canvas** (a Grid clips it to the host size).
- Default range is the **full length**; Stop returns to 00:00:00 (the Start point).
- Crop box: preview uses `Player.Clip` (clip is applied **before** `RenderTransform`).
- GIF settings are constants: `GifFramesPerSecond`, `GifMaxWidth`. Loop = `encoder.SetRepeat(0)`; **play once = do not call `SetRepeat`**.
- **Text overlays**
  - Size 12-64 is relative to an **800 px wide GIF** (`TextOverlayRenderer.ReferenceWidth`), so text looks the same on any recording size.
  - Position X/Y in video pixels; drag on the preview.
  - Show from/until times, or "always shown".
  - Animations: Flash, Fade, Shake, Broken letters, Typewriter, Rainbow, with speed 0.5x/1x/2x.
  - Animation time = `TextOverlay.AnimationTime(time, gifStart)`: since GIF start for always-shown text, otherwise since `FromSeconds`.
  - Preview and output both call `TextOverlayRenderer` (`RenderPreview` for the dialog/preview, `Draw` for frames).
  - Colour only via the custom picker (no colour dropdown).
- Text in **trimmed MP4** is not supported yet (GIF only).

---

## 7. Win32 / platform notes

| API | Where | Notes |
| --- | --- | --- |
| `RegisterHotKey` / `UnregisterHotKey` | `HotkeyService` | `MOD_NOREPEAT` is always added |
| `SetWindowDisplayAffinity` (0x11 = exclude from capture) | `CaptureVisibility` | Needs Windows 10 2004+. Pass 0 to show the window in captures again |
| `EnumDisplayMonitors`, `GetMonitorInfo` | `MonitorService` | |
| `EnumWindows`, `GetWindowText`, `GetWindowLong(GWL_EXSTYLE)`, `IsIconic`, `IsWindowVisible` | `WindowService` | |
| `DwmGetWindowAttribute` (9 = extended frame bounds, 14 = cloaked) | `WindowService` | Bounds without the invisible resize border |
| `GetSystemMetrics` | `RecordingService` | Fallback size for bitrate |

The manifest sets **PerMonitorV2** DPI, so all monitor/window/area numbers are **real pixels**. Do not mix in WPF device-independent units without converting (`VisualTreeHelper.GetDpi`).

---

## 8. Coding conventions

- C# 12 / .NET 8, `Nullable` and `ImplicitUsings` enabled. **Implicit usings do not include `System.IO`** - add it.
- File-scoped logic stays in the existing style: block namespaces (`namespace RevReel.X { ... }`), 4 spaces, braces on new lines.
- XML doc comments (`/// <summary>`) on public members; write comments for a non-expert reader, in plain English.
- Services contain no UI types. Windows and views talk to services, never the other way round.
- Put shared constants in one place (`AppInfo` for names/URLs/versions).
- No duplicated P/Invoke: reuse `CaptureVisibility`, `WindowService`, `MonitorService`.
- Catch exceptions at the edge (UI handlers, service entry points) and turn them into a status message; never let a damaged settings or bookmark file stop the app starting.
- Strings shown to the user: short, plain, no jargon, and they must also appear in the status line (not only a pop-up).

### XAML / theme
Brushes (in `Resources/Styles.xaml`): `BackgroundBrush #1A1D23`, `PanelBrush #232831`, `TextBrush`, `MutedTextBrush #9AA0AA`, `AccentBrush #FF6A00`, `RecordBrush #E5484D`, `SecondaryBrush #323945`.
Styles: `PrimaryButton`, `SecondaryButton`, dark `Menu`, `MenuItem` (role-based template), `ContextMenu`, `Separator`. Main-window-only styles (`TitleButton`, `CloseTitleButton`, `ToolButton`, `ToolRadio`, `IconLabel`) live in `MainWindow.xaml` resources.
Icon colours: monitor blue `#4DA3FF`, window purple `#B58CFF`, area teal `#2DD4BF`, record red `#E5484D`, GIF orange `#FF9F43`.

---

## 9. WPF pitfalls already hit (do not repeat)

1. `MediaElement` inside a `Grid` is clipped to the host size -> use a `Canvas`.
2. `Clip` is applied before `RenderTransform`.
3. In `TextBlock`, `_` is literal. Mnemonics only work in `Label`, `AccessText`, and button/menu content strings.
4. A named control must not hide an inherited member (a field called `TextInput` hid `UIElement.TextInput`; renamed `WordsBox`).
5. `using System.Windows.Shapes;` collides with `System.IO.Path` - use an alias (`Shapes`).
6. `ShowDialog()` disables other windows of the app.
7. Menu text in a `ContextMenu` was unreadable until a dark `ContextMenu` style existed. Keep it.
8. A `ContextMenu` popup has no transparency, so the style uses square corners.
9. Radio button state can disagree with the real mode after a cancelled choice -> always call `UpdateModeButtons()`.
10. `x:Name` on elements inside `Button.ContextMenu` still generates fields; fine to use.
11. `System.Drawing` and `System.Windows` both define `Point`, `Size`, `Color`, `Rectangle`: alias (`Drawing = System.Drawing`) or fully qualify.
12. Python/`sed` patches of C# must be checked; a malformed patch once left a SyntaxError (no changes applied).

---

## 10. Testing checklist (manual - there are no automated tests yet)

1. Each mode button: outline matches the "Recording:" line.
2. Two monitors: Whole Screen menu lists both + "All monitors"; each records the right screen.
3. A Window: record 5 s, play back; close the window first -> falls back to the main monitor with a note.
4. Custom Area: red outline shows, video contains only the area, outline is not in the video.
5. Ctrl+Alt+F10 starts/stops while another program has focus; Ctrl+Alt+F9 takes a screenshot.
6. Ctrl+Alt+F8 bookmark: file `<video>.mp4.bookmarks.json` appears; pop-up and status line show it.
7. Replay GIFs F5/F6/F7 while recording; GIF opens and loops.
8. Screenshot in every mode: PNG has the right area and does not show RevReel.
9. GIF Maker: full length by default, trim, crop, text (all animations), loop vs play once.
10. Menu button: File, Option, Help readable; Option items disabled while recording.
11. Closing during recording asks to save first.
12. Keyboard only: reach every button with Tab and Alt shortcuts.

---

## 11. Known gaps and roadmap

### Not verified yet (user has not reported results)
- Replay GIF hotkeys, area recording, all-monitors layout, bookmark key, play-once GIF behaviour, the new window recording and screenshot, the new main window.

### Planned
- Pause / Resume while recording.
- App icon (`.ico` from the RR logo).
- Crop for trimmed MP4.
- Text overlays in trimmed MP4 (currently GIF only).
- Arrow-key nudging of text in the GIF maker.
- Split `GifEditorWindow.xaml.cs` (about 1,100 lines) into preview / crop / text parts, **only after a clean build**.
- Installer in `Installer/`, notes in `docs/`.
- Dark and Light themes (README lists it; only dark exists).
- Possible: audio recording (must stay optional; no feature may rely on sound).

### Open questions for the owner
- Copyright (decided): both names are used - "RevGamer (Rev Creative Developer)" in `LICENSE` and `AppInfo.Copyright`.

---

## 12. How to add things

### A new setting
1. Add the property (with default) to `Models/AppSettings.cs`.
2. Clamp/validate it in `SettingsService.Sanitize`.
3. Add a menu item (with `x:Name` if it needs a tick) in the `MainMenu` Option menu in `MainWindow.xaml`; handle it in `MainWindow.xaml.cs`; call `PersistSettings(message)`; tick it in `UpdateMenuChecks()`.
4. Disable it in `ShowState` if it must not change while recording.
5. Document it in `README.md`.

### A new recording source kind
1. Add to `RecordingSourceKind`.
2. Resolve it (with a fallback and a note) in `RecordingTarget.Resolve`.
3. Add the ScreenRecorderLib source in `RecordingService.BuildSources`.
4. Add its bounds in `ScreenshotService.BoundsOf`.
5. Add its label/detail in `MainWindow.UpdateModeButtons` and a button or menu entry.

### A new global hotkey
Add one line in `MainWindow` (`SourceInitialized`): `_hotkeys.Add(mods, key, "label", action, always: true/false)`. Use `always: true` only for keys that must work while idle. Update the F1 list (`ShowShortcuts`), `EnableReplayKeys` text and the README tables.

### A new text animation
Add to `TextAnimation` (keep list order), handle it in `TextOverlayRenderer.DrawOne`, add a display name in `TextEditWindow`, and describe it in the README.

---

## 13. Documentation duties

When behaviour changes, update **in the same change**: `README.md` (features, keys, tables), the F1 shortcut text (`ShowShortcuts`), `AppInfo.ReleaseDateText` on release, and this file.

## 14. Release checklist (planned)

1. Set `<Version>` in `RevReel.csproj` and `AppInfo.ReleaseDateText`.
2. `dotnet build -c Release -p:Platform=x64`.
3. Run the testing checklist (section 10).
4. Tag `vX.Y.Z` on GitHub and create a release (the update check reads `releases/latest`).
5. Attach the build/installer to the release.
