using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Whisper.net;

namespace Clippy;

public partial class MainWindow : Window
{
    public event Action? HideToTrayRequested;

    private static readonly string ScreenshotDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Clippy_Screenshots");

    private static readonly string OutputDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Clippy_Transcripts");

    private static readonly string DefaultModelPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clippy", "models", "ggml-base.en.bin");

    private Process? _ffmpegProcess;
    private string? _currentRecordingPath;
    private bool _isRecording;

    private SubtitleOverlayWindow? _subtitleOverlay;
    private AnswerOverlayWindow? _answerOverlay;
    private LiveTranscriptionService? _liveTranscription;
    private LlmService? _llmService;
    private bool _isSubtitling;

    private CameraOverlayWindow? _cameraOverlay;
    private CameraService? _cameraService;
    private LlmService? _cameraLlmService;
    private bool _isCameraActive;

    private readonly LlmService _llmManager = new();
    private List<LlmModel> _availableModels = new();

    private readonly WhisperModelManager _whisperModelManager = new();
    private List<WhisperModelInfo> _whisperModels = new();
    private CancellationTokenSource? _downloadCts;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnWindowOpened;
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        LoadWhisperModels();
        await LoadModelsAsync();
    }

    private async Task LoadModelsAsync()
    {
        ConnectionDot.Background = new SolidColorBrush(Colors.Gray);
        ConnectionLabel.Text = "Checking...";
        ConnectionLabel.Foreground = new SolidColorBrush(Colors.Gray);
        RefreshButton.IsEnabled = false;
        ModelDropdown.ItemsSource = null;
        _availableModels.Clear();

        try
        {
            var models = await Task.Run(() => _llmManager.ListAllModelsAsync());
            _availableModels = models;

            if (models.Count == 0)
            {
                ConnectionDot.Background = new SolidColorBrush(Colors.Orange);
                ConnectionLabel.Text = "No models";
                ConnectionLabel.Foreground = new SolidColorBrush(Colors.Orange);
                SetStatus("No backends found. Start Ollama (port 11434) or LlamaBarn (port 2276).");
            }
            else
            {
                // Group backends for status label
                var backends = new HashSet<string>();
                foreach (var m in models)
                    backends.Add(m.Backend);
                var backendList = string.Join(" + ", backends);

                ConnectionDot.Background = new SolidColorBrush(Colors.LimeGreen);
                ConnectionLabel.Text = $"{backendList} ({models.Count})";
                ConnectionLabel.Foreground = new SolidColorBrush(Colors.LimeGreen);

                ModelDropdown.ItemsSource = models;
                ModelDropdown.SelectedIndex = 0;

                SetStatus($"Loaded {models.Count} model(s) from {backendList}.");
            }
        }
        catch (Exception ex)
        {
            ConnectionDot.Background = new SolidColorBrush(Colors.Red);
            ConnectionLabel.Text = "Error";
            ConnectionLabel.Foreground = new SolidColorBrush(Colors.Red);
            SetStatus($"Error loading models: {ex.Message}");
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void OnModelSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Selection tracked via _availableModels + SelectedIndex
    }

    private LlmModel? GetSelectedModel()
    {
        if (ModelDropdown.SelectedItem is LlmModel model)
            return model;
        return null;
    }

    private async void OnRefreshClicked(object? sender, RoutedEventArgs e)
    {
        await LoadModelsAsync();
    }

    private void LoadWhisperModels()
    {
        _whisperModels = _whisperModelManager.GetAvailableModels();
        WhisperModelDropdown.ItemsSource = _whisperModels;

        // Select the first installed model, or the first one overall
        var installed = _whisperModels.FindIndex(m => m.IsInstalled);
        WhisperModelDropdown.SelectedIndex = installed >= 0 ? installed : (_whisperModels.Count > 0 ? 0 : -1);
    }

    private void OnWhisperModelSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (WhisperModelDropdown.SelectedItem is WhisperModelInfo model)
        {
            DownloadWhisperButton.IsEnabled = !model.IsInstalled;
            DownloadWhisperButton.Content = model.IsInstalled ? "Installed" : "Download";
        }
    }

    private async void OnDownloadWhisperClicked(object? sender, RoutedEventArgs e)
    {
        if (WhisperModelDropdown.SelectedItem is not WhisperModelInfo model)
            return;

        if (model.IsInstalled)
        {
            SetStatus("Model already installed.");
            return;
        }

        DownloadWhisperButton.IsEnabled = false;
        DownloadWhisperButton.Content = "0%";
        SetStatus($"Downloading {model.Name}...");

        _downloadCts = new CancellationTokenSource();

        try
        {
            await _whisperModelManager.DownloadModelAsync(
                model,
                percent => Dispatcher.UIThread.Post(() =>
                {
                    DownloadWhisperButton.Content = $"{percent}%";
                }),
                _downloadCts.Token);

            SetStatus($"Downloaded {model.Name} successfully.");
            LoadWhisperModels();
        }
        catch (OperationCanceledException)
        {
            SetStatus("Download cancelled.");
        }
        catch (Exception ex)
        {
            SetStatus($"Download failed: {ex.Message}");
        }
        finally
        {
            DownloadWhisperButton.IsEnabled = true;
            DownloadWhisperButton.Content = "Download";
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    private string GetSelectedWhisperModelPath()
    {
        if (WhisperModelDropdown.SelectedItem is WhisperModelInfo model && model.IsInstalled)
            return model.FilePath;
        return DefaultModelPath;
    }

    private void OnHideClicked(object? sender, RoutedEventArgs e)
    {
        HideToTrayRequested?.Invoke();
    }

    private async void OnClipItClicked(object? sender, RoutedEventArgs e)
    {
        var selected = GetSelectedModel();
        if (selected == null)
        {
            SetStatus("No model selected. Please select a vision model first.");
            return;
        }

        Hide();
        await Task.Delay(300);

        Directory.CreateDirectory(ScreenshotDir);
        var fileName = $"clip_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        var filePath = Path.Combine(ScreenshotDir, fileName);

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "screencapture",
            Arguments = $"-x \"{filePath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (process != null)
            await process.WaitForExitAsync();

        Show();
        Activate();

        if (!File.Exists(filePath))
        {
            SetStatus("Screenshot failed.");
            return;
        }

        var imageBytes = await File.ReadAllBytesAsync(filePath);
        if (imageBytes.Length < 100)
        {
            SetStatus("Screenshot too small.");
            return;
        }

        // Show overlay with preview immediately
        var overlay = new ScreenshotOverlayWindow();
        overlay.SetModelLabel(selected.DisplayName);
        overlay.SetPreview(imageBytes);
        overlay.Show();
        overlay.PositionAtCenter();

        SetStatus("Analyzing screenshot...");

        // Send to vision model in background
        _ = Task.Run(async () =>
        {
            using var llm = new LlmService { SelectedModel = selected };
            try
            {
                var description = await llm.AskVisionAsync(
                    imageBytes,
                    "Describe what you see in this screenshot in detail.");

                overlay.UpdateDescription(
                    string.IsNullOrWhiteSpace(description)
                        ? "(No description returned)"
                        : description);

                Dispatcher.UIThread.Post(() => SetStatus("Screenshot analyzed."));
            }
            catch (Exception ex)
            {
                overlay.UpdateDescription($"Error: {ex.Message}");
                Dispatcher.UIThread.Post(() => SetStatus($"Analysis error: {ex.Message}"));
            }
        });
    }

    private async void OnListenClicked(object? sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            await StopRecordingAndTranscribe();
        }
        else
        {
            StartRecording();
        }
    }

    private void StartRecording()
    {
        var whisperPath = GetSelectedWhisperModelPath();
        if (!File.Exists(whisperPath))
        {
            SetStatus("Whisper model not found. Select an installed model or download one.");
            return;
        }

        Directory.CreateDirectory(OutputDir);
        _currentRecordingPath = Path.Combine(OutputDir, $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.wav");

        // Record from default microphone using ffmpeg
        // -f avfoundation -i ":default" captures default audio input
        // -ar 16000 -ac 1 = 16kHz mono (required by Whisper)
        // -y = overwrite output
        _ffmpegProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-f avfoundation -i \":default\" -ar 16000 -ac 1 -y \"{_currentRecordingPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        });

        if (_ffmpegProcess != null)
        {
            _isRecording = true;
            ListenButton.Content = "Stop";
            SetStatus("Recording... Click Stop when done.");
        }
    }

    private async Task StopRecordingAndTranscribe()
    {
        ListenButton.IsEnabled = false;

        // Send 'q' to ffmpeg to stop gracefully
        if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
        {
            // Kill the process (ffmpeg stops on SIGINT/SIGTERM)
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "kill",
                    Arguments = $"-INT {_ffmpegProcess.Id}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                await _ffmpegProcess.WaitForExitAsync();
            }
            catch
            {
                try { _ffmpegProcess.Kill(); } catch { }
            }
        }

        _isRecording = false;
        _ffmpegProcess = null;
        ListenButton.Content = "Listen";

        if (_currentRecordingPath == null || !File.Exists(_currentRecordingPath))
        {
            SetStatus("Recording failed.");
            ListenButton.IsEnabled = true;
            return;
        }

        SetStatus("Transcribing audio...");

        var recordingPath = _currentRecordingPath;
        var transcriptPath = Path.ChangeExtension(recordingPath, ".txt");

        try
        {
            var modelPath = GetSelectedWhisperModelPath();
            var text = await Task.Run(() => TranscribeAudio(recordingPath, modelPath));

            if (!string.IsNullOrWhiteSpace(text))
            {
                await File.WriteAllTextAsync(transcriptPath, text);
                SetStatus($"Saved: {Path.GetFileName(transcriptPath)}");
            }
            else
            {
                SetStatus("No speech detected in recording.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Transcription error: {ex.Message}");
        }

        ListenButton.IsEnabled = true;
    }

    private static async Task<string> TranscribeAudio(string wavPath, string modelPath)
    {
        using var factory = WhisperFactory.FromPath(modelPath);
        using var processor = factory.CreateBuilder().WithLanguage("en").Build();

        using var fileStream = File.OpenRead(wavPath);

        var result = new System.Text.StringBuilder();
        await foreach (var segment in processor.ProcessAsync(fileStream))
        {
            result.Append(segment.Text);
        }

        return result.ToString().Trim();
    }

    private async void OnSubtitleClicked(object? sender, RoutedEventArgs e)
    {
        if (_isSubtitling)
        {
            await StopSubtitling();
        }
        else
        {
            StartSubtitling();
        }
    }

    private void StartSubtitling()
    {
        var whisperPath = GetSelectedWhisperModelPath();
        if (!File.Exists(whisperPath))
        {
            SetStatus("Whisper model not found. Select an installed model or download one.");
            return;
        }

        var selected = GetSelectedModel();
        if (selected == null)
        {
            SetStatus("No LLM model selected. Please select a model from the dropdown first.");
            return;
        }

        _subtitleOverlay = new SubtitleOverlayWindow();
        _subtitleOverlay.OnAskRequested = OnQuestionDetected;
        _subtitleOverlay.OnCloseRequested = () => _ = StopSubtitling();

        var whisperModelName = WhisperModelDropdown.SelectedItem is WhisperModelInfo wm
            ? wm.Name
            : Path.GetFileNameWithoutExtension(whisperPath);
        _subtitleOverlay.SetModelLabel(whisperModelName);

        _subtitleOverlay.Show();
        _subtitleOverlay.PositionAtBottomCenter();

        _answerOverlay = new AnswerOverlayWindow();
        _answerOverlay.OnCloseRequested = () => _ = StopSubtitling();
        _answerOverlay.SetModelLabel(selected.DisplayName);
        _answerOverlay.Show();
        _answerOverlay.PositionBelowSubtitle(_subtitleOverlay);

        _llmService = new LlmService { SelectedModel = selected };

        _liveTranscription = new LiveTranscriptionService(
            whisperPath,
            text => _subtitleOverlay.UpdateSubtitle(text),
            onQuestionDetected: OnQuestionDetected,
            chunkIntervalMs: 1500
        );

        try
        {
            _liveTranscription.Start();
            _isSubtitling = true;
            SubtitleButton.Content = "Stop Subtitle";
            SetStatus("Live subtitling active...");
        }
        catch (Exception ex)
        {
            _subtitleOverlay.Close();
            _subtitleOverlay = null;
            _answerOverlay?.Close();
            _answerOverlay = null;
            _liveTranscription?.Dispose();
            _liveTranscription = null;
            _llmService?.Dispose();
            _llmService = null;
            SetStatus($"Failed to start subtitling: {ex.Message}");
        }
    }

    private async Task StopSubtitling()
    {
        SubtitleButton.IsEnabled = false;

        if (_liveTranscription != null)
        {
            await _liveTranscription.StopAsync();
            _liveTranscription.Dispose();
            _liveTranscription = null;
        }

        _subtitleOverlay?.Close();
        _subtitleOverlay = null;

        _answerOverlay?.StopSpeaking();
        _answerOverlay?.Close();
        _answerOverlay = null;

        _llmService?.Dispose();
        _llmService = null;

        _isSubtitling = false;
        SubtitleButton.Content = "Listen+Subtitle";
        SubtitleButton.IsEnabled = true;
        SetStatus("Subtitling stopped.");
    }

    private void OnQuestionDetected(string question)
    {
        string? context = null;
        Dispatcher.UIThread.Invoke(() =>
        {
            _answerOverlay?.UpdateAnswer($"Q: {question}\nThinking...");
            context = ContextTextBox.Text?.Trim();
        });

        _ = Task.Run(async () =>
        {
            if (_llmService == null) return;

            try
            {
                var answer = await _llmService.AskAsync(question, context);
                if (!string.IsNullOrWhiteSpace(answer))
                {
                    var displayText = $"Q: {question}\nA: {answer}";
                    _answerOverlay?.UpdateAnswer(displayText);

                    var readAloud = false;
                    Dispatcher.UIThread.Invoke(() => readAloud = ReadAloudCheckBox.IsChecked == true);
                    if (readAloud)
                        _answerOverlay?.SpeakAnswer(displayText);
                }
                else
                {
                    _answerOverlay?.UpdateAnswer($"Q: {question}\nNo answer received.");
                }
            }
            catch (OperationCanceledException)
            {
                // Previous request was canceled by a newer question — ignore
            }
            catch (Exception ex)
            {
                _answerOverlay?.UpdateAnswer($"Q: {question}\nError: {ex.Message}");
            }
        });
    }

    private async void OnCameraClicked(object? sender, RoutedEventArgs e)
    {
        if (_isCameraActive)
        {
            await StopCamera();
        }
        else
        {
            StartCamera();
        }
    }

    private void StartCamera()
    {
        var selected = GetSelectedModel();
        if (selected == null)
        {
            SetStatus("No LLM model selected. Please select a vision model from the dropdown first.");
            return;
        }

        _cameraLlmService = new LlmService { SelectedModel = selected };

        _cameraOverlay = new CameraOverlayWindow();
        _cameraOverlay.OnCloseRequested = () => _ = StopCamera();
        _cameraOverlay.SetModelLabel(selected.DisplayName);
        _cameraOverlay.Show();
        _cameraOverlay.PositionAtCenter();

        _cameraService = new CameraService(
            _cameraLlmService,
            description => _cameraOverlay.UpdateDescription(description),
            onFrameCaptured: imageBytes => _cameraOverlay.UpdatePreview(imageBytes),
            intervalSeconds: 5
        );

        _cameraOverlay.OnSnapRequested = () => _cameraService.SnapAsync();

        var continuous = ContinuousCameraCheckBox.IsChecked == true;

        try
        {
            _cameraService.Start(continuous);
            _isCameraActive = true;
            CameraButton.Content = "Stop Camera";
            SetStatus(continuous ? "Camera analysis active (continuous)..." : "Camera ready (snap-only mode).");
        }
        catch (Exception ex)
        {
            _cameraOverlay.Close();
            _cameraOverlay = null;
            _cameraService?.Dispose();
            _cameraService = null;
            _cameraLlmService?.Dispose();
            _cameraLlmService = null;
            SetStatus($"Failed to start camera: {ex.Message}");
        }
    }

    private async Task StopCamera()
    {
        CameraButton.IsEnabled = false;

        if (_cameraService != null)
        {
            await _cameraService.StopAsync();
            _cameraService.Dispose();
            _cameraService = null;
        }

        _cameraOverlay?.StopSpeaking();
        _cameraOverlay?.Close();
        _cameraOverlay = null;

        _cameraLlmService?.Dispose();
        _cameraLlmService = null;

        _isCameraActive = false;
        CameraButton.Content = "Camera";
        CameraButton.IsEnabled = true;
        SetStatus("Camera stopped.");
    }

    private void SetStatus(string message)
    {
        Dispatcher.UIThread.Post(() => StatusText.Text = message);
    }
}
