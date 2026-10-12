# RevReel

RevReel — a simple Windows screen recorder with bookmarks and instant replay GIFs. By Rev Creative Developer.

> Status: early development (v0.1.0). Compact toolbar: **Whole Screen**, **A Window**,
> **Custom Area**, **Record**, **Screenshot** and **GIF Maker**.
> The menu button (three lines, top right) opens File, Option and Help.

## Planned features

- Record the whole screen, one window, or a custom area (MP4).
- Bookmarks: mark a moment while recording, export a clip or GIF later.
- Instant replay GIFs of the last 3, 5 or 10 seconds, on global hotkeys.
- Written status text and visual indicators only. No feature depends on sound.
- Dark and Light themes.

## Requirements

- Windows 10 (1809 or later) or Windows 11, 64-bit.
- Build: .NET 8 SDK, and the Windows 10 SDK 10.0.19041.0
  (installed with Visual Studio 2022's ".NET desktop development" workload).

## Build and run (Visual Studio terminal)

```
dotnet restore RevReel.sln
dotnet build RevReel.sln -c Debug -p:Platform=x64
dotnet run --project src\RevReel\RevReel.csproj -c Debug -p:Platform=x64
```

Recordings are saved to `Videos\RevReel` by default (change it in Option). Settings are stored in `%AppData%\RevReel\settings.json`.

The update check reads the latest GitHub Release. Tag releases like `v0.2.0`.

## Project layout

```
RevReel.sln
src/RevReel/
  Models/      data types (recording state)
  Services/    recording engine wrapper
  Views/       windows and dialogs (later)
  Resources/   colours and shared styles
  Assets/      icons and logo
docs/          notes and design decisions
Installer/     installer script (output is not committed)
```

## Third-party software

- [ScreenRecorderLib](https://github.com/sskodje/ScreenRecorderLib) (MIT).

## License

MIT. See `LICENSE`.


## Replay GIFs

While recording, RevReel keeps the last few seconds as small pictures in a temporary folder
and deletes older ones, so disk use stays small. Press a key to turn the last 3, 5 or 10
seconds into a looping GIF saved in your save folder (`RevReel_replay_5s_<date>.gif`).

| Keys (work while a game has the focus) | Result |
| --- | --- |
| Ctrl+Alt+F5 | GIF of the last 3 seconds |
| Ctrl+Alt+F6 | GIF of the last 5 seconds |
| Ctrl+Alt+F7 | GIF of the last 10 seconds |

The same actions are in **File > Save replay GIF**. Switch the feature off in
**Option > Enable replay GIFs**. A small pop-up in the top-right corner confirms each GIF in
words (no sound), and the pop-up is hidden from the recording.

Third-party: Openize.Animated-GIF (Apache-2.0), System.Drawing.Common (MIT).

## The toolbar (what to record)

The three buttons on the left choose what the next recording and screenshot capture.
The chosen one has an orange outline, and the line under the buttons says it in words
("Recording: Whole screen (...)").

- **Whole Screen** - records a whole monitor. With more than one monitor a small menu opens so you pick which one (or **All monitors**, one wide video). If the chosen monitor is missing, the main one is used and the line says so.
- **A Window** - a list of open windows opens (arrow keys + Enter, or double-click). Only that window is recorded. If it is closed later, the main monitor is recorded instead and the line says so. Minimised windows are not listed.
- **Custom Area** - every monitor is dimmed. Drag a box on any monitor (or use the arrow keys: arrows move, Shift + arrows resize), then press Enter or "Use this area". While recording, a red outline marks the area; it is hidden from the video.

**Record** starts recording and becomes **Stop** while recording. **Screenshot** saves a PNG
(`RevReel_screenshot_<date>.png`) of exactly what would be recorded. **GIF Maker** opens a finished recording.
The same choices are in **Option > Record source**.

| Keys (work in games and when RevReel is not in front) | Result |
| --- | --- |
| Ctrl+Alt+F10 | Start or stop recording |
| Ctrl+Alt+F9 | Screenshot |
| Ctrl+Alt+F8 | Bookmark (while recording) |

Alt shortcuts inside RevReel: Alt+W whole screen, Alt+N window, Alt+A area, Alt+R record/stop,
Alt+C screenshot, Alt+G GIF Maker, Alt+L show last file.

Pop-up messages (such as "GIF saved") appear on the monitor being recorded.
The source cannot be changed while recording. The chosen area and window are remembered until RevReel closes.
RevReel's own window is hidden from recordings and screenshots.

## Bookmarks and the GIF maker

For long sessions (for example a 3 hour game):

1. Record as normal. When something funny or perfect happens, press **Ctrl+Alt+F8** (works while a game has the focus) or use **File > Add bookmark**. A pop-up says "Bookmark 3 added at 02:03:12". Bookmarks are saved next to the video in `<video>.mp4.bookmarks.json`, as soon as you add them.
2. Stop the recording. The GIF maker works on a finished recording.
3. Click **Make GIF...** (or **File > Make GIF from a recording...**) and pick the video. Bookmarks are listed on the left.
4. Choose a bookmark (it starts 5 seconds before and ends 3 seconds after) or type your own start and end times. Drag the slider to see the picture at any moment, and use "Set start here" / "Set end here" to fine-tune.
5. **Drag a box on the picture** to crop to just the part you want (for example only the dancing team). "Reset crop" removes it.
6. Choose smoothness (frames per second) and width, then click **Make GIF**. The GIF is saved next to the video. A GIF can be up to 60 seconds long.

The picture is read directly from the finished video, so no extra disk space is used while recording.

### More in the GIF maker

- **Play, Pause and Stop** play the recording in the preview. "Stop at the End time" pauses at your End time; Stop goes back to your Start time.
- The **timeline** covers the whole recording. Arrow keys move 1 second, Page Up / Page Down 10 seconds, and "Back 1 s" / "Forward 1 s" step precisely.
- **Add bookmark at the position** marks the moment shown in the preview (saved with the others). **Remove selected bookmark** deletes one.
- **Crop:** drag a box on the picture, then click **Crop**. The preview zooms into that part, and the GIF uses only that part. Click **Full screen** to go back to the whole picture, then make another GIF or trim from the same moment.
- **Text:** click **Add text...** to put words on the GIF. Choose the font, size (12 to 64), colour, bold, italic, underline and a dark outline. Each text can show for the whole GIF, or only from one time to another (for example 00:00:05 to 00:00:08). In the preview, a text that is not shown at the moment you are looking at is faded. **Colour picker...** opens a colour field and hue bar (with hue, saturation, brightness, red, green, blue and colour code boxes). **Animation** can make a text flash, fade in and out, shake, show broken (glitching) letters, type itself out letter by letter, or cycle through rainbow colours, at slow, normal or fast speed. Drag the text in the picture to move it, double-click it to change it, or use **Change text...** and **Remove text**. You can add several texts. Text is drawn into every frame of the GIF. It is not added to the trimmed MP4.
- **Loop or play once:** next to **Make GIF**, choose **Loop** (the GIF plays again and again, the default) or **Play once** (it stops on the last frame).
- **Save trimmed video (MP4)** saves only the part between Start and End as a new MP4 (whole picture, original size). The original recording is never changed.
