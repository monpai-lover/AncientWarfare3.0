using AncientWarfare3.content;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     入伍编年史:Postfix City.makeWarrior(Actor)(makeWarrior 在 City 自身声明,typeof 正确)。
    ///     贵族被征为战士 → 记一条"入伍从军"(war 分类)。仅贵族(ChronicleEvents 内部门槛)。
    /// </summary>
    [HarmonyPatch]
    public static class AW_EnlistPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.makeWarrior))]
        public static bool MakeWarrior_Asylum_Prefix(Actor pActor)
        {
            return RoyalAsylumRules.CanPerformProtectedRole(
                RoyalAsylumService.IsActive(pActor));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "setProfession",
            new[] { typeof(UnitProfession), typeof(bool) })]
        public static bool SetProfession_Asylum_Prefix(Actor __instance, UnitProfession pType)
        {
            return pType != UnitProfession.Warrior ||
                   RoyalAsylumRules.CanPerformProtectedRole(
                       RoyalAsylumService.IsActive(__instance));
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.makeWarrior))]
        public static void MakeWarrior_Postfix(Actor pActor)
        {
            if (pActor?.data == null || !pActor.isWarrior()) return;
            ChronicleEvents.OnEnlisted(pActor);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.getNextJob))]
        public static bool GetNextJob_Asylum_Prefix(Actor __instance, ref string __result)
        {
            if (!RoyalAsylumService.IsActive(__instance)) return true;
            __result = RoyalAsylumContent.ActorJobId;
            return false;
        }
    }
}
