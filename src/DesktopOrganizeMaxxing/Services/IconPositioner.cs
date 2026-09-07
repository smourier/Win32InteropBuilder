namespace DesktopOrganizeMaxxing.Services;

using DesktopOrganizeMaxxing.Interop;
using DesktopOrganizeMaxxing.Models;

/// <summary>
/// Mode 1 service: Repositions desktop icons by category into grid regions.
/// Uses the desktop ListView to move icons to calculated positions.
/// </summary>
public class IconPositioner
{
    private readonly DesktopListView _desktopListView;

    // Icon grid spacing - natural Windows desktop proportions
    private const int IconWidth = 96;
    private const int IconHeight = 104;
    private const int Padding = 16;
    private const int CategoryGap = 20;

    public IconPositioner()
    {
        _desktopListView = new DesktopListView();
    }

    /// <summary>
    /// Initializes the icon positioner by finding the desktop ListView.
    /// </summary>
    public bool Initialize()
    {
        return _desktopListView.Initialize();
    }

    /// <summary>
    /// Gets the current positions of all desktop icons.
    /// </summary>
    public List<(string name, int x, int y)> GetCurrentPositions()
    {
        return _desktopListView.GetAllItems();
    }

    /// <summary>
    /// Repositions all categorized items on the desktop according to their category's region.
    /// Returns the number of items successfully repositioned.
    /// </summary>
    public int ArrangeByCategories(List<DesktopItem> items, List<Category> categories)
    {
        _desktopListView.EnsureAutoArrangeDisabled();

        int screenWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        int screenHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);

        // Subtract taskbar height (approximate)
        int workHeight = screenHeight - 48;
        int movedCount = 0;

        foreach (var category in categories.OrderBy(c => c.SortOrder))
        {
            var categoryItems = items
                .Where(i => i.CategoryId == category.Id)
                .ToList();

            if (categoryItems.Count == 0) continue;

            // Calculate the region's bounding box
            var (regionX, regionY, regionWidth, regionHeight) = GetRegionBounds(
                category.Region, screenWidth, workHeight);

            // Calculate compact square block layout for the category (e.g. 4 -> 2x2, 9 -> 3x3, 6 -> 3x2)
            int count = categoryItems.Count;
            int cols = count switch
            {
                <= 1 => 1,
                <= 4 => 2,
                <= 6 => 3,
                <= 9 => 3,
                <= 12 => 4,
                _ => Math.Max(2, (int)Math.Ceiling(Math.Sqrt(count)))
            };

            for (int i = 0; i < categoryItems.Count; i++)
            {
                var item = categoryItems[i];
                int col = i % cols;
                int row = i / cols;

                int x = regionX + Padding + col * IconWidth;
                int y = regionY + Padding + row * IconHeight;

                // Find the icon in the desktop ListView and move it
                int index = _desktopListView.FindItemByName(item.Name, item.FullPath);
                if (index >= 0)
                {
                    _desktopListView.SetItemPosition(index, x, y);
                    item.PositionX = x;
                    item.PositionY = y;
                    movedCount++;
                }
            }
        }

        _desktopListView.RefreshDesktop();
        return movedCount;
    }

    /// <summary>
    /// Restores icon positions from a saved layout.
    /// </summary>
    public int RestorePositions(List<DesktopItem> savedItems)
    {
        _desktopListView.EnsureAutoArrangeDisabled();
        int restoredCount = 0;

        foreach (var item in savedItems)
        {
            int index = _desktopListView.FindItemByName(item.Name, item.FullPath);
            if (index >= 0)
            {
                _desktopListView.SetItemPosition(index, item.PositionX, item.PositionY);
                restoredCount++;
            }
        }

        _desktopListView.RefreshDesktop();
        return restoredCount;
    }

    /// <summary>
    /// Calculates the bounding box for a desktop region.
    /// </summary>
    private static (int x, int y, int width, int height) GetRegionBounds(
        DesktopRegion region, int screenWidth, int screenHeight)
    {
        int thirdW = screenWidth / 3;
        int thirdH = screenHeight / 3;

        return region switch
        {
            DesktopRegion.TopLeft => (0, 0, thirdW, thirdH),
            DesktopRegion.TopCenter => (thirdW, 0, thirdW, thirdH),
            DesktopRegion.TopRight => (thirdW * 2, 0, thirdW, thirdH),
            DesktopRegion.MiddleLeft => (0, thirdH, thirdW, thirdH),
            DesktopRegion.Center => (thirdW, thirdH, thirdW, thirdH),
            DesktopRegion.MiddleRight => (thirdW * 2, thirdH, thirdW, thirdH),
            DesktopRegion.BottomLeft => (0, thirdH * 2, thirdW, thirdH),
            DesktopRegion.BottomCenter => (thirdW, thirdH * 2, thirdW, thirdH),
            DesktopRegion.BottomRight => (thirdW * 2, thirdH * 2, thirdW, thirdH),
            _ => (0, 0, screenWidth, screenHeight)
        };
    }
}
