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

    private static readonly string ModelPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clippy", "models", "ggml-base.en.bin");

    private Process? _ffmpegProcess;
    private string? _currentRecordingPath;
    private bool _isRecording;

    private SubtitleOverlayWindow? _subtitleOverlay;
    private AnswerOverlayWindow? _answerOverlay;
    private LiveTranscriptionService? _liveTranscription;
    private LlmService? _llmService;
    private bool _isSubtitling;

    private readonly LlmService _llmManager = new();
    private List<LlmModel> _availableModels = new();

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnWindowOpened;
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
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

    private void OnHideClicked(object? sender, RoutedEventArgs e)
    {
        HideToTrayRequested?.Invoke();
    }

    private async void OnClipItClicked(object? sender, RoutedEventArgs e)
    {
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
        if (!File.Exists(ModelPath))
        {
            SetStatus("Whisper model not found. Downloading may still be in progress...");
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
            var text = await Task.Run(() => TranscribeAudio(recordingPath));

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

    private static async Task<string> TranscribeAudio(string wavPath)
    {
        using var factory = WhisperFactory.FromPath(ModelPath);
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
        if (!File.Exists(ModelPath))
        {
            SetStatus("Whisper model not found. Downloading may still be in progress...");
            return;
        }

        var selected = GetSelectedModel();
        if (selected == null)
        {
            SetStatus("No model selected. Please select a model from the dropdown first.");
            return;
        }

        _subtitleOverlay = new SubtitleOverlayWindow();
        _subtitleOverlay.OnAskRequested = OnQuestionDetected;
        _subtitleOverlay.Show();
        _subtitleOverlay.PositionAtBottomCenter();

        _answerOverlay = new AnswerOverlayWindow();
        _answerOverlay.Show();
        _answerOverlay.PositionBelowSubtitle(_subtitleOverlay);

        _llmService = new LlmService { SelectedModel = selected };

        _liveTranscription = new LiveTranscriptionService(
            ModelPath,
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
        _answerOverlay?.UpdateAnswer($"Q: {question}\nThinking...");

        _ = Task.Run(async () =>
        {
            if (_llmService == null) return;

            try
            {
                var answer = await _llmService.AskAsync(question);
                if (!string.IsNullOrWhiteSpace(answer))
                {
                    _answerOverlay?.UpdateAnswer($"Q: {question}\nA: {answer}");
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

    private void SetStatus(string message)
    {
        Dispatcher.UIThread.Post(() => StatusText.Text = message);
    }
}
