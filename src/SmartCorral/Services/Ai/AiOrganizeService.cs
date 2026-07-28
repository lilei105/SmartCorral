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
    /// <param name="onResult">Optional (message, isError) callback — when given (e.g. the tray
    /// "Re-categorize" action), a balloon reports the outcome so a ~15 s run isn't silent. Startup
    /// omits it (background, logged only).</param>
    public static async Task RunAsync(FrameManager frames, AppSettings settings, Action<string, bool>? onResult = null)
    {
        bool hasKey = !string.IsNullOrWhiteSpace(settings.AiApiKey);
        bool configured = !string.IsNullOrWhiteSpace(settings.AiModel) &&
                          (hasKey || IsLocalEndpoint(settings.AiBaseUrl));
        Logger.Info($"AI organize start: configured={configured} (model='{settings.AiModel}', key={(hasKey ? "set" : "missing")}, base='{settings.AiBaseUrl}')");
        if (!configured)
        {
            Logger.Warn("AI organize: not configured — skipping.");
            Report(onResult, "AI 未配置：请在「设置」里填好 Base URL / API Key / Model。", isError: true);
            return;
        }

        // 1. Scan desktop (files + folders, no COM) off-thread.
        var allFiles = await Task.Run(DesktopScanner.Scan);
        Logger.Info($"AI organize: scanned {allFiles.Count} desktop item(s).");

        // 2. Skip files already imported into a frame.
        var existing = new HashSet<string>(frames.AllItemSourcePaths(), StringComparer.OrdinalIgnoreCase);
        var toCategorize = allFiles.Where(f => !existing.Contains(f.FullPath)).ToList();
        Logger.Info($"AI organize: {toCategorize.Count} to categorize ({allFiles.Count - toCategorize.Count} already filed).");
        if (toCategorize.Count == 0)
        {
            Logger.Info("AI organize: nothing to do.");
            Report(onResult, "桌面没有需要分类的新文件。", isError: false);
            return;
        }

        // 3. Categorize via LLM (off-thread) — index-keyed so name echo can't mismatch.
        Dictionary<int, string> assignments;
        try
        {
            using var llm = new LlmClient(settings.AiBaseUrl, settings.AiApiKey, settings.AiModel);
            assignments = await AiCategorizer.CategorizeAsync(llm, toCategorize);
        }
        catch (Exception ex)
        {
            Logger.Error("AI organize: LLM call failed — desktop stays manual.", ex);
            Report(onResult, "AI 分类失败，原因详见 smartcorral.log。", isError: true);
            return;
        }
        Logger.Info($"AI organize: model categorized {assignments.Count}/{toCategorize.Count} item(s).");
        if (assignments.Count == 0)
        {
            Logger.Warn("AI organize: model returned no categories.");
            Report(onResult, "AI 这次没有给出分类，桌面保持原样。", isError: true);
            return;
        }

        // 4. Apply on the UI thread (frame creation + COM shortcut import + render).
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            int applied = 0;
            for (int i = 0; i < toCategorize.Count; i++)
            {
                if (!assignments.TryGetValue(i + 1, out string? category))
                    continue; // model didn't return this one — leave it where it is

                var file = toCategorize[i];
                var frame = frames.EnsureCategoryFrame(category);
                frames.AddDesktopFile(frame, file.FullPath, file.DisplayName);
                frames.Refresh(frame);
                applied++;
            }
            Logger.Info($"AI organize: filed {applied} item(s) into frames.");
            frames.RemoveEmptyDefaultFrames();
            frames.SizeFramesToContent();
            frames.ArrangeAll();   // right-aligned grid + persist
            Report(onResult, $"已用 AI 归类 {applied} 项。", isError: false);
        });
    }

    /// <summary>Marshals a human-readable outcome to the UI thread (so the tray balloon callback is
    /// safe to invoke from the off-thread LLM phase). No-op when no callback was supplied.</summary>
    private static void Report(Action<string, bool>? onResult, string message, bool isError)
    {
        if (onResult == null) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
            dispatcher.InvokeAsync(() => onResult(message, isError));
        else
            onResult(message, isError);
    }

    private static bool IsLocalEndpoint(string url)
        => url.Contains("localhost", StringComparison.OrdinalIgnoreCase)
           || url.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase);
}
