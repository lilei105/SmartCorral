using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace SmartCorral.Services.Ai;

/// <summary>A desktop item (file or folder) the LLM will categorize.</summary>
public sealed record FileDescriptor(string FullPath, string DisplayName, string Ext, bool IsFolder);

/// <summary>
/// Multi-step AI categorization:
///   1. Profile the user from their desktop items → design creative, personalized categories.
///   2. Categorize files + .lnk icons into those categories (pass 1).
///   3. Categorize folders (with first-level children) into the EXISTING categories from pass 1
///      (pass 2 — prefer reusing, create new only if 5+ don't fit).
/// </summary>
public static class AiCategorizer
{
    // ---- Step 1: Profile + category design ----

    public static async Task<(string Profile, List<string> Categories)> ProfileAsync(
        LlmClient llm, IReadOnlyList<FileDescriptor> allItems)
    {
        if (allItems.Count == 0) return ("", new List<string>());

        const string sys = "You are a desktop organizer AI. Analyze the user's desktop items to understand " +
            "WHO they are (office worker? developer? designer? student?). Then design 5-8 CREATIVE, " +
            "PERSONALIZED category names — NOT generic labels like '工具/媒体/社交'. Tailor them to THIS user.\n" +
            "For example: 钉钉+飞书+企业微信 → '办公协作' (not '社交'); Docker+Git+IDE → '开发环境' (not '开发');\n" +
            "游戏+Steam → '游戏娱乐'; 报告+合同+发票 → '商务文档'.\n" +
            "ALWAYS include a catch-all category (e.g., '杂项' or '其他') for items that don't fit specific ones.\n" +
            "Category names in the SAME language as the majority of the file names.\n" +
            "Reply ONLY with JSON: {\"profile\":\"<one sentence about the user>\"," +
            "\"categories\":[\"<name1>\",\"<name2>\",...]}";

        string user = "Desktop items:\n" + string.Join("\n",
            allItems.Select((f, i) => $"{i + 1}. {f.DisplayName}{(f.IsFolder ? " [文件夹]" : "")}"));

        string? content = await llm.ChatJsonAsync(sys, user);
        Dump("profile", content);

        var categories = new List<string>();
        string profile = "";
        try
        {
            using var doc = JsonDocument.Parse(content ?? "");
            if (doc.RootElement.TryGetProperty("profile", out var p))
                profile = p.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("categories", out var arr))
                foreach (var el in arr.EnumerateArray())
                {
                    string? c = el.GetString();
                    if (!string.IsNullOrWhiteSpace(c)) categories.Add(c!.Trim());
                }
        }
        catch { }
        return (profile, categories);
    }

    // ---- Step 2: Categorize files + .lnk into given categories ----

    public static async Task<Dictionary<int, string>> CategorizeAsync(
        LlmClient llm, IReadOnlyList<FileDescriptor> items, IReadOnlyList<string> categories)
    {
        var result = new Dictionary<int, string>();
        if (items.Count == 0 || categories.Count == 0) return result;

        string catList = string.Join(" / ", categories);
        string sys = $"Assign each desktop item to the BEST-FIT category. Categories: {catList}.\n" +
            "You MUST provide an entry for EVERY SINGLE item — never omit any. " +
            "If unsure, assign to the CLOSEST category or the catch-all (杂项/其他). " +
            "Use the EXACT category names from the list above.\n" +
            "Reply ONLY with JSON: {\"assignments\":[{\"index\":<number>,\"category\":\"<name>\"}]}. " +
            "'index' is the item's number from the list. No commentary.";

        string user = "Categorize these items:\n" + string.Join("\n",
            items.Select((f, i) => $"{i + 1}. {f.DisplayName}{(f.IsFolder ? " [文件夹]" : f.Ext.Length > 0 ? $" (.{f.Ext})" : "")}"));

        string? content = await llm.ChatJsonAsync(sys, user);
        Dump("categorize", content);
        ParseAssignments(content, result);
        return result;
    }

    // ---- Step 3: Categorize folders (with children) into EXISTING categories ----

    public static async Task<Dictionary<int, string>> CategorizeFoldersAsync(
        LlmClient llm, IReadOnlyList<(FileDescriptor folder, List<string> children)> folders,
        IReadOnlyList<string> existingCategories)
    {
        var result = new Dictionary<int, string>();
        if (folders.Count == 0) return result;

        string catList = existingCategories.Count > 0 ? string.Join(" / ", existingCategories) : "(none yet)";
        string sys = $"Categorize desktop FOLDERS. Each folder lists its first-level contents.\n" +
            $"EXISTING categories (PREFER these): {catList}.\n" +
            "You MUST assign EVERY folder — never omit any. If unsure, use the closest existing category.\n" +
            "Only suggest a NEW category if 5+ folders genuinely don't fit any existing one.\n" +
            "Reply ONLY with JSON: {\"assignments\":[{\"index\":<number>,\"category\":\"<name>\"}]}. " +
            "Use existing category names where possible. No commentary.";

        string user = "Categorize these folders:\n" + string.Join("\n",
            folders.Select((pair, i) =>
            {
                string children = pair.children.Count > 0
                    ? $" (内含: {string.Join(", ", pair.children.Take(15))})"
                    : " (空文件夹)";
                return $"{i + 1}. {pair.folder.DisplayName}{children}";
            }));

        string? content = await llm.ChatJsonAsync(sys, user);
        Dump("folders", content);
        ParseAssignments(content, result);
        return result;
    }

    // ---- helpers ----

    private static void ParseAssignments(string? content, Dictionary<int, string> result)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("assignments", out var arr))
                foreach (var el in arr.EnumerateArray())
                    if (el.TryGetProperty("index", out var idx) && idx.TryGetInt32(out int i) &&
                        el.TryGetProperty("category", out var catEl))
                    {
                        string? cat = catEl.GetString();
                        if (!string.IsNullOrWhiteSpace(cat)) result[i] = cat!.Trim();
                    }
        }
        catch { }
    }

    private static void Dump(string tag, string? content)
    {
        try
        {
            string dump = Path.Combine(AppContext.BaseDirectory, "data", $"last_ai_{tag}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(dump)!);
            File.WriteAllText(dump, content ?? "<null>");
        }
        catch { }
    }
}
