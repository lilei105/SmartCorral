using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace SmartCorral.Services.Ai;

/// <summary>A desktop file the LLM will categorize.</summary>
public sealed record FileDescriptor(string FullPath, string DisplayName, string Ext);

/// <summary>Text-first categorization: one batched LLM call maps each file name to a category.</summary>
public static class AiCategorizer
{
    private const string SystemPrompt =
        "You categorize files on a user's Windows desktop into a SMALL number (3-6) of clean, " +
        "human-readable category names. Reply ONLY with JSON of this exact shape: " +
        "{\"assignments\":[{\"name\":\"<exact file name>\",\"category\":\"<category>\"}]}. " +
        "Echo the EXACT 'name' given for every file. Category names: concise, in the SAME language " +
        "as the file names (Chinese if the names are Chinese, otherwise English). " +
        "Use at most 6 distinct categories. No commentary, no extra fields.";

    public static async Task<Dictionary<string, string>> CategorizeAsync(
        LlmClient llm, IReadOnlyList<FileDescriptor> files)
    {
        if (files.Count == 0) return new Dictionary<string, string>();

        string user = "Categorize these desktop files. Reply with JSON only.\n" +
                      string.Join("\n", files.Select(f => $"- {f.DisplayName} (.{f.Ext})"));

        string? content = await llm.ChatJsonAsync(SystemPrompt, user);

        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(content)) return result;

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("assignments", out var arr))
            {
                foreach (var el in arr.EnumerateArray())
                {
                    string? name = el.TryGetProperty("name", out var n) ? n.GetString() : null;
                    string? cat = el.TryGetProperty("category", out var c) ? c.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(cat))
                    {
                        result[name!.Trim()] = cat!.Trim();
                    }
                }
            }
        }
        catch
        {
            // malformed JSON — return whatever we parsed so far
        }

        return result;
    }
}
