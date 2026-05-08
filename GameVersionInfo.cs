using System;
using System.IO;
using System.Text.Json;
using Godot;

namespace SellToMerchant
{
    public enum GameChannel
    {
        Unknown,
        Stable,
        PublicBeta,
    }

    public sealed class GameVersionInfo
    {
        public string InstallDir { get; init; } = "";
        public string Version { get; init; } = "";
        public string Branch { get; init; } = "";
        public string Commit { get; init; } = "";
        public string SteamBetaKey { get; init; } = "";
        public GameChannel Channel { get; init; } = GameChannel.Unknown;

        public static GameVersionInfo Detect()
        {
            var installDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
            var releaseInfoPath = Path.Combine(installDir, "release_info.json");

            string version = "";
            string branch = "";
            string commit = "";
            if (File.Exists(releaseInfoPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(releaseInfoPath));
                    version = ReadString(doc.RootElement, "version");
                    branch = ReadString(doc.RootElement, "branch");
                    commit = ReadString(doc.RootElement, "commit");
                }
                catch (Exception ex)
                {
                    GD.Print($"[SellToMerchant] Failed to read release_info.json: {ex.Message}");
                }
            }

            var steamappsDir = Directory.GetParent(installDir)?.Parent?.FullName ?? "";
            var appManifestPath = Path.Combine(steamappsDir, "appmanifest_2868840.acf");
            string betaKey = "";
            if (File.Exists(appManifestPath))
            {
                try
                {
                    betaKey = ReadAcfValue(File.ReadAllText(appManifestPath), "BetaKey");
                }
                catch (Exception ex)
                {
                    GD.Print($"[SellToMerchant] Failed to read Steam appmanifest: {ex.Message}");
                }
            }

            return new GameVersionInfo
            {
                InstallDir = installDir,
                Version = version,
                Branch = branch,
                Commit = commit,
                SteamBetaKey = betaKey,
                Channel = DetectChannel(branch, betaKey),
            };
        }

        private static GameChannel DetectChannel(string branch, string betaKey)
        {
            if (string.Equals(betaKey, "public-beta", StringComparison.OrdinalIgnoreCase))
                return GameChannel.PublicBeta;

            if (branch.IndexOf("beta", StringComparison.OrdinalIgnoreCase) >= 0)
                return GameChannel.PublicBeta;

            if (!string.IsNullOrWhiteSpace(branch) || !string.IsNullOrWhiteSpace(betaKey))
                return GameChannel.Stable;

            return GameChannel.Unknown;
        }

        private static string ReadString(JsonElement root, string propertyName)
        {
            if (root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";

            return "";
        }

        private static string ReadAcfValue(string manifest, string key)
        {
            var marker = $"\"{key}\"";
            var keyIndex = manifest.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0) return "";

            var firstQuote = manifest.IndexOf('"', keyIndex + marker.Length);
            if (firstQuote < 0) return "";

            var secondQuote = manifest.IndexOf('"', firstQuote + 1);
            if (secondQuote < 0) return "";

            return manifest.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
        }
    }
}
