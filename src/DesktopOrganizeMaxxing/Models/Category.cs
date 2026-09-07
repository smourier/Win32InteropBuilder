namespace DesktopOrganizeMaxxing.Models;

using System.Windows.Media;

/// <summary>
/// Represents a category that groups desktop items together.
/// </summary>
public class Category
{
    /// <summary>Unique identifier.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Display name (e.g., "Games", "Development").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Emoji or icon identifier for the category.</summary>
    public string IconEmoji { get; set; } = "📁";

    /// <summary>Optional path to a custom .ico or image file.</summary>
    public string? CustomIconPath { get; set; }

    /// <summary>Color associated with this category (hex string).</summary>
    public string ColorHex { get; set; } = "#5B7FFF";

    /// <summary>Desktop region for Mode 1 positioning.</summary>
    public DesktopRegion Region { get; set; } = DesktopRegion.Center;

    /// <summary>Sort order for display.</summary>
    public int SortOrder { get; set; }

    /// <summary>List of item IDs assigned to this category.</summary>
    public List<string> ItemIds { get; set; } = new();

    /// <summary>Gets the Color from the hex string (not serialized).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Color Color
    {
        get
        {
            try { return (Color)ColorConverter.ConvertFromString(ColorHex); }
            catch { return Colors.CornflowerBlue; }
        }
    }
}

/// <summary>
/// Predefined regions on the desktop for Mode 1 icon placement.
/// </summary>
public enum DesktopRegion
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}
