using System;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.lineage
{
    internal static class ActorBirthAffiliationService
    {
        internal static void Reconcile(Actor pBaby, City pBirthCity)
        {
            ReconcileCore(pBaby, pBirthCity, null, null);
        }

        internal static void Reconcile(Actor pBaby, Actor pParent1 = null,
            Actor pParent2 = null)
        {
            ReconcileCore(pBaby, null, pParent1, pParent2);
        }

        private static void ReconcileCore(Actor pBaby, City pBirthCity,
            Actor pParent1, Actor pParent2)
        {
            if (pBaby?.data == null || pBaby.isRekt()) return;
            if (AW3MultiplayerReplicaScope.IsApplying) return;

            City targetCity = ResolveTargetCity(
                pBaby, pBirthCity, pParent1, pParent2);
            Kingdom targetKingdom = targetCity?.kingdom;
            bool hasCity = targetCity?.data != null && !targetCity.isRekt();
            bool cityKingdomValid = targetKingdom?.data != null &&
                                    targetKingdom.asset != null &&
                                    !targetKingdom.isRekt();
            bool actorKingdomValid = pBaby.kingdom?.data != null &&
                                     pBaby.kingdom.asset != null &&
                                     !pBaby.kingdom.isRekt();
            bool matches = actorKingdomValid &&
                           ReferenceEquals(pBaby.kingdom, targetKingdom);

            if (!ActorBirthAffiliationRules.ShouldRepairCityKingdom(
                    hasCity, cityKingdomValid, actorKingdomValid, matches,
                    pBaby.city == targetCity))
                return;

            long actorId = pBaby.data.id;
            long kingdomId = targetKingdom.id;
            long cityId = targetCity.data.id;
            try
            {
                using (FormalAffiliationTransferScope.Open(
                           actorId, kingdomId, cityId))
                {
                    if (pBaby.city != targetCity)
                        pBaby.joinCity(targetCity);
                    if (pBaby.kingdom != targetKingdom)
                        pBaby.joinKingdom(targetKingdom);
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Newborn city affiliation repair failed: " +
                                    actorId + " " + error.Message);
            }

            bool repaired = pBaby.city == targetCity &&
                            pBaby.kingdom == targetKingdom;
            if (!repaired && ActorBirthAffiliationRules.ShouldQueueRetry(
                    pHasCity: pBaby.city != null || targetCity != null,
                    pCityKingdomValid: targetKingdom?.data != null &&
                                      targetKingdom.asset != null &&
                                      !targetKingdom.isRekt(),
                    pActorKingdomMatchesCity: pBaby.kingdom == targetKingdom,
                    pActorCityMatchesTarget: pBaby.city == targetCity))
                ActorKingdomSafetyService.QueueRepair(pBaby);
        }

        // The explicit city passed by ActorManager is authoritative. Once the
        // baby has been attached there, a later makeBaby callback must not use
        // a parent's current residence to move it back across a border.
        private static City ResolveTargetCity(Actor pBaby, City pBirthCity,
            Actor pParent1, Actor pParent2)
        {
            return pBirthCity ?? pBaby?.city ?? pParent1?.city ??
                   pParent2?.city;
        }
    }
}
