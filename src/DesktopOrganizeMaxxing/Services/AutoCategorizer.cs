namespace DesktopOrganizeMaxxing.Services;

using System.IO;
using DesktopOrganizeMaxxing.Models;

/// <summary>
/// Rule-based engine that automatically categorizes desktop items based on
/// file names, paths, extensions, and known application databases.
/// </summary>
public class AutoCategorizer
{
    /// <summary>
    /// Returns the default set of categories.
    /// </summary>
    public static List<Category> GetDefaultCategories()
    {
        return new List<Category>
        {
            new() { Name = "Games", IconEmoji = "🎮", ColorHex = "#E74C3C", Region = DesktopRegion.TopLeft, SortOrder = 0 },
            new() { Name = "Development", IconEmoji = "💻", ColorHex = "#3498DB", Region = DesktopRegion.TopCenter, SortOrder = 1 },
            new() { Name = "Productivity", IconEmoji = "📊", ColorHex = "#2ECC71", Region = DesktopRegion.TopRight, SortOrder = 2 },
            new() { Name = "Audio & Music", IconEmoji = "🎵", ColorHex = "#9B59B6", Region = DesktopRegion.MiddleLeft, SortOrder = 3 },
            new() { Name = "Media & Design", IconEmoji = "🎨", ColorHex = "#E67E22", Region = DesktopRegion.Center, SortOrder = 4 },
            new() { Name = "Internet & Social", IconEmoji = "🌐", ColorHex = "#1ABC9C", Region = DesktopRegion.MiddleRight, SortOrder = 5 },
            new() { Name = "Security & VPN", IconEmoji = "🛡️", ColorHex = "#34495E", Region = DesktopRegion.BottomLeft, SortOrder = 6 },
            new() { Name = "Utilities", IconEmoji = "🔧", ColorHex = "#95A5A6", Region = DesktopRegion.BottomCenter, SortOrder = 7 },
            new() { Name = "Folders", IconEmoji = "📁", ColorHex = "#F39C12", Region = DesktopRegion.BottomRight, SortOrder = 8 },
            new() { Name = "Files & Documents", IconEmoji = "📄", ColorHex = "#7F8C8D", Region = DesktopRegion.BottomRight, SortOrder = 9 },
        };
    }

    /// <summary>
    /// Automatically categorizes a list of desktop items using rule-based detection.
    /// Returns updated categories with items assigned.
    /// </summary>
    public List<Category> Categorize(List<DesktopItem> items, List<Category>? existingCategories = null)
    {
        var categories = existingCategories ?? GetDefaultCategories();

        foreach (var item in items)
        {
            string? categoryName = DetectCategory(item);
            var category = categories.FirstOrDefault(c =>
                c.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));

            if (category != null)
            {
                item.CategoryId = category.Id;
                if (!category.ItemIds.Contains(item.Id))
                    category.ItemIds.Add(item.Id);
            }
        }

