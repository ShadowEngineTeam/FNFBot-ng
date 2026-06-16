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
- **Psych Engine** / **Shadow Engine** charts (`psych_legacy` and `psych_v1`)
- **Codename Engine** charts

## Building

The project targets **.NET 8** (`net8.0-windows`) and builds with the latest
Visual Studio or the .NET SDK:

```
dotnet build FNFBot20.sln -c Release
```

No external assemblies are required, the chart parser is built in.

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
