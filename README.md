# SteadyClick

A mouse clicker for Windows and macOS that clicks one spot over and over with the real cursor, at a
pace that looks like a person rather than a metronome.

**Download:** the latest `SteadyClick.exe` (Windows) and `SteadyClick-mac.zip` (macOS 13 or newer,
Apple silicon and Intel) are on the [releases page](https://github.com/Zlerp/steadyclick/releases/latest).

## What it does

- **Where:** a fixed spot (type X/Y, pick it on screen, or point at it and press the pick hotkey
  from any app), or wherever the cursor already is.
- **Random offset:** each click lands within a radius of the spot. 0 means the same point every time.
- **Timing:** a minimum and maximum wait, a minimum and maximum hold time, and an optional start delay.
- **Human-like rhythm:** waits bunch around a typical pace with a tail of slower ones, the pace
  drifts faster and slower over several clicks, and about 1 wait in 30 is a longer pause. Hold times
  and the offset vary the same way. "Even spread" gives plain uniform randomness instead. A small
  chart shows the waits the current settings will produce.
- **Click:** left, right or middle, single or double, with an option to put the cursor back after
  each click so you can keep working.
- **Stopping:** a global start/stop hotkey, a click limit, a time limit, and an emergency stop when
  the cursor is shoved into a screen's top-left corner.
- **Session details:** every click is recorded (time, wait before it, hold, position, offset) with
  running totals. Each run is saved as a CSV file and can be exported.

A fixed-spot click carries its own position, so it lands on the exact point even if your hand is
moving the mouse at that moment.

## Windows

Run `SteadyClick.exe`. Nothing to install. Default hotkeys: **F6** start/stop, **F7** pick spot.

Windows blocks injected clicks into apps that run as administrator unless SteadyClick is also run
as administrator. Run logs are in `%APPDATA%\SteadyClick\logs`.

## macOS

1. Unzip and move `SteadyClick.app` to Applications.
2. The app is not notarized by Apple, so the first launch is blocked. Open **System Settings >
   Privacy & Security**, scroll down and press **Open Anyway**. (Or run
   `xattr -dr com.apple.quarantine /Applications/SteadyClick.app` once.)
3. Press Start once. macOS asks for **Accessibility** permission, which it requires before any app
   may click for you. Allow it under **Privacy & Security > Accessibility**, then press Start again.

Default hotkeys: **⌃⌥S** start/stop, **⌃⌥P** pick spot. After updating to a new build, remove the
old SteadyClick entry from the Accessibility list and allow the new one. Run logs are in
`~/Library/Application Support/SteadyClick/logs`.

## Building

| Platform | Command | Needs |
| --- | --- | --- |
| Windows | `powershell -ExecutionPolicy Bypass -File build.ps1` | Nothing beyond Windows (uses the built-in C# compiler) |
| macOS | `bash mac/build.sh` | Apple's Command Line Tools (`xcode-select --install`) |

The Windows app is C# and WinForms in `src/`. The Mac app is Swift and SwiftUI in `mac/Sources/`.
The timing model (`Rhythm`) is the same in both; change them together.

## Testing

| Command | What it checks |
| --- | --- |
| `SteadyClick.exe --selftest report.txt` | Timing statistics over 50,000 simulated waits, then real clicks into a test window: counts, gaps, offset radius, right and double clicks, instant stop, time limit, emergency corner, the run record and its CSV. Takes over the mouse for about 8 seconds. |
| `SteadyClick.exe --uitest report.txt` | Opens the real window and presses the real hotkeys: pick from anywhere, pick inside the overlay by hotkey and by click, start and stop. |
| `SteadyClick --selftest` (Mac binary) | The same timing statistics, the click loop, the run record and its CSV. Runs on every push in GitHub Actions. |
