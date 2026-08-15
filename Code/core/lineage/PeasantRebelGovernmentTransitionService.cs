using AncientWarfare3.api.multiplayer;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelGovernmentTransitionService
    {
        internal static bool TrySetClassState(Kingdom pKingdom,
            string pTargetClass)
        {
            if (!PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying ||
                pKingdom?.data == null || pKingdom.isRekt()) return false;

            string current = KingdomPolicyService.GetClassId(pKingdom);
            if (!PeasantRebelRouteRules.CanSwitchGovernment(
                    current, pTargetClass)) return false;
            if (pTargetClass == KingdomPolicyDefs.ClassBandit)
                return EnterBandit(pKingdom);
            if (current == KingdomPolicyDefs.ClassBandit)
                return PeasantRebelRouteService.ConvertBanditToFounding(
                    pKingdom,
                    PeasantRebelRouteService.ResolveOrigin(pKingdom));
            if (pTargetClass == KingdomPolicyDefs.ClassRebel)
                return EnterRebel(pKingdom);
            if (current == KingdomPolicyDefs.ClassRebel &&
                pTargetClass != KingdomPolicyDefs.ClassRebel)
                return MandateRebelService.SettleRebelGovernment(
                    pKingdom, "manual_government_change", pTargetClass);
            return KingdomPolicyService.ApplyClassStateDirect(
                pKingdom, pTargetClass);
        }

        internal static bool TryEnterBandit(Kingdom pRebel,
            Kingdom pOrigin, City pFoundingCity, Actor pFounder)
        {
            if (!PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying ||
                pRebel?.data == null || pRebel.isRekt() ||
                pOrigin?.data == null || pOrigin.isRekt() ||
                pFoundingCity?.data == null || pFoundingCity.isRekt() ||
                pFoundingCity.kingdom != pRebel ||
                pFounder?.data == null || pFounder.isRekt() ||
                PeasantRebelRouteService.IsBanditOrEntering(pRebel) ||
                KingdomPolicyService.GetClassId(pRebel) !=
                KingdomPolicyDefs.ClassRebel ||
                PeasantRebelRouteService.ResolveOrigin(pRebel) != pOrigin ||
                PeasantRebelRouteService.SafeCityCount(pRebel) <= 0 ||
                World.world?.wars == null || World.world.cities == null ||
                World.world.kingdoms == null ||
                TopTileLibrary.wall_wild == null) return false;

            pRebel.data.get(LineageKeys.MANDATE_REBEL_NAME_ROOT,
                out string root, "");
            if (string.IsNullOrWhiteSpace(root)) return false;
            return PeasantRebelRouteService.EnterExistingBanditGovernment(
                pRebel, pOrigin, pFoundingCity, pFounder);
        }

        private static bool EnterBandit(Kingdom pKingdom)
        {
            Kingdom origin =
                PeasantRebelRouteService.ResolveOrigin(pKingdom);
            City founding =
                PeasantRebelRouteService.ResolveFoundingCity(pKingdom) ??
                ResolveCurrentCity(pKingdom);
            Actor founder = pKingdom?.king ?? founding?.leader;
            return TryEnterBandit(pKingdom, origin, founding, founder);
        }

        private static bool EnterRebel(Kingdom pKingdom)
        {
            City founding = ResolveCurrentCity(pKingdom);
            Actor founder = pKingdom?.king ?? founding?.leader;
            return PeasantRebelRouteService.
                InitializeManualFoundingGovernment(pKingdom, pKingdom,
                    founding, founder);
        }

        private static City ResolveCurrentCity(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            City capital = pKingdom.capital;
            if (capital?.data != null && !capital.isRekt() &&
                capital.kingdom == pKingdom) return capital;
            try
            {
                foreach (City city in pKingdom.getCities())
                    if (city?.data != null && !city.isRekt() &&
                        city.kingdom == pKingdom) return city;
            }
            catch { }
            return null;
        }
    }
}
