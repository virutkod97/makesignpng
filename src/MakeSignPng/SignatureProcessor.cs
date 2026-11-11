using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MakeSignPng;

/// <summary>
/// Các tùy chọn tách nền chữ ký.
/// </summary>
public sealed class ProcessOptions
{
    /// <summary>Điểm sáng (luminance) mà từ đó trở lên coi là nền -> trong suốt (0..255).</summary>
    public int WhiteThreshold { get; set; } = 200;

    /// <summary>Điểm sáng mà từ đó trở xuống coi là mực đậm -> đục hoàn toàn (0..255).</summary>
    public int InkThreshold { get; set; } = 120;

    /// <summary>Đổi toàn bộ nét sang một màu mực đồng nhất.</summary>
    public bool Recolor { get; set; } = false;

    /// <summary>Màu mực dùng khi <see cref="Recolor"/> = true.</summary>
    public Color InkColor { get; set; } = Color.FromArgb(0, 40, 160);

    /// <summary>Bỏ các đốm nhiễu nhỏ hơn số pixel này (0 = không lọc).</summary>
    public int DespeckleSize { get; set; } = 8;

    /// <summary>Lề chừa quanh chữ ký sau khi cắt, tính theo % cạnh dài (0..40).</summary>
    public int PaddingPercent { get; set; } = 6;
}

public static class SignatureProcessor
{
    /// <summary>
    /// Nạp ảnh từ tệp, trả về bản 32bpp ARGB vẽ được (không giữ khóa tệp, xử lý cả ảnh indexed).
    /// </summary>
    public static Bitmap LoadImageFile(string path)
    {
        using var temp = new Bitmap(path);
        var copy = new Bitmap(temp.Width, temp.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(copy);
        g.Clear(Color.Transparent);
        g.DrawImage(temp, new Rectangle(0, 0, temp.Width, temp.Height));
        return copy;
    }

    /// <summary>Phần mở rộng ảnh được hỗ trợ (chữ thường, có dấu chấm).</summary>
    public static bool IsSupportedImage(string path)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tif" or ".tiff" or ".gif";
    }

    /// <summary>
    /// Tách nền từ ảnh gốc, trả về ảnh 32bpp ARGB đã tự cắt sát chữ ký.
    /// Nếu không tìm thấy nét nào thì trả về bản sao trong suốt cùng kích thước.
    /// </summary>
    public static Bitmap RemoveBackground(Bitmap source, ProcessOptions opt)
    {
        int w = source.Width;
        int h = source.Height;

        var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);

        // Bảo đảm nguồn ở dạng 32bpp ARGB để đọc ổn định.
        using var src32 = To32bpp(source);

        var srcData = src32.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var dstData = result.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        int white = Math.Max(opt.WhiteThreshold, opt.InkThreshold + 1);
        int ink = opt.InkThreshold;
        int range = white - ink;

