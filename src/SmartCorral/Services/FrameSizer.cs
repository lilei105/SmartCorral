using System;

namespace SmartCorral.Services;

/// <summary>
/// Picks frame width/height and per-item metrics, all scaled by <see cref="Scale"/> (a UI zoom set
/// from settings). Base values are calibrated to the default 40px icon: item button = 76 + 8 margin
/// = 84px wide; overhead = RootBorder(20) + WrapPanel(20) + slack(4) = 44px; ~74px per icon row;
/// ~90px chrome (title bar + padding).
/// </summary>
public static class FrameSizer
{
    /// <summary>UI zoom for frame contents (icon + label + title + frame sizing). 1.0 = default.</summary>
    public static double Scale { get; set; } = 1.0;

    // Base values at Scale = 1.0.
    private const double BaseIconSize = 40;
    private const double BaseItemFont = 12;
    private const double BaseTitleFont = 14;
    private const double BaseButtonWidth = 76;
    private const double BaseLabelMaxWidth = 72;
    private const double BasePerItemWidth = 84;
    private const double BaseWidthOverhead = 44;
    private const double BaseRowPitchY = 74;
    private const double BaseChrome = 90;
    private const double MinHeight = 150;
    private const double MaxHeight = 1000;

    public static double IconSize => BaseIconSize * Scale;
    public static double ItemFont => BaseItemFont * Scale;
    public static double TitleFont => BaseTitleFont * Scale;
    public static double ButtonWidth => BaseButtonWidth * Scale;
    public static double LabelMaxWidth => BaseLabelMaxWidth * Scale;

    public static double WidthForColumns(int columns) =>
        Math.Max(2, columns) * BasePerItemWidth * Scale + BaseWidthOverhead * Scale;

    public static double HeightFor(int fileCount, int folderCount, int columns)
    {
        int cols = Math.Max(1, columns);
        int fileRows = (int)Math.Ceiling(fileCount / (double)cols);
        int folderRows = folderCount > 0 ? (int)Math.Ceiling(folderCount / (double)cols) : 0;
        double gap = folderRows > 0 ? 12 * Scale : 0;
        double h = BaseChrome * Scale + (fileRows + folderRows) * BaseRowPitchY * Scale + gap;
        return Math.Clamp(h, MinHeight * Scale, MaxHeight);
    }
}
