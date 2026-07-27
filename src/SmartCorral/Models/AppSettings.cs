namespace SmartCorral.Models;

/// <summary>User-configurable settings. Persisted to data/settings.json.</summary>
public class AppSettings
{
    /// <summary>OpenAI-compatible base URL, e.g. https://api.openai.com/v1, https://api.deepseek.com/v1, http://localhost:11434/v1 (Ollama).</summary>
    public string AiBaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>API key (empty for local/no-auth endpoints like Ollama). TODO: DPAPI-encrypt at rest.</summary>
    public string AiApiKey { get; set; } = "";

    /// <summary>Chat model id, e.g. gpt-4o-mini, deepseek-chat.</summary>
    public string AiModel { get; set; } = "gpt-4o-mini";

    /// <summary>How many icons fit in one frame row (2–8). Frame width derives from this.</summary>
    public int IconsPerRow { get; set; } = 3;
}
