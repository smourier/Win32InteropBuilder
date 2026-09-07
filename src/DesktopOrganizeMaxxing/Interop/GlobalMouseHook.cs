namespace DesktopOrganizeMaxxing.Interop;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using static NativeMethods;

/// <summary>
/// Global low-level mouse hook to detect double-clicks on the desktop background.
/// Used to toggle fence visibility (Mode 2).
/// </summary>
public class GlobalMouseHook : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelMouseProc _proc;
    private DateTime _lastClick = DateTime.MinValue;
    private POINT _lastClickPoint;
    private bool _disposed;

    /// <summary>
    /// Fired when a double-click is detected on the desktop background.
    /// </summary>
    public event EventHandler? DesktopDoubleClicked;

    /// <summary>
    /// Time window for double-click detection (ms).
    /// </summary>
    public int DoubleClickInterval { get; set; } = 400;

    /// <summary>
    /// Maximum distance between two clicks to count as double-click (pixels).
    /// </summary>
    public int DoubleClickDistance { get; set; } = 10;

    public GlobalMouseHook()
    {
        _proc = HookCallback;
    }

    /// <summary>
    /// Installs the global mouse hook.
    /// </summary>
    public void Install()
    {
        if (_hookId != IntPtr.Zero) return;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);

        if (_hookId == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to install mouse hook. Error: {Marshal.GetLastWin32Error()}");
        }
    }

    /// <summary>
    /// Removes the global mouse hook.
    /// </summary>
    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == WM_LBUTTONDOWN)
        {
            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

            if (IsDesktopBackground(hookStruct.pt))
            {
                var now = DateTime.Now;
                var elapsed = (now - _lastClick).TotalMilliseconds;
                var distance = Math.Sqrt(
                    Math.Pow(hookStruct.pt.X - _lastClickPoint.X, 2) +
                    Math.Pow(hookStruct.pt.Y - _lastClickPoint.Y, 2));

                if (elapsed < DoubleClickInterval && distance < DoubleClickDistance)
                {
                    // Double-click detected on desktop! Fire asynchronously so hook never blocks
                    _lastClick = DateTime.MinValue; // Reset to avoid triple-click
                    Task.Run(() => DesktopDoubleClicked?.Invoke(this, EventArgs.Empty));
                }
                else
                {
                    _lastClick = now;
                    _lastClickPoint = hookStruct.pt;
                }
            }
            else
            {
                _lastClick = DateTime.MinValue;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// Determines if a screen point is on the desktop background (not on a window or taskbar).
    /// </summary>
    private static bool IsDesktopBackground(POINT pt)
    {
        IntPtr hwnd = WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero) return false;

        var className = new StringBuilder(256);
        GetClassName(hwnd, className, 256);
        string cls = className.ToString();

        // Desktop background windows
        if (cls == "SysListView32" || cls == "SHELLDLL_DefView" || cls == "Progman" || cls == "WorkerW")
            return true;

        return false;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Uninstall();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    ~GlobalMouseHook()
    {
        Dispose();
    }
}
