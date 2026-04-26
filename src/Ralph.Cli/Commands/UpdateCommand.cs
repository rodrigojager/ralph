using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Ralph.Cli.Infrastructure;
using Ralph.Core.Config;
using Ralph.Core.Localization;

namespace Ralph.Cli.Commands;

public sealed class UpdateCommand
{
    public async Task<int> ExecuteAsync(string currentVersion, IStringCatalog s, CancellationToken ct = default)
    {
        Console.WriteLine(s.Get("update.checking"));
        Console.WriteLine(s.Format("update.current", currentVersion));

        var config = GlobalConfig.Load();
        var repo = ReleaseChannel.ResolveRepo(config.ReleaseRepo);
        if (!ReleaseChannel.IsConfigured(repo))
        {
            Console.Error.WriteLine(s.Get("update.repo_not_configured"));
            return 1;
        }

        Console.WriteLine(s.Format("update.repo", repo));

        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ralph-updater/1.0");
            http.Timeout = TimeSpan.FromSeconds(20);

            var api = ReleaseChannel.LatestReleaseApi(repo);
            var release = await http.GetFromJsonAsync<GitHubRelease>(api, ct);
            if (release == null)
            {
                Console.Error.WriteLine(s.Format("update.fail", s.Get("update.parse_release_fail")));
                return 1;
            }

            var latest = NormalizeVersionToken(release.TagName);
            var current = NormalizeVersionToken(currentVersion);
            Console.WriteLine(s.Format("update.latest", latest));

            var exePath = CurrentExecutableLocator.Resolve();
            if (string.IsNullOrWhiteSpace(exePath))
            {
                Console.Error.WriteLine(s.Format("update.fail", s.Get("update.no_exe_path")));
                return 1;
            }
            var installDir = Path.GetDirectoryName(exePath)!;

            if (string.Equals(latest, current, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(s.Get("update.up_to_date"));
                await SyncLanguageFilesAsync(http, release, installDir, s, ct);
                return 0;
            }

            Console.WriteLine(s.Format("update.available", currentVersion, latest));

            var assetName = GetBinaryAssetName();
            var asset = release.Assets?.FirstOrDefault(a =>
                string.Equals(a.Name, assetName, StringComparison.OrdinalIgnoreCase));

            if (asset == null || string.IsNullOrWhiteSpace(asset.DownloadUrl))
            {
                Console.Error.WriteLine(s.Get("update.no_asset"));
                Console.Error.WriteLine(s.Format("update.looking_for", assetName));
                Console.Error.WriteLine(s.Format("update.available_assets", string.Join(", ", release.Assets?.Select(a => a.Name) ?? [])));
                return 1;
            }

            Console.WriteLine(s.Format("update.downloading", assetName));
            var tmpPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}-{assetName}");
            var bytes = await http.GetByteArrayAsync(asset.DownloadUrl, ct);
            await File.WriteAllBytesAsync(tmpPath, bytes, ct);

            await SyncLanguageFilesAsync(http, release, installDir, s, ct);
            Console.WriteLine(s.Get("update.applying"));
            ApplyBinaryUpdate(tmpPath, exePath);

            Console.WriteLine(s.Format("update.ok", latest));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(s.Format("update.fail", ex.Message));
            return 1;
        }
    }

    private static string NormalizeVersionToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "unknown";

        var token = raw.Trim();
        var plusIndex = token.IndexOf('+');
        if (plusIndex >= 0)
            token = token[..plusIndex];
        return token.TrimStart('v', 'V');
    }

    private static string GetBinaryAssetName()
    {
        if (OperatingSystem.IsWindows()) return "ralph-win-x64.exe";
        if (OperatingSystem.IsMacOS())
        {
            return System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                == System.Runtime.InteropServices.Architecture.Arm64
                ? "ralph-osx-arm64"
                : "ralph-osx-x64";
        }
        return "ralph-linux-x64";
    }

    private static async Task SyncLanguageFilesAsync(
        HttpClient http,
        GitHubRelease release,
        string installDir,
        IStringCatalog s,
        CancellationToken ct)
    {
        var langAsset = release.Assets?.FirstOrDefault(a =>
            string.Equals(a.Name, ReleaseChannel.LanguageAssetName, StringComparison.OrdinalIgnoreCase));

        if (langAsset == null || string.IsNullOrWhiteSpace(langAsset.DownloadUrl))
        {
            Console.WriteLine(s.Get("update.lang_sync_skipped"));
            return;
        }

        Console.WriteLine(s.Get("update.lang_syncing"));

        var zipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}-{ReleaseChannel.LanguageAssetName}");
        try
        {
            var bytes = await http.GetByteArrayAsync(langAsset.DownloadUrl, ct);
            await File.WriteAllBytesAsync(zipPath, bytes, ct);

            var langDir = Path.Combine(installDir, "lang");
            Directory.CreateDirectory(langDir);
            var updated = 0;

            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    continue;

                var fileName = Path.GetFileName(entry.FullName);
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                var destination = Path.Combine(langDir, fileName);
                entry.ExtractToFile(destination, overwrite: true);
                updated++;
            }

            if (updated > 0)
                Console.WriteLine(s.Format("update.lang_synced", updated));
            else
                Console.WriteLine(s.Get("update.lang_sync_skipped"));
        }
        catch (Exception ex)
        {
            Console.WriteLine(s.Format("update.lang_sync_warn", ex.Message));
        }
        finally
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
        }
    }

    private static void ApplyBinaryUpdate(string newBinaryPath, string currentExePath)
    {
        if (OperatingSystem.IsWindows())
        {
            var backup = currentExePath + ".old";
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(currentExePath, backup);
            File.Copy(newBinaryPath, currentExePath);
        }
        else
        {
            File.Copy(newBinaryPath, currentExePath, overwrite: true);
            System.Diagnostics.Process.Start("chmod", $"+x \"{currentExePath}\"")?.WaitForExit(5_000);
        }

        File.Delete(newBinaryPath);
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? DownloadUrl { get; set; }
    }
}
