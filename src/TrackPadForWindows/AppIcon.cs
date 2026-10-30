namespace TrackPadForWindows;

internal static class AppIcon
{
    private static Icon? _icon;

    // Gömülü app.ico; bulunamazsa Windows'un varsayılan uygulama simgesi.
    public static Icon Get()
    {
        if (_icon != null) return _icon;
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("TrackPadForWindows.app.ico");
        _icon = stream != null ? new Icon(stream) : SystemIcons.Application;
        return _icon;
    }
}
