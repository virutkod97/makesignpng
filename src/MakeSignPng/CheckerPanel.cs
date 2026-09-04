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

    public CheckerPanel(int checkerCell = 10)
    {
        _cell = checkerCell;
        DoubleBuffered = true;
        BackColor = Color.White;
        SetStyle(ControlStyles.ResizeRedraw, true);
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

        double scale = Math.Min((double)Width / _image.Width, (double)Height / _image.Height);
        scale = Math.Min(scale, 1.0); // không phóng to quá 100% để tránh vỡ nét khi xem
        if (scale <= 0) scale = 1;
        int dw = Math.Max(1, (int)(_image.Width * scale));
        int dh = Math.Max(1, (int)(_image.Height * scale));
        int ox = (Width - dw) / 2;
        int oy = (Height - dh) / 2;
        g.DrawImage(_image, ox, oy, dw, dh);
    }
}
