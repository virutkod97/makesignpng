using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace MakeSignPng;

public sealed class MainForm : Form
{
    // Ảnh gốc đầy đủ (dùng khi xuất file) và bản thu nhỏ để xem trước cho mượt.
    private Bitmap? _sourceFull;
    private Bitmap? _sourceWork;

    // Kết quả đã tách nền + đã cắt (bản xem trước, từ _sourceWork).
    private Bitmap? _trimmedPreview;

    private SplitContainer? _split;

    private string _lastDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    private string _baseName = "chu_ky";

    // Điều khiển
    private readonly CheckerPanel _sourcePanel = new() { Dock = DockStyle.Fill };
    private readonly CheckerPanel _resultPanel = new() { Dock = DockStyle.Fill };
    private readonly CheckerPanel _preview280 = new(8);
    private readonly CheckerPanel _preview70 = new(6);

    private readonly TrackBar _whiteBar = new() { Minimum = 40, Maximum = 254, TickFrequency = 20, Width = 240 };
    private readonly TrackBar _inkBar = new() { Minimum = 10, Maximum = 250, TickFrequency = 20, Width = 240 };
    private readonly NumericUpDown _despeckle = new() { Minimum = 0, Maximum = 500, Value = 8, Width = 70 };
    private readonly NumericUpDown _padding = new() { Minimum = 0, Maximum = 40, Value = 6, Width = 70 };
    private readonly CheckBox _recolor = new() { Text = "Đổi màu mực", AutoSize = true };
    private readonly Button _colorBtn = new() { Text = "Chọn màu", Width = 90, Enabled = false };
    private readonly CheckBox _autoThreshold = new()
        { Text = "Tự động ngưỡng khi mở ảnh", AutoSize = true, Checked = true };
    private readonly Label _whiteLbl = new() { AutoSize = true };
    private readonly Label _inkLbl = new() { AutoSize = true };
    private readonly Label _statusLbl = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly TextBox _nameBox = new() { Width = 280 };

