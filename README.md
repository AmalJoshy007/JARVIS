# J.A.R.V.I.S. Floating Desktop AI Assistant (.NET 8 & Ollama)

Autonomous, offline-first voice and desktop assistant for Windows built with **C# / .NET 8 WPF**, **Ollama** running local **IBM Granite 3.1**, and **Microsoft Semantic Kernel**.

Features a **futuristic floating Arc Reactor orb** directly on your desktop that expands into a **holographic glassmorphic HUD window** upon click.

---

## Architecture Overview

```text
Jarvis/
│
├── JarvisBot.csproj                  # .NET 8 Windows WPF, Semantic Kernel, Ollama connector, System.Speech
├── App.xaml / App.xaml.cs            # WPF Application lifecycle, window coordinator & event dispatching
│
├── UI/
│   ├── FloatingOrbWindow.xaml        # Floating desktop Arc Reactor orb (topmost, draggable, audio-reactive)
│   ├── FloatingOrbWindow.xaml.cs     # Orb rotation animations, voice pulse scaling, click-to-expand
│   ├── JarvisHudWindow.xaml          # Glassmorphic holographic HUD (transcripts, live visualizer, quick chips)
│   └── JarvisHudWindow.xaml.cs       # 16-bar neon equalizer, chat feed, quick action handlers
│
├── Configuration/
│   └── AppSettings.cs                # Strongly-typed configuration loader
├── Plugins/
│   ├── SystemControlPlugin.cs        # Native tools: App launching, Workstation lock, Clock
│   └── WebNavigationPlugin.cs        # Native tools: Google Search, YouTube navigation
├── Services/
│   ├── AudioService.cs               # Dual command/dictation STT, TTS, and live audio monitoring
│   └── AgentService.cs               # Semantic Kernel + Ollama Chat + Native Tool Dispatching
├── scripts/
│   ├── Setup-StartupShortcut.ps1     # Setup shortcut in shell:startup
│   ├── Setup-TaskScheduler.ps1       # Setup Windows Scheduled Task with highest privileges
│   └── Remove-Startup.ps1            # Uninstall autostart entries
├── publish/
│   └── JarvisBot.exe                 # Published self-contained single-file executable
└── appsettings.json                  # Model endpoint, model name, and speech parameters
```

---

## Visual Experience & Desktop UI

### 1. Floating Desktop Arc Reactor Orb
- Sits unobtrusively at the bottom-right corner of your desktop, floating on top of all windows (`Topmost="True"`).
- **Concentric Rotating Neon Rings:** Counter-rotating cybernetic dashed circles.
- **Audio-Reactive Glow:** Scales and pulses dynamically in real time whenever your microphone hears sound or your voice!
- **Draggable:** Click and drag the orb anywhere on your screen.
- **Click to Expand:** Single-click or double-click instantly toggles the Holographic HUD.
- **Right-Click Context Menu:** Quick options to Open HUD, Toggle Mic, Reset Position, or Shutdown.

### 2. Holographic Glassmorphic HUD
- **Deep Glassmorphism:** Semi-transparent dark cosmic backdrop (`#0E1424`) with electric cyan drop-shadow neon glow.
- **Live 16-Bar Audio Equalizer:** Neon equalizer bars that bounce in real time to speech input.
- **Live Speech Transcription:** Streams hypothesized words as you speak (*"🎙️ Listening: open not..."*).
- **Interactive Transcript Feed:** Shows user voice inputs, J.A.R.V.I.S. responses, and action execution chips (*"🚀 Launched Notepad"*).
- **Quick Action Chips:** One-click shortcut buttons for `📝 Notepad`, `🔢 Calculator`, `🌐 Search Web`, `📺 YouTube`, `⏰ Current Time`, and `🔒 Lock Screen`.
- **Hybrid Controls:** Speak freely or type into the cybernetic input bar.

---

## Requirements

1. **.NET 8.0 Windows Desktop Runtime / SDK** (Installed)
2. **Ollama for Windows** (Running on `http://localhost:11434`)
3. **IBM Granite Model**:
   ```bash
   ollama pull granite3.1-dense:2b
   ```
4. **Microphone & Speakers** for speech recognition and synthesis.

---

## Configuration (`appsettings.json`)

```json
{
  "Jarvis": {
    "OllamaEndpoint": "http://localhost:11434",
    "ModelId": "granite3.1-dense:2b",
    "SpeechConfidenceThreshold": 0.30,
    "SpeechRate": 1,
    "SystemPrompt": "You are J.A.R.V.I.S., a direct, concise, and efficient desktop AI assistant. When the user requests an action, use the tool syntax: [TOOL_CALL: PluginName-FunctionName {\"param\": \"value\"}]. Available tools:\n- SystemControl-LaunchApplication {\"applicationName\": \"string\"} (opens apps like notepad, calc, chrome, spotify, cmd)\n- SystemControl-LockWorkstation {} (locks screen)\n- SystemControl-OpenWebsite {\"url\": \"string\"} (opens url)\n- SystemControl-GetCurrentTime {} (gets current local time)\n- WebNavigation-SearchWeb {\"query\": \"string\"} (searches Google)\n- WebNavigation-OpenYouTube {\"query\": \"string\"} (opens YouTube)\nIf no tool is needed (e.g., greetings or conversational queries), reply directly in under 2 sentences. Do not generate fake user replies or multiple dialogue turns."
  }
}
```

---

## Build & Run

### Launch in Development
```powershell
dotnet run
```

### Launch Published Single-File Executable
```powershell
.\publish\JarvisBot.exe
```

---

## Autostart on Windows Boot

- **Option A (Startup Folder Shortcut):**
  ```powershell
  powershell -ExecutionPolicy Bypass -File scripts\Setup-StartupShortcut.ps1
  ```
- **Option B (Task Scheduler - Highest Privileges):**
  ```powershell
  powershell -ExecutionPolicy Bypass -File scripts\Setup-TaskScheduler.ps1
  ```

---

## Voice Commands

| Action | Spoken Instruction | Triggered Execution |
|---|---|---|
| Launch Application | *"Open Notepad"* | Launches `notepad.exe` |
| Launch Calculator | *"Open Calculator"* | Launches `calc.exe` |
| Search Google | *"Search the web for quantum computing"* | Opens default browser with search query |
| YouTube | *"Open YouTube"* | Navigates to YouTube |
| Current Time | *"What time is it?"* | Spoken local time |
| Lock Workstation | *"Lock screen"* | Locks Windows workstation |
