using System;

namespace SmartCorral.Services;

/// <summary>
/// Picks frame width/height from a chosen icons-per-row count. Constants are calibrated to the
/// actual layout: item button = 76 + 8 margin = 84px; overhead = RootBorder(20) + WrapPanel(20)
/// + slack(4) = 44px horizontally; title(42) + margins/padding + slack = 90px chrome vertically;
/// ~64px per icon row.
/// </summary>
public static class FrameSizer
{
    private const double PerItemWidth = 84;
    private const double WidthOverhead = 44;
    private const double RowPitchY = 64;
    private const double Chrome = 90;
    private const double MinHeight = 150; // ensures a single icon row never overflows into a scrollbar
    private const double MaxHeight = 1000;

    public static double WidthForColumns(int columns) => Math.Max(2, columns) * PerItemWidth + WidthOverhead;

    public static double HeightFor(int fileCount, int folderCount, int columns)
    {
        int cols = Math.Max(1, columns);
        int fileRows = (int)Math.Ceiling(fileCount / (double)cols);
        int folderRows = folderCount > 0 ? (int)Math.Ceiling(folderCount / (double)cols) : 0;
        double gap = folderRows > 0 ? 12 : 0;
        double h = Chrome + (fileRows + folderRows) * RowPitchY + gap;
        return Math.Clamp(h, MinHeight, MaxHeight);
    }
}
