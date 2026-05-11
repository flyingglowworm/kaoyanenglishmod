using System.Linq;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using KaoyanEnglishMod.Relics;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;
using MegaCrit.Sts2.Core.Saves;

namespace KaoyanEnglishMod.Patches;

[HarmonyPatch(typeof(Ironclad), nameof(Ironclad.StartingRelics), MethodType.Getter)]
public static class KaoyanLexiconCompendiumStarterRelicPatch
{
    public static void Postfix(ref IReadOnlyList<RelicModel> __result)
    {
        var kaoyanLexicon = ModelDb.Relic<KaoyanLexicon>();
        if (__result.Any(relic => relic.Id == kaoyanLexicon.Id))
        {
            return;
        }

        var relics = __result.ToList();
        relics.Add(kaoyanLexicon);
        __result = relics;
    }
}

[HarmonyPatch(typeof(Player), "PopulateStartingRelics")]
public static class KaoyanLexiconStartingRelicPatch
{
    public static void Postfix(Player __instance)
    {
        if (__instance.Relics.Any(relic => relic is KaoyanLexicon))
        {
            return;
        }

        var kaoyanLexicon = ModelDb.Relic<KaoyanLexicon>().ToMutable();
        kaoyanLexicon.FloorAddedToDeck = 1;
        SaveManager.Instance.MarkRelicAsSeen(kaoyanLexicon);
        __instance.AddRelicInternal(kaoyanLexicon);
    }
}

[HarmonyPatch(typeof(NRelicCollectionEntry), nameof(NRelicCollectionEntry._Ready))]
public static class KaoyanLexiconLockedCollectionIconPatch
{
    public static void Postfix(NRelicCollectionEntry __instance)
    {
        if (__instance.relic is not KaoyanLexicon || __instance.ModelVisibility != ModelVisibility.Locked)
        {
            return;
        }

        var relicHolder = __instance.GetNodeOrNull<Control>("RelicHolder");
        if (relicHolder == null)
        {
            return;
        }

        foreach (var child in relicHolder.GetChildren())
        {
            child.QueueFreeSafely();
        }

        var relicNode = NRelic.Create(__instance.relic.ToMutable(), NRelic.IconSize.Small);
        if (relicNode == null)
        {
            return;
        }

        relicHolder.AddChildSafely(relicNode);
        relicNode.Icon.SelfModulate = new Color(0.03f, 0.04f, 0.045f, 0.96f);
        relicNode.Outline.SelfModulate = new Color(0.68f, 0.82f, 0.82f, 0.8f);
        relicNode.MouseFilter = Control.MouseFilterEnum.Ignore;
        relicNode.FocusMode = Control.FocusModeEnum.None;
        AccessTools.Field(typeof(NRelicCollectionEntry), "_relicNode")?.SetValue(__instance, relicNode);
    }
}
