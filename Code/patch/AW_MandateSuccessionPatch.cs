using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using ai.behaviours;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    public static class AW_MandateSuccessionPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(KingdomBehCheckKing), nameof(KingdomBehCheckKing.execute))]
        public static bool Execute_Prefix(KingdomBehCheckKing __instance,
            Kingdom pKingdom,
            ref BehResult __result)
        {
            if (!UsesManagedLineage(pKingdom)) return true;
            if (pKingdom.data.timer_new_king > 0f) return true;
            Actor king = pKingdom.king;
            if (king?.data != null && king.isAlive()) return true;

            // 双重兜底：有注册继承人 + 无国王，跳过异步 fallback 直接即位。
            Actor successor = HeirService.PeekRegisteredHeir(pKingdom);
            if (successor?.data != null)
            {
                if (king?.data != null &&
                    AW3MultiplayerSuccessionFacade.TryDefer(pKingdom, king))
                {
                    __result = BehResult.Continue;
                    return false;
                }
                if (HeirService.PrepareRegisteredHeirForAccession(pKingdom, successor))
                {
                    __instance.makeKingAndMoveToCapital(pKingdom, successor);
                    __result = BehResult.Continue;
                    return false;
                }
            }

            // 缓存为空：走异步 fallback（档案回溯/朝廷候选），它内部已包含 RefreshHeir。
            successor = AuthoritativeSuccessionService.EnsureRegisteredCandidate(pKingdom, king);
            if (king?.data != null &&
                AW3MultiplayerSuccessionFacade.TryDefer(pKingdom, king))
            {
                __result = BehResult.Continue;
                return false;
            }
            if (successor?.data != null &&
                HeirService.PrepareRegisteredHeirForAccession(pKingdom, successor))
                __instance.makeKingAndMoveToCapital(pKingdom, successor);
            __result = BehResult.Continue;
            return false;
        }

        private static bool UsesManagedLineage(Kingdom pKingdom)
        {
            return pKingdom?.data != null &&
                   SuccessionTransitionRules.ShouldUseManagedSuccession(
                       LineageService.IsXiaKingdom(pKingdom),
                       XiaizationService.UsesXiaizedInstitutionSystem(pKingdom));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(KingdomBehCheckKing), "checkKingdomChaos")]
        public static bool CheckKingdomChaos_Prefix(Kingdom pMainKingdom)
        {
            bool managed = UsesManagedLineage(pMainKingdom);
            bool blocked = SuccessionTransitionRules
                .ShouldBlockVanillaMassFragmentation(managed);
            if (!blocked) return true;
            if (MandateService.ShouldBlockPeacefulFellApart(pMainKingdom))
                MandateService.OnPeacefulFellApartBlocked(pMainKingdom);
            return false;
        }
    }
}
