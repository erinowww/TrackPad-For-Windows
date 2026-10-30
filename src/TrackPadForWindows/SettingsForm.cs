using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TrackPadForWindows;

// Yuvarlak menünün düzenini sürükle-bırak ile ayarlama penceresi.
internal sealed class SettingsForm : Form
{
    internal const string PartFormat = "TrackPadForWindows.Part";
    internal const string SlotFormat = "TrackPadForWindows.Slot";
    // Sürüklenen öğenin sırası. Aynı işlem içinde kaldığı için veriyi serileştirmeye gerek yok.
    internal static int DragIndex = -1;

    private readonly Theme _theme;
    private readonly AppConfig _config;
    private readonly Action _onSaved;
    private readonly List<ActionConfig> _catalog = AppConfig.Catalog();
    private readonly List<ActionConfig?> _slots;
    private readonly float _s;

    private readonly PartsList _parts;
    private readonly RingEditor _ring;
    private readonly CountSelector _count;
    private readonly FlatButton _save;
    private readonly FlatButton _reset;
    private readonly Font _h1, _h2, _body, _small;

    public SettingsForm(AppConfig config, Theme theme, Action onSaved)
    {
        _config = config;
        _theme = theme;
        _onSaved = onSaved;
        _slots = config.Menu.Select(m => (ActionConfig?)m.Clone()).ToList();
        if (_slots.Count < 2) while (_slots.Count < 4) _slots.Add(null);

        Text = "TrackPad For Windows – Ayarlar";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = theme.Window;
        DoubleBuffered = true;
        Icon = AppIcon.Get();

        _ = Handle;
        _s = DeviceDpi / 96f;
        ClientSize = new Size(S(900), S(600));
        Ui.ApplyWindowStyle(Handle, theme.Dark);

        _h1 = new Font("Segoe UI Semibold", 20f * _s, GraphicsUnit.Pixel);
        _h2 = new Font("Segoe UI Semibold", 14f * _s, GraphicsUnit.Pixel);
        _body = new Font("Segoe UI", 13f * _s, GraphicsUnit.Pixel);
        _small = new Font("Segoe UI", 12f * _s, GraphicsUnit.Pixel);

        _parts = new PartsList(this) { Bounds = new Rectangle(S(24), S(76), S(280), S(500)) };
        _ring = new RingEditor(this) { Bounds = new Rectangle(S(420), S(150), S(380), S(380)) };
        _count = new CountSelector(this) { Bounds = new Rectangle(S(560), S(92), S(304), S(36)) };
        _reset = new FlatButton(this, "Varsayılana dön", accent: false) { Bounds = new Rectangle(S(588), S(540), S(140), S(36)) };
        _save = new FlatButton(this, "Kaydet", accent: true) { Bounds = new Rectangle(S(740), S(540), S(124), S(36)) };

        _count.Value = _slots.Count;
        _count.ValueChanged += SetSlotCount;
        _reset.Click += (_, _) =>
        {
            _slots.Clear();
            _slots.AddRange(AppConfig.CreateDefault().Menu);
            _count.Value = _slots.Count;
            _ring.Invalidate();
        };
        _save.Click += (_, _) => Save();

        Controls.AddRange(new Control[] { _parts, _ring, _count, _reset, _save });
    }

    internal Theme Theme => _theme;
    internal float Scale_ => _s;
    internal List<ActionConfig> Catalog => _catalog;
    internal List<ActionConfig?> Slots => _slots;
    internal Font BodyFont => _body;
    internal Font SmallFont => _small;
    internal Font HeaderFont => _h2;

    private int S(float v) => (int)Math.Round(v * _s);

    private void SetSlotCount(int n)
    {
        while (_slots.Count < n) _slots.Add(null);
        while (_slots.Count > n) _slots.RemoveAt(_slots.Count - 1);
        _ring.Invalidate();
    }

