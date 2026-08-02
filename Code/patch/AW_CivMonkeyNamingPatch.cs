using AncientWarfare3.content;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_CivMonkeyNamingPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(NameGenerator), nameof(NameGenerator.generateName))]
        private static bool GenerateNamePrefix(Actor pActor, MetaType pType, long pSeed,
            ref string __result)
        {
            if (!CivMonkeyNamingRules.IsCivilizedMonkey(pActor?.asset?.id)) return true;

            if (pType == MetaType.Unit)
                __result = CivMonkeyNamingRules.BuildActorName(
                    CivMonkeyNamingContent.ResolveInheritedFamily(pActor), pSeed, (int)pType);
            else if (pType == MetaType.City)
                __result = CivMonkeyNamingRules.PickCity(pSeed, (int)pType);
            else if (pType == MetaType.Clan)
                __result = CivMonkeyNamingRules.ResolveSurname(
                    CivMonkeyNamingContent.ResolveInheritedFamily(pActor),
                    CivMonkeyNamingContent.ActorSeed(pActor?.getID() ?? 0L));
            else if (pType == MetaType.Kingdom)
                __result = CivMonkeyNamingRules.PickKingdom(pSeed, (int)pType);
            else
                return true;
            return false;
        }
    }
}
