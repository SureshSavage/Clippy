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
| **Audio Recording & Transcription** | Records microphone input via ffmpeg, transcribes with OpenAI Whisper (selectable model) |
| **Live Subtitling** | Continuous real-time transcription displayed as a resizable, draggable overlay at the bottom of the screen |
| **Voice Activity Detection** | Audio is transcribed based on speech/silence boundaries instead of fixed intervals — complete utterances are captured naturally |
| **Question Detection** | Automatically detects spoken questions using two-tier pattern matching (start words + keywords anywhere in text) |
| **Manual Ask Button** | An "Ask" button on the subtitle overlay lets users manually send the current transcript to the LLM if automatic detection missed a question |
| **AI-Powered Answers** | Routes detected questions to a local LLM (Ollama or LlamaBarn) and displays concise answers in a separate overlay |
| **Multi-Backend Support** | Probes both Ollama (port 11434) and LlamaBarn (port 2276) on startup, merging all models into a single dropdown |
| **LLM Model Selection** | Shows connection status (green/red/orange indicator) and lists all available LLM models from all backends in a dropdown |
| **Whisper Model Selection** | Dropdown listing all available Whisper ggml models (tiny through large-v3) with in-app download and size info |
| **Text-to-Speech (Read Aloud)** | Answers can be read aloud using macOS built-in `say` command — auto-triggered via checkbox or manually via speaker button on the answer overlay |
| **Resizable Overlays** | Both overlay windows can be resized by dragging the grip in the bottom-right corner, with scrollable content |
| **System Tray Integration** | Minimizes to the macOS menu bar with show/exit controls |
| **Draggable Overlays** | Both subtitle and answer overlay windows can be repositioned via click-and-drag |

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                        MainWindow                            │
│  ┌────────────────────────────────────────────────────────┐  │
│  │  LLM Status Bar                                        │  │
│  │  [● Ollama + LlamaBarn (5)]  [ComboBox]  [Refresh]     │  │
│  ├────────────────────────────────────────────────────────┤  │
│  │  Whisper Model Bar                                      │  │
│  │  [Whisper:]  [ComboBox ▾ Base (English)]  [Download]    │  │
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
│                    │  (selectable      │              │
│                    │   model size)     │              │
│                    │       ↓           │              │
│                    │  Question detect  │──────────────┘
│                    └───────┬───────────┘        ▲
│                            │                    │
│              ┌─────────────┴─────────────┐      │
│    SubtitleOverlayWindow       AnswerOverlayWindow
│    (transcript + Ask btn)      (Q&A display)
│    [resizable, scrollable]     [resizable, scrollable]
└──────────────────────────────────────────────────────────────┘

WhisperModelManager
  └── Scans ~/.clippy/models/ for installed ggml-*.bin files
  └── Lists known models (tiny → large-v3) with sizes
  └── Downloads models from HuggingFace with progress
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
| Whisper Model | Selectable via UI from installed ggml models | Configurable |
| Screenshots | macOS `screencapture` | System utility |
| Text-to-Speech | macOS `say` | System utility |

## Project Structure

```
Clippy/
├── Assets/
│   └── icon.png                       # System tray icon
├── App.axaml                          # Application XAML root (Fluent theme)
├── App.axaml.cs                       # Application lifecycle, tray icon setup
├── Program.cs                         # Entry point
├── MainWindow.axaml                   # Main window UI layout (LLM bar + Whisper bar + buttons)
├── MainWindow.axaml.cs                # Main window logic (model mgmt, recording, subtitling)
├── SubtitleOverlayWindow.axaml        # Subtitle overlay UI (resizable, scrollable, Ask button)
├── SubtitleOverlayWindow.axaml.cs     # Subtitle overlay logic (drag, resize, ask callback)
├── AnswerOverlayWindow.axaml          # Answer overlay UI (resizable, scrollable)
├── AnswerOverlayWindow.axaml.cs       # Answer overlay logic (drag, resize)
├── LiveTranscriptionService.cs        # VAD-based audio capture and transcription engine
├── OllamaService.cs                   # Multi-backend LLM client (LlmService, LlmModel)
├── WhisperModelManager.cs             # Whisper model discovery, listing, and download
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

## Setup

### 1. Install a Whisper Model

Models can be downloaded directly from the app using the **Download** button next to the Whisper dropdown. Alternatively, download manually:

```bash
mkdir -p ~/.clippy/models
# Download any ggml model from https://huggingface.co/ggerganov/whisper.cpp/tree/main
# Example: ggml-base.en.bin (142 MB, good starting point)
curl -L -o ~/.clippy/models/ggml-base.en.bin \
  https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin
