using System;

namespace SmartCorral.Services;

/// <summary>
/// Picks a frame height that fits its icon rows (no scroll) given the item count and width.
/// Layout assumptions (WPF DIPs): ~80px per item horizontally, ~64px per row vertically,
/// ~80px of chrome (title bar + paddings). Tunable if the look is off.
/// </summary>
public static class FrameSizer
{
    public const double DefaultWidth = 320;

    private const double ItemPitchX = 80;   // item button (76) + margin
    private const double RowPitchY = 64;    // icon + label + margin per row
    private const double Chrome = 80;       // title bar + inner paddings + window border margin
    private const double MinHeight = 140;
    private const double MaxHeight = 1000;

    public static double HeightFor(int itemCount, double width)
    {
        int columns = Math.Max(1, (int)Math.Floor((width - 20) / ItemPitchX)); // 320 -> 3
        int rows = (int)Math.Ceiling(itemCount / (double)columns);
        double h = Chrome + rows * RowPitchY;
        return Math.Clamp(h, MinHeight, MaxHeight);
    }
}
