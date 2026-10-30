using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Windows.Media.Control;

namespace TrackPadForWindows;

// Çalan müziği gösteren kart: kapak, şarkı adı, sanatçı, ilerleme çubuğu ve kontroller.
internal sealed class MediaWidget : Form
{
    private const float W = 380, H = 178;
    private const float ArtX = 18, ArtY = 18, ArtSize = 96;
    private const float TextX = 130;
    private const float ControlsY = 88;
    private const float BarY = 138;

    private enum Button { None, Back10, Prev, Play, Next, Fwd10, Bar }

    private readonly Theme _theme;
    private readonly float _s;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 40 };
    private readonly Font _titleFont, _artistFont, _smallFont, _iconFont, _playFont, _seekFont;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string _title = "Şu anda çalan bir şey yok";
    private string _artist = "Spotify, YouTube veya başka bir oynatıcıda müzik başlat";
    private string _app = "";
    private Image? _art;
    private bool _playing;
    private TimeSpan _position, _duration;
    private DateTimeOffset _positionAt;
    private Button _hot = Button.None;
    private bool _closing;
    private bool _activated;
    private IntPtr _foregroundAtOpen;
    private long _outsideSince = -1;
    private int _ticks;

    public MediaWidget(Point anchor, Theme theme)
    {
        _theme = theme;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = theme.Card;

        _ = Handle;
        _s = DeviceDpi / 96f;
        var size = new Size((int)(W * _s), (int)(H * _s));
        var area = Screen.FromPoint(anchor).WorkingArea;
        Bounds = new Rectangle(
            Math.Clamp(anchor.X - size.Width / 2, area.Left, Math.Max(area.Left, area.Right - size.Width)),
            Math.Clamp(anchor.Y - size.Height / 2, area.Top, Math.Max(area.Top, area.Bottom - size.Height)),
            size.Width, size.Height);

        if (!Ui.ApplyWindowStyle(Handle, theme.Dark))
        {
            using var path = Ui.RoundRect(new RectangleF(0, 0, size.Width, size.Height), 10 * _s);
            Region = new Region(path);
        }

        _titleFont = new Font("Segoe UI Semibold", 15f * _s, GraphicsUnit.Pixel);
        _artistFont = new Font("Segoe UI", 13f * _s, GraphicsUnit.Pixel);
        _smallFont = new Font("Segoe UI", 11f * _s, GraphicsUnit.Pixel);
        _seekFont = new Font("Segoe UI Semibold", 11f * _s, GraphicsUnit.Pixel);
        _iconFont = new Font(Ui.IconFamily, 16f * _s, GraphicsUnit.Pixel);
        _playFont = new Font(Ui.IconFamily, 18f * _s, GraphicsUnit.Pixel);

        _timer.Tick += (_, _) => OnTick();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= Ui.CS_DROPSHADOW;
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    public async void Popup()
    {
        Opacity = 0;
        _foregroundAtOpen = Native.GetForegroundWindow();
        Show();
        Native.ForceForeground(Handle);
        Activate();
        _timer.Start();
        for (int i = 1; i <= 8; i++)
        {
            Opacity = i / 8.0;
            await Task.Delay(12);
        }

        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => Ui_(AttachSession);
            AttachSession();
        }
        catch (Exception ex)
        {
            _title = "Medya bilgisine erişilemedi";
            _artist = ex.Message;
            Invalidate();
        }
    }

    private void OnTick()
    {
        // Odak alamadıysa Deactivate gelmez: başka pencere öne geçince ya da imleç uzun süre dışarıdaysa kapan.
        if (!_activated && !_closing)
        {
            long now = Environment.TickCount64;
            if (Native.GetForegroundWindow() != _foregroundAtOpen) CloseAnimated();
            else if (Bounds.Contains(Cursor.Position)) _outsideSince = -1;
            else if (_outsideSince < 0) _outsideSince = now;
            else if (now - _outsideSince > 6000) CloseAnimated();
        }

        if (_playing && ++_ticks % 6 == 0) Invalidate();
    }

    // WinRT olayları başka iş parçacığından gelir; arayüz iş parçacığına aktar.
    private void Ui_(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(a); } catch (InvalidOperationException) { }
    }

    private void AttachSession()
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaChanged;
            _session.PlaybackInfoChanged -= OnPlaybackChanged;
            _session.TimelinePropertiesChanged -= OnTimelineChanged;
        }
        _session = _manager?.GetCurrentSession();
        if (_session == null)
        {
            _title = "Şu anda çalan bir şey yok";
            _artist = "Spotify, YouTube veya başka bir oynatıcıda müzik başlat";
            _app = "";
            _art = null;
            _playing = false;
            _duration = TimeSpan.Zero;
            Invalidate();
            return;
        }
        _session.MediaPropertiesChanged += OnMediaChanged;
        _session.PlaybackInfoChanged += OnPlaybackChanged;
        _session.TimelinePropertiesChanged += OnTimelineChanged;
        _app = FriendlyAppName(_session.SourceAppUserModelId);
        LoadMedia();
        LoadPlayback();
        LoadTimeline();
    }

    private void OnMediaChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => Ui_(LoadMedia);
    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => Ui_(LoadPlayback);
    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => Ui_(LoadTimeline);

    private static string FriendlyAppName(string id)
    {
        string name = id.Split('!')[0];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        int us = name.IndexOf('_');
        if (us > 0) name = name[..us];
        int dot = name.LastIndexOf('.');
        if (dot >= 0 && dot < name.Length - 1) name = name[(dot + 1)..];
        return name.Length > 0 ? char.ToUpper(name[0]) + name[1..] : name;
    }

    private async void LoadMedia()
    {
        var session = _session;
        if (session == null) return;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (session != _session || IsDisposed) return;
            _title = string.IsNullOrWhiteSpace(props.Title) ? "Bilinmeyen parça" : props.Title;
            _artist = string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumTitle ?? "" : props.Artist;

            Image? art = null;
            if (props.Thumbnail != null)
            {
                using var stream = (await props.Thumbnail.OpenReadAsync()).AsStream();
                var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;
                art = Image.FromStream(ms);
            }
            if (IsDisposed) return;
            _art?.Dispose();
            _art = art;
            Invalidate();
        }
        catch
        {
            // Bazı oynatıcılar bilgi vermez; mevcut gösterim kalır.
        }
    }

    private void LoadPlayback()
    {
        var info = _session?.GetPlaybackInfo();
        _playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        LoadTimeline();
    }

    private void LoadTimeline()
    {
        var tl = _session?.GetTimelineProperties();
        if (tl == null) return;
        _duration = tl.EndTime - tl.StartTime;
        _position = tl.Position - tl.StartTime;
        _positionAt = tl.LastUpdatedTime;
        Invalidate();
    }

    private TimeSpan CurrentPosition()
    {
        var pos = _position;
        if (_playing && _positionAt.Year > 2000) pos += DateTimeOffset.Now - _positionAt;
        if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
        if (_duration > TimeSpan.Zero && pos > _duration) pos = _duration;
        return pos;
    }

    private static string Fmt(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    // ---- Yerleşim ----
    private RectangleF Sc(float x, float y, float w, float h) => new(x * _s, y * _s, w * _s, h * _s);

    private (Button Kind, PointF Center, float R)[] ButtonLayout()
    {
        float cx = (TextX + W - 18) / 2f;
        return new[]
        {
            (Button.Back10, new PointF(cx - 98, ControlsY), 15f),
            (Button.Prev, new PointF(cx - 50, ControlsY), 18f),
            (Button.Play, new PointF(cx, ControlsY), 22f),
            (Button.Next, new PointF(cx + 50, ControlsY), 18f),
            (Button.Fwd10, new PointF(cx + 98, ControlsY), 15f),
        };
    }

    private RectangleF BarRect => Sc(18, BarY - 6, W - 36, 12);

    private Button HitTest(Point p)
    {
        foreach (var (kind, c, r) in ButtonLayout())
        {
            float dx = p.X / _s - c.X, dy = p.Y / _s - c.Y;
            if (dx * dx + dy * dy <= r * r) return kind;
        }
        if (_duration > TimeSpan.Zero && BarRect.Contains(p)) return Button.Bar;
        return Button.None;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hot = HitTest(e.Location);
        if (hot != _hot)
        {
            _hot = hot;
            Cursor = hot == Button.None ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = Button.None;
        Invalidate();
    }

    protected override async void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || _session == null) return;
        var session = _session;
        try
        {
            switch (HitTest(e.Location))
            {
                case Button.Play: await session.TryTogglePlayPauseAsync(); break;
                case Button.Prev: await session.TrySkipPreviousAsync(); break;
                case Button.Next: await session.TrySkipNextAsync(); break;
                case Button.Back10: await Seek(CurrentPosition() - TimeSpan.FromSeconds(10)); break;
                case Button.Fwd10: await Seek(CurrentPosition() + TimeSpan.FromSeconds(10)); break;
                case Button.Bar:
                    var bar = BarRect;
                    double frac = Math.Clamp((e.X - bar.X) / bar.Width, 0, 1);
                    await Seek(TimeSpan.FromTicks((long)(_duration.Ticks * frac)));
                    break;
            }
        }
        catch
        {
            // Oynatıcı bu komutu desteklemiyor olabilir.
        }
    }

    private async Task Seek(TimeSpan target)
    {
        if (_session == null) return;
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        if (_duration > TimeSpan.Zero && target > _duration) target = _duration;
        var start = _session.GetTimelineProperties().StartTime;
        if (await _session.TryChangePlaybackPositionAsync((start + target).Ticks))
        {
            _position = target;
            _positionAt = DateTimeOffset.Now;
            Invalidate();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) CloseAnimated();
        else if (e.KeyCode == Keys.Space) _ = _session?.TryTogglePlayPauseAsync();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _activated = true;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        CloseAnimated();
    }

    private async void CloseAnimated()
    {
        if (_closing) return;
        _closing = true;
        for (int i = 7; i >= 0; i--)
        {
            Opacity = i / 8.0;
            await Task.Delay(12);
        }
        Close();
    }

    // ---- Çizim ----
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(_theme.Card);

        DrawArt(g);

        float tw = W - TextX - 18;
        using (var b = new SolidBrush(_theme.Text))
            g.DrawString(_title, _titleFont, b, Sc(TextX, 18, tw, 22), Ui.LeftMiddle);
        using (var b = new SolidBrush(_theme.SubText))
            g.DrawString(_artist, _artistFont, b, Sc(TextX, 41, tw, 20), Ui.LeftMiddle);

        DrawButtons(g);
        DrawProgress(g);
    }

    private void DrawArt(Graphics g)
    {
        var r = Sc(ArtX, ArtY, ArtSize, ArtSize);
        using var path = Ui.RoundRect(r, 10 * _s);
        if (_art != null)
        {
            var state = g.Save();
            g.SetClip(path);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            // Kareye sığdır (ortadan kırp)
            float ratio = Math.Max(r.Width / _art.Width, r.Height / _art.Height);
            float w = _art.Width * ratio, h = _art.Height * ratio;
            g.DrawImage(_art, r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h);
            g.Restore(state);
        }
        else
        {
            using var bg = new LinearGradientBrush(r, Ui.Lerp(_theme.Accent, Color.White, 0.25f), Ui.Lerp(_theme.Accent, Color.Black, 0.25f), 45f);
            g.FillPath(bg, path);
            using var f = new Font(Ui.IconFamily, 34f * _s, GraphicsUnit.Pixel);
            using var b = new SolidBrush(Color.FromArgb(230, Color.White));
            g.DrawString("", f, b, r, Ui.Centered);
        }
    }

    private void DrawButtons(Graphics g)
    {
        bool enabled = _session != null;
        foreach (var (kind, c, r) in ButtonLayout())
        {
            var rect = Sc(c.X - r, c.Y - r, r * 2, r * 2);
            bool hot = _hot == kind && enabled;
            var fg = enabled ? _theme.Text : Color.FromArgb(110, _theme.SubText);

            if (kind == Button.Play)
            {
                var fill = enabled ? (hot ? Ui.Lerp(_theme.Accent, Color.White, 0.12f) : _theme.Accent) : _theme.CardHover;
                using (var b = new SolidBrush(fill)) g.FillEllipse(b, rect);
                using var tb = new SolidBrush(enabled ? _theme.AccentText : fg);
                g.DrawString(_playing ? "" : "", _playFont, tb, rect, Ui.Centered);
                continue;
            }

            if (hot)
                using (var b = new SolidBrush(_theme.CardHover)) g.FillEllipse(b, rect);
            using var brush = new SolidBrush(fg);
            switch (kind)
            {
                case Button.Prev: g.DrawString("", _iconFont, brush, rect, Ui.Centered); break;
                case Button.Next: g.DrawString("", _iconFont, brush, rect, Ui.Centered); break;
                case Button.Back10: g.DrawString("−10", _seekFont, brush, rect, Ui.Centered); break;
                case Button.Fwd10: g.DrawString("+10", _seekFont, brush, rect, Ui.Centered); break;
            }
        }
    }

    private void DrawProgress(Graphics g)
    {
        float h = (_hot == Button.Bar ? 6 : 4) * _s;
        var bar = BarRect;
        var track = new RectangleF(bar.X, bar.Y + (bar.Height - h) / 2, bar.Width, h);
        using (var path = Ui.RoundRect(track, h / 2))
        using (var b = new SolidBrush(_theme.Stroke))
            g.FillPath(b, path);

        var pos = CurrentPosition();
        float frac = _duration > TimeSpan.Zero ? (float)(pos.TotalMilliseconds / _duration.TotalMilliseconds) : 0f;
        if (frac > 0)
        {
            var done = new RectangleF(track.X, track.Y, Math.Max(h, track.Width * frac), h);
            using var path = Ui.RoundRect(done, h / 2);
            using var b = new SolidBrush(_theme.Accent);
            g.FillPath(b, path);
        }
        if (_hot == Button.Bar)
        {
            float kx = track.X + track.Width * frac, kr = 6 * _s;
            using var b = new SolidBrush(_theme.Text);
            g.FillEllipse(b, kx - kr, track.Y + h / 2 - kr, kr * 2, kr * 2);
        }

        using var sb = new SolidBrush(_theme.SubText);
        var row = Sc(18, BarY + 8, W - 36, 18);
        if (_duration > TimeSpan.Zero)
        {
            g.DrawString(Fmt(pos), _smallFont, sb, row, Ui.LeftMiddle);
            g.DrawString("-" + Fmt(_duration - pos), _smallFont, sb, row, Ui.RightMiddle);
        }
        g.DrawString(_app, _smallFont, sb, row, Ui.Centered);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnMediaChanged;
                _session.PlaybackInfoChanged -= OnPlaybackChanged;
                _session.TimelinePropertiesChanged -= OnTimelineChanged;
            }
            _timer.Dispose();
            _art?.Dispose();
            _titleFont.Dispose();
            _artistFont.Dispose();
            _smallFont.Dispose();
            _seekFont.Dispose();
            _iconFont.Dispose();
            _playFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
