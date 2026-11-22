A small fix release. If you already run 1.0.0, MyDeck updates itself; nothing to do.

## Fixes

- Some text in the editor, the sample deck and the tablet app still showed up in Turkish when English was selected. It's all English now.
- Numbers in English mode use English formatting (`0.97` instead of `0,97`).
- A media app that was closing or changing tracks could leave a "Couldn't read media" warning with no details in the log. One-off misses are now skipped; the warning only shows if reading keeps failing, and it includes the error code.
- The sample deck's audio page has a Spotify slider instead of a browser-specific one.

## Install

1. Download `MyDeck-Setup.exe` below and run it. No admin rights needed. Prefer a portable copy? Take `MyDeck-win-x64.zip`, unzip it anywhere and run `MyDeck.exe`.
2. The app isn't code-signed, so Windows may say "Windows protected your PC". Click **More info**, then **Run anyway**.
3. On the tablet, open the address shown in the MyDeck window and install the app.
4. Open MyDeck on the tablet and accept the connection on the PC if the codes match.

Needs 64-bit Windows 10 (version 2004 or newer) or Windows 11 and Android 5.1 or newer. Nothing else to install.

`MyDeck-win-x64.zip.sha256` holds the SHA-256 checksum of the zip.
