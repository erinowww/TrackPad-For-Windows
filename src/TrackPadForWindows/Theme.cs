using Microsoft.Win32;

namespace TrackPadForWindows;

// Windows'un açık/koyu tema ayarına ve vurgu rengine uyan renkler (Windows 11 Fluent tonları).
internal sealed class Theme
{
    public required bool Dark { get; init; }
    // Yuvarlak menü
    public required Color Base { get; init; }
    public required Color Slice { get; init; }
    public required Color Center { get; init; }
    // Pencereler
    public required Color Window { get; init; }
    public required Color Card { get; init; }
    public required Color CardHover { get; init; }
    public required Color Stroke { get; init; }
    // Ortak
    public required Color Text { get; init; }
    public required Color SubText { get; init; }
    public required Color Accent { get; init; }
    public required Color AccentText { get; init; }

    public static Theme Load()
    {
        bool dark = IsSystemDark();
        Color accent = ReadAccent() ?? Color.FromArgb(0, 120, 212);
        double lum = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255;
        Color accentText = lum > 0.62 ? Color.FromArgb(20, 20, 24) : Color.White;

        return dark
            ? new Theme
            {
                Dark = true,
                Base = Color.FromArgb(215, 24, 24, 28),
                Slice = Color.FromArgb(235, 42, 42, 48),
                Center = Color.FromArgb(245, 34, 34, 40),
                Window = Color.FromArgb(32, 32, 32),
                Card = Color.FromArgb(43, 43, 43),
                CardHover = Color.FromArgb(52, 52, 52),
                Stroke = Color.FromArgb(60, 60, 60),
                Text = Color.FromArgb(240, 240, 244),
                SubText = Color.FromArgb(160, 160, 168),
                Accent = accent,
                AccentText = accentText,
            }
            : new Theme
            {
                Dark = false,
                Base = Color.FromArgb(205, 225, 225, 232),
                Slice = Color.FromArgb(240, 252, 252, 254),
                Center = Color.FromArgb(248, 244, 244, 248),
                Window = Color.FromArgb(243, 243, 243),
                Card = Color.FromArgb(255, 255, 255),
                CardHover = Color.FromArgb(246, 246, 246),
                Stroke = Color.FromArgb(229, 229, 229),
                Text = Color.FromArgb(28, 28, 34),
                SubText = Color.FromArgb(96, 96, 104),
                Accent = accent,
                AccentText = accentText,
            };
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    private static Color? ReadAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int abgr)
                return Color.FromArgb(255, abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
        }
        catch
        {
            // varsayılan renk kullanılır
        }
        return null;
    }
}
