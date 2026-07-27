namespace SmartCorral.Models;

/// <summary>A single item (shortcut) inside a DataFrame.</summary>
public class FrameItem
{
    /// <summary>Path relative to the data folder, e.g. "shortcuts/foo.lnk".</summary>
    public string Filename { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    /// <summary>True for web/URL links.</summary>
    public bool IsLink { get; set; }
    public bool IsNetwork { get; set; }
    public int DisplayOrder { get; set; }
    /// <summary>Resolved target path (cached for icon extraction / launch).</summary>
    public string? Target { get; set; }

    /// <summary>The original source file path (e.g. desktop file) this item was imported from — used to avoid duplicates.</summary>
    public string? SourcePath { get; set; }
}
