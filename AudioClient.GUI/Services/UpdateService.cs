using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AudioClient.GUI.Services;

public sealed class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/orange3134/ResoniteAudioClient/releases/latest";
    private const string ExpectedGuiExeName = "AudioClient.GUI.exe";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string _appDir;

    public UpdateService(string appDir)
    {
        _appDir = Path.GetFullPath(appDir);
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ResoniteAudioClient-Updater");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    public async Task<UpdateCheckResult> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("GitHub release response was empty.");

        Version latestVersion = ParseReleaseVersion(release.TagName);
        GitHubAsset? asset = SelectZipAsset(release.Assets);

        bool isAvailable = latestVersion > NormalizeVersion(CurrentVersion);
        if (isAvailable && asset is null)
            throw new InvalidOperationException("Latest release has no ZIP asset for AudioClient.");

        return new UpdateCheckResult(
            IsAvailable: isAvailable,
            CurrentVersion: NormalizeVersion(CurrentVersion),
            LatestVersion: latestVersion,
            ReleaseName: string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name,
            ReleasePageUrl: release.HtmlUrl,
            AssetName: asset?.Name,
            AssetDownloadUrl: asset?.BrowserDownloadUrl);
    }

    public async Task<string> DownloadAndPrepareAsync(UpdateCheckResult update, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.AssetDownloadUrl))
            throw new InvalidOperationException("No update asset download URL is available.");

        string workDir = Path.Combine(Path.GetTempPath(), "AudioClientUpdate", Guid.NewGuid().ToString("N"));
        string zipPath = Path.Combine(workDir, update.AssetName ?? "AudioClient.zip");
        string extractDir = Path.Combine(workDir, "extracted");

        Directory.CreateDirectory(workDir);
        Directory.CreateDirectory(extractDir);

        using (var response = await _httpClient.GetAsync(update.AssetDownloadUrl, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = File.Create(zipPath);
            await source.CopyToAsync(destination, cancellationToken);
        }

        ZipFile.ExtractToDirectory(zipPath, extractDir);
        string payloadRoot = ResolvePayloadRoot(extractDir);
        string scriptPath = Path.Combine(workDir, "apply-update.ps1");
        File.WriteAllText(scriptPath, BuildUpdateScript(payloadRoot), new UTF8Encoding(false));

        return scriptPath;
    }

    public void StartUpdaterScript(string scriptPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\""
        };

        Process.Start(startInfo);
    }

    private string BuildUpdateScript(string payloadRoot)
    {
        string targetDir = EscapePowerShellString(_appDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string sourceDir = EscapePowerShellString(payloadRoot);
        string exePath = EscapePowerShellString(Path.Combine(_appDir, ExpectedGuiExeName));
        int processId = Environment.ProcessId;

        return $$"""
$ErrorActionPreference = 'Stop'
$targetDir = '{{targetDir}}'
$sourceDir = '{{sourceDir}}'
$exePath = '{{exePath}}'
$processId = {{processId}}

try {
    Wait-Process -Id $processId -Timeout 120 -ErrorAction SilentlyContinue
} catch {
}

Start-Sleep -Milliseconds 500
Copy-Item -Path (Join-Path $sourceDir '*') -Destination $targetDir -Recurse -Force
Start-Process -FilePath $exePath -WorkingDirectory $targetDir
""";
    }

    private static string ResolvePayloadRoot(string extractDir)
    {
        string directExe = Path.Combine(extractDir, ExpectedGuiExeName);
        if (File.Exists(directExe))
            return extractDir;

        string? nestedExe = Directory.EnumerateFiles(extractDir, ExpectedGuiExeName, SearchOption.AllDirectories)
            .OrderBy(path => path.Length)
            .FirstOrDefault();

        if (nestedExe is null)
            throw new InvalidOperationException($"Update ZIP does not contain {ExpectedGuiExeName}.");

        return Path.GetDirectoryName(nestedExe)
            ?? throw new InvalidOperationException("Could not resolve update payload directory.");
    }

    private static GitHubAsset? SelectZipAsset(GitHubAsset[]? assets)
    {
        if (assets is null || assets.Length == 0)
            return null;

        return assets
            .Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.Name.Contains("win", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(a => a.Name.Contains("x64", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(a => a.Name.Contains("AudioClient", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
    }

    private static Version ParseReleaseVersion(string tagName)
    {
        string value = tagName.Trim();
        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            value = value[1..];

        int prereleaseIndex = value.IndexOfAny(['-', '+']);
        if (prereleaseIndex >= 0)
            value = value[..prereleaseIndex];

        return Version.TryParse(value, out var version)
            ? NormalizeVersion(version)
            : throw new InvalidOperationException($"Release tag '{tagName}' is not a valid version.");
    }

    private static Version NormalizeVersion(Version version)
        => new(
            Math.Max(version.Major, 0),
            Math.Max(version.Minor, 0),
            Math.Max(version.Build, 0));

    private static string EscapePowerShellString(string value)
        => value.Replace("'", "''");

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";

        [JsonPropertyName("assets")]
        public GitHubAsset[]? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }
}

public sealed record UpdateCheckResult(
    bool IsAvailable,
    Version CurrentVersion,
    Version LatestVersion,
    string? ReleaseName,
    string ReleasePageUrl,
    string? AssetName,
    string? AssetDownloadUrl);
