using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using DesktopOrganizeMaxxing.Interop;
using DesktopOrganizeMaxxing.Models;
using DesktopOrganizeMaxxing.Services;

namespace DesktopOrganizeMaxxing.Fences;

/// <summary>
/// Transparent desktop fence window supporting:
/// - Native 8-direction border & corner resizing via WM_NCHITTEST
/// - Multi-tab fences (category & folder portal tabs) with tab switching and drag-merging
/// - Drag-and-drop desktop items between fences to reassign categories
/// - Auto roll-up on hover (stays rolled up at rest, unrolls on mouse hover)
/// - Horizontal and Vertical orientations (opening downwards or sideways)
/// - Aero Snap / Edge magnetic snapping to screen borders
/// - Chameleon hover opacity mode
/// </summary>
public partial class FenceWindow : Window
{
    private readonly FenceConfig _config;
    private readonly List<DesktopItem> _allItems;
    private readonly List<Category> _categories;
    private readonly FenceManager? _manager;

    private bool _isRolledUp;
    private bool _isAnimating;
    private bool _openUpward;
    private double _expandedHeight = 280;
    private double _expandedWidth = 380;
    private FileSystemWatcher? _folderWatcher;
    private HwndSource? _hwndSource;

    private readonly Dictionary<string, List<DesktopItem>> _tabItemsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _tabCountCache = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _folderLoadCts;
    private static readonly SolidColorBrush ItemHoverBrush;
    private static readonly SolidColorBrush TabCountBrush;

    static FenceWindow()
    {
        ItemHoverBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
        ItemHoverBrush.Freeze();
        TabCountBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
        TabCountBrush.Freeze();
    }

    public string FenceId => _config.Id;
    public FenceConfig Config => _config;
    public FenceManager? Manager => _manager;

    public void InvalidateCache(string? key = null)
    {
        if (key != null)
        {
            _tabItemsCache.Remove(key);
            _tabCountCache.Remove(key);
        }
        else
        {
            _tabItemsCache.Clear();
            _tabCountCache.Clear();
        }
    }

    public FenceWindow(FenceConfig config, List<DesktopItem> allItems, List<Category>? categories = null, FenceManager? manager = null)
    {
        InitializeComponent();
        _config = config;
        _allItems = allItems;
        _categories = categories ?? App.Config.Categories;
        _manager = manager;

        _config.EnsureDefaultTab();

        if (_config.Height < 150)
            _config.Height = 280;
        if (_config.Width < 180)
            _config.Width = 380;

        _expandedHeight = _config.Height;
        _expandedWidth = _config.Width;
        _isRolledUp = config.AutoRollUpOnHover || config.IsRolledUp;
        _openUpward = config.OpenUpward;
    }

