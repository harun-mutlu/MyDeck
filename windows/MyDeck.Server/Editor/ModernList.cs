using System.Collections;
using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace MyDeck.Server.Editor;

public class ModernList : Control
{
    public sealed class ItemList : IEnumerable<object>
    {
        readonly ModernList _owner;
        readonly List<object> _items = new();
        internal ItemList(ModernList owner) => _owner = owner;
        public int Count => _items.Count;
        public object this[int i] { get => _items[i]; set { _items[i] = value; _owner.Changed(); } }
        public void Add(object o) { _items.Add(o); _owner.Changed(); }
        public void Insert(int i, object o) { _items.Insert(i, o); _owner.Changed(); }
        public void AddRange(IEnumerable<object> items) { _items.AddRange(items); _owner.Changed(); }
        public void RemoveAt(int i) { _items.RemoveAt(i); _owner.Changed(); }
        public void Remove(object o) { _items.Remove(o); _owner.Changed(); }
        public void Clear() { _items.Clear(); _owner.Changed(); }
        public int IndexOf(object o) => _items.IndexOf(o);
        public IEnumerator<object> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }

    public ItemList Items { get; }
    public event DrawItemEventHandler? DrawItem;
    public event EventHandler? SelectedIndexChanged;

    int _selected = -1, _hover = -1, _offset, _update;
    bool _barHover, _barDrag, _byKey;
    int _dragStart, _dragOffset;
    const int BarHit = 12;

    [DefaultValue(32)] public int ItemHeight { get; set; } = 32;
    [DefaultValue("")] public string EmptyText { get; set; } = "";
    public Color SelectedBackColor { get; set; } = Ui.Field;
    public Color AccentColor { get; set; } = Ui.Cyan;

