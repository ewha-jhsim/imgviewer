using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ImgViewer.Services;

/// <summary>One release: its version, the installer download URL, and release notes.</summary>
public sealed record UpdateInfo(Version Version, string DownloadUrl, string? Notes);

/// <summary>
/// Self-updater backed by <b>GitHub Releases</b>. On demand it asks the GitHub API for the
/// latest release, compares its tag (e.g. <c>v1.1.0</c>) with the running build, and — if
/// newer — downloads the release's <c>Setup.exe</c> asset and runs it silently. Because the
/// app is installed under Program Files, the installer (not an in-place file swap) performs
/// the upgrade: a helper script waits for this app to exit, runs the installer silently
/// (elevating via UAC once), then relaunches the freshly-installed app.
///
/// Configure <see cref="GitHubOwner"/> / <see cref="GitHubRepo"/> below. You can also
/// override the API URL at runtime by dropping an <c>update.url</c> file (containing just
/// the URL) next to the .exe.
/// </summary>
public static class Updater
{
    public const string GitHubOwner = "ewha-jhsim";
    public const string GitHubRepo = "imgviewer";

    private static readonly HttpClient Http = CreateClient();

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>False while the GitHub repo is still the unedited placeholder.</summary>
    public static bool IsConfigured =>
        !ApiUrl.Contains("your-github-username", StringComparison.OrdinalIgnoreCase);

    /// <summary>The GitHub "latest release" API endpoint (or an <c>update.url</c> override).</summary>
    public static string ApiUrl
    {
        get
        {
            try
            {
                string? dir = Path.GetDirectoryName(Environment.ProcessPath);
                string overridePath = Path.Combine(dir ?? ".", "update.url");
                if (File.Exists(overridePath))
                {
                    string url = File.ReadAllText(overridePath).Trim();
                    if (!string.IsNullOrEmpty(url))
                        return url;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Updater: reading update.url failed: {ex}");
            }
            return $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
        }
    }

    /// <summary>
    /// Queries GitHub for the latest release and returns it only if its tag is a newer
    /// version than the running build and it carries a downloadable installer asset.
    /// </summary>
    public static async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        string json = await Http.GetStringAsync(ApiUrl, ct).ConfigureAwait(false);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        // Tag like "v1.2.0" -> Version 1.2.0
        string? tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        Version? remote = ParseTag(tag);
        if (remote is null || remote <= CurrentVersion)
            return null;

        string? notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;

        // Find the installer asset (prefer a "*Setup*.exe", else any ".exe").
        string? url = FindInstallerAsset(root);
        if (string.IsNullOrWhiteSpace(url))
            return null;

        return new UpdateInfo(remote, url!, notes);
    }

    private static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;
        string cleaned = tag.TrimStart('v', 'V').Trim();
        return Version.TryParse(cleaned, out Version? v) ? v : null;
    }

    private static string? FindInstallerAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        string? anyExe = null;
        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string? name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            string? dl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (name is null || dl is null)
                continue;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;

            anyExe ??= dl;
            if (name.Contains("setup", StringComparison.OrdinalIgnoreCase))
                return dl; // best match
        }
        return anyExe;
    }

    /// <summary>
    /// Downloads the installer to a temp file, reporting progress (0..1). Returns the path.
    /// </summary>
    public static async Task<string> DownloadAsync(
        UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"ImgViewer-{info.Version}-Setup.exe");

        using HttpResponseMessage resp = await Http
            .GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        long? total = resp.Content.Headers.ContentLength;
        await using Stream src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            read += n;
            if (total is > 0)
                progress?.Report((double)read / total.Value);
        }
        progress?.Report(1.0);
        return tempFile;
    }

    /// <summary>
    /// Spawns a detached helper that waits for this process to exit, runs the downloaded
    /// installer silently (which elevates via UAC and upgrades the Program Files install),
    /// then relaunches the app. Call <c>Close()</c> right after so the helper can proceed.
    /// </summary>
    public static void ApplyAndRestart(string installerPath)
    {
        string target = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine current executable path.");
        int pid = Environment.ProcessId;

        string script = Path.Combine(Path.GetTempPath(), $"imgviewer-update-{pid}.cmd");
        string cmd =
            "@echo off\r\n" +
            "setlocal\r\n" +
            ":wait\r\n" +
            $"tasklist /fi \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul\r\n" +
            "if not errorlevel 1 (\r\n" +
            "  ping -n 2 127.0.0.1 >nul\r\n" +
            "  goto wait\r\n" +
            ")\r\n" +
            // The installer's own manifest triggers the UAC elevation prompt.
            $"\"{installerPath}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART\r\n" +
            $"start \"\" \"{target}\"\r\n" +
            $"del \"{installerPath}\" >nul 2>&1\r\n" +
            "del \"%~f0\"\r\n";
        File.WriteAllText(script, cmd);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{script}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        Process.Start(psi);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // GitHub requires a User-Agent and recommends an explicit API version header.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ImgViewer/{CurrentVersion}");
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }
}
