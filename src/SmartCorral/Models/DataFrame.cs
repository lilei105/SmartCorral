namespace SmartCorral.Models;

/// <summary>A frame that holds the user's own shortcut items.</summary>
public class DataFrame : Frame
{
    public List<FrameItem> Items { get; set; } = new();
}