    public ModernList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Items = new ItemList(this);
        BackColor = Ui.Card;
        ForeColor = Ui.Ink;
        Font = Ui.Body;
    }

    public void BeginUpdate() => _update++;
    public void EndUpdate() { if (--_update <= 0) { _update = 0; Changed(); } }

    void Changed()
    {
        if (_update > 0) return;
        if (_selected >= Items.Count) _selected = Items.Count - 1;
        _offset = Math.Clamp(_offset, 0, MaxOffset);
        Invalidate();
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            var v = Math.Clamp(value, -1, Items.Count - 1);
            if (v == _selected) return;
            _selected = v;
            Reveal(v);
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedItem => _selected >= 0 && _selected < Items.Count ? Items[_selected] : null;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int TopIndex
    {
        get => _offset / Math.Max(1, ItemHeight);
        set { _offset = Math.Clamp(value * ItemHeight, 0, MaxOffset); Invalidate(); }
    }

    public void ScrollToEnd() { _offset = MaxOffset; Invalidate(); }

    int ContentHeight => Items.Count * ItemHeight;
    int MaxOffset => Math.Max(0, ContentHeight - Height);

    void Reveal(int i)
    {
        if (i < 0) return;
        var top = i * ItemHeight;
        if (top < _offset) _offset = top;
        else if (top + ItemHeight > _offset + Height) _offset = top + ItemHeight - Height;
        _offset = Math.Clamp(_offset, 0, MaxOffset);
    }

    int IndexAt(int y)
    {
        var i = (y + _offset) / Math.Max(1, ItemHeight);
        return y >= 0 && i < Items.Count ? i : -1;
    }

    RectangleF Thumb()
    {
        var h = Math.Max(28f, Height * (float)Height / Math.Max(1, ContentHeight));
        var y = MaxOffset == 0 ? 0 : (Height - h) * _offset / MaxOffset;
        var w = _barHover || _barDrag ? 8f : 4f;
        return new RectangleF(Width - w - 2, y + 2, w, h - 4);
    }

    bool OnBar(int x) => MaxOffset > 0 && x >= Width - BarHit;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (Items.Count == 0 && EmptyText.Length > 0)
        {
            TextRenderer.DrawText(g, EmptyText, Font, new Rectangle(12, 0, Width - 24, Math.Min(Height, 80)), Ui.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return;
        }
        var first = _offset / Math.Max(1, ItemHeight);
        for (var i = first; i < Items.Count; i++)
        {
            var r = new Rectangle(0, i * ItemHeight - _offset, Width - (MaxOffset > 0 ? BarHit : 0), ItemHeight);
            if (r.Top > Height) break;
            var state = i == _selected ? DrawItemState.Selected : DrawItemState.Default;
            if (i == _hover && i != _selected)
                using (var hb = new SolidBrush(Ui.Mix(BackColor, Ui.Field, 0.5f))) g.FillRectangle(hb, r);
            if (DrawItem is not null)
            {
                if (i == _selected) using (var b = new SolidBrush(SelectedBackColor)) g.FillRectangle(b, r);
                DrawItem(this, new DrawItemEventArgs(g, Font, r, i, state));
            }
            else DrawDefault(g, r, i, i == _selected);
        }
        if (MaxOffset > 0)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var t = Thumb();
            using var path = Fx.Round(t, t.Width / 2);
            using var b = new SolidBrush(_barHover || _barDrag ? Ui.Mix(Ui.Border, Ui.Ink, 0.35f) : Color.FromArgb(150, Ui.Mix(Ui.Border, Ui.Ink, 0.2f)));
            g.FillPath(b, path);
        }
    }

    void DrawDefault(Graphics g, Rectangle r, int i, bool sel)
    {
        if (sel)
        {
            using (var b = new SolidBrush(SelectedBackColor)) g.FillRectangle(b, r);
            using var a = new SolidBrush(AccentColor);
            g.FillRectangle(a, r.X, r.Y + 6, 3, r.Height - 12);
        }
        TextRenderer.DrawText(g, Items[i]?.ToString() ?? "", Font, new Rectangle(r.X + 12, r.Y, r.Width - 20, r.Height), ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    public static void DrawSelection(DrawItemEventArgs e, Color accent)
    {
        if ((e.State & DrawItemState.Selected) == 0) return;
        using var a = new SolidBrush(accent);
        e.Graphics.FillRectangle(a, e.Bounds.X, e.Bounds.Y + 6, 3, e.Bounds.Height - 12);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (OnBar(e.X))
        {
            var t = Thumb();
            if (e.Y < t.Top || e.Y > t.Bottom)
            {
                var ratio = Math.Clamp((e.Y - t.Height / 2) / Math.Max(1, Height - t.Height), 0, 1);
                _offset = (int)(ratio * MaxOffset);
            }
            _barDrag = true; Capture = true; _dragStart = e.Y; _dragOffset = _offset;
            Invalidate();
            return;
        }
        var i = IndexAt(e.Y);
        if (i >= 0) SelectedIndex = i;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_barDrag)
        {
            var per = MaxOffset / Math.Max(1f, Height - Thumb().Height - 4);
            _offset = Math.Clamp(_dragOffset + (int)((e.Y - _dragStart) * per), 0, MaxOffset);
            Invalidate();
            return;
        }
        var bar = OnBar(e.X);
        var h = bar ? -1 : IndexAt(e.Y);
        if (h != _hover || bar != _barHover) { _hover = h; _barHover = bar; Invalidate(); }
        Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _barDrag = false; Capture = false; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; _barHover = false; Invalidate(); }

    protected override void OnDoubleClick(EventArgs e)
    {
        var p = PointToClient(MousePosition);
        if (!_byKey && (OnBar(p.X) || IndexAt(p.Y) < 0)) return;
        base.OnDoubleClick(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _offset = Math.Clamp(_offset - e.Delta / 2, 0, MaxOffset);
        Invalidate();
        if (e is HandledMouseEventArgs h) h.Handled = true;
    }

    protected override bool IsInputKey(Keys k) => k is Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(k);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Up: SelectedIndex = Math.Max(0, _selected - 1); break;
            case Keys.Down: SelectedIndex = Math.Min(Items.Count - 1, _selected + 1); break;
            case Keys.Home: SelectedIndex = 0; break;
            case Keys.End: SelectedIndex = Items.Count - 1; break;
            case Keys.Enter when _selected >= 0: _byKey = true; OnDoubleClick(EventArgs.Empty); _byKey = false; break;
        }
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); _offset = Math.Clamp(_offset, 0, MaxOffset); }
}
