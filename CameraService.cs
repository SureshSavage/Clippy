using System.Diagnostics;
using System.IO;

namespace Clippy;

public class CameraService : IDisposable
{
    private readonly LlmService _llmService;
    private readonly Action<string> _onDescriptionReady;
    private readonly Action<byte[]>? _onFrameCaptured;
    private readonly int _intervalSeconds;

    private Process? _ffmpegProcess;
    private CancellationTokenSource? _cts;
    private Task? _analysisLoop;
    private string? _tempDir;
    private int _lastFrameIndex;
    private bool _disposed;

    public CameraService(
        LlmService llmService,
        Action<string> onDescriptionReady,
        Action<byte[]>? onFrameCaptured = null,
        int intervalSeconds = 5)
    {
        _llmService = llmService;
        _onDescriptionReady = onDescriptionReady;
        _onFrameCaptured = onFrameCaptured;
        _intervalSeconds = intervalSeconds;
    }

    public void Start(bool continuous = true)
    {
        _cts = new CancellationTokenSource();

        if (!continuous)
        {
            // Snap-only mode: no ffmpeg process, user clicks Snap manually
            _onDescriptionReady("Snap-only mode. Click Snap to capture and analyze a frame.");
            return;
        }

        _tempDir = Path.Combine(Path.GetTempPath(), $"clippy_camera_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _lastFrameIndex = 0;

        // Capture one frame every N seconds from the default camera
        // -f avfoundation -i "0" = default video device on macOS
        // -vf fps=1/N = one frame every N seconds
        // -q:v 2 = good JPEG quality
        _ffmpegProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-f avfoundation -framerate 30 -i \"0\" -vf fps=1/{_intervalSeconds} -q:v 2 \"{Path.Combine(_tempDir, "frame_%04d.jpg")}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        });

        if (_ffmpegProcess == null)
            throw new InvalidOperationException("Failed to start ffmpeg for camera capture.");

        // Discard ffmpeg stderr to prevent buffer blocking
        _ffmpegProcess.ErrorDataReceived += (_, _) => { };
        _ffmpegProcess.BeginErrorReadLine();

        _analysisLoop = Task.Run(() => AnalysisLoopAsync(_cts.Token));
    }

    private async Task AnalysisLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, ct);

                var nextIndex = _lastFrameIndex + 1;
                var framePath = Path.Combine(_tempDir!, $"frame_{nextIndex:D4}.jpg");

                if (!File.Exists(framePath))
                    continue;

                // Wait briefly for ffmpeg to finish writing the file
                await Task.Delay(200, ct);

                byte[] imageBytes;
                try
                {
                    imageBytes = await File.ReadAllBytesAsync(framePath, ct);
                }
                catch (IOException)
                {
                    continue; // File still being written
                }

                if (imageBytes.Length < 100)
                    continue; // Too small, likely incomplete

                _lastFrameIndex = nextIndex;

                _onFrameCaptured?.Invoke(imageBytes);
                _onDescriptionReady("Analyzing frame...");

                var description = await _llmService.AskVisionAsync(
                    imageBytes,
                    "Describe what you see in this image concisely in 2-3 sentences.",
                    ct);

                if (!string.IsNullOrWhiteSpace(description))
                    _onDescriptionReady(description);
                else
                    _onDescriptionReady("(No description returned)");

                // Clean up old frames to save disk space
                CleanupOldFrames(nextIndex);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _onDescriptionReady($"Error: {ex.Message}");
                try { await Task.Delay(2000, ct); } catch { break; }
            }
        }
    }

    public async Task<(string description, byte[]? imageBytes)> SnapAsync(CancellationToken ct = default)
    {
        var snapDir = Path.Combine(Path.GetTempPath(), "clippy_snap");
        Directory.CreateDirectory(snapDir);
        var snapPath = Path.Combine(snapDir, $"snap_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");

        // Capture a single frame from the camera
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-f avfoundation -framerate 30 -i \"0\" -frames:v 1 -q:v 2 -y \"{snapPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        });

        if (process == null)
            return ("Failed to capture frame.", null);

        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        if (!File.Exists(snapPath))
            return ("Capture failed — no frame saved.", null);

        var imageBytes = await File.ReadAllBytesAsync(snapPath, ct);

        if (imageBytes.Length < 100)
            return ("Captured frame too small.", null);

        var description = await _llmService.AskVisionAsync(
            imageBytes,
            "Describe what you see in this image in detail.",
            ct);

        // Clean up snap file
        try { File.Delete(snapPath); } catch { }

        return (string.IsNullOrWhiteSpace(description)
            ? "(No description returned)"
            : description, imageBytes);
    }

    private void CleanupOldFrames(int currentIndex)
    {
        // Keep only the last 2 frames
        for (int i = 1; i < currentIndex - 1; i++)
        {
            var path = Path.Combine(_tempDir!, $"frame_{i:D4}.jpg");
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();

        if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
        {
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

        if (_analysisLoop != null)
        {
            try { await _analysisLoop; } catch { }
        }

        // Clean up temp directory
        if (_tempDir != null)
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        _cts?.Dispose();

        if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
        {
            try { _ffmpegProcess.Kill(); } catch { }
        }
        _ffmpegProcess = null;

        if (_tempDir != null)
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
