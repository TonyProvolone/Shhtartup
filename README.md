# Tinnitdown

Worried about annihilating your ear drums when a new game/app starts up? Well I've got some mediocre news for you!

Tinnitdown is a small app that runs in the background on Windows 10/11 and drops the Windows volume of those apps/games down to whatever volume level you set within the app. This allows you to not have to drastically adjust in-game volume as much to accommodate.

- Recognises games from Steam, Epic, GOG, Ubisoft Connect, EA and Battle.net. Will add more as needed.
- Only sets the initial startup volume. Has no effect otherwise.
- Remembers every adjusted game/app, so it catches them next time however they're launched.

DISCLAIMER: This was totally written using AI because I'm a lowly front end web developer, not an app developer, and this is my vain attempt to protect what little hearing I probably have left at this point.

## How to install

[![Download Latest Version](https://img.shields.io/badge/Download-Latest%20Version-0078D4?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/TonyProvolone/Tinnitdown/releases/latest/download/Tinnitdown.exe)

1. Download the latest version above and run it.
2. Choose where to install, whether you want a desktop shortcut and
   Windows startup, then click **Install**.
3. Tinnitdown starts and runs in the system tray.

The app isn't code-signed, so Windows SmartScreen may warn you the first time you run the installer. If so, click **More info** → **Run anyway**.

## How to use

Tinnitdown runs minimized in your system tray. Right-click the tray icon to:
- **Set the default volume** (default 25%)
- Open **Settings**
  - Toggle *Run on startup*
  - Clear any saved games/apps
- **Check for updates** or **Exit**

## How to update

Tinnitdown checks for a new version every time it starts.
- **Install and restart**: updates and reopens Tinnitdown right away.
- **Install and restart later**: updates now, and the new version runs the next time Tinnitdown starts.
- **Remind me later**: asks again the next time Tinnitdown starts.

You can also right-click the tray icon and choose **Check for updates**, or download the latest
version above and run it. It installs over your existing copy, in the same folder.

## How to uninstall

Open **Settings** → **Apps** → **Installed apps**, find Tinnitdown and choose **Uninstall**. This
removes the app along with all of its settings and remembered games/apps.
