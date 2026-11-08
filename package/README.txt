SPIDEY COMPANION v1.3.0 - media helper for the Spider-Man live wallpaper,
now with a desktop buddy
========================================================================

The program file is named with its version - SpideyCompanion-v1.3.0.exe -
so a new release never gets confused with an older copy on the same PC.

WHAT IS THIS?
Wallpaper Engine wallpapers are allowed to READ what is playing on your
PC (through the Windows media session) but are NOT allowed to control
it - Wallpaper Engine gives them no play/pause/next/seek functions at
all. Spidey Companion is the bridge: the wallpaper's media deck sends a
tiny request to this program at 127.0.0.1:8471 (your own PC only), and
this program presses the buttons on the real Windows media session -
the same one the Windows volume overlay controls. That means it works
with Spotify, YouTube in a browser, Windows Media Player, and any
other app that shows up in the Windows media overlay.

NEW IN v1.3.0 - THE BUDDY
A chibi Spider-Man now lives on your desktop: the 46 poses of a
"Shimeji" desktop-pet pack.
He walks along the bottom of your screen (on top of the taskbar),
pauses (sometimes sitting down, dangling his legs, or lying flat),
does web-swing jumps, and when he reaches a screen edge he sometimes
CLIMBS the wall, hangs at the top for a moment, then drops back down.
He hides himself automatically while a fullscreen app (a game, a
video) is in front - he never gets in the way.

  * Drag him anywhere with the mouse: he looks surprised, then scared
    as he falls, bounces, and lands.
  * Click (poke) him: he reacts with an emotion.
  * Clicks pass through his transparent pixels - only his body is
    clickable, the rest of your desktop works normally around him.
  * Right-click his tray icon to: show/hide him, set his size
    (Compact 96 px / Native 128 px / Large 160 px), pace (Chill / Normal /
    Zippy), and which display he lives on; make him jump, emote or
    walk on demand; or quit the whole companion.
  * His preferences are saved in %APPDATA%\SpideyCompanion\settings.txt
    - that small file is the only thing the companion stores.

WHAT IT DOES - COMPLETELY:
  * The buddy above, plus a tray icon to control him.
  * Listens on 127.0.0.1 port 8471. Nothing outside your PC can reach it.
  * GET /play        -> play or pause whatever is playing
  * GET /next        -> next track / video
  * GET /prev        -> previous track / video
  * GET /seek?pos=N  -> jump to N seconds in the current track / video
  * GET /status      -> a plain-text report that starts with the exact
                        version running, plus the media session it can
                        see, which controls are available, and the
                        update state
  * GET /update      -> checks for an update right now
  * GET /temps       -> passes through LibreHardwareMonitor's data
                        for the wallpaper's temperature readouts
  * GET /health      -> answers "ok"
  * Anything else is ignored.

AUTOMATIC UPDATES
  The companion can update ITSELF - no uninstall/reinstall dance.
  Next to the exe is update-feed.txt. If it contains a line
      feed=<url of a small version file>
  the companion checks that file on startup and every 12 hours. The
  version file is plain text:
      version=1.4.0
      url=<direct link to the new exe>
      sha256=<sha-256 of the exe>     (optional, always verified
                                       when present - a mismatch is
                                       rejected and nothing installs)
  A newer version downloads under its own versioned file name,
  verifies, replaces the old copy, and restarts itself. If feed= is
  blank (the default), it never checks and never downloads anything.

SETUP NOTES THAT MATTER
  * Spotify: turn ON Spotify Settings -> Display -> "Show desktop
    overlay when using media keys" - without it, Spotify may not
    publish a media session for anything to control.
  * Desktop VLC does not publish a Windows media session by itself
    (the Store version of VLC does).

INSTALL
  Preferred: run the Spidey Companion installer (Setup.exe) from the
  GitHub release - it installs per-user (no admin), adds a Start Menu
  entry, starts with Windows, and can be removed from Windows
  Settings like any app.
  Manual fallback: run Install-SpideyCompanion.bat from this package.
  It stops any older copies (under any SpideyCompanion file name,
  plus the old "SpiderCompanion" name from early builds), removes
  them, copies this version in, sets it to start with Windows, and
  starts it right away. Your existing update-feed.txt is never
  overwritten. Windows may show a blue SmartScreen warning because
  the program is not signed with a paid certificate - click "More
  info" then "Run anyway". The full source code is included so you
  (or anyone) can check exactly what it does, and even rebuild it.

CHECK IT IS RUNNING
  Look for the buddy on your desktop and the icon in the system tray.
  Open http://127.0.0.1:8471/health in your browser - it should show
  "ok". Open http://127.0.0.1:8471/status while something is playing
  to see the version, session and controls it has found.

REMOVE IT
  If you used the installer: Windows Settings -> Apps -> Spidey
  Companion -> Uninstall. If you used the .bat: run
  Uninstall-SpideyCompanion.bat. His settings file can be deleted at
  %APPDATA%\SpideyCompanion.

SOURCE CODE / TRUST
  Program.cs (helper + updater) and Buddy.cs (the buddy engine)
  in this folder are the COMPLETE source code; the buddy's 46 pose
  images are embedded in the exe as resources. It is a .NET
  Framework 4.8 program (built into every Windows 10/11 PC, so there
  is nothing else to install). Rebuild it yourself with the free
  .NET SDK:  dotnet build -c Release
  SHA-256 of the included SpideyCompanion-v1.3.0.exe:
  5dcea2688853dce29512e86ea0d19634aceefb934561eaf9a7ebb5a351bcfdad

CREDITS
  The buddy's poses are the 46 frames of a "Shimeji" Spider-Man
  desktop-pet pack (shime1.png - shime46.png).
  Sprite art by haydentheartist (Reddit) - thank you!
  The pose-to-action mapping follows the pack's own configuration
  files. Shimeji pets are a long-running fan tradition (the
  Shimeji-ee program itself is by Yuki Yamada / Group Finity).
  The poses are used here as found, unmodified.

  An earlier unreleased build used pixel data from "Buddy" by Rahaman Bin Ujit
  (MIT License, Copyright (c) 2026 Rahaman Bin Ujit,
  https://github.com/rahamanbinujit/buddy-app); that art was
  dropped before release at the project owner's request, and the thanks
  stands - his behaviour model still informs the buddy's timing.

  Spider-Man is a trademark/character of Marvel; this project is a
  free, non-commercial fan project and is not affiliated with or
  endorsed by Marvel.