    private void Save()
    {
        var items = _slots.Where(s => s != null).Select(s => s!).ToList();
        if (items.Count == 0)
        {
            MessageBox.Show(this, "Menüye en az bir parça ekle.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _config.Menu = items;
        try
        {
            _config.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Kaydedilemedi.\n\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _onSaved();
        Close();
    }

    internal void SetSlot(int index, ActionConfig? item)
    {
        if (index < 0 || index >= _slots.Count) return;
        _slots[index] = item;
        _ring.Invalidate();
    }

    internal void SwapSlots(int a, int b)
    {
        if (a == b || a < 0 || b < 0 || a >= _slots.Count || b >= _slots.Count) return;
        (_slots[a], _slots[b]) = (_slots[b], _slots[a]);
        _ring.Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var text = new SolidBrush(_theme.Text);
        using var sub = new SolidBrush(_theme.SubText);
        g.DrawString("TrackPad For Windows", _h1, text, S(24), S(18));
        g.DrawString("Parçaları sağdaki menüye sürükleyip bırak.", _body, sub, S(24), S(48));

        // Sağdaki kart
        var card = new RectangleF(S(328), S(76), S(548), S(500));
        using (var path = Ui.RoundRect(card, 8 * _s))
        {
            using var b = new SolidBrush(_theme.Card);
            g.FillPath(b, path);
            using var p = new Pen(_theme.Stroke);
            g.DrawPath(p, path);
        }
        g.DrawString("Menü düzeni", _h2, text, S(348), S(92));
        g.DrawString("Dilim sayısı", _small, sub, S(348), S(114));
        g.DrawString("Dilimler arasında sürükleyerek yer değiştir, boşaltmak için sağ tıkla.", _small, sub,
            new RectangleF(S(348), S(546), S(232), S(36)));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _h1.Dispose();
            _h2.Dispose();
            _body.Dispose();
            _small.Dispose();
        }
        base.Dispose(disposing);
    }
}

// Soldaki parça listesi: her parça sürüklenebilir bir karo.
internal sealed class PartsList : Control
{
    private const float Header = 56, TileH = 44, Gap = 6, Pad = 12;

    private readonly SettingsForm _owner;
    private readonly Font _iconFont;
    private int _hot = -1;
    private int _pressed = -1;
    private Point _pressPoint;
    private float _scroll;
    private bool _dropHighlight;

    public PartsList(SettingsForm owner)
    {
        _owner = owner;
        DoubleBuffered = true;
        AllowDrop = true;
        _iconFont = new Font(Ui.IconFamily, 16f * owner.Scale_, GraphicsUnit.Pixel);
    }

    private float Sc => _owner.Scale_;

    private float ContentHeight => (Header + Pad + _owner.Catalog.Count * (TileH + Gap)) * Sc;

    private RectangleF TileRect(int i) =>
        new(Pad * Sc, (Header + i * (TileH + Gap)) * Sc - _scroll, Width - Pad * 2 * Sc, TileH * Sc);

    private int IndexAt(Point p)
    {
        if (p.Y < Header * Sc) return -1;
        for (int i = 0; i < _owner.Catalog.Count; i++)
            if (TileRect(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        float max = Math.Max(0, ContentHeight - Height);
        _scroll = Math.Clamp(_scroll - e.Delta / 2f, 0, max);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_pressed >= 0 && e.Button == MouseButtons.Left)
        {
            var drag = SystemInformation.DragSize;
            if (Math.Abs(e.X - _pressPoint.X) > drag.Width || Math.Abs(e.Y - _pressPoint.Y) > drag.Height)
            {
                int index = _pressed;
                _pressed = -1;
                var data = new DataObject();
                data.SetData(SettingsForm.PartFormat, "part");
                SettingsForm.DragIndex = index;
                DoDragDrop(data, DragDropEffects.Copy);
                return;
            }
        }
        int hot = IndexAt(e.Location);
        if (hot != _hot)
        {
            _hot = hot;
            Cursor = hot >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _pressed = IndexAt(e.Location);
        _pressPoint = e.Location;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = -1;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = -1;
        Invalidate();
    }

    // Menüden buraya sürüklenen dilim boşaltılır.
    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        bool slot = e.Data?.GetDataPresent(SettingsForm.SlotFormat) == true;
        e.Effect = slot ? DragDropEffects.Move : DragDropEffects.None;
        _dropHighlight = slot;
        Invalidate();
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        _dropHighlight = false;
        Invalidate();
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        _dropHighlight = false;
        if (e.Data?.GetDataPresent(SettingsForm.SlotFormat) == true) _owner.SetSlot(SettingsForm.DragIndex, null);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var t = _owner.Theme;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(t.Window);

        var card = new RectangleF(0, 0, Width - 1, Height - 1);
        using (var path = Ui.RoundRect(card, 8 * Sc))
        {
            using var b = new SolidBrush(_dropHighlight ? Ui.Lerp(t.Card, t.Accent, 0.12f) : t.Card);
            g.FillPath(b, path);
            using var p = new Pen(_dropHighlight ? t.Accent : t.Stroke);
            g.DrawPath(p, path);
            g.SetClip(path);
        }

        using var text = new SolidBrush(t.Text);
        using var sub = new SolidBrush(t.SubText);

        for (int i = 0; i < _owner.Catalog.Count; i++)
        {
            var r = TileRect(i);
            if (r.Bottom < Header * Sc || r.Top > Height) continue;
            var item = _owner.Catalog[i];
            using (var path = Ui.RoundRect(r, 6 * Sc))
            {
                using var b = new SolidBrush(i == _hot ? t.CardHover : Ui.Lerp(t.Card, t.Window, 0.5f));
                g.FillPath(b, path);
            }
            var icon = new RectangleF(r.X + 6 * Sc, r.Y, 32 * Sc, r.Height);
            using (var ab = new SolidBrush(t.Accent))
                g.DrawString(Ui.IconText(item), Ui.HasGlyph(item) ? _iconFont : _owner.HeaderFont, ab, icon, Ui.Centered);
            g.DrawString(item.Label ?? "", _owner.BodyFont, text,
                new RectangleF(r.X + 44 * Sc, r.Y, r.Width - 64 * Sc, r.Height), Ui.LeftMiddle);
            // Sürükleme tutamacı
            g.DrawString("", _iconFont, sub, new RectangleF(r.Right - 28 * Sc, r.Y, 24 * Sc, r.Height), Ui.Centered);
        }

        // Başlık en üstte sabit
        using (var hb = new SolidBrush(_dropHighlight ? Ui.Lerp(t.Card, t.Accent, 0.12f) : t.Card))
            g.FillRectangle(hb, 0, 0, Width, Header * Sc - 4 * Sc);
        g.DrawString("Parçalar", _owner.HeaderFont, text, 16 * Sc, 14 * Sc);
        g.DrawString(_dropHighlight ? "Menüden çıkarmak için bırak" : "Tut ve menüye sürükle", _owner.SmallFont, sub, 16 * Sc, 34 * Sc);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _iconFont.Dispose();
        base.Dispose(disposing);
    }
}

// Sağdaki yuvarlak menü önizlemesi; parçalar dilimlere bırakılır.
internal sealed class RingEditor : Control
{
    private const float GapDeg = 2f;

    private readonly SettingsForm _owner;
    private readonly Font _iconFont;
    private int _hot = -1;
    private int _dropTarget = -1;
    private int _pressed = -1;
    private Point _pressPoint;

    public RingEditor(SettingsForm owner)
    {
        _owner = owner;
        DoubleBuffered = true;
        AllowDrop = true;
        _iconFont = new Font(Ui.IconFamily, 24f * owner.Scale_, GraphicsUnit.Pixel);
    }

    private float Outer => Math.Min(Width, Height) / 2f - 10 * _owner.Scale_;
    private float Inner => Outer * 0.34f;

    private int SlotAt(Point p, bool anywhere = false)
    {
        int n = _owner.Slots.Count;
        if (n == 0) return -1;
        float dx = p.X - Width / 2f, dy = p.Y - Height / 2f;
        float d = MathF.Sqrt(dx * dx + dy * dy);
        if (!anywhere && (d < Inner || d > Outer + 8 * _owner.Scale_)) return -1;
        if (d < 4) return -1;
        float sweep = 360f / n;
        float angle = MathF.Atan2(dy, dx) * 180f / MathF.PI;
        float rel = ((angle + 90f + sweep / 2f) % 360f + 360f) % 360f;
        return Math.Min((int)(rel / sweep), n - 1);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_pressed >= 0 && e.Button == MouseButtons.Left && _owner.Slots[_pressed] != null)
        {
            var drag = SystemInformation.DragSize;
            if (Math.Abs(e.X - _pressPoint.X) > drag.Width || Math.Abs(e.Y - _pressPoint.Y) > drag.Height)
            {
                int index = _pressed;
                _pressed = -1;
                var data = new DataObject();
                data.SetData(SettingsForm.SlotFormat, "slot");
                SettingsForm.DragIndex = index;
                DoDragDrop(data, DragDropEffects.Move);
                return;
            }
        }
        int hot = SlotAt(e.Location);
        if (hot != _hot)
        {
            _hot = hot;
            Cursor = hot >= 0 && _owner.Slots[hot] != null ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int slot = SlotAt(e.Location);
        if (e.Button == MouseButtons.Right)
        {
            if (slot >= 0) _owner.SetSlot(slot, null);
            return;
        }
        _pressed = slot;
        _pressPoint = e.Location;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = -1;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = -1;
        Invalidate();
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        var p = PointToClient(new Point(e.X, e.Y));
        int target = SlotAt(p, anywhere: true);
        if (e.Data?.GetDataPresent(SettingsForm.PartFormat) == true) e.Effect = DragDropEffects.Copy;
        else if (e.Data?.GetDataPresent(SettingsForm.SlotFormat) == true) e.Effect = DragDropEffects.Move;
        else e.Effect = DragDropEffects.None;
        if (target != _dropTarget)
        {
            _dropTarget = target;
            Invalidate();
        }
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        _dropTarget = -1;
        Invalidate();
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        int target = _dropTarget;
        _dropTarget = -1;
        if (target < 0) return;
        int index = SettingsForm.DragIndex;
        if (e.Data?.GetDataPresent(SettingsForm.PartFormat) == true && index >= 0 && index < _owner.Catalog.Count)
            _owner.SetSlot(target, _owner.Catalog[index].Clone());
        else if (e.Data?.GetDataPresent(SettingsForm.SlotFormat) == true)
            _owner.SwapSlots(index, target);
        Invalidate();
    }

    private static GraphicsPath Sector(PointF c, float ri, float ro, float start, float sweep)
    {
        var p = new GraphicsPath();
        p.AddArc(c.X - ro, c.Y - ro, ro * 2, ro * 2, start, sweep);
        p.AddArc(c.X - ri, c.Y - ri, ri * 2, ri * 2, start + sweep, -sweep);
        p.CloseFigure();
        return p;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var t = _owner.Theme;
        float sc = _owner.Scale_;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(t.Card);

        var c = new PointF(Width / 2f, Height / 2f);
        int n = _owner.Slots.Count;
        float sweep = 360f / n;

        for (int i = 0; i < n; i++)
        {
            var item = _owner.Slots[i];
            bool drop = i == _dropTarget;
            bool hot = i == _hot && item != null;
            float start = -90f - sweep / 2f + i * sweep + GapDeg / 2f;
            float sw = sweep - GapDeg;
            float ro = Outer + (drop || hot ? 6 * sc : 0);

            using (var path = Sector(c, Inner, ro, start, sw))
            {
                Color fill = drop ? t.Accent
                    : item == null ? t.Card
                    : hot ? Ui.Lerp(t.CardHover, t.Accent, 0.18f) : Ui.Lerp(t.Window, t.CardHover, 0.6f);
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (item == null && !drop)
                {
                    using var pen = new Pen(t.SubText, 1.2f * sc) { DashStyle = DashStyle.Dash };
                    g.DrawPath(pen, path);
                }
            }

            float mid = (start + sw / 2f) * MathF.PI / 180f;
            float rc = (Inner + ro) / 2f;
            float px = c.X + MathF.Cos(mid) * rc, py = c.Y + MathF.Sin(mid) * rc;
            var fg = drop ? t.AccentText : item == null ? t.SubText : t.Text;
            using var brush = new SolidBrush(fg);

            if (item == null)
            {
                g.DrawString("", _iconFont, brush, new RectangleF(px - 20 * sc, py - 24 * sc, 40 * sc, 32 * sc), Ui.Centered);
                g.DrawString("Boş", _owner.SmallFont, brush, new RectangleF(px - 50 * sc, py + 6 * sc, 100 * sc, 18 * sc), Ui.Centered);
            }
            else
            {
                using var ib = new SolidBrush(drop ? t.AccentText : t.Accent);
                g.DrawString(Ui.IconText(item), Ui.HasGlyph(item) ? _iconFont : _owner.HeaderFont, ib,
                    new RectangleF(px - 20 * sc, py - 26 * sc, 40 * sc, 34 * sc), Ui.Centered);
                g.DrawString(item.Label ?? "", _owner.SmallFont, brush,
                    new RectangleF(px - 55 * sc, py + 6 * sc, 110 * sc, 18 * sc), Ui.Centered);
            }
        }

        // Orta
        using (var b = new SolidBrush(t.Window))
            g.FillEllipse(b, c.X - Inner + 4 * sc, c.Y - Inner + 4 * sc, (Inner - 4 * sc) * 2, (Inner - 4 * sc) * 2);
        using (var sb = new SolidBrush(t.SubText))
            g.DrawString("4 parmak\ndokunuş", _owner.SmallFont, sb, new RectangleF(c.X - Inner, c.Y - Inner, Inner * 2, Inner * 2),
                new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _iconFont.Dispose();
        base.Dispose(disposing);
    }
}

// 2-8 arası dilim sayısı seçici (Windows 11 tarzı segment düğmeler).
internal sealed class CountSelector : Control
{
    private const int Min = 2, Max = 8;
    private readonly SettingsForm _owner;
    private int _value = 4;
    private int _hot = -1;

    public event Action<int>? ValueChanged;

    public CountSelector(SettingsForm owner)
    {
        _owner = owner;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
    }

    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, Min, Max);
            Invalidate();
        }
    }

    private RectangleF Cell(int i)
    {
        int count = Max - Min + 1;
        float gap = 4 * _owner.Scale_;
        float w = (Width - gap * (count - 1)) / count;
        return new RectangleF(i * (w + gap), 0, w, Height - 1);
    }

    private int IndexAt(Point p)
    {
        for (int i = 0; i <= Max - Min; i++)
            if (Cell(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hot = IndexAt(e.Location);
        if (hot != _hot) { _hot = hot; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int i = IndexAt(e.Location);
        if (i < 0 || Min + i == _value) return;
        Value = Min + i;
        ValueChanged?.Invoke(_value);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var t = _owner.Theme;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(t.Card);
        for (int i = 0; i <= Max - Min; i++)
        {
            var r = Cell(i);
            bool selected = Min + i == _value;
            using var path = Ui.RoundRect(r, 6 * _owner.Scale_);
            using (var b = new SolidBrush(selected ? t.Accent : i == _hot ? t.CardHover : Ui.Lerp(t.Window, t.Card, 0.4f)))
                g.FillPath(b, path);
            if (!selected)
                using (var p = new Pen(t.Stroke)) g.DrawPath(p, path);
            using var tb = new SolidBrush(selected ? t.AccentText : t.Text);
            g.DrawString((Min + i).ToString(), _owner.BodyFont, tb, r, Ui.Centered);
        }
    }
}

// Yuvarlak köşeli düz düğme.
internal sealed class FlatButton : Control
{
    private readonly SettingsForm _owner;
    private readonly bool _accent;
    private bool _hot;

    public FlatButton(SettingsForm owner, string text, bool accent)
    {
        _owner = owner;
        _accent = accent;
        Text = text;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hot = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var t = _owner.Theme;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(t.Card);
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        using var path = Ui.RoundRect(r, 6 * _owner.Scale_);
        Color fill = _accent
            ? (_hot ? Ui.Lerp(t.Accent, Color.White, 0.12f) : t.Accent)
            : (_hot ? t.CardHover : Ui.Lerp(t.Window, t.Card, 0.4f));
        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        if (!_accent)
            using (var p = new Pen(t.Stroke)) g.DrawPath(p, path);
        using var tb = new SolidBrush(_accent ? t.AccentText : t.Text);
        g.DrawString(Text, _owner.BodyFont, tb, r, Ui.Centered);
    }
}
