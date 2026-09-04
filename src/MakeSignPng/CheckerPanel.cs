using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MakeSignPng;

/// <summary>
/// Panel vẽ nền ô caro để hiển thị vùng trong suốt, và vẽ ảnh giữ tỉ lệ, canh giữa.
/// </summary>
public sealed class CheckerPanel : Panel
{
    private Image? _image;
    private readonly int _cell;

    // Con trỏ dạng vòng tròn cọ (dùng cho chế độ tẩy).
    private bool _brushCursor;
    private int _brushDiameter = 24;
    private Point _mouse;
    private bool _inside;

    public CheckerPanel(int checkerCell = 10)
    {
        _cell = checkerCell;
        DoubleBuffered = true;
        BackColor = Color.White;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    /// <summary>Bật hiển thị con trỏ vòng tròn cọ (ẩn con trỏ hệ thống).</summary>
    public bool BrushCursor
    {
        get => _brushCursor;
        set
        {
            _brushCursor = value;
            Cursor = value ? Cursors.None : Cursors.Default;
            Invalidate();
        }
    }

    /// <summary>Đường kính vòng tròn cọ, tính theo pixel trên panel.</summary>
    public int BrushDiameter
    {
        get => _brushDiameter;
        set { _brushDiameter = Math.Max(1, value); if (_brushCursor) Invalidate(); }
    }

    private Rectangle BrushRect(Point c)
    {
        int r = _brushDiameter / 2 + 3;
        return new Rectangle(c.X - r, c.Y - r, r * 2, r * 2);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _inside = true;
        if (_brushCursor) Invalidate(BrushRect(_mouse));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _inside = false;
        if (_brushCursor) Invalidate(BrushRect(_mouse));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_brushCursor) return;
        var dirty = Rectangle.Union(BrushRect(_mouse), BrushRect(e.Location));
        _mouse = e.Location;
        dirty.Inflate(4, 4);
        Invalidate(dirty);
    }

    /// <summary>Ảnh hiển thị. Panel không sở hữu ảnh (không tự dispose).</summary>
    public Image? Image
    {
        get => _image;
        set { _image = value; Invalidate(); }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        var light = Color.FromArgb(255, 255, 255);
        var dark = Color.FromArgb(214, 217, 223);
        using var lightBrush = new SolidBrush(light);
        using var darkBrush = new SolidBrush(dark);
        g.FillRectangle(lightBrush, ClientRectangle);
        for (int y = 0; y < Height; y += _cell)
        {
            for (int x = 0; x < Width; x += _cell)
            {
                if (((x / _cell) + (y / _cell)) % 2 == 0)
                    g.FillRectangle(darkBrush, x, y, _cell, _cell);
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_image == null) return;

        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var r = GetImageRect();
        g.DrawImage(_image, r.X, r.Y, r.Width, r.Height);

        DrawBrushCursor(g);
    }

    private void DrawBrushCursor(Graphics g)
    {
        if (!_brushCursor || !_inside) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float d = Math.Max(2, _brushDiameter);
        var rect = new RectangleF(_mouse.X - d / 2f, _mouse.Y - d / 2f, d, d);

        // Viền đôi (đen ngoài + trắng trong) để thấy rõ trên mọi nền.
        using (var outer = new Pen(Color.FromArgb(160, 0, 0, 0), 3f))
            g.DrawEllipse(outer, rect);
        using (var inner = new Pen(Color.FromArgb(230, 255, 255, 255), 1.4f))
            g.DrawEllipse(inner, rect);

        // Chấm tâm nhỏ cho dễ nhắm.
        using var dot = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
        g.FillEllipse(dot, _mouse.X - 1.5f, _mouse.Y - 1.5f, 3f, 3f);
    }

    /// <summary>Vùng (theo toạ độ panel) mà ảnh đang được vẽ, giữ tỉ lệ & canh giữa.</summary>
    public RectangleF GetImageRect()
    {
        if (_image == null) return RectangleF.Empty;
        double scale = Math.Min((double)Width / _image.Width, (double)Height / _image.Height);
        scale = Math.Min(scale, 1.0); // không phóng to quá 100%
        if (scale <= 0) scale = 1;
        float dw = Math.Max(1, (float)(_image.Width * scale));
        float dh = Math.Max(1, (float)(_image.Height * scale));
        float ox = (Width - dw) / 2f;
        float oy = (Height - dh) / 2f;
        return new RectangleF(ox, oy, dw, dh);
    }

    /// <summary>Đổi một điểm trên panel sang toạ độ pixel của ảnh; null nếu ngoài ảnh.</summary>
    public PointF? PanelToImage(Point p)
    {
        if (_image == null) return null;
        var r = GetImageRect();
        if (!r.Contains(p)) return null;
        float ix = (p.X - r.X) / r.Width * _image.Width;
        float iy = (p.Y - r.Y) / r.Height * _image.Height;
        return new PointF(ix, iy);
    }

    /// <summary>Tỉ lệ hiển thị (kích thước vẽ / kích thước ảnh).</summary>
    public float DisplayScale
    {
        get
        {
            if (_image == null || _image.Width == 0) return 1f;
            return GetImageRect().Width / _image.Width;
        }
    }
}
