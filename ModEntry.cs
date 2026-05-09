using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace SellToMerchant
{
    [ModInitializer("Initialize")]
    public static class ModEntry
    {
        private const string HarmonyId = "com.selltomerchant.patch";
        private static Harmony? _harmony;

        public static void Initialize()
        {
            GD.Print("[SellToMerchant] Initializing v1.0.5...");
            AutoUpdate.CaptureMainThreadContext();

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll();

            var versionInfo = GameVersionInfo.Detect();
            GD.Print($"[SellToMerchant] Game version: {versionInfo.Version}, branch: {versionInfo.Branch}, betaKey: {versionInfo.SteamBetaKey}, channel: {versionInfo.Channel}");

            // Branch-aware auto update: when the user switches between stable
            // and public-beta, the updater switches to the matching release asset.
            _ = AutoUpdate.CheckForUpdateAfterStartupAsync(versionInfo);

            GD.Print("[SellToMerchant] Patches applied. Mod loaded.");
        }
    }
}
