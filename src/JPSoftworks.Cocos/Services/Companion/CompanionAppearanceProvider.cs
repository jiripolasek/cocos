using Windows.UI;

namespace JPSoftworks.Cocos.Services.Companion;

internal sealed record CompanionAppearanceOption(string Emoji, Color AccentColor)
{
    public string AccentHex => CompanionAppearanceSerializer.ToHex(AccentColor);
}

internal interface ICompanionAppearanceProvider
{
    IReadOnlyList<CompanionAppearanceOption> Options { get; }

    CompanionAppearanceOption GetRandom();

    CompanionAppearanceOption? FindByEmoji(string emoji);
}

internal sealed class CompanionAppearanceProvider : ICompanionAppearanceProvider
{
    private static readonly IReadOnlyList<CompanionAppearanceOption> _options =
    [
        new("🦊", Color.FromArgb(255, 249, 115, 22)),
        new("🐱", Color.FromArgb(255, 168, 85, 247)),
        new("🐻", Color.FromArgb(255, 166, 99, 61)),
        new("🐰", Color.FromArgb(255, 236, 72, 153)),
        new("🐨", Color.FromArgb(255, 100, 116, 139)),
        new("🦉", Color.FromArgb(255, 217, 119, 6)),
        new("🐧", Color.FromArgb(255, 14, 165, 233)),
        new("🐼", Color.FromArgb(255, 52, 211, 153)),
        new("🐵", Color.FromArgb(255, 251, 146, 60)),
        new("🐤", Color.FromArgb(255, 250, 204, 21)),
        new("🐶", Color.FromArgb(255, 59, 130, 246)),
        new("🐹", Color.FromArgb(255, 251, 191, 36)),
        new("🐯", Color.FromArgb(255, 234, 88, 12)),
        new("🐸", Color.FromArgb(255, 34, 197, 94)),
        new("🐙", Color.FromArgb(255, 147, 51, 234)),
        new("🦄", Color.FromArgb(255, 244, 114, 182)),
        new("🐮", Color.FromArgb(255, 71, 85, 105)),
        new("🐷", Color.FromArgb(255, 251, 113, 133)),
        new("🐔", Color.FromArgb(255, 245, 158, 11)),
        new("🐥", Color.FromArgb(255, 234, 179, 8)),
        new("🐺", Color.FromArgb(255, 148, 163, 184)),
        new("🦁", Color.FromArgb(255, 249, 115, 22)),
        new("🐗", Color.FromArgb(255, 120, 113, 108)),
        new("🦓", Color.FromArgb(255, 129, 140, 248)),
        new("🦒", Color.FromArgb(255, 253, 186, 116)),
        new("🦔", Color.FromArgb(255, 163, 163, 163)),
        new("🦦", Color.FromArgb(255, 94, 234, 212)),
        new("🦥", Color.FromArgb(255, 196, 181, 253)),
        new("🐢", Color.FromArgb(255, 74, 222, 128)),
        new("🐬", Color.FromArgb(255, 56, 189, 248)),
        new("🐳", Color.FromArgb(255, 14, 116, 144)),
        new("🐞", Color.FromArgb(255, 239, 68, 68)),
        new("🐝", Color.FromArgb(255, 252, 211, 77)),
        new("🐛", Color.FromArgb(255, 134, 239, 172)),
        new("🐍", Color.FromArgb(255, 16, 185, 129)),
        new("🦋", Color.FromArgb(255, 217, 70, 239)),
        new("🐿", Color.FromArgb(255, 251, 146, 60)),
        new("🦝", Color.FromArgb(255, 148, 163, 184)),
        new("🦌", Color.FromArgb(255, 180, 83, 9)),
        new("🐴", Color.FromArgb(255, 120, 113, 108)),
        new("🐐", Color.FromArgb(255, 168, 162, 158)),
        new("🐑", Color.FromArgb(255, 226, 232, 240)),
        new("🐕", Color.FromArgb(255, 234, 179, 8)),
        new("🦜", Color.FromArgb(255, 34, 197, 94)),
        new("🦚", Color.FromArgb(255, 59, 130, 246)),
        new("🦩", Color.FromArgb(255, 244, 114, 182)),
        new("🦢", Color.FromArgb(255, 203, 213, 225)),
        new("🦭", Color.FromArgb(255, 56, 189, 248)),
        new("🦈", Color.FromArgb(255, 14, 116, 144)),
        new("🐠", Color.FromArgb(255, 125, 211, 252)),
        new("🐟", Color.FromArgb(255, 37, 99, 235)),
        new("🐊", Color.FromArgb(255, 21, 128, 61)),
        new("🦎", Color.FromArgb(255, 132, 204, 22)),
        new("🦘", Color.FromArgb(255, 249, 115, 22)),
        new("🦨", Color.FromArgb(255, 163, 163, 163)),
        new("🦡", Color.FromArgb(255, 100, 116, 139)),
        new("🦫", Color.FromArgb(255, 168, 85, 247)),
        new("🌞", Color.FromArgb(255, 253, 224, 71)),
        new("🌝", Color.FromArgb(255, 148, 163, 184)),
        new("🧙", Color.FromArgb(255, 79, 70, 229))
    ];

    private static readonly Random _random = new();

    public IReadOnlyList<CompanionAppearanceOption> Options => _options;

    public CompanionAppearanceOption GetRandom() => _options[_random.Next(_options.Count)];

    public CompanionAppearanceOption? FindByEmoji(string emoji)
    {
        return _options.FirstOrDefault(option => string.Equals(option.Emoji, emoji, StringComparison.Ordinal));
    }
}

internal static class CompanionAppearanceSerializer
{
    public static string ToHex(Color color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    public static bool TryParseHex(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var text = hex.Trim();
        if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        if (text.Length != 8)
        {
            return false;
        }

        if (!byte.TryParse(text[..2], System.Globalization.NumberStyles.HexNumber, null, out var a))
        {
            return false;
        }

        if (!byte.TryParse(text.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var r))
        {
            return false;
        }

        if (!byte.TryParse(text.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var g))
        {
            return false;
        }

        if (!byte.TryParse(text.Substring(6, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return false;
        }

        color = Color.FromArgb(a, r, g, b);
        return true;
    }
}
