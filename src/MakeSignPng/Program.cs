using System;
using System.IO;
using System.Windows.Forms;

namespace MakeSignPng;

internal static class Program
{
    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "MakeSignPng_startup.log");

    [STAThread]
    private static void Main()
    {
        Log("Starting MakeSignPng...");

        // Bắt mọi lỗi chưa xử lý để hiện ra thay vì tắt âm thầm.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Report("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        Application.ThreadException += (_, e) =>
            Report("Application.ThreadException", e.Exception);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        try
        {
            ApplicationConfiguration.Initialize();
            Log("Config initialized, launching form...");
            Application.Run(new MainForm());
            Log("Exited normally.");
        }
        catch (Exception ex)
        {
            Report("Main", ex);
        }
    }

    private static void Log(string message)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:O}] {message}{Environment.NewLine}"); }
        catch { /* không để việc ghi log làm sập app */ }
    }

    private static void Report(string where, Exception? ex)
    {
        Log($"ERROR in {where}: {ex}");
        try
        {
            MessageBox.Show(
                (ex?.ToString() ?? "Lỗi không xác định") +
                "\n\nNhật ký đã ghi tại:\n" + LogPath,
                "MakeSignPng — Lỗi khởi động",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { /* môi trường không hiển thị được hộp thoại */ }
    }
}
