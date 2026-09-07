namespace DesktopOrganizeMaxxing.Services;

using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DesktopOrganizeMaxxing.Models;
using static DesktopOrganizeMaxxing.Interop.NativeMethods;
using IWshRuntimeLibrary = dynamic;

/// <summary>
/// Scans the user's desktop and public desktop for all items (shortcuts, folders, files, executables).
/// Resolves shortcut targets and extracts icons.
/// </summary>
public class DesktopScanner
{
    private static readonly ConcurrentDictionary<string, BitmapSource> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string UserDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private static readonly string PublicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

    /// <summary>
    /// Scans both user and public desktop for all items.
    /// </summary>
    public List<DesktopItem> ScanDesktop()
    {
        var items = new List<DesktopItem>();

        // Scan user desktop
        if (Directory.Exists(UserDesktop))
        {
            items.AddRange(ScanDirectory(UserDesktop, isPublic: false));
        }

        // Scan public desktop
        if (Directory.Exists(PublicDesktop))
        {
            items.AddRange(ScanDirectory(PublicDesktop, isPublic: true));
        }

        return items;
    }

    private List<DesktopItem> ScanDirectory(string path, bool isPublic)
    {
        var items = new List<DesktopItem>();

        try
        {
            // Get all files
            foreach (string file in Directory.GetFiles(path))
            {
                string fileName = Path.GetFileName(file);
                // Skip hidden/system files like desktop.ini
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                    continue;

                var item = CreateDesktopItem(file, isPublic);
                if (item != null)
                    items.Add(item);
            }

            // Get all directories (folders on desktop)
            foreach (string dir in Directory.GetDirectories(path))
            {
                string dirName = Path.GetFileName(dir);
                // Skip hidden directories
                var dirInfo = new DirectoryInfo(dir);
                if ((dirInfo.Attributes & FileAttributes.Hidden) != 0)
                    continue;

                var item = new DesktopItem
                {
                    Id = GetDeterministicId(dir),
                    Name = dirName,
                    FullPath = dir,
                    TargetPath = dir,
                    ItemType = DesktopItemType.Folder,
                    IsPublicDesktop = isPublic,
                    Icon = ExtractIcon(dir)
                };
                items.Add(item);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error scanning {path}: {ex.Message}");
        }

        return items;
    }

    /// <summary>
    /// Computes a stable deterministic ID from the file path so assignments persist across scans.
    /// </summary>
    public static string GetDeterministicId(string path)
    {
        byte[] hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return Convert.ToHexString(hash);
    }

    public DesktopItem? CreateDesktopItem(string filePath, bool isPublic)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        string name = Path.GetFileNameWithoutExtension(filePath);

        var item = new DesktopItem
        {
            Id = GetDeterministicId(filePath),
            FullPath = filePath,
            IsPublicDesktop = isPublic
        };

        switch (ext)
        {
            case ".lnk":
                item.ItemType = DesktopItemType.Shortcut;
                item.Name = name;
                var target = ResolveShortcut(filePath);
                item.TargetPath = target ?? filePath;
                item.TargetExtension = Path.GetExtension(item.TargetPath).ToLowerInvariant();
                break;

            case ".url":
                item.ItemType = DesktopItemType.UrlShortcut;
                item.Name = name;
                item.TargetPath = ResolveUrlShortcut(filePath) ?? filePath;
                item.TargetExtension = ".url";
                break;

            case ".exe":
                item.ItemType = DesktopItemType.Executable;
                item.Name = name;
                item.TargetPath = filePath;
                item.TargetExtension = ".exe";
                break;

            default:
                item.ItemType = DesktopItemType.File;
                item.Name = Path.GetFileName(filePath); // Keep extension for files
                item.TargetPath = filePath;
                item.TargetExtension = ext;
                break;
        }

        item.Icon = ExtractIcon(filePath);
        return item;
    }

    /// <summary>
    /// Resolves a .lnk shortcut to its target path using COM Shell.
    /// </summary>
    private static string? ResolveShortcut(string shortcutPath)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return null;

            try
            {
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                string targetPath = shortcut.TargetPath;
                return string.IsNullOrEmpty(targetPath) ? null : targetPath;
            }
            finally
            {
                Marshal.ReleaseComObject(shell);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves a .url shortcut to its URL.
    /// </summary>
    private static string? ResolveUrlShortcut(string urlPath)
    {
        try
        {
            foreach (string line in File.ReadLines(urlPath))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    return line[4..];
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Loads an image or icon file directly from disk as a BitmapSource without relying on Windows shell file associations.
    /// Supports .png, .jpg, .jpeg, .bmp, .webp, .gif, and .ico files.
    /// </summary>
    public static BitmapSource? LoadCustomIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".gif")
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.DecodePixelWidth = 96; // Critical: decodes at icon size to save hundreds of MBs of RAM
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }

            if (ext is ".ico")
            {
                // Try BitmapDecoder to pick a frame around 64x64/96x96
                try
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count > 0)
                    {
                        var bestFrame = decoder.Frames
                            .OrderBy(f => Math.Abs(f.PixelWidth - 64))
                            .First();
                        bestFrame.Freeze();
                        return bestFrame;
                    }
                }
                catch { }

