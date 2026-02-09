<div align="center">

<img src="./art/StoreLogo.svg" alt="CoCos logo" width="200" height="200">
<h1 align="center"><span style="font-weight: bold">CoCos</span> <br /><span style="font-weight: 200">Contextual Companions</span></h1>

<video width="600" controls>
  <source src="./art/intro.mp4" type="video/mp4">
  Your browser does not support the video tag.
</video>

</div>

> [!NOTE]
> **Work in Progress Prototype** — CoCos is an early-stage prototype. Features are like baby sea turtles, cute but not all will make it to the ocean.

## What are CoCos?

Ever feel like you're constantly copying-pasting between your work and an AI app? What if the AI came to you instead?

**CoCos** are personal AI companions that you can summon for any app on your screen. Each companion, or **"CoCo"**, sticks to your window and follows you around, ready to help.

The best part? Each CoCo gets its own quirky identity with a unique color and animal emoji (like 🐿️ or 🐰 or 🦖), so you always know who you're talking to. Think of them as your own army of personal, productive Minions!

Use them to summarize text, draft ideas, or just take quick notes without ever leaving your workflow.

## What can your CoCos do?

- **Stick to your apps like glue** — Summon a CoCo with a hotkey, and it stays attached to the app you're working in.
- **Summon a CoCo with a magic spell** — Okay, it's a hotkey (**Win + Shift + K**), but it feels like magic.
- **Build your own colorful army** — Each CoCo has a unique look so you can tell them apart.
- **Chat and take notes** — Every CoCo has a split personality: one part AI chatterbox, one part trusty notepad.
- **Know where they are and what you're doing** — CoCos recognize the app they're attached to and can reference your current input, selection, or context. In File Explorer, they can see your selected files. In text editors, they know what you've highlighted. Smart little creatures!
- **Bring Your Own Brain (AI Model)** — Power your CoCos with Ollama (for local, private use) or ChatGPT. You can even give different CoCos different brains!
- **Your data stays with you** — Your context isn't sent to the cloud by default. What happens in your app, stays in your app.

## Supported AI Models

Your CoCos can be powered by:
- **Ollama** — Run models locally on your machine (great for privacy!)
- **ChatGPT** — Use powerful cloud-based models (requires an API key)
- More models coming soon!

## CoCo Commands (Keyboard Shortcuts)

- **Win + Shift + K** — Summon a new CoCo for your active app.
- **Win + Shift + J** — Jump to the Notes tab in your current CoCo.
- **Ctrl + Enter** — Send a message or save a note.
- **Ctrl + Tab / Ctrl + Shift + Tab** — Switch between Chat and Notes.

## Adopt Your First CoCo (Getting Started)

**Requirements:** Windows 10 (1809+) or Windows 11, .NET SDK (net10.0), and either Ollama running locally or a ChatGPT API key.

### Build & Run

**Command line:**
```pwsh
dotnet build .\src\JPSoftworks.Cocos\JPSoftworks.Cocos.csproj -c Debug -p:Platform=x64
dotnet run --project .\src\JPSoftworks.Cocos\JPSoftworks.Cocos.csproj -c Debug -p:Platform=x64
```

**Visual Studio:**
1. Open `JPSoftworks.Cocos.slnx`
2. Set **JPSoftworks.Cocos** as the startup project
3. Press **F5**

## Teaching Your CoCos (Setup)

- Go to settings to choose your default AI model (Ollama or ChatGPT).
- If you're using ChatGPT, add your API key.
- You can override the default model for any individual CoCo. Give them their own personality!

## Where Your CoCos Live (Data)

App data, logs, and settings are stored in `%LOCALAPPDATA%\JPSoftworks\CoCos`.

## Licence

Apache 2.0

## Author

[Jiří Polášek](https://jiripolasek.com)