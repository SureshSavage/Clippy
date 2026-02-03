using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
    private LiveTranscriptionService? _liveTranscription;
    private bool _isSubtitling;

    public MainWindow()
    {
        InitializeComponent();
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

        _subtitleOverlay = new SubtitleOverlayWindow();
        _subtitleOverlay.Show();
        _subtitleOverlay.PositionAtBottomCenter();

        _liveTranscription = new LiveTranscriptionService(
            ModelPath,
            text => _subtitleOverlay.UpdateSubtitle(text),
            chunkIntervalMs: 3000
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
            _liveTranscription?.Dispose();
            _liveTranscription = null;
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

        _isSubtitling = false;
        SubtitleButton.Content = "Listen+Subtitle";
        SubtitleButton.IsEnabled = true;
        SetStatus("Subtitling stopped.");
    }

    private void SetStatus(string message)
    {
        Dispatcher.UIThread.Post(() => StatusText.Text = message);
    }
}
