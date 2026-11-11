using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MakeSignPng;

/// <summary>
/// Cửa sổ xử lý hàng loạt: tách nền toàn bộ ảnh trong thư mục nguồn và lưu kết quả
/// vào thư mục đích, giữ nguyên tên tệp gốc.
/// </summary>
public sealed class BatchForm : Form
{
    private readonly ProcessOptions _opt;
    private readonly int _offX, _offY;

    private readonly TextBox _srcBox = new() { Width = 420 };
    private readonly TextBox _dstBox = new() { Width = 420 };
    private readonly Button _srcBtn = new() { Text = "Chọn…", Width = 70 };
    private readonly Button _dstBtn = new() { Text = "Chọn…", Width = 70 };
    private readonly CheckBox _size280 = new() { Text = "280 × 140", AutoSize = true, Checked = true };
    private readonly CheckBox _size70 = new() { Text = "70 × 70", AutoSize = true, Checked = true };
    private readonly CheckBox _autoThr = new() { Text = "Tự động ngưỡng cho từng ảnh", AutoSize = true };
    private readonly Button _startBtn = new() { Text = "Bắt đầu", Width = 110, Height = 30 };
    private readonly Button _closeBtn = new() { Text = "Đóng", Width = 90, Height = 30 };
    private readonly ProgressBar _progress = new() { Width = 500, Height = 18 };
    private readonly Label _progLbl = new() { AutoSize = true, Text = "" };
    private readonly TextBox _log = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        Width = 500, Height = 180, Font = new Font("Consolas", 8.5f)
    };

    private CancellationTokenSource? _cts;
    private bool _running;

    public BatchForm(ProcessOptions options, int offsetX, int offsetY, bool autoThreshold, string? initialDir)
    {
        _opt = options;
        _offX = offsetX;
        _offY = offsetY;
        _autoThr.Checked = autoThreshold;

        Text = "Xử lý hàng loạt theo thư mục";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 470);
        Font = new Font("Segoe UI", 9f);

        if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
            _srcBox.Text = initialDir;

        BuildUi();

        _srcBtn.Click += (_, _) => PickFolder(_srcBox, "Chọn thư mục nguồn (chứa ảnh gốc)");
        _dstBtn.Click += (_, _) => PickFolder(_dstBox, "Chọn thư mục đích (nơi lưu PNG)");
        _startBtn.Click += OnStart;
        _closeBtn.Click += (_, _) => Close();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            AutoSize = false
        };

        Control Row(string title, TextBox box, Button btn)
        {
            var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 6) };
            box.Margin = new Padding(0, 1, 6, 0);
            btn.Margin = new Padding(0, 0, 0, 0);
            p.Controls.Add(box);
            p.Controls.Add(btn);
            var host = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            host.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 2) });
            host.Controls.Add(p);
            return host;
        }

        root.Controls.Add(Row("Thư mục nguồn", _srcBox, _srcBtn));
        root.Controls.Add(Row("Thư mục đích", _dstBox, _dstBtn));

        var opts = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 6) };
        opts.Controls.Add(new Label { Text = "Kích thước xuất:", AutoSize = true, Margin = new Padding(0, 4, 8, 0) });
        _size280.Margin = new Padding(0, 4, 12, 0);
        _size70.Margin = new Padding(0, 4, 0, 0);
        opts.Controls.Add(_size280);
        opts.Controls.Add(_size70);
        root.Controls.Add(opts);

        _autoThr.Margin = new Padding(0, 0, 0, 6);
        root.Controls.Add(_autoThr);

        var note = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 0, 8),
            Text = "• Giữ nguyên tên tệp gốc (đổi đuôi .png).\n" +
                   "• Chọn cả hai kích thước → tạo thư mục con 280x140 / 70x70 trong thư mục đích.\n" +
                   "• Dùng các thiết lập (ngưỡng, màu, lọc nhiễu, lề, lệch) hiện có trên cửa sổ chính."
        };
        root.Controls.Add(note);

        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
        _startBtn.Margin = new Padding(0, 0, 10, 0);
        actions.Controls.Add(_startBtn);
        actions.Controls.Add(_closeBtn);
        root.Controls.Add(actions);

        _progress.Margin = new Padding(0, 2, 0, 2);
        root.Controls.Add(_progress);
        _progLbl.Margin = new Padding(0, 0, 0, 6);
        root.Controls.Add(_progLbl);
        root.Controls.Add(_log);

        Controls.Add(root);
    }

    private static void PickFolder(TextBox target, string description)
    {
        using var dlg = new FolderBrowserDialog { Description = description, UseDescriptionForTitle = true };
        if (Directory.Exists(target.Text)) dlg.SelectedPath = target.Text;
        if (dlg.ShowDialog() == DialogResult.OK)
            target.Text = dlg.SelectedPath;
    }

    private void AppendLog(string line)
    {
        _log.AppendText(line + Environment.NewLine);
    }

    private async void OnStart(object? sender, EventArgs e)
    {
        if (_running)
        {
            _cts?.Cancel();   // nút đang ở chế độ "Dừng"
            return;
        }

        string src = _srcBox.Text.Trim();
        string dst = _dstBox.Text.Trim();

        if (!Directory.Exists(src))
        {
            MessageBox.Show(this, "Thư mục nguồn không tồn tại.", "Thiếu thư mục nguồn",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(dst))
        {
            MessageBox.Show(this, "Hãy chọn thư mục đích.", "Thiếu thư mục đích",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!_size280.Checked && !_size70.Checked)
        {
            MessageBox.Show(this, "Hãy chọn ít nhất một kích thước xuất.", "Chưa chọn kích thước",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (Path.GetFullPath(src).Equals(Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase))
        {
            if (MessageBox.Show(this,
                    "Thư mục đích trùng thư mục nguồn. Tệp .png cùng tên sẽ bị ghi đè. Tiếp tục?",
                    "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
        }

        var files = Directory.EnumerateFiles(src)
            .Where(SignatureProcessor.IsSupportedImage)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            MessageBox.Show(this, "Không tìm thấy ảnh nào (png/jpg/jpeg/bmp/tif/tiff/gif) trong thư mục nguồn.",
                "Không có ảnh", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        bool both = _size280.Checked && _size70.Checked;
        string dir280 = both ? Path.Combine(dst, "280x140") : dst;
        string dir70 = both ? Path.Combine(dst, "70x70") : dst;
        bool do280 = _size280.Checked, do70 = _size70.Checked;
        bool autoThr = _autoThr.Checked;

        try
        {
            Directory.CreateDirectory(dst);
            if (do280) Directory.CreateDirectory(dir280);
            if (do70) Directory.CreateDirectory(dir70);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Không tạo được thư mục đích:\n" + ex.Message, "Lỗi",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        SetRunning(true);
        _log.Clear();
        _progress.Minimum = 0;
        _progress.Maximum = files.Count;
        _progress.Value = 0;
        _progLbl.Text = $"0 / {files.Count}";
        AppendLog($"Bắt đầu: {files.Count} ảnh.");

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var progress = new Progress<BatchProgress>(p =>
        {
            _progress.Value = Math.Min(p.Done, _progress.Maximum);
            _progLbl.Text = $"{p.Done} / {files.Count}";
            if (p.Message != null) AppendLog(p.Message);
        });

        int ok = 0, fail = 0;
        try
        {
            (ok, fail) = await Task.Run(() => ProcessAll(
                files, dir280, dir70, do280, do70, autoThr, token, progress), token);
            AppendLog($"Hoàn tất: {ok} thành công, {fail} lỗi.");
        }
        catch (OperationCanceledException)
        {
            AppendLog("Đã dừng theo yêu cầu.");
        }
        catch (Exception ex)
        {
            AppendLog("Lỗi: " + ex.Message);
        }
        finally
        {
            SetRunning(false);
            _cts.Dispose();
            _cts = null;
        }
    }

    private (int ok, int fail) ProcessAll(
        List<string> files, string dir280, string dir70,
        bool do280, bool do70, bool autoThr,
        CancellationToken token, IProgress<BatchProgress> progress)
    {
        int ok = 0, fail = 0, done = 0;
        foreach (string file in files)
        {
            token.ThrowIfCancellationRequested();
            string name = Path.GetFileNameWithoutExtension(file);
            try
            {
                using Bitmap full = SignatureProcessor.LoadImageFile(file);

                var opt = new ProcessOptions
                {
                    WhiteThreshold = _opt.WhiteThreshold,
                    InkThreshold = _opt.InkThreshold,
                    Recolor = _opt.Recolor,
                    InkColor = _opt.InkColor,
                    DespeckleSize = _opt.DespeckleSize,
                    PaddingPercent = _opt.PaddingPercent
                };
                if (autoThr)
                {
                    var (white, ink) = SignatureProcessor.AutoThreshold(full);
                    opt.WhiteThreshold = white;
                    opt.InkThreshold = ink;
                }

                using Bitmap trimmed = SignatureProcessor.RemoveBackground(full, opt);
                if (do280)
                {
                    using var b = SignatureProcessor.FitTo(trimmed, 280, 140, _offX, _offY);
                    b.Save(Path.Combine(dir280, name + ".png"), ImageFormat.Png);
                }
                if (do70)
                {
                    using var b = SignatureProcessor.FitTo(trimmed, 70, 70);
                    b.Save(Path.Combine(dir70, name + ".png"), ImageFormat.Png);
                }
                ok++;
                progress.Report(new BatchProgress(++done, $"✓ {Path.GetFileName(file)}"));
            }
            catch (Exception ex)
            {
                fail++;
                progress.Report(new BatchProgress(++done, $"✗ {Path.GetFileName(file)} — {ex.Message}"));
            }
        }
        return (ok, fail);
    }

    private void SetRunning(bool running)
    {
        _running = running;
        _startBtn.Text = running ? "Dừng" : "Bắt đầu";
        _srcBox.Enabled = _dstBox.Enabled = !running;
        _srcBtn.Enabled = _dstBtn.Enabled = !running;
        _size280.Enabled = _size70.Enabled = _autoThr.Enabled = !running;
        _closeBtn.Enabled = !running;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_running)
        {
            // Đang chạy: chặn đóng, yêu cầu dừng trước.
            _cts?.Cancel();
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }

    private readonly record struct BatchProgress(int Done, string? Message);
}
