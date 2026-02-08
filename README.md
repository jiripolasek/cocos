<div align="center">

<img src="./art/StoreLogo.svg" alt="CoCos logo" width="200" height="200">
<h1 align="center"><span style="font-weight: bold">CoCos</span> <br /><span style="font-weight: 200">Contextual Companions</span></h1>

</div>

CoCos (Contextual Companions) is a tray-first WinUI 3 prototype that spawns sticky companion windows attached to your active app. Use global hotkeys to create a companion, capture lightweight context, and switch between chat and notes without losing focus.

Highlights:

- System tray app with a hidden main/settings window by default.
- Global hotkey to create a companion attached to the current foreground window.
- Companion windows follow their parent, stay top-most while the parent is active, and support custom positioning.
- Chat + Notes tabs, with context preview and quick note saving.
- Per-companion model override with a default model fallback.
- SQLite persistence for sessions, chat history, and notes.

## Keyboard shortcuts

- **Win + Shift + K** — Create a new companion for the active window.
- **Win + Shift + J** — Open the Notes tab in the active companion.
- **Ctrl + Enter** — Send chat prompt / add a note (when the input is focused).
- **Ctrl + Tab / Ctrl + Shift + Tab** — Switch between Chat and Notes tabs.

## Getting started

> **Note:** Requires Windows 10 (1809+) or Windows 11 and the .NET SDK matching the `net10.0` target.

### Build and run (CLI)

```pwsh
dotnet build .\src\JPSoftworks.Cocos\JPSoftworks.Cocos.csproj -c Debug -p:Platform=x64
dotnet run --project .\src\JPSoftworks.Cocos\JPSoftworks.Cocos.csproj -c Debug -p:Platform=x64
```

### Build and run (Visual Studio)

- Open `JPSoftworks.Cococs.slnx`.
- Set **JPSoftworks.Cococs** as the startup project.
- Press **F5**.

## Project structure

- `JPSoftworks.Cococs.slnx` — Solution file.
- `src/JPSoftworks.Cocos` — WinUI 3 app (windows, services, views, view models, assets).
  - `Services/Chat`, `Services/Companion`, `Services/Context`, `Services/HotKeys`, `Services/Settings`
- `tests` — Test projects (currently empty).

## Data & logs

- App data (including logs and SQLite) lives under `%LOCALAPPDATA%\JPSoftworks\CoCos`.

## Licence

Apache 2.0

## Author

[Jiří Polášek](https://jiripolasek.com)