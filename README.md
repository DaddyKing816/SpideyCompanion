# Spidey Companion

A tiny Windows helper app that adds media controls and hardware temps plus a desktop buddy to Wallpaper Engine web wallpapers.

Web wallpapers can't control media playback by themselves, so Spidey Companion runs on `127.0.0.1:8471` with a tray icon and provides working **play/pause, next/previous and seek** over a local HTTP endpoint. It also passes **CPU/GPU temperatures** through from LibreHardwareMonitor.

## Download

**[Download SpideyCompanion-Setup-v1.3.1.exe](https://github.com/DaddyKing816/SpideyCompanion/releases/download/v1.3.1/SpideyCompanion-Setup-v1.3.1.exe)**

Or browse every version on the [Releases page](https://github.com/DaddyKing816/SpideyCompanion/releases).

One file, nothing to unzip. Run it: it installs per-user (no admin) and starts Spidey right away. Run the same installer again later and it checks GitHub for the latest version, then offers **Update** or **Uninstall**. You can also uninstall any time from Windows Settings, like any app.

Windows SmartScreen may warn because the app is unsigned: choose **More info → Run anyway**.

## Install

1. Download the installer from the link above
2. Run `SpideyCompanion-Setup-v1.3.1.exe` — no admin needed, it installs just for your user account
3. That's it: Spidey appears at the bottom of your screen and a Spidey icon lands in your tray (right-click it for his controls), and the media service starts on `127.0.0.1:8471`

To update later, just run the newest installer again — it detects your installed version and offers **Update** or **Uninstall**. To remove it without the installer: Windows Settings → Apps → Spidey Companion → Uninstall.

## What's in v1.3.1

A chibi Spider-Man buddy (46 Shimeji poses, art by haydentheartist) who lives on your desktop: he walks along the taskbar, sits, lies down, does web-swing jumps, climbs your screen edges, and can be dragged and dropped - just grab him.

He's **off by default** — fresh installs run just the media service until you switch him on:

- **Show buddy** on/off (tray icon, right-click)
- **Wallpaper only** - he stays on the desktop layer, behind your windows
- **Buddy size** - 96 / 128 / 160 px
- **Pace** and **Swing height** (Small hop / Classic / Big swing)
- **Jump now**, **Climb a wall**, **Show an emotion**, **Walk now**

## Requirements

- Windows 10/11 with .NET Framework 4.8 (built into Windows)
- For temperatures: LibreHardwareMonitor running with its Remote Web Server enabled on port 8085
- Without this app, a wallpaper's media display still works — it just can't be controlled

## How it works

Spidey Companion talks to the Windows media session (the same system Spotify, browsers and media players report to), so it controls whatever is actually playing. It only listens on your own machine (127.0.0.1) — nothing leaves your PC.

It can also update itself: the installed `update-feed.txt` points at this repo's `feed.txt`, so the app checks on startup and every 12 hours, verifies the download's SHA-256 hash, and swaps itself in place when a newer version is published here.

## Source

Full source lives in this repo: `src/` (the app), `installer/` (Inno Setup script), `installer-exe/` (the single-file installer) — the installer also drops `Program.cs` and `Buddy.cs` next to the installed app.

---

*Unofficial fan project. Not affiliated with Marvel, Insomniac Games or Sony.*
