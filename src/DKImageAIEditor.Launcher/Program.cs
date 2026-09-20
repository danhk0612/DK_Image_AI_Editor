using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace DKImageAIEditor.Launcher;

internal static class Program
{
    private const string AppExeName = "DKImageAIEditor.App.exe";
    private const string LauncherExeName = "DKImageAIEditor.exe";
    private const string RuntimeDownloadUrl = "https://dotnet.microsoft.com/download/dotnet/10.0";
    private const string UpdaterRootName = "DKImageAIEditorUpdater";

    private const uint MbOk = 0x00000000;
    private const uint MbOkCancel = 0x00000001;
    private const uint MbIconError = 0x00000010;
    private const uint MbIconInformation = 0x00000040;
    private const int IdOk = 1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && string.Equals(args[0], "--apply-update", StringComparison.Ordinal))
            {
                return StageUpdater(args);
            }

            if (args.Length > 0 && string.Equals(args[0], "--apply-update-internal", StringComparison.Ordinal))
            {
                return ApplyUpdate(args);
            }

            CleanupUpdaterTemp();

            if (!HasDesktopRuntime10())
            {
                var result = MessageBoxW(
                    0,
                    "DK Image AI Editor를 실행하려면 Microsoft .NET 10 Desktop Runtime (x64)이 필요합니다.\n\n확인을 누르면 Microsoft 공식 다운로드 페이지를 엽니다.\n취소를 누르면 종료합니다.",
                    "필요한 구성 요소가 없습니다.",
                    MbOkCancel | MbIconInformation);

                if (result == IdOk)
                {
                    Process.Start(new ProcessStartInfo(RuntimeDownloadUrl)
                    {
                        UseShellExecute = true
                    });
                }

                return 2;
            }

            return LaunchApp(args);
        }
        catch (Exception exception)
        {
            MessageBoxW(
                0,
                $"프로그램을 시작하지 못했습니다.\n\n{exception.Message}",
                "DK Image AI Editor",
                MbOk | MbIconError);
            return 1;
        }
    }

    private static bool HasDesktopRuntime10()
    {
        var roots = new[]
        {
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet")
        };

        foreach (var root in roots.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var sharedFx = Path.Combine(root!, "shared", "Microsoft.WindowsDesktop.App");
            if (!Directory.Exists(sharedFx))
            {
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(sharedFx))
            {
                var name = Path.GetFileName(directory);
                if (Version.TryParse(name, out var version) && version.Major == 10)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int LaunchApp(string[] args)
    {
        var appPath = Path.Combine(AppContext.BaseDirectory, AppExeName);
        if (!File.Exists(appPath))
        {
            MessageBoxW(
                0,
                $"{AppExeName} 파일을 찾을 수 없습니다.\n\n프로그램 ZIP을 다시 압축 해제해 주세요.",
                "DK Image AI Editor",
                MbOk | MbIconError);
            return 3;
        }

        var startInfo = new ProcessStartInfo(appPath)
        {
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory
        };

        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process.Start(startInfo);
        return 0;
    }

    private static int StageUpdater(string[] args)
    {
        if (args.Length < 3)
        {
            throw new ArgumentException("업데이트 인수가 올바르지 않습니다.");
        }

        var packagePath = Path.GetFullPath(args[1]);
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("업데이트 패키지를 찾을 수 없습니다.", packagePath);
        }

        if (!int.TryParse(args[2], out var parentPid))
        {
            throw new ArgumentException("업데이트 대상 프로세스 ID가 올바르지 않습니다.");
        }

        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("런처 실행 파일 경로를 확인할 수 없습니다.");
        var updaterDirectory = Path.Combine(
            Path.GetTempPath(),
            UpdaterRootName,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updaterDirectory);

        var updaterPath = Path.Combine(updaterDirectory, "DKImageAIEditor.Updater.exe");
        File.Copy(processPath, updaterPath, true);

        var installDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var startInfo = new ProcessStartInfo(updaterPath)
        {
            UseShellExecute = false,
            WorkingDirectory = updaterDirectory
        };
        startInfo.ArgumentList.Add("--apply-update-internal");
        startInfo.ArgumentList.Add(packagePath);
        startInfo.ArgumentList.Add(installDirectory);
        startInfo.ArgumentList.Add(parentPid.ToString());

        Process.Start(startInfo);
        return 0;
    }

    private static int ApplyUpdate(string[] args)
    {
        if (args.Length < 4)
        {
            throw new ArgumentException("내부 업데이트 인수가 올바르지 않습니다.");
        }

        var packagePath = Path.GetFullPath(args[1]);
        var installDirectory = Path.GetFullPath(args[2]);
        if (!int.TryParse(args[3], out var parentPid))
        {
            throw new ArgumentException("업데이트 대상 프로세스 ID가 올바르지 않습니다.");
        }

        WaitForProcessExit(parentPid);

        var extractDirectory = Path.Combine(
            Path.GetTempPath(),
            UpdaterRootName,
            "extract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractDirectory);

        try
        {
            ZipFile.ExtractToDirectory(packagePath, extractDirectory, overwriteFiles: true);

            foreach (var sourcePath in Directory.EnumerateFiles(extractDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(extractDirectory, sourcePath);
                var targetPath = Path.Combine(installDirectory, relativePath);
                var targetDirectory = Path.GetDirectoryName(targetPath);

                if (!string.IsNullOrWhiteSpace(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                File.Copy(sourcePath, targetPath, true);
            }

            try
            {
                File.Delete(packagePath);
            }
            catch
            {
            }

            var launcherPath = Path.Combine(installDirectory, LauncherExeName);
            Process.Start(new ProcessStartInfo(launcherPath)
            {
                UseShellExecute = true,
                WorkingDirectory = installDirectory
            });

            return 0;
        }
        finally
        {
            try
            {
                Directory.Delete(extractDirectory, true);
            }
            catch
            {
            }
        }
    }

    private static void WaitForProcessExit(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.WaitForExit();
        }
        catch (ArgumentException)
        {
        }
    }

    private static void CleanupUpdaterTemp()
    {
        var root = Path.Combine(Path.GetTempPath(), UpdaterRootName);
        if (!Directory.Exists(root))
        {
            return;
        }

        try
        {
            Directory.Delete(root, true);
        }
        catch
        {
        }
    }
}
