using System.Collections.Generic;
using System.Windows;
using SmartCorral.Models;

namespace SmartCorral.Services;

/// <summary>
/// Lays frames out in a right-aligned, top-down grid (columns fill right-to-left).
/// Like Fences' auto-organize: tidy columns docked to the right edge of the work area.
/// </summary>
public static class FrameArranger
{
    public static void Arrange(IList<Frame> frames, Rect workArea, double margin = 12, double gap = 12)
    {
        double rightEdge = workArea.Right - margin;
        double bottomEdge = workArea.Bottom - margin;
        double topStart = workArea.Top + margin;

        double y = topStart;
        double colWidth = 0;

        foreach (var f in frames)
        {
            // start a new column if this frame would overflow the bottom edge
            if (y + f.Height > bottomEdge && y > topStart + 0.5)
            {
                rightEdge -= colWidth + gap;
                y = topStart;
                colWidth = 0;
            }

            f.X = rightEdge - f.Width; // right-align each frame to the column's right edge
            f.Y = y;

            y += f.Height + gap;
            if (f.Width > colWidth) colWidth = f.Width;
        }
    }
}
