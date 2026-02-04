using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;

namespace Clippy;

public class LiveTranscriptionService : IDisposable
{
    private static readonly HashSet<string> QuestionStartWords = new(StringComparer.OrdinalIgnoreCase)
        { "what", "why", "how", "when", "where", "who", "which",
          "is", "are", "do", "does", "can", "could", "would",
          "should", "will", "shall", "has", "have", "did", "was", "were",
          "explain", "explains", "describe", "define" };

    private static readonly HashSet<string> QuestionKeywords = new(StringComparer.OrdinalIgnoreCase)
        { "explain", "explains", "example", "using", "difference",
          "how", "what", "compare", "between", "meaning" };

    private readonly string _modelPath;
    private readonly Action<string> _onTextUpdated;
    private readonly Action<string>? _onQuestionDetected;
    private readonly int _chunkIntervalMs;

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private Process? _ffmpegProcess;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private Task? _transcribeTask;

    private readonly List<string> _recentLines = new();
    private const int MaxRecentLines = 3;

    public LiveTranscriptionService(
        string modelPath,
        Action<string> onTextUpdated,
        Action<string>? onQuestionDetected = null,
        int chunkIntervalMs = 1500)
    {
        _modelPath = modelPath;
        _onTextUpdated = onTextUpdated;
        _onQuestionDetected = onQuestionDetected;
        _chunkIntervalMs = chunkIntervalMs;
    }

    public void Start()
    {
        if (!File.Exists(_modelPath))
            throw new FileNotFoundException("Whisper model not found.", _modelPath);

        _cts = new CancellationTokenSource();

        _factory = WhisperFactory.FromPath(_modelPath);
        _processor = _factory.CreateBuilder()
            .WithLanguage("en")
            .WithThreads(Math.Max(1, Environment.ProcessorCount / 2))
            .WithNoContext()
            .WithSingleSegment()
            .WithGreedySamplingStrategy()
            .ParentBuilder
            .Build();

        _ffmpegProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = "-f avfoundation -i \":default\" -ar 16000 -ac 1 -f s16le -acodec pcm_s16le -probesize 32 -analyzeduration 0 pipe:1",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        if (_ffmpegProcess == null)
            throw new InvalidOperationException("Failed to start ffmpeg.");

        var pcmQueue = new Queue<float[]>();
        var queueLock = new object();
        var dataAvailable = new AutoResetEvent(false);

        _readTask = Task.Run(() => ReadAudioLoop(
            _ffmpegProcess.StandardOutput.BaseStream,
            pcmQueue, queueLock, dataAvailable,
            _cts.Token));

        _transcribeTask = Task.Run(() => TranscribeLoop(
            pcmQueue, queueLock, dataAvailable,
            _cts.Token));
    }

    private void ReadAudioLoop(
        Stream stdout,
        Queue<float[]> pcmQueue,
        object queueLock,
        AutoResetEvent dataAvailable,
        CancellationToken ct)
    {
        var chunkSeconds = _chunkIntervalMs / 1000.0;
        var bytesPerChunk = (int)(16000 * 2 * chunkSeconds);
        var buffer = new byte[bytesPerChunk];

        while (!ct.IsCancellationRequested)
        {
            int totalRead = 0;
            while (totalRead < bytesPerChunk && !ct.IsCancellationRequested)
            {
                int read = stdout.Read(buffer, totalRead, bytesPerChunk - totalRead);
                if (read == 0) return;
                totalRead += read;
            }

            if (ct.IsCancellationRequested) break;

            var sampleCount = totalRead / 2;
            var samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = BitConverter.ToInt16(buffer, i * 2);
                samples[i] = sample / 32768.0f;
            }

            lock (queueLock)
            {
                pcmQueue.Enqueue(samples);
            }
            dataAvailable.Set();
        }
    }

    private async Task TranscribeLoop(
        Queue<float[]> pcmQueue,
        object queueLock,
        AutoResetEvent dataAvailable,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            dataAvailable.WaitOne(TimeSpan.FromMilliseconds(100));

            float[]? samples = null;
            lock (queueLock)
            {
                if (pcmQueue.Count == 0) continue;

                // Drain the queue: merge all pending chunks to avoid falling behind
                if (pcmQueue.Count == 1)
                {
                    samples = pcmQueue.Dequeue();
                }
                else
                {
                    var totalLength = 0;
                    foreach (var chunk in pcmQueue)
                        totalLength += chunk.Length;

                    samples = new float[totalLength];
                    var offset = 0;
                    while (pcmQueue.Count > 0)
                    {
                        var chunk = pcmQueue.Dequeue();
                        Array.Copy(chunk, 0, samples, offset, chunk.Length);
                        offset += chunk.Length;
                    }
                }
            }

            if (samples == null || _processor == null) continue;

            try
            {
                var sb = new StringBuilder();
                await foreach (var segment in _processor.ProcessAsync(
                    new ReadOnlyMemory<float>(samples), ct))
                {
                    sb.Append(segment.Text);
                }

                var text = sb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    _recentLines.Add(text);
                    if (_recentLines.Count > MaxRecentLines)
                        _recentLines.RemoveAt(0);

                    var displayText = string.Join("\n", _recentLines);
                    _onTextUpdated(displayText);

                    if (_onQuestionDetected != null && IsQuestion(text))
                    {
                        _onQuestionDetected(text);
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _onTextUpdated($"[Error: {ex.Message}]");
            }
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

        if (_readTask != null)
            try { await _readTask; } catch { }
        if (_transcribeTask != null)
            try { await _transcribeTask; } catch { }
    }

    private static bool IsQuestion(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Contains('?'))
            return true;

        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return false;

        // Check if the sentence starts with a question/command word
        if (QuestionStartWords.Contains(words[0]))
            return true;

        // Check if any keyword appears anywhere in the text
        foreach (var word in words)
        {
            if (QuestionKeywords.Contains(word))
                return true;
        }

        return false;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _processor?.Dispose();
        _factory?.Dispose();
        _ffmpegProcess?.Dispose();
        _cts?.Dispose();
    }
}
