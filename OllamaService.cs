using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clippy;

public record LlmModel(string Name, string Backend, string BaseUrl)
{
    public string DisplayName => $"{Name}  [{Backend}]";
    public override string ToString() => DisplayName;
}

public record BackendConfig(string Name, string BaseUrl);

public class LlmService : IDisposable
{
    private static readonly BackendConfig[] Backends =
    {
        new("Ollama", "http://localhost:11434"),
        new("LlamaBarn", "http://localhost:2276"),
    };

    private readonly HttpClient _httpClient;
    private readonly HttpClient _inferenceClient;
    private CancellationTokenSource? _currentRequest;

    public LlmModel? SelectedModel { get; set; }

    public LlmService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _inferenceClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    public async Task<List<LlmModel>> ListAllModelsAsync()
    {
        var allModels = new List<LlmModel>();

        foreach (var backend in Backends)
        {
            try
            {
                var models = await ListModelsFromBackendAsync(backend);
                allModels.AddRange(models);
            }
            catch
            {
                // Backend not reachable, skip it
            }
        }

        return allModels;
    }

    private async Task<List<LlmModel>> ListModelsFromBackendAsync(BackendConfig backend)
    {
        var models = new List<LlmModel>();

        // Try Ollama-style /api/tags first
        try
        {
            var response = await _httpClient.GetAsync($"{backend.BaseUrl}/api/tags");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("models", out var modelsArray))
                {
                    foreach (var model in modelsArray.EnumerateArray())
                    {
                        if (model.TryGetProperty("name", out var name))
                        {
                            var n = name.GetString();
                            if (!string.IsNullOrEmpty(n))
                                models.Add(new LlmModel(n, backend.Name, backend.BaseUrl));
                        }
                    }
                }

                if (models.Count > 0)
                    return models;
            }
        }
        catch { }

        // Fallback: try OpenAI-style /v1/models
        var resp = await _httpClient.GetAsync($"{backend.BaseUrl}/v1/models");
        resp.EnsureSuccessStatusCode();

        var jsonStr = await resp.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(jsonStr);

        if (document.RootElement.TryGetProperty("data", out var dataArray))
        {
            foreach (var item in dataArray.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id))
                {
                    var n = id.GetString();
                    if (!string.IsNullOrEmpty(n))
                        models.Add(new LlmModel(n, backend.Name, backend.BaseUrl));
                }
            }
        }

        return models;
    }

    public async Task<string> AskAsync(string question, string? context = null, CancellationToken ct = default)
    {
        if (SelectedModel == null)
            return "No model selected.";

        // Cancel previous request gracefully
        var oldCts = _currentRequest;
        _currentRequest = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _currentRequest.Token;

        if (oldCts != null)
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }

        var messages = new List<object>();

        if (!string.IsNullOrWhiteSpace(context))
        {
            messages.Add(new { role = "system", content = context });
        }

        messages.Add(new { role = "user", content = $"Answer this question concisely in 1-2 sentences:\n\n{question}" });

        var requestBody = new
        {
            model = SelectedModel.Name,
            messages,
            stream = false
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _inferenceClient.PostAsync(
            $"{SelectedModel.BaseUrl}/v1/chat/completions", content, token);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(token);
        using var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.TryGetProperty("choices", out var choices)
            && choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var contentText))
            {
                return contentText.GetString()?.Trim() ?? "";
            }
        }

        return "";
    }

    public void Dispose()
    {
        _currentRequest?.Cancel();
        _currentRequest?.Dispose();
        _httpClient.Dispose();
        _inferenceClient.Dispose();
    }
}
