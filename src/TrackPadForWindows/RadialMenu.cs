using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using static TrackPadForWindows.Native;

namespace TrackPadForWindows;

// İmlecin etrafında açılan yuvarlak menü. Üzerine gelinen dilim dışarı doğru büyür ve vurgu rengini alır.
internal sealed class RadialMenu : Form
{
    // 96 DPI'daki ölçüler (piksel)
    private const float OuterR = 128f;
    private const float InnerR = 42f;
    private const float Pop = 12f;
    private const float Pad = 24f;
    private const float GapDeg = 1.4f;

    private const int HotNone = -1;
    private const int HotCenter = -2;

    private readonly Theme _theme;
    private readonly Stack<List<ActionConfig>> _back = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private readonly Point _anchor;
    private readonly string _iconFamily;
    private readonly Font _iconFont;
    private readonly Font _labelFont;
    private readonly Font _centerFont;
    private readonly Font _glyphFont;

    private List<ActionConfig> _items;
    private float[] _hover = Array.Empty<float>();
    private float _centerHover;
    private float _open;     // açılış animasyonu 0..1
    private float _pageAnim = 1f; // alt menüye geçiş animasyonu 0..1
    private int _hot = HotNone;
    private bool _closing;
    private bool _activated;
    private IntPtr _foregroundAtOpen;
    private long _outsideSince = -1;
    private ActionConfig? _chosen;
    private float _scale = 1f;
    private int _sizePx;
    private Point _center;

    public event Action<ActionConfig>? ActionChosen;

