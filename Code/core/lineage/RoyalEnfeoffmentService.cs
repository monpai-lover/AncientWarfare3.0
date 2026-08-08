using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class RoyalEnfeoffmentService
    {
        public const string Reason = "royal_enfeoffment";

        public static bool TryCreate(City pSeat, out string pReason)
        {
            pReason = "invalid_city";
            if (!IsValidSeat(pSeat, out Kingdom suzerain)) return false;
            if (!TryFindKing(suzerain, out Actor candidate))
            {
                pReason = "no_royal_candidate";
                return false;
            }

            Kingdom newKingdom = null;
            Kingdom originalKingdom = suzerain;
            try
            {
                newKingdom = World.world.kingdoms.makeNewCivKingdom(
                    candidate, pID: null, pLog: true);
                if (newKingdom?.data == null)
                {
                    pReason = "kingdom_creation_failed";
                    return false;
                }

                pSeat.setKingdom(newKingdom);
                if (pSeat.kingdom != newKingdom)
                {
                    pReason = "city_transfer_failed";
                    Rollback(newKingdom, originalKingdom, pSeat, candidate);
                    return false;
                }

                candidate.joinCity(pSeat);
                newKingdom.setCapital(pSeat);
                if (!VassalService.SetVassal(newKingdom, originalKingdom,
                        Reason))
                {
                    pReason = "vassal_creation_failed";
                    Rollback(newKingdom, originalKingdom, pSeat, candidate);
                    return false;
                }

                ChronicleEvents.OnRoyalEnfeoffment(originalKingdom,
                    newKingdom, pSeat, candidate);
                pReason = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Royal enfeoffment failed: " +
                    exception.GetType().Name + ": " + exception.Message);
                pReason = "creation_failed";
                Rollback(newKingdom, originalKingdom, pSeat, candidate);
                return false;
            }
        }

        private static bool IsValidSeat(City pSeat,
            out Kingdom pSuzerain)
        {
            pSuzerain = pSeat?.kingdom;
            return pSeat?.data != null && !pSeat.isRekt() &&
                   pSeat.isAlive() && pSuzerain?.data != null &&
                   !pSuzerain.isRekt() && pSuzerain.isCiv() &&
                   !pSuzerain.isNeutral() && pSuzerain.countCities() > 1;
        }

        private static bool TryFindKing(Kingdom pSuzerain,
            out Actor pCandidate)
        {
            pCandidate = null;
            if (pSuzerain?.data == null) return false;
            try
            {
                pSuzerain.data.get(LineageKeys.KINGDOM_HEIR_ID,
                    out long heirId, -1L);
                Actor heir = heirId < 0
                    ? null
                    : World.world?.units?.get(heirId);
                if (IsEligible(heir, pSuzerain))
                {
                    pCandidate = heir;
                    return true;
                }

                List<Actor> candidates =
                    InheritanceCandidateService.CollectRoyalCandidates(
                        pSuzerain, pSuzerain.king);
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (!IsEligible(candidates[i], pSuzerain)) continue;
                    pCandidate = candidates[i];
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static bool IsEligible(Actor pActor, Kingdom pSuzerain)
        {
            try
            {
                return pActor?.data != null && pActor.kingdom == pSuzerain &&
                       pActor.isAlive() && !pActor.isRekt() &&
                       pActor.isAdult() && !pActor.isKing();
            }
            catch { return false; }
        }

        private static void Rollback(Kingdom pNewKingdom,
            Kingdom pOriginalKingdom, City pSeat, Actor pCandidate)
        {
            try
            {
                if (pSeat?.data != null && pOriginalKingdom?.data != null &&
                    pSeat.kingdom != pOriginalKingdom)
                    pSeat.setKingdom(pOriginalKingdom);
            }
            catch { }
            try
            {
                if (pCandidate?.data != null && pSeat?.data != null)
                    pCandidate.joinCity(pSeat);
                else if (pCandidate?.data != null &&
                         pOriginalKingdom?.data != null)
                    pCandidate.joinKingdom(pOriginalKingdom);
            }
            catch { }
            try
            {
                if (pNewKingdom?.data != null &&
                    World.world?.kingdoms?.get(pNewKingdom.id) != null)
                    World.world.kingdoms.removeObject(pNewKingdom);
            }
            catch { }
        }
    }
}
