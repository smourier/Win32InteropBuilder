using System.Windows;
using DesktopOrganizeMaxxing.Models;
using DesktopOrganizeMaxxing.Services;
using System.Windows.Forms;
using System.Drawing;

namespace DesktopOrganizeMaxxing;

/// <summary>
/// Application entry point. Handles theme detection, system tray, and global state.
/// </summary>
public partial class App : Application
{
    private NotifyIcon? _trayIcon;
    private static System.Threading.Mutex? _singleInstanceMutex;
    private static System.Threading.EventWaitHandle? _showWindowEventWaitHandle;
    private const string MutexName = @"Local\DesktopOrganizeMaxxing_SingleInstance_Mutex";
    private const string EventName = @"Local\DesktopOrganizeMaxxing_ShowWindow_Event";

    public static AppConfig Config { get; set; } = new();
    public static ConfigManager ConfigManager { get; } = new();

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        bool createdNew;
        try
        {
            _singleInstanceMutex = new System.Threading.Mutex(true, MutexName, out createdNew);
        }
        catch
        {
            createdNew = true;
        }

        if (!createdNew)
        {
            // Another instance is already running! Signal it to show and exit immediately.
            try
            {
                using var showEvent = System.Threading.EventWaitHandle.OpenExisting(EventName);
                showEvent.Set();
            }
            catch { }

            Shutdown();
            return;
        }

        // Setup the cross-process activation listener
        try
        {
            _showWindowEventWaitHandle = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, EventName);
            Task.Run(() =>
            {
                while (_showWindowEventWaitHandle != null)
                {
                    try
                    {
                        _showWindowEventWaitHandle.WaitOne();
                        Current?.Dispatcher.BeginInvoke(() => ShowMainWindow());
                    }
                    catch { break; }
                }
            });
        }
        catch { }

        // Load saved config
        Config = ConfigManager.LoadConfig() ?? new AppConfig();

        // Apply theme
        ApplyTheme(Config.Theme);

        // Ensure startup registry setting matches saved config
        ConfigManager.SetStartupEnabled(Config.RunAtStartup);

        // Setup system tray
        SetupTrayIcon();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        // Release single-instance resources
        _showWindowEventWaitHandle?.Dispose();
        _showWindowEventWaitHandle = null;
        if (_singleInstanceMutex != null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        // Save config on exit
        ConfigManager.SaveConfig(Config);

        // Always ensure native desktop icons are visible when app exits
        try
        {
            new Interop.DesktopListView().ShowDesktopIcons();
        }
        catch { }

        // Cleanup tray icon
        _trayIcon?.Dispose();
    }

    /// <summary>
    /// Applies the theme by swapping resource dictionaries.
    /// </summary>
    public static void ApplyTheme(AppThemeMode mode)
    {
        bool useDark = mode switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => IsWindowsDarkMode()
        };

        var themeUri = new Uri(useDark
            ? "Themes/DarkTheme.xaml"
            : "Themes/LightTheme.xaml", UriKind.Relative);

        var app = Current;
        if (app?.Resources.MergedDictionaries.Count > 0)
        {
            app.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = themeUri };
        }
    }

    /// <summary>
    /// Detects Windows dark mode from registry.
    /// </summary>
    private static bool IsWindowsDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int intVal && intVal == 0;
        }
        catch
        {
            return true; // Default to dark
        }
    }

    /// <summary>
    /// Sets up the system tray icon with context menu.
    /// </summary>
    private void SetupTrayIcon()
    {
        Icon? appIcon = null;
        try
        {
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "desktop_organiser_maxxing_logo.ico");
            if (System.IO.File.Exists(iconPath))
            {
                appIcon = new Icon(iconPath);
            }
            else
            {
                var iconUri = new Uri("pack://application:,,,/Assets/desktop_organiser_maxxing_logo.ico");
                var streamInfo = GetResourceStream(iconUri);
                if (streamInfo != null)
                {
                    appIcon = new Icon(streamInfo.Stream);
                }
            }
        }
        catch { }

        _trayIcon = new NotifyIcon
        {
            Text = "Desktop Organize Maxxing",
            Icon = appIcon ?? SystemIcons.Application,
            Visible = true
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open", null, (_, _) => ShowMainWindow());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (_, _) =>
        {
            _trayIcon.Visible = false;
            Shutdown();
        });

        _trayIcon.ContextMenuStrip = contextMenu;
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    public void ShowMainWindow()
    {
        if (MainWindow == null)
        {
            MainWindow = new Views.MainWindow();
        }
        if (MainWindow.WindowState == WindowState.Minimized)
            MainWindow.WindowState = WindowState.Normal;
        MainWindow.Show();
        MainWindow.Activate();
        MainWindow.Topmost = true;
        MainWindow.Topmost = false;
        MainWindow.Focus();
    }
}