                // Fallback using System.Drawing.Icon (64x64)
                try
                {
                    using var sysIcon = new System.Drawing.Icon(path, 64, 64);
                    var bmp = Imaging.CreateBitmapSourceFromHIcon(
                        sysIcon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bmp.Freeze();
                    return bmp;
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading custom icon {path}: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Extracts the icon for a file or folder as a high-quality BitmapSource using Windows Shell.
    /// Uses an intelligent thread-safe cache to avoid redundant Shell/COM queries and reduce RAM/CPU usage.
    /// </summary>
    public static BitmapSource? ExtractIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        bool isDir = Directory.Exists(path);
        string ext = isDir ? string.Empty : Path.GetExtension(path).ToLowerInvariant();

        // Determine cache key
        string cacheKey;
        if (isDir)
        {
            // If the directory has a custom desktop.ini with an icon, cache by path; otherwise use shared folder icon
            bool hasCustomFolderConfig = File.Exists(Path.Combine(path, "desktop.ini"));
            cacheKey = hasCustomFolderConfig ? ("dir:" + path) : "__sys_folder__";
        }
        else if (ext is ".lnk" or ".url" or ".exe" or ".ico" or ".appref-ms")
        {
            // Executables, shortcuts, and custom .ico files have specific per-file icons
            cacheKey = "file:" + path;
        }
        else if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".gif")
        {
            // Direct images can be loaded as their own thumbnail/icon
            cacheKey = "img:" + path;
        }
        else
        {
            // Standard document types (.pdf, .txt, .zip, .docx, .xlsx, .mp4, etc.)
            // All share the exact same icon associated with their extension in Windows!
            cacheKey = "ext:" + (string.IsNullOrEmpty(ext) ? "__no_ext__" : ext);
        }

        if (_iconCache.TryGetValue(cacheKey, out var cachedIcon))
        {
            return cachedIcon;
        }

        BitmapSource? extracted = ExtractIconInternal(path, ext, isDir);
        if (extracted != null)
        {
            if (extracted.CanFreeze && !extracted.IsFrozen)
                extracted.Freeze();

            PruneIconCacheIfNeeded();
            _iconCache[cacheKey] = extracted;
        }

        return extracted;
    }

    private static void PruneIconCacheIfNeeded()
    {
        if (_iconCache.Count > 300)
        {
            var keysToRemove = _iconCache.Keys
                .Where(k => k.StartsWith("img:") || k.StartsWith("file:") || k.StartsWith("dir:"))
                .Take(100)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _iconCache.TryRemove(key, out _);
            }
        }
    }

    private static BitmapSource? ExtractIconInternal(string path, string ext, bool isDir)
    {
        if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".gif" or ".ico")
        {
            var custom = LoadCustomIcon(path);
            if (custom != null)
            {
                if (custom.CanFreeze && !custom.IsFrozen) custom.Freeze();
                return custom;
            }
        }

        try
        {
            var shinfo = new SHFILEINFO();
            IntPtr hRes = SHGetFileInfo(
                path,
                0,
                ref shinfo,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_LARGEICON);

            if (shinfo.hIcon != IntPtr.Zero)
            {
                try
                {
                    var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                        shinfo.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bitmapSource.Freeze();
                    return bitmapSource;
                }
                finally
                {
                    DestroyIcon(shinfo.hIcon);
                }
            }

            // Fallback for regular files if SHGetFileInfo had an issue
            if (!isDir && File.Exists(path))
            {
                using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (sysIcon != null)
                {
                    var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                        sysIcon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bitmapSource.Freeze();
                    return bitmapSource;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error extracting icon for {path}: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Moves a file or directory to the Windows Recycle Bin, showing native Windows confirmation dialogs.
    /// Returns true if successfully moved to Recycle Bin, false if cancelled by user or on error.
    /// </summary>
    public static bool SendToRecycleBin(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            if (File.Exists(path))
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    path,
                    Microsoft.VisualBasic.FileIO.UIOption.AllDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                    Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
                return true;
            }
            else if (Directory.Exists(path))
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                    path,
                    Microsoft.VisualBasic.FileIO.UIOption.AllDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                    Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
                return true;
            }
        }
        catch (OperationCanceledException)
        {
            // User clicked No/Cancel in the Windows confirmation prompt
            return false;
        }
        catch (Exception ex)
        {
            bool isEn = App.Config?.Language == AppLanguage.English;
            string title = isEn ? "Delete Error" : "Erreur de suppression";
            string msg = isEn ? $"Could not delete item:\n{ex.Message}" : $"Impossible de supprimer l'élément :\n{ex.Message}";
            MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return false;
    }
}
