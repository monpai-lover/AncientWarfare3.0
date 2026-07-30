using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyFieldIndexService
    {
        private static readonly ArmyFieldIdentityIndex Index =
            new ArmyFieldIdentityIndex();

        public static void OnArmyChanged(Army pArmy)
        {
            Kingdom changedKingdom = SafeKingdom(pArmy);
            if (!IsFieldArmy(pArmy, out Kingdom kingdom))
            {
                if (pArmy != null) Index.Remove(pArmy.id);
                StandingArmyService.OnFieldArmyChanged(changedKingdom);
                return;
            }
            Index.Register(pArmy.id, kingdom.id);
            StandingArmyService.OnFieldArmyChanged(kingdom);
        }

        public static void OnArmyDisposed(Army pArmy)
        {
            Kingdom kingdom = SafeKingdom(pArmy);
            if (pArmy != null) Index.Remove(pArmy.id);
            StandingArmyService.OnFieldArmyChanged(kingdom);
        }

        public static int Count(Kingdom pKingdom)
        {
            return pKingdom?.data == null ? 0 : Index.Count(pKingdom.id);
        }

        public static ArmyStrategicIdCursor CreateSnapshotCursor(
            Kingdom pKingdom)
        {
            return Index.CreateCursor(pKingdom?.data == null
                ? -1L
                : pKingdom.id);
        }

        public static Army ResolveIndexedArmy(long pArmyId,
            long pKingdomId)
        {
            Army army = ArmyStrategicIndexService.ResolveIndexedArmy(
                pArmyId, pKingdomId);
            if (IsFieldArmy(army, out Kingdom kingdom) &&
                kingdom.id == pKingdomId) return army;
            Index.Remove(pArmyId);
            return null;
        }

        public static bool TryRouteCappedCandidate(Actor pActor,
            City pCity, out Army pArmy)
        {
            pArmy = null;
            Kingdom kingdom = pCity?.kingdom;
            if (pActor?.data == null || pCity?.data == null ||
                kingdom?.data == null || pActor.kingdom != kingdom ||
                Count(kingdom) < ArmyEstablishmentRules.MaximumFieldArmies)
                return false;

            return TryRouteStandingCandidate(pActor, pCity, out pArmy);
        }

        public static bool TryRouteStandingCandidate(Actor pActor,
            City pCity, out Army pArmy)
        {
            pArmy = null;
            Kingdom kingdom = pCity?.kingdom;
            if (pActor?.data == null || pCity?.data == null ||
                kingdom?.data == null || pActor.kingdom != kingdom ||
                pActor.army != null) return false;

            ArmyStrategicIdCursor cursor = CreateSnapshotCursor(kingdom);
            IReadOnlyList<long> ids = cursor.Take(
                ArmyEstablishmentRules.MaximumFieldArmies);
            int bestUnits = int.MaxValue;
            bool bestPreferred = false;
            for (int i = 0; i < ids.Count; i++)
            {
                Army candidate = ResolveIndexedArmy(ids[i], kingdom.id);
                if (candidate?.data == null) continue;
                int units = SafeUnitCount(candidate);
                bool preferred = AWArmyService.GetAnchorCityId(candidate) ==
                                 pCity.id;
                if (pArmy != null &&
                    (!preferred || bestPreferred) &&
                    (preferred != bestPreferred || units >= bestUnits))
                    continue;
                pArmy = candidate;
                bestUnits = units;
                bestPreferred = preferred;
            }
            if (pArmy?.data == null) return false;
            AWArmyService.AddToArmy(pActor, pArmy);
            return pActor.army == pArmy;
        }

        public static bool IsFieldCreationExempt(Actor pActor, City pCity)
        {
            if (pCity?.data == null) return true;
            MilitaryRecruitmentKind kind = MilitaryRecruitmentScope.Current;
            if (kind != MilitaryRecruitmentKind.None &&
                kind != MilitaryRecruitmentKind.StandingArmy &&
                kind != MilitaryRecruitmentKind.TemporaryLevy)
                return true;
            if (pActor?.army?.data != null &&
                AWArmyService.IsSpecialArmy(pActor.army)) return true;
            return pActor?.data != null &&
                   TemporarySlaveVanguardService.IsMember(pActor);
        }

        public static void ClearRuntime()
        {
            Index.Clear();
        }

        private static bool IsFieldArmy(Army pArmy, out Kingdom pKingdom)
        {
            pKingdom = null;
            bool hasData = pArmy?.data != null;
            bool restorationArmy = false;
            bool nonReplacingShell = false;
            if (hasData)
            {
                pArmy.data.get(LineageKeys.RESTORATION_UPRISING_ARMY,
                    out restorationArmy, false);
                pArmy.data.get(LineageKeys.AW_ARMY_NON_REPLACING_SHELL,
                    out nonReplacingShell, false);
            }
            bool alive = false;
            try
            {
                alive = pArmy != null && pArmy.isAlive();
                if (alive) pKingdom = pArmy.getKingdom();
            }
            catch { alive = false; }
            bool hasKingdom = pKingdom?.data != null && !pKingdom.isRekt();
            return !nonReplacingShell &&
                   ArmyEstablishmentRules.IsFieldArmyClassification(
                hasData, alive, hasKingdom,
                markedSpecial: AWArmyService.IsSpecialArmy(pArmy),
                sortie: GarrisonSortieService.IsSortieArmy(pArmy),
                controlledCreationShell:
                    AWArmyService.IsSpecialArmyCreationInProgress(pArmy),
                restorationArmy: restorationArmy);
        }

        private static int SafeUnitCount(Army pArmy)
        {
            try { return Math.Max(0, pArmy?.units?.Count ?? 0); }
            catch { return 0; }
        }

        private static Kingdom SafeKingdom(Army pArmy)
        {
            try { return pArmy?.getKingdom(); }
            catch { return null; }
        }
    }
}
