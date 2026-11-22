using System.Drawing.Drawing2D;
using System.Drawing.Text;
using MyDeck.Server.Core;

namespace MyDeck.Server.Editor;

sealed class IconPicker : DarkForm
{
    public string? Result { get; private set; }

    static readonly (string Hex, string En, string Tr)[] Named =
    {
        ("E720", "microphone mic voice record", "mikrofon mic ses kayıt"),
        ("E767", "volume speaker sound", "ses hoparlör volume"),
        ("E74F", "mute silent off", "sessiz kapalı mute"),
        ("E992", "volume 0", "ses 0"),
        ("E993", "volume low down", "ses düşük azalt"),
        ("E994", "volume medium", "ses orta"),
        ("E995", "volume high up", "ses yüksek artır"),
        ("E768", "play start", "oynat play başlat"),
        ("E769", "pause", "duraklat pause"),
        ("E71A", "stop", "durdur stop"),
        ("E892", "previous back track", "önceki geri şarkı"),
        ("E893", "next forward track", "sonraki ileri şarkı"),
        ("E8D6", "music audio file", "müzik ses dosyası"),
        ("E7F6", "headphones headset", "kulaklık headset"),
        ("E714", "video movie", "video film"),
        ("E722", "camera photo screenshot", "kamera fotoğraf ekran görüntüsü"),
        ("E7FC", "game gamepad controller", "oyun gamepad kontrolcü"),
        ("E945", "lightning fast macro", "şimşek hızlı makro"),
        ("E765", "keyboard key", "klavye tuş"),
        ("E962", "mouse", "fare mouse"),
        ("E7F4", "desktop monitor screen", "masaüstü monitör ekran"),
        ("E770", "computer pc system", "bilgisayar pc sistem"),
        ("E7F8", "laptop", "dizüstü laptop"),
        ("E8EA", "phone mobile", "telefon cep"),
        ("E772", "devices", "cihazlar"),
        ("E713", "settings gear", "ayarlar dişli settings"),
        ("E771", "personalize paint", "kişiselleştir boya"),
        ("E790", "color palette", "renk palet"),
        ("E781", "bulb light idea", "ampul ışık fikir"),
        ("E706", "brightness sun", "parlaklık güneş"),
        ("E708", "night moon sleep", "gece ay uyku"),
        ("E72E", "lock security", "kilit kilitle güvenlik"),
        ("E785", "unlock", "kilit açık"),
        ("E7E8", "power shutdown", "güç kapat power"),
        ("E777", "restart", "yeniden başlat"),
        ("E80F", "home", "ev ana sayfa home"),
        ("E72B", "back left arrow", "geri sol ok"),
        ("E72A", "forward right arrow", "ileri sağ ok"),
        ("E74A", "up arrow", "yukarı ok"),
        ("E74B", "down arrow", "aşağı ok"),
        ("E72C", "refresh reload", "yenile döngü"),
        ("E710", "add plus new", "ekle artı yeni"),
        ("E738", "minus remove", "eksi çıkar"),
        ("E711", "cancel close x", "iptal kapat çarpı x"),
        ("E73E", "check ok done", "onay tik tamam"),
        ("E74D", "delete trash bin", "sil çöp kutusu"),
        ("E74E", "save disk", "kaydet disket"),
        ("E8C8", "copy", "kopyala"),
        ("E77F", "paste clipboard", "yapıştır pano"),
        ("E8C6", "cut scissors", "kes makas"),
        ("E7A7", "undo", "geri al undo"),
        ("E7A6", "redo", "yinele redo"),
        ("E8B3", "select all", "tümünü seç"),
        ("E721", "search find", "ara büyüteç bul"),
        ("E8A3", "zoom in", "yakınlaştır"),
        ("E71F", "zoom out", "uzaklaştır"),
        ("E740", "full screen", "tam ekran"),
        ("E73F", "minimize window", "pencereyi küçült"),
        ("E8BB", "close window", "pencere kapat"),
        ("E8A7", "new window open", "yeni pencere aç"),
        ("E7C4", "task view alt tab", "görev görünümü alt tab"),
        ("E756", "command line terminal", "komut satırı terminal"),
        ("E943", "code developer", "kod geliştirici"),
        ("E9D9", "task manager performance", "görev yöneticisi tanılama performans"),
        ("E8B7", "folder", "klasör dosya"),
        ("E8F4", "new folder", "yeni klasör"),
        ("EC50", "file explorer", "dosya gezgini explorer"),
        ("E8A5", "document", "belge doküman"),
        ("E8F1", "library shelf", "kütüphane raf"),
        ("E7C3", "page", "sayfa"),
        ("E896", "download", "indir download"),
        ("E898", "upload", "yükle upload"),
        ("E753", "cloud", "bulut"),
        ("E774", "globe web internet browser", "dünya web internet tarayıcı"),
        ("E71B", "link", "bağlantı link"),
        ("E701", "wifi wireless", "wifi kablosuz"),
        ("E702", "bluetooth", "bluetooth"),
        ("E715", "mail email", "posta e-posta mail"),
        ("E8BD", "message chat", "mesaj sohbet chat"),
        ("E716", "people group", "kişiler grup"),
        ("E77B", "person profile user", "kişi profil kullanıcı"),
        ("E717", "phone call", "telefon ara"),
        ("E787", "calendar date", "takvim tarih"),
        ("E916", "stopwatch timer", "kronometre zamanlayıcı"),
        ("E823", "clock time", "saat zaman"),
        ("E7ED", "alarm bell", "alarm zil"),
        ("EA8F", "notification bell", "bildirim zil"),
        ("E946", "info", "bilgi info"),
        ("E7BA", "warning", "uyarı dikkat"),
        ("E897", "help question", "yardım soru"),
        ("EB51", "heart favorite", "kalp favori"),
        ("E734", "star favorite", "yıldız favori"),
        ("E735", "star filled", "dolu yıldız"),
        ("E8EC", "tag label", "etiket"),
        ("E8EF", "calculator", "hesap makinesi"),
        ("E70F", "edit pen", "düzenle kalem"),
        ("E7E6", "brush paint", "boya fırça"),
        ("E8F2", "chat bubble", "sohbet balonu"),
        ("E9F9", "chart graph stats", "grafik çizelge istatistik"),
        ("E8CB", "sort", "sırala"),
        ("E81D", "location map", "konum harita"),
        ("E707", "map", "harita"),
        ("E719", "shopping cart", "alışveriş sepet"),
        ("E8C7", "payment card", "ödeme kart"),
        ("E8D7", "permission", "izin yetki"),
        ("E83F", "battery", "pil batarya"),
        ("E7EF", "live broadcast", "yayın canlı"),
        ("E93E", "stream", "yayın akış"),
        ("E8F3", "do not disturb", "rahatsız etme"),
    };

