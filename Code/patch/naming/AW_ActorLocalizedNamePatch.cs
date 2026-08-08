using AncientWarfare3.core.naming;
using AncientWarfare3.core.lineage;
using AncientWarfare3.content.figures;
using HarmonyLib;

namespace AncientWarfare3.patch.naming
{
    [HarmonyPatch]
    internal static class AW_ActorLocalizedNamePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Actor), nameof(Actor.getName))]
        private static void GetName_Prefix(Actor __instance)
        {
            if (__instance?.data == null ||
                !string.IsNullOrWhiteSpace(__instance.data.name)) return;
            AWLocalizedNameService.ProjectActor(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Actor), nameof(Actor.getName))]
        private static void GetName_Postfix(Actor __instance,
            ref string __result)
        {
            if (__instance?.data != null && __instance.data.custom_name &&
                !string.IsNullOrWhiteSpace(__instance.data.name))
            {
                __result = __instance.data.name;
                return;
            }
            // Lineage-managed actors already have an authoritative display
            // name assembled from given/family/shi data. Projecting the
            // generic localized slot here would overwrite that name.
            if (__instance?.data != null &&
                (LineageService.IsXia(__instance) ||
                 LineageService.UsesAwLineageSystem(__instance) ||
                 IsHistoricalFigure(__instance)))
            {
                // A persisted given name can outlive the last promotion
                // projection. Recompose the authoritative 氏/姓 + 名 before
                // returning it. Skip uninitialized newborns because the
                // fallback inside ApplyDisplayName legitimately reads getName.
                if (!string.IsNullOrEmpty(__instance.data.name))
                    __result = __instance.data.name;
                return;
            }
            string projected = AWLocalizedNameService.ProjectStored(
                __instance?.data);
            if (!string.IsNullOrEmpty(projected)) __result = projected;
        }

        private static bool IsHistoricalFigure(Actor pActor)
        {
            try
            {
                return pActor.hasTrait(HistoricalFigureService.TRAIT_FIGURE) ||
                       pActor.hasTrait(HistoricalFigureService.TRAIT_FIRST);
            }
            catch { return false; }
        }
    }
}
