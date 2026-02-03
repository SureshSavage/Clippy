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
| **Live Subtitling** | Continuous real-time transcription displayed as a draggable overlay at the bottom of the screen |
| **Question Detection** | Automatically detects spoken questions using pattern matching (question marks and question-starting words) |
| **AI-Powered Answers** | Routes detected questions to a local Ollama LLM and displays concise answers in a separate overlay |
| **Model Selection** | Connects to Ollama on startup, shows connection status (green/red indicator), and lists all available models in a dropdown for switching |
| **System Tray Integration** | Minimizes to the macOS menu bar with show/exit controls |
| **Draggable Overlays** | Both subtitle and answer overlay windows can be repositioned via click-and-drag |

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│                       MainWindow                         │
│  ┌────────────────────────────────────────────────────┐  │
│  │  Model Status Bar                                  │  │
│  │  [● Connected]  [ComboBox: model list]  [Refresh]  │  │
│  └────────────────────────────────────────────────────┘  │
│  (Orchestrates UI, buttons, model selection)              │
├──────────┬──────────────────┬────────────────────────────┤
│          │                  │                             │
│  screencapture     LiveTranscriptionService     OllamaService
│  (macOS native)    ┌───────────────────┐    (LLM API + model mgmt)
│                    │  ffmpeg (audio)   │         │
│                    │       ↓           │    ListModelsAsync()
│                    │  Queue<float[]>   │    IsConnectedAsync()
│                    │  (drain & merge)  │    AskAsync()
│                    │       ↓           │         │
│                    │  Whisper.net      │         │
│                    │  (greedy, multi-  │         │
│                    │   threaded)       │         │
│                    │       ↓           │         │
│                    │  Question detect  │─────────┘
│                    └───────┬───────────┘
│                            │
│              ┌─────────────┴─────────────┐
│    SubtitleOverlayWindow       AnswerOverlayWindow
│    (live transcript text)      (Q&A display)
└──────────────────────────────────────────────────────────┘
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
| LLM Backend | Ollama (OpenAI-compatible API) | External service |
| LLM Model | Selectable via UI (default: qwen3-4b-thinking) | Configurable |
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
├── SubtitleOverlayWindow.axaml        # Subtitle overlay UI definition
├── SubtitleOverlayWindow.axaml.cs     # Subtitle overlay logic (draggable, transparent)
├── AnswerOverlayWindow.axaml          # Answer overlay UI definition
├── AnswerOverlayWindow.axaml.cs       # Answer overlay logic (draggable, scrollable)
├── LiveTranscriptionService.cs        # Real-time audio capture and transcription engine
├── OllamaService.cs                   # LLM API client, model listing, connection status
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
- **Ollama** — Required for AI question-answering
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

### 2. Set Up Ollama

Start Ollama on port 2276 and pull a model:

```bash
OLLAMA_HOST=0.0.0.0:2276 ollama serve
ollama pull qwen3-4b-thinking
```

You can pull multiple models — they will all appear in the in-app dropdown:

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
On startup, Clippy connects to the Ollama server and displays the connection status:
- **Green dot** + "Connected" — Ollama is reachable; the dropdown is populated with all available models.
- **Red dot** + "Disconnected" — Ollama is not running or unreachable; start Ollama and click **Refresh**.

Select any model from the dropdown before starting a Listen+Subtitle session. The selected model will be used for answering detected questions.

### Clip-it (Screenshot)
Click **Clip-it** to capture the screen. The window hides during capture to stay out of the screenshot. Images are saved to `~/Desktop/Clippy_Screenshots/` with timestamped filenames.

### Listen (Record & Transcribe)
Click **Listen** to start recording from the default microphone. Click again to stop. The recorded audio is transcribed using Whisper and the transcript is saved to `~/Desktop/Clippy_Transcripts/`.

### Listen+Subtitle (Live Transcription)
Click **Listen+Subtitle** to enable continuous real-time transcription. A subtitle overlay appears at the bottom of the screen showing the last 3 lines of recognized speech. If a question is detected, it is automatically sent to the selected Ollama model and the answer is displayed in a separate overlay below the subtitles. Click again to stop.

### Hide to Menu Bar
Click **Hide to Menu Bar** to minimize the application to the system tray. Right-click or click the tray icon to show the window again or exit.

## Configuration

### Ollama Service

The LLM connection is configured in `OllamaService.cs`:

| Parameter | Default | Description |
|-----------|---------|-------------|
| `_baseUrl` | `http://localhost:2276` | Ollama API endpoint |
| `CurrentModel` | `qwen3-4b-thinking` | LLM model name (changeable via UI dropdown) |
| Timeout | 60 seconds | HTTP request timeout |
| Prompt | `"Answer this question concisely in 1-2 sentences:\n\n{question}"` | System prompt for answers |

