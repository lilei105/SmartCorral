using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartCorral.Services.Ai;

/// <summary>
/// Minimal OpenAI-compatible chat client. Works with OpenAI, DeepSeek, Qwen, Ollama, etc.
/// Sends a single chat-completions request and returns the message content (expected to be JSON).
/// </summary>
public sealed class LlmClient : IDisposable
{
    private readonly HttpClient _http = new();
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _model;

    public LlmClient(string baseUrl, string apiKey, string model)
    {
        _endpoint = baseUrl.Trim().TrimEnd('/') + "/chat/completions";
        _apiKey = apiKey;
        _model = model;
    }

    public async Task<string?> ChatJsonAsync(string systemPrompt, string userPrompt)
    {
        object payload = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            response_format = new { type = "json_object" },
            temperature = 0
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrEmpty(_apiKey))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        using var resp = await _http.SendAsync(req, cts.Token);
        resp.EnsureSuccessStatusCode();

        string body = await resp.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
    }

    public void Dispose() => _http.Dispose();
}
