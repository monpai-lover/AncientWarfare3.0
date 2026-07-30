using System;
using System.Globalization;

namespace AncientWarfare3.core.lineage
{
    internal sealed class ArmyReplenishmentOperationState
    {
        internal long ArmyId { get; set; } = -1L;
        internal long KingdomId { get; set; } = -1L;
        internal long SourceCityId { get; set; } = -1L;
        internal int ApprovedShortage { get; set; }
        internal int EnlistedCount { get; set; }
        internal double StartTime { get; set; }
        internal double DeadlineTime { get; set; }
    }

    internal static class ArmyReplenishmentOperationService
    {
        internal static bool TryRead(Army pArmy,
            out ArmyReplenishmentOperationState pState)
        {
            pState = null;
            if (pArmy?.data == null) return false;

            pArmy.data.get(LineageKeys.ARMY_REPLENISHMENT_OPERATION_VERSION,
                out int version, 0);
            if (version == 0) return false;

            pArmy.data.get(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_KINGDOM_ID,
                out long kingdomId, -1L);
            pArmy.data.get(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_SOURCE_CITY_ID,
                out long sourceCityId, -1L);
            pArmy.data.get(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_APPROVED_SHORTAGE,
                out int approvedShortage, 0);
            pArmy.data.get(LineageKeys.ARMY_REPLENISHMENT_OPERATION_ENLISTED,
                out int persistedEnlisted, 0);
            pArmy.data.get(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_START_TIME,
                out string startText, string.Empty);
            pArmy.data.get(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_DEADLINE_TIME,
                out string deadlineText, string.Empty);

            if (version != ArmyReplenishmentOperationRules.SchemaVersion ||
                kingdomId < 0L || sourceCityId < 0L ||
                approvedShortage <= 0 ||
                !TryParseFinite(startText, out double startTime) ||
                !TryParseFinite(deadlineText, out double persistedDeadline))
            {
                Clear(pArmy);
                return false;
            }

            Kingdom kingdom = SafeKingdom(pArmy);
            City sourceCity = FindCity(sourceCityId);
            if (!IsLiveOrdinaryArmy(pArmy) ||
                !IsLiveKingdom(kingdom) || kingdom.id != kingdomId ||
                !IsControlledCity(sourceCity, kingdom) ||
                !HasActiveFormalWar(kingdom))
            {
                Clear(pArmy);
                return false;
            }

            double deadline = ArmyReplenishmentOperationRules.ResolveDeadline(
                startTime, persistedDeadline);
            int enlisted = ArmyReplenishmentOperationRules.ClampEnlisted(
                approvedShortage, persistedEnlisted);
            pState = new ArmyReplenishmentOperationState
            {
                ArmyId = pArmy.id,
                KingdomId = kingdomId,
                SourceCityId = sourceCityId,
                ApprovedShortage = approvedShortage,
                EnlistedCount = enlisted,
                StartTime = startTime,
                DeadlineTime = deadline
            };

            if (enlisted != persistedEnlisted || deadline != persistedDeadline)
                Persist(pArmy, pState);
            return true;
        }

        internal static ArmyReplenishmentOperationState Ensure(Army pArmy,
            Kingdom pKingdom, City pSourceCity, int pRequestedShortage,
            double pStartTime)
        {
            if (TryRead(pArmy, out ArmyReplenishmentOperationState existing))
                return existing;
            if (!IsLiveOrdinaryArmy(pArmy) ||
                !IsLiveKingdom(pKingdom) || SafeKingdom(pArmy) != pKingdom ||
                !IsControlledCity(pSourceCity, pKingdom) ||
                !HasActiveFormalWar(pKingdom) || pRequestedShortage <= 0 ||
                !IsFinite(pStartTime)) return null;

            int approved =
                ArmyReplenishmentOperationRules.ResolveApprovedShortage(
                    existingApproved: 0,
                    requestedShortage: pRequestedShortage);
            var state = new ArmyReplenishmentOperationState
            {
                ArmyId = pArmy.id,
                KingdomId = pKingdom.id,
                SourceCityId = pSourceCity.id,
                ApprovedShortage = approved,
                EnlistedCount = 0,
                StartTime = pStartTime,
                DeadlineTime = pStartTime +
                    ArmyReplenishmentOperationRules.DurationWorldSeconds
            };
            Persist(pArmy, state);
            return state;
        }

        internal static void Clear(Army pArmy)
        {
            if (pArmy?.data == null) return;
            pArmy.data.removeInt(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_VERSION);
            pArmy.data.removeLong(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_KINGDOM_ID);
            pArmy.data.removeLong(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_SOURCE_CITY_ID);
            pArmy.data.removeInt(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_APPROVED_SHORTAGE);
            pArmy.data.removeInt(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_ENLISTED);
            pArmy.data.removeString(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_START_TIME);
            pArmy.data.removeString(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_DEADLINE_TIME);
        }

        private static void Persist(Army pArmy,
            ArmyReplenishmentOperationState pState)
        {
            if (pArmy?.data == null || pState == null) return;
            pArmy.data.set(LineageKeys.ARMY_REPLENISHMENT_OPERATION_VERSION,
                ArmyReplenishmentOperationRules.SchemaVersion);
            pArmy.data.set(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_KINGDOM_ID,
                pState.KingdomId);
            pArmy.data.set(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_SOURCE_CITY_ID,
                pState.SourceCityId);
            pArmy.data.set(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_APPROVED_SHORTAGE,
                pState.ApprovedShortage);
            pArmy.data.set(LineageKeys.ARMY_REPLENISHMENT_OPERATION_ENLISTED,
                pState.EnlistedCount);
            pArmy.data.set(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_START_TIME,
                pState.StartTime.ToString("R", CultureInfo.InvariantCulture));
            pArmy.data.set(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_DEADLINE_TIME,
                pState.DeadlineTime.ToString("R",
                    CultureInfo.InvariantCulture));
        }

        private static bool TryParseFinite(string pText, out double pValue)
        {
            return double.TryParse(pText, NumberStyles.Float,
                       CultureInfo.InvariantCulture, out pValue) &&
                   IsFinite(pValue);
        }

        private static bool IsFinite(double pValue)
        {
            return !double.IsNaN(pValue) && !double.IsInfinity(pValue) &&
                   pValue >= 0d;
        }

        private static bool IsLiveOrdinaryArmy(Army pArmy)
        {
            try
            {
                return pArmy?.data != null && pArmy.isAlive() &&
                       ArmyNativeNameService.IsOrdinaryArmy(pArmy);
            }
            catch { return false; }
        }

        private static bool IsLiveKingdom(Kingdom pKingdom)
        {
            try
            {
                return pKingdom?.data != null && pKingdom.isAlive() &&
                       !pKingdom.isRekt();
            }
            catch { return false; }
        }

        private static bool IsControlledCity(City pCity, Kingdom pKingdom)
        {
            try
            {
                return pCity?.data != null && pCity.isAlive() &&
                       !pCity.isRekt() && pCity.kingdom == pKingdom;
            }
            catch { return false; }
        }

        private static bool HasActiveFormalWar(Kingdom pKingdom)
        {
            if (!IsLiveKingdom(pKingdom) || World.world?.wars == null)
                return false;
            try
            {
                foreach (War war in World.world.wars.getWars(pKingdom))
                {
                    if (war?.data != null && !war.hasEnded() &&
                        war.hasKingdom(pKingdom)) return true;
                }
            }
            catch { }
            return false;
        }

        private static Kingdom SafeKingdom(Army pArmy)
        {
            try { return pArmy?.getKingdom(); }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }
    }
}
