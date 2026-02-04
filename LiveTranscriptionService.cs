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
          "explain", "explains", "describe", "define", "tell" };

    private static readonly HashSet<string> QuestionKeywords = new(StringComparer.OrdinalIgnoreCase)
        { "explain", "explains", "example", "using", "difference",
          "how", "what", "compare", "between", "meaning", "tell", "me" };

    private readonly string _modelPath;
    private readonly Action<string> _onTextUpdated;
    private readonly Action<string>? _onQuestionDetected;

    // VAD parameters
    private const int SampleRate = 16000;
    private const int FrameSamples = 1600;       // 100ms per frame
    private const int FrameBytes = FrameSamples * 2;
    private const float SilenceThreshold = 0.015f; // RMS below this = silence
    private const int SilenceFramesToTrigger = 6;  // 600ms of silence to trigger transcription
    private const int MinSpeechFrames = 3;         // at least 300ms of speech to bother transcribing
    private const int MaxSpeechSeconds = 15;       // cap at 15s to avoid huge chunks
    private const int MaxSpeechFrames = MaxSpeechSeconds * (SampleRate / FrameSamples);

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private Process? _ffmpegProcess;
    private CancellationTokenSource? _cts;
    private Task? _vadTask;
    private Task? _transcribeTask;

    private readonly List<string> _recentLines = new();
    private const int MaxRecentLines = 3;

    public LiveTranscriptionService(
        string modelPath,
        Action<string> onTextUpdated,
        Action<string>? onQuestionDetected = null,
        int chunkIntervalMs = 1500) // kept for API compat but unused
    {
        _modelPath = modelPath;
        _onTextUpdated = onTextUpdated;
        _onQuestionDetected = onQuestionDetected;
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

        var speechQueue = new Queue<float[]>();
        var queueLock = new object();
        var dataAvailable = new AutoResetEvent(false);

        _vadTask = Task.Run(() => VadLoop(
            _ffmpegProcess.StandardOutput.BaseStream,
            speechQueue, queueLock, dataAvailable,
            _cts.Token));

        _transcribeTask = Task.Run(() => TranscribeLoop(
            speechQueue, queueLock, dataAvailable,
            _cts.Token));
    }

    private void VadLoop(
        Stream stdout,
        Queue<float[]> speechQueue,
        object queueLock,
        AutoResetEvent dataAvailable,
        CancellationToken ct)
    {
        var frameBuffer = new byte[FrameBytes];
        var speechBuffer = new List<float>();
        int silentFrames = 0;
        int speechFrames = 0;

        while (!ct.IsCancellationRequested)
        {
            // Read one 100ms frame
            int totalRead = 0;
            while (totalRead < FrameBytes && !ct.IsCancellationRequested)
            {
                int read = stdout.Read(frameBuffer, totalRead, FrameBytes - totalRead);
                if (read == 0) return;
                totalRead += read;
            }

            if (ct.IsCancellationRequested) break;

            // Convert bytes to float samples
            var frameSamples = new float[totalRead / 2];
            for (int i = 0; i < frameSamples.Length; i++)
            {
                short sample = BitConverter.ToInt16(frameBuffer, i * 2);
                frameSamples[i] = sample / 32768.0f;
            }

            // Calculate RMS energy of this frame
            float rms = CalculateRms(frameSamples);
            bool isSpeech = rms >= SilenceThreshold;

            if (isSpeech)
            {
                // Speech detected — accumulate
                speechBuffer.AddRange(frameSamples);
                speechFrames++;
                silentFrames = 0;

                // Safety cap: if speaking for too long, flush what we have
                if (speechFrames >= MaxSpeechFrames)
                {
                    FlushSpeech(speechBuffer, speechQueue, queueLock, dataAvailable);
                    speechBuffer.Clear();
                    speechFrames = 0;
                }
            }
            else
            {
                if (speechFrames > 0)
                {
                    // We were in speech, now silence
                    silentFrames++;

                    // Keep adding silence frames to capture trailing audio
                    speechBuffer.AddRange(frameSamples);

                    if (silentFrames >= SilenceFramesToTrigger)
                    {
                        // Enough silence — send speech to transcription if long enough
                        if (speechFrames >= MinSpeechFrames)
                        {
                            FlushSpeech(speechBuffer, speechQueue, queueLock, dataAvailable);
                        }

                        speechBuffer.Clear();
                        speechFrames = 0;
                        silentFrames = 0;
                    }
                }
                // else: silence with no prior speech — ignore
            }
        }

        // Flush any remaining speech on shutdown
        if (speechFrames >= MinSpeechFrames && speechBuffer.Count > 0)
        {
            FlushSpeech(speechBuffer, speechQueue, queueLock, dataAvailable);
        }
    }

    private static float CalculateRms(float[] samples)
    {
        double sum = 0;
        for (int i = 0; i < samples.Length; i++)
            sum += samples[i] * samples[i];
        return (float)Math.Sqrt(sum / samples.Length);
    }

    private static void FlushSpeech(
        List<float> speechBuffer,
        Queue<float[]> speechQueue,
        object queueLock,
        AutoResetEvent dataAvailable)
    {
        var speech = speechBuffer.ToArray();
        lock (queueLock)
        {
            speechQueue.Enqueue(speech);
        }
        dataAvailable.Set();
    }

    private async Task TranscribeLoop(
        Queue<float[]> speechQueue,
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
                if (speechQueue.Count == 0) continue;

                // Drain and merge all pending speech segments
                if (speechQueue.Count == 1)
                {
                    samples = speechQueue.Dequeue();
                }
                else
                {
                    var totalLength = 0;
                    foreach (var chunk in speechQueue)
                        totalLength += chunk.Length;

                    samples = new float[totalLength];
                    var offset = 0;
                    while (speechQueue.Count > 0)
                    {
                        var chunk = speechQueue.Dequeue();
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

        if (_vadTask != null)
            try { await _vadTask; } catch { }
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

        if (QuestionStartWords.Contains(words[0]))
            return true;

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