```

Available Whisper models (larger = more accurate, slower):

| Model | File | Size | Notes |
|-------|------|------|-------|
| Tiny (English) | `ggml-tiny.en.bin` | 75 MB | Fastest, lowest accuracy |
| Tiny | `ggml-tiny.bin` | 75 MB | Multilingual |
| Base (English) | `ggml-base.en.bin` | 142 MB | Good balance for English |
| Base | `ggml-base.bin` | 142 MB | Multilingual |
| Small (English) | `ggml-small.en.bin` | 466 MB | Better for technical words |
| Small | `ggml-small.bin` | 466 MB | Multilingual |
| Medium (English) | `ggml-medium.en.bin` | 1.5 GB | High accuracy |
| Medium | `ggml-medium.bin` | 1.5 GB | Multilingual |
| Large v3 | `ggml-large-v3.bin` | 2.9 GB | Best accuracy, slowest |

For better recognition of technical vocabulary, use **Small** or **Medium** English models.

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

### LLM Model Selection
On startup, Clippy probes both Ollama (port 11434) and LlamaBarn (port 2276):
- **Green dot** + "Ollama + LlamaBarn (5)" — Both backends reachable; all models listed in the dropdown with backend labels (e.g. `qwen3-4b-thinking [LlamaBarn]`).
- **Green dot** + "Ollama (3)" — Only Ollama is reachable.
- **Orange dot** + "No models" — Backends reachable but no models pulled.
- **Red dot** + "Error" — No backends reachable; start a backend and click **Refresh**.

Select any model from the dropdown before starting a Listen+Subtitle session.

### Whisper Model Selection
The Whisper model bar shows all known ggml models with their install status:
- **Installed models** show `[installed]` in the dropdown and are ready to use.
- **Uninstalled models** show their size (e.g. `Small (English) (466 MB)`). Select one and click **Download** to install it — progress is shown as a percentage on the button.
- The first installed model is auto-selected on startup.
- Any `.bin` file dropped into `~/.clippy/models/` is automatically detected.
- Changing the Whisper model takes effect on the next Listen or Listen+Subtitle session.

### Clip-it (Screenshot)
Click **Clip-it** to capture the screen. The window hides during capture to stay out of the screenshot. Images are saved to `~/Desktop/Clippy_Screenshots/` with timestamped filenames.

### Listen (Record & Transcribe)
Click **Listen** to start recording from the default microphone. Click again to stop. The recorded audio is transcribed using the selected Whisper model and the transcript is saved to `~/Desktop/Clippy_Transcripts/`.

### Listen+Subtitle (Live Transcription)
Click **Listen+Subtitle** to enable continuous real-time transcription. A subtitle overlay appears at the bottom of the screen showing the last 3 lines of recognized speech. Audio is processed using voice activity detection — transcription happens when a pause in speech is detected, giving complete natural utterances.

If a question is detected, it is automatically sent to the selected LLM model and the answer appears in the blue overlay below. If automatic detection missed a question, click the **Ask** button on the subtitle overlay to manually send the current text.

Both overlays can be **dragged** anywhere on screen and **resized** by dragging the grip in the bottom-right corner. Content scrolls when it overflows.

### Read Aloud (Text-to-Speech)
Check the **Read answers aloud** checkbox in the main window to have every LLM answer automatically spoken via the macOS `say` command. You can also click the **speaker button** on the answer overlay to manually re-read the current answer at any time. Starting a new answer automatically stops any in-progress speech.

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
| Model directory | `~/.clippy/models/` |
| Default model | `ggml-base.en.bin` (fallback if no selection) |
| Download source | `https://huggingface.co/ggerganov/whisper.cpp/resolve/main/` |
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

The primary UI controller (600x490). Orchestrates all features — model management, screenshot capture, audio recording, live subtitling — and manages the lifecycle of overlay windows and services.