    readonly string _color;
    readonly TextBox _search;
    readonly IconGrid _grid;
    readonly List<(string Hex, string Names, string Label)> _all;

    public IconPicker(string? current, string color)
    {
        _color = color;
        Text = T("Choose icon");
        BackColor = Ui.Bg;
        ForeColor = Ui.Ink;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(700, 560);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(12, 10, 12, 0), BackColor = Ui.Panel };
        _search = Ui.TextBox();
        _search.Width = 300;
        _search.PlaceholderText = T("Search: microphone, volume, folder, game…");
        _search.TextChanged += (_, _) => Fill();
        var file = Ui.Button(T("From a file…"));
        file.Click += (_, _) => PickFile();
        var none = Ui.Button(T("No icon"));
        none.Click += (_, _) => { Result = ""; DialogResult = DialogResult.OK; };
        top.Controls.AddRange(new Control[] { _search, file, none });

        _grid = new IconGrid(color) { Dock = DockStyle.Fill };
        _grid.Picked += hex => { Result = "glyph:" + hex; DialogResult = DialogResult.OK; };
        Controls.Add(_grid);
        Controls.Add(top);

        _all = BuildAll();
        Fill();
        Shown += (_, _) => _search.Focus();
    }

    static List<(string, string, string)> BuildAll()
    {
        var list = Named
            .Select(n => (n.Hex, n.Tr + " " + n.En, L.Turkish ? n.Tr : n.En))
            .DistinctBy(n => n.Hex).ToList();
        var known = list.Select(n => n.Item1).ToHashSet();
        foreach (var cp in ScanFont())
            if (known.Add(cp)) list.Add((cp, "", ""));
        return list;
    }

    static List<string>? _scanned;

    static List<string> ScanFont()
    {
        if (_scanned is not null) return _scanned;
        var found = new List<string>();
        using var fonts = new InstalledFontCollection();
        var name = fonts.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        using var family = new FontFamily(name);
        foreach (var (a, b) in new[] { (0xE700, 0xEDFF), (0xF000, 0xF8FF) })
            for (var cp = a; cp <= b; cp++)
            {
                using var path = new GraphicsPath();
                path.AddString(char.ConvertFromUtf32(cp), family, 0, 32, PointF.Empty, StringFormat.GenericTypographic);
                if (path.PointCount > 0) found.Add(cp.ToString("X4"));
            }
        return _scanned = found;
    }

    void Fill()
    {
        var q = _search.Text.Trim().ToLower(L.Culture);
        var items = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(i => i.Names.Contains(q) || i.Hex.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        _grid.SetItems(items);
    }

    void PickFile()
    {
        using var dlg = new OpenFileDialog
        {
            Title = T("File to use as the icon"),
            Filter = T("Image or program") + "|*.png;*.jpg;*.jpeg;*.bmp;*.ico;*.exe;*.lnk|" + T("All files") + "|*.*",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            Result = dlg.FileName;
            DialogResult = DialogResult.OK;
        }
    }
}

