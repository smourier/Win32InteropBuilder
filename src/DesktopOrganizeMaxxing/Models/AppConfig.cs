namespace DesktopOrganizeMaxxing.Models;

/// <summary>
/// Root application configuration, serialized to JSON.
/// </summary>
public class AppConfig
{
    /// <summary>The active display mode.</summary>
    public DisplayMode ActiveMode { get; set; } = DisplayMode.None;

    /// <summary>How categorization was done.</summary>
    public CategorizationMode CategorizationMode { get; set; } = CategorizationMode.Automatic;

    /// <summary>All categories.</summary>
    public List<Category> Categories { get; set; } = new();

    /// <summary>All known desktop items.</summary>
    public List<DesktopItem> DesktopItems { get; set; } = new();

    /// <summary>Fence configurations for Mode 2.</summary>
    public List<FenceConfig> Fences { get; set; } = new();

    /// <summary>Named saved layouts.</summary>
    public List<SavedLayout> SavedLayouts { get; set; } = new();

    /// <summary>Whether to run at Windows startup.</summary>
    public bool RunAtStartup { get; set; } = true;

    /// <summary>Theme preference.</summary>
    public AppThemeMode Theme { get; set; } = AppThemeMode.Auto;

    /// <summary>Whether fences are currently hidden (double-click toggle).</summary>
    public bool FencesHidden { get; set; } = false;

    /// <summary>Whether to prompt the user to categorize when a new item is added to the desktop.</summary>
    public bool PromptOnNewDesktopItem { get; set; } = true;

    /// <summary>Language preference (English or French).</summary>
    public AppLanguage Language { get; set; } = AppLanguage.English;

    /// <summary>Whether the initial setup wizard has been completed.</summary>
    public bool SetupCompleted { get; set; } = false;

    /// <summary>Whether fences should automatically avoid overlapping when moved.</summary>
    public bool PreventFenceOverlap { get; set; } = true;
}

/// <summary>
/// A named snapshot of the current layout.
/// </summary>
public class SavedLayout
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<Category> Categories { get; set; } = new();
    public List<DesktopItem> DesktopItems { get; set; } = new();
    public List<FenceConfig> Fences { get; set; } = new();
    public DisplayMode Mode { get; set; } = DisplayMode.None;
}

public enum DisplayMode
{
    None,
    SimpleOrganize,  // Mode 1
    Fences           // Mode 2
}

public enum CategorizationMode
{
    Automatic,
    Manual
}

public enum AppThemeMode
{
    Auto,
    Dark,
    Light
}

public enum AppLanguage
{
    English,
    French
}