**LLM Status Bar:**
- **Connection indicator** — Green/orange/red dot showing backend connectivity
- **Connection label** — Lists connected backends and model count
- **Model dropdown** — ComboBox showing all LLM models with backend labels (e.g. `model-name [Ollama]`)
- **Refresh button** — Re-probes all backends for the latest model list

**Whisper Model Bar:**
- **Label** — "Whisper:" indicating the transcription model section
- **Model dropdown** — ComboBox listing all known Whisper ggml models with install status and size
- **Download button** — Downloads the selected uninstalled model from HuggingFace with live progress

**Buttons:**
- **Clip-it** — Screenshot capture
- **Listen** — Toggle audio recording and transcription (uses selected Whisper model)
- **Listen+Subtitle** — Toggle live transcription with overlays (uses selected Whisper and LLM models)
- **Read answers aloud** — Checkbox to auto-speak LLM answers via macOS `say`
- **Hide to Menu Bar** — Minimize to system tray

### WhisperModelManager

Manages Whisper model discovery and installation. Located in `WhisperModelManager.cs`.

**Capabilities:**
- `GetAvailableModels()` — Scans `~/.clippy/models/` for installed ggml models, returns all known models with install status
- `GetCurrentModel()` — Returns the first installed model
- `DownloadModelAsync()` — Downloads a model from HuggingFace with progress callback (percentage)

**Data model:**
```csharp
record WhisperModelInfo(string Name, string FileName, long SizeMb, bool IsInstalled, string FilePath)
// Example: WhisperModelInfo("Small (English)", "ggml-small.en.bin", 466, true, "/Users/.../.clippy/models/ggml-small.en.bin")
// Displays as: "Small (English) (466 MB) [installed]"
```

**Known models (auto-listed):**
- Tiny / Tiny (English) — 75 MB
- Base / Base (English) — 142 MB
- Small / Small (English) — 466 MB
- Medium / Medium (English) — 1.5 GB
- Large v3 — 2.9 GB

Custom `.bin` files placed in `~/.clippy/models/` are also detected automatically.

### LiveTranscriptionService

The VAD-based real-time audio processing engine. Runs two background threads:

1. **VadLoop** — Reads 100ms audio frames from ffmpeg, calculates RMS energy to detect speech vs. silence. Accumulates audio during speech, then flushes to the transcription queue when 600ms of silence is detected after speech.
2. **TranscribeLoop** — Drains all pending speech segments from the queue (merging if multiple accumulated), runs them through Whisper, and checks for questions.

Accepts a model path parameter so the caller can pass the user-selected Whisper model.

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
- **Speaker button** — Reads the current answer aloud using macOS `say`; kills any previous speech before starting

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
TranscribeLoop (Whisper.net — selected model, greedy, multi-threaded, no-context)
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
    ├── LLM Models ──────────────────────────────────────────────
    │   │
    │   ▼
    │   LlmService.ListAllModelsAsync()
    │   │
    │   ├── Probe Ollama (localhost:11434)
    │   │     ├── Try /api/tags → parse "models[].name"
    │   │     └── Fallback /v1/models → parse "data[].id"
    │   │
    │   ├── Probe LlamaBarn (localhost:2276)
    │   │     ├── Try /api/tags → parse "models[].name"
    │   │     └── Fallback /v1/models → parse "data[].id"
    │   │
    │   ├── Both succeed → Green dot, "Ollama + LlamaBarn (N)"
    │   ├── One succeeds → Green dot, "Backend (N)"
    │   ├── None with models → Orange dot, "No models"
    │   └── All fail → Red dot, "Error"
    │
    ├── Whisper Models ──────────────────────────────────────────
    │   │
    │   ▼
    │   WhisperModelManager.GetAvailableModels()
    │   │
    │   ├── List known models (tiny → large-v3) with install status
    │   ├── Scan ~/.clippy/models/ for additional .bin files
    │   ├── Auto-select first installed model
    │   └── Show "Download" button for uninstalled models
    │         │
    │         ▼ (on click)
    │   WhisperModelManager.DownloadModelAsync()
    │         └── Stream from HuggingFace → progress % → refresh list
    │
    ▼
User selects models from dropdowns
    │
    ▼
Listen/Listen+Subtitle uses selected Whisper + LLM models
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

## Technical Deep Dive

### Audio Processing Algorithm (VadLoop)