    public Rect GetCurrentWorkArea()
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero)
            {
                IntPtr hMonitor = NativeMethods.MonitorFromWindow(helper.Handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
                if (hMonitor != IntPtr.Zero)
                {
                    var mi = new NativeMethods.MONITORINFO();
                    mi.cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>();
                    if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                    {
                        var source = PresentationSource.FromVisual(this);
                        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                        return new Rect(
                            mi.rcWork.Left / dpiX,
                            mi.rcWork.Top / dpiY,
                            (mi.rcWork.Right - mi.rcWork.Left) / dpiX,
                            (mi.rcWork.Bottom - mi.rcWork.Top) / dpiY);
                    }
                }
            }
        }
        catch { }

        return SystemParameters.WorkArea;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Position and size
        Left = _config.X;
        Top = _config.Y;

        var workArea = GetCurrentWorkArea();

        if (_config.Orientation == FenceOrientation.Vertical)
        {
            Width = Math.Max(_config.Width, 260);
            Height = Math.Max(_config.Height, 320);
            _expandedWidth = Width;
            _expandedHeight = Height;

            bool isDockedRight = (Left + Width / 2) > (workArea.Left + workArea.Width / 2);

            double collapsedW = _config.ShowTabTitles ? 56 : 42;
            if (_config.AutoRollUpOnHover || _config.IsRolledUp)
            {
                _isRolledUp = true;
                Width = collapsedW;
                if (isDockedRight)
                    Left = workArea.Right - collapsedW;
                ContentScroller.Visibility = Visibility.Collapsed;
                RollUpBtn.Content = isDockedRight ? "◀" : "▶";
            }
            else
            {
                _isRolledUp = false;
                Width = _expandedWidth;
                if (isDockedRight)
                    Left = workArea.Right - _expandedWidth;
                ContentScroller.Visibility = Visibility.Visible;
                RollUpBtn.Content = isDockedRight ? "▶" : "◀";
            }
        }
        else
        {
            Width = Math.Max(_config.Width, 200);
            Height = Math.Max(_config.Height, 200);
            _expandedWidth = Width;
            _expandedHeight = Height;

            if (_config.AutoRollUpOnHover || _config.IsRolledUp)
            {
                _isRolledUp = true;
                Height = 40;
                ContentScroller.Visibility = Visibility.Collapsed;
                RollUpBtn.Content = _openUpward ? "▲" : "▼";
            }
            else
            {
                _isRolledUp = false;
                Height = _expandedHeight;
                ContentScroller.Visibility = Visibility.Visible;
                RollUpBtn.Content = _openUpward ? "▼" : "▲";
            }
        }

        ApplyOrientationLayout();
        UpdateResizeGripsVisibility();

        // Apply visual styling
        ApplyConfig();

        // Hook Win32 window messages for native border/corner resizing
        var helper = new WindowInteropHelper(this);
        if (helper.Handle != IntPtr.Zero)
        {
            _hwndSource = HwndSource.FromHwnd(helper.Handle);
            _hwndSource?.AddHook(WndProc);

            // Exclude Fence window from Alt+Tab and Task View
            try
            {
                int exStyle = NativeMethods.GetWindowLong(helper.Handle, NativeMethods.GWL_EXSTYLE).ToInt32();
                exStyle = (exStyle | NativeMethods.WS_EX_TOOLWINDOW) & ~NativeMethods.WS_EX_APPWINDOW;
                NativeMethods.SetWindowLong(helper.Handle, NativeMethods.GWL_EXSTYLE, (IntPtr)exStyle);
            }
            catch { }
        }

        // Keep config in sync on size/position changes
        SizeChanged += (_, _) =>
        {
            if (!_isAnimating && !_isRolledUp)
            {
                if (ActualWidth > 100)
                {
                    _config.Width = ActualWidth;
                    _expandedWidth = ActualWidth;
                }
                if (ActualHeight > 100)
                {
                    _config.Height = ActualHeight;
                    _expandedHeight = ActualHeight;
                }
            }
        };

        LocationChanged += (_, _) =>
        {
            if (!_isAnimating)
            {
                _config.X = Left;
                _config.Y = Top;
            }
        };

        // Render tabs and content
        RenderTabs();
        PopulateActiveTab();

        // Chameleon mode idle setup
        if (_config.ChameleonMode && !_config.AutoRollUpOnHover)
        {
            FenceBorder.BeginAnimation(OpacityProperty, null);
            FenceBorder.Opacity = _config.ChameleonIdleOpacity;
        }
        else
        {
            FenceBorder.BeginAnimation(OpacityProperty, null);
            FenceBorder.Opacity = 1.0;
        }

        // Initial snap check if placed near borders
        SnapToEdges(saveImmediately: false);

        // Keep pinned at bottom of desktop Z-order
        SetDesktopPosition();
    }

    /// <summary>
    /// Win32 window procedure to enable native 8-way border and corner resizing with Windows cursors.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_NCHITTEST && !_isRolledUp)
        {
            int x = unchecked((short)(lParam.ToInt32() & 0xFFFF));
            int y = unchecked((short)((lParam.ToInt32() >> 16) & 0xFFFF));
            Point pt = PointFromScreen(new Point(x, y));

            const int b = 8; // 8-pixel resize boundary
            bool left = pt.X <= b;
            bool right = pt.X >= ActualWidth - b;
            bool top = pt.Y <= b;
            bool bottom = pt.Y >= ActualHeight - b;

            if (top && left) { handled = true; return (IntPtr)NativeMethods.HTTOPLEFT; }
            if (top && right) { handled = true; return (IntPtr)NativeMethods.HTTOPRIGHT; }
            if (bottom && left) { handled = true; return (IntPtr)NativeMethods.HTBOTTOMLEFT; }
            if (bottom && right) { handled = true; return (IntPtr)NativeMethods.HTBOTTOMRIGHT; }
            if (left) { handled = true; return (IntPtr)NativeMethods.HTLEFT; }
            if (right) { handled = true; return (IntPtr)NativeMethods.HTRIGHT; }
            if (top) { handled = true; return (IntPtr)NativeMethods.HTTOP; }
            if (bottom) { handled = true; return (IntPtr)NativeMethods.HTBOTTOM; }
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Applies visual configuration (colors, shape, opacity) to the fence.
    /// </summary>
    public void ApplyConfig()
    {
        try
        {
            var bgColor = (Color)ColorConverter.ConvertFromString(_config.BackgroundColorHex);
            FenceBackground.Color = bgColor;
            FenceBackground.Opacity = _config.Opacity;
        }
        catch { }

        try
        {
            var tbColor = (Color)ColorConverter.ConvertFromString(_config.TitleBarColorHex);
            TitleBarBackground.Color = tbColor;
        }
        catch { }

        FenceBorder.CornerRadius = _config.Shape switch
        {
            FenceShape.Rectangle => new CornerRadius(0),
            FenceShape.RoundedRectangle => new CornerRadius(_config.CornerRadius),
            FenceShape.Circle => new CornerRadius(Math.Min(Width, Height) / 2),
            _ => new CornerRadius(12)
        };

        double r = _config.CornerRadius > 0 ? _config.CornerRadius : 12;
        if (_config.Shape == FenceShape.Rectangle)
        {
            TitleBar.CornerRadius = new CornerRadius(0);
        }
        else if (_isRolledUp)
        {
            TitleBar.CornerRadius = new CornerRadius(r);
        }
        else if (_config.Orientation == FenceOrientation.Horizontal && _openUpward)
        {
            TitleBar.CornerRadius = new CornerRadius(0, 0, r, r);
        }
        else
        {
            TitleBar.CornerRadius = new CornerRadius(r, r, 0, 0);
        }
        TitleBarBg.CornerRadius = TitleBar.CornerRadius;

        // Ensure opacity is 1.0 and clear any animation if chameleon mode is disabled
        if (!_config.ChameleonMode)
        {
            FenceBorder.BeginAnimation(OpacityProperty, null);
            FenceBorder.Opacity = 1.0;
        }
        else if (!IsMouseOver && !_config.AutoRollUpOnHover)
        {
            FenceBorder.BeginAnimation(OpacityProperty, null);
            FenceBorder.Opacity = _config.ChameleonIdleOpacity;
        }
    }

    /// <summary>
    /// Renders tab pills in the fence header (TabsPanel).
    /// </summary>
    public void RenderTabs()
    {
        _config.EnsureDefaultTab();
        TabsPanel.Children.Clear();

        if (_config.ActiveTabIndex >= _config.Tabs.Count)
            _config.ActiveTabIndex = 0;

        for (int i = 0; i < _config.Tabs.Count; i++)
        {
            var tab = _config.Tabs[i];
            int tabIndex = i;
            bool isActive = (i == _config.ActiveTabIndex);

            // Compute count without blocking UI
            int count = 0;
            if (tab.IsFolderPortal)
            {
                if (!string.IsNullOrEmpty(tab.FolderPortalPath))
                {
                    if (_tabCountCache.TryGetValue(tab.FolderPortalPath, out int c))
                    {
                        count = c;
                    }
                    else if (_tabItemsCache.TryGetValue("folder:" + tab.FolderPortalPath, out var cachedList))
                    {
                        count = cachedList.Count;
                        _tabCountCache[tab.FolderPortalPath] = count;
                    }
                    else
                    {
                        string fPath = tab.FolderPortalPath;
                        Task.Run(() =>
                        {
                            try
                            {
                                if (Directory.Exists(fPath))
                                {
                                    int cnt = Directory.GetFileSystemEntries(fPath).Length;
                                    Dispatcher.InvokeAsync(() =>
                                    {
                                        _tabCountCache[fPath] = cnt;
                                    });
                                }
                            }
                            catch { }
                        });
                    }
                }
            }
            else
            {
                var cat = _categories.FirstOrDefault(c => c.Id == tab.CategoryId || c.Name.Equals(tab.Title, StringComparison.OrdinalIgnoreCase));
                var catId = tab.CategoryId ?? cat?.Id;
                count = _allItems.Count(item => item.CategoryId == catId || (cat != null && cat.ItemIds.Contains(item.Id)));
            }

            bool isVertical = _config.Orientation == FenceOrientation.Vertical;

            var tabBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = isVertical ? new Thickness(6, 6, 6, 6) : new Thickness(10, 4, 10, 4),
                Margin = isVertical ? new Thickness(0, 0, 0, 6) : new Thickness(0, 0, 4, 0),
                ToolTip = isVertical ? $"{tab.Title} ({count})" : null,
                Cursor = Cursors.Hand,
                Background = isActive
                    ? new SolidColorBrush(Color.FromArgb(90, 255, 255, 255))
                    : new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                BorderBrush = isActive
                    ? new SolidColorBrush(Color.FromArgb(160, 255, 255, 255))
                    : Brushes.Transparent,
                BorderThickness = new Thickness(1)
            };

            var stack = new StackPanel
            {
                Orientation = isVertical ? Orientation.Vertical : Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            var category = _categories.FirstOrDefault(c => c.Id == tab.CategoryId || c.Name.Equals(tab.Title, StringComparison.OrdinalIgnoreCase));
            string? customIcon = tab.CustomIconPath ?? category?.CustomIconPath;
            if (!string.IsNullOrEmpty(customIcon) && File.Exists(customIcon))
            {
                var iconImg = DesktopScanner.LoadCustomIcon(customIcon);
                if (iconImg != null)
                {
                    stack.Children.Add(new Image
                    {
                        Source = iconImg,
                        Width = 18,
                        Height = 18,
                        Margin = isVertical ? new Thickness(0) : new Thickness(0, 0, 6, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }
                else
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = tab.IconEmoji,
                        FontSize = 13,
                        Margin = isVertical ? new Thickness(0) : new Thickness(0, 0, 4, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }
            }
            else
            {
                stack.Children.Add(new TextBlock
                {
                    Text = tab.IconEmoji,
                    FontSize = 13,
                    Margin = isVertical ? new Thickness(0) : new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            if (_config.ShowTabTitles)
            {
                if (!isVertical)
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = tab.Title,
                        FontSize = 12,
                        FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal,
                        Foreground = Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    stack.Children.Add(new TextBlock
                    {
                        Text = $" ({count})",
                        FontSize = 10,
                        Foreground = TabCountBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(2, 0, 0, 0)
                    });
                }
                else
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = tab.Title,
                        FontSize = 10,
                        FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal,
                        Foreground = Brushes.White,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = 48,
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                }
            }

            tabBorder.Child = stack;

            // Click to activate tab
            tabBorder.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                _config.ActiveTabIndex = tabIndex;
                RenderTabs();
                PopulateActiveTab();
            };

            // Drag tab to merge onto another fence
            Point tabStartPoint = new Point();
            tabBorder.PreviewMouseLeftButtonDown += (_, pe) => tabStartPoint = pe.GetPosition(null);
            tabBorder.PreviewMouseMove += (_, pe) =>
            {
                if (pe.LeftButton == MouseButtonState.Pressed)
                {
                    Point cur = pe.GetPosition(null);
                    if (Math.Abs(cur.X - tabStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                        Math.Abs(cur.Y - tabStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                    {
                        DragDrop.DoDragDrop(tabBorder, new System.Windows.DataObject("FenceTab", tab), System.Windows.DragDropEffects.Move);
                    }
                }
            };

            // Tab context menu (e.g. Detach Tab if multi-tab)
            if (_config.Tabs.Count > 1)
            {
                bool isEn = App.Config.Language == AppLanguage.English;
                var tabMenu = new ContextMenu();
                var detachItem = new MenuItem { Header = isEn ? "📂 Detach Tab (New Fence)" : "📂 Détacher cet onglet (nouvelle fence)" };
                detachItem.Click += (_, _) => _manager?.DetachTab(tab, this);
                tabMenu.Items.Add(detachItem);
                tabBorder.ContextMenu = tabMenu;
            }

            TabsPanel.Children.Add(tabBorder);
        }
    }

    public FenceTab? GetActiveTab()
    {
        if (_config.Tabs.Count == 0) _config.EnsureDefaultTab();
        if (_config.ActiveTabIndex >= _config.Tabs.Count) _config.ActiveTabIndex = 0;
        return _config.Tabs.Count > 0 ? _config.Tabs[_config.ActiveTabIndex] : null;
    }

    /// <summary>
    /// Populates icons according to the currently active tab.
    /// </summary>
    public void PopulateActiveTab()
    {
        var tab = GetActiveTab();
        if (tab == null) return;

        if (tab.IsFolderPortal)
        {
            PopulateFolderPortal(tab.FolderPortalPath);
        }
        else
        {
            var cat = _categories.FirstOrDefault(c => c.Id == tab.CategoryId || c.Name.Equals(tab.Title, StringComparison.OrdinalIgnoreCase));
            var catId = tab.CategoryId ?? cat?.Id;

            string catKey = "cat:" + (catId ?? tab.Title);
            if (_tabItemsCache.TryGetValue(catKey, out var cachedItems))
            {
                PopulateCategoryIcons(cachedItems);
                return;
            }

            var items = _allItems.Where(i =>
                i.CategoryId == catId ||
                (cat != null && cat.ItemIds.Contains(i.Id)) ||
                (!string.IsNullOrEmpty(i.FullPath) && cat != null && cat.ItemIds.Contains(i.FullPath)) ||
                (!string.IsNullOrEmpty(i.FullPath) && cat != null && cat.ItemIds.Contains(DesktopScanner.GetDeterministicId(i.FullPath)))
            ).ToList();

            _tabItemsCache[catKey] = items;
            PopulateCategoryIcons(items);
        }
    }

    private void PopulateCategoryIcons(List<DesktopItem> items)
    {
        RenderItemsInPanel(items);
    }

    private async void PopulateFolderPortal(string? folderPath)
    {
        _folderLoadCts?.Cancel();
        _folderLoadCts?.Dispose();
        _folderLoadCts = new CancellationTokenSource();
        var token = _folderLoadCts.Token;

        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            IconsPanel.Children.Clear();
            return;
        }

        string cacheKey = "folder:" + folderPath;
        if (_tabItemsCache.TryGetValue(cacheKey, out var cachedItems))
        {
            RenderItemsInPanel(cachedItems);
            SetupFolderWatcher(folderPath);
            return;
        }

        try
        {
            var items = await Task.Run(() =>
            {
                var list = new List<DesktopItem>();
                if (!Directory.Exists(folderPath)) return list;

                var dirInfo = new DirectoryInfo(folderPath);
                var dirs = dirInfo.GetDirectories();
                foreach (var dir in dirs)
                {
                    if (token.IsCancellationRequested) return list;
                    if ((dir.Attributes & FileAttributes.Hidden) != 0) continue;
                    var icon = DesktopScanner.ExtractIcon(dir.FullName);
                    list.Add(new DesktopItem { Name = dir.Name, FullPath = dir.FullName, Icon = icon });
                }

                var files = dirInfo.GetFiles();
                foreach (var file in files)
                {
                    if (token.IsCancellationRequested) return list;
                    if (file.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    if ((file.Attributes & FileAttributes.Hidden) != 0) continue;
                    var icon = DesktopScanner.ExtractIcon(file.FullName);
                    list.Add(new DesktopItem { Name = file.Name, FullPath = file.FullName, Icon = icon });
                }

                return list;
            }, token);

            if (token.IsCancellationRequested) return;

            _tabItemsCache[cacheKey] = items;
            _tabCountCache[folderPath] = items.Count;
            RenderItemsInPanel(items);
            SetupFolderWatcher(folderPath);
            _ = Task.Delay(1000).ContinueWith(_ => NativeMethods.TrimProcessMemory());
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error populating folder portal: {ex.Message}");
        }
    }

    private void RenderItemsInPanel(List<DesktopItem> items)
    {
        IconsPanel.Children.Clear();
        var panels = new List<UIElement>(items.Count);
        foreach (var item in items)
        {
            panels.Add(CreateIconPanel(item));
        }
        foreach (var p in panels)
        {
            IconsPanel.Children.Add(p);
        }
    }

    /// <summary>
    /// Creates an interactive icon panel for an item, supporting double-click launch and drag-and-drop to other fences.
    /// </summary>
    private Border CreateIconPanel(DesktopItem item)
    {
        if (item.Icon == null && !string.IsNullOrEmpty(item.FullPath))
        {
            item.Icon = DesktopScanner.ExtractIcon(item.FullPath);
        }

        var panel = new Border
        {
            Width = 84,
            Height = 94,
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = $"{item.Name}\n{item.FullPath}"
        };

        var stack = new StackPanel
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var image = new Image
        {
            Width = 44,
            Height = 44,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4)
        };
        if (item.Icon != null)
            image.Source = item.Icon;
        stack.Children.Add(image);

        stack.Children.Add(new TextBlock
        {
            Text = item.Name.Length > 13 ? item.Name[..12] + "…" : item.Name,
            FontSize = 11,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 80
        });

        panel.Child = stack;

        panel.MouseEnter += (_, _) => panel.Background = ItemHoverBrush;
        panel.MouseLeave += (_, _) => panel.Background = Brushes.Transparent;

        // Double-click to launch
        panel.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                e.Handled = true;
                LaunchItem(item);
            }
        };

        // Right-click context menu (Open, Open location, Move, Delete to Recycle Bin)
        panel.MouseRightButtonDown += (s, e) =>
        {
            e.Handled = true;
            ShowItemContextMenu(panel, item);
        };

        // Drag to move item to another fence
        Point iconStartPoint = new Point();
        panel.PreviewMouseLeftButtonDown += (_, e) => iconStartPoint = e.GetPosition(null);
        panel.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point cur = e.GetPosition(null);
                if (Math.Abs(cur.X - iconStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(cur.Y - iconStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    DragDrop.DoDragDrop(panel, new System.Windows.DataObject("DesktopItem", item), System.Windows.DragDropEffects.Move);
                }
            }
        };

        return panel;
    }

    private void ShowItemContextMenu(FrameworkElement target, DesktopItem item)
    {
        var menu = new ContextMenu();
        bool isEn = App.Config.Language == AppLanguage.English;

        // Open
        var openItem = new MenuItem
        {
            Header = isEn ? "🚀 Open" : "🚀 Ouvrir",
            FontWeight = FontWeights.SemiBold
        };
        openItem.Click += (_, _) => LaunchItem(item);
        menu.Items.Add(openItem);

        // Open file location
        var locItem = new MenuItem
        {
            Header = isEn ? "📂 Open File Location" : "📂 Ouvrir l'emplacement du fichier"
        };
        locItem.Click += (_, _) => OpenItemLocation(item);
        menu.Items.Add(locItem);

        var activeTab = GetActiveTab();
        if (activeTab != null && !activeTab.IsFolderPortal)
        {
            menu.Items.Add(new Separator());

            var moveMenu = new MenuItem { Header = isEn ? "🏷️ Move to Category" : "🏷️ Déplacer vers la catégorie" };
            foreach (var cat in _categories)
            {
                if (cat.Id == activeTab.CategoryId) continue;
                var catOption = new MenuItem { Header = $"{cat.IconEmoji} {cat.Name}" };
                var targetCatId = cat.Id;
                catOption.Click += (_, _) =>
                {
                    _manager?.MoveItemToCategory(item, targetCatId);
                    InvalidateCache();
                    PopulateActiveTab();
                    RenderTabs();
                };
                moveMenu.Items.Add(catOption);
            }
            var uncatOption = new MenuItem { Header = isEn ? "❓ Unassigned" : "❓ Non classé" };
            uncatOption.Click += (_, _) =>
            {
                _manager?.MoveItemToCategory(item, null);
                InvalidateCache();
                PopulateActiveTab();
                RenderTabs();
            };
            moveMenu.Items.Add(uncatOption);
            menu.Items.Add(moveMenu);
        }

        menu.Items.Add(new Separator());

        // Delete to Recycle Bin
        var deleteItem = new MenuItem
        {
            Header = isEn ? "🗑️ Delete (Send to Recycle Bin)" : "🗑️ Supprimer (vers la corbeille)",
            Foreground = Brushes.IndianRed
        };
        deleteItem.Click += (_, _) => DeleteItemToRecycleBin(item);
        menu.Items.Add(deleteItem);

        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    private void LaunchItem(DesktopItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.FullPath)) return;
        Task.Run(() =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = item.FullPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error launching {item.FullPath}: {ex.Message}");
            }
        });
    }

    private void OpenItemLocation(DesktopItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.FullPath)) return;
        Task.Run(() =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{item.FullPath}\"",
                    UseShellExecute = true
                });
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
            InvalidateCache();
            _allItems.RemoveAll(i => i.Id == item.Id || i.FullPath.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
            App.Config.DesktopItems.RemoveAll(i => i.Id == item.Id || i.FullPath.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));

            foreach (var cat in _categories)
            {
                cat.ItemIds.Remove(item.Id);
            }
            foreach (var cat in App.Config.Categories)
            {
                cat.ItemIds.Remove(item.Id);
            }

            App.ConfigManager.SaveConfig(App.Config);

            PopulateActiveTab();
            RenderTabs();
            _manager?.RefreshAllFences();
        }
    }

    // ========== DRAG & DROP HANDLING (APP ICONS & TABS) ==========

    private void Fence_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent("DesktopItem") || e.Data.GetDataPresent("FenceTab"))
        {
            e.Effects = System.Windows.DragDropEffects.Move;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Fence_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent("DesktopItem"))
        {
            var item = e.Data.GetData("DesktopItem") as DesktopItem;
            if (item != null)
            {
                var activeTab = GetActiveTab();
                if (activeTab != null && !activeTab.IsFolderPortal && activeTab.CategoryId != null)
                {
                    _manager?.MoveItemToCategory(item, activeTab.CategoryId);
                }
            }
        }
        else if (e.Data.GetDataPresent("FenceTab"))
        {
            var tab = e.Data.GetData("FenceTab") as FenceTab;
            if (tab != null && !_config.Tabs.Any(t => t.Id == tab.Id))
            {
                _manager?.MergeTab(tab, this);
            }
        }
    }

    // ========== HOVER & AUTO ROLL-UP BEHAVIOR ==========

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_config.AutoRollUpOnHover && _isRolledUp)
        {
            PerformUnroll();
        }

        if (_config.ChameleonMode)
        {
            AnimateOpacity(1.0, 200);
        }
        else
        {
            FenceBorder.BeginAnimation(OpacityProperty, null);
            FenceBorder.Opacity = 1.0;
        }
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_config.AutoRollUpOnHover && !_isRolledUp)
        {
            PerformRollUp();
        }

        if (_config.ChameleonMode)
        {
            AnimateOpacity(_config.ChameleonIdleOpacity, 400);
        }
        else
        {
            FenceBorder.BeginAnimation(OpacityProperty, null);
            FenceBorder.Opacity = 1.0;
        }
    }

    private void AnimateOpacity(double targetOpacity, int durationMs)
    {
        var anim = new DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new QuadraticEase()
        };
        FenceBorder.BeginAnimation(OpacityProperty, anim);
    }

    // ========== ROLL-UP ==========

    private void RollUp_Click(object sender, RoutedEventArgs e)
    {
        if (_isRolledUp)
            PerformUnroll();
        else
            PerformRollUp();
    }

    public void ApplyOrientationLayout()
    {
        var workArea = GetCurrentWorkArea();
        bool isDockedRight = (Left + ActualWidth / 2) > (workArea.Left + workArea.Width / 2);

        if (_config.Orientation == FenceOrientation.Vertical)
        {
            double barWidth = _config.ShowTabTitles ? 56 : 42;
            RowTopTitle.Height = new GridLength(0);
            ContentRow.Height = new GridLength(1, GridUnitType.Star);

            if (isDockedRight)
            {
                Col0.Width = new GridLength(1, GridUnitType.Star);
                Col1.Width = new GridLength(barWidth);

                Grid.SetRow(ContentScroller, 1);
                Grid.SetColumn(ContentScroller, 0);

                Grid.SetRow(TitleBar, 1);
                Grid.SetColumn(TitleBar, 1);

                TitleBar.CornerRadius = new CornerRadius(0, 12, 12, 0);
                TitleBarBg.CornerRadius = new CornerRadius(0, 12, 12, 0);
                RollUpBtn.Content = _isRolledUp ? "◀" : "▶";
            }
            else
            {
                Col0.Width = new GridLength(barWidth);
                Col1.Width = new GridLength(1, GridUnitType.Star);

                Grid.SetRow(TitleBar, 1);
                Grid.SetColumn(TitleBar, 0);

                Grid.SetRow(ContentScroller, 1);
                Grid.SetColumn(ContentScroller, 1);

                TitleBar.CornerRadius = new CornerRadius(12, 0, 0, 12);
                TitleBarBg.CornerRadius = new CornerRadius(12, 0, 0, 12);
                RollUpBtn.Content = _isRolledUp ? "▶" : "◀";
            }

            TitleBar.Width = barWidth;
            TitleBar.Height = double.NaN;

            TitleBarContentGrid.Margin = new Thickness(0, 6, 0, 6);
            TabsScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            TabsScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            TabsScroller.Margin = new Thickness(0, 0, 0, 32);
            TabsPanel.Orientation = Orientation.Vertical;
            TabsPanel.VerticalAlignment = VerticalAlignment.Top;
            TabsPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;

            RollUpBtn.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            RollUpBtn.VerticalAlignment = VerticalAlignment.Bottom;
            RollUpBtn.Margin = new Thickness(0, 0, 0, 4);
        }
        else
        {
            Col0.Width = new GridLength(1, GridUnitType.Star);
            Col1.Width = new GridLength(0);

            double r = _config.Shape == FenceShape.Rectangle ? 0 : (_config.CornerRadius > 0 ? _config.CornerRadius : 12);

            if (_openUpward)
            {
                // Inverted layout: Content on Top (Row 0), TitleBar on Bottom (Row 1)
                RowTopTitle.Height = new GridLength(1, GridUnitType.Star);
                ContentRow.Height = new GridLength(38);

                Grid.SetRow(ContentScroller, 0);
                Grid.SetColumn(ContentScroller, 0);
                ContentScroller.Margin = new Thickness(8, 8, 8, 4);

                Grid.SetRow(TitleBar, 1);
                Grid.SetColumn(TitleBar, 0);

                var cornerRad = _isRolledUp ? new CornerRadius(r) : new CornerRadius(0, 0, r, r);
                TitleBar.CornerRadius = cornerRad;
                TitleBarBg.CornerRadius = cornerRad;

                RollUpBtn.Content = _isRolledUp ? "▲" : "▼";
            }
            else
            {
                // Standard layout: TitleBar on Top (Row 0), Content on Bottom (Row 1)
                RowTopTitle.Height = new GridLength(38);
                ContentRow.Height = new GridLength(1, GridUnitType.Star);

                Grid.SetRow(TitleBar, 0);
                Grid.SetColumn(TitleBar, 0);

                Grid.SetRow(ContentScroller, 1);
                Grid.SetColumn(ContentScroller, 0);
                ContentScroller.Margin = new Thickness(8, 4, 8, 8);

                var cornerRad = _isRolledUp ? new CornerRadius(r) : new CornerRadius(r, r, 0, 0);
                TitleBar.CornerRadius = cornerRad;
                TitleBarBg.CornerRadius = cornerRad;

                RollUpBtn.Content = _isRolledUp ? "▼" : "▲";
            }

            TitleBar.Width = double.NaN;
            TitleBar.Height = 38;

            TitleBarContentGrid.Margin = new Thickness(6, 0, 6, 0);
            TabsScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            TabsScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            TabsScroller.Margin = new Thickness(0, 0, 32, 0);
            TabsPanel.Orientation = Orientation.Horizontal;
            TabsPanel.VerticalAlignment = VerticalAlignment.Center;
            TabsPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;

            RollUpBtn.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            RollUpBtn.VerticalAlignment = VerticalAlignment.Center;
            RollUpBtn.Margin = new Thickness(0, 0, 0, 0);
        }
    }

    public void SetOrientation(FenceOrientation newOrient)
    {
        if (_config.Orientation == newOrient) return;
        _config.Orientation = newOrient;

        if (newOrient == FenceOrientation.Vertical)
        {
            if (Height < Width)
            {
                double tmp = Width;
                Width = Math.Max(Height, 320);
                Height = Math.Max(tmp, 400);
            }
            _expandedWidth = Width;
            _expandedHeight = Height;
        }
        else
        {
            if (Width < Height)
            {
                double tmp = Height;
                Height = Math.Max(Width, 260);
                Width = Math.Max(tmp, 380);
            }
            _expandedWidth = Width;
            _expandedHeight = Height;
        }

        _config.Width = Width;
        _config.Height = Height;

        ApplyOrientationLayout();
        RenderTabs();
        PopulateActiveTab();
        SnapToEdges();
        UpdateResizeGripsVisibility();
        App.ConfigManager.SaveConfig(App.Config);
    }

    private int RollUpDurationMs => Math.Max(0, _config.RollUpAnimationDurationMs);

    private void PerformRollUp()
    {
        if (_isRolledUp || _isAnimating) return;

        int duration = RollUpDurationMs;

        if (_config.Orientation == FenceOrientation.Vertical)
        {
            if (ActualWidth > 100)
            {
                _expandedWidth = ActualWidth;
                _config.Width = ActualWidth;
            }

            _isRolledUp = true;
            _config.IsRolledUp = true;

            var workArea = GetCurrentWorkArea();
            bool isDockedRight = (Left + ActualWidth / 2) > (workArea.Left + workArea.Width / 2);
            double collapsedW = _config.ShowTabTitles ? 56 : 42;

            if (duration == 0)
            {
                Width = collapsedW;
                if (isDockedRight)
                    Left = workArea.Right - collapsedW;
                ContentScroller.Visibility = Visibility.Collapsed;
                RollUpBtn.Content = isDockedRight ? "◀" : "▶";
                UpdateResizeGripsVisibility();
                return;
            }

            _isAnimating = true;

            var widthAnim = new DoubleAnimation(collapsedW, TimeSpan.FromMilliseconds(duration))
            {
                EasingFunction = new QuadraticEase()
            };

            if (isDockedRight)
            {
                double targetLeft = workArea.Right - collapsedW;
                var leftAnim = new DoubleAnimation(targetLeft, TimeSpan.FromMilliseconds(duration))
                {
                    EasingFunction = new QuadraticEase()
                };
                BeginAnimation(LeftProperty, leftAnim);
            }

            widthAnim.Completed += (_, _) =>
            {
                BeginAnimation(WidthProperty, null);
                Width = collapsedW;
                if (isDockedRight)
                {
                    BeginAnimation(LeftProperty, null);
                    Left = workArea.Right - collapsedW;
                }
                _isAnimating = false;
                ContentScroller.Visibility = Visibility.Collapsed;
                RollUpBtn.Content = isDockedRight ? "◀" : "▶";
                UpdateResizeGripsVisibility();
            };
            BeginAnimation(WidthProperty, widthAnim);
        }
        else
        {
            if (ActualHeight > 100)
            {
                _expandedHeight = ActualHeight;
                _config.Height = ActualHeight;
            }

            _isRolledUp = true;
            _config.IsRolledUp = true;

            if (_openUpward)
            {
                // Collapses downward to bottom bar (bottom stays pinned)
                double currentBottom = Top + (ActualHeight > 0 ? ActualHeight : Height);
                double targetTop = currentBottom - 40;

                if (duration == 0)
                {
                    Height = 40;
                    Top = targetTop;
                    _config.Y = Top;
                    ContentScroller.Visibility = Visibility.Collapsed;
                    RollUpBtn.Content = "▲";
                    ApplyOrientationLayout();
                    UpdateResizeGripsVisibility();
                    return;
                }

                _isAnimating = true;

                var topAnim = new DoubleAnimation(targetTop, TimeSpan.FromMilliseconds(duration))
                {
                    EasingFunction = new QuadraticEase()
                };
                var heightAnim = new DoubleAnimation(40, TimeSpan.FromMilliseconds(duration))
                {
                    EasingFunction = new QuadraticEase()
                };

                heightAnim.Completed += (_, _) =>
                {
                    BeginAnimation(HeightProperty, null);
                    Height = 40;
                    BeginAnimation(TopProperty, null);
                    Top = targetTop;
                    _config.Y = Top;
                    _isAnimating = false;
                    ContentScroller.Visibility = Visibility.Collapsed;
                    RollUpBtn.Content = "▲";
                    ApplyOrientationLayout();
                    UpdateResizeGripsVisibility();
                };

                BeginAnimation(TopProperty, topAnim);
                BeginAnimation(HeightProperty, heightAnim);
            }
            else
            {
                if (duration == 0)
                {
                    Height = 40;
                    ContentScroller.Visibility = Visibility.Collapsed;
                    RollUpBtn.Content = "▼";
                    ApplyOrientationLayout();
                    UpdateResizeGripsVisibility();
                    return;
                }

                _isAnimating = true;

                var anim = new DoubleAnimation(40, TimeSpan.FromMilliseconds(duration))
                {
                    EasingFunction = new QuadraticEase()
                };
                anim.Completed += (_, _) =>
                {
                    BeginAnimation(HeightProperty, null);
                    Height = 40;
                    _isAnimating = false;
                    ContentScroller.Visibility = Visibility.Collapsed;
                    RollUpBtn.Content = "▼";
                    ApplyOrientationLayout();
                    UpdateResizeGripsVisibility();
                };
                BeginAnimation(HeightProperty, anim);
            }
        }
    }

    private void PerformUnroll()
    {
        if (!_isRolledUp || _isAnimating) return;

        int duration = RollUpDurationMs;

        if (_config.Orientation == FenceOrientation.Vertical)
        {
            _isRolledUp = false;
            _config.IsRolledUp = false;
            ContentScroller.Visibility = Visibility.Visible;
            UpdateResizeGripsVisibility();

            double targetWidth = Math.Max(_expandedWidth, Math.Max(_config.Width, 260));
            var workArea = GetCurrentWorkArea();
            bool isDockedRight = (Left + ActualWidth / 2) > (workArea.Left + workArea.Width / 2);

            if (duration == 0)
            {
                Width = targetWidth;
                if (isDockedRight)
                    Left = workArea.Right - targetWidth;
                RollUpBtn.Content = isDockedRight ? "▶" : "◀";
                return;
            }

            _isAnimating = true;

            var widthAnim = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(duration))
            {
                EasingFunction = new QuadraticEase()
            };

            if (isDockedRight)
            {
                double targetLeft = workArea.Right - targetWidth;
                var leftAnim = new DoubleAnimation(targetLeft, TimeSpan.FromMilliseconds(duration))
                {
                    EasingFunction = new QuadraticEase()
                };
                BeginAnimation(LeftProperty, leftAnim);
            }

            widthAnim.Completed += (_, _) =>
            {
                BeginAnimation(WidthProperty, null);
                Width = targetWidth;
                if (isDockedRight)
                {
                    BeginAnimation(LeftProperty, null);
                    Left = workArea.Right - targetWidth;
                }
                _isAnimating = false;
                RollUpBtn.Content = isDockedRight ? "▶" : "◀";
            };
            BeginAnimation(WidthProperty, widthAnim);
        }
        else
        {
            CheckAndAdjustOpenDirection();

            _isRolledUp = false;
            _config.IsRolledUp = false;
            ContentScroller.Visibility = Visibility.Visible;
            UpdateResizeGripsVisibility();

            double targetHeight = Math.Max(_expandedHeight, Math.Max(_config.Height, 220));

            if (_openUpward)
            {
                // Expands UPWARD from bottom bar
                double currentBottom = Top + (ActualHeight > 0 ? ActualHeight : Height);
                double targetTop = currentBottom - targetHeight;
                var workArea = GetCurrentWorkArea();
                if (targetTop < workArea.Top)
                {
                    targetTop = workArea.Top;
                    targetHeight = currentBottom - targetTop;
                }

                if (duration == 0)
                {
                    Height = targetHeight;
                    Top = targetTop;
                    _config.Y = Top;
                    _config.Height = targetHeight;
                    RollUpBtn.Content = "▼";
                    ApplyOrientationLayout();
                    return;
                }

                _isAnimating = true;

                var topAnim = new DoubleAnimation(targetTop, TimeSpan.FromMilliseconds(duration))
                {
                    EasingFunction = new QuadraticEase()
                };
                var heightAnim = new DoubleAnimation(targetHeight, TimeSpan.FromMilliseconds(duration))
                {
                    EasingFunction = new QuadraticEase()
                };

                heightAnim.Completed += (_, _) =>
                {
                    BeginAnimation(HeightProperty, null);
                    Height = targetHeight;
                    BeginAnimation(TopProperty, null);
                    Top = targetTop;
                    _config.Y = Top;
                    _config.Height = targetHeight;
                    _isAnimating = false;
                    RollUpBtn.Content = "▼";
                    ApplyOrientationLayout();
                };

                BeginAnimation(TopProperty, topAnim);
                BeginAnimation(HeightProperty, heightAnim);
            }
            else
            {
                if (duration == 0)
                {
                    Height = targetHeight;
                    _config.Height = targetHeight;
                    RollUpBtn.Content = "▲";
                    ApplyOrientationLayout();
                    return;
                }

                _isAnimating = true;

                var anim = new DoubleAnimation(targetHeight, TimeSpan.FromMilliseconds(duration))
                {
                    EasingFunction = new QuadraticEase()
                };
                anim.Completed += (_, _) =>
                {
                    BeginAnimation(HeightProperty, null);
                    Height = targetHeight;
                    _config.Height = targetHeight;
                    _isAnimating = false;
                    RollUpBtn.Content = "▲";
                    ApplyOrientationLayout();
                };
                BeginAnimation(HeightProperty, anim);
            }
        }
    }

    // ========== NATIVE RESIZING (8-DIRECTIONS) ==========

    private void Resize_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (sender is FrameworkElement element && element.Tag is string tagStr && int.TryParse(tagStr, out int dir))
        {
            if (_isRolledUp)
            {
                if (_config.Orientation == FenceOrientation.Horizontal && dir != 1 && dir != 2) return;
                if (_config.Orientation == FenceOrientation.Vertical && dir != 3 && dir != 6) return;
            }

            e.Handled = true;
            Mouse.Capture(null);

            var helper = new WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero)
            {
                NativeMethods.SendMessage(helper.Handle, 0x0112 /* WM_SYSCOMMAND */, (IntPtr)(0xF000 + dir), IntPtr.Zero);

                _config.Width = ActualWidth;
                _config.Height = ActualHeight;
                if (_config.Orientation == FenceOrientation.Vertical)
                {
                    if (!_isRolledUp && ActualWidth > 100) _expandedWidth = ActualWidth;
                    if (ActualHeight > 100) _expandedHeight = ActualHeight;
                }
                else
                {
                    if (ActualWidth > 100) _expandedWidth = ActualWidth;
                    if (!_isRolledUp && ActualHeight > 100) _expandedHeight = ActualHeight;
                }
                SnapToEdges();
                App.ConfigManager.SaveConfig(App.Config);
            }
        }
    }

    private void UpdateResizeGripsVisibility()
    {
        if (_isRolledUp)
        {
            if (_config.Orientation == FenceOrientation.Vertical)
            {
                if (ResizeTop != null) ResizeTop.Visibility = Visibility.Visible;
                if (ResizeBottom != null) ResizeBottom.Visibility = Visibility.Visible;
                if (ResizeTopLeft != null) ResizeTopLeft.Visibility = Visibility.Collapsed;
                if (ResizeTopRight != null) ResizeTopRight.Visibility = Visibility.Collapsed;
                if (ResizeBottomLeft != null) ResizeBottomLeft.Visibility = Visibility.Collapsed;
                if (ResizeBottomRight != null) ResizeBottomRight.Visibility = Visibility.Collapsed;
                if (ResizeLeft != null) ResizeLeft.Visibility = Visibility.Collapsed;
                if (ResizeRight != null) ResizeRight.Visibility = Visibility.Collapsed;
            }
            else
            {
                if (ResizeTop != null) ResizeTop.Visibility = Visibility.Collapsed;
                if (ResizeBottom != null) ResizeBottom.Visibility = Visibility.Collapsed;
                if (ResizeTopLeft != null) ResizeTopLeft.Visibility = Visibility.Collapsed;
                if (ResizeTopRight != null) ResizeTopRight.Visibility = Visibility.Collapsed;
                if (ResizeBottomLeft != null) ResizeBottomLeft.Visibility = Visibility.Collapsed;
                if (ResizeBottomRight != null) ResizeBottomRight.Visibility = Visibility.Collapsed;
                if (ResizeLeft != null) ResizeLeft.Visibility = Visibility.Visible;
                if (ResizeRight != null) ResizeRight.Visibility = Visibility.Visible;
            }
        }
        else
        {
            if (ResizeTop != null) ResizeTop.Visibility = Visibility.Visible;
            if (ResizeBottom != null) ResizeBottom.Visibility = Visibility.Visible;
            if (ResizeTopLeft != null) ResizeTopLeft.Visibility = Visibility.Visible;
            if (ResizeTopRight != null) ResizeTopRight.Visibility = Visibility.Visible;
            if (ResizeBottomLeft != null) ResizeBottomLeft.Visibility = Visibility.Visible;
            if (ResizeBottomRight != null) ResizeBottomRight.Visibility = Visibility.Visible;
            if (ResizeLeft != null) ResizeLeft.Visibility = Visibility.Visible;
            if (ResizeRight != null) ResizeRight.Visibility = Visibility.Visible;
        }
    }

    // ========== DRAG TO MOVE & SNAP TO EDGES ==========

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try
            {
                DragMove();
                SnapToEdges();
            }
            catch { }
        }
    }

    public void SnapToEdges(bool saveImmediately = true)
    {
        var workArea = GetCurrentWorkArea();
        const double snapThreshold = 35.0;

        double curLeft = Left;
        double curTop = Top;
        double curW = ActualWidth > 0 ? ActualWidth : Width;
        double curH = ActualHeight > 0 ? ActualHeight : Height;

        // Snap to Left screen edge
        if (Math.Abs(curLeft - workArea.Left) <= snapThreshold || curLeft < workArea.Left)
        {
            curLeft = workArea.Left;
        }
        // Snap to Right screen edge
        else if (Math.Abs((curLeft + curW) - workArea.Right) <= snapThreshold || (curLeft + curW) > workArea.Right)
        {
            curLeft = workArea.Right - curW;
        }

        // Snap to Top screen edge
        bool snappedToTop = false;
        if (Math.Abs(curTop - workArea.Top) <= snapThreshold || curTop < workArea.Top)
        {
            curTop = workArea.Top;
            snappedToTop = true;
        }
        // Snap to Bottom screen edge
        bool snappedToBottom = false;
        if (Math.Abs((curTop + curH) - workArea.Bottom) <= snapThreshold || (curTop + curH) > workArea.Bottom)
        {
            curTop = workArea.Bottom - curH;
            snappedToBottom = true;
        }

        Left = curLeft;
        Top = curTop;

        _config.X = Left;
        _config.Y = Top;

        if (_config.Orientation == FenceOrientation.Vertical)
        {
            ApplyOrientationLayout();
        }
        else
        {
            double targetHeight = Math.Max(_expandedHeight, Math.Max(_config.Height, 220));
            if (snappedToBottom && !snappedToTop)
            {
                _openUpward = true;
                _config.OpenUpward = true;
            }
            else if (snappedToTop)
            {
                _openUpward = false;
                _config.OpenUpward = false;
            }
            else
            {
                double spaceBelow = workArea.Bottom - curTop;
                double spaceAbove = (curTop + curH) - workArea.Top;
                if (spaceBelow < targetHeight && spaceAbove > spaceBelow)
                {
                    _openUpward = true;
                    _config.OpenUpward = true;
                }
                else if (spaceBelow >= targetHeight)
                {
                    _openUpward = false;
                    _config.OpenUpward = false;
                }
            }

            ApplyOrientationLayout();
        }

        if (saveImmediately)
        {
            App.ConfigManager.SaveConfig(App.Config);
        }
    }

    /// <summary>
    /// Checks available distance above and below the fence to determine whether it should open upwards or downwards.
    /// If there is not enough distance in the current direction, it automatically inverts.
    /// </summary>
    public void CheckAndAdjustOpenDirection()
    {
        if (_config.Orientation != FenceOrientation.Horizontal) return;

        var workArea = GetCurrentWorkArea();
        double targetHeight = Math.Max(_expandedHeight, Math.Max(_config.Height, 220));
        double curH = ActualHeight > 0 ? ActualHeight : Height;
        double currentBottom = Top + curH;

        bool nearBottom = Math.Abs(currentBottom - workArea.Bottom) <= 40 || currentBottom >= workArea.Bottom - 5;
        bool nearTop = Math.Abs(Top - workArea.Top) <= 40 || Top <= workArea.Top + 5;

        if (nearBottom && !nearTop)
        {
            _openUpward = true;
            _config.OpenUpward = true;
        }
        else if (nearTop)
        {
            _openUpward = false;
            _config.OpenUpward = false;
        }
        else
        {
            double spaceBelow = workArea.Bottom - Top;
            double spaceAbove = currentBottom - workArea.Top;

            if (!_openUpward)
            {
                if (spaceBelow < targetHeight && spaceAbove > spaceBelow)
                {
                    _openUpward = true;
                    _config.OpenUpward = true;
                }
            }
            else
            {
                if (spaceAbove < targetHeight && spaceBelow > spaceAbove)
                {
                    _openUpward = false;
                    _config.OpenUpward = false;
                }
            }
        }

        ApplyOrientationLayout();
    }

    // ========== CONTEXT MENU ==========

    private void FenceBorder_RightClick(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        bool isEn = App.Config.Language == AppLanguage.English;

        string rollUpHeader;
        if (_config.Orientation == FenceOrientation.Vertical)
        {
            bool isDockedRight = (Left + ActualWidth / 2) > (GetCurrentWorkArea().Left + GetCurrentWorkArea().Width / 2);
            if (isEn)
                rollUpHeader = _isRolledUp ? (isDockedRight ? "◀ Expand" : "▶ Expand") : (isDockedRight ? "▶ Roll Up" : "◀ Roll Up");
            else
                rollUpHeader = _isRolledUp ? (isDockedRight ? "◀ Déplier" : "▶ Déplier") : (isDockedRight ? "▶ Replier" : "◀ Replier");
        }
        else
        {
            if (_openUpward)
            {
                if (isEn)
                    rollUpHeader = _isRolledUp ? "▲ Expand Up" : "▼ Roll Down";
                else
                    rollUpHeader = _isRolledUp ? "▲ Déplier vers le haut" : "▼ Replier vers le bas";
            }
            else
            {
                if (isEn)
                    rollUpHeader = _isRolledUp ? "▼ Expand Down" : "▲ Roll Up";
                else
                    rollUpHeader = _isRolledUp ? "▼ Déplier vers le bas" : "▲ Replier vers le haut";
            }
        }
        var rollUp = new MenuItem { Header = rollUpHeader };
        rollUp.Click += (_, _) => RollUp_Click(null!, null!);
        menu.Items.Add(rollUp);

        // Auto Roll-Up on Hover toggle
        var autoRollUpItem = new MenuItem
        {
            Header = isEn ? "⚡ Auto Roll-Up on Hover" : "⚡ Repli auto au survol",
            IsCheckable = true,
            IsChecked = _config.AutoRollUpOnHover
        };
        autoRollUpItem.Click += (_, _) =>
        {
            _config.AutoRollUpOnHover = autoRollUpItem.IsChecked;
            App.ConfigManager.SaveConfig(App.Config);

            if (_config.AutoRollUpOnHover)
            {
                if (!IsMouseOver)
                    PerformRollUp();
            }
            else
            {
                PerformUnroll();
            }
        };
        menu.Items.Add(autoRollUpItem);

        // Roll-Up Speed submenu
        var speedMenu = new MenuItem { Header = isEn ? "⏱️ Roll-Up Speed" : "⏱️ Vitesse de repli" };
        var speeds = isEn ? new (string label, int ms)[]
        {
            ("⚡ Instant (0 ms)", 0),
            ("🚀 Ultra-fast (100 ms)", 100),
            ("🏎️ Fast (175 ms)", 175),
            ("⏱️ Normal (250 ms)", 250),
            ("🌊 Smooth (400 ms)", 400),
            ("🐢 Slow (600 ms)", 600)
        } : new (string label, int ms)[]
        {
            ("⚡ Instantanée (0 ms)", 0),
            ("🚀 Ultra-rapide (100 ms)", 100),
            ("🏎️ Rapide (175 ms)", 175),
            ("⏱️ Normale (250 ms)", 250),
            ("🌊 Fluide / Douce (400 ms)", 400),
            ("🐢 Lente (600 ms)", 600)
        };
        foreach (var (label, ms) in speeds)
        {
            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = _config.RollUpAnimationDurationMs == ms
            };
            var targetMs = ms;
            item.Click += (_, _) =>
            {
                _config.RollUpAnimationDurationMs = targetMs;
                App.ConfigManager.SaveConfig(App.Config);
            };
            speedMenu.Items.Add(item);
        }
        menu.Items.Add(speedMenu);

        // Orientation submenu
        var orientMenu = new MenuItem { Header = "📐 Orientation" };
        var horizItem = new MenuItem
        {
            Header = isEn ? "↔ Horizontal (Roll up/down)" : "↔ Mode Horizontal (repli haut/bas)",
            IsCheckable = true,
            IsChecked = _config.Orientation == FenceOrientation.Horizontal
        };
        horizItem.Click += (_, _) => SetOrientation(FenceOrientation.Horizontal);
        orientMenu.Items.Add(horizItem);

        if (_config.Orientation == FenceOrientation.Horizontal)
        {
            var upwardItem = new MenuItem
            {
                Header = isEn ? "⬆ Open Upward" : "⬆ S'ouvrir vers le haut",
                IsCheckable = true,
                IsChecked = _openUpward
            };
            upwardItem.Click += (_, _) =>
            {
                _openUpward = upwardItem.IsChecked;
                _config.OpenUpward = _openUpward;
                ApplyOrientationLayout();
                App.ConfigManager.SaveConfig(App.Config);
            };
            orientMenu.Items.Add(upwardItem);
        }

        var vertItem = new MenuItem
        {
            Header = isEn ? "↕ Vertical (Side opening)" : "↕ Mode Vertical (ouverture de côté)",
            IsCheckable = true,
            IsChecked = _config.Orientation == FenceOrientation.Vertical
        };
        vertItem.Click += (_, _) => SetOrientation(FenceOrientation.Vertical);
        orientMenu.Items.Add(vertItem);
        menu.Items.Add(orientMenu);

        // Show/Hide Tab Titles toggle
        var tabTitlesItem = new MenuItem
        {
            Header = isEn ? "🏷️ Show Tab Titles" : "🏷️ Afficher les noms des onglets",
            IsCheckable = true,
            IsChecked = _config.ShowTabTitles
        };
        tabTitlesItem.Click += (_, _) =>
        {
            _config.ShowTabTitles = tabTitlesItem.IsChecked;
            ApplyOrientationLayout();
            RenderTabs();
            App.ConfigManager.SaveConfig(App.Config);
        };
        menu.Items.Add(tabTitlesItem);

        // Chameleon mode toggle
        var chameleon = new MenuItem
        {
            Header = isEn ? "🦎 Chameleon Mode" : "🦎 Mode Caméléon",
            IsCheckable = true,
            IsChecked = _config.ChameleonMode
        };
        chameleon.Click += (_, _) =>
        {
            _config.ChameleonMode = chameleon.IsChecked;
            FenceBorder.BeginAnimation(OpacityProperty, null);
            if (_config.ChameleonMode)
            {
                if (!IsMouseOver && !_config.AutoRollUpOnHover)
                    FenceBorder.Opacity = _config.ChameleonIdleOpacity;
                else
                    FenceBorder.Opacity = 1.0;
            }
            else
            {
                FenceBorder.Opacity = 1.0;
            }
            App.ConfigManager.SaveConfig(App.Config);
        };
        menu.Items.Add(chameleon);

        menu.Items.Add(new Separator());

        var shapeSub = new MenuItem { Header = isEn ? "📐 Shape" : "📐 Forme" };
        foreach (var shape in Enum.GetValues<FenceShape>())
        {
            var item = new MenuItem { Header = shape.ToString(), IsChecked = _config.Shape == shape };
            var s = shape;
            item.Click += (_, _) =>
            {
                _config.Shape = s;
                ApplyConfig();
                App.ConfigManager.SaveConfig(App.Config);
            };
            shapeSub.Items.Add(item);
        }
        menu.Items.Add(shapeSub);

        if (_config.Tabs.Count > 1)
        {
            menu.Items.Add(new Separator());
            var detachMenu = new MenuItem { Header = isEn ? "📂 Detach Active Tab to New Fence" : "📂 Détacher l'onglet actif en nouvelle fence" };
            detachMenu.Click += (_, _) =>
            {
                var tab = GetActiveTab();
                if (tab != null)
                    _manager?.DetachTab(tab, this);
            };
            menu.Items.Add(detachMenu);
        }

        menu.IsOpen = true;
    }

    // ========== FOLDER WATCHER ==========

    private void SetupFolderWatcher(string folderPath)
    {
        if (_folderWatcher != null && _folderWatcher.Path.Equals(folderPath, StringComparison.OrdinalIgnoreCase))
            return;

        _folderWatcher?.Dispose();
        _folderWatcher = new FileSystemWatcher(folderPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };

        System.Timers.Timer? debounce = null;
        void OnChange(object s, FileSystemEventArgs a)
        {
            debounce?.Dispose();
            debounce = new System.Timers.Timer(500) { AutoReset = false };
            debounce.Elapsed += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                _tabItemsCache.Remove("folder:" + folderPath);
                _tabCountCache.Remove(folderPath);
                PopulateActiveTab();
            });
            debounce.Start();
        }

        _folderWatcher.Created += OnChange;
        _folderWatcher.Deleted += OnChange;
        _folderWatcher.Renamed += (s, a) => OnChange(s, a);
    }

    // ========== DESKTOP POSITION ==========

    public void SetDesktopPosition()
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(
                    helper.Handle,
                    NativeMethods.HWND_BOTTOM,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SetDesktopPosition error: {ex.Message}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _folderLoadCts?.Cancel();
        _folderLoadCts?.Dispose();
        _folderWatcher?.Dispose();
        _hwndSource?.Dispose();
        base.OnClosed(e);
    }
}
