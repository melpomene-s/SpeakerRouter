# SpeakerRouter

[简体中文](README.zh-CN.md)

SpeakerRouter is a lightweight Windows 11 tray app that routes the system default audio output based on which screen the currently audible window is on.

## Features

- Detects audible Windows Core Audio sessions and maps browser child processes back to their visible parent window.
- Binds each monitor to a playback device and switches the Windows default output device automatically.
- Uses WinForms, Win32, and Windows Core Audio APIs with no third-party packages.
- Runs quietly from the system tray, with optional startup at login.
- Saves settings to `%AppData%\SpeakerRouter\settings.json`.

## Usage

1. Run `SpeakerRouter.exe`.
2. Left-click the tray icon, or right-click it and open `Main Window`.
3. Bind each screen to a playback device and save.
4. Enable automatic detection and switching.
