using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Clippy;

public record WhisperModelInfo(string Name, string FileName, long SizeMb, bool IsInstalled, string FilePath)
{
    public string DisplayName => IsInstalled
        ? $"{Name} ({SizeMb} MB) [installed]"
        : $"{Name} ({SizeMb} MB)";

    public override string ToString() => DisplayName;
}

public class WhisperModelManager
{
    private static readonly string ModelsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clippy", "models");

    private const string BaseDownloadUrl =
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main";

    private static readonly (string Name, string FileName, long SizeMb)[] KnownModels =
    {
        ("Tiny (English)",    "ggml-tiny.en.bin",   75),
        ("Tiny",              "ggml-tiny.bin",      75),
        ("Base (English)",    "ggml-base.en.bin",  142),
        ("Base",              "ggml-base.bin",     142),
        ("Small (English)",   "ggml-small.en.bin", 466),
        ("Small",             "ggml-small.bin",    466),
        ("Medium (English)",  "ggml-medium.en.bin", 1500),
        ("Medium",            "ggml-medium.bin",   1500),
        ("Large v3",          "ggml-large-v3.bin", 2900),
    };

    public List<WhisperModelInfo> GetAvailableModels()
    {
        Directory.CreateDirectory(ModelsDir);

        var models = new List<WhisperModelInfo>();

        foreach (var (name, fileName, sizeMb) in KnownModels)
        {
            var filePath = Path.Combine(ModelsDir, fileName);
            var installed = File.Exists(filePath);
            models.Add(new WhisperModelInfo(name, fileName, sizeMb, installed, filePath));
        }

        // Also pick up any other .bin files in the directory that aren't in KnownModels
        var knownFiles = new HashSet<string>(KnownModels.Select(m => m.FileName), StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(ModelsDir, "ggml-*.bin"))
        {
            var fileName = Path.GetFileName(file);
            if (!knownFiles.Contains(fileName))
            {
                var fileInfo = new FileInfo(file);
                var sizeMb = fileInfo.Length / (1024 * 1024);
                models.Add(new WhisperModelInfo(fileName, fileName, sizeMb, true, file));
            }
        }

        return models;
    }

    public WhisperModelInfo? GetCurrentModel()
    {
        var models = GetAvailableModels();
        return models.FirstOrDefault(m => m.IsInstalled);
    }

    public async Task DownloadModelAsync(WhisperModelInfo model, Action<int>? onProgress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(ModelsDir);

        var url = $"{BaseDownloadUrl}/{model.FileName}";
        var destPath = Path.Combine(ModelsDir, model.FileName);
        var tempPath = destPath + ".downloading";

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(30);

        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

        var buffer = new byte[81920];
        long totalRead = 0;
        int lastReportedPercent = -1;

        while (true)
        {
            var read = await contentStream.ReadAsync(buffer, ct);
            if (read == 0) break;

            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            totalRead += read;

            if (totalBytes > 0)
            {
                var percent = (int)(totalRead * 100 / totalBytes);
                if (percent != lastReportedPercent)
                {
                    lastReportedPercent = percent;
                    onProgress?.Invoke(percent);
                }
            }
        }

        fileStream.Close();

        // Rename temp file to final name
        if (File.Exists(destPath))
            File.Delete(destPath);
        File.Move(tempPath, destPath);
    }
}
