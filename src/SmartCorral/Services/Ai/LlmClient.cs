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
        if (!resp.IsSuccessStatusCode)
        {
            // EnsureSuccessStatusCode throws with only the status code, hiding WHY (e.g. an unsupported
            // response_format, bad model, auth). Read the body and surface it — both in the log and in
            // the exception — so a silent "nothing happened" becomes a diagnosable error.
            string errBody = await ReadSafeAsync(resp.Content, cts.Token);
            string snippet = errBody.Length > 600 ? errBody[..600] + "…" : errBody;
            Logger.Error($"LLM HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} ({_endpoint})  body: {snippet}");
            throw new HttpRequestException($"LLM {(int)resp.StatusCode} {resp.ReasonPhrase}: {snippet}");
        }

        string body = await resp.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
    }

    private static async Task<string> ReadSafeAsync(HttpContent content, CancellationToken ct)
    {
        try { return await content.ReadAsStringAsync(ct); }
        catch { return "(unreadable body)"; }
    }

    public void Dispose() => _http.Dispose();
}
