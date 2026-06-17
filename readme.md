# FNFBot

A bot that automatically plays Friday Night Funkin' charts by injecting key presses in time with an internal stopwatch.

> **Timing note** - FNFBot doesn't know when a song actually starts in-game. Press **F2** at the right moment to sync playback. Use the offset setting to fine-tune early/late hits.

## Engine support

FNFBot reads chart `.json` files directly. It supports:

- **Base game** (legacy and V-Slice)
- **Psych Engine** / **Shadow Engine** (`legacy` and `psych_v1`)
- **Codename Engine**

## Requirements

Cross-platform on **.NET 8**. The app (`FNFBot.App`) uses Avalonia UI and runs on Windows, Linux, and macOS.

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
3. Start the song in-game, then press **F2** at the downscroll start to sync.

The console logs what the bot is doing. The note field shows upcoming notes and hold lengths.

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
