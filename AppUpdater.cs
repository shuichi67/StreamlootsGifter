using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StreamlootsManager;

public sealed record AppUpdateRelease(Version Version, string Tag, string AssetName, Uri DownloadUri, string Notes, string? Sha256);

public static class AppUpdater
{
    private static readonly HttpClient Client = CreateClient();
    private const long MaximumUpdateBytes = 300L * 1024 * 1024;
    private static readonly Regex RepositoryPattern = new("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

    public static Version CurrentVersion => typeof(AppUpdater).Assembly.GetName().Version ?? new Version(0, 0, 0);
    public static bool IsValidRepository(string? value) => !string.IsNullOrWhiteSpace(value) && RepositoryPattern.IsMatch(value.Trim());

    public static async Task<AppUpdateRelease> GetLatestAsync(string repository, CancellationToken cancellationToken = default)
    {
        repository = repository.Trim();
        if (!IsValidRepository(repository)) throw new ArgumentException("Enter a public GitHub repository as owner/repository.", nameof(repository));

        using var response = await Client.GetAsync($"https://api.github.com/repos/{repository}/releases/latest", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("No public GitHub release was found. Confirm the owner/repository and publish a release with an update ZIP attached.");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagValue) ? tagValue.GetString() ?? "" : "";
        var versionText = tag.TrimStart('v', 'V');
        if (!Version.TryParse(versionText, out var version))
            throw new InvalidDataException($"Release tag '{tag}' must be a version such as 0.5.6.");

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The latest GitHub release has no update assets.");

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? "" : "";
            var urlText = asset.TryGetProperty("browser_download_url", out var urlValue) ? urlValue.GetString() ?? "" : "";
            if (!name.StartsWith("StreamlootsGifter-Update-v", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(urlText, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps || !downloadUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update ZIP does not have a trusted GitHub HTTPS download address.");
            string? digest = null;
            if (asset.TryGetProperty("digest", out var digestValue))
            {
                var raw = digestValue.GetString();
                if (raw?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true) digest = raw[7..];
            }
            var notes = root.TryGetProperty("body", out var bodyValue) ? bodyValue.GetString() ?? "" : "";
            return new AppUpdateRelease(version, tag, name, downloadUri, notes, digest);
        }
        throw new InvalidDataException("The latest release is missing a StreamlootsGifter-Update-v*.zip asset.");
    }

    public static async Task<string> DownloadAsync(AppUpdateRelease release, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(release.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumUpdateBytes)
            throw new InvalidDataException("The update download is larger than the allowed 300 MB limit.");

        var updateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamlootsGifter", "Updates");
        Directory.CreateDirectory(updateDirectory);
        var destination = Path.Combine(updateDirectory, $"{Guid.NewGuid():N}-{Path.GetFileName(release.AssetName)}");
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > MaximumUpdateBytes) throw new InvalidDataException("The update download is larger than the allowed 300 MB limit.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                if (response.Content.Headers.ContentLength is > 0 and var length) progress?.Report(Math.Clamp((double)total / length, 0, 1));
            }
        }

        if (!string.IsNullOrWhiteSpace(release.Sha256))
        {
            await using var file = File.OpenRead(destination);
            var actual = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(file, cancellationToken));
            if (!actual.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(destination);
                throw new InvalidDataException("The downloaded update did not match GitHub's SHA-256 digest.");
            }
        }
        return destination;
    }

    public static void LaunchInstaller(string archivePath, string installRoot, int processId)
    {
        if (!File.Exists(Path.Combine(installRoot, "StreamlootsGifter.exe")))
            throw new InvalidOperationException("In-app updates are available in the installed StreamlootsGifter.exe build, not when running from dotnet run.");

        var stagingDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamlootsGifter", "Updates", $"apply-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, stagingDirectory, overwriteFiles: false);
        var installer = Path.Combine(stagingDirectory, "Apply-Update.ps1");
        if (!File.Exists(installer) || !Directory.Exists(Path.Combine(stagingDirectory, "payload")))
            throw new InvalidDataException("The downloaded ZIP is not a recognized Streamloots Gifter update package.");

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-STA");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(installer);
        // Keep the path out of PowerShell's argument parser. Its process
        // environment preserves spaces and non-ASCII Windows paths verbatim.
        startInfo.Environment["STREAMLOOTS_GIFTER_INSTALL_ROOT"] = Path.GetFullPath(installRoot);
        startInfo.ArgumentList.Add("-WaitForPid");
        startInfo.ArgumentList.Add(processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-Restart");
        Process.Start(startInfo);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StreamlootsGifter", CurrentVersion.ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