sealed class IconGrid : Control
{
    const int Cell = 60, Gap = 6, Pad = 12, Bar = 6;

    readonly string _color;
    readonly Dictionary<string, Image?> _cache = new();
    readonly ToolTip _tip = new();
    List<(string Hex, string Names, string Label)> _items = new();
    int _scroll, _hover = -1, _dragFrom = -1, _dragScroll;
    string? _tipShown;

    public event Action<string>? Picked;

    public IconGrid(string color)
    {
        _color = color;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Ui.Bg;
        Cursor = Cursors.Hand;
    }

    public void SetItems(List<(string Hex, string Names, string Label)> items)
    {
        _items = items;
        _scroll = 0;
        _hover = -1;
        Invalidate();
    }

    int Columns => Math.Max(1, (Width - Pad * 2 - Bar + Gap) / (Cell + Gap));
    int Rows => (_items.Count + Columns - 1) / Columns;
    int ContentHeight => Pad * 2 + Rows * (Cell + Gap) - Gap;
    int MaxScroll => Math.Max(0, ContentHeight - Height);

    void ScrollTo(int y)
    {
        var v = Math.Clamp(y, 0, MaxScroll);
        if (v == _scroll) return;
        _scroll = v;
        _hover = -1;
        _tipShown = null;
        _tip.Hide(this);
        Invalidate();
    }

    Rectangle CellRect(int i)
    {
        var cols = Columns;
        var used = cols * (Cell + Gap) - Gap;
        var left = Math.Max(Pad, (Width - Bar - used) / 2);
        return new Rectangle(left + i % cols * (Cell + Gap), Pad + i / cols * (Cell + Gap) - _scroll, Cell, Cell);
    }