        return categories;
    }

    /// <summary>
    /// Detects the best category for a desktop item based on rules.
    /// </summary>
    public string DetectCategory(DesktopItem item)
    {
        // Handle folders first
        if (item.ItemType == DesktopItemType.Folder)
            return "Folders";

        // Handle documents/files
        if (item.ItemType == DesktopItemType.File)
            return CategorizeByExtension(item.TargetExtension);

        string nameLower = item.Name.ToLowerInvariant();
        string targetLower = item.TargetPath.ToLowerInvariant();

        // Check against known applications
        if (IsGame(nameLower, targetLower)) return "Games";
        if (IsDevelopment(nameLower, targetLower)) return "Development";
        if (IsAudioMusic(nameLower, targetLower)) return "Audio & Music";
        if (IsMediaDesign(nameLower, targetLower)) return "Media & Design";
        if (IsProductivity(nameLower, targetLower)) return "Productivity";
        if (IsInternetSocial(nameLower, targetLower)) return "Internet & Social";
        if (IsSecurity(nameLower, targetLower)) return "Security & VPN";
        if (IsUtility(nameLower, targetLower)) return "Utilities";

        // URL shortcuts are typically internet-related
        if (item.ItemType == DesktopItemType.UrlShortcut)
            return "Internet & Social";

        // Fallback: categorize by extension
        return CategorizeByExtension(item.TargetExtension);
    }

    private static bool IsGame(string name, string target)
    {
        string[] gameKeywords = {
            "steam", "epic games", "gog", "ubisoft", "origin", "ea app", "battle.net",
            "riot", "valorant", "fortnite", "minecraft", "roblox", "league of legends",
            "overwatch", "counter-strike", "csgo", "cs2", "dota", "apex", "pubg",
            "game", "gaming", "launcher", "people playground", "brotato", "rust",
            "among us", "genshin", "call of duty", "warzone", "fifa", "nba",
            "elden ring", "cyberpunk", "hogwarts", "starfield", "diablo",
            "world of warcraft", "wow", "ffxiv", "final fantasy", "zelda",
            "pokemon", "mario", "sonic", "halo", "destiny", "borderlands",
            "fallout", "skyrim", "witcher", "assassin", "resident evil", "adulttale"
        };

        string[] gamePaths = {
            "steamapps", "epic games", "riot games", "program files\\steam",
            "program files (x86)\\steam", "gog galaxy", "ubisoft game launcher"
        };

        // Check Steam URL shortcuts
        if (target.Contains("steam://")) return true;

        return gameKeywords.Any(k => name.Contains(k)) ||
               gamePaths.Any(p => target.Contains(p));
    }

    private static bool IsDevelopment(string name, string target)
    {
        string[] devKeywords = {
            "visual studio", "vs code", "vscode", "code", "cursor", "intellij", "idea",
            "pycharm", "webstorm", "rider", "clion", "goland", "android studio",
            "eclipse", "netbeans", "sublime", "atom", "notepad++", "vim", "neovim",
            "git", "github", "gitlab", "docker", "postman", "insomnia",
            "node", "python", "java", "terminal", "powershell", "wsl",
            "cmake", "mingw", "compiler", "debugger", "database", "mongodb",
            "pgadmin", "mysql", "redis", "advanced installer", "framework",
            "sdk", "unity", "unreal", "godot"
        };

        string[] devPaths = {
            "microsoft visual studio", "jetbrains", "cursor\\cursor",
            "github\\gitkraken", "docker", "postman"
        };

        // Special handling: "R 4.5.2" or similar R versions
        if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^r\s*\d"))
            return true;

        return devKeywords.Any(k => name.Contains(k)) ||
               devPaths.Any(p => target.Contains(p));
    }

    private static bool IsAudioMusic(string name, string target)
    {
        string[] audioKeywords = {
            "spotify", "audacity", "cubase", "fl studio", "ableton", "logic pro",
            "reaper", "pro tools", "garageband", "bandlab", "soundcloud",
            "musicbee", "foobar", "winamp", "itunes", "amazon music",
            "tidal", "deezer", "youtube music", "voicemeeter", "obs",
            "asio", "vst", "synthesizer", "midi", "audio",
            "sound", "music", "podcast", "audition"
        };

        return audioKeywords.Any(k => name.Contains(k));
    }

    private static bool IsMediaDesign(string name, string target)
    {
        string[] mediaKeywords = {
            "photoshop", "illustrator", "premiere", "after effects", "lightroom",
            "figma", "sketch", "canva", "gimp", "inkscape", "paint",
            "blender", "maya", "cinema 4d", "3ds max", "davinci",
            "capcut", "filmora", "handbrake", "vlc", "mpv",
            "obs studio", "streamlabs", "xsplit", "shotcut",
            "photos", "gallery", "image", "video", "photo",
            "camera", "screen", "capture", "snip", "screenshot",
            "frameview", "fat blob"
        };

        return mediaKeywords.Any(k => name.Contains(k));
    }

    private static bool IsProductivity(string name, string target)
    {
        string[] prodKeywords = {
            "word", "excel", "powerpoint", "outlook", "onenote", "onedrive",
            "teams", "slack", "zoom", "notion", "obsidian", "evernote",
            "trello", "todoist", "asana", "monday", "office",
            "libreoffice", "openoffice", "acrobat", "pdf",
            "calculator", "calendar", "mail", "writer", "calc",
            "presentation", "sheets", "docs", "drive"
        };

        return prodKeywords.Any(k => name.Contains(k));
    }

    private static bool IsInternetSocial(string name, string target)
    {
        string[] internetKeywords = {
            "chrome", "firefox", "edge", "opera", "brave", "safari", "vivaldi",
            "tor", "discord", "telegram", "whatsapp", "signal", "messenger",
            "skype", "twitter", "facebook", "instagram", "tiktok", "reddit",
            "twitch", "youtube", "netflix", "amazon prime", "disney",
            "browser", "internet", "web", "mail", "email"
        };

        return internetKeywords.Any(k => name.Contains(k));
    }

    private static bool IsSecurity(string name, string target)
    {
        string[] securityKeywords = {
            "malwarebytes", "norton", "mcafee", "kaspersky", "bitdefender",
            "avast", "avg", "windows defender", "fortinet", "forticlient",
            "vpn", "nordvpn", "expressvpn", "surfshark", "protonvpn",
            "wireguard", "openvpn", "firewall", "antivirus", "security",
            "keepass", "lastpass", "1password", "bitwarden", "encryp"
        };

        return securityKeywords.Any(k => name.Contains(k));
    }

    private static bool IsUtility(string name, string target)
    {
        string[] utilKeywords = {
            "7-zip", "winrar", "winzip", "peazip", "bandizip",
            "ccleaner", "bleachbit", "treesize", "windirstat",
            "hwmonitor", "cpu-z", "gpu-z", "speccy", "hwinfo",
            "driver", "rufus", "etcher", "ventoy", "partition",
            "defrag", "disk", "backup", "restore", "system",
            "task manager", "process", "autoruns", "sysinternals",
            "everything", "wox", "powertoys", "autohotkey",
            "liquidlauncher", "installer", "setup", "config",
            "registry", "cleaner", "optimizer", "tweak",
            "fences", "rainmeter", "wallpaper"
        };

        return utilKeywords.Any(k => name.Contains(k));
    }

    private static string CategorizeByExtension(string ext)
    {
        return ext switch
        {
            ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx"
                or ".pdf" or ".txt" or ".rtf" or ".odt" or ".ods" or ".odp"
                or ".csv" => "Files & Documents",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".svg"
                or ".webp" or ".ico" or ".tiff" or ".psd" or ".ai" => "Media & Design",
            ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".wma"
                or ".m4a" or ".mid" or ".midi" => "Audio & Music",
            ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" or ".flv"
                or ".webm" => "Media & Design",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" => "Utilities",
            ".exe" or ".msi" => "Utilities",
            _ => "Files & Documents"
        };
    }
}
