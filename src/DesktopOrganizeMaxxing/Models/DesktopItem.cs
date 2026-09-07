namespace DesktopOrganizeMaxxing.Models;

using System.Windows.Media.Imaging;

/// <summary>
/// Represents an item found on the desktop (shortcut, folder, file, executable).
/// </summary>
public class DesktopItem
{
    /// <summary>Unique identifier for this item.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Display name of the item (without extension for shortcuts).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Full path to the desktop item (.lnk, folder, file).</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>Resolved target path for shortcuts, or same as FullPath for non-shortcuts.</summary>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>The type of desktop item.</summary>
    public DesktopItemType ItemType { get; set; } = DesktopItemType.Unknown;

    /// <summary>File extension of the target (e.g., ".exe", ".pdf").</summary>
    public string TargetExtension { get; set; } = string.Empty;

    /// <summary>The category this item is assigned to (null = uncategorized).</summary>
    public string? CategoryId { get; set; }

    /// <summary>Extracted icon as a BitmapSource (not serialized).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public BitmapSource? Icon { get; set; }

    /// <summary>Current X position on the desktop.</summary>
    public int PositionX { get; set; }

    /// <summary>Current Y position on the desktop.</summary>
    public int PositionY { get; set; }

    /// <summary>Whether this item is from the Public Desktop (shared across users).</summary>
    public bool IsPublicDesktop { get; set; }

    public override string ToString() => Name;
}

public enum DesktopItemType
{
    Unknown,
    Shortcut,    // .lnk file
    Folder,      // Directory
    Executable,  // .exe file
    File,        // Any other file
    UrlShortcut  // .url file
}
