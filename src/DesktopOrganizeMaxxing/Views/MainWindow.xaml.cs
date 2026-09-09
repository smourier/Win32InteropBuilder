using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopOrganizeMaxxing.Fences;
using DesktopOrganizeMaxxing.Interop;
using DesktopOrganizeMaxxing.Models;
using DesktopOrganizeMaxxing.Services;

namespace DesktopOrganizeMaxxing.Views;

/// <summary>
/// Main application window with navigation and all management pages.
/// </summary>
public partial class MainWindow : Window
{
    private readonly DesktopScanner _scanner = new();
    private readonly AutoCategorizer _categorizer = new();
    private readonly IconPositioner _positioner = new();
    private readonly DesktopListView _desktopListView = new();
    private FenceManager? _fenceManager;

    private List<DesktopItem> _scannedItems = new();
    private FileSystemWatcher? _userDesktopWatcher;
    private FileSystemWatcher? _publicDesktopWatcher;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _recentNewItemEvents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<DesktopItem> _pendingNewItems = new();
    private DesktopItem? _currentPromptItem;

    // All page grids for navigation
    private Grid[] AllPages => new[] { PageWelcome, PageCategories, PageMode, PageLayouts, PageFences, PagePortals, PageSettings };
    private Button[] AllNavButtons => new[] { NavWelcome, NavCategories, NavMode, NavLayouts, NavFences, NavPortals, NavSettings };

