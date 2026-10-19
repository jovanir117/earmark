# Earmark

Send every Chrome tab, and every Windows app, to its own audio output. Tab 1 on the headphones, tab 2 on the speakers, the game on the headset and Spotify on the TV, all at the same time.

Two parts that work on their own:

- **Chrome/Edge extension** (Manifest V3): routes each tab.
- **Windows app** (`windows/`): routes each app, from the tray.

> The interface is in Spanish.

## Chrome extension

### Install

1. Open `chrome://extensions` (or `edge://extensions`) and turn on **Developer mode**.
2. **Load unpacked** and pick this folder.
3. Pin the icon. The first time, press **Dar permiso** so device names show up.

### Use

Open the tab that plays sound, click the icon, **click a device**. **Predeterminado del sistema** puts the tab back to normal.

- **▶** next to each device plays a test tone there, so you know which is which.
- The icon shows **♪** on routed tabs and **!** when the tab's device was unplugged.
- Remembers the last device you used on each site.
- **Several at once**: turn on the switch and tick several devices; the tab plays on all of them.
- **Volume up to 200%**, left/right **balance** and **mono**, with a limiter so loud tabs don't distort.
- **`Alt+Shift+A`** moves the current tab to the next output without opening the popup (change it in `chrome://extensions/shortcuts`).
- Choose what happens when a device disconnects: fall back to the default output, or mute (so headphone audio never leaks to the speakers).

### How it works

- `chrome.tabCapture` captures the tab's audio (Chrome mutes it on its original output).
- An offscreen document plays it with `HTMLMediaElement.setSinkId(device)`.
- On `devicechange` every route is resolved again: a missing device falls back to the default and the route comes back by itself when the device returns. Matching is by device ID, then by name if the ID changed (`resolve.js`).
- Closing the tab ends the track and the route cleans itself up.

```
npm test
```

### Limits

- Adds some latency (tens of ms); with video you may notice a tiny lip-sync offset.
- Does not work on `chrome://` pages or the Chrome Web Store (Chrome forbids capturing them).
- Routes are lost when the browser restarts.

## Windows app

`windows/AudioRouterApps.exe` lists every app that is playing audio, each with a menu to pick its output. Example: game → headphones, Chrome → speakers.

- **Lives in the tray.** Right-click the 🔊 icon: every app that plays, where it goes, and a submenu to change it on the spot. Left-click opens the window.
- Closing the window keeps it running in the tray; **Salir** in the menu quits.
- **Start with Windows** from the menu (starts hidden, icon only).
- Follows the Windows light/dark app theme.
- Windows remembers the choice per app, even after the app closes.
- Idle: about 1 MB of memory and ~0% CPU.

### Build

```
windows\build.cmd
```

Uses the C# compiler that ships with Windows (.NET Framework 4.x); nothing to install. It also runs a self-test.

### Command line

```
AudioRouterApps.exe --dump               # list outputs and the current default, change nothing
AudioRouterApps.exe --default "Speakers" # make the first output whose name contains the text the Windows default
AudioRouterApps.exe --tray               # start with the tray icon only
```

`--default` exits with 0 when done, 2 when no output matches and 1 when Windows refuses. Handy for scripts and "scenes" (gaming, TV, headphones).

### Notes

- Per-app routing uses the same internal (undocumented) Windows API as the Volume Mixer and EarTrumpet. If a Windows update breaks it, the window says so and links to the Windows mixer.
- Games with their own audio device picker ignore this: change it inside the game.
- Errors are logged to `%TEMP%\AudioRouterApps.log`.

## License

MIT