The VadLoop thread reads raw PCM audio from ffmpeg's stdout in 100ms frames and classifies each frame as speech or silence using RMS energy:

```
1. Read 3200 bytes from ffmpeg stdout (100ms at 16kHz mono, 16-bit PCM = 1600 samples × 2 bytes)
2. Convert byte pairs to float samples: sample = (short)(byte[i] | byte[i+1] << 8) / 32768.0
3. Calculate frame RMS energy:
       RMS = sqrt( sum(sample²) / sampleCount )
4. Classify frame:
       RMS ≥ 0.015  →  SPEECH
       RMS < 0.015  →  SILENCE
5. State machine:
       SPEECH detected:
           - Append frame's float samples to speech buffer
           - Reset silence counter
           - If total buffered speech ≥ 15 seconds → force flush to queue (safety cap)
       SILENCE detected after speech:
           - Increment silence frame counter
           - If silence frames ≥ 6 (600ms) AND speech frames ≥ 3 (300ms):
               → Copy speech buffer into float[] and enqueue
               → Signal TranscribeLoop via AutoResetEvent
               → Reset speech buffer and counters
       SILENCE detected with no prior speech:
           - Ignored (ambient noise / quiet room)
6. Loop back to step 1
```

### Whisper Transcription Engine (TranscribeLoop)

The TranscribeLoop runs on a separate thread and processes speech segments from the VAD queue:

```
1. Wait on AutoResetEvent with 100ms timeout
2. Lock the queue and drain ALL pending float[] segments
3. If no segments → loop back to step 1
4. Merge all drained segments into a single contiguous float[] buffer
   (handles backpressure — if VAD produces faster than Whisper consumes,
    multiple utterances are merged into one inference call)
5. Create WhisperProcessor with current model:
       - Language: "en"
       - Threads: Environment.ProcessorCount / 2
       - SamplingStrategy: GreedySamplingStrategy()
       - WithNoContext() — no cross-chunk context reuse
       - WithSingleSegment() — return result immediately
6. Run processor.ProcessAsync(mergedBuffer)
7. Concatenate all returned segment texts
8. Dispatch to UI thread: update SubtitleOverlayWindow with rolling 3-line display
9. Run question detection on the transcribed text
10. If question detected → invoke OnQuestionDetected callback
11. Loop back to step 1
```

### Question Detection Algorithm

Two-tier `HashSet<string>` matching with `StringComparer.OrdinalIgnoreCase` for O(1) lookups:

```
Input: transcribed text string

Step 1 — Punctuation check:
    If text contains '?' → return TRUE

Step 2 — Normalize:
    Split text into words on whitespace

Step 3 — Start-word check:
    If words[0] is in StartWordsSet → return TRUE
    StartWordsSet = { what, why, how, when, where, who, which,
                      is, are, do, does, can, could, would, should,
                      will, shall, has, have, did, was, were,
                      explain, explains, describe, define, tell }

Step 4 — Keyword-anywhere check:
    For each word in text:
        If word is in KeywordsSet → return TRUE
    KeywordsSet = { explain, explains, example, using, difference,
                    how, what, compare, between, meaning, tell, me }

Step 5 — No match:
    return FALSE
```

### LLM Request Lifecycle

```
1. Cancel any in-flight request:
       - Dispose previous CancellationTokenSource
       - Create new linked CancellationTokenSource
2. Build request payload:
       {
         "model": selectedModel.Name,
         "messages": [
           { "role": "system", "content": contextText },           // only if context provided
           { "role": "user",   "content": "Answer this question concisely in 1-2 sentences:\n\n{question}" }
         ]
       }
3. POST to {selectedModel.BaseUrl}/v1/chat/completions
       - Content-Type: application/json
       - Timeout: 120 seconds
4. Parse JSON response:
       Extract choices[0].message.content
5. Return answer string to caller
6. Caller updates AnswerOverlayWindow via Dispatcher.UIThread
```

### Concurrency Model

Clippy uses a multi-threaded architecture with explicit synchronization:

