using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using SmartCorral.Models;

namespace SmartCorral.Services.Ai;

/// <summary>
/// On startup (if AI is configured): scan the desktop, ask the LLM for categories, then create
/// category-named frames and file each desktop item into the right one. Non-destructive: files
/// already imported (by SourcePath) are skipped, so manual drops are preserved.
/// LLM/HTTP runs off-thread; frame/COM mutation is marshalled onto the UI thread.
/// </summary>
public static class AiOrganizeService
{
    public static async Task RunAsync(FrameManager frames, AppSettings settings)
    {
        bool configured = !string.IsNullOrWhiteSpace(settings.AiModel) &&
                          (!string.IsNullOrWhiteSpace(settings.AiApiKey) || IsLocalEndpoint(settings.AiBaseUrl));
        if (!configured) return;

        // 1. Scan desktop (no COM) off-thread.
        var allFiles = await Task.Run(DesktopScanner.ScanFiles);

        // 2. Skip files already imported into a frame.
        var existing = new HashSet<string>(frames.AllItemSourcePaths(), StringComparer.OrdinalIgnoreCase);
        var toCategorize = allFiles.Where(f => !existing.Contains(f.FullPath)).ToList();
        if (toCategorize.Count == 0) return;

        // 3. Categorize via LLM (off-thread) — index-keyed so name echo can't mismatch.
        Dictionary<int, string> assignments;
        try
        {
            using var llm = new LlmClient(settings.AiBaseUrl, settings.AiApiKey, settings.AiModel);
            assignments = await AiCategorizer.CategorizeAsync(llm, toCategorize);
        }
        catch
        {
            return; // network/LLM failure — silently skip; desktop stays manual.
        }
        if (assignments.Count == 0) return;

        // 4. Apply on the UI thread (frame creation + COM shortcut import + render).
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            for (int i = 0; i < toCategorize.Count; i++)
            {
                if (!assignments.TryGetValue(i + 1, out string? category))
                    continue; // model didn't return this one — leave it where it is

                var file = toCategorize[i];
                var frame = frames.EnsureCategoryFrame(category);
                frames.AddDesktopFile(frame, file.FullPath, file.DisplayName);
                frames.Refresh(frame);
            }
            frames.RemoveEmptyDefaultFrames();
            frames.ArrangeAll();   // right-aligned grid + persist
        });
    }

    private static bool IsLocalEndpoint(string url)
        => url.Contains("localhost", StringComparison.OrdinalIgnoreCase)
           || url.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase);
}
