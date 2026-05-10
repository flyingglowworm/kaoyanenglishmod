using HarmonyLib;
using KaoyanEnglishMod.Relics;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models.RelicPools;

namespace KaoyanEnglishMod;

[ModInitializer(nameof(ModLoaded))]
public static class KaoyanEnglishEntry
{
    public const string ModId = "KaoyanEnglishMod";
    public const string HarmonyId = "MuYezhou.KaoyanEnglishMod";

    private static Harmony? _harmony;

    public static void ModLoaded()
    {
        Log.Warn($"{ModId} loaded.");

        ModHelper.AddModelToPool(typeof(IroncladRelicPool), typeof(KaoyanLexicon));

        _harmony = new Harmony(HarmonyId);
        _harmony.PatchAll();

        Log.Warn($"{ModId} Harmony patches applied.");
    }
}