**API endpoints used:**
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/tags` | GET | List all available models for the dropdown |
| `/v1/chat/completions` | POST | Send questions and receive answers (OpenAI-compatible) |

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

### Audio Recording (ffmpeg)

| Parameter | Value |
|-----------|-------|
| Input | macOS avfoundation `:default` (default mic) |
| Format | PCM signed 16-bit little-endian (`s16le`) |
| Sample rate | 16000 Hz |
| Channels | 1 (mono) |
| Chunk interval | 1500 ms |
| Probe size | 32 bytes (minimal startup delay) |
| Analyze duration | 0 (no initial analysis delay) |

## Components

### MainWindow

The primary UI controller (600x420). Orchestrates all features — model management, screenshot capture, audio recording, live subtitling — and manages the lifecycle of overlay windows and services.

**Model Status Bar:**
- **Connection indicator** — Green/red dot showing Ollama connectivity
- **Connection label** — "Connected", "Disconnected", or "Checking..."
- **Model dropdown** — ComboBox populated with all available Ollama models
- **Refresh button** — Re-queries Ollama for the latest model list

**Buttons:**
- **Clip-it** — Screenshot capture
- **Listen** — Toggle audio recording and transcription
- **Listen+Subtitle** — Toggle live transcription with overlays (uses selected model)
- **Hide to Menu Bar** — Minimize to system tray

### LiveTranscriptionService

The real-time audio processing engine. Runs two background threads:

1. **ReadAudioLoop** — Continuously reads raw PCM bytes from ffmpeg's stdout and converts them to `float[]` audio samples, pushing them into a thread-safe queue.
2. **TranscribeLoop** — Drains all pending audio chunks from the queue (merging them if multiple have accumulated), runs them through the Whisper model, and checks for questions.

**Performance characteristics:**
- 1.5-second chunk interval for low-latency transcription
- Queue drain-and-merge prevents stale audio from piling up
- 100ms poll interval for responsive chunk pickup
- Multi-threaded Whisper inference with greedy decoding

**Question detection** triggers on:
- Text ending with `?`
- Text starting with any of: *what, why, how, when, where, who, which, is, are, do, does, can, could, would, should, will, shall, has, have, did, was, were*
- Uses a `HashSet` with case-insensitive comparison for O(1) lookups

### OllamaService

HTTP client for the Ollama LLM API. Provides model management and question answering.

**Capabilities:**
- `ListModelsAsync()` — Fetches all available models from `GET /api/tags`
- `IsConnectedAsync()` — Health check (returns true/false)
- `AskAsync()` — Sends questions via `POST /v1/chat/completions` (OpenAI-compatible)
- `CurrentModel` — Settable property for switching models at runtime
- Supports request cancellation so new questions cancel pending requests

**Request format:**
```json
{
  "model": "<selected-model>",
  "messages": [{ "role": "user", "content": "Answer this question concisely..." }],
  "stream": false
}
```

### SubtitleOverlayWindow

Borderless, transparent, always-on-top window (800x120) positioned at the bottom-center of the screen. Displays rolling transcription text in 22pt white semi-bold font on a semi-transparent black background (`#CC000000`). Supports drag-to-reposition.

### AnswerOverlayWindow

Borderless, transparent, always-on-top window (800x140) positioned directly below the subtitle overlay. Displays detected questions and AI-generated answers in a scrollable view on a semi-transparent dark blue background (`#CC1A237E`). Supports drag-to-reposition.

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
ReadAudioLoop (1.5s chunks, byte[] → float[] conversion)
    │
    ▼
Thread-safe Queue<float[]> (drain & merge on dequeue)
    │
    ▼
TranscribeLoop (Whisper.net — greedy, multi-threaded, no-context)
    │
    ├──► SubtitleOverlayWindow (rolling 3-line display)
    │
    ▼
Question Detection (HashSet pattern matching)
    │
    ▼
OllamaService.AskAsync() → Ollama API (selected model)
    │
    ▼
AnswerOverlayWindow (Q&A display)
```

### Model Selection Flow

```
App startup / Refresh button click
    │
    ▼
OllamaService.ListModelsAsync() → GET /api/tags
    │
    ├── Success → Green dot, populate ComboBox dropdown
    │
    └── Failure → Red dot, "Disconnected" label
    │
    ▼
User selects model from dropdown
    │
    ▼
_selectedModel updated → used by next Listen+Subtitle session
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
| **1.5s chunk interval** | Audio is processed in 1.5-second windows instead of 3 seconds, halving perceived latency |
| **Multi-threaded Whisper** | Uses `ProcessorCount / 2` threads for parallel inference across CPU cores |
| **Greedy sampling** | Skips expensive beam search decoding in favor of fastest-path greedy decoding |
| **No context reuse** | `WithNoContext()` prevents Whisper from reprocessing previous audio context each chunk |
| **Single segment mode** | `WithSingleSegment()` returns results immediately without waiting for segment boundaries |
| **Zero-delay ffmpeg startup** | `-probesize 32 -analyzeduration 0` eliminates ffmpeg's initial stream analysis delay |
| **Queue drain & merge** | When transcription falls behind, all pending chunks are merged into one instead of processing stale audio sequentially |
| **100ms poll interval** | Reduced from 500ms for faster chunk pickup between transcription cycles |
| **HashSet question lookup** | O(1) case-insensitive word matching instead of linear array search |

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
| Ollama | Local LLM inference server | `brew install ollama` |
| screencapture | macOS native screenshot utility | Pre-installed on macOS |
| Whisper model (ggml-base.en.bin) | English speech recognition model | [HuggingFace](https://huggingface.co/ggerganov/whisper.cpp/tree/main) |
