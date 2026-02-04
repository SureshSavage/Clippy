# Clippy

A macOS desktop application that provides intelligent clipboard management, real-time audio transcription with live subtitles, and AI-powered question answering — all accessible from the system tray.

## Table of Contents

- [Features](#features)
- [Architecture](#architecture)
- [Technology Stack](#technology-stack)
- [Project Structure](#project-structure)
- [Prerequisites](#prerequisites)
- [Setup](#setup)
- [Build & Run](#build--run)
- [Usage](#usage)
- [Configuration](#configuration)
- [Components](#components)
- [Data Flow](#data-flow)
- [Performance Optimizations](#performance-optimizations)
- [Dependencies](#dependencies)

## Features

| Feature | Description |
|---------|-------------|
| **Screenshot Capture** | Captures the screen using macOS native `screencapture` and saves to `~/Desktop/Clippy_Screenshots/` |
| **Audio Recording & Transcription** | Records microphone input via ffmpeg, transcribes with OpenAI Whisper (ggml-base.en model) |
| **Live Subtitling** | Continuous real-time transcription displayed as a resizable, draggable overlay at the bottom of the screen |
| **Voice Activity Detection** | Audio is transcribed based on speech/silence boundaries instead of fixed intervals — complete utterances are captured naturally |
| **Question Detection** | Automatically detects spoken questions using two-tier pattern matching (start words + keywords anywhere in text) |
| **Manual Ask Button** | An "Ask" button on the subtitle overlay lets users manually send the current transcript to the LLM if automatic detection missed a question |
| **AI-Powered Answers** | Routes detected questions to a local LLM (Ollama or LlamaBarn) and displays concise answers in a separate overlay |
| **Multi-Backend Support** | Probes both Ollama (port 11434) and LlamaBarn (port 2276) on startup, merging all models into a single dropdown |
| **Model Selection** | Shows connection status (green/red/orange indicator) and lists all available models from all backends in a dropdown |
| **Resizable Overlays** | Both overlay windows can be resized by dragging the grip in the bottom-right corner, with scrollable content |
| **System Tray Integration** | Minimizes to the macOS menu bar with show/exit controls |
| **Draggable Overlays** | Both subtitle and answer overlay windows can be repositioned via click-and-drag |

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                        MainWindow                            │
│  ┌────────────────────────────────────────────────────────┐  │
│  │  Model Status Bar                                      │  │
│  │  [● Ollama + LlamaBarn (5)]  [ComboBox]  [Refresh]     │  │
│  └────────────────────────────────────────────────────────┘  │
│  (Orchestrates UI, buttons, model selection)                  │
├──────────┬──────────────────┬────────────────────────────────┤
│          │                  │                                 │
│  screencapture     LiveTranscriptionService          LlmService
│  (macOS native)    ┌───────────────────┐      (multi-backend client)
│                    │  ffmpeg (audio)   │              │
│                    │       ↓           │     Ollama (11434)
│                    │  VadLoop (VAD)    │     LlamaBarn (2276)
│                    │  (speech/silence) │              │
│                    │       ↓           │    ListAllModelsAsync()
│                    │  Queue<float[]>   │    AskAsync()
│                    │       ↓           │              │
│                    │  Whisper.net      │              │
│                    │  (greedy, multi-  │              │
│                    │   threaded)       │              │
│                    │       ↓           │              │
│                    │  Question detect  │──────────────┘
│                    └───────┬───────────┘        ▲
│                            │                    │
│              ┌─────────────┴─────────────┐      │
│    SubtitleOverlayWindow       AnswerOverlayWindow
│    (transcript + Ask btn)      (Q&A display)
│    [resizable, scrollable]     [resizable, scrollable]
└──────────────────────────────────────────────────────────────┘
```

## Technology Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Language | C# | 11 |
| Runtime | .NET | 9.0 |
| UI Framework | Avalonia UI | 11.3.11 |
| UI Theme | Avalonia Fluent Theme | 11.3.11 |
| Speech-to-Text | Whisper.net (OpenAI Whisper) | 1.9.0 |
| Audio Capture | ffmpeg (avfoundation) | System-installed |
| LLM Backends | Ollama + LlamaBarn (OpenAI-compatible API) | External services |
| LLM Model | Selectable via UI from all backends | Configurable |
| Screenshots | macOS `screencapture` | System utility |

## Project Structure

```
Clippy/
├── Assets/
│   └── icon.png                       # System tray icon
├── App.axaml                          # Application XAML root (Fluent theme)
├── App.axaml.cs                       # Application lifecycle, tray icon setup
├── Program.cs                         # Entry point
├── MainWindow.axaml                   # Main window UI layout (model bar + buttons)
├── MainWindow.axaml.cs                # Main window logic (model mgmt, recording, subtitling)
├── SubtitleOverlayWindow.axaml        # Subtitle overlay UI (resizable, scrollable, Ask button)
├── SubtitleOverlayWindow.axaml.cs     # Subtitle overlay logic (drag, resize, ask callback)
├── AnswerOverlayWindow.axaml          # Answer overlay UI (resizable, scrollable)
├── AnswerOverlayWindow.axaml.cs       # Answer overlay logic (drag, resize)
├── LiveTranscriptionService.cs        # VAD-based audio capture and transcription engine
├── OllamaService.cs                   # Multi-backend LLM client (LlmService, LlmModel)
├── Clippy.csproj                      # Project file and NuGet dependencies
└── README.md
```

## Prerequisites

- **.NET 9.0 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet/9.0)
- **macOS** — Uses macOS-specific system tools (`screencapture`, `kill`)
- **ffmpeg** — Required for audio recording
  ```bash
  brew install ffmpeg
  ```
- **Ollama and/or LlamaBarn** — At least one LLM backend for question-answering
  ```bash
  brew install ollama
  ```
- **Whisper model file** — English base model for speech recognition

## Setup

### 1. Install the Whisper Model

Create the model directory and download the model:

```bash
mkdir -p ~/.clippy/models
# Download ggml-base.en.bin from https://huggingface.co/ggerganov/whisper.cpp/tree/main
# and place it at ~/.clippy/models/ggml-base.en.bin
```

### 2. Set Up LLM Backend(s)

Clippy auto-detects both backends on startup. Set up one or both:

**Ollama (port 11434):**
```bash
ollama serve
ollama pull qwen3-4b-thinking
```

**LlamaBarn (port 2276):**
```bash
# Start LlamaBarn on its default port
# Models served by LlamaBarn will appear in the dropdown automatically
```

You can pull multiple models — they will all appear in the in-app dropdown with their backend label:

```bash
ollama pull llama3
ollama pull mistral
```

### 3. Restore NuGet Packages

```bash
cd Clippy
dotnet restore
```

## Build & Run

**Debug build:**

```bash
dotnet build
dotnet run
```

**Release build:**

```bash
dotnet publish -c Release
```

Output artifacts:
- Debug: `bin/Debug/net9.0/Clippy.dll`
- Release: `bin/Release/net9.0/publish/`

## Usage

### Model Selection
On startup, Clippy probes both Ollama (port 11434) and LlamaBarn (port 2276):
- **Green dot** + "Ollama + LlamaBarn (5)" — Both backends reachable; all models listed in the dropdown with backend labels (e.g. `qwen3-4b-thinking [LlamaBarn]`).
- **Green dot** + "Ollama (3)" — Only Ollama is reachable.
- **Orange dot** + "No models" — Backends reachable but no models pulled.
- **Red dot** + "Error" — No backends reachable; start a backend and click **Refresh**.

Select any model from the dropdown before starting a Listen+Subtitle session.

### Clip-it (Screenshot)
Click **Clip-it** to capture the screen. The window hides during capture to stay out of the screenshot. Images are saved to `~/Desktop/Clippy_Screenshots/` with timestamped filenames.

### Listen (Record & Transcribe)
Click **Listen** to start recording from the default microphone. Click again to stop. The recorded audio is transcribed using Whisper and the transcript is saved to `~/Desktop/Clippy_Transcripts/`.

### Listen+Subtitle (Live Transcription)
Click **Listen+Subtitle** to enable continuous real-time transcription. A subtitle overlay appears at the bottom of the screen showing the last 3 lines of recognized speech. Audio is processed using voice activity detection — transcription happens when a pause in speech is detected, giving complete natural utterances.

If a question is detected, it is automatically sent to the selected model and the answer appears in the blue overlay below. If automatic detection missed a question, click the **Ask** button on the subtitle overlay to manually send the current text.

Both overlays can be **dragged** anywhere on screen and **resized** by dragging the grip in the bottom-right corner. Content scrolls when it overflows.

### Hide to Menu Bar
Click **Hide to Menu Bar** to minimize the application to the system tray. Right-click or click the tray icon to show the window again or exit.

## Configuration

### LLM Service

The multi-backend LLM client is configured in `OllamaService.cs`:

**Backends (probed automatically):**
| Backend | URL | Model Listing | Chat API |
|---------|-----|---------------|----------|
| Ollama | `http://localhost:11434` | `/api/tags` then `/v1/models` | `/v1/chat/completions` |
| LlamaBarn | `http://localhost:2276` | `/api/tags` then `/v1/models` | `/v1/chat/completions` |

| Parameter | Default | Description |
|-----------|---------|-------------|
| List timeout | 10 seconds | Timeout for model listing requests |
| Inference timeout | 120 seconds | Timeout for question-answering requests |
| Prompt | `"Answer this question concisely in 1-2 sentences:\n\n{question}"` | System prompt for answers |

### Whisper Model

| Parameter | Value |
|-----------|-------|
| Model path | `~/.clippy/models/ggml-base.en.bin` |
| Language | English (`en`) |
| Sample rate | 16 kHz |
| Channels | Mono |
| Threads | `ProcessorCount / 2` (auto-detected) |
| Sampling strategy | Greedy (fastest) |
| Context reuse | Disabled (`WithNoContext`) |
| Segment mode | Single segment (`WithSingleSegment`) |

### Voice Activity Detection (VAD)

| Parameter | Value | Description |
|-----------|-------|-------------|
| Frame size | 100ms (1600 samples) | Small frames for responsive speech detection |
| Silence threshold | 0.015 RMS | Energy below this is considered silence |
| Silence trigger | 600ms (6 frames) | Duration of silence after speech to trigger transcription |
| Min speech | 300ms (3 frames) | Minimum speech duration to avoid noise bursts |
| Max speech | 15 seconds | Safety cap to flush very long utterances |

### Question Detection

Two-tier pattern matching with `HashSet<string>` (O(1) case-insensitive lookups):

| Tier | Words | Match Rule |
|------|-------|------------|
| **Start words** | what, why, how, when, where, who, which, is, are, do, does, can, could, would, should, will, shall, has, have, did, was, were, explain, explains, describe, define, tell | Must be the **first word** |
| **Keywords** | explain, explains, example, using, difference, how, what, compare, between, meaning, tell, me | Can appear **anywhere** in the text |

## Components

### MainWindow

The primary UI controller (600x420). Orchestrates all features — model management, screenshot capture, audio recording, live subtitling — and manages the lifecycle of overlay windows and services.

**Model Status Bar:**
- **Connection indicator** — Green/orange/red dot showing backend connectivity
- **Connection label** — Lists connected backends and model count
- **Model dropdown** — ComboBox showing all models with backend labels (e.g. `model-name [Ollama]`)
- **Refresh button** — Re-probes all backends for the latest model list

**Buttons:**
- **Clip-it** — Screenshot capture
- **Listen** — Toggle audio recording and transcription
- **Listen+Subtitle** — Toggle live transcription with overlays (uses selected model)
- **Hide to Menu Bar** — Minimize to system tray

### LiveTranscriptionService

The VAD-based real-time audio processing engine. Runs two background threads:

1. **VadLoop** — Reads 100ms audio frames from ffmpeg, calculates RMS energy to detect speech vs. silence. Accumulates audio during speech, then flushes to the transcription queue when 600ms of silence is detected after speech.
2. **TranscribeLoop** — Drains all pending speech segments from the queue (merging if multiple accumulated), runs them through Whisper, and checks for questions.

**How VAD works:**
```
Microphone → ffmpeg → 100ms frames → RMS energy check
                                         │
                        ┌────────────────┴────────────────┐
                    Speech (RMS ≥ 0.015)           Silence (RMS < 0.015)
                        │                                  │
                   Accumulate frames              Was there prior speech?
                        │                            Yes → count silent frames
                   Hit 15s cap? → Flush              ≥ 600ms silence? → Flush to Whisper
```

**Question detection** triggers on:
- Text containing `?`
- Text starting with a question/command word (what, explain, tell, etc.)
- Text containing a question keyword anywhere (difference, example, using, etc.)

### LlmService

Multi-backend HTTP client for LLM inference. Probes both Ollama and LlamaBarn, aggregating models from all reachable backends.

**Capabilities:**
- `ListAllModelsAsync()` — Probes all backends, tries `/api/tags` then falls back to `/v1/models`
- `AskAsync()` — Routes questions to the correct backend URL based on the selected model
- `SelectedModel` — `LlmModel` record containing name, backend label, and base URL
- Supports request cancellation so new questions cancel pending requests

**Data model:**
```csharp
record LlmModel(string Name, string Backend, string BaseUrl)
// Example: LlmModel("qwen3-4b-thinking", "LlamaBarn", "http://localhost:2276")
// Displays as: "qwen3-4b-thinking  [LlamaBarn]"
```

### SubtitleOverlayWindow

Borderless, transparent, always-on-top overlay (default 800x120, min 300x80). Displays rolling transcription text in 22pt white semi-bold font on a semi-transparent black background (`#CC000000`).

**Features:**
- **Drag to move** — Click and drag anywhere on the overlay body
- **Resize** — Drag the grip lines in the bottom-right corner
- **Scrollable** — Content scrolls vertically when text overflows
- **Ask button** — Manually sends the current transcript text to the selected LLM model for an answer

### AnswerOverlayWindow

Borderless, transparent, always-on-top overlay (default 800x140, min 300x80). Displays detected questions and AI-generated answers on a semi-transparent dark blue background (`#CC1A237E`).

**Features:**
- **Drag to move** — Click and drag anywhere on the overlay body
- **Resize** — Drag the grip lines in the bottom-right corner
- **Scrollable** — Content scrolls vertically for long answers

### App

Manages application lifecycle, initializes the Fluent theme, and sets up the system tray icon with "Show Clippy" and "Exit" context menu options.

## Data Flow

### Audio Transcription Pipeline

```
Microphone
    │
    ▼
ffmpeg (avfoundation, 16kHz mono PCM s16le, zero-delay probe)
    │
    ▼
VadLoop (100ms frames, RMS energy → speech/silence detection)
    │
    ├── Speech detected → accumulate in buffer
    │
    ├── 600ms silence after speech → flush buffer to queue
    │
    ▼
Thread-safe Queue<float[]> (drain & merge on dequeue)
    │
    ▼
TranscribeLoop (Whisper.net — greedy, multi-threaded, no-context)
    │
    ├──► SubtitleOverlayWindow (rolling 3-line display + Ask button)
    │                                          │
    ▼                                          │ (manual ask)
Question Detection (two-tier HashSet matching) ◄┘
    │
    ▼
LlmService.AskAsync() → correct backend (Ollama or LlamaBarn)
    │
    ▼
AnswerOverlayWindow (Q&A display)
```

### Model Selection Flow

```
App startup / Refresh button click
    │
    ▼
LlmService.ListAllModelsAsync()
    │
    ├── Probe Ollama (localhost:11434)
    │     ├── Try /api/tags → parse "models[].name"
    │     └── Fallback /v1/models → parse "data[].id"
    │
    ├── Probe LlamaBarn (localhost:2276)
    │     ├── Try /api/tags → parse "models[].name"
    │     └── Fallback /v1/models → parse "data[].id"
    │
    ├── Both succeed → Green dot, "Ollama + LlamaBarn (N)"
    ├── One succeeds → Green dot, "Backend (N)"
    ├── None with models → Orange dot, "No models"
    └── All fail → Red dot, "Error"
    │
    ▼
User selects model from dropdown (shows backend label)
    │
    ▼
LlmModel selected → routes to correct backend URL on next session
```

### Screenshot Pipeline

```
Clip-it button click
    │
    ▼
MainWindow hides
    │
    ▼
macOS screencapture -x (silent capture)
    │
    ▼
Save to ~/Desktop/Clippy_Screenshots/clip_YYYYMMDD_HHmmss.png
    │
    ▼
MainWindow shows, status updated
```

## Performance Optimizations

The live transcription pipeline is tuned for low-latency real-time use:

| Optimization | Detail |
|---|---|
| **Voice Activity Detection** | Audio is transcribed on speech/silence boundaries instead of fixed intervals — Whisper only runs when someone actually spoke |
| **Multi-threaded Whisper** | Uses `ProcessorCount / 2` threads for parallel inference across CPU cores |
| **Greedy sampling** | Skips expensive beam search decoding in favor of fastest-path greedy decoding |
| **No context reuse** | `WithNoContext()` prevents Whisper from reprocessing previous audio context each chunk |
| **Single segment mode** | `WithSingleSegment()` returns results immediately without waiting for segment boundaries |
| **Zero-delay ffmpeg startup** | `-probesize 32 -analyzeduration 0` eliminates ffmpeg's initial stream analysis delay |
| **Queue drain & merge** | When transcription falls behind, all pending speech segments are merged into one |
| **100ms poll interval** | Fast chunk pickup between transcription cycles |
| **HashSet question lookup** | O(1) case-insensitive word matching with two-tier detection |
| **Persistent inference client** | Reuses a single `HttpClient` for all LLM requests instead of creating one per call |

## Dependencies

### NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| Avalonia | 11.3.11 | Cross-platform XAML UI framework |
| Avalonia.Desktop | 11.3.11 | Desktop platform support |
| Avalonia.Themes.Fluent | 11.3.11 | Modern Fluent design theme |
| Whisper.net | 1.9.0 | OpenAI Whisper speech-to-text wrapper |
| Whisper.net.Runtime | 1.9.0 | Native runtime for Whisper inference |

### External System Dependencies

| Dependency | Purpose | Install |
|------------|---------|---------|
| ffmpeg | Audio recording and format conversion | `brew install ffmpeg` |
| Ollama | Local LLM inference server (port 11434) | `brew install ollama` |
| LlamaBarn | Alternative LLM inference server (port 2276) | See LlamaBarn docs |
| screencapture | macOS native screenshot utility | Pre-installed on macOS |
| Whisper model (ggml-base.en.bin) | English speech recognition model | [HuggingFace](https://huggingface.co/ggerganov/whisper.cpp/tree/main) |
