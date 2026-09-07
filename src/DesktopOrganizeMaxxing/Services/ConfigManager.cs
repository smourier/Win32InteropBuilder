namespace DesktopOrganizeMaxxing.Services;

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopOrganizeMaxxing.Models;

/// <summary>
/// Manages persistence of app configuration to JSON files in AppData.
/// Supports multiple saved layouts and diff detection for added/removed icons.
/// </summary>
public class ConfigManager
{
    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DesktopOrganizeMaxxing");

    private static readonly string ConfigFilePath = Path.Combine(AppDataPath, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Ensures the AppData directory exists.
    /// </summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(AppDataPath);
    }

    /// <summary>
    /// Saves the current configuration to disk.
    /// </summary>
    public void SaveConfig(AppConfig config)
    {
        EnsureDirectories();
        string json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigFilePath, json);
    }

    /// <summary>
    /// Loads the configuration from disk. Returns null if no config exists.
    /// </summary>
    public AppConfig? LoadConfig()
    {
        if (!File.Exists(ConfigFilePath)) return null;

        try
        {
            string json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading config: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Saves the current state as a named layout.
    /// </summary>
    public void SaveLayout(AppConfig config, string layoutName)
    {
        var layout = new SavedLayout
        {
            Name = layoutName,
            CreatedAt = DateTime.Now,
            Categories = config.Categories.Select(CloneCategory).ToList(),
            DesktopItems = config.DesktopItems.Select(CloneItem).ToList(),
            Fences = config.Fences.Select(CloneFence).ToList(),
            Mode = config.ActiveMode
        };

        // Replace existing layout with same name, or add new
        config.SavedLayouts.RemoveAll(l =>
            l.Name.Equals(layoutName, StringComparison.OrdinalIgnoreCase));
        config.SavedLayouts.Add(layout);

        SaveConfig(config);
    }

    /// <summary>
    /// Loads a saved layout and returns the updated config. Returns null if not found.
    /// </summary>
    public AppConfig? LoadLayout(AppConfig config, string layoutId)
    {
        var layout = config.SavedLayouts.FirstOrDefault(l => l.Id == layoutId);
        if (layout == null) return null;

        config.Categories = layout.Categories;
        config.DesktopItems = layout.DesktopItems;
        config.Fences = layout.Fences;
        config.ActiveMode = layout.Mode;

        return config;
    }

    /// <summary>
    /// Compares current desktop items with saved items and returns diff.
    /// </summary>
    public (List<DesktopItem> added, List<DesktopItem> removed) DetectChanges(
        List<DesktopItem> currentItems, List<DesktopItem> savedItems)
    {
        var currentPaths = currentItems.Select(i => i.FullPath.ToLowerInvariant()).ToHashSet();
        var savedPaths = savedItems.Select(i => i.FullPath.ToLowerInvariant()).ToHashSet();

        var added = currentItems
            .Where(i => !savedPaths.Contains(i.FullPath.ToLowerInvariant()))
            .ToList();

        var removed = savedItems
            .Where(i => !currentPaths.Contains(i.FullPath.ToLowerInvariant()))
            .ToList();

        return (added, removed);
    }

    /// <summary>
    /// Manages the Windows startup registry entry.
    /// <summary>
    /// Finds the absolute path to the application's executable.
    /// </summary>
    public static string? GetApplicationExecutablePath()
    {
        string? procPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(procPath) &&
            !procPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase) &&
            !procPath.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(procPath))
        {
            return procPath;
        }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates = new[]
        {
            Path.Combine(baseDir, "DesktopOrganizeMaxxing.exe"),
            Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\publish\standalone\DesktopOrganizeMaxxing.exe")),
            Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\publish\standalone\DesktopOrganizeMaxxing.exe")),
            @"C:\Users\Yvann\Documents\desktop organize maxxing\publish\standalone\DesktopOrganizeMaxxing.exe"
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return procPath;
    }

    /// <summary>
    /// Enables or disables running the app at Windows startup via the registry.
    /// </summary>
    public static void SetStartupEnabled(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

            if (key == null) return;

            if (enabled)
            {
                string? exePath = GetApplicationExecutablePath();
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    key.SetValue("DesktopOrganizeMaxxing", $"\"{exePath}\" --autostart");
                }
            }
            else
            {
                key.DeleteValue("DesktopOrganizeMaxxing", false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error setting startup: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if the app is set to run at startup.
    /// </summary>
    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("DesktopOrganizeMaxxing") != null;
        }
        catch
        {
            return false;
        }
    }

    // Deep clone helpers to avoid reference sharing between layouts
    private static Category CloneCategory(Category c) => new()
    {
        Id = c.Id, Name = c.Name, IconEmoji = c.IconEmoji, CustomIconPath = c.CustomIconPath,
        ColorHex = c.ColorHex, Region = c.Region, SortOrder = c.SortOrder, ItemIds = new List<string>(c.ItemIds)
    };

    private static DesktopItem CloneItem(DesktopItem i) => new()
    {
        Id = i.Id, Name = i.Name, FullPath = i.FullPath, TargetPath = i.TargetPath,
        ItemType = i.ItemType, TargetExtension = i.TargetExtension, CategoryId = i.CategoryId,
        PositionX = i.PositionX, PositionY = i.PositionY, IsPublicDesktop = i.IsPublicDesktop
    };

    private static FenceTab CloneTab(FenceTab t) => new()
    {
        Id = t.Id, CategoryId = t.CategoryId, Title = t.Title,
        IconEmoji = t.IconEmoji, CustomIconPath = t.CustomIconPath,
        IsFolderPortal = t.IsFolderPortal, FolderPortalPath = t.FolderPortalPath
    };

    private static FenceConfig CloneFence(FenceConfig f) => new()
    {
        Id = f.Id, CategoryId = f.CategoryId, Title = f.Title,
        X = f.X, Y = f.Y, Width = f.Width, Height = f.Height,
        Shape = f.Shape, CornerRadius = f.CornerRadius,
        BackgroundColorHex = f.BackgroundColorHex, Opacity = f.Opacity,
        ChameleonMode = f.ChameleonMode, ChameleonIdleOpacity = f.ChameleonIdleOpacity,
        IsRolledUp = f.IsRolledUp, AutoRollUpOnHover = f.AutoRollUpOnHover,
        Orientation = f.Orientation, ShowTabTitles = f.ShowTabTitles,
        IsFolderPortal = f.IsFolderPortal, FolderPortalPath = f.FolderPortalPath,
        TitleBarColorHex = f.TitleBarColorHex, TextColorHex = f.TextColorHex,
        IconEmoji = f.IconEmoji, CustomIconPath = f.CustomIconPath,
        ActiveTabIndex = f.ActiveTabIndex,
        Tabs = f.Tabs.Select(CloneTab).ToList()
    };
}
