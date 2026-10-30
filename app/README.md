# screenless for Windows

A small tray app for people who don't want a terminal. Plug in the phone, press
one button, and the phone opens in a window. Works over USB or Wi-Fi.

![screenless app](screenshot.png)

## What it does

- **Detects your phone** over USB or Wi-Fi and shows the model, transport and state.
- **Turns on Wi-Fi mode** in one click: saves the phone's IP and re-arms wireless.
- **Connect by IP** if the phone is already on Wi-Fi and you know its address.
- **Starts and stops the mirror** with a sane quality preset for each transport.
- **Lives in the tray**: close the window and it keeps running. Right-click for
  Start, Stop, Wi-Fi mode, Connect, Startup and Exit.
- **Starts with Windows** optionally.
- **Installs scrcpy automatically** on first run if it's missing (official build,
  no admin rights, no Microsoft Store).

## Use it

1. Double-click `screenless.exe`.
2. Plug in the phone with a data cable.
3. Press **Turn on Wi-Fi mode (plug in USB first)**. It prints the phone's IP.
4. Unplug the cable.
5. Press **Start mirror**.

Next time there is no cable at all: open the app and press **Start mirror**. If the
phone restarted, wireless mode is off — plug in once, press the Wi-Fi button, unplug.

If you prefer Wi-Fi only: type the phone's IP (Settings → Wi-Fi → your network →
IP address) and press **Connect**, then **Start mirror**.

## Options

| Option | Meaning |
|---|---|
| Quality → Auto | USB: 8 Mbps. Wi-Fi: 3 Mbps at 1024 px. |
| Quality → Best | 8 Mbps; up to 1280 px on Wi-Fi. |
| Quality → Smooth | 2 Mbps at 800 px for weak Wi-Fi. |
| Phone audio | Forwards the phone's sound (Android 11+). |
| Start with Windows | Launches into the tray at login. |

## Build it yourself

No .NET SDK or NuGet needed — it compiles with the C# compiler that ships with Windows:

```powershell
cd app
powershell -ExecutionPolicy Bypass -File .\build.ps1
# -> app\dist\screenless.exe
```

The source is one file (`ScreenlessApp.cs`), plus `screenless.ico` and `app.manifest`.

## Notes

- Windows 10/11, .NET Framework 4.8 (already installed on both).
- The `.exe` is unsigned, so SmartScreen may ask once: **More info → Run anyway**.
- On Wi-Fi the phone can sleep and drop the link when no mirror is running. Press
  **Connect** or **Start mirror** to wake it.
- Everything the app does is the same adb/scrcpy flow as `windows\phone.ps1`.
