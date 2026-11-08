# Spidey Companion

A tiny Windows helper app that adds media controls and hardware temps plus a desktop buddy to Wallpaper Engine web wallpapers.

Web wallpapers can't control media playback by themselves, so Spidey Companion runs on `127.0.0.1:8471` with a tray icon (since the buddy release) on `127.0.0.1:8471` and provides working **play/pause, next/previous and seek** over a local HTTP endpoint. It also passes **CPU/GPU temperatures** through from LibreHardwareMonitor.

## Download

From v1.3.0 the buddy build ships as ONE installer file:
**SpideyCompanion-Setup-v1.3.0.exe** - grab it from this repo's
Actions tab (the "Build installer" workflow attaches it to every
run) or from Releases when one is published. Run it: it installs
per-user, no admin, and can be uninstalled from Windows Settings
like any app.

New in this build: a chibi Spider-Man buddy (46 Shimeji poses)
who lives on your desktop - he walks along the taskbar, sits,
lies down, web-swing jumps, climbs your screen edges, and can be
dragged and dropped. Right-click his tray icon to control him.

## Install

1. Unzip `SpideyCompanion-v1.2.1.zip`
2. Run `Install-SpideyCompanion.bat`
3. Start Spidey Companion — it sits in the background, no window

Windows SmartScreen may warn because the app is unsigned: choose **More info → Run anyway**.

## Requirements

- Windows 10/11 with .NET Framework 4.8 (built into Windows)
- For temperatures: LibreHardwareMonitor running with its Remote Web Server enabled on port 8085
- Without this app, a wallpaper's media display still works — it just can't be controlled

## How it works

Spidey Companion talks to the Windows media session (the same system Spotify, browsers and media players report to), so it controls whatever is actually playing. It only listens on your own machine (127.0.0.1) — nothing leaves your PC.

It can also update itself: if an `update-feed.txt` next to the app points at a version file, it checks on startup and every 12 hours, verifies the download's SHA-256 hash, and swaps itself in place.

## Source

`Program.cs` in the Releases zip is the full source of the shipped build.

---

*Unofficial fan project. Not affiliated with Marvel, Insomniac Games or Sony.*