    public MainWindow()
    {
        InitializeComponent();
        LoadSettings();
        _fenceManager = new FenceManager();

        // Clean up any orphaned fences whose category no longer exists
        bool purgedOrphaned = App.Config.Fences.RemoveAll(f => !f.IsFolderPortal &&
            !App.Config.Categories.Any(c => c.Id == f.CategoryId || c.Name.Equals(f.Title, StringComparison.OrdinalIgnoreCase)) &&
            !f.Tabs.Any(t => t.IsFolderPortal || App.Config.Categories.Any(c => c.Id == t.CategoryId || c.Name.Equals(t.Title, StringComparison.OrdinalIgnoreCase)))) > 0;
        if (purgedOrphaned)
        {
            App.ConfigManager.SaveConfig(App.Config);
        }

        // Auto-restore fences on startup if fences or config exist
        if (App.Config.Fences.Count > 0 || (App.Config.SetupCompleted && App.Config.DesktopItems.Count > 0))
        {
            _scannedItems = App.Config.DesktopItems;
            // Ensure icons are populated (icons are not serialized in JSON)
            foreach (var item in _scannedItems)
            {
                if (item.Icon == null && !string.IsNullOrEmpty(item.FullPath))
                    item.Icon = DesktopScanner.ExtractIcon(item.FullPath);
            }

            ScanStatus.Text = $"✅ {_scannedItems.Count} items loaded from saved config.";
            RefreshCategoryDisplay();
            RefreshLayoutList();

            // If fences mode was active, restore fences
            if (App.Config.ActiveMode == DisplayMode.Fences)
            {
                StartFencesMode();
            }
            else
            {
                // In Simple mode, ensure folder portals are shown
                _fenceManager.CreateFences(App.Config.Fences, _scannedItems, App.Config.Categories, DisplayMode.SimpleOrganize);
            }
        }
        else if (App.Config.Fences.Any(f => f.IsFolderPortal))
        {
            _fenceManager.CreateFences(App.Config.Fences, _scannedItems, App.Config.Categories, App.Config.ActiveMode);
        }

        SetupDesktopWatchers();

        Loaded += (_, _) =>
        {
            if (Environment.GetCommandLineArgs().Any(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase) || a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)))
            {
                Hide();
            }
        };
    }

    // ========== TITLE BAR ==========

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // Minimize to tray instead of closing
        Hide();
        NativeMethods.TrimProcessMemory();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            NativeMethods.TrimProcessMemory();
        }
    }

    // ========== NAVIGATION ==========

    private void ShowPage(Grid page, Button activeNav)
    {
        foreach (var p in AllPages) p.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;

        foreach (var btn in AllNavButtons)
            btn.Style = (Style)FindResource("NavButton");
        activeNav.Style = (Style)FindResource("NavButtonActive");
    }

    private void NavWelcome_Click(object sender, RoutedEventArgs e) => ShowPage(PageWelcome, NavWelcome);
    private void NavCategories_Click(object sender, RoutedEventArgs e) { ShowPage(PageCategories, NavCategories); RefreshCategoryDisplay(); }
    private void NavMode_Click(object sender, RoutedEventArgs e) { ShowPage(PageMode, NavMode); UpdateModeCards(); }
    private void NavLayouts_Click(object sender, RoutedEventArgs e) { ShowPage(PageLayouts, NavLayouts); RefreshLayoutList(); }
    private void NavFences_Click(object sender, RoutedEventArgs e) { ShowPage(PageFences, NavFences); RefreshFenceEditor(); }
    private void NavPortals_Click(object sender, RoutedEventArgs e) { ShowPage(PagePortals, NavPortals); RefreshPortalList(); }
    private void NavSettings_Click(object sender, RoutedEventArgs e) => ShowPage(PageSettings, NavSettings);

    // ========== DESKTOP SCAN ==========

    private void ScanDesktop_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _scannedItems = _scanner.ScanDesktop();
            App.Config.DesktopItems = _scannedItems;
            ScanStatus.Text = $"✅ Found {_scannedItems.Count} items on the desktop.";

            // Check for changes if there's a saved config
            if (App.Config.SetupCompleted)
            {
                var savedItems = App.Config.DesktopItems;
                var (added, removed) = App.ConfigManager.DetectChanges(_scannedItems, savedItems);
                if (added.Count > 0 || removed.Count > 0)
                {
                    ScanStatus.Text += $"\n📊 Changes detected: {added.Count} new, {removed.Count} removed.";
                }
            }
        }
        catch (Exception ex)
        {
            ScanStatus.Text = $"❌ Error scanning: {ex.Message}";
        }
    }

    // ========== CATEGORIZATION ==========

    private void AutoMode_Click(object sender, MouseButtonEventArgs e)
    {
        App.Config.CategorizationMode = CategorizationMode.Automatic;
        AutoModeCard.BorderBrush = (Brush)FindResource("PrimaryBrush");
        ManualModeCard.BorderBrush = (Brush)FindResource("BorderBrush");

        if (_scannedItems.Count > 0)
        {
            App.Config.Categories = _categorizer.Categorize(_scannedItems);
            ScanStatus.Text = $"✅ Auto-categorized {_scannedItems.Count} items into {App.Config.Categories.Count(c => c.ItemIds.Count > 0)} categories.";
        }
    }

    private void ManualMode_Click(object sender, MouseButtonEventArgs e)
    {
        App.Config.CategorizationMode = CategorizationMode.Manual;
        ManualModeCard.BorderBrush = (Brush)FindResource("PrimaryBrush");
        AutoModeCard.BorderBrush = (Brush)FindResource("BorderBrush");

        if (App.Config.Categories.Count == 0)
        {
            App.Config.Categories = AutoCategorizer.GetDefaultCategories();
        }

        ShowPage(PageCategories, NavCategories);
        RefreshCategoryDisplay();
    }

    private void AutoCategorize_Click(object sender, RoutedEventArgs e)
    {
        if (_scannedItems.Count == 0)
        {
            _scannedItems = _scanner.ScanDesktop();
            App.Config.DesktopItems = _scannedItems;
        }

        App.Config.Categories = _categorizer.Categorize(_scannedItems, App.Config.Categories);
        RefreshCategoryDisplay();
    }

    private void QuickApply_Click(object sender, RoutedEventArgs e)
    {
        // Scan → Categorize → Apply Mode 1
        _scannedItems = _scanner.ScanDesktop();
        App.Config.DesktopItems = _scannedItems;
        App.Config.Categories = _categorizer.Categorize(_scannedItems);
        App.Config.ActiveMode = DisplayMode.SimpleOrganize;
        App.Config.SetupCompleted = true;

        ApplySimpleMode();
        ScanStatus.Text = $"✅ Quick apply done! {_scannedItems.Count} items organized.";
        App.ConfigManager.SaveConfig(App.Config);
    }

    // ========== CATEGORY MANAGEMENT ==========

    private void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        var picker = new IconPickerWindow("📁", null) { Owner = this };
        string chosenEmoji = "📁";
        string? chosenPath = null;
        if (picker.ShowDialog() == true)
        {
            chosenEmoji = picker.SelectedEmoji;
            chosenPath = picker.SelectedCustomIconPath;
        }

        var newCat = new Category
        {
            Name = "New Category",
            IconEmoji = chosenEmoji,
            CustomIconPath = chosenPath,
            ColorHex = "#6C63FF",
            SortOrder = App.Config.Categories.Count
        };
        App.Config.Categories.Add(newCat);

        // If in Fences mode, create the corresponding fence window on desktop
        if (App.Config.ActiveMode == DisplayMode.Fences)
        {
            var newFence = new FenceConfig
            {
                CategoryId = newCat.Id,
                Title = newCat.Name,
                IconEmoji = newCat.IconEmoji,
                CustomIconPath = newCat.CustomIconPath,
                BackgroundColorHex = newCat.ColorHex,
                X = 100 + (App.Config.Categories.Count % 4) * 60,
                Y = 100 + (App.Config.Categories.Count % 3) * 60,
                Width = 380,
                Height = 280
            };
            App.Config.Fences.Add(newFence);
            _fenceManager ??= new FenceManager();
            _fenceManager.CreateFences(App.Config.Fences, _scannedItems, App.Config.Categories, DisplayMode.Fences);
        }

        App.ConfigManager.SaveConfig(App.Config);
        RefreshCategoryDisplay();
    }

    private void RefreshCategoryDisplay()
    {
        bool isEn = App.Config.Language == AppLanguage.English;
        CategoryList.Items.Clear();

        foreach (var category in App.Config.Categories.OrderBy(c => c.SortOrder))
        {
            var categoryItems = _scannedItems.Where(i => i.CategoryId == category.Id).ToList();

            // Category card width
            var card = new Border
            {
                Width = 330,
                Margin = new Thickness(0, 0, 16, 16),
                CornerRadius = new CornerRadius(12),
                Background = (Brush)FindResource("BgCardBrush"),
                BorderBrush = new SolidColorBrush(category.Color),
                BorderThickness = new Thickness(1, 4, 1, 1),
                Padding = new Thickness(16)
            };

            var stack = new StackPanel();

            // Category header
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

            // Interactive category icon button (click to open icon bank / import .ico)
            var iconBtn = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(6),
                Background = (Brush)FindResource("BgTertiaryBrush"),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Cliquer pour changer l'icône / importer un .ico"
            };

            if (!string.IsNullOrEmpty(category.CustomIconPath) && System.IO.File.Exists(category.CustomIconPath))
            {
                var imgSource = DesktopScanner.LoadCustomIcon(category.CustomIconPath);
                if (imgSource != null)
                {
                    iconBtn.Child = new Image { Source = imgSource, Width = 20, Height = 20 };
                }
                else
                {
                    iconBtn.Child = new TextBlock { Text = category.IconEmoji, FontSize = 18, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                }
            }
            else
            {
                iconBtn.Child = new TextBlock { Text = category.IconEmoji, FontSize = 18, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            }

            var curCategory = category;
            iconBtn.MouseLeftButtonDown += (_, _) =>
            {
                var picker = new IconPickerWindow(curCategory.IconEmoji, curCategory.CustomIconPath)
                {
                    Owner = this
                };
                if (picker.ShowDialog() == true)
                {
                    curCategory.IconEmoji = picker.SelectedEmoji;
                    curCategory.CustomIconPath = picker.SelectedCustomIconPath;

                    // Sync with associated fences/tabs
                    foreach (var f in App.Config.Fences)
                    {
                        if (f.CategoryId == curCategory.Id || f.Title.Equals(curCategory.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            f.IconEmoji = curCategory.IconEmoji;
                            f.CustomIconPath = curCategory.CustomIconPath;
                            foreach (var tab in f.Tabs.Where(t => t.CategoryId == curCategory.Id || t.Title.Equals(curCategory.Name, StringComparison.OrdinalIgnoreCase)))
                            {
                                tab.IconEmoji = curCategory.IconEmoji;
                                tab.CustomIconPath = curCategory.CustomIconPath;
                            }
                        }
                    }

                    App.ConfigManager.SaveConfig(App.Config);
                    RefreshCategoryDisplay();
                    _fenceManager?.RefreshAllFences();
                }
            };
            header.Children.Add(iconBtn);

            var nameBox = new TextBox
            {
                Text = category.Name,
                Style = (Style)FindResource("ModernTextBox"),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0)
            };
            var catId = category.Id;
            nameBox.LostFocus += (_, _) =>
            {
                var cat = App.Config.Categories.FirstOrDefault(c => c.Id == catId);
                if (cat != null)
                {
                    cat.Name = nameBox.Text;
                    foreach (var f in App.Config.Fences)
                    {
                        if (f.CategoryId == catId) f.Title = cat.Name;
                        foreach (var tab in f.Tabs.Where(t => t.CategoryId == catId))
                            tab.Title = cat.Name;
                    }
                    App.ConfigManager.SaveConfig(App.Config);
                    _fenceManager?.RefreshAllFences();
                }
            };
            header.Children.Add(nameBox);

            // Delete button
            var deleteBtn = new Button
            {
                Content = "🗑",
                Background = Brushes.Transparent,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 14,
                Margin = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(4),
                VerticalAlignment = VerticalAlignment.Center
            };
            deleteBtn.Click += (_, _) =>
            {
                App.Config.Categories.RemoveAll(c => c.Id == catId);
                foreach (var item in _scannedItems.Where(i => i.CategoryId == catId))
                    item.CategoryId = null;

                // Remove category tabs and remove fence if no tabs remain
                foreach (var f in App.Config.Fences)
                {
                    f.Tabs.RemoveAll(t => t.CategoryId == catId);
                }
                App.Config.Fences.RemoveAll(f => !f.IsFolderPortal && (f.CategoryId == catId || f.Tabs.Count == 0));

                App.ConfigManager.SaveConfig(App.Config);

                // Immediately update fences on desktop!
                _fenceManager?.CreateFences(App.Config.Fences, _scannedItems, App.Config.Categories, App.Config.ActiveMode);

                RefreshCategoryDisplay();
            };
            header.Children.Add(deleteBtn);

            stack.Children.Add(header);

            // Region selector
            var regionPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            regionPanel.Children.Add(new TextBlock
            {
                Text = isEn ? "Region: " : "Région : ",
                Style = (Style)FindResource("BodyText"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            var regionCombo = new ComboBox
            {
                FontSize = 12,
                ItemsSource = Enum.GetValues<DesktopRegion>(),
                SelectedItem = category.Region,
                Background = (Brush)FindResource("BgTertiaryBrush"),
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                Padding = new Thickness(6, 4, 6, 4)
            };
            regionCombo.SelectionChanged += (_, _) =>
            {
                var cat = App.Config.Categories.FirstOrDefault(c => c.Id == catId);
                if (cat != null && regionCombo.SelectedItem is DesktopRegion r)
                    cat.Region = r;
            };
            regionPanel.Children.Add(regionCombo);
            stack.Children.Add(regionPanel);

            // Item count
            stack.Children.Add(new TextBlock
            {
                Text = isEn ? $"{categoryItems.Count} item(s)" : $"{categoryItems.Count} élément(s)",
                Style = (Style)FindResource("BodyText"),
                FontSize = 12,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                Margin = new Thickness(0, 0, 0, 8)
            });

            // Scrollable item list with category switcher
            var itemsScroller = new ScrollViewer
            {
                MaxHeight = 200,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var itemsListStack = new StackPanel();

            foreach (var item in categoryItems)
            {
                var itemGrid = new Grid { Margin = new Thickness(0, 3, 0, 3), Background = Brushes.Transparent };
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(115) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

                var itemPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
                if (item.Icon != null)
                {
                    itemPanel.Children.Add(new Image { Source = item.Icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) });
                }
                itemPanel.Children.Add(new TextBlock
                {
                    Text = item.Name,
                    Style = (Style)FindResource("BodyText"),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = item.FullPath
                });
                Grid.SetColumn(itemPanel, 0);
                itemGrid.Children.Add(itemPanel);

                // Reassign category dropdown
                var catNames = App.Config.Categories.Select(c => $"{c.IconEmoji} {c.Name}").ToList();
                catNames.Add(isEn ? "❓ Unassigned" : "❓ Non classé");

                var moveCombo = new ComboBox
                {
                    ItemsSource = catNames,
                    SelectedItem = $"{category.IconEmoji} {category.Name}",
                    FontSize = 10,
                    Height = 24,
                    Background = (Brush)FindResource("BgTertiaryBrush"),
                    Foreground = (Brush)FindResource("TextPrimaryBrush"),
                    Padding = new Thickness(4, 1, 4, 1)
                };

                var curItem = item;
                var curCatId = catId;
                moveCombo.SelectionChanged += (_, _) =>
                {
                    if (moveCombo.SelectedIndex >= 0 && moveCombo.SelectedIndex < App.Config.Categories.Count)
                    {
                        var targetCat = App.Config.Categories[moveCombo.SelectedIndex];
                        if (targetCat.Id != curCatId)
                        {
                            curItem.CategoryId = targetCat.Id;
                            if (!targetCat.ItemIds.Contains(curItem.Id))
                                targetCat.ItemIds.Add(curItem.Id);

                            var oldCat = App.Config.Categories.FirstOrDefault(c => c.Id == curCatId);
                            oldCat?.ItemIds.Remove(curItem.Id);

                            App.ConfigManager.SaveConfig(App.Config);
                            RefreshCategoryDisplay();
                            _fenceManager?.RefreshAllFences();
                        }
                    }
                    else if (moveCombo.SelectedIndex == App.Config.Categories.Count)
                    {
                        // Uncategorized
                        curItem.CategoryId = null;
                        var oldCat = App.Config.Categories.FirstOrDefault(c => c.Id == curCatId);
                        oldCat?.ItemIds.Remove(curItem.Id);

                        App.ConfigManager.SaveConfig(App.Config);
                        RefreshCategoryDisplay();
                        _fenceManager?.RefreshAllFences();
                    }
                };

                Grid.SetColumn(moveCombo, 1);
                itemGrid.Children.Add(moveCombo);

                // Delete button
                var delItemBtn = new Button
                {
                    Content = "🗑",
                    ToolTip = isEn ? "Delete (Send to Recycle Bin)" : "Supprimer (vers la corbeille)",
                    Background = Brushes.Transparent,
                    Foreground = (Brush)FindResource("TextMutedBrush"),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    FontSize = 11,
                    Padding = new Thickness(4, 2, 4, 2),
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                delItemBtn.Click += (_, _) => DeleteItemToRecycleBin(curItem);
                Grid.SetColumn(delItemBtn, 2);
                itemGrid.Children.Add(delItemBtn);

                // Context menu & double click to launch
                itemGrid.ContextMenu = CreateItemContextMenu(curItem);
                itemGrid.MouseLeftButtonDown += (_, e) =>
                {
                    if (e.ClickCount == 2)
                    {
                        e.Handled = true;
                        LaunchDesktopItem(curItem);
                    }
                };

                itemsListStack.Children.Add(itemGrid);
            }

            itemsScroller.Content = itemsListStack;
            stack.Children.Add(itemsScroller);

            card.Child = stack;
            CategoryList.Items.Add(card);
        }

        // Uncategorized items
        var uncategorized = _scannedItems.Where(i => i.CategoryId == null).ToList();
        if (uncategorized.Count > 0)
        {
            var uncatCard = new Border
            {
                Width = 580,
                Margin = new Thickness(0, 0, 16, 16),
                CornerRadius = new CornerRadius(12),
                Background = (Brush)FindResource("BgCardBrush"),
                BorderBrush = (Brush)FindResource("WarningBrush"),
                BorderThickness = new Thickness(1, 4, 1, 1),
                Padding = new Thickness(16)
            };

            var uncatStack = new StackPanel();
            uncatStack.Children.Add(new TextBlock
            {
                Text = isEn ? "❓ Uncategorized Items" : "❓ Éléments non classés",
                Style = (Style)FindResource("H3"),
                Margin = new Thickness(0, 0, 0, 4)
            });
            uncatStack.Children.Add(new TextBlock
            {
                Text = isEn
                    ? $"{uncategorized.Count} items need assignment — use the dropdown to assign each one."
                    : $"{uncategorized.Count} élément(s) à classer — choisissez une catégorie dans le menu déroulant.",
                Style = (Style)FindResource("BodyText"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 12)
            });

            foreach (var item in uncategorized)
            {
                var itemGrid = new Grid { Margin = new Thickness(0, 3, 0, 3), Background = Brushes.Transparent };
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

                var itemPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
                if (item.Icon != null)
                    itemPanel.Children.Add(new Image { Source = item.Icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0) });
                itemPanel.Children.Add(new TextBlock
                {
                    Text = item.Name,
                    Style = (Style)FindResource("BodyText"),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = item.FullPath
                });
                Grid.SetColumn(itemPanel, 0);
                itemGrid.Children.Add(itemPanel);

                // Category assignment dropdown
                var catNames = new List<string> { isEn ? "— Select —" : "— Choisir —" };
                catNames.AddRange(App.Config.Categories.Select(c => $"{c.IconEmoji} {c.Name}"));
                var assignCombo = new ComboBox
                {
                    ItemsSource = catNames,
                    SelectedIndex = 0,
                    FontSize = 12,
                    Background = (Brush)FindResource("BgTertiaryBrush"),
                    Foreground = (Brush)FindResource("TextPrimaryBrush"),
                    Padding = new Thickness(6, 4, 6, 4)
                };
                var itemRef = item;
                assignCombo.SelectionChanged += (_, _) =>
                {
                    if (assignCombo.SelectedIndex > 0)
                    {
                        var targetCat = App.Config.Categories[assignCombo.SelectedIndex - 1];
                        itemRef.CategoryId = targetCat.Id;
                        if (!targetCat.ItemIds.Contains(itemRef.Id))
                            targetCat.ItemIds.Add(itemRef.Id);
                        App.ConfigManager.SaveConfig(App.Config);
                        RefreshCategoryDisplay();
                        _fenceManager?.RefreshAllFences();
                    }
                };
                Grid.SetColumn(assignCombo, 1);
                itemGrid.Children.Add(assignCombo);

                // Delete button
                var delItemBtn = new Button
                {
                    Content = "🗑",
                    ToolTip = isEn ? "Delete (Send to Recycle Bin)" : "Supprimer (vers la corbeille)",
                    Background = Brushes.Transparent,
                    Foreground = (Brush)FindResource("TextMutedBrush"),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    FontSize = 11,
                    Padding = new Thickness(4, 2, 4, 2),
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                delItemBtn.Click += (_, _) => DeleteItemToRecycleBin(itemRef);
                Grid.SetColumn(delItemBtn, 2);
                itemGrid.Children.Add(delItemBtn);

                // Context menu & double click to launch
                itemGrid.ContextMenu = CreateItemContextMenu(itemRef);
                itemGrid.MouseLeftButtonDown += (_, e) =>
                {
                    if (e.ClickCount == 2)
                    {
                        e.Handled = true;
                        LaunchDesktopItem(itemRef);
                    }
                };

                uncatStack.Children.Add(itemGrid);
            }

            uncatCard.Child = uncatStack;
            CategoryList.Items.Add(uncatCard);
        }
    }

    private ContextMenu CreateItemContextMenu(DesktopItem item)
    {
        var menu = new ContextMenu();
        bool isEn = App.Config.Language == AppLanguage.English;

        var openItem = new MenuItem { Header = isEn ? "🚀 Open" : "🚀 Ouvrir", FontWeight = FontWeights.SemiBold };
        openItem.Click += (_, _) => LaunchDesktopItem(item);
        menu.Items.Add(openItem);

        var locItem = new MenuItem { Header = isEn ? "📂 Open File Location" : "📂 Ouvrir l'emplacement du fichier" };
        locItem.Click += (_, _) => OpenDesktopItemLocation(item);
        menu.Items.Add(locItem);

        menu.Items.Add(new Separator());

        var deleteItem = new MenuItem
        {
            Header = isEn ? "🗑️ Delete (Send to Recycle Bin)" : "🗑️ Supprimer (vers la corbeille)",
            Foreground = Brushes.IndianRed
        };
        deleteItem.Click += (_, _) => DeleteItemToRecycleBin(item);
        menu.Items.Add(deleteItem);

        return menu;
    }

    private void LaunchDesktopItem(DesktopItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.FullPath)) return;
        Task.Run(() =>
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = item.FullPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error launching {item.FullPath}: {ex.Message}");
            }
        });
    }

    private void OpenDesktopItemLocation(DesktopItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.FullPath)) return;
        Task.Run(() =>
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{item.FullPath}\"", UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening item location {item.FullPath}: {ex.Message}");
            }
        });
    }

    private void DeleteItemToRecycleBin(DesktopItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.FullPath)) return;

        bool deleted = DesktopScanner.SendToRecycleBin(item.FullPath);
        if (deleted)
        {
            _scannedItems.RemoveAll(i => i.Id == item.Id || i.FullPath.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
            App.Config.DesktopItems.RemoveAll(i => i.Id == item.Id || i.FullPath.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));

            foreach (var cat in App.Config.Categories)
            {
                cat.ItemIds.Remove(item.Id);
            }

            App.ConfigManager.SaveConfig(App.Config);

            RefreshCategoryDisplay();
            _fenceManager?.CreateFences(App.Config.Fences, _scannedItems, App.Config.Categories, App.Config.ActiveMode);
        }
    }

    // ========== MODE SELECTION ==========

    private void UpdateModeCards()
    {
        var primary = (Brush)FindResource("PrimaryBrush");
        var border = (Brush)FindResource("BorderBrush");

        Mode1Card.BorderBrush = App.Config.ActiveMode == DisplayMode.SimpleOrganize ? primary : border;
        Mode2Card.BorderBrush = App.Config.ActiveMode == DisplayMode.Fences ? primary : border;
    }

    private void SelectMode1_Click(object sender, MouseButtonEventArgs e)
    {
        App.Config.ActiveMode = DisplayMode.SimpleOrganize;
        UpdateModeCards();
    }

    private void SelectMode2_Click(object sender, MouseButtonEventArgs e)
    {
        App.Config.ActiveMode = DisplayMode.Fences;
        UpdateModeCards();
    }

    private void ApplyMode_Click(object sender, RoutedEventArgs e)
    {
        App.Config.SetupCompleted = true;

        if (App.Config.ActiveMode == DisplayMode.SimpleOrganize)
        {
            ApplySimpleMode();
        }
        else if (App.Config.ActiveMode == DisplayMode.Fences)
        {
            StartFencesMode();
        }

        App.ConfigManager.SaveConfig(App.Config);
        MessageBox.Show("Mode appliqué avec succès !", "Desktop Organize Maxxing",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ========== MODE 1: SIMPLE ORGANIZE ==========

    private void ApplySimpleMode()
    {
        if (_scannedItems.Count == 0)
        {
            _scannedItems = _scanner.ScanDesktop();
            App.Config.DesktopItems = _scannedItems;
        }

        if (!_scannedItems.Any(i => i.CategoryId != null))
        {
            App.Config.Categories = _categorizer.Categorize(_scannedItems, App.Config.Categories);
        }

        if (!_positioner.Initialize())
        {
            MessageBox.Show("Could not access the desktop icon list. Please ensure Windows Explorer is running.",
                "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int moved = _positioner.ArrangeByCategories(_scannedItems, App.Config.Categories);
        ScanStatus.Text = $"✅ Mode simple appliqué : {moved} icônes organisées sur le bureau.";

        // In Simple mode: desktop icons are visible, folder portals remain visible on desktop
        _fenceManager ??= new FenceManager();
        _fenceManager.CreateFences(App.Config.Fences, _scannedItems, App.Config.Categories, DisplayMode.SimpleOrganize);
    }

    // ========== MODE 2: FENCES ==========

    private void StartFencesMode()
    {
        if (_scannedItems.Count == 0)
        {
            _scannedItems = _scanner.ScanDesktop();
            App.Config.DesktopItems = _scannedItems;
        }

        if (!_scannedItems.Any(i => i.CategoryId != null))
        {
            App.Config.Categories = _categorizer.Categorize(_scannedItems, App.Config.Categories);
        }

        // Deduplicate: If category C is inside a multi-tab fence, remove any standalone duplicate fence for C
        var tabbedCategoryIds = App.Config.Fences
            .Where(f => !f.IsFolderPortal && f.Tabs.Count > 1)
            .SelectMany(f => f.Tabs)
            .Select(t => t.CategoryId)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet();

        var tabbedTitles = App.Config.Fences
            .Where(f => !f.IsFolderPortal && f.Tabs.Count > 1)
            .SelectMany(f => f.Tabs)
            .Select(t => t.Title)
            .Where(t => !string.IsNullOrEmpty(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        App.Config.Fences.RemoveAll(f => !f.IsFolderPortal && f.Tabs.Count <= 1 &&
            ((f.CategoryId != null && tabbedCategoryIds.Contains(f.CategoryId)) || tabbedTitles.Contains(f.Title)));

        // Create fence configs for categories that are truly NOT yet represented in any fence or tab
        int offsetX = 50;
        int offsetY = 50;
        foreach (var cat in App.Config.Categories)
        {
            bool hasItems = _scannedItems.Any(i => i.CategoryId == cat.Id) || cat.ItemIds.Count > 0;
            if (!hasItems) continue;

            // Check if this category is ALREADY present in any fence (either as CategoryId, Title, or inside Tabs!)
            bool alreadyRepresented = App.Config.Fences.Any(f => !f.IsFolderPortal &&
                (f.CategoryId == cat.Id ||
                 f.Title.Equals(cat.Name, StringComparison.OrdinalIgnoreCase) ||
                 f.Tabs.Any(t => t.CategoryId == cat.Id || t.Title.Equals(cat.Name, StringComparison.OrdinalIgnoreCase))));

            if (alreadyRepresented)
                continue;

            App.Config.Fences.Add(new FenceConfig
            {
                CategoryId = cat.Id,
                Title = cat.Name,
                IconEmoji = cat.IconEmoji,
                CustomIconPath = cat.CustomIconPath,
                X = offsetX,
                Y = offsetY,
                Width = 380,
                Height = 280,
                BackgroundColorHex = cat.ColorHex
            });
            offsetX += 400;
            if (offsetX > 1200)
            {
                offsetX = 50;
                offsetY += 300;
            }
        }

        _fenceManager ??= new FenceManager();
        _fenceManager.CreateFences(App.Config.Fences, _scannedItems, App.Config.Categories, DisplayMode.Fences);
    }

    private void StopFencesMode()
    {
        _fenceManager?.CreateFences(App.Config.Fences, _scannedItems, App.Config.Categories, DisplayMode.SimpleOrganize);
    }

    // ========== LAYOUTS ==========

    private void SaveLayout_Click(object sender, RoutedEventArgs e)
    {
        string name = LayoutNameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Enter a layout name.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        App.ConfigManager.SaveLayout(App.Config, name);
        RefreshLayoutList();
        MessageBox.Show($"Layout '{name}' saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RefreshLayoutList()
    {
        bool isEn = App.Config.Language == AppLanguage.English;
        LayoutList.Items.Clear();
        foreach (var layout in App.Config.SavedLayouts.OrderByDescending(l => l.CreatedAt))
        {
            var card = new Border
            {
                Style = (Style)FindResource("CardPanel"),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel();
            info.Children.Add(new TextBlock
            {
                Text = $"💾 {layout.Name}",
                Style = (Style)FindResource("H3")
            });
            info.Children.Add(new TextBlock
            {
                Text = $"{layout.CreatedAt:g} • {layout.Mode} • {layout.Categories.Count} " + (isEn ? "categories" : "catégories"),
                Style = (Style)FindResource("BodyText"),
                FontSize = 12
            });
            Grid.SetColumn(info, 0);
            grid.Children.Add(info);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var loadId = layout.Id;
            var loadBtn = new Button { Content = isEn ? "Apply" : "Appliquer", Style = (Style)FindResource("ModernButton"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 8, 12, 8) };
            loadBtn.Click += (_, _) => LoadLayout(loadId);
            buttons.Children.Add(loadBtn);

            var delBtn = new Button { Content = "🗑", Style = (Style)FindResource("DangerButton"), Padding = new Thickness(10, 8, 10, 8) };
            delBtn.Click += (_, _) => { App.Config.SavedLayouts.RemoveAll(l => l.Id == loadId); App.ConfigManager.SaveConfig(App.Config); RefreshLayoutList(); };
            buttons.Children.Add(delBtn);

            Grid.SetColumn(buttons, 1);
            grid.Children.Add(buttons);

            card.Child = grid;
            LayoutList.Items.Add(card);
        }
    }

    private void LoadLayout(string layoutId)
    {
        var updated = App.ConfigManager.LoadLayout(App.Config, layoutId);
        if (updated == null)
        {
            MessageBox.Show("Layout not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        App.Config = updated;
        _scannedItems = App.Config.DesktopItems;

        // Re-apply
        if (App.Config.ActiveMode == DisplayMode.SimpleOrganize)
        {
            ApplySimpleMode();
        }
        else if (App.Config.ActiveMode == DisplayMode.Fences)
        {
            StartFencesMode();
        }

        RefreshCategoryDisplay();
        MessageBox.Show("Layout loaded!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ========== FENCE EDITOR ==========

    private void RefreshFenceEditor()
    {
        // Purge any orphaned fences whose category no longer exists
        bool purged = App.Config.Fences.RemoveAll(f => !f.IsFolderPortal &&
            !App.Config.Categories.Any(c => c.Id == f.CategoryId || c.Name.Equals(f.Title, StringComparison.OrdinalIgnoreCase)) &&
            !f.Tabs.Any(t => t.IsFolderPortal || App.Config.Categories.Any(c => c.Id == t.CategoryId || c.Name.Equals(t.Title, StringComparison.OrdinalIgnoreCase)))) > 0;

        if (purged)
        {
            App.ConfigManager.SaveConfig(App.Config);
            _fenceManager?.CreateFences(App.Config.Fences, _scannedItems, App.Config.Categories, App.Config.ActiveMode);
        }

        FenceEditorList.Items.Clear();
        foreach (var fence in App.Config.Fences)
        {
            var card = new Border
            {
                Style = (Style)FindResource("CardPanel"),
                Margin = new Thickness(0, 0, 0, 16)
            };

            var stack = new StackPanel();

            // Card Header with Title and Delete Fence Button
            var cardHeader = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            cardHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cardHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleText = new TextBlock
            {
                Text = $"{fence.IconEmoji} {fence.Title}",
                Style = (Style)FindResource("H3"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleText, 0);
            cardHeader.Children.Add(titleText);

            bool isEn = App.Config.Language == AppLanguage.English;

            var deleteFenceBtn = new Button
            {
                Content = isEn ? "🗑 Delete this Fence" : "🗑 Supprimer cette Fence",
                Style = (Style)FindResource("DangerButton"),
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            var fenceIdToDelete = fence.Id;
            deleteFenceBtn.Click += (_, _) =>
            {
                App.Config.Fences.RemoveAll(f => f.Id == fenceIdToDelete);
                App.ConfigManager.SaveConfig(App.Config);
                _fenceManager?.CreateFences(App.Config.Fences, _scannedItems, App.Config.Categories, App.Config.ActiveMode);
                RefreshFenceEditor();
            };
            Grid.SetColumn(deleteFenceBtn, 1);
            cardHeader.Children.Add(deleteFenceBtn);

            stack.Children.Add(cardHeader);

            // Shape selector
            var shapePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            shapePanel.Children.Add(new TextBlock { Text = isEn ? "Shape: " : "Forme : ", Style = (Style)FindResource("BodyText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var shapeCombo = new ComboBox
            {
                ItemsSource = Enum.GetValues<FenceShape>(),
                SelectedItem = fence.Shape,
                Width = 150,
                Background = (Brush)FindResource("BgTertiaryBrush"),
                Foreground = (Brush)FindResource("TextPrimaryBrush")
            };
            var fenceId = fence.Id;
            shapeCombo.SelectionChanged += (_, _) =>
            {
                var f = App.Config.Fences.FirstOrDefault(x => x.Id == fenceId);
                if (f != null && shapeCombo.SelectedItem is FenceShape s) f.Shape = s;
            };
            shapePanel.Children.Add(shapeCombo);
            stack.Children.Add(shapePanel);

            // Opacity slider
            var opacityPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            opacityPanel.Children.Add(new TextBlock { Text = isEn ? "Opacity: " : "Opacité : ", Style = (Style)FindResource("BodyText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var opacitySlider = new Slider { Minimum = 10, Maximum = 100, Value = fence.Opacity * 100, Width = 200 };
            var opacityLabel = new TextBlock { Text = $"{fence.Opacity * 100:0}%", Style = (Style)FindResource("BodyText"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            opacitySlider.ValueChanged += (_, _) =>
            {
                var f = App.Config.Fences.FirstOrDefault(x => x.Id == fenceId);
                if (f != null) f.Opacity = opacitySlider.Value / 100.0;
                opacityLabel.Text = $"{opacitySlider.Value:0}%";
            };
            opacityPanel.Children.Add(opacitySlider);
            opacityPanel.Children.Add(opacityLabel);
            stack.Children.Add(opacityPanel);

            // Corner radius
            var radiusPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            radiusPanel.Children.Add(new TextBlock { Text = isEn ? "Corner Radius: " : "Rayon des coins : ", Style = (Style)FindResource("BodyText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var radiusSlider = new Slider { Minimum = 0, Maximum = 50, Value = fence.CornerRadius, Width = 200 };
            radiusSlider.ValueChanged += (_, _) =>
            {
                var f = App.Config.Fences.FirstOrDefault(x => x.Id == fenceId);
                if (f != null) f.CornerRadius = radiusSlider.Value;
            };
            radiusPanel.Children.Add(radiusSlider);
            stack.Children.Add(radiusPanel);

            // Background color
            var colorPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            colorPanel.Children.Add(new TextBlock { Text = isEn ? "Background Color: " : "Couleur de fond : ", Style = (Style)FindResource("BodyText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });

            var colorPreview = new Border
            {
                Width = 28, Height = 28,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 8, 0),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1)
            };
            try { colorPreview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fence.BackgroundColorHex)); } catch { }

            var colorBox = new TextBox
            {
                Text = fence.BackgroundColorHex,
                Style = (Style)FindResource("ModernTextBox"),
                Width = 120,
                FontSize = 13
            };
            colorBox.LostFocus += (_, _) =>
            {
                var f = App.Config.Fences.FirstOrDefault(x => x.Id == fenceId);
                if (f != null)
                {
                    f.BackgroundColorHex = colorBox.Text;
                    try { colorPreview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorBox.Text)); } catch { }
                }
            };
            colorPanel.Children.Add(colorPreview);
            colorPanel.Children.Add(colorBox);
            stack.Children.Add(colorPanel);

            // Visual Color Palette Swatches
            var palette = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
            var presetColors = new[]
            {
                "#1E1E2E", "#11111B", "#2D2D3F", "#34495E",
                "#6C63FF", "#3498DB", "#00BCD4", "#1ABC9C",
                "#2ECC71", "#27AE60", "#F1C40F", "#E67E22",
                "#E74C3C", "#E91E63", "#9B59B6", "#8E44AD"
            };
            foreach (var hex in presetColors)
            {
                var swatch = new Border
                {
                    Width = 26,
                    Height = 26,
                    Margin = new Thickness(0, 0, 6, 6),
                    CornerRadius = new CornerRadius(5),
                    Cursor = Cursors.Hand,
                    BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    ToolTip = hex
                };
                try { swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); } catch { }

                var selectedHex = hex;
                swatch.MouseLeftButtonDown += (_, _) =>
                {
                    colorBox.Text = selectedHex;
                    colorPreview.Background = swatch.Background;
                    var f = App.Config.Fences.FirstOrDefault(x => x.Id == fenceId);
                    if (f != null)
                    {
                        f.BackgroundColorHex = selectedHex;
                        _fenceManager?.UpdateFences(App.Config.Fences);
                    }
                };
                palette.Children.Add(swatch);
            }
            stack.Children.Add(palette);

            // Auto roll-up on hover toggle
            var autoRollUpToggle = new CheckBox
            {
                Style = (Style)FindResource("ToggleSwitch"),
                Content = isEn ? "⚡ Auto Roll-Up on Hover (collapse when idle)" : "⚡ Repli automatique au survol (au repos)",
                IsChecked = fence.AutoRollUpOnHover,
                Margin = new Thickness(0, 0, 0, 8)
            };
            autoRollUpToggle.Click += (_, _) =>
            {
                var f = App.Config.Fences.FirstOrDefault(x => x.Id == fenceId);
                if (f != null)
                {
                    f.AutoRollUpOnHover = autoRollUpToggle.IsChecked == true;
                    _fenceManager?.UpdateFences(App.Config.Fences);
                    App.ConfigManager.SaveConfig(App.Config);
                }
            };
            stack.Children.Add(autoRollUpToggle);

            // Roll-up animation speed slider
            var speedPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            speedPanel.Children.Add(new TextBlock
            {
                Text = isEn ? "⏱️ Roll-Up Speed: " : "⏱️ Vitesse de repli : ",
                Style = (Style)FindResource("BodyText"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            var speedSlider = new Slider
            {
                Minimum = 0,
                Maximum = 600,
                Value = fence.RollUpAnimationDurationMs,
                Width = 160,
                TickFrequency = 25,
                IsSnapToTickEnabled = true,
                VerticalAlignment = VerticalAlignment.Center
            };
            var speedLabel = new TextBlock
            {
                Text = fence.RollUpAnimationDurationMs == 0 ? (isEn ? "0 ms (Instant)" : "0 ms (Instantané)") : $"{fence.RollUpAnimationDurationMs} ms",
                Style = (Style)FindResource("BodyText"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 130
            };
            speedSlider.ValueChanged += (_, _) =>
            {
                int val = (int)speedSlider.Value;
                var f = App.Config.Fences.FirstOrDefault(x => x.Id == fenceId);
                if (f != null)
                {
                    f.RollUpAnimationDurationMs = val;
                    App.ConfigManager.SaveConfig(App.Config);
                    _fenceManager?.UpdateFences(App.Config.Fences);
                }
                speedLabel.Text = val == 0 ? (isEn ? "0 ms (Instant)" : "0 ms (Instantané)") : $"{val} ms";
            };
            speedPanel.Children.Add(speedSlider);
            speedPanel.Children.Add(speedLabel);
            stack.Children.Add(speedPanel);

            // Overlap search step slider (global)
            var stepPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            stepPanel.Children.Add(new TextBlock
            {
                Text = isEn ? "🔎 Overlap step: " : "🔎 Pas de recherche d\'overlap : ",
                Style = (Style)FindResource("BodyText"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            var stepSlider = new Slider
            {
                Minimum = 1,
                Maximum = 40,
                Value = App.Config.OverlapSearchStep,
                Width = 160,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                VerticalAlignment = VerticalAlignment.Center
            };
            var stepLabel = new TextBlock
            {
                Text = $"{App.Config.OverlapSearchStep} px",
                Style = (Style)FindResource("BodyText"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 130
            };
            stepSlider.ValueChanged += (_, _) =>
            {
                int val = (int)stepSlider.Value;
                App.Config.OverlapSearchStep = val;
                App.ConfigManager.SaveConfig(App.Config);
                stepLabel.Text = $"{val} px";
            };
            stepPanel.Children.Add(stepSlider);
            stepPanel.Children.Add(stepLabel);
            stack.Children.Add(stepPanel);

            // Orientation selector
            var orientPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            orientPanel.Children.Add(new TextBlock
            {
                Text = isEn ? "📐 Orientation: " : "📐 Orientation : ",
                Style = (Style)FindResource("BodyText"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            var horizBtn = new Button
            {
                Content = "↔ Horizontal",
                Style = fence.Orientation == FenceOrientation.Horizontal ? (Style)FindResource("ModernButton") : (Style)FindResource("SecondaryButton"),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 6, 0)
            };

            var vertBtn = new Button
            {
                Content = isEn ? "↕ Vertical (side opening)" : "↕ Vertical (ouverture de côté)",
                Style = fence.Orientation == FenceOrientation.Vertical ? (Style)FindResource("ModernButton") : (Style)FindResource("SecondaryButton"),
                Padding = new Thickness(12, 6, 12, 6)
            };

            horizBtn.Click += (_, _) =>
            {
                var f = App.Config.Fences.FirstOrDefault(x => x.Id == fenceId);
                if (f != null)
                {
                    f.Orientation = FenceOrientation.Horizontal;
                    App.ConfigManager.SaveConfig(App.Config);
                    _fenceManager?.CreateFences(App.Config.Fences, _scannedItems, App.Config.Categories, App.Config.ActiveMode);
                    RefreshFenceEditor();
                }
            };

            vertBtn.Click += (_, _) =>
            {
                var f = App.Config.Fences.FirstOrDefault(x => x.Id == fenceId);
                if (f != null)
                {
                    f.Orientation = FenceOrientation.Vertical;
                    App.ConfigManager.SaveConfig(App.Config);
                    _fenceManager?.CreateFences(App.Config.Fences, _scannedItems, App.Config.Categories, App.Config.ActiveMode);
                    RefreshFenceEditor();
                }
            };

            orientPanel.Children.Add(horizBtn);
            orientPanel.Children.Add(vertBtn);
            stack.Children.Add(orientPanel);

            // Tab Titles toggle (show or hide tab text)
            var tabTitlesToggle = new CheckBox
            {
                Style = (Style)FindResource("ToggleSwitch"),
                Content = isEn ? "🏷️ Show tab titles (text title)" : "🏷️ Afficher les noms des onglets (titre texte)",
                IsChecked = fence.ShowTabTitles,
                Margin = new Thickness(0, 0, 0, 8)
            };
            tabTitlesToggle.Click += (_, _) =>
            {
                var f = App.Config.Fences.FirstOrDefault(x => x.Id == fenceId);
                if (f != null)
                {
                    f.ShowTabTitles = tabTitlesToggle.IsChecked == true;
                    App.ConfigManager.SaveConfig(App.Config);
                    _fenceManager?.CreateFences(App.Config.Fences, _scannedItems, App.Config.Categories, App.Config.ActiveMode);
                }
            };
            stack.Children.Add(tabTitlesToggle);

            // Toggles
            var chameleonToggle = new CheckBox
            {
                Style = (Style)FindResource("ToggleSwitch"),
                Content = isEn ? "🦎 Chameleon (hover) mode" : "🦎 Mode Caméléon (au survol)",
                IsChecked = fence.ChameleonMode,
                Margin = new Thickness(0, 0, 0, 8)
            };
            chameleonToggle.Click += (_, _) =>
            {
                var f = App.Config.Fences.FirstOrDefault(x => x.Id == fenceId);
                if (f != null)
                {
                    f.ChameleonMode = chameleonToggle.IsChecked == true;
                    _fenceManager?.UpdateFences(App.Config.Fences);
                    App.ConfigManager.SaveConfig(App.Config);
                }
            };
            stack.Children.Add(chameleonToggle);

            // Apply button
            var applyBtn = new Button
            {
                Content = isEn ? "Apply Changes" : "Appliquer les modifications",
                Style = (Style)FindResource("ModernButton"),
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(12, 8, 12, 8)
            };
            applyBtn.Click += (_, _) =>
            {
                _fenceManager?.UpdateFences(App.Config.Fences);
                App.ConfigManager.SaveConfig(App.Config);
            };
            stack.Children.Add(applyBtn);

            card.Child = stack;
            FenceEditorList.Items.Add(card);
        }
    }

    // ========== FOLDER PORTALS ==========

    private void AddPortal_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select a folder for the portal"
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var portalFence = new FenceConfig
            {
                IsFolderPortal = true,
                FolderPortalPath = dialog.SelectedPath,
                Title = System.IO.Path.GetFileName(dialog.SelectedPath),
                IconEmoji = "📂",
                X = 120,
                Y = 120,
                Width = 400,
                Height = 350
            };
            App.Config.Fences.Add(portalFence);
            App.ConfigManager.SaveConfig(App.Config);

            if (_fenceManager == null)
            {
                _fenceManager = new FenceManager();
            }
            _fenceManager.CreateFences(App.Config.Fences, _scannedItems, App.Config.Categories, App.Config.ActiveMode);

            RefreshPortalList();
        }
    }

    private void RefreshPortalList()
    {
        PortalList.Items.Clear();
        foreach (var fence in App.Config.Fences.Where(f => f.IsFolderPortal))
        {
            var card = new Border
            {
                Style = (Style)FindResource("CardPanel"),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel();
            info.Children.Add(new TextBlock
            {
                Text = $"📂 {fence.Title}",
                Style = (Style)FindResource("H3")
            });
            info.Children.Add(new TextBlock
            {
                Text = fence.FolderPortalPath ?? "",
                Style = (Style)FindResource("BodyText"),
                FontSize = 12
            });
            Grid.SetColumn(info, 0);
            grid.Children.Add(info);

            var fenceId = fence.Id;
            var delBtn = new Button { Content = "🗑", Style = (Style)FindResource("DangerButton"), Padding = new Thickness(10, 8, 10, 8), VerticalAlignment = VerticalAlignment.Center };
            delBtn.Click += (_, _) =>
            {
                App.Config.Fences.RemoveAll(f => f.Id == fenceId);
                App.ConfigManager.SaveConfig(App.Config);
                RefreshPortalList();
                _fenceManager?.CreateFences(App.Config.Fences, _scannedItems, App.Config.Categories, App.Config.ActiveMode);
            };
            Grid.SetColumn(delBtn, 1);
            grid.Children.Add(delBtn);

            card.Child = grid;
            PortalList.Items.Add(card);
        }
    }

    // ========== SETTINGS ==========

    private void LoadSettings()
    {
        LangEnglish.IsChecked = App.Config.Language == AppLanguage.English;
        LangFrench.IsChecked = App.Config.Language == AppLanguage.French;
        ThemeAuto.IsChecked = App.Config.Theme == AppThemeMode.Auto;
        ThemeDark.IsChecked = App.Config.Theme == AppThemeMode.Dark;
        ThemeLight.IsChecked = App.Config.Theme == AppThemeMode.Light;
        StartupToggle.IsChecked = App.Config.RunAtStartup;
        PromptOnNewItemToggle.IsChecked = App.Config.PromptOnNewDesktopItem;
        ApplyLanguage();
    }

    private void LanguageChanged(object sender, RoutedEventArgs e)
    {
        if (LangFrench.IsChecked == true)
            App.Config.Language = AppLanguage.French;
        else
            App.Config.Language = AppLanguage.English;

        App.ConfigManager.SaveConfig(App.Config);
        ApplyLanguage();
        _fenceManager?.RefreshAllFences();
    }

    private void ApplyLanguage()
    {
        bool isEn = App.Config.Language == AppLanguage.English;

        // Sidebar headers & buttons
        SidebarSetupHeader.Text = isEn ? "SETUP" : "CONFIGURATION";
        SidebarManageHeader.Text = isEn ? "MANAGE" : "GESTION";
        SidebarSettingsHeader.Text = isEn ? "SETTINGS" : "PARAMÈTRES";

        NavWelcome.Content = isEn ? "🏠  Welcome" : "🏠  Accueil";
        NavCategories.Content = isEn ? "📋  Categories" : "📋  Catégories";
        NavMode.Content = isEn ? "🎯  Display Mode" : "🎯  Mode d'affichage";
        NavLayouts.Content = isEn ? "💾  Saved Layouts" : "💾  Dispositions";
        NavFences.Content = isEn ? "🔲  Fence Editor" : "🔲  Éditeur de Fences";
        NavPortals.Content = isEn ? "📂  Folder Portals" : "📂  Portails de dossiers";
        NavSettings.Content = isEn ? "⚙️  Settings" : "⚙️  Paramètres";

        // PageWelcome
        WelcomeTitle.Text = isEn ? "Welcome to Desktop Organize Maxxing" : "Bienvenue sur Desktop Organize Maxxing";
        WelcomeSubtitle.Text = isEn ? "Intelligently organize your desktop icons into clean categories. Choose automatic detection or create your own." : "Organisez intelligemment les icônes de votre bureau en catégories nettes. Choisissez la détection automatique ou créez vos propres règles.";
        WelcomeScanTitle.Text = isEn ? "📡  Desktop Scan" : "📡  Scan du Bureau";
        WelcomeScanBtn.Content = isEn ? "🔍  Scan Desktop" : "🔍  Scanner le bureau";
        WelcomeRescanBtn.Content = isEn ? "Rescan" : "Re-scanner";
        WelcomeCatModeTitle.Text = isEn ? "🧠  Categorization Mode" : "🧠  Mode de Catégorisation";
        WelcomeAutoTitle.Text = isEn ? "⚡ Automatic" : "⚡ Automatique";
        WelcomeAutoDesc.Text = isEn ? "Let the app detect categories automatically based on file type, name, and known applications." : "L'application détecte automatiquement les catégories selon le type de fichier, le nom et les applications connues.";
        WelcomeManualTitle.Text = isEn ? "✋ Manual" : "✋ Manuel";
        WelcomeManualDesc.Text = isEn ? "Create your own categories and assign each item to the category of your choice." : "Créez vos propres catégories et assignez chaque élément à la catégorie de votre choix.";
        WelcomeQuickApplyTitle.Text = isEn ? "🚀  Quick Apply" : "🚀  Application Rapide";
        WelcomeQuickApplyDesc.Text = isEn ? "Scan → Auto-Categorize → Apply to desktop in one click." : "Scan → Auto-Catégorisation → Application sur le bureau en un clic.";
        WelcomeQuickApplyBtn.Content = isEn ? "⚡ Auto-Organize Now" : "⚡ Auto-Organiser Maintenant";

        // PageCategories
        CategoriesTitle.Text = isEn ? "Categories" : "Catégories";
        CategoriesSubtitle.Text = isEn ? "Manage categories and assign desktop items to them." : "Gérez vos catégories et assignez-y les éléments de votre bureau.";
        AddCategoryBtn.Content = isEn ? "➕ Add Category" : "➕ Ajouter une catégorie";
        AutoCategorizeBtn.Content = isEn ? "🔄 Auto-Categorize" : "🔄 Auto-Catégoriser";

        // PageMode
        ModeTitle.Text = isEn ? "Display Mode" : "Mode d'Affichage";
        ModeSubtitle.Text = isEn ? "Choose how your organized icons are displayed on the desktop." : "Choisissez comment vos icônes organisées sont affichées sur le bureau.";
        Mode1Title.Text = isEn ? "Simple Organize" : "Organisation Simple";
        Mode1Desc.Text = isEn ? "Repositions your native desktop icons by category. No visual overlays — just clean icon arrangement." : "Repositionne vos icônes natives du bureau par catégorie. Aucun panneau visuel — juste un alignement propre des icônes.";
        Mode1Bullet1.Text = isEn ? "✓ Lightweight" : "✓ Ultra-léger";
        Mode1Bullet2.Text = isEn ? "✓ No extra processes" : "✓ Aucun processus supplémentaire";
        Mode1Bullet3.Text = isEn ? "✓ Save & restore layouts" : "✓ Sauvegarde & restauration de dispositions";
        Mode2Title.Text = isEn ? "Fences Mode" : "Mode Fences";
        Mode2Desc.Text = isEn ? "Creates visual fence panels on your desktop. Full customization of colors, shapes, transparency, and effects." : "Crée des panneaux Fences visuels sur votre bureau. Personnalisation complète des couleurs, formes, transparence et effets.";
        Mode2Bullet1.Text = isEn ? "✓ Custom shapes & colors" : "✓ Formes & couleurs sur-mesure";
        Mode2Bullet2.Text = isEn ? "✓ Chameleon hover effect" : "✓ Effet Caméléon au survol";
        Mode2Bullet3.Text = isEn ? "✓ Roll-up & hide on double-click" : "✓ Repli auto & escamotage";
        Mode2Bullet4.Text = isEn ? "✓ Folder Portals" : "✓ Portails de dossiers";
        ApplyModeBtn.Content = isEn ? "✅ Apply Selected Mode" : "✅ Appliquer le Mode Sélectionné";

        // PageLayouts
        LayoutsTitle.Text = isEn ? "Saved Layouts" : "Dispositions Sauvegardées";
        LayoutsSubtitle.Text = isEn ? "Save your current arrangement and restore it later. New or removed icons are handled automatically." : "Sauvegardez votre arrangement actuel et restaurez-le plus tard. Les icônes ajoutées ou supprimées sont gérées automatiquement.";
        SaveLayoutHeader.Text = isEn ? "💾 Save Current Layout" : "💾 Sauvegarder la disposition actuelle";
        SaveLayoutBtn.Content = isEn ? "Save" : "Sauvegarder";

        // PageFences
        FenceEditorTitle.Text = isEn ? "Fence Editor" : "Éditeur de Fences";
        FenceEditorSubtitle.Text = isEn ? "Customize the appearance of each fence panel." : "Personnalisez l'apparence et le comportement de chaque panneau Fence.";

        // PagePortals
        PortalsTitle.Text = isEn ? "Folder Portals" : "Portails de Dossiers";
        PortalsSubtitle.Text = isEn ? "Create portals to browse folder contents directly on your desktop without opening File Explorer." : "Créez des portails pour parcourir le contenu de vos dossiers directement sur votre bureau sans ouvrir l'Explorateur.";
        AddPortalBtn.Content = isEn ? "➕ Add Folder Portal" : "➕ Ajouter un portail de dossier";

        // Settings page
        SettingsTitle.Text = isEn ? "Settings" : "Paramètres";
        SettingsLangTitle.Text = isEn ? "🌐 Language" : "🌐 Langue";
        SettingsThemeTitle.Text = isEn ? "🎨 Theme" : "🎨 Thème";
        ThemeAuto.Content = isEn ? "Auto" : "Automatique";
        ThemeDark.Content = isEn ? "Dark" : "Sombre";
        ThemeLight.Content = isEn ? "Light" : "Clair";
        SettingsStartupTitle.Text = isEn ? "🚀 Startup" : "🚀 Démarrage";
        StartupToggle.Content = isEn ? "Run at Windows startup" : "Lancer au démarrage de Windows";
        SettingsNewItemTitle.Text = isEn ? "✨ New Desktop Items" : "✨ Nouveaux éléments du bureau";
        PromptOnNewItemToggle.Content = isEn ? "Open app to organize new desktop apps/files" : "Ouvrir l'application pour classer les nouveaux éléments";
        SettingsAboutTitle.Text = isEn ? "ℹ️ About" : "ℹ️ À propos";
        SettingsAboutAuthor.Text = isEn ? "Created by Igrek" : "Créé par Igrek";
        SettingsAboutQuote.Text = "« organize your desktop to ascend and lower your cortisol and stop crymaxx »";
        SettingsAboutDesc.Text = isEn ? "Intelligent desktop icon organization." : "Organisation intelligente des icônes de bureau.";

        // New item modal
        NewItemTitle.Text = isEn ? "New shortcut or app detected" : "Nouveau raccourci ou application détecté";
        NewItemSubtitle.Text = isEn ? "Choose which Fence or category to place it in:" : "Choisissez dans quelle Fence ou catégorie le placer :";
        NewItemDestinationLabel.Text = isEn ? "📁 Destination (Fence / Category):" : "📁 Destination (Fence / Catégorie) :";
        NewItemDismissBtn.Content = isEn ? "Keep as is" : "Laisser tel quel";
        NewItemAssignBtn.Content = isEn ? "Place in Category" : "Placer dans la catégorie";

        // Refresh dynamic pages
        RefreshCategoryDisplay();
        RefreshFenceEditor();
        RefreshPortalList();
        RefreshLayoutList();
    }

    private void ThemeChanged(object sender, RoutedEventArgs e)
    {
        if (ThemeDark.IsChecked == true) App.Config.Theme = AppThemeMode.Dark;
        else if (ThemeLight.IsChecked == true) App.Config.Theme = AppThemeMode.Light;
        else App.Config.Theme = AppThemeMode.Auto;

        App.ApplyTheme(App.Config.Theme);
        App.ConfigManager.SaveConfig(App.Config);
    }

    private void StartupToggle_Click(object sender, RoutedEventArgs e)
    {
        App.Config.RunAtStartup = StartupToggle.IsChecked == true;
        ConfigManager.SetStartupEnabled(App.Config.RunAtStartup);
        App.ConfigManager.SaveConfig(App.Config);
    }

    private void PromptOnNewItemToggle_Click(object sender, RoutedEventArgs e)
    {
        App.Config.PromptOnNewDesktopItem = PromptOnNewItemToggle.IsChecked == true;
        App.ConfigManager.SaveConfig(App.Config);
    }

    // ========== DESKTOP NEW ITEM WATCHER & PLACEMENT PROMPT ==========

    private void SetupDesktopWatchers()
    {
        try
        {
            string userDesk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (System.IO.Directory.Exists(userDesk))
            {
                _userDesktopWatcher = new FileSystemWatcher(userDesk)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                _userDesktopWatcher.Created += (s, e) => OnDesktopFileDiscovered(e.FullPath, isPublic: false);
                _userDesktopWatcher.Renamed += (s, e) => OnDesktopFileDiscovered(e.FullPath, isPublic: false);
            }

            string pubDesk = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (System.IO.Directory.Exists(pubDesk))
            {
                _publicDesktopWatcher = new FileSystemWatcher(pubDesk)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                _publicDesktopWatcher.Created += (s, e) => OnDesktopFileDiscovered(e.FullPath, isPublic: true);
                _publicDesktopWatcher.Renamed += (s, e) => OnDesktopFileDiscovered(e.FullPath, isPublic: true);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error setting up desktop watchers: {ex.Message}");
        }
    }

    private void OnDesktopFileDiscovered(string fullPath, bool isPublic)
    {
        if (!App.Config.PromptOnNewDesktopItem) return;
        if (string.IsNullOrWhiteSpace(fullPath)) return;

        string fileName = System.IO.Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(fileName)) return;

        // Ignore desktop.ini, temporary, partial downloads, and swap files
        if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) return;
        string ext = System.IO.Path.GetExtension(fullPath).ToLowerInvariant();
        if (ext is ".tmp" or ".crdownload" or ".part" or ".download" or ".lock" or ".swp") return;
        if (fileName.StartsWith("~$") || fileName.StartsWith(".~")) return;

        // Debounce: ignore events for the same file within 2 seconds
        var now = DateTime.UtcNow;
        if (_recentNewItemEvents.TryGetValue(fullPath, out var lastTime) && (now - lastTime).TotalSeconds < 2)
            return;
        _recentNewItemEvents[fullPath] = now;

        // Wait a moment so the file is completely written by Windows or the installer
        Task.Run(async () =>
        {
            await Task.Delay(800);

            if (!System.IO.File.Exists(fullPath) && !System.IO.Directory.Exists(fullPath))
                return;

            string detId = DesktopScanner.GetDeterministicId(fullPath);
            if (_scannedItems.Any(i => i.Id == detId || i.FullPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase)))
                return;

            var item = _scanner.CreateDesktopItem(fullPath, isPublic);
            if (item == null) return;

            Dispatcher.Invoke(() =>
            {
                EnqueueNewItemPrompt(item);
            });
        });
    }

    private void EnqueueNewItemPrompt(DesktopItem item)
    {
        if (_currentPromptItem?.FullPath.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase) == true)
            return;
        if (_pendingNewItems.Any(p => p.FullPath.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase)))
            return;

        _pendingNewItems.Enqueue(item);

        if (_currentPromptItem == null)
        {
            ShowNextPendingItemPrompt();
        }
    }

    private void ShowNextPendingItemPrompt()
    {
        if (_pendingNewItems.Count == 0)
        {
            _currentPromptItem = null;
            NewItemOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        _currentPromptItem = _pendingNewItems.Dequeue();

        // Restore and bring MainWindow to foreground
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();

        // Set item details in card
        NewItemIcon.Source = _currentPromptItem.Icon;
        NewItemName.Text = _currentPromptItem.Name;
        NewItemPath.Text = _currentPromptItem.FullPath;

        // Detect suggested category
        string suggestedCatName = _categorizer.DetectCategory(_currentPromptItem);

        // Populate categories combo
        NewItemCategoryCombo.Items.Clear();
        int selectedIndex = 0;
        int idx = 0;

        foreach (var cat in App.Config.Categories)
        {
            var comboItem = new ComboBoxItem
            {
                Content = $"{cat.IconEmoji}  {cat.Name}",
                Tag = cat.Id
            };
            NewItemCategoryCombo.Items.Add(comboItem);

            if (cat.Name.Equals(suggestedCatName, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = idx;
            }
            idx++;
        }

        if (NewItemCategoryCombo.Items.Count > 0)
            NewItemCategoryCombo.SelectedIndex = selectedIndex;

        NewItemOverlay.Visibility = Visibility.Visible;
    }

    private void NewItemAssign_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPromptItem == null)
        {
            ShowNextPendingItemPrompt();
            return;
        }

        var selected = NewItemCategoryCombo.SelectedItem as ComboBoxItem;
        string? catId = selected?.Tag as string;

        if (!string.IsNullOrEmpty(catId))
        {
            _currentPromptItem.CategoryId = catId;
            var cat = App.Config.Categories.FirstOrDefault(c => c.Id == catId);
            if (cat != null && !cat.ItemIds.Contains(_currentPromptItem.Id))
            {
                cat.ItemIds.Add(_currentPromptItem.Id);
            }
        }

        if (!_scannedItems.Any(i => i.Id == _currentPromptItem.Id))
        {
            _scannedItems.Add(_currentPromptItem);
        }
        if (!App.Config.DesktopItems.Any(i => i.Id == _currentPromptItem.Id))
        {
            App.Config.DesktopItems.Add(_currentPromptItem);
        }

        App.ConfigManager.SaveConfig(App.Config);
        _fenceManager?.RefreshAllFences();

        // If Fences mode is active, hide native desktop icons
        if (App.Config.ActiveMode == DisplayMode.Fences)
        {
            _desktopListView.HideDesktopIcons();
        }

        ShowNextPendingItemPrompt();
    }

    private void NewItemDismiss_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPromptItem != null)
        {
            if (!_scannedItems.Any(i => i.Id == _currentPromptItem.Id))
            {
                _scannedItems.Add(_currentPromptItem);
            }
            if (!App.Config.DesktopItems.Any(i => i.Id == _currentPromptItem.Id))
            {
                App.Config.DesktopItems.Add(_currentPromptItem);
            }
            App.ConfigManager.SaveConfig(App.Config);
        }

        ShowNextPendingItemPrompt();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _userDesktopWatcher?.Dispose();
        _publicDesktopWatcher?.Dispose();
    }
}
