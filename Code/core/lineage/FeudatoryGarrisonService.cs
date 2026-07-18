using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class FeudatoryGarrisonService
    {
        public static bool EnsureFor(FeudatorySnapshot pSnapshot)
        {
            if (pSnapshot == null) return false;
            Kingdom empire = FindKingdom(pSnapshot.EmpireKingdomId);
            City seat = FindCity(pSnapshot.SeatCityId);
            if (empire?.data == null || seat?.data == null ||
                seat.kingdom != empire)
                return false;

            Actor captain = FindActor(pSnapshot.GarrisonCaptainActorId);
            if (!IsEligibleCaptain(captain, empire) ||
                IsUsedByAnotherFeudatory(pSnapshot.FeudatoryId,
                    captain.data.id))
                captain = SelectCaptain(empire, seat, pSnapshot.FeudatoryId);
            if (captain?.data == null) return false;

            string ownerName = (empire.name ?? "") + " " +
                               (seat.data.name ?? "");
            string name = AWArmyRoleRules.DisplayName(
                AWArmyRole.FeudatoryGarrison, ownerName, 1);
            Army army = AWArmyService.EnsureArmy(empire, seat, captain,
                AWArmyRole.FeudatoryGarrison, name, pDetached: false);
            if (army?.data == null) return false;
            return FeudatoryService.UpdateGarrison(pSnapshot.FeudatoryId,
                army.id, captain.data.id);
        }

        private static Actor SelectCaptain(Kingdom pEmpire, City pSeat,
            long pFeudatoryId)
        {
            Actor best = null;
            int bestScore = int.MinValue;
            foreach (Actor general in GeneralService.GetActiveGenerals(pEmpire))
            {
                if (!IsEligibleCaptain(general, pEmpire) ||
                    IsUsedByAnotherFeudatory(pFeudatoryId, general.data.id))
                    continue;
                int score = GeneralService.GetMerit(general) * 10 +
                            general.warfare;
                if (general.city == pSeat) score += 1000;
                if (best == null || score > bestScore ||
                    score == bestScore && general.data.id < best.data.id)
                {
                    best = general;
                    bestScore = score;
                }
            }
            return best;
        }

        private static bool IsEligibleCaptain(Actor pActor, Kingdom pEmpire)
        {
            return pActor?.data != null && !pActor.isRekt() && pActor.isAlive() &&
                   pActor.isAdult() && pActor.isSexMale() &&
                   pActor.kingdom == pEmpire && GeneralService.IsGeneral(pActor);
        }

        private static bool IsUsedByAnotherFeudatory(long pFeudatoryId,
            long pActorId)
        {
            if (pActorId < 0 ||
                !FeudatoryService.TryGet(pFeudatoryId,
                    out FeudatorySnapshot self))
                return false;
            IReadOnlyList<FeudatorySnapshot> rows =
                FeudatoryService.GetByKingdom(self.EmpireKingdomId);
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].FeudatoryId != pFeudatoryId &&
                    rows[i].GarrisonCaptainActorId == pActorId)
                    return true;
            return false;
        }

        private static Kingdom FindKingdom(long pId)
        {
            try { return World.world?.kingdoms?.get(pId); }
            catch { return null; }
        }

        private static City FindCity(long pId)
        {
            try { return World.world?.cities?.get(pId); }
            catch { return null; }
        }

        private static Actor FindActor(long pId)
        {
            if (pId < 0) return null;
            try
            {
                ActorManager units = World.world?.units;
                return units?.get(pId);
            }
            catch { return null; }
        }
    }
}