| Thread | Role | Synchronization |
|--------|------|-----------------|
| **UI Thread** | Avalonia dispatcher, all UI updates | `Dispatcher.UIThread.InvokeAsync()` |
| **VadLoop Thread** | Reads ffmpeg stdout, runs VAD, enqueues speech | `lock(_queueLock)` on shared `Queue<float[]>` |
| **TranscribeLoop Thread** | Dequeues speech, runs Whisper, detects questions | `lock(_queueLock)` + `AutoResetEvent` signaling |
| **Task Pool** | LLM requests (`AskAsync`), model downloads | `CancellationTokenSource` for request cancellation |

**Key synchronization primitives:**
- `lock(_queueLock)` — Protects `Queue<float[]>` between VadLoop (producer) and TranscribeLoop (consumer)
- `AutoResetEvent` — Signals TranscribeLoop when new speech data is available, with 100ms poll fallback
- `CancellationTokenSource` — Linked tokens allow cancelling a previous LLM request when a new question arrives
- `Dispatcher.UIThread.InvokeAsync()` — Marshals all UI updates (subtitle text, answer text, status labels) from background threads to the Avalonia UI thread

### ffmpeg Command Parameters

**Live streaming (Listen+Subtitle):**
```bash
ffmpeg -f avfoundation -i ":default" -ar 16000 -ac 1 -f s16le -probesize 32 -analyzeduration 0 pipe:1
```

| Flag | Purpose |
|------|---------|
| `-f avfoundation` | macOS audio/video capture framework |
| `-i ":default"` | Default microphone (colon prefix = audio-only) |
| `-ar 16000` | 16 kHz sample rate (required by Whisper) |
| `-ac 1` | Mono channel |
| `-f s16le` | Raw 16-bit signed little-endian PCM output |
| `-probesize 32` | Minimal probe size — eliminates startup delay |
| `-analyzeduration 0` | Skip stream analysis — zero-delay start |
| `pipe:1` | Stream raw PCM to stdout (read by VadLoop) |

**Single recording (Listen):**
```bash
ffmpeg -f avfoundation -i ":default" -ar 16000 -ac 1 {outputPath}.wav
```

Records to a WAV file. On stop, the ffmpeg process is killed and the WAV is passed to Whisper for batch transcription.

### Error Handling & Reliability

| Scenario | Handling |
|----------|----------|
| LLM backend unreachable | Graceful fallback — red/orange status dot, empty model list, no crash |
| Both backends down | Red dot + "Error" label; user clicks Refresh after starting a backend |
| Whisper model not installed | Download button shown; transcription buttons disabled until model selected |
| ffmpeg not installed | Status label shows error; recording/subtitling fail gracefully |
| LLM inference timeout | 120-second timeout; request cancelled on new question |
| Model download interrupted | Temporary `.downloading` file is left (not renamed); can retry |
| Long utterance (>15s) | Safety cap forces flush to prevent unbounded memory growth |
| Transcription backpressure | Queue drain merges all pending segments into one Whisper call |
| Concurrent questions | Previous in-flight LLM request cancelled via CancellationToken |
| UI updates from background | All UI mutations dispatched to Avalonia UI thread |

### Known Limitations

- **macOS only** — Depends on `screencapture`, `avfoundation`, and macOS `kill` command
- **Local LLM only** — No cloud API support; requires Ollama or LlamaBarn running locally
- **English-optimized** — Whisper language is hardcoded to `"en"`; VAD and question detection are English-only
- **Single microphone** — Always uses the system default microphone (`:default`)
- **No persistent settings** — Model selections, window positions, and font sizes reset on restart
- **Memory usage** — Large Whisper models (Medium: 1.5 GB, Large: 2.9 GB) require significant RAM
- **No GPU acceleration** — Whisper.net runs CPU-only inference; large models may be slow on older hardware
- **Question detection heuristics** — Pattern-based detection may miss complex questions or trigger on non-questions; the manual Ask button compensates for this

## Performance Optimizations

The live transcription pipeline is tuned for low-latency real-time use:

| Optimization | Detail |
|---|---|
| **Voice Activity Detection** | Audio is transcribed on speech/silence boundaries instead of fixed intervals — Whisper only runs when someone actually spoke |
| **Selectable Whisper model** | Choose between speed (Tiny) and accuracy (Large v3) based on your needs |
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
| say | macOS text-to-speech for reading answers aloud | Pre-installed on macOS |
| Whisper model (ggml) | Speech recognition model (selectable in-app) | In-app download or [HuggingFace](https://huggingface.co/ggerganov/whisper.cpp/tree/main) |
