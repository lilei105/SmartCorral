namespace SmartCorral.Models;

/// <summary>Root persisted state.</summary>
public class AppData
{
    public List<Frame> Frames { get; set; } = new();
}
