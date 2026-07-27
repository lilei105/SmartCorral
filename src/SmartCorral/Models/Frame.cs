using System.Text.Json.Serialization;

namespace SmartCorral.Models;

/// <summary>
/// Base type for all frames. Persisted polymorphically (System.Text.Json discriminator "$kind").
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(DataFrame), "data")]
public abstract class Frame
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "New Frame";
    public double X { get; set; } = 120;
    public double Y { get; set; } = 120;
    public double Width { get; set; } = 360;
    public double Height { get; set; } = 220;
    public bool IsLocked { get; set; } = false;
    public bool IsRolled { get; set; } = false;
}
