using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace DKImageAIEditor.Services;

public sealed record UpdateInfo(
    Version Version,
    string TagName,
    string AssetName,
    string AssetUrl,
    string ReleaseUrl);

public static class UpdateService
{
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/danhk0612/DK_Image_AI_Editor/releases/latest";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);

    public static string CurrentVersionText =>
        $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{Math.Max(CurrentVersion.Build, 0)}";

    public static async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var response = await HttpClient.GetAsync(LatestReleaseApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var tagName = root.GetProperty("tag_name").GetString()
            ?? throw new InvalidOperationException("최신 릴리스 태그를 확인할 수 없습니다.");
        var releaseUrl = root.GetProperty("html_url").GetString()
            ?? "https://github.com/danhk0612/DK_Image_AI_Editor/releases/latest";

        var normalizedVersion = tagName.StartsWith('v') ? tagName[1..] : tagName;
        if (!Version.TryParse(normalizedVersion, out var latestVersion))
        {
            throw new InvalidOperationException($"최신 릴리스 버전을 해석할 수 없습니다: {tagName}");
        }

        if (latestVersion <= CurrentVersion)
        {
            return null;
        }

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var assetName = asset.GetProperty("name").GetString();
            var assetUrl = asset.GetProperty("browser_download_url").GetString();

            if (!string.IsNullOrWhiteSpace(assetName) &&
                !string.IsNullOrWhiteSpace(assetUrl) &&
                assetName.StartsWith("DK-Image-AI-Editor-v", StringComparison.OrdinalIgnoreCase) &&
                assetName.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase))
            {
                return new UpdateInfo(latestVersion, tagName, assetName, assetUrl, releaseUrl);
            }
        }

        throw new InvalidOperationException("최신 릴리스에서 Windows x64 업데이트 ZIP을 찾을 수 없습니다.");
    }

    public static async Task<string> DownloadUpdateAsync(
        UpdateInfo update,
        CancellationToken cancellationToken = default)
    {
        var updateDirectory = Path.Combine(
            Path.GetTempPath(),
            "DKImageAIEditor",
            "Updates");
        Directory.CreateDirectory(updateDirectory);

        var packagePath = Path.Combine(updateDirectory, update.AssetName);

        using var response = await HttpClient.GetAsync(
            update.AssetUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(
            packagePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);
        await source.CopyToAsync(target, cancellationToken);

        return packagePath;
    }

    public static void StartUpdate(string packagePath)
    {
        var launcherPath = Path.Combine(AppContext.BaseDirectory, "DKImageAIEditor.exe");
        if (!File.Exists(launcherPath))
        {
            throw new FileNotFoundException(
                "업데이트를 적용할 DKImageAIEditor.exe 런처를 찾을 수 없습니다.",
                launcherPath);
        }

        var startInfo = new ProcessStartInfo(launcherPath)
        {
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add("--apply-update");
        startInfo.ArgumentList.Add(packagePath);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());

        Process.Start(startInfo);
        Application.Current.Shutdown();
    }

    public static void OpenReleasePage(UpdateInfo update)
    {
        Process.Start(new ProcessStartInfo(update.ReleaseUrl)
        {
            UseShellExecute = true
        });
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DK-Image-AI-Editor-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }
}