    int HitIndex(Point p)
    {
        if (p.X >= Width - Bar - 4 && MaxScroll > 0) return -1;
        for (int i = FirstVisible(); i < _items.Count; i++)
        {
            var r = CellRect(i);
            if (r.Top > Height) break;
            if (r.Contains(p)) return i;
        }
        return -1;
    }

    int FirstVisible() => Math.Max(0, (_scroll - Pad) / (Cell + Gap) * Columns);

    Rectangle Thumb()
    {
        if (MaxScroll <= 0) return Rectangle.Empty;
        var h = Math.Max(30, Height * Height / ContentHeight);
        var y = (int)((Height - h) * (_scroll / (double)MaxScroll));
        return new Rectangle(Width - Bar - 2, y, Bar, h);
    }

    Image? Icon(string hex)
    {
        if (_cache.TryGetValue(hex, out var img)) return img;
        var png = IconRenderer.Render("glyph:" + hex, 30, _color);
        img = png is null ? null : Image.FromStream(new MemoryStream(png));
        _cache[hex] = img;
        return img;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        if (_items.Count == 0)
        {
            TextRenderer.DrawText(g, T("Nothing found"), Font, ClientRectangle, Ui.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        using var card = new SolidBrush(Ui.Card);
        using var hot = new SolidBrush(Ui.Field);
        using var border = new Pen(Ui.Border);
        for (int i = FirstVisible(); i < _items.Count; i++)
        {
            var r = CellRect(i);
            if (r.Top > Height) break;
            if (r.Bottom < 0) continue;
            using var path = Round(r, 8);
            g.FillPath(i == _hover ? hot : card, path);
            g.DrawPath(border, path);
            var img = Icon(_items[i].Hex);
            if (img is not null)
                g.DrawImage(img, r.X + (Cell - img.Width) / 2, r.Y + (Cell - img.Height) / 2, img.Width, img.Height);
        }

        var t = Thumb();
        if (!t.IsEmpty)
        {
            using var b = new SolidBrush(Color.FromArgb(_dragFrom >= 0 ? 150 : 90, Ui.Ink));
            using var p = Round(t, Bar / 2);
            g.FillPath(b, p);
        }
    }

    static GraphicsPath Round(Rectangle r, int radius)
    {
        var d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollTo(_scroll - e.Delta / 120 * (Cell + Gap));
        base.OnMouseWheel(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        var t = Thumb();
        if (!t.IsEmpty && e.X >= Width - Bar - 4)
        {
            if (!t.Contains(e.Location))
                ScrollTo((int)((e.Y - t.Height / 2.0) / (Height - t.Height) * MaxScroll));
            _dragFrom = e.Y;
            _dragScroll = _scroll;
            Capture = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragFrom >= 0)
        {
            var t = Thumb();
            var range = Height - t.Height;
            if (range > 0) ScrollTo(_dragScroll + (int)((e.Y - _dragFrom) / (double)range * MaxScroll));
            return;
        }
        var i = HitIndex(e.Location);
        if (i != _hover)
        {
            _hover = i;
            Invalidate();
        }
        var tip = i < 0 ? null : string.IsNullOrEmpty(_items[i].Label) ? _items[i].Hex : $"{_items[i].Label} · {_items[i].Hex}";
        if (tip != _tipShown)
        {
            _tipShown = tip;
            if (tip is null) _tip.Hide(this);
            else _tip.Show(tip, this, e.X + 14, e.Y + 18, 2500);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragFrom >= 0)
        {
            _dragFrom = -1;
            Capture = false;
            Invalidate();
            return;
        }
        if (e.Button == MouseButtons.Left)
        {
            var i = HitIndex(e.Location);
            if (i >= 0) Picked?.Invoke(_items[i].Hex);
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        _tipShown = null;
        _tip.Hide(this);
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ScrollTo(_scroll);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var img in _cache.Values) img?.Dispose();
            _tip.Dispose();
        }
        base.Dispose(disposing);
    }
}
