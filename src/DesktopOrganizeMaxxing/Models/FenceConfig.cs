namespace DesktopOrganizeMaxxing.Models;

/// <summary>
/// Represents a single tab within a Fence (can be a category or a folder portal).
/// </summary>
public class FenceTab
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Associated category ID (null for folder portals).</summary>
    public string? CategoryId { get; set; }

    /// <summary>Display title of the tab.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Icon emoji for the tab header.</summary>
    public string IconEmoji { get; set; } = "📁";

    /// <summary>Optional custom icon path (.ico or image).</summary>
    public string? CustomIconPath { get; set; }

    /// <summary>Whether this tab is a folder portal.</summary>
    public bool IsFolderPortal { get; set; } = false;

    /// <summary>Folder path for folder portals.</summary>
    public string? FolderPortalPath { get; set; }
}

/// <summary>
/// Configuration for a single fence window (Mode 2), which may contain multiple tabs.
/// </summary>
public class FenceConfig
{
    /// <summary>Unique identifier.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Associated category ID (null for folder portals or multi-tab fences).</summary>
    public string? CategoryId { get; set; }

    /// <summary>Display title of the fence.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Position X on screen.</summary>
    public double X { get; set; } = 100;

    /// <summary>Position Y on screen.</summary>
    public double Y { get; set; } = 100;

    /// <summary>Width of the fence.</summary>
    public double Width { get; set; } = 400;

    /// <summary>Height of the fence.</summary>
    public double Height { get; set; } = 300;

    /// <summary>Shape of the fence.</summary>
    public FenceShape Shape { get; set; } = FenceShape.RoundedRectangle;

    /// <summary>Corner radius for rounded shapes.</summary>
    public double CornerRadius { get; set; } = 12;

    /// <summary>Background color (hex string).</summary>
    public string BackgroundColorHex { get; set; } = "#1E1E2E";

    /// <summary>Background opacity (0.0 to 1.0).</summary>
    public double Opacity { get; set; } = 0.75;

    /// <summary>Whether chameleon/hover mode is enabled.</summary>
    public bool ChameleonMode { get; set; } = true;

    /// <summary>Opacity when chameleon mode is active and mouse is not hovering.</summary>
    public double ChameleonIdleOpacity { get; set; } = 0.3;

    /// <summary>Whether the fence is currently rolled up.</summary>
    public bool IsRolledUp { get; set; } = false;

    /// <summary>Whether the fence automatically rolls up when mouse leaves and expands on hover.</summary>
    public bool AutoRollUpOnHover { get; set; } = false;

    /// <summary>Whether this fence is a folder portal (for single-tab fences).</summary>
    public bool IsFolderPortal { get; set; } = false;

    /// <summary>Folder path for folder portals.</summary>
    public string? FolderPortalPath { get; set; }

    /// <summary>Title bar color hex.</summary>
    public string TitleBarColorHex { get; set; } = "#2D2D3F";

    /// <summary>Text color hex.</summary>
    public string TextColorHex { get; set; } = "#FFFFFF";

    /// <summary>Icon emoji for the fence header.</summary>
    public string IconEmoji { get; set; } = "📁";

    /// <summary>Optional custom icon path (.ico or image).</summary>
    public string? CustomIconPath { get; set; }

    /// <summary>Tabs contained in this fence (for merged/tabbed fences).</summary>
    public List<FenceTab> Tabs { get; set; } = new();

    /// <summary>Currently selected tab index.</summary>
    public int ActiveTabIndex { get; set; } = 0;

    /// <summary>Orientation of the fence: Horizontal (standard, top header, rolls up/down) or Vertical (side header, docked to edge, rolls sideways).</summary>
    public FenceOrientation Orientation { get; set; } = FenceOrientation.Horizontal;

    /// <summary>Whether to show text titles for tabs (can be toggled in both horizontal and vertical modes).</summary>
    public bool ShowTabTitles { get; set; } = true;

    /// <summary>Whether this horizontal fence opens upwards (e.g. snapped to bottom of screen or limited space below).</summary>
    public bool OpenUpward { get; set; } = false;

    /// <summary>Roll-up / unroll animation duration in milliseconds (e.g. 100ms ultra-fast, 200ms normal, 400ms smooth, 0ms instant).</summary>
    public int RollUpAnimationDurationMs { get; set; } = 200;

    /// <summary>
    /// Ensures this config has at least one tab representing itself.
    /// </summary>
    public void EnsureDefaultTab()
    {
        if (Tabs.Count == 0)
        {
            Tabs.Add(new FenceTab
            {
                Id = Id,
                CategoryId = CategoryId,
                Title = Title,
                IconEmoji = IconEmoji,
                CustomIconPath = CustomIconPath,
                IsFolderPortal = IsFolderPortal,
                FolderPortalPath = FolderPortalPath
            });
        }
    }
}

public enum FenceOrientation
{
    Horizontal,
    Vertical
}

public enum FenceShape
{
    Rectangle,
    RoundedRectangle,
    Circle
}
