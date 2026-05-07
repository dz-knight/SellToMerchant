using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace SellToMerchant
{
    public static class AutoUpdate
    {
        private const string GithubRepo = "dz-knight/SellToMerchant";
        private const string CurrentVersion = "1.0.2";
        private const string StateFileName = "SellToMerchant.update-state.json";

        private static readonly System.Net.Http.HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        static AutoUpdate()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("SellToMerchant-Mod/1.0");
        }

        public static async Task CheckForUpdateAsync(GameVersionInfo versionInfo)
        {
            if (string.IsNullOrWhiteSpace(GithubRepo) || GithubRepo.Contains("yourname/", StringComparison.OrdinalIgnoreCase))
            {
                GD.Print("[SellToMerchant] Auto update is disabled until GithubRepo is configured.");
                return;
            }

            try
            {
                var modDir = AppContext.BaseDirectory;
                var statePath = Path.Combine(modDir, StateFileName);
                var state = UpdateState.Load(statePath);
                var expectedChannel = ChannelId(versionInfo.Channel);
                var forceChannelSwitch = !string.Equals(state.InstalledChannel, expectedChannel, StringComparison.OrdinalIgnoreCase);

                var release = await FetchLatestReleaseAsync();
                if (release == null)
                    return;

                var assetName = AssetNameFor(versionInfo.Channel);
                var asset = release.Value.assets
                    .FirstOrDefault(a => string.Equals(a.name, assetName, StringComparison.OrdinalIgnoreCase));

                if (asset.name == null || asset.browser_download_url == null)
                {
                    GD.Print($"[SellToMerchant] No update asset found for channel '{expectedChannel}'. Expected: {assetName}");
                    return;
                }

                var latestVersion = release.Value.version;
                var needsVersionUpdate = IsNewer(latestVersion, CurrentVersion);

                if (!needsVersionUpdate && !forceChannelSwitch)
                {
                    state.InstalledVersion = CurrentVersion;
                    state.InstalledChannel = expectedChannel;
                    state.Save(statePath);
                    GD.Print($"[SellToMerchant] Up to date on channel '{expectedChannel}' (v{CurrentVersion}).");
                    return;
                }

                await DownloadAndInstallAsync(asset.browser_download_url, modDir);

                state.InstalledVersion = latestVersion;
                state.InstalledChannel = expectedChannel;
                state.Save(statePath);

                GD.Print($"[SellToMerchant] Updated mod package for channel '{expectedChannel}' to v{latestVersion}. Restart the game to load the new files.");
            }
            catch (HttpRequestException)
            {
                GD.Print("[SellToMerchant] Auto update failed: network unavailable.");
            }
            catch (Exception ex)
            {
                GD.Print($"[SellToMerchant] Auto update failed: {ex.Message}");
            }
        }

        private static async Task<(string version, (string? name, string? browser_download_url)[] assets)?> FetchLatestReleaseAsync()
        {
            var url = $"https://api.github.com/repos/{GithubRepo}/releases/latest";
            var response = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);

            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var version = tag.TrimStart('v');

            var assets = doc.RootElement.GetProperty("assets")
                .EnumerateArray()
                .Select(a => (
                    a.GetProperty("name").GetString(),
                    a.GetProperty("browser_download_url").GetString()))
                .ToArray();

            return (version, assets);
        }

        private static async Task DownloadAndInstallAsync(string downloadUrl, string modDir)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "SellToMerchantUpdate", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var zipPath = Path.Combine(tempDir, "update.zip");
            await File.WriteAllBytesAsync(zipPath, await Http.GetByteArrayAsync(downloadUrl));

            var extractDir = Path.Combine(tempDir, "extract");
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            foreach (var fileName in new[] { "SellToMerchant.dll", "SellToMerchant.json" })
            {
                var source = Directory.GetFiles(extractDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (source == null)
                    continue;

                File.Copy(source, Path.Combine(modDir, fileName), overwrite: true);
            }

            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
            }
        }

        private static string AssetNameFor(GameChannel channel)
        {
            return channel switch
            {
                GameChannel.PublicBeta => "SellToMerchant-public-beta.zip",
                _ => "SellToMerchant-stable.zip",
            };
        }

        private static string ChannelId(GameChannel channel)
        {
            return channel switch
            {
                GameChannel.PublicBeta => "public-beta",
                GameChannel.Stable => "stable",
                _ => "unknown",
            };
        }

        private static bool IsNewer(string incoming, string current)
        {
            try
            {
                var left = incoming.Split('.');
                var right = current.Split('.');
                var len = Math.Max(left.Length, right.Length);
                for (int i = 0; i < len; i++)
                {
                    var l = i < left.Length && int.TryParse(left[i], out var lv) ? lv : 0;
                    var r = i < right.Length && int.TryParse(right[i], out var rv) ? rv : 0;
                    if (l != r) return l > r;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
