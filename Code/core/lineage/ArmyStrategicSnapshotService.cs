using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal sealed class ArmyStrategicSnapshotBatch
    {
        public ArmyStrategicSnapshotBatch(
            IReadOnlyList<ArmyStrategicFacts> pArmies, bool pComplete)
        {
            Armies = pArmies ?? Array.Empty<ArmyStrategicFacts>();
            Complete = pComplete;
        }

        public IReadOnlyList<ArmyStrategicFacts> Armies { get; }
        public bool Complete { get; }
    }

    internal static class ArmyStrategicSnapshotService
    {
        public const int MaximumArmiesPerWorkItem = 8;

        public static ArmyStrategicSnapshotBatch CaptureNext(
            Kingdom pKingdom, ArmyStrategicIdCursor pCursor)
        {
            if (pKingdom?.data == null || pCursor == null)
                return new ArmyStrategicSnapshotBatch(
                    Array.Empty<ArmyStrategicFacts>(), pComplete: true);

            IReadOnlyList<long> ids =
                pCursor.Take(MaximumArmiesPerWorkItem);
            var facts = new List<ArmyStrategicFacts>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                Army army = ArmyStrategicIndexService.ResolveIndexedArmy(
                    ids[i], pKingdom.id);
                ArmyStrategicFacts item = BuildFacts(army, pKingdom);
                if (item != null) facts.Add(item);
            }
            return new ArmyStrategicSnapshotBatch(facts,
                pCursor.IsComplete);
        }

        private static ArmyStrategicFacts BuildFacts(Army pArmy,
            Kingdom pKingdom)
        {
            if (pArmy?.data == null || pKingdom?.data == null) return null;
            Actor captain = SafeCaptain(pArmy);
            City anchor = SafeAnchorCity(pArmy);
            long currentTargetCityId = ResolveCurrentTargetCityId(pArmy,
                anchor);
            int units;
            try { units = Math.Max(0, pArmy.countUnits()); }
            catch { units = 0; }
            bool captainAlive = captain?.data != null &&
                                SafeActorAlive(captain);
            if (captainAlive && AWArmyService.IsRoleArmy(pArmy,
                    AWArmyRole.SlaveArmy))
                captainAlive = TemporarySlaveVanguardService.
                    IsOperationalCaptain(pArmy, captain);
            bool royalGuard = AWArmyService.IsRoleArmy(pArmy,
                                  AWArmyRole.RoyalGuard) ||
                              RoyalGuardService.IsRoyalGuard(captain);
            bool dedicatedGarrison =
                WartimeGarrisonService.IsActive(captain) ||
                GarrisonSortieService.IsSortieArmy(pArmy);
            bool specialArmy = AWArmyService.IsSpecialArmy(pArmy);
            ArmyOperationalStateView operational =
                ArmyLogisticsService.GetOperationalState(pArmy);
            int captainX = int.MinValue;
            int captainY = int.MinValue;
            try
            {
                WorldTile tile = captainAlive ? captain.current_tile : null;
                if (tile?.data != null)
                {
                    captainX = tile.x;
                    captainY = tile.y;
                }
            }
            catch { }
            return new ArmyStrategicFacts(
                pArmy.id,
                pKingdom.id,
                anchor?.id ?? -1L,
                captain?.data?.id ?? -1L,
                currentTargetCityId,
                units,
                AWArmyService.GetRole(pArmy),
                captainAlive,
                royalGuard,
                dedicatedGarrison,
                operational.Supply,
                operational.Organization,
                captainX,
                captainY,
                specialArmy);
        }

        private static long ResolveCurrentTargetCityId(Army pArmy,
            City pAnchor)
        {
            if (ArmyRtsRuntimeModeRules.ShouldCommit(
                    ArmyRtsRuntimeMode.Current) &&
                ArmyRtsControllerService.TryGetProjection(pArmy,
                    out ArmyRtsStrategicProjection projection) &&
                projection.TargetCityId >= 0L)
                return projection.TargetCityId;
            return SafeLegacyTargetCityId(pAnchor);
        }

        private static long SafeLegacyTargetCityId(City pAnchor)
        {
            if (pAnchor?.data == null) return -1L;
            City target = null;
            try { target = pAnchor.target_attack_city; }
            catch { }
            if (target?.data == null)
            {
                try { target = pAnchor.target_attack_zone?.city; }
                catch { target = null; }
            }
            return SafeCityId(target);
        }

        private static long SafeCityId(City pCity)
        {
            if (pCity?.data == null) return -1L;
            try { return pCity.data.id; }
            catch { return -1L; }
        }

        private static Actor SafeCaptain(Army pArmy)
        {
            try { return pArmy?.getCaptain(); }
            catch { return null; }
        }

        private static City SafeAnchorCity(Army pArmy)
        {
            try
            {
                City city = pArmy?.getCity();
                if (city?.data != null) return city;
            }
            catch { }
            try { return AWArmyService.FindAnchorCity(pArmy); }
            catch { return null; }
        }

        private static bool SafeActorAlive(Actor pActor)
        {
            try { return pActor.isAlive() && !pActor.isRekt(); }
            catch { return false; }
        }
    }
}
