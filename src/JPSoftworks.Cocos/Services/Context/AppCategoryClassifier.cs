namespace JPSoftworks.Cocos.Services.Context;

internal static class AppCategoryClassifier
{
    private static readonly Dictionary<string, string> ProcessCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["explorer"] = "File manager",
        ["cmd"] = "Terminal",
        ["powershell"] = "Terminal",
        ["pwsh"] = "Terminal",
        ["wt"] = "Terminal",
        ["conhost"] = "Terminal",
        ["windowsTerminal"] = "Terminal",
        ["code"] = "IDE",
        ["code-insiders"] = "IDE",
        ["devenv"] = "IDE",
        ["rider64"] = "IDE",
        ["idea64"] = "IDE",
        ["pycharm64"] = "IDE",
        ["clion64"] = "IDE",
        ["webstorm64"] = "IDE",
        ["phpstorm64"] = "IDE",
        ["goland64"] = "IDE",
        ["notepad"] = "Editor",
        ["notepad++"] = "Editor",
        ["wordpad"] = "Editor",
        ["msedge"] = "Browser",
        ["chrome"] = "Browser",
        ["firefox"] = "Browser",
        ["brave"] = "Browser",
        ["opera"] = "Browser",
        ["vivaldi"] = "Browser",
        ["arc"] = "Browser",
        ["teams"] = "Communication",
        ["slack"] = "Communication",
        ["discord"] = "Communication",
        ["zoom"] = "Communication",
        ["outlook"] = "Email",
        ["thunderbird"] = "Email",
        ["winword"] = "Document",
        ["excel"] = "Spreadsheet",
        ["powerpnt"] = "Presentation",
        ["onenote"] = "Notes",
        ["spotify"] = "Media",
        ["vlc"] = "Media",
        ["groove"] = "Media",
        ["wmplayer"] = "Media",
        ["sumatrapdf"] = "Reader"
    };

    public static string? GetCategory(string? processName, string? windowTitle, string? appName, string? description)
    {
        var normalized = NormalizeProcessName(processName);
        if (!string.IsNullOrWhiteSpace(normalized) && ProcessCategories.TryGetValue(normalized, out var category))
        {
            return category;
        }

        var combined = $"{appName} {description} {windowTitle}".ToLowerInvariant();
        if (combined.Contains("browser"))
        {
            return "Browser";
        }

        if (combined.Contains("terminal") || combined.Contains("shell"))
        {
            return "Terminal";
        }

        if (combined.Contains("mail") || combined.Contains("email"))
        {
            return "Email";
        }

        if (combined.Contains("chat") || combined.Contains("message"))
        {
            return "Communication";
        }

        return null;
    }

    private static string? NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        var trimmed = processName.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4].ToLowerInvariant()
            : trimmed.ToLowerInvariant();
    }
}
