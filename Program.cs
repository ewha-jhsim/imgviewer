using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ImgViewer.Services;
using ImgViewer.UI;

namespace ImgViewer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Surface any startup failure instead of silently exiting (a GUI app has no
        // console, so unhandled errors would otherwise be invisible).
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportFatal(e.ExceptionObject as Exception);

        try
        {
            // Command-line maintenance verbs for default-app / file-association setup.
            if (args.Length > 0)
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "--register":
                        return DefaultApp.Register(machineWide: false) ? 0 : 1;
                    case "--unregister":
                        return DefaultApp.Unregister(machineWide: false) ? 0 : 1;
                }
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string? initialPath = args.Length > 0 && File.Exists(args[0]) ? args[0] : null;
            Application.Run(new MainForm(initialPath));
            return 0;
        }
        catch (Exception ex)
        {
            ReportFatal(ex);
            return 1;
        }
    }

    /// <summary>Writes the error to a log file and shows it, so failures are never silent.</summary>
    private static void ReportFatal(Exception? ex)
    {
        string text = ex?.ToString() ?? "Unknown error";
        string logPath = WriteLog(text);

        try
        {
            MessageBox.Show(
                $"ImgViewer failed to start:\n\n{ex?.Message}\n\nDetails written to:\n{logPath}",
                "ImgViewer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch
        {
            // If even the message box can't be shown, the log file is our record.
        }
    }

    private static string WriteLog(string text)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImgViewer");
            Directory.CreateDirectory(dir);
            string logPath = Path.Combine(dir, "crash.log");
            File.AppendAllText(logPath,
                $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===={Environment.NewLine}{text}{Environment.NewLine}{Environment.NewLine}",
                Encoding.UTF8);
            return logPath;
        }
        catch
        {
            return "(could not write log file)";
        }
    }
}
