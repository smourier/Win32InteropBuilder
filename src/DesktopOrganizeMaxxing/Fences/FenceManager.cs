using System.Windows;
using DesktopOrganizeMaxxing.Interop;
using DesktopOrganizeMaxxing.Models;
using DesktopOrganizeMaxxing.Services;

namespace DesktopOrganizeMaxxing.Fences;

/// <summary>
/// Manages all fence windows (category fences, multi-tab fences, and folder portals),
/// handles double-click desktop toggle in both Simple and Fences modes,
/// and facilitates tab merging/detaching and drag-and-drop between fences.
/// </summary>
public class FenceManager : IDisposable
{
    private readonly List<FenceWindow> _fenceWindows = new();
    private readonly DesktopListView _desktopListView = new();
    private GlobalMouseHook? _mouseHook;
    private bool _elementsVisible = true;
    private DisplayMode _currentMode = DisplayMode.SimpleOrganize;
    private bool _disposed;

    private List<DesktopItem> _lastAllItems = new();
    private List<Category> _lastCategories = new();
    private List<FenceConfig> _lastFenceConfigs = new();

    public DesktopListView DesktopListView => _desktopListView;

    public FenceManager()
    {
        InstallDesktopHook();
    }

    /// <summary>
    /// Creates fence windows and folder portals according to the current display mode.
    /// In Mode 1 (Simple): Only Folder Portals are shown; native desktop icons are visible.
    /// In Mode 2 (Fences): Category Fences AND Folder Portals are shown; native desktop icons are hidden.
    /// </summary>
    public void CreateFences(List<FenceConfig> fenceConfigs, List<DesktopItem> allItems, List<Category> categories, DisplayMode mode)
    {
        _currentMode = mode;
        _lastFenceConfigs = fenceConfigs;
        _lastAllItems = allItems;
        _lastCategories = categories;

        // Close existing fence windows
        foreach (var window in _fenceWindows)
        {
            try { window.Close(); } catch { }
        }
        _fenceWindows.Clear();

        // Deduplication safety: if category C is in a multi-tab fence, do not spawn a standalone duplicate fence for C
        var multiTabCategoryIds = fenceConfigs
            .Where(f => !f.IsFolderPortal && f.Tabs.Count > 1)
            .SelectMany(f => f.Tabs)
            .Select(t => t.CategoryId)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet();

        var multiTabTitles = fenceConfigs
            .Where(f => !f.IsFolderPortal && f.Tabs.Count > 1)
            .SelectMany(f => f.Tabs)
            .Select(t => t.Title)
            .Where(t => !string.IsNullOrEmpty(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var configsToCreate = fenceConfigs
            .Where(f => f.IsFolderPortal || f.Tabs.Count > 1 || ((f.CategoryId == null || !multiTabCategoryIds.Contains(f.CategoryId)) && !multiTabTitles.Contains(f.Title)))
            .ToList();

        foreach (var config in configsToCreate)
        {
            config.EnsureDefaultTab();

            bool isPortalOnly = config.Tabs.Count > 0 && config.Tabs.All(t => t.IsFolderPortal);

            if (mode == DisplayMode.SimpleOrganize)
            {
                // In Simple mode: only display folder portals
                if (isPortalOnly || config.IsFolderPortal)
                {
                    var portalWindow = new FenceWindow(config, allItems, categories, this);
                    portalWindow.Show();
                    portalWindow.SetDesktopPosition();
                    _fenceWindows.Add(portalWindow);
                }
            }
            else // DisplayMode.Fences
            {
                var fenceWindow = new FenceWindow(config, allItems, categories, this);
                fenceWindow.Show();
                fenceWindow.SetDesktopPosition();
                _fenceWindows.Add(fenceWindow);
            }
        }

        if (mode == DisplayMode.Fences)
        {
            // Hide native desktop icons in Fences mode
            _desktopListView.HideDesktopIcons();
        }
        else
        {
            // Ensure native desktop icons are visible in Simple mode
            _desktopListView.ShowDesktopIcons();
        }

        _elementsVisible = true;
    }

    /// <summary>
    /// Updates all existing fence windows with new visual configurations.
    /// </summary>
    public void UpdateFences(List<FenceConfig> fenceConfigs)
    {
        _lastFenceConfigs = fenceConfigs;
        foreach (var window in _fenceWindows)
        {
            var config = fenceConfigs.FirstOrDefault(c => c.Id == window.FenceId);
            if (config != null)
            {
                window.ApplyConfig();
                window.RenderTabs();
                window.PopulateActiveTab();
            }
        }
    }

    /// <summary>
    /// Moves an item to a target category and refreshes all fences.
    /// </summary>
    public void MoveItemToCategory(DesktopItem item, string? newCategoryId)
    {
        item.CategoryId = newCategoryId;

        foreach (var cat in _lastCategories)
        {
            if (cat.Id == newCategoryId)
            {
                if (!cat.ItemIds.Contains(item.Id))
                    cat.ItemIds.Add(item.Id);
            }
            else
            {
                cat.ItemIds.Remove(item.Id);
                if (!string.IsNullOrEmpty(item.FullPath))
                {
                    cat.ItemIds.Remove(item.FullPath);
                    cat.ItemIds.Remove(DesktopScanner.GetDeterministicId(item.FullPath));
                }
            }
        }

        App.ConfigManager.SaveConfig(App.Config);
        RefreshAllFences();
    }

    /// <summary>
    /// Merges a tab into another fence window and updates configuration.
    /// </summary>
    public void MergeTab(FenceTab tab, FenceWindow targetWindow)
    {
        var sourceWindow = _fenceWindows.FirstOrDefault(w => w.Config.Tabs.Any(t => t.Id == tab.Id));
        if (sourceWindow == null || sourceWindow == targetWindow) return;

        // Remove from source fence
        sourceWindow.Config.Tabs.RemoveAll(t => t.Id == tab.Id);

        // Add to target fence
        targetWindow.Config.Tabs.Add(tab);
        targetWindow.Config.ActiveTabIndex = targetWindow.Config.Tabs.Count - 1;

        if (sourceWindow.Config.Tabs.Count == 0)
        {
            _fenceWindows.Remove(sourceWindow);
            App.Config.Fences.RemoveAll(f => f.Id == sourceWindow.Config.Id);
            try { sourceWindow.Close(); } catch { }
        }
        else
        {
            if (sourceWindow.Config.ActiveTabIndex >= sourceWindow.Config.Tabs.Count)
                sourceWindow.Config.ActiveTabIndex = 0;
            sourceWindow.RenderTabs();
            sourceWindow.PopulateActiveTab();
        }

        targetWindow.RenderTabs();
        targetWindow.PopulateActiveTab();

        App.ConfigManager.SaveConfig(App.Config);
    }

    /// <summary>
    /// Detaches a tab from a fence into its own standalone fence window.
    /// </summary>
    public void DetachTab(FenceTab tab, FenceWindow fromWindow)
    {
        if (fromWindow.Config.Tabs.Count <= 1) return;

        fromWindow.Config.Tabs.RemoveAll(t => t.Id == tab.Id);
        if (fromWindow.Config.ActiveTabIndex >= fromWindow.Config.Tabs.Count)
            fromWindow.Config.ActiveTabIndex = 0;
        fromWindow.RenderTabs();
        fromWindow.PopulateActiveTab();

        var newConfig = new FenceConfig
        {
            Id = Guid.NewGuid().ToString(),
            Title = tab.Title,
            IconEmoji = tab.IconEmoji,
            IsFolderPortal = tab.IsFolderPortal,
            FolderPortalPath = tab.FolderPortalPath,
            CategoryId = tab.CategoryId,
            X = fromWindow.Left + 50,
            Y = fromWindow.Top + 50,
            Width = fromWindow.Width,
            Height = fromWindow.Height,
            BackgroundColorHex = fromWindow.Config.BackgroundColorHex,
            TitleBarColorHex = fromWindow.Config.TitleBarColorHex,
            Opacity = fromWindow.Config.Opacity,
            Shape = fromWindow.Config.Shape,
            CornerRadius = fromWindow.Config.CornerRadius,
            ChameleonMode = fromWindow.Config.ChameleonMode,
            AutoRollUpOnHover = fromWindow.Config.AutoRollUpOnHover,
            Tabs = new List<FenceTab> { tab }
        };

        App.Config.Fences.Add(newConfig);
        App.ConfigManager.SaveConfig(App.Config);

        var newWindow = new FenceWindow(newConfig, _lastAllItems, _lastCategories, this);
        newWindow.Show();
        newWindow.SetDesktopPosition();
        _fenceWindows.Add(newWindow);
    }

    /// <summary>
    /// Refreshes all open fence windows.
    /// </summary>
    public void RefreshAllFences()
    {
        foreach (var window in _fenceWindows)
        {
            window.InvalidateCache();
            window.RenderTabs();
            window.PopulateActiveTab();
        }
    }

    /// <summary>
    /// Attempts to resolve overlapping fences by nudging the moved window away from others.
    /// This is a simple strategy: for each overlapping fence, try shifting the moved window
    /// right, left, down or up until no intersection or until attempts exhausted.
    /// </summary>
    public void ResolveOverlap(FenceWindow movedWindow)
    {
        if (movedWindow == null) return;
        if (!App.Config.PreventFenceOverlap) return;

        var workArea = movedWindow.GetCurrentWorkArea();

        Rect movedRect = new Rect(movedWindow.Left, movedWindow.Top,
            movedWindow.ActualWidth > 0 ? movedWindow.ActualWidth : movedWindow.Width,
            movedWindow.ActualHeight > 0 ? movedWindow.ActualHeight : movedWindow.Height);
        // Improved strategy: search outward in a spiral for the nearest free position
        // that does not intersect any other fence and stays within the work area.
        var occupied = new List<Rect>();
        foreach (var other in _fenceWindows)
        {
            if (other == movedWindow) continue;
            var r = new Rect(other.Left, other.Top,
                other.ActualWidth > 0 ? other.ActualWidth : other.Width,
                other.ActualHeight > 0 ? other.ActualHeight : other.Height);
            occupied.Add(r);
        }

        bool IsFreeAt(double x, double y)
        {
            var candidate = new Rect(x, y, movedRect.Width, movedRect.Height);
            // must be inside work area
            if (candidate.Left < workArea.Left || candidate.Top < workArea.Top) return false;
            if (candidate.Right > workArea.Right || candidate.Bottom > workArea.Bottom) return false;
            foreach (var o in occupied)
            {
                if (candidate.IntersectsWith(o)) return false;
            }
            return true;
        }

        // If original position is already free (except self), nothing to do
        if (IsFreeAt(movedRect.X, movedRect.Y)) return;

        var original = new Point(movedRect.X, movedRect.Y);
        Point? best = null;
        double bestDistSq = double.MaxValue;

        const double step = 20.0; // pixel sampling step
        const double maxRadius = 600.0; // search radius

        // check increasing radii and several angles to find closest free spot
        for (double r = step; r <= maxRadius; r += step)
        {
            for (double deg = 0; deg < 360; deg += 22.5)
            {
                double rad = deg * Math.PI / 180.0;
                double nx = original.X + Math.Cos(rad) * r;
                double ny = original.Y + Math.Sin(rad) * r;

                // round to integer to avoid micro offsets
                nx = Math.Round(nx);
                ny = Math.Round(ny);

                if (!IsFreeAt(nx, ny)) continue;

                double dx = nx - original.X;
                double dy = ny - original.Y;
                double distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = new Point(nx, ny);
                }
            }

            // if we found a candidate at this radius, stop searching further (nearest found)
            if (best.HasValue) break;
        }

        if (best.HasValue)
        {
            var target = best.Value;
            movedWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                movedWindow.Left = target.X;
                movedWindow.Top = target.Y;
                movedWindow.SnapToEdges();
                movedWindow.Config.X = movedWindow.Left;
                movedWindow.Config.Y = movedWindow.Top;
                App.ConfigManager.SaveConfig(App.Config);
            }));
        }
    }

    /// <summary>
    /// Toggles desktop visibility on desktop double-click:
    /// - In Simple Mode: Toggles native desktop icons AND folder portals (clean wallpaper toggle).
    /// - In Fences Mode: Toggles category fences AND folder portals.
    /// </summary>
    public void ToggleVisibility()
    {
        _elementsVisible = !_elementsVisible;

        if (_currentMode == DisplayMode.SimpleOrganize)
        {
            if (_elementsVisible)
                _desktopListView.ShowDesktopIcons();
            else
                _desktopListView.HideDesktopIcons();

            foreach (var window in _fenceWindows)
            {
                if (_elementsVisible)
                {
                    window.Show();
                    window.SetDesktopPosition();
                }
                else
                {
                    window.Hide();
                }
            }
        }
        else // DisplayMode.Fences
        {
            foreach (var window in _fenceWindows)
            {
                if (_elementsVisible)
                {
                    window.Show();
                    window.SetDesktopPosition();
                }
                else
                {
                    window.Hide();
                }
            }
        }

        App.Config.FencesHidden = !_elementsVisible;
    }

    public void ShowAll()
    {
        _elementsVisible = true;
        foreach (var window in _fenceWindows)
        {
            window.Show();
            window.SetDesktopPosition();
        }

        if (_currentMode == DisplayMode.SimpleOrganize)
            _desktopListView.ShowDesktopIcons();
        else
            _desktopListView.HideDesktopIcons();
    }

    public void HideAll()
    {
        _elementsVisible = false;
        foreach (var window in _fenceWindows)
            window.Hide();

        _desktopListView.HideDesktopIcons();
    }

    private void InstallDesktopHook()
    {
        _mouseHook?.Dispose();
        _mouseHook = new GlobalMouseHook();
        _mouseHook.DesktopDoubleClicked += (_, _) =>
        {
            Application.Current?.Dispatcher.BeginInvoke(ToggleVisibility);
        };

        try
        {
            _mouseHook.Install();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to install mouse hook: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _mouseHook?.Dispose();
        _desktopListView.ShowDesktopIcons();

        foreach (var window in _fenceWindows)
        {
            try { window.Close(); } catch { }
        }
        _fenceWindows.Clear();

        GC.SuppressFinalize(this);
    }

    ~FenceManager()
    {
        Dispose();
    }
}