    private Color _inkColor = Color.FromArgb(0, 40, 160);
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 150 };
    private bool _loading;

    public MainForm()
    {
        Text = "MakeSignPng — Tạo chữ ký PNG trong suốt";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1040, 660);
        Size = new Size(1180, 760);
        Font = new Font("Segoe UI", 9f);
        KeyPreview = true; // để bắt Ctrl+V dán ảnh ở mọi nơi trên form
        TryLoadWindowIcon();

        BuildUi();

        _debounce.Tick += (_, _) => { _debounce.Stop(); Reprocess(); };
        _whiteBar.ValueChanged += OnParamChanged;
        _inkBar.ValueChanged += OnParamChanged;
        _despeckle.ValueChanged += OnParamChanged;
        _padding.ValueChanged += OnParamChanged;
        _recolor.CheckedChanged += (_, _) => { _colorBtn.Enabled = _recolor.Checked; OnParamChanged(null, EventArgs.Empty); };
        _colorBtn.Click += OnPickColor;

        ApplySettings(AppSettings.Load());
        UpdateThresholdLabels();
    }

    private void TryLoadWindowIcon()
    {
        try
        {
            using var s = typeof(MainForm).Assembly
                .GetManifestResourceStream("MakeSignPng.app.ico");
            if (s != null) Icon = new Icon(s);
        }
        catch { /* không có icon cũng không sao */ }
    }

    private void ApplySettings(AppSettings s)
    {
        _loading = true;
        _whiteBar.Value = Clamp(s.WhiteThreshold, _whiteBar.Minimum, _whiteBar.Maximum);
        _inkBar.Value = Clamp(s.InkThreshold, _inkBar.Minimum, _inkBar.Maximum);
        _despeckle.Value = Clamp(s.Despeckle, (int)_despeckle.Minimum, (int)_despeckle.Maximum);
        _padding.Value = Clamp(s.Padding, (int)_padding.Minimum, (int)_padding.Maximum);
        _inkColor = Color.FromArgb(s.InkColorArgb);
        _colorBtn.BackColor = _inkColor;
        _recolor.Checked = s.Recolor;
        _colorBtn.Enabled = s.Recolor;
        _autoThreshold.Checked = s.AutoThresholdOnOpen;
        _loading = false;
    }

    private AppSettings CurrentSettings() => new()
    {
        WhiteThreshold = _whiteBar.Value,
        InkThreshold = _inkBar.Value,
        Recolor = _recolor.Checked,
        InkColorArgb = _inkColor.ToArgb(),
        Despeckle = (int)_despeckle.Value,
        Padding = (int)_padding.Value,
        AutoThresholdOnOpen = _autoThreshold.Checked
    };

    private void BuildUi()
    {
        // ===== Thanh công cụ trên cùng =====
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8, 8, 8, 4), WrapContents = false };
        var openBtn = new Button { Text = "Mở ảnh…", Width = 100, Height = 30 };
        var pasteBtn = new Button { Text = "Dán từ clipboard", Width = 130, Height = 30 };
        var autoBtn = new Button { Text = "Tự động ngưỡng", Width = 130, Height = 30 };
        var save280 = new Button { Text = "Lưu 280×140", Width = 120, Height = 30 };
        var save70 = new Button { Text = "Lưu 70×70", Width = 110, Height = 30 };
        var saveBoth = new Button { Text = "Lưu cả hai…", Width = 120, Height = 30 };
        openBtn.Click += OnOpen;
        pasteBtn.Click += OnPaste;
        autoBtn.Click += OnAuto;
        save280.Click += (_, _) => SaveOne(280, 140);
        save70.Click += (_, _) => SaveOne(70, 70);
        saveBoth.Click += OnSaveBoth;
        top.Controls.AddRange(new Control[] { openBtn, pasteBtn, autoBtn, new Label { Width = 20 }, save280, save70, saveBoth });

        // ===== Bảng điều khiển bên phải =====
        var side = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 320,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(10),
            BackColor = SystemColors.Control
        };

        side.Controls.Add(Section("Tên file lưu (không cần đuôi .png)"));
        _nameBox.Text = _baseName;
        side.Controls.Add(_nameBox);
        var hintLbl = new Label
        {
            Text = "Hậu tố _280x140 / _70x70 sẽ tự thêm.",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 0, 4)
        };
        side.Controls.Add(hintLbl);

        side.Controls.Add(Section("Ngưỡng nền (sáng hơn = trong suốt)"));
        side.Controls.Add(_whiteLbl);
        side.Controls.Add(_whiteBar);
        side.Controls.Add(Section("Ngưỡng mực (tối hơn = đục hoàn toàn)"));
        side.Controls.Add(_inkLbl);
        side.Controls.Add(_inkBar);
        _autoThreshold.Margin = new Padding(0, 4, 0, 0);
        side.Controls.Add(_autoThreshold);

        side.Controls.Add(Spacer());
        var recolorRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        recolorRow.Controls.Add(_recolor);
        recolorRow.Controls.Add(_colorBtn);
        side.Controls.Add(recolorRow);

        side.Controls.Add(Spacer());
        side.Controls.Add(LabeledRow("Lọc nhiễu (px):", _despeckle));
        side.Controls.Add(LabeledRow("Lề chừa (%):", _padding));

        side.Controls.Add(Spacer());
        side.Controls.Add(Section("Xem trước 280×140"));
        _preview280.Size = new Size(280, 140);
        _preview280.Margin = new Padding(0, 2, 0, 8);
        side.Controls.Add(_preview280);
        side.Controls.Add(Section("Xem trước 70×70"));
        _preview70.Size = new Size(70, 70);
        _preview70.Margin = new Padding(0, 2, 0, 8);
        side.Controls.Add(_preview70);

        // ===== Khu ảnh: gốc | kết quả =====
        // Lưu ý: KHÔNG đặt Panel1MinSize/Panel2MinSize hay SplitterDistance ở đây —
        // lúc này SplitContainer chưa có kích thước thật nên sẽ ném InvalidOperationException.
        // Việc đó làm trong OnShown khi form đã có kích thước.
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6 };
        split.Panel1.Controls.Add(WrapTitled(_sourcePanel, "Ảnh gốc"));
        split.Panel2.Controls.Add(WrapTitled(_resultPanel, "Kết quả (đã tách nền + cắt)"));
        _split = split;

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(10, 4, 10, 4) };
        _statusLbl.Text = "Hãy mở một ảnh chữ ký để bắt đầu.";
        bottom.Controls.Add(_statusLbl);

        Controls.Add(split);
        Controls.Add(side);
        Controls.Add(top);
        Controls.Add(bottom);
    }

    private static Label Section(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        Margin = new Padding(0, 8, 0, 2)
    };

    private static Control Spacer() => new Panel { Height = 6, Width = 10 };

    private static Control LabeledRow(string label, Control ctrl)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        row.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        row.Controls.Add(ctrl);
        return row;
    }

    private static Control WrapTitled(Control inner, string title)
    {
        var host = new Panel { Dock = DockStyle.Fill };
        var lbl = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 22,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            BackColor = SystemColors.ControlLight,
            Padding = new Padding(6, 0, 0, 0)
        };
        host.Controls.Add(inner);
        host.Controls.Add(lbl);
        return host;
    }

    private void OnParamChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        UpdateThresholdLabels();
        if (_sourceWork == null) return;
        _debounce.Stop();
        _debounce.Start();
    }

    private void UpdateThresholdLabels()
    {
        _whiteLbl.Text = $"Ngưỡng nền: {_whiteBar.Value}";
        _inkLbl.Text = $"Ngưỡng mực: {_inkBar.Value}";
    }

    private void OnPickColor(object? sender, EventArgs e)
    {
        using var dlg = new ColorDialog { Color = _inkColor, FullOpen = true };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _inkColor = dlg.Color;
            _colorBtn.BackColor = _inkColor;
            OnParamChanged(null, EventArgs.Empty);
        }
    }

    private void OnOpen(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Chọn ảnh chữ ký gốc",
            Filter = "Ảnh (*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif|Tất cả (*.*)|*.*",
            InitialDirectory = Directory.Exists(_lastDir) ? _lastDir : null
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            LoadImage(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Không mở được ảnh:\n" + ex.Message, "Lỗi",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadImage(string path)
    {
        _lastDir = Path.GetDirectoryName(path) ?? _lastDir;
        var loaded = LoadBitmapUnlocked(path);
        SetSource(loaded, Path.GetFileNameWithoutExtension(path));
    }

    private void OnPaste(object? sender, EventArgs e)
    {
        try
        {
            // Trường hợp 1: clipboard chứa ảnh bitmap (ảnh chụp màn hình, Copy ảnh...).
            if (Clipboard.ContainsImage())
            {
                using var img = Clipboard.GetImage();
                if (img != null)
                {
                    SetSource(new Bitmap(img), "chu_ky_clipboard");
                    _statusLbl.Text = "Đã dán ảnh từ clipboard.";
                    return;
                }
            }

            // Trường hợp 2: clipboard chứa tệp ảnh đã Copy trong Explorer.
            if (Clipboard.ContainsFileDropList())
            {
                foreach (string? f in Clipboard.GetFileDropList())
                {
                    if (f != null && IsImageFile(f) && File.Exists(f))
                    {
                        LoadImage(f);
                        _statusLbl.Text = "Đã dán tệp ảnh: " + Path.GetFileName(f);
                        return;
                    }
                }
            }

            MessageBox.Show(this,
                "Clipboard không có ảnh.\nHãy chụp màn hình (hoặc Copy một ảnh) rồi bấm lại.",
                "Không có ảnh trong clipboard",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Không dán được ảnh:\n" + ex.Message, "Lỗi",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Nạp một ảnh nguồn mới (từ tệp hoặc clipboard) và cập nhật toàn bộ.</summary>
    private void SetSource(Bitmap loaded, string suggestedName)
    {
        _baseName = SanitizeName(suggestedName);
        _nameBox.Text = _baseName; // gợi ý tên, người dùng có thể sửa lại

        _sourceFull?.Dispose();
        _sourceWork?.Dispose();
        _sourceFull = loaded;
        _sourceWork = MakeWorkCopy(loaded, 1400);
        _sourcePanel.Image = _sourceFull;

        // Tự động đoán ngưỡng cho ảnh mới (nếu người dùng bật tùy chọn này);
        // nếu tắt thì giữ nguyên ngưỡng đã lưu / đang chỉnh.
        if (_autoThreshold.Checked)
        {
            _loading = true;
            var (white, ink) = SignatureProcessor.AutoThreshold(_sourceWork);
            _whiteBar.Value = Clamp(white, _whiteBar.Minimum, _whiteBar.Maximum);
            _inkBar.Value = Clamp(ink, _inkBar.Minimum, _inkBar.Maximum);
            _loading = false;
            UpdateThresholdLabels();
        }

        Reprocess();
    }

    private static bool IsImageFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tif" or ".tiff" or ".gif";
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Ctrl+V: dán ảnh từ clipboard (trừ khi đang gõ trong ô tên file).
        if (e.Control && e.KeyCode == Keys.V && !_nameBox.Focused)
        {
            OnPaste(this, EventArgs.Empty);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        base.OnKeyDown(e);
    }

    /// <summary>Tên file cơ sở người dùng nhập (đã lọc ký tự cấm), fallback về tên gợi ý.</summary>
    private string EffectiveBaseName()
    {
        string n = SanitizeName(_nameBox.Text);
        return string.IsNullOrWhiteSpace(n) ? _baseName : n;
    }

    private void OnAuto(object? sender, EventArgs e)
    {
        if (_sourceWork == null) return;
        _loading = true;
        var (white, ink) = SignatureProcessor.AutoThreshold(_sourceWork);
        _whiteBar.Value = Clamp(white, _whiteBar.Minimum, _whiteBar.Maximum);
        _inkBar.Value = Clamp(ink, _inkBar.Minimum, _inkBar.Maximum);
        _loading = false;
        UpdateThresholdLabels();
        Reprocess();
    }

    private ProcessOptions CurrentOptions() => new()
    {
        WhiteThreshold = _whiteBar.Value,
        InkThreshold = _inkBar.Value,
        Recolor = _recolor.Checked,
        InkColor = _inkColor,
        DespeckleSize = (int)_despeckle.Value,
        PaddingPercent = (int)_padding.Value
    };

    private void Reprocess()
    {
        if (_sourceWork == null) return;
        try
        {
            Cursor = Cursors.WaitCursor;
            var opt = CurrentOptions();
            var trimmed = SignatureProcessor.RemoveBackground(_sourceWork, opt);

            _trimmedPreview?.Dispose();
            _trimmedPreview = trimmed;
            _resultPanel.Image = trimmed;

            using var p280 = SignatureProcessor.FitTo(trimmed, 280, 140);
            using var p70 = SignatureProcessor.FitTo(trimmed, 70, 70);
            SwapPreview(_preview280, p280);
            SwapPreview(_preview70, p70);

            _statusLbl.Text = $"Kích thước nét sau cắt: {trimmed.Width}×{trimmed.Height}px. " +
                              "Chỉnh thanh trượt để tinh chỉnh rồi bấm Lưu.";
        }
        catch (Exception ex)
        {
            _statusLbl.Text = "Lỗi xử lý: " + ex.Message;
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static void SwapPreview(CheckerPanel panel, Bitmap src)
    {
        var old = panel.Image;
        panel.Image = new Bitmap(src); // bản sao độc lập để panel giữ
        old?.Dispose();
    }

    private void SaveOne(int w, int h)
    {
        if (_sourceFull == null) { WarnNoImage(); return; }
        using var dlg = new SaveFileDialog
        {
            Title = $"Lưu chữ ký {w}×{h}",
            Filter = "PNG (*.png)|*.png",
            FileName = $"{EffectiveBaseName()}_{w}x{h}.png",
            InitialDirectory = Directory.Exists(_lastDir) ? _lastDir : null
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var bmp = RenderFinal(w, h);
            bmp.Save(dlg.FileName, ImageFormat.Png);
            _lastDir = Path.GetDirectoryName(dlg.FileName) ?? _lastDir;
            _statusLbl.Text = "Đã lưu: " + dlg.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Không lưu được:\n" + ex.Message, "Lỗi",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnSaveBoth(object? sender, EventArgs e)
    {
        if (_sourceFull == null) { WarnNoImage(); return; }
        using var dlg = new SaveFileDialog
        {
            Title = "Chọn tên & thư mục (hậu tố _280x140 / _70x70 sẽ tự thêm)",
            Filter = "PNG (*.png)|*.png",
            FileName = $"{EffectiveBaseName()}.png",
            InitialDirectory = Directory.Exists(_lastDir) ? _lastDir : null
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            string dir = Path.GetDirectoryName(dlg.FileName) ?? _lastDir;
            string baseName = Path.GetFileNameWithoutExtension(dlg.FileName);
            string p1 = Path.Combine(dir, $"{baseName}_280x140.png");
            string p2 = Path.Combine(dir, $"{baseName}_70x70.png");

            using (var b1 = RenderFinal(280, 140)) b1.Save(p1, ImageFormat.Png);
            using (var b2 = RenderFinal(70, 70)) b2.Save(p2, ImageFormat.Png);

            _lastDir = dir;
            _statusLbl.Text = $"Đã lưu 2 tệp: {Path.GetFileName(p1)}, {Path.GetFileName(p2)}";
            MessageBox.Show(this, $"Đã lưu:\n{p1}\n{p2}", "Xong",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Không lưu được:\n" + ex.Message, "Lỗi",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Xử lý lại từ ảnh GỐC đầy đủ (chất lượng cao) rồi ghép vào khung đích.</summary>
    private Bitmap RenderFinal(int w, int h)
    {
        var opt = CurrentOptions();
        using var trimmed = SignatureProcessor.RemoveBackground(_sourceFull!, opt);
        return SignatureProcessor.FitTo(trimmed, w, h);
    }

    private void WarnNoImage() =>
        MessageBox.Show(this, "Bạn chưa mở ảnh nào.", "Chưa có ảnh",
            MessageBoxButtons.OK, MessageBoxIcon.Information);

    // Đọc ảnh mà không giữ khóa tệp (để có thể ghi đè/di chuyển tệp gốc).
    private static Bitmap LoadBitmapUnlocked(string path)
    {
        using var temp = new Bitmap(path);
        return new Bitmap(temp);
    }

    private static Bitmap MakeWorkCopy(Bitmap full, int maxDim)
    {
        if (full.Width <= maxDim && full.Height <= maxDim)
            return new Bitmap(full);

        double scale = Math.Min((double)maxDim / full.Width, (double)maxDim / full.Height);
        int w = Math.Max(1, (int)Math.Round(full.Width * scale));
        int h = Math.Max(1, (int)Math.Round(full.Height * scale));
        var work = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(work);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(full, 0, 0, w, h);
        return work;
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "chu_ky";
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_split != null && _split.Width > 500)
        {
            // Đặt SplitterDistance hợp lệ trước (với min-size mặc định), rồi mới siết min-size.
            int target = Math.Min(Math.Max(_split.Width / 2, 60),
                                   _split.Width - 60 - _split.SplitterWidth);
            _split.SplitterDistance = target;
            _split.Panel1MinSize = 200;
            _split.Panel2MinSize = 200;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        CurrentSettings().Save();
        _sourceFull?.Dispose();
        _sourceWork?.Dispose();
        _trimmedPreview?.Dispose();
        _preview280.Image?.Dispose();
        _preview70.Image?.Dispose();
        base.OnFormClosed(e);
    }
}