        try
        {
            int bytes = Math.Abs(srcData.Stride) * h;
            byte[] srcBuf = new byte[bytes];
            byte[] dstBuf = new byte[bytes];
            Marshal.Copy(srcData.Scan0, srcBuf, 0, bytes);

            int stride = srcData.Stride;
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x * 4;
                    byte b = srcBuf[i];
                    byte g = srcBuf[i + 1];
                    byte r = srcBuf[i + 2];
                    byte srcA = srcBuf[i + 3];

                    // Luminance chuẩn Rec.601.
                    int lum = (r * 299 + g * 587 + b * 114) / 1000;

                    int alpha;
                    if (lum >= white) alpha = 0;
                    else if (lum <= ink) alpha = 255;
                    else alpha = 255 * (white - lum) / range;

                    // Tôn trọng alpha sẵn có của ảnh gốc (ảnh đã trong suốt một phần).
                    alpha = alpha * srcA / 255;

                    if (opt.Recolor)
                    {
                        dstBuf[i] = opt.InkColor.B;
                        dstBuf[i + 1] = opt.InkColor.G;
                        dstBuf[i + 2] = opt.InkColor.R;
                    }
                    else
                    {
                        dstBuf[i] = b;
                        dstBuf[i + 1] = g;
                        dstBuf[i + 2] = r;
                    }
                    dstBuf[i + 3] = (byte)alpha;
                }
            }

            Marshal.Copy(dstBuf, 0, dstData.Scan0, bytes);
        }
        finally
        {
            src32.UnlockBits(srcData);
            result.UnlockBits(dstData);
        }

        if (opt.DespeckleSize > 0)
            Despeckle(result, opt.DespeckleSize);

        Bitmap cropped = AutoCrop(result, opt.PaddingPercent);
        if (!ReferenceEquals(cropped, result))
            result.Dispose();

        return cropped;
    }

    /// <summary>
    /// Đưa ảnh trong suốt đã cắt vào khung <paramref name="targetW"/> x <paramref name="targetH"/>,
    /// giữ tỉ lệ, canh giữa, phần thừa để trong suốt.
    /// </summary>
    public static Bitmap FitTo(Bitmap trimmed, int targetW, int targetH,
        int offsetX = 0, int offsetY = 0)
    {
        var canvas = new Bitmap(targetW, targetH, PixelFormat.Format32bppArgb);
        using var gfx = Graphics.FromImage(canvas);
        gfx.Clear(Color.Transparent);
        gfx.InterpolationMode = InterpolationMode.HighQualityBicubic;
        gfx.SmoothingMode = SmoothingMode.HighQuality;
        gfx.PixelOffsetMode = PixelOffsetMode.HighQuality;
        gfx.CompositingQuality = CompositingQuality.HighQuality;

        if (trimmed.Width == 0 || trimmed.Height == 0)
            return canvas;

        double scale = Math.Min((double)targetW / trimmed.Width, (double)targetH / trimmed.Height);
        int drawW = Math.Max(1, (int)Math.Round(trimmed.Width * scale));
        int drawH = Math.Max(1, (int)Math.Round(trimmed.Height * scale));
        int offX = (targetW - drawW) / 2 + offsetX;
        int offY = (targetH - drawH) / 2 + offsetY;

        gfx.DrawImage(trimmed, new Rectangle(offX, offY, drawW, drawH));
        return canvas;
    }

    /// <summary>Ước lượng ngưỡng nền tự động từ các pixel viền ảnh.</summary>
    public static (int white, int ink) AutoThreshold(Bitmap source)
    {
        using var src32 = To32bpp(source);
        int w = src32.Width, h = src32.Height;
        var data = src32.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int bytes = Math.Abs(data.Stride) * h;
            byte[] buf = new byte[bytes];
            Marshal.Copy(data.Scan0, buf, 0, bytes);
            int stride = data.Stride;

            // Lấy mẫu viền (khoảng 8% mỗi cạnh) để đoán độ sáng của giấy nền.
            long sum = 0;
            int count = 0;
            int marginX = Math.Max(1, w / 12);
            int marginY = Math.Max(1, h / 12);
            for (int y = 0; y < h; y++)
            {
                bool edgeRow = y < marginY || y >= h - marginY;
                int row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    if (!edgeRow && x >= marginX && x < w - marginX) continue;
                    int i = row + x * 4;
                    int lum = (buf[i + 2] * 299 + buf[i + 1] * 587 + buf[i] * 114) / 1000;
                    sum += lum;
                    count++;
                }
            }

            int bg = count > 0 ? (int)(sum / count) : 220;
            int white = Clamp(bg - 25, 60, 250);
            int ink = Clamp(white - 80, 20, white - 5);
            return (white, ink);
        }
        finally
        {
            src32.UnlockBits(data);
        }
    }

    private static Bitmap To32bpp(Bitmap source)
    {
        var bmp = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
        return bmp;
    }

    /// <summary>Cắt sát vùng có nét (alpha > 16) và chừa lề theo % cạnh dài.</summary>
    private static Bitmap AutoCrop(Bitmap bmp, int paddingPercent)
    {
        int w = bmp.Width, h = bmp.Height;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        int minX = w, minY = h, maxX = -1, maxY = -1;
        try
        {
            int bytes = Math.Abs(data.Stride) * h;
            byte[] buf = new byte[bytes];
            Marshal.Copy(data.Scan0, buf, 0, bytes);
            int stride = data.Stride;
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    byte a = buf[row + x * 4 + 3];
                    if (a > 16)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        if (maxX < 0)
            return new Bitmap(bmp); // Không có nét nào: trả bản sao.

        int boxW = maxX - minX + 1;
        int boxH = maxY - minY + 1;
        int pad = (int)Math.Round(Math.Max(boxW, boxH) * paddingPercent / 100.0);

        int cx = Math.Max(0, minX - pad);
        int cy = Math.Max(0, minY - pad);
        int cw = Math.Min(w - cx, boxW + pad * 2);
        int ch = Math.Min(h - cy, boxH + pad * 2);

        var outBmp = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(outBmp);
        g.Clear(Color.Transparent);
        g.DrawImage(bmp, new Rectangle(0, 0, cw, ch),
            new Rectangle(cx, cy, cw, ch), GraphicsUnit.Pixel);
        return outBmp;
    }

    /// <summary>Xóa các cụm pixel đục nhỏ hơn <paramref name="minSize"/> (lọc nhiễu chấm mực).</summary>
    private static void Despeckle(Bitmap bmp, int minSize)
    {
        int w = bmp.Width, h = bmp.Height;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            int bytes = Math.Abs(data.Stride) * h;
            byte[] buf = new byte[bytes];
            Marshal.Copy(data.Scan0, buf, 0, bytes);
            int stride = data.Stride;

            var visited = new bool[w * h];
            var stack = new int[w * h];
            var component = new int[w * h];

            for (int start = 0; start < w * h; start++)
            {
                if (visited[start]) continue;
                int sx = start % w, sy = start / w;
                if (buf[sy * stride + sx * 4 + 3] <= 16)
                {
                    visited[start] = true;
                    continue;
                }

                // Flood fill 8 hướng cho một cụm nét liền.
                int sp = 0, cc = 0;
                stack[sp++] = start;
                visited[start] = true;
                while (sp > 0)
                {
                    int p = stack[--sp];
                    component[cc++] = p;
                    int px = p % w, py = p / w;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = py + dy;
                        if (ny < 0 || ny >= h) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = px + dx;
                            if (nx < 0 || nx >= w) continue;
                            int np = ny * w + nx;
                            if (visited[np]) continue;
                            if (buf[ny * stride + nx * 4 + 3] > 16)
                            {
                                visited[np] = true;
                                stack[sp++] = np;
                            }
                            else
                            {
                                visited[np] = true;
                            }
                        }
                    }
                }

                if (cc < minSize)
                {
                    // Cụm quá nhỏ -> xóa (đặt alpha = 0).
                    for (int k = 0; k < cc; k++)
                    {
                        int p = component[k];
                        int px = p % w, py = p / w;
                        buf[py * stride + px * 4 + 3] = 0;
                    }
                }
            }

            Marshal.Copy(buf, 0, data.Scan0, bytes);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
}
