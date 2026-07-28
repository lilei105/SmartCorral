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

    /// <summary>Render folders on their own row below files within each frame.</summary>
    public bool SeparateFolders { get; set; } = true;

    /// <summary>Keep frames always-on-top of other windows. Turn off so other windows can cover them.</summary>
    public bool ForceTopmost { get; set; } = true;

    /// <summary>Frame UI zoom (icon + label + title + frame sizing together). 1.0 = default; 0.8–1.3.</summary>
    public double UIScale { get; set; } = 1.0;

    /// <summary>Write a timestamped trace to data/smartcorral.log (lifecycle, custody moves, AI pipeline
    /// + errors). Default on so problems are captured without a settings round-trip.</summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>Auto-file newly-arrived desktop items (downloads/save-as) via a FileSystemWatcher +
    /// incremental AI categorize. Default on (the product's pitch is "automatic"). Only acts when AI
    /// is configured; processes only genuinely-new paths, never pre-existing leftovers.</summary>
    public bool EnableIncrementalCategorize { get; set; } = true;
}
