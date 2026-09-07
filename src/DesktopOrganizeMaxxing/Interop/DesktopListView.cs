namespace DesktopOrganizeMaxxing.Interop;

using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using static NativeMethods;

/// <summary>
/// Provides access to the desktop's internal ListView control for reading and setting icon positions,
/// as well as toggling visibility (for Fences mode).
/// The Windows desktop uses a SysListView32 control inside SHELLDLL_DefView to display icons.
/// </summary>
public class DesktopListView
{
    private IntPtr _listViewHandle = IntPtr.Zero;

    public IntPtr Handle => _listViewHandle;

    /// <summary>
    /// Finds and caches the handle to the desktop's SysListView32 control.
    /// </summary>
    public bool Initialize()
    {
        _listViewHandle = GetDesktopListViewHandle();
        if (_listViewHandle != IntPtr.Zero)
        {
            EnsureAutoArrangeDisabled();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the handle to the desktop's SysListView32 by traversing the window hierarchy.
    /// </summary>
    public static IntPtr GetDesktopListViewHandle()
    {
        // 1. Check Progman
        IntPtr progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            IntPtr shellView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView != IntPtr.Zero)
            {
                IntPtr listView = FindWindowEx(shellView, IntPtr.Zero, "SysListView32", null);
                if (listView != IntPtr.Zero) return listView;
            }
        }

        // 2. In modern Windows (10/11), SHELLDLL_DefView is often a child of WorkerW
        IntPtr workerW = IntPtr.Zero;
        while ((workerW = FindWindowEx(GetDesktopWindow(), workerW, "WorkerW", null)) != IntPtr.Zero)
        {
            IntPtr shellView = FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView != IntPtr.Zero)
            {
                IntPtr listView = FindWindowEx(shellView, IntPtr.Zero, "SysListView32", null);
                if (listView != IntPtr.Zero) return listView;
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Disables Windows auto-arrange if enabled, allowing programmatic icon repositioning.
    /// </summary>
    public void EnsureAutoArrangeDisabled()
    {
        if (_listViewHandle == IntPtr.Zero) return;

        try
        {
            long style = (long)GetWindowLongPtr(_listViewHandle, GWL_STYLE);
            if ((style & LVS_AUTOARRANGE) != 0)
            {
                style &= ~LVS_AUTOARRANGE;
                SetWindowLongPtr(_listViewHandle, GWL_STYLE, new IntPtr(style));
            }
        }
        catch { }
    }

    /// <summary>
    /// Hides the native desktop icons on the wallpaper (used in Fences mode).
    /// </summary>
    public void HideDesktopIcons()
    {
        if (_listViewHandle == IntPtr.Zero)
            _listViewHandle = GetDesktopListViewHandle();

        if (_listViewHandle != IntPtr.Zero)
        {
            ShowWindow(_listViewHandle, SW_HIDE);
        }
    }

    /// <summary>
    /// Restores visibility of the native desktop icons (when leaving Fences mode).
    /// </summary>
    public void ShowDesktopIcons()
    {
        if (_listViewHandle == IntPtr.Zero)
            _listViewHandle = GetDesktopListViewHandle();

        if (_listViewHandle != IntPtr.Zero)
        {
            ShowWindow(_listViewHandle, SW_SHOW);
            RefreshDesktop();
        }
    }

    /// <summary>
    /// Gets the number of icons on the desktop.
    /// </summary>
    public int GetItemCount()
    {
        if (_listViewHandle == IntPtr.Zero) return 0;
        return (int)SendMessage(_listViewHandle, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// Gets the name of a desktop icon by index, reading from the Explorer process memory.
    /// </summary>
    public string? GetItemText(int index)
    {
        if (_listViewHandle == IntPtr.Zero) return null;

        GetWindowThreadProcessId(_listViewHandle, out uint processId);
        IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, processId);
        if (hProcess == IntPtr.Zero) return null;

        try
        {
            IntPtr remoteMem = VirtualAllocEx(hProcess, IntPtr.Zero, 4096, MEM_COMMIT, PAGE_READWRITE);
            if (remoteMem == IntPtr.Zero) return null;

            try
            {
                IntPtr remoteTextBuf = remoteMem + Marshal.SizeOf<LVITEMW>();

                var lvItem = new LVITEMW
                {
                    mask = LVIF_TEXT,
                    iItem = index,
                    iSubItem = 0,
                    pszText = remoteTextBuf,
                    cchTextMax = 260
                };

                IntPtr localLvItem = Marshal.AllocHGlobal(Marshal.SizeOf<LVITEMW>());
                try
                {
                    Marshal.StructureToPtr(lvItem, localLvItem, false);
                    WriteProcessMemory(hProcess, remoteMem, localLvItem, (uint)Marshal.SizeOf<LVITEMW>(), out _);

                    SendMessage(_listViewHandle, LVM_GETITEMTEXTW, index, remoteMem);

                    byte[] textBuffer = new byte[520];
                    IntPtr localTextBuf = Marshal.AllocHGlobal(520);
                    try
                    {
                        ReadProcessMemory(hProcess, remoteTextBuf, localTextBuf, 520, out _);
                        Marshal.Copy(localTextBuf, textBuffer, 0, 520);
                        string text = Encoding.Unicode.GetString(textBuffer);
                        int nullIndex = text.IndexOf('\0');
                        return nullIndex >= 0 ? text[..nullIndex] : text;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(localTextBuf);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(localLvItem);
                }
            }
            finally
            {
                VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
            }
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    /// <summary>
    /// Gets the position of a desktop icon by index.
    /// </summary>
    public (int x, int y)? GetItemPosition(int index)
    {
        if (_listViewHandle == IntPtr.Zero) return null;

        GetWindowThreadProcessId(_listViewHandle, out uint processId);
        IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ, false, processId);
        if (hProcess == IntPtr.Zero) return null;

        try
        {
            IntPtr remoteMem = VirtualAllocEx(hProcess, IntPtr.Zero, 8, MEM_COMMIT, PAGE_READWRITE);
            if (remoteMem == IntPtr.Zero) return null;

            try
            {
                SendMessage(_listViewHandle, LVM_GETITEMPOSITION, index, remoteMem);

                IntPtr localBuf = Marshal.AllocHGlobal(8);
                try
                {
                    ReadProcessMemory(hProcess, remoteMem, localBuf, 8, out _);
                    int x = Marshal.ReadInt32(localBuf, 0);
                    int y = Marshal.ReadInt32(localBuf, 4);
                    return (x, y);
                }
                finally
                {
                    Marshal.FreeHGlobal(localBuf);
                }
            }
            finally
            {
                VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
            }
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    /// <summary>
    /// Sets the position of a desktop icon by index.
    /// </summary>
    public bool SetItemPosition(int index, int x, int y)
    {
        if (_listViewHandle == IntPtr.Zero) return false;

        // 1. Direct standard LVM_SETITEMPOSITION message (LOWORD = x, HIWORD = y)
        IntPtr lParam = new IntPtr((y << 16) | (x & 0xFFFF));
        SendMessage(_listViewHandle, LVM_SETITEMPOSITION, (IntPtr)index, lParam);

        // 2. Also send LVM_SETITEMPOSITION32 with 32-bit coordinates in Explorer memory
        GetWindowThreadProcessId(_listViewHandle, out uint processId);
        IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_WRITE, false, processId);
        if (hProcess != IntPtr.Zero)
        {
            try
            {
                IntPtr remoteMem = VirtualAllocEx(hProcess, IntPtr.Zero, 8, MEM_COMMIT, PAGE_READWRITE);
                if (remoteMem != IntPtr.Zero)
                {
                    try
                    {
                        IntPtr localBuf = Marshal.AllocHGlobal(8);
                        try
                        {
                            Marshal.WriteInt32(localBuf, 0, x);
                            Marshal.WriteInt32(localBuf, 4, y);
                            WriteProcessMemory(hProcess, remoteMem, localBuf, 8, out _);
                            SendMessage(_listViewHandle, LVM_SETITEMPOSITION32, index, remoteMem);
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(localBuf);
                        }
                    }
                    finally
                    {
                        VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
                    }
                }
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }

        return true;
    }

    /// <summary>
    /// Forces the desktop to redraw after repositioning icons.
    /// </summary>
    public void RefreshDesktop()
    {
        if (_listViewHandle == IntPtr.Zero) return;

        int count = GetItemCount();
        if (count > 0)
        {
            SendMessage(_listViewHandle, LVM_REDRAWITEMS, IntPtr.Zero, new IntPtr(count - 1));
        }
        InvalidateRect(_listViewHandle, IntPtr.Zero, true);
        UpdateWindow(_listViewHandle);
    }

    /// <summary>
    /// Finds the ListView index for an icon by multiple name matching strategies.
    /// </summary>
    public int FindItemByName(string name, string? fullPath = null)
    {
        int count = GetItemCount();
        if (count == 0) return -1;

        // Collect all desktop item names once for fast matching
        var listItems = new List<(int Index, string Name)>();
        for (int i = 0; i < count; i++)
        {
            string? text = GetItemText(i);
            if (!string.IsNullOrEmpty(text))
                listItems.Add((i, text));
        }

        // Strategy 1: Exact match
        foreach (var item in listItems)
        {
            if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
                return item.Index;
        }

        // Strategy 2: Without extension
        string nameWithoutExt = Path.GetFileNameWithoutExtension(name);
        foreach (var item in listItems)
        {
            string itemWithoutExt = Path.GetFileNameWithoutExtension(item.Name);
            if (string.Equals(itemWithoutExt, nameWithoutExt, StringComparison.OrdinalIgnoreCase))
                return item.Index;
        }

        // Strategy 3: Full path filename match
        if (!string.IsNullOrEmpty(fullPath))
        {
            string fileName = Path.GetFileName(fullPath);
            string fileWithoutExt = Path.GetFileNameWithoutExtension(fullPath);

            foreach (var item in listItems)
            {
                if (string.Equals(item.Name, fileName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.Name, fileWithoutExt, StringComparison.OrdinalIgnoreCase))
                    return item.Index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Gets all icon names and their current positions.
    /// </summary>
    public List<(string name, int x, int y)> GetAllItems()
    {
        var result = new List<(string, int, int)>();
        int count = GetItemCount();
        for (int i = 0; i < count; i++)
        {
            string? name = GetItemText(i);
            var pos = GetItemPosition(i);
            if (name != null && pos.HasValue)
            {
                result.Add((name, pos.Value.x, pos.Value.y));
            }
        }
        return result;
    }
}
