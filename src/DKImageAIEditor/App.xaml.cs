using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace DKImageAIEditor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        base.OnStartup(e);

        try
        {
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            var logPath = WriteCrashLog("startup", exception);
            ShowFatalError(
                "프로그램 시작 중 오류가 발생했습니다.",
                exception,
                logPath);
            Shutdown(-1);
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logPath = WriteCrashLog("dispatcher", e.Exception);
        ShowFatalError(
            "프로그램 실행 중 처리하지 못한 오류가 발생했습니다.",
            e.Exception,
            logPath);
        e.Handled = true;
        Shutdown(-1);
    }

    private static void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteCrashLog("appdomain", exception);
        }
        else
        {
            WriteCrashLog("appdomain", new Exception(e.ExceptionObject?.ToString() ?? "Unknown unhandled exception."));
        }
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("task", e.Exception);
        e.SetObserved();
    }

    private static string? WriteCrashLog(string category, Exception exception)
    {
        try
        {
            var dataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DKImageAIEditor");
            var logDirectory = Path.Combine(dataRoot, "Logs");
            Directory.CreateDirectory(logDirectory);

            var logPath = Path.Combine(
                logDirectory,
                $"{category}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");

            var text = new StringBuilder()
                .AppendLine($"Time: {DateTimeOffset.Now:O}")
                .AppendLine($"Category: {category}")
                .AppendLine($"OS: {Environment.OSVersion}")
                .AppendLine($"64-bit process: {Environment.Is64BitProcess}")
                .AppendLine($"Base directory: {AppContext.BaseDirectory}")
                .AppendLine()
                .AppendLine(exception.ToString())
                .ToString();

            File.WriteAllText(logPath, text, Encoding.UTF8);
            return logPath;
        }
        catch
        {
            return null;
        }
    }

    private static void ShowFatalError(string message, Exception exception, string? logPath)
    {
        try
        {
            var details = string.IsNullOrWhiteSpace(logPath)
                ? $"{message}\n\n{exception.Message}"
                : $"{message}\n\n{exception.Message}\n\n로그:\n{logPath}";

            MessageBox.Show(
                details,
                "DK Image AI Editor 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
        }
    }
}
