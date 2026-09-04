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
    private FlowLayoutPanel? _saveGroup;   // nhóm 3 nút Lưu, bám theo đường ngăn cách

    private string _lastDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    private string _baseName = "chu_ky";

    // Điều khiển
    private readonly CheckerPanel _sourcePanel = new() { Dock = DockStyle.Fill };
    private readonly CheckerPanel _resultPanel = new() { Dock = DockStyle.Fill };
    private readonly CheckerPanel _preview280 = new(8);
    private readonly CheckerPanel _preview70 = new(6);

    private const int ColW = 280; // bề rộng chuẩn của cột (bằng ô tên file)
    private readonly TrackBar _whiteBar = new()
        { Minimum = 40, Maximum = 254, TickFrequency = 20, Width = ColW, AutoSize = false, Height = 32 };
    private readonly TrackBar _inkBar = new()
        { Minimum = 10, Maximum = 250, TickFrequency = 20, Width = ColW, AutoSize = false, Height = 32 };
    private readonly NumericUpDown _despeckle = new() { Minimum = 0, Maximum = 500, Value = 8, Width = 70 };
    private readonly NumericUpDown _padding = new() { Minimum = 0, Maximum = 40, Value = 6, Width = 70 };
    // Mô hình dữ liệu offset (ẩn); điều khiển bằng cụm nút mũi tên.
    private readonly NumericUpDown _offX = new() { Minimum = -140, Maximum = 140, Value = 0 };
    private readonly NumericUpDown _offY = new() { Minimum = -70, Maximum = 70, Value = 0 };
    private readonly Label _offReadout = new()
        { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(2, 3, 0, 0) };
    private readonly CheckBox _recolor = new() { Text = "Đổi màu mực", AutoSize = true };
    private readonly Button _colorBtn = new()
        { Text = "", Width = 54, Height = 22, FlatStyle = FlatStyle.Flat, Enabled = false };
    private readonly CheckBox _autoThreshold = new()
        { Text = "Tự động ngưỡng khi mở ảnh", AutoSize = true, Checked = true };
    private readonly Label _whiteLbl = new()
        { AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(0, 6, 0, 0) };
    private readonly Label _inkLbl = new()
        { AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(0, 6, 0, 0) };
    private readonly Label _statusLbl = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly TextBox _nameBox = new() { Width = 280 };

    private Color _inkColor = Color.FromArgb(0, 40, 160);
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 150 };
    private bool _loading;

    // Công cụ tẩy nền thủ công trên ảnh gốc.
    private readonly CheckBox _eraseBtn = new()
        { Text = "Tẩy nền", Appearance = Appearance.Button, AutoSize = false,
          Width = 78, Height = 26, TextAlign = ContentAlignment.MiddleCenter,
          FlatStyle = FlatStyle.System, Enabled = false };
    private readonly NumericUpDown _brushSize = new()
        { Minimum = 4, Maximum = 200, Value = 24, Width = 60,
          TextAlign = HorizontalAlignment.Center, Enabled = false };
    private readonly Button _rotateBtn = new()
        { Text = "", Width = 30, Height = 26, Font = new Font("Segoe UI", 12f), Enabled = false };
    private readonly Button _undoBtn = new() { Text = "Hoàn tác", AutoSize = true, Enabled = false };
    private readonly Button _restoreBtn = new() { Text = "Khôi phục", AutoSize = true, Enabled = false };
    private Bitmap? _pristine;                 // ảnh gốc chưa tẩy, để Khôi phục
    private readonly Stack<Bitmap> _undo = new();
    private const int UndoLimit = 10;
    private bool _erasing;
    private PointF? _lastImgPt;

    public MainForm()
    {
        Text = "MakeSignPng — Tạo chữ ký PNG trong suốt";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1160, 700);
        Size = new Size(1320, 810);
        Font = new Font("Segoe UI", 9f);
        KeyPreview = true; // để bắt Ctrl+V dán ảnh ở mọi nơi trên form
        TryLoadWindowIcon();

        BuildUi();

        _debounce.Tick += (_, _) => { _debounce.Stop(); Reprocess(); };
        _whiteBar.ValueChanged += OnParamChanged;
        _inkBar.ValueChanged += OnParamChanged;
        _despeckle.ValueChanged += OnParamChanged;
        _padding.ValueChanged += OnParamChanged;
        _offX.ValueChanged += OnParamChanged;
        _offY.ValueChanged += OnParamChanged;
        _offX.ValueChanged += (_, _) => _offReadout.Text = OffsetText();
        _offY.ValueChanged += (_, _) => _offReadout.Text = OffsetText();
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
        _offX.Value = Clamp(s.OffsetX, (int)_offX.Minimum, (int)_offX.Maximum);
        _offY.Value = Clamp(s.OffsetY, (int)_offY.Minimum, (int)_offY.Maximum);
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
        AutoThresholdOnOpen = _autoThreshold.Checked,
        OffsetX = (int)_offX.Value,
        OffsetY = (int)_offY.Value
    };

    private void BuildUi()
    {
        // ===== Thanh công cụ trên cùng =====
        var top = new Panel { Dock = DockStyle.Top, Height = 46 };
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

        var leftGroup = new FlowLayoutPanel
            { AutoSize = true, WrapContents = false, Location = new Point(8, 8), Margin = new Padding(0) };
        leftGroup.Controls.AddRange(new Control[] { openBtn, pasteBtn, autoBtn });

        save280.Margin = new Padding(0, 3, 6, 3); // trái=0 để bám đường ngăn cách, dọc = 3 như 2 nút kia
        _saveGroup = new FlowLayoutPanel
            { AutoSize = true, WrapContents = false, Location = new Point(440, 8), Margin = new Padding(0) };
        _saveGroup.Controls.AddRange(new Control[] { save280, save70, saveBoth });

        top.Controls.Add(leftGroup);
        top.Controls.Add(_saveGroup);

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

        side.Controls.Add(Section("Tên file lưu"));
        _nameBox.Text = _baseName;
        side.Controls.Add(_nameBox);

        side.Controls.Add(_whiteLbl);
        side.Controls.Add(_whiteBar);
        side.Controls.Add(_inkLbl);
        side.Controls.Add(_inkBar);
        _autoThreshold.Margin = new Padding(0, 4, 0, 0);
        side.Controls.Add(_autoThreshold);

        side.Controls.Add(Spacer());
        var recolorRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0) };
        _recolor.Margin = new Padding(0, 3, 6, 0);
        _colorBtn.Margin = new Padding(0, 2, 0, 0);
        _colorBtn.FlatAppearance.BorderColor = Color.Gray;
        _colorBtn.FlatAppearance.BorderSize = 1;
        _colorBtn.BackColor = _inkColor;
        new ToolTip().SetToolTip(_colorBtn, "Chọn màu mực");
        recolorRow.Controls.Add(_recolor);
        recolorRow.Controls.Add(_colorBtn);
        side.Controls.Add(recolorRow);

        side.Controls.Add(Spacer());
        side.Controls.Add(LabeledRow("Lọc nhiễu (px):", _despeckle));
        side.Controls.Add(LabeledRow("Lề chừa (%):", _padding));

        side.Controls.Add(Section("Căn chỉnh vị trí (280×140)"));
        side.Controls.Add(BuildOffsetPad());

        side.Controls.Add(Spacer());
        side.Controls.Add(Section("Xem trước 280×140"));
        _preview280.Size = new Size(280, 140);
        _preview280.Margin = new Padding(0, 2, 0, 8);
        side.Controls.Add(_preview280);
        side.Controls.Add(Section("Xem trước 70×70"));
        _preview70.Size = new Size(70, 70);
        _preview70.Margin = new Padding(0, 2, 0, 8);
        side.Controls.Add(_preview70);

        // Chuẩn hóa mép trái = 0 cho mọi control trong cột để thẳng hàng tuyệt đối.
        foreach (Control c in side.Controls)
            c.Margin = new Padding(0, c.Margin.Top, c.Margin.Right, c.Margin.Bottom);

        // ===== Khu ảnh: gốc | kết quả =====
        // Lưu ý: KHÔNG đặt Panel1MinSize/Panel2MinSize hay SplitterDistance ở đây —
        // lúc này SplitContainer chưa có kích thước thật nên sẽ ném InvalidOperationException.
        // Việc đó làm trong OnShown khi form đã có kích thước.
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6 };
        split.Panel1.Controls.Add(BuildSourceHost());
        split.Panel2.Controls.Add(WrapTitled(_resultPanel, "Kết quả"));
        split.SplitterMoved += (_, _) => PositionSaveGroup();
        _split = split;

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(10, 0, 10, 0) };
        _statusLbl.Text = "Hãy mở một ảnh chữ ký để bắt đầu.";
        _statusLbl.AutoSize = false;
        _statusLbl.Dock = DockStyle.Fill;
        _statusLbl.TextAlign = ContentAlignment.MiddleLeft;
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
        Margin = new Padding(0, 5, 0, 0)
    };

    private static Control Spacer() => new Panel { Height = 2, Width = 10, Margin = new Padding(0) };

    private static Control LabeledRow(string label, Control ctrl)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 1, 0, 1) };
        row.Controls.Add(new Label
        {
            Text = label, AutoSize = false, Width = 96, Height = 24,
            TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 2, 4, 0)
        });
        row.Controls.Add(ctrl);
        return row;
    }

    // Cụm 4 nút mũi tên (trên/dưới/trái/phải) xếp thành dấu +, nút giữa để về 0.
    // Đặt tọa độ bằng tay, đối xứng tuyệt đối quanh tâm cột (ColW/2).
    private Control BuildOffsetPad()
    {
        const int step = 4;
        const int bw = 36, bh = 30, gap = 3;
        Button Arrow(string text, float size = 9f) => new()
        {
            Text = text, Width = bw, Height = bh,
            Font = new Font("Segoe UI", size), FlatStyle = FlatStyle.System
        };

        var up = Arrow("▲"); var down = Arrow("▼");
        var left = Arrow("◀"); var right = Arrow("▶");
        var center = Arrow("●", 7f);
        new ToolTip().SetToolTip(center, "Về giữa (0, 0)");

        up.Click += (_, _) => Nudge(_offY, -step);
        down.Click += (_, _) => Nudge(_offY, +step);
        left.Click += (_, _) => Nudge(_offX, -step);
        right.Click += (_, _) => Nudge(_offX, +step);
        center.Click += (_, _) => { _offX.Value = 0; _offY.Value = 0; };

        int cx = ColW / 2;          // tâm ngang = tâm ô tên file/xem trước
        int colX = cx - bw / 2;     // cột giữa (up/center/down)
        up.Location = new Point(colX, 0);
        center.Location = new Point(colX, bh + gap);
        down.Location = new Point(colX, 2 * (bh + gap));
        left.Location = new Point(colX - (bw + gap), bh + gap);
        right.Location = new Point(colX + (bw + gap), bh + gap);

        _offReadout.Text = OffsetText();
        _offReadout.TextAlign = ContentAlignment.MiddleCenter;
        _offReadout.AutoSize = false;
        _offReadout.Width = ColW;
        _offReadout.Height = 20;
        _offReadout.Location = new Point(0, 3 * (bh + gap) + 4);

        var host = new Panel
        {
            Width = ColW,
            Height = _offReadout.Bottom + 2,
            Margin = new Padding(0, 2, 0, 0)
        };
        host.Controls.AddRange(new Control[] { up, center, down, left, right, _offReadout });
        return host;
    }

    private static void Nudge(NumericUpDown n, int delta)
    {
        decimal v = n.Value + delta;
        if (v < n.Minimum) v = n.Minimum;
        if (v > n.Maximum) v = n.Maximum;
        n.Value = v; // kích hoạt ValueChanged -> cập nhật kết quả & nhãn
    }

    private string OffsetText() => $"Lệch X: {(int)_offX.Value}   Y: {(int)_offY.Value} px";

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

    /// <summary>Ô "Ảnh gốc" kèm thanh công cụ tẩy nền thủ công.</summary>
    private Control BuildSourceHost()
    {
        var host = new Panel { Dock = DockStyle.Fill };

        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            WrapContents = false,
            BackColor = SystemColors.ControlLight,
            Padding = new Padding(6, 4, 6, 4)
        };
        var lbl = new Label
        {
            Text = "Ảnh gốc",
            AutoSize = true,
            Margin = new Padding(0, 6, 10, 0),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
        };
        var brushLbl = new Label { Text = "Cỡ cọ:", AutoSize = true, Margin = new Padding(6, 6, 4, 0) };

        _sourcePanel.BrushDiameter = (int)_brushSize.Value;
        _eraseBtn.CheckedChanged += (_, _) =>
        {
            _sourcePanel.BrushDiameter = (int)_brushSize.Value;
            _sourcePanel.BrushCursor = _eraseBtn.Checked;
            _statusLbl.Text = _eraseBtn.Checked
                ? "Chế độ tẩy: kéo chuột lên ảnh gốc để quét bỏ vết nền thừa."
                : "Đã tắt chế độ tẩy.";
        };
        _brushSize.ValueChanged += (_, _) => _sourcePanel.BrushDiameter = (int)_brushSize.Value;
        _undoBtn.Click += (_, _) => UndoErase();
        _restoreBtn.Click += (_, _) => RestoreOriginal();
        _rotateBtn.Margin = new Padding(3, 3, 8, 3);
        new ToolTip().SetToolTip(_rotateBtn, "Xoay 90°");
        _rotateBtn.Click += (_, _) => RotateSource();
        // Tự vẽ icon xoay bằng vector, đối xứng quanh tâm nút -> căn giữa tuyệt đối.
        _rotateBtn.Paint += (s, e) =>
        {
            var b = (Button)s!;
            DrawRotateGlyph(e.Graphics, b.ClientRectangle,
                b.Enabled ? SystemColors.ControlText : SystemColors.GrayText);
        };

        header.Controls.AddRange(new Control[]
            { lbl, _rotateBtn, _eraseBtn, brushLbl, _brushSize, _undoBtn, _restoreBtn });

        // Sự kiện chuột để tẩy.
        _sourcePanel.MouseDown += SourceMouseDown;
        _sourcePanel.MouseMove += SourceMouseMove;
        _sourcePanel.MouseUp += SourceMouseUp;

        host.Controls.Add(_sourcePanel);
        host.Controls.Add(header);
        return host;
    }

    private void SourceMouseDown(object? sender, MouseEventArgs e)
    {
        if (!_eraseBtn.Checked || e.Button != MouseButtons.Left || _sourceFull == null) return;
        var pt = _sourcePanel.PanelToImage(e.Location);
        if (pt == null) return;

        PushUndoSnapshot();          // lưu trạng thái trước nét tẩy để Hoàn tác
        _erasing = true;
        _lastImgPt = pt;
        EraseSegment(pt.Value, pt.Value);
        _sourcePanel.Invalidate();
    }

    private void SourceMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_erasing || _sourceFull == null) return;
        var pt = _sourcePanel.PanelToImage(e.Location);
        if (pt == null) return;
        EraseSegment(_lastImgPt ?? pt.Value, pt.Value);
        _lastImgPt = pt;
        _sourcePanel.Invalidate();
        _debounce.Stop();
        _debounce.Start();           // cập nhật kết quả trong lúc tẩy (có giảm tải)
    }

    private void SourceMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_erasing) return;
        _erasing = false;
        _lastImgPt = null;
        Reprocess();                 // cập nhật kết quả lần cuối sau khi nhả chuột
    }

    /// <summary>Quét màu trắng (thành nền) dọc đoạn từ a đến b, trên cả ảnh đầy đủ và bản xem trước.</summary>
    private void EraseSegment(PointF a, PointF b)
    {
        if (_sourceFull == null) return;
        float diameter = (float)_brushSize.Value / Math.Max(_sourcePanel.DisplayScale, 0.0001f);
        PaintWhiteSegment(_sourceFull, a, b, diameter);

        if (_sourceWork != null)
        {
            float ws = (float)_sourceWork.Width / _sourceFull.Width;
            PaintWhiteSegment(_sourceWork,
                new PointF(a.X * ws, a.Y * ws),
                new PointF(b.X * ws, b.Y * ws),
                diameter * ws);
        }
    }

    private static void PaintWhiteSegment(Bitmap bmp, PointF a, PointF b, float diameter)
    {
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float r = Math.Max(1f, diameter / 2f);
        using var pen = new Pen(Color.White, Math.Max(1f, diameter))
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, a, b);
        using var brush = new SolidBrush(Color.White);
        g.FillEllipse(brush, a.X - r, a.Y - r, r * 2, r * 2);
        g.FillEllipse(brush, b.X - r, b.Y - r, r * 2, r * 2);
    }

    private void PushUndoSnapshot()
    {
        if (_sourceFull == null) return;
        _undo.Push(new Bitmap(_sourceFull));
        while (_undo.Count > UndoLimit) TrimUndoBottom();
        _undoBtn.Enabled = _undo.Count > 0;
    }

    // Bỏ phần tử cũ nhất của stack khi vượt hạn mức (giải phóng bộ nhớ).
    private void TrimUndoBottom()
    {
        var keep = _undo.ToArray();          // [top..bottom]
        keep[^1].Dispose();
        _undo.Clear();
        for (int i = keep.Length - 2; i >= 0; i--) _undo.Push(keep[i]);
    }

    private void UndoErase()
    {
        if (_undo.Count == 0 || _sourceFull == null) return;
        var prev = _undo.Pop();
        _sourceFull.Dispose();
        _sourceFull = prev;
        _sourceWork?.Dispose();
        _sourceWork = MakeWorkCopy(_sourceFull, 1400);
        _sourcePanel.Image = _sourceFull;
        _undoBtn.Enabled = _undo.Count > 0;
        _sourcePanel.Invalidate();
        Reprocess();
    }

    // Vẽ biểu tượng xoay (cung tròn hở + mũi tên) căn đúng tâm hình chữ nhật.
    private static void DrawRotateGlyph(Graphics g, Rectangle bounds, Color color)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = bounds.X + bounds.Width / 2f;
        float cy = bounds.Y + bounds.Height / 2f;
        float r = Math.Min(bounds.Width, bounds.Height) * 0.28f;
        var rect = new RectangleF(cx - r, cy - r, 2 * r, 2 * r);

        using var pen = new Pen(color, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        // Cung tròn hở ~285°, chừa khe ở phía trên bên phải.
        const float startAngle = -55f;
        const float sweep = 285f;
        g.DrawArc(pen, rect, startAngle, sweep);

        // Mũi tên (tam giác đặc) tại điểm KẾT cung, hướng theo chiều kim đồng hồ.
        double a = (startAngle + sweep) * Math.PI / 180.0;
        float px = cx + r * (float)Math.Cos(a);
        float py = cy + r * (float)Math.Sin(a);
        float tx = -(float)Math.Sin(a), ty = (float)Math.Cos(a); // tiếp tuyến theo chiều kim đồng hồ
        float nx = (float)Math.Cos(a), ny = (float)Math.Sin(a);  // pháp tuyến hướng ra ngoài
        float ah = r * 0.85f, aw = r * 0.55f;
        var tip = new PointF(px + tx * ah, py + ty * ah);
        var w1 = new PointF(px + nx * aw, py + ny * aw);
        var w2 = new PointF(px - nx * aw, py - ny * aw);
        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, new[] { tip, w1, w2 });
    }

    // Xoay ảnh gốc 90° theo chiều kim đồng hồ (cả bản đầy đủ, bản xem trước và bản gốc lưu).
    private void RotateSource()
    {
        if (_sourceFull == null) return;
        _sourceFull.RotateFlip(RotateFlipType.Rotate90FlipNone);
        _sourceWork?.RotateFlip(RotateFlipType.Rotate90FlipNone);
        _pristine?.RotateFlip(RotateFlipType.Rotate90FlipNone);
        ClearUndo();                 // kích thước đổi -> bỏ lịch sử tẩy cũ
        _sourcePanel.Image = _sourceFull;
        _sourcePanel.Invalidate();
        Reprocess();
        _statusLbl.Text = "Đã xoay ảnh 90°.";
    }

    private void RestoreOriginal()
    {
        if (_pristine == null) return;
        ClearUndo();
        _sourceFull?.Dispose();
        _sourceWork?.Dispose();
        _sourceFull = new Bitmap(_pristine);
        _sourceWork = MakeWorkCopy(_sourceFull, 1400);
        _sourcePanel.Image = _sourceFull;
        _sourcePanel.Invalidate();
        Reprocess();
        _statusLbl.Text = "Đã khôi phục ảnh gốc (bỏ mọi vết tẩy).";
    }

    private void ClearUndo()
    {
        while (_undo.Count > 0) _undo.Pop().Dispose();
        _undoBtn.Enabled = false;
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

        // Bảo đảm ảnh vẽ được (ảnh indexed như GIF/PNG-8 phải chuyển sang 32bpp).
        var full = EnsureDrawable(loaded);
        if (!ReferenceEquals(full, loaded)) loaded.Dispose();

        _sourceFull?.Dispose();
        _sourceWork?.Dispose();
        _sourceFull = full;
        _sourceWork = MakeWorkCopy(full, 1400);
        _sourcePanel.Image = _sourceFull;

        // Lưu bản gốc để "Khôi phục" và bật công cụ tẩy.
        _pristine?.Dispose();
        _pristine = new Bitmap(full);
        ClearUndo();
        _rotateBtn.Enabled = true;
        _eraseBtn.Enabled = true;
        _brushSize.Enabled = true;
        _restoreBtn.Enabled = true;

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

            using var p280 = SignatureProcessor.FitTo(trimmed, 280, 140, (int)_offX.Value, (int)_offY.Value);
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
        bool isMain = w == 280 && h == 140;           // offset chỉ áp cho bản 280×140
        return SignatureProcessor.FitTo(trimmed, w, h,
            isMain ? (int)_offX.Value : 0,
            isMain ? (int)_offY.Value : 0);
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

    // Chuyển ảnh indexed (GIF/PNG-8...) sang 32bpp để có thể vẽ (tẩy) lên được.
    private static Bitmap EnsureDrawable(Bitmap src)
    {
        if ((src.PixelFormat & PixelFormat.Indexed) == 0)
            return src;
        var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.DrawImage(src, 0, 0, src.Width, src.Height);
        return bmp;
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
        PositionSaveGroup();
    }

    // Đưa nhóm nút Lưu bám vào mép trái ô "Kết quả" (đường ngăn cách).
    private void PositionSaveGroup()
    {
        if (_split == null || _saveGroup == null) return;
        _saveGroup.Left = _split.Left + _split.SplitterDistance + _split.SplitterWidth;
        _saveGroup.Top = 8;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        CurrentSettings().Save();
        _pristine?.Dispose();
        while (_undo.Count > 0) _undo.Pop().Dispose();
        _sourceFull?.Dispose();
        _sourceWork?.Dispose();
        _trimmedPreview?.Dispose();
        _preview280.Image?.Dispose();
        _preview70.Image?.Dispose();
        base.OnFormClosed(e);
    }
}
