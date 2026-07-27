using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace SmartCorral.Services.Ai;

/// <summary>A desktop item (file or folder) the LLM will categorize.</summary>
public sealed record FileDescriptor(string FullPath, string DisplayName, string Ext, bool IsFolder);

/// <summary>Text-first categorization: one batched LLM call maps each file (by 1-based index) to a category.</summary>
public static class AiCategorizer
{
    private const string SystemPrompt =
        "You categorize files on a user's Windows desktop into BROAD buckets. " +
        "Use AT MOST 5 distinct categories — strongly prefer FEWER, larger groups over many tiny ones. " +
        "Typical umbrella categories: 游戏 / 办公 / 开发 / 媒体 / 工具 / 社交 / 系统 — pick whatever fits " +
        "THIS user's files, but keep the total to <=5. The user gives a NUMBERED list. " +
        "Reply ONLY with JSON: {\"assignments\":[{\"index\":<number>,\"category\":\"<category>\"}]}. " +
        "'index' is the file's number from the list. Provide ONE entry for EVERY number. " +
        "Category names: concise, in the SAME language as the file names (Chinese if the names " +
        "are Chinese, otherwise English). The list may contain both files and folders " +
        "(folders are marked [文件夹]); categorize all of them. No commentary.";

    public static async Task<Dictionary<int, string>> CategorizeAsync(
        LlmClient llm, IReadOnlyList<FileDescriptor> files)
    {
        var result = new Dictionary<int, string>();
        if (files.Count == 0) return result;

        string user = "Categorize these desktop items (reply JSON only):\n" +
                      string.Join("\n", files.Select((f, i) => f.IsFolder
                          ? $"{i + 1}. {f.DisplayName} [文件夹]"
                          : $"{i + 1}. {f.DisplayName} (.{f.Ext})"));

        string? content = await llm.ChatJsonAsync(SystemPrompt, user);

        // Debug dump — the raw model response, handy when something looks off.
        try
        {
            string dump = Path.Combine(AppContext.BaseDirectory, "data", "last_ai.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(dump)!);
            File.WriteAllText(dump, content ?? "<null>");
        }
        catch { }

        if (string.IsNullOrWhiteSpace(content)) return result;

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("assignments", out var arr))
            {
                foreach (var el in arr.EnumerateArray())
                {
                    if (el.TryGetProperty("index", out var idx) &&
                        el.TryGetProperty("category", out var catEl) &&
                        idx.TryGetInt32(out int i))
                    {
                        string? cat = catEl.GetString();
                        if (!string.IsNullOrWhiteSpace(cat)) result[i] = cat!.Trim();
                    }
                }
            }
        }
        catch
        {
            // malformed JSON — return what we parsed
        }

        return result;
    }
}
