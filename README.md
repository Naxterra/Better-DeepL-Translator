# Better DeepL Translator

A lightweight Windows desktop app that brings your **DeepL Pro** subscription to the desktop — no browser tab needed.

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![.NET](https://img.shields.io/badge/.NET-10-purple)
![License](https://img.shields.io/badge/license-MIT-green)

## Features

- **Instant translation** — type or paste and results appear automatically
- **Global hotkey** `Ctrl+C+C` — copy any text anywhere and it opens translated
- **Auto-login** — reads your active DeepL Pro session from Brave browser automatically, no manual token setup
- **Classic & Next-gen engine** toggle per translation
- **Language swap** — flip source and target with one click
- **System tray** — minimizes to tray, always one click away
- **Start with Windows** — optional autorun via registry
- **DE / EN UI** — interface language toggle in the title bar
- **Dark theme** throughout

## Requirements

- Windows 10 or 11 (64-bit)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) — x64
- An active **DeepL Pro** subscription logged in via Brave browser

## Installation

1. Download `Better DeepL Translator.exe` from the [latest release](https://github.com/Naxterra/Better-DeepL-Translator/releases/latest)
2. Run it — no installer, no admin rights needed
3. Click **⚙ Verbindung → Anmelden** to authenticate (opens browser OAuth flow)

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
dotnet publish -c Release -r win-x64 --no-self-contained -o dist
```

Requires .NET 10 SDK.

## Disclaimer

This app uses DeepL's internal web API on behalf of your own authenticated DeepL Pro account. It is not affiliated with or endorsed by DeepL SE. Use it in accordance with DeepL's Terms of Service.

## Author

**Naxterra** — [github.com/Naxterra](https://github.com/Naxterra)