    public RadialMenu(List<ActionConfig> items, Point anchor, Theme theme)
    {
        _items = items;
        _anchor = anchor;
        _theme = theme;
        _hover = new float[items.Count];

        _iconFamily = FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        _iconFont = new Font(_iconFamily, 22f, GraphicsUnit.Pixel);
        _labelFont = new Font("Segoe UI", 11f, GraphicsUnit.Pixel);
        _centerFont = new Font("Segoe UI Semibold", 11.5f, GraphicsUnit.Pixel);
        _glyphFont = new Font(_iconFamily, 16f, GraphicsUnit.Pixel);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        TopMost = true;

        _timer.Tick += (_, _) => Tick();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
            return cp;
        }
    }

    public void Popup()
    {
        _ = Handle; // DPI'ı öğrenmek için pencereyi oluştur
        _scale = DeviceDpi / 96f;
        _sizePx = (int)Math.Ceiling((OuterR + Pop + Pad) * 2 * _scale);

        var area = Screen.FromPoint(_anchor).WorkingArea;
        int left = Math.Clamp(_anchor.X - _sizePx / 2, area.Left, Math.Max(area.Left, area.Right - _sizePx));
        int top = Math.Clamp(_anchor.Y - _sizePx / 2, area.Top, Math.Max(area.Top, area.Bottom - _sizePx));
        Bounds = new Rectangle(left, top, _sizePx, _sizePx);
        _center = new Point(left + _sizePx / 2, top + _sizePx / 2);

        Render();
        _foregroundAtOpen = GetForegroundWindow();
        Show();
        ForceForeground(Handle);
        Activate();
        _timer.Start();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _activated = true;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (_activated) BeginClose(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) GoBackOrClose();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_closing) return;
        if (e.Button == MouseButtons.Right)
        {
            GoBackOrClose();
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        int hit = HitTest(Cursor.Position);
        if (hit == HotCenter) GoBackOrClose();
        else if (hit >= 0) Choose(_items[hit]);
    }

    private void Choose(ActionConfig item)
    {
        if (item.Kind == "submenu")
        {
            var children = item.Items ?? new();
            if (children.Count == 0) return;
            _back.Push(_items);
            SetItems(children);
        }
        else
        {
            BeginClose(item);
        }
    }

    private void GoBackOrClose()
    {
        if (_back.Count > 0) SetItems(_back.Pop());
        else BeginClose(null);
    }

    private void SetItems(List<ActionConfig> items)
    {
        _items = items;
        _hover = new float[items.Count];
        _hot = HotNone;
        _pageAnim = 0f;
    }

    private void BeginClose(ActionConfig? chosen)
    {
        if (_closing) return;
        _closing = true;
        _chosen = chosen;
    }

    private int HitTest(Point screen)
    {
        float dx = (screen.X - _center.X) / _scale;
        float dy = (screen.Y - _center.Y) / _scale;
        float d = MathF.Sqrt(dx * dx + dy * dy);
        if (d <= InnerR) return HotCenter;
        if (d > OuterR + Pop || _items.Count == 0) return HotNone;

        float sweep = 360f / _items.Count;
        float angle = MathF.Atan2(dy, dx) * 180f / MathF.PI; // -180..180, 0 = sağ
        float rel = (angle + 90f + sweep / 2f) % 360f;
        if (rel < 0) rel += 360f;
        return Math.Min((int)(rel / sweep), _items.Count - 1);
    }

    // Pencere odak alamadıysa Deactivate gelmez: başka pencere öne geçince
    // ya da imleç uzun süre menünün dışında kalınca kapan.
    private void CloseIfAbandoned()
    {
        if (GetForegroundWindow() != _foregroundAtOpen)
        {
            BeginClose(null);
            return;
        }
        long now = Environment.TickCount64;
        if (HitTest(Cursor.Position) != HotNone) _outsideSince = -1;
        else if (_outsideSince < 0) _outsideSince = now;
        else if (now - _outsideSince > 2500) BeginClose(null);
    }

    private static float Approach(float value, float target, float step) =>
        value < target ? Math.Min(target, value + step) : Math.Max(target, value - step);

    private void Tick()
    {
        bool dirty = false;

        if (!_closing)
        {
            if (!_activated) CloseIfAbandoned();

            int hot = HitTest(Cursor.Position);
            if (hot != _hot)
            {
                _hot = hot;
                Cursor = hot == HotNone ? Cursors.Default : Cursors.Hand;
                dirty = true;
            }
        }

        float target = _closing ? 0f : 1f;
        float openStep = _closing ? 0.2f : 0.11f;
        if (_open != target) { _open = Approach(_open, target, openStep); dirty = true; }
        if (_pageAnim < 1f) { _pageAnim = Approach(_pageAnim, 1f, 0.12f); dirty = true; }

        for (int i = 0; i < _hover.Length; i++)
        {
            float t = i == _hot ? 1f : 0f;
            if (_hover[i] != t) { _hover[i] = Approach(_hover[i], t, 0.16f); dirty = true; }
        }
        float ct = _hot == HotCenter ? 1f : 0f;
        if (_centerHover != ct) { _centerHover = Approach(_centerHover, ct, 0.16f); dirty = true; }

        if (dirty) Render();

        if (_closing && _open <= 0f)
        {
            _timer.Stop();
            Hide();
            var chosen = _chosen;
            Close();
            if (chosen != null) ActionChosen?.Invoke(chosen);
        }
    }

    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

    private static Color Lerp(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    private static GraphicsPath Sector(float ri, float ro, float start, float sweep)
    {
        var p = new GraphicsPath();
        p.AddArc(-ro, -ro, ro * 2, ro * 2, start, sweep);
        p.AddArc(-ri, -ri, ri * 2, ri * 2, start + sweep, -sweep);
        p.CloseFigure();
        return p;
    }

    private string IconText(ActionConfig item)
    {
        if (item.Icon is { Length: > 0 } icon &&
            int.TryParse(icon, System.Globalization.NumberStyles.HexNumber, null, out int code))
            return char.ConvertFromUtf32(code);
        return string.IsNullOrEmpty(item.Label) ? "?" : item.Label[..1].ToUpperInvariant();
    }

    private Font IconFontFor(ActionConfig item) =>
        item.Icon is { Length: > 0 } ? _iconFont : _centerFont;

    private void Render()
    {
        if (_sizePx <= 0) return;
        using var bmp = new Bitmap(_sizePx, _sizePx, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            float open = EaseOut(_open);
            float page = EaseOut(_pageAnim);
            g.TranslateTransform(_sizePx / 2f, _sizePx / 2f);
            g.ScaleTransform(_scale * (0.82f + 0.18f * open), _scale * (0.82f + 0.18f * open));
            g.RotateTransform(-12f * (1f - open));

            DrawShadow(g);
            using (var b = new SolidBrush(_theme.Base)) g.FillEllipse(b, -OuterR, -OuterR, OuterR * 2, OuterR * 2);
            DrawSlices(g, page);
            DrawCenter(g);
        }
        Push(bmp, (byte)(255 * EaseOut(_open)));
    }

    private void DrawShadow(Graphics g)
    {
        for (int i = 0; i < 8; i++)
        {
            float r = OuterR + 1 + i * 2.2f;
            using var b = new SolidBrush(Color.FromArgb(16 - i * 2, 0, 0, 0));
            g.FillEllipse(b, -r, -r + 3, r * 2, r * 2);
        }
    }

    private void DrawSlices(Graphics g, float page)
    {
        int n = _items.Count;
        if (n == 0) return;
        float sweep = 360f / n;
        using var center = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
        };

        for (int i = 0; i < n; i++)
        {
            var item = _items[i];
            float t = EaseOut(_hover[i]);
            float start = -90f - sweep / 2f + i * sweep + GapDeg / 2f;
            float sw = sweep - GapDeg;
            float ri = InnerR + 4f;
            float ro = (OuterR - 4f) * (0.9f + 0.1f * page) + Pop * t;

            using (var path = Sector(ri, ro, start, sw))
            using (var fill = new SolidBrush(Lerp(_theme.Slice, _theme.Accent, t)))
            {
                g.FillPath(fill, path);
            }

            float mid = (start + sw / 2f) * MathF.PI / 180f;
            float rc = (ri + ro) / 2f + 2f;
            float px = MathF.Cos(mid) * rc;
            float py = MathF.Sin(mid) * rc;

            var fg = Lerp(_theme.Text, _theme.AccentText, t);
            var sub = Lerp(_theme.SubText, _theme.AccentText, t);
            int alpha = (int)(255 * page);

            var state = g.Save();
            g.TranslateTransform(px, py - 8f);
            g.ScaleTransform(1f + 0.12f * t, 1f + 0.12f * t);
            using (var b = new SolidBrush(Color.FromArgb(alpha, fg)))
                g.DrawString(IconText(item), IconFontFor(item), b, new RectangleF(-20, -16, 40, 32), center);
            g.Restore(state);

            string label = item.Label ?? "";
            if (item.Kind == "submenu") label += " ›";
            using (var b = new SolidBrush(Color.FromArgb(alpha, sub)))
                g.DrawString(label, _labelFont, b, new RectangleF(px - 40, py + 10, 80, 16), center);
        }
    }

    private void DrawCenter(Graphics g)
    {
        float t = EaseOut(_centerHover);
        float r = InnerR + 2f * t;
        using (var b = new SolidBrush(Lerp(_theme.Center, _theme.Accent, t)))
            g.FillEllipse(b, -r, -r, r * 2, r * 2);

        using var center = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisWord,
        };
        var fg = Lerp(_theme.Text, _theme.AccentText, t);
        using var brush = new SolidBrush(fg);

        if (_hot >= 0 && _hot < _items.Count)
        {
            // Üzerine gelinen seçeneğin tam adı ortada
            g.DrawString(_items[_hot].Label ?? "", _centerFont, brush, new RectangleF(-InnerR + 6, -InnerR + 8, (InnerR - 6) * 2, (InnerR - 8) * 2), center);
        }
        else
        {
            string glyph = _back.Count > 0 ? "" : ""; // geri / kapat
            g.DrawString(glyph, _glyphFont, brush, new RectangleF(-20, -20, 40, 40), center);
        }
    }

    // Bitmap'i alfa kanalıyla birlikte pencereye bas.
    private void Push(Bitmap bmp, byte opacity)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr old = SelectObject(memDc, hBitmap);
        try
        {
            var size = new SIZE(bmp.Width, bmp.Height);
            var src = new POINT(0, 0);
            var dst = new POINT(Left, Top);
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                SourceConstantAlpha = opacity,
                AlphaFormat = AC_SRC_ALPHA,
            };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hBitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _iconFont.Dispose();
            _labelFont.Dispose();
            _centerFont.Dispose();
            _glyphFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
