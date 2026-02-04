using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Clippy;

public class OllamaService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private CancellationTokenSource? _currentRequest;

    public string CurrentModel { get; set; }

    public OllamaService(string model = "qwen3-4b-thinking", string baseUrl = "http://localhost:11434")
    {
        CurrentModel = model;
        _baseUrl = baseUrl;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<List<string>> ListModelsAsync()
    {
        var models = new List<string>();
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags");
        response.EnsureSuccessStatusCode();

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
                        models.Add(n);
                }
            }
        }

        return models;
    }

    public async Task<bool> IsConnectedAsync()
    {
        try
        {
            await ListModelsAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> AskAsync(string question, CancellationToken ct = default)
    {
        _currentRequest?.Cancel();
        _currentRequest = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _currentRequest.Token;

        var requestBody = new
        {
            model = CurrentModel,
            messages = new[]
            {
                new { role = "user", content = $"Answer this question concisely in 1-2 sentences:\n\n{question}" }
            },
            stream = false
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_baseUrl}/v1/chat/completions", content, token);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(token);
        using var doc = JsonDocument.Parse(responseJson);

        // OpenAI format: { "choices": [{ "message": { "content": "..." } }] }
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
    }
}
