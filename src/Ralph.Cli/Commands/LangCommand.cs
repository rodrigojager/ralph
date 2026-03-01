using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Ralph.Core.Config;
using Ralph.Core.Localization;

namespace Ralph.Cli.Commands;

public sealed class LangCommand
{
    public int Execute(string subCommand, string? arg, IStringCatalog s)
    {
        var config = GlobalConfig.Load();

        switch (subCommand.ToLowerInvariant())
        {
            case "current":
                Console.WriteLine(s.Format("lang.current", config.Lang));
                return 0;

            case "list":
                Console.WriteLine(s.Get("lang.available"));
                foreach (var code in StringCatalog.Available())
                    Console.WriteLine($"  {code}{(code == config.Lang ? " *" : "")}");
                return 0;

            case "set":
                if (string.IsNullOrWhiteSpace(arg))
                {
                    Console.Error.WriteLine(s.Get("lang.usage_set"));
                    return 1;
                }
                var available = StringCatalog.Available();
                if (!available.Contains(arg, StringComparer.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine(s.Format("lang.not_found", arg, string.Join(", ", available)));
                    return 1;
                }
                config.Lang = arg;
                config.Save();
                var newS = StringCatalog.Load(arg);
                Console.WriteLine(newS.Format("lang.set_ok", arg));
                return 0;

            case "update":
                return UpdateLanguagePack(config, s);

            default:
                Console.Error.WriteLine(s.Format("lang.unknown_subcommand", subCommand));
                return 1;
        }
    }

    private static int UpdateLanguagePack(GlobalConfig config, IStringCatalog s)
    {
        Console.WriteLine(s.Get("lang.update_checking"));
        var repo = ReleaseChannel.ResolveRepo(config.ReleaseRepo);
        if (!ReleaseChannel.IsConfigured(repo))
        {
            Console.Error.WriteLine(s.Get("update.repo_not_configured"));
            return 1;
        }

        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ralph-lang-updater/1.0");
            http.Timeout = TimeSpan.FromSeconds(20);

            var api = ReleaseChannel.LatestReleaseApi(repo);
            var release = http.GetFromJsonAsync<GitHubRelease>(api).GetAwaiter().GetResult();
            if (release == null)
            {
                Console.Error.WriteLine(s.Get("lang.update_none"));
                return 0;
            }

            var langAsset = release.Assets?.FirstOrDefault(a =>
                string.Equals(a.Name, ReleaseChannel.LanguageAssetName, StringComparison.OrdinalIgnoreCase));

            if (langAsset == null || string.IsNullOrWhiteSpace(langAsset.DownloadUrl))
            {
                Console.WriteLine(s.Get("lang.update_none"));
                return 0;
            }

            var zipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}-{ReleaseChannel.LanguageAssetName}");
            try
            {
                var bytes = http.GetByteArrayAsync(langAsset.DownloadUrl).GetAwaiter().GetResult();
                File.WriteAllBytes(zipPath, bytes);

                var langDir = Path.Combine(AppContext.BaseDirectory, "lang");
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

                if (updated == 0)
                {
                    Console.WriteLine(s.Get("lang.update_none"));
                    return 0;
                }

                Console.WriteLine(s.Get("lang.update_ok"));
                return 0;
            }
            finally
            {
                if (File.Exists(zipPath))
                    File.Delete(zipPath);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(s.Format("lang.update_fail", ex.Message));
            return 1;
        }
    }

    private sealed class GitHubRelease
    {
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
