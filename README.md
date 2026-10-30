# Better DeepL Translator

A lightweight Windows desktop app that brings your **DeepL Pro** subscription to the desktop — no browser tab needed.

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![.NET](https://img.shields.io/badge/.NET-11-purple)
![License](https://img.shields.io/badge/license-MIT-green)

## Features

- **Instant translation** — type or paste and results appear automatically
- **Global hotkey** `Ctrl+C+C` — copy any text anywhere and it opens translated
- **Survives network switches**: when Windows switches networks (Wi-Fi, LAN, VPN), the app drops its old connections and retries on the new network instead of going blank
- **Login once**: sign in with your DeepL Pro account in the browser. The session is renewed automatically and stored encrypted for your Windows user
- **Classic & Next-gen engine** toggle per translation
- **Language swap** — flip source and target with one click
- **System tray** — minimizes to tray, always one click away
- **Start with Windows** — optional autorun via registry
- **DE / EN UI** — interface language toggle in the title bar
- **Dark theme** throughout

## Requirements

- Windows 10 or 11 (64-bit)
- [.NET 11 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/11.0) (x64)
- An active **DeepL Pro** subscription

## Installation

1. Download `BetterDeepLTranslator-<version>-setup.exe` from the [latest release](https://github.com/Naxterra/Better-DeepL-Translator/releases/latest).
2. Run it. It installs for your Windows user only, into `%LOCALAPPDATA%\Programs\Better DeepL Translator`, so no administrator rights are needed. It adds a Start menu entry and, if you choose, a desktop shortcut.
3. Click **⚙ Verbindung → Anmelden** to sign in. This opens the DeepL login in your browser.

To uninstall, use **Settings → Apps → Installed apps**. The uninstaller removes the "Start with Windows" entry and asks whether to delete your settings and the saved login as well.

After the first login the session is cached; subsequent launches connect automatically.

## Usage

| Action | How |
|---|---|
| Translate | Type in the left box — translation appears after a short pause |
| Force translate | `Ctrl+Enter` |
| Translate clipboard | Press `Ctrl+C+C` anywhere on the system |
| Switch languages | Use the dropdowns or click ⇌ to swap |
| Copy result | Click **📋 Kopieren / Copy** |
| Switch engine | Classic ↔ Next-gen toggle in the title bar |
| Logout / switch account | ⚙ Verbindung → Abmelden |

## Building from source

```
git clone https://github.com/Naxterra/Better-DeepL-Translator.git
cd Better-DeepL-Translator
dotnet publish DeepLTranslator.csproj -c Release -r win-x64 --no-self-contained -o dist
```

Run the tests with `dotnet test`. Building requires the .NET 11 SDK.

To build the installer, run `installer\build.ps1` in PowerShell. It needs Inno Setup 7 (`winget install JRSoftware.InnoSetup.7`) and writes the setup file to `artifacts\installer`.

## Disclaimer

This app uses DeepL's internal web API on behalf of your own authenticated DeepL Pro account. It is not affiliated with or endorsed by DeepL SE. Use it in accordance with DeepL's Terms of Service.

## Author

**Naxterra** — [github.com/Naxterra](https://github.com/Naxterra)
