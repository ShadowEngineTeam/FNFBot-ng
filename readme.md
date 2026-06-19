# FNFBot

A bot that automatically plays Friday Night Funkin' charts by injecting key presses. It can sync to a stopwatch you start by hand (**F2**), or **attach to the running game** and play in lockstep with its Conductor, auto-starting on the song's countdown.

## Engine support

FNFBot reads chart `.json` files directly. It supports:

- **Base game** (legacy and V-Slice)
- **Psych Engine** (psych_legacy and psych_v1)
- **Shadow Engine**
- **Troll Engine**
- **Nightmare Vision**
- **Codename Engine**
- **CDev Engine**
- **Kade Engine**

## Requirements

Cross-platform on **.NET 8**. The app (`FNFBot.App`) uses Avalonia UI and runs on Windows, Linux, and macOS. Released as self-contained builds for Windows (x64, x86, arm64), Linux (x64, arm, arm64), and macOS (x64, arm64).

Key injection and global hotkeys are per-OS:

- **Windows** - works out of the box (`SendInput` + `GetAsyncKeyState`).
- **Linux** - injects keys via `/dev/uinput` (X11 and Wayland) and reads `/dev/input/event*` for hotkeys. Both need device access:

  ```bash
  # read access to keyboards (for hotkeys)
  sudo usermod -aG input "$(whoami)"

  # write access to /dev/uinput (for sending key presses)
  echo 'KERNEL=="uinput", GROUP="input", MODE="0660", OPTIONS+="static_node=uinput"' \
    | sudo tee /etc/udev/rules.d/99-uinput.rules
  sudo modprobe uinput
  sudo udevadm control --reload-rules && sudo udevadm trigger
  ```

  Log out and back in so the `input` group takes effect, then relaunch. Don't run the app with `sudo` - the group/udev setup is the right fix.

- **macOS** - uses `CGEvent`. Grant **Accessibility** (and Input Monitoring) permission under System Settings -> Privacy & Security.

If key injection fails (e.g. missing permissions) the app still runs and shows the reason in the log.

## Building

```
dotnet build FNFBot.sln -c Release
```

Run the app from `FNFBot.App`; the parsers and engine live in the shared `FNFBot.Core` library.

## Usage

1. Enter the game or mod folder path and click **Check Dir** to scan for charts.
2. Double-click a chart in the tree to load it.
3. Play the loaded song, either by pressing **F2** in time with the countdown, or by attaching to the game (below) to sync automatically.

The console logs what the bot is doing. The note field shows upcoming notes and hold lengths.

### Attaching to the game (auto-sync)

Instead of timing by hand, the bot can read the engine's `Conductor.songPosition` from memory and play in sync with it:

1. Load the chart for the song you are about to play.
2. Click **Attach Game** and pick the engine's process.
3. Start (or restart) the song in-game. The bot arms on the countdown, plays in time, follows pauses, resumes and restarts, and stays idle in menus and freeplay.

Reading another process's memory needs elevated rights:

- **Windows** - run the bot as administrator if the game itself runs elevated.
- **Linux** - the no-sudo route is to keep the `input`-group setup above and relax ptrace once per boot (`sudo sysctl -w kernel.yama.ptrace_scope=0`), then run the app normally. Running the whole app with `sudo` also works but bypasses the `input`-group setup.
- **macOS** - launch the bot with `sudo` so `task_for_pid` can read the game.

Attach covers the engines that keep `songPosition` as a module static (Psych, Shadow, Codename, Kade, Nightmare Vision, Troll, and unrecognised forks via a generic fallback). **Funkin V-Slice** and **macOS** attach are experimental. When attach is unavailable the manual **F2** workflow always works. 32-bit builds (Windows x86, Linux arm) can only attach to 32-bit games.

Games run through a compatibility or translation layer (**Wine, Box64/Box86, QEMU, FEX, MuVM**) are **rejected at attach** with a clear message. The emulated process shares its memory with the loader binary, not the game binary, so the module scanner cannot locate the game's static variables. Instead, use a build of FNFBot that matches the game's native architecture (e.g. the x86_64 build for Box64-hosted games, or the Windows build for Wine). The **F2** manual workflow always works as a fallback regardless of process architecture.

### Hotkeys

| Key | Action |
|-----|--------|
| F1  | Rewind (reload the chart from the beginning) |
| F2  | Play / Pause |
| F3  | Fast-forward (skip to end) |
| F4  | Close chart |

### Settings

Click **Settings** to open the timing dialog. All values are saved to `bot.settings` next to the executable and can also be edited by hand (`key=value` per line).

| Setting | Default | Description |
|---------|---------|-------------|
| Offset | 0 | ms added to every hit time (negative = hit earlier) |
| Press min / max | 56 / 110 | Random tap hold duration range (ms) |
| Hold min / max | 44 / 90 | Extra ms to keep a sustain held past its tail end |
| Press rate | 100 | Accuracy 0-100; below 100 adds random timing jitter |
| Auto fail | off | Randomly miss ~10% of tapped notes |
| Fail count | 0 | Reserved, not yet enforced |
