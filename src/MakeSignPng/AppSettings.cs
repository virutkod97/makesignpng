using System;
using System.IO;
using System.Text.Json;

namespace MakeSignPng;

/// <summary>
/// Cấu hình xử lý được lưu lại giữa các lần chạy (không lưu tên file).
/// </summary>
public sealed class AppSettings
{
    public int WhiteThreshold { get; set; } = 200;
    public int InkThreshold { get; set; } = 120;
    public bool Recolor { get; set; } = false;
    public int InkColorArgb { get; set; } = unchecked((int)0xFF0028A0); // xanh mặc định
    public int Despeckle { get; set; } = 8;
    public int Padding { get; set; } = 6;
    public bool AutoThresholdOnOpen { get; set; } = true;
    public int OffsetX { get; set; } = 0;   // lệch ngang cho bản 280x140
    public int OffsetY { get; set; } = 0;   // lệch dọc cho bản 280x140

    private static string FilePath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MakeSignPng");
            return Path.Combine(dir, "settings.json");
        }
    }

    /// <summary>Đọc cấu hình đã lưu; nếu lỗi/không có thì trả về mặc định.</summary>
    public static AppSettings Load()
    {
        try
        {
            string path = FilePath;
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s != null) return s;
            }
        }
        catch { /* hỏng file cấu hình thì dùng mặc định */ }
        return new AppSettings();
    }

    /// <summary>Ghi cấu hình ra đĩa (bỏ qua nếu lỗi để không làm phiền người dùng).</summary>
    public void Save()
    {
        try
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { /* không cản trở thoát app */ }
    }
}
