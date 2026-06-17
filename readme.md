# FNFBot Rewrite
## The better version of FNFBot

### WARNING!

FNFBot doesn't know where the song starts on its own — you press **F1** at the right
moment to start/stop it in sync. So don't open issues saying it "hits notes early" or
whatever.

## extra keys won't be added, stop asking for it ##

### What is FNFBot?

FNFBot is a bot program that lets users automatically play Friday Night Funkin' charts.

## Engine support

FNFBot reads chart `.json` files directly. It supports:

- The **base game** chart format (both `legacy` and V-Slice)
- **Psych Engine** / **Shadow Engine** charts (`legacy` and `psych_v1`)
- **Codename Engine** charts

## Requirements

Cross-platform on **.NET 8** (needs the .NET 8 runtime). The recommended app is the
Avalonia UI (`FNFBot.App`), which runs on Windows, Linux and macOS.

Key injection / global hotkeys are per-OS:

- **Windows** — works out of the box (`SendInput` + `GetAsyncKeyState`).
- **Linux** — injects keys via `/dev/uinput` (works on X11 **and** Wayland) and reads
  `/dev/input/event*` for the F1-F7 hotkeys. Both need device access:

  ```bash
  # 1) read access to keyboards (for the F-key hotkeys)
  sudo usermod -aG input "$(whoami)"

  # 2) write access to /dev/uinput (for sending presses) — the input group alone
  #    does NOT cover uinput, so add a udev rule:
  echo 'KERNEL=="uinput", GROUP="input", MODE="0660", OPTIONS+="static_node=uinput"' \
    | sudo tee /etc/udev/rules.d/99-uinput.rules
  sudo modprobe uinput
  sudo udevadm control --reload-rules && sudo udevadm trigger
  ```

  Then **log out and back in** (so the `input` group applies) and relaunch. Don't run the
  app with `sudo` — GUI apps misbehave under it, and the group/udev setup above is the
  proper fix.
- **macOS** — uses `CGEvent`. Grant the app **Accessibility** (and Input Monitoring)
  permission under System Settings → Privacy & Security, or injected keys are ignored.

If input can't initialise (e.g. missing permissions) the app still runs and shows the
reason in its log; only key sending is disabled.

## Building

Builds with the latest Visual Studio or the .NET SDK:

```
dotnet build FNFBot.sln -c Release
```

Run the cross-platform app from `FNFBot.App` (builds as `FNFBot`); the parsers and engine
live in the shared `FNFBot.Core` library. No external chart libraries are required.

Any RID-targeted publish produces a **single self-contained binary** (no .NET install needed):

```
dotnet publish FNFBot.App/FNFBot.App.csproj -c Release -r linux-x64   # -> one file: FNFBot
```

CI builds the app on Windows, Linux and macOS; tagging `v*` publishes a single self-contained
binary per OS/arch (win/linux/osx × x64/arm64) and attaches them to a GitHub Release.

### How do I use FNFBot?

FNFBot has 3 main sections, as shown here:


![3Sections](https://i.imgur.com/fwlUZPg.png)

The **red** section is where you enter all the data like the game's directory on your computer.

The **green** area is the console, this outputs useful information.

Examples:

- What happened when you pressed a keybind
- What notes the bot's planning on hitting
- When the bot completes a song

The **blue** section is where the bot renders the notes that are *probably* there, including the length of held notes.

### Keybinds

| Keybind | Description |
| ------- | ----------- |
| F1 | Start/Stop playing the selected map |
| F2 / F3 | Increase / decrease the **offset** |
| F4 / F5 | Increase / decrease the **press duration** |
| F6 / F7 | Increase / decrease the **sustain overhold** |

- **Offset**: ms to hit before/after the note time (default 25).
- **Press duration**: how long each tapped note is held down, in ms (default 40). Raise it to play more like a human.
- **Sustain overhold**: extra ms a hold note is kept down past its true end so the tail's final piece always registers (default 20). Increase this if the bot drops the end of long notes.

All three are saved to `bot.settings` next to the exe (one `key=value` per line), so you can also edit them by hand.
