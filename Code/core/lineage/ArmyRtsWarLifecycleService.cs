using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public sealed class ArmyRtsWarLifecycleRecord
    {
        public long WarId { get; internal set; } = -1L;
        public long ArmyId { get; internal set; } = -1L;
        public int BaselineStrength { get; internal set; }
        public ArmyRtsWarPhase Phase { get; internal set; }
        public ArmyRtsMission PreviousOffensiveMission { get; set; }
        public long ReplenishmentCityId { get; set; } = -1L;
        public string WaitReason { get; set; } = string.Empty;
        public double WaitDeadline { get; set; } = double.NaN;
    }

    public sealed class ArmyRtsWarLifecycleStateStore
    {
        private readonly Dictionary<(long WarId, long ArmyId),
            ArmyRtsWarLifecycleRecord> _records =
                new Dictionary<(long WarId, long ArmyId),
                    ArmyRtsWarLifecycleRecord>();

        public int Count => _records.Count;

        public ArmyRtsWarLifecycleRecord Ensure(long warId, long armyId,
            int living, ArmyRtsWarPhase phase)
        {
            if (warId < 0L || armyId < 0L || living <= 0) return null;
            var key = (warId, armyId);
            if (_records.TryGetValue(key,
                    out ArmyRtsWarLifecycleRecord existing))
                return existing;
            var created = new ArmyRtsWarLifecycleRecord
            {
                WarId = warId,
                ArmyId = armyId,
                BaselineStrength = living,
                Phase = phase
            };
            _records[key] = created;
            return created;
        }

        public bool TryGet(long warId, long armyId,
            out ArmyRtsWarLifecycleRecord record)
        {
            return _records.TryGetValue((warId, armyId), out record);
        }

        public bool TrySetPhase(long warId, long armyId,
            ArmyRtsWarPhase phase)
        {
            if (!TryGet(warId, armyId,
                    out ArmyRtsWarLifecycleRecord record)) return false;
            record.Phase = phase;
            return true;
        }

        public int RemoveArmy(long armyId)
        {
            if (armyId < 0L || _records.Count == 0) return 0;
            var remove = new List<(long WarId, long ArmyId)>();
            foreach (var pair in _records)
                if (pair.Key.ArmyId == armyId) remove.Add(pair.Key);
            for (int i = 0; i < remove.Count; i++)
                _records.Remove(remove[i]);
            return remove.Count;
        }

        public int ClearWar(long warId)
        {
            if (warId < 0L || _records.Count == 0) return 0;
            var remove = new List<(long WarId, long ArmyId)>();
            foreach (var pair in _records)
                if (pair.Key.WarId == warId) remove.Add(pair.Key);
            for (int i = 0; i < remove.Count; i++)
                _records.Remove(remove[i]);
            return remove.Count;
        }

        public void Clear()
        {
            _records.Clear();
        }
    }

#if !AW3_RULES_TESTS
    internal static class ArmyRtsWarLifecycleService
    {
        private static readonly ArmyRtsWarLifecycleStateStore Store =
            new ArmyRtsWarLifecycleStateStore();

        public static void OnWarStarted(War pWar)
        {
            if (pWar?.data == null || World.world?.armies == null) return;
            foreach (Army army in World.world.armies)
            {
                Kingdom kingdom = AWArmyService.GetIntendedKingdom(army);
                bool participant;
                try
                {
                    participant = army?.data != null &&
                                  kingdom?.data != null &&
                                  pWar.hasKingdom(kingdom);
                }
                catch { participant = false; }
                if (!participant) continue;
                EnsureForArmy(pWar.data.id, army,
                    ArmyRtsWarPhase.StrategicMovement);
            }
        }

        public static ArmyRtsWarLifecycleRecord OnMissionAssigned(
            Army pArmy, ArmyRtsMission pMission)
        {
            if (pArmy?.data == null || pMission == null) return null;
            ArmyRtsWarLifecycleRecord record = EnsureForArmy(
                pMission.WarId, pArmy,
                ArmyRtsWarPhase.StrategicMovement);
            if (record != null &&
                pMission.ProposalKind != ArmyRtsProposalKind.Retreat)
                record.PreviousOffensiveMission =
                    ArmyRtsControllerRules.CopyMission(pMission);
            return record;
        }

        public static bool TryGet(long pWarId, long pArmyId,
            out ArmyRtsWarLifecycleRecord pRecord)
        {
            return Store.TryGet(pWarId, pArmyId, out pRecord);
        }

        public static bool TrySetPhase(long pWarId, long pArmyId,
            ArmyRtsWarPhase pPhase)
        {
            bool changed = Store.TrySetPhase(pWarId, pArmyId, pPhase);
            if (changed) Persist(FindArmy(pArmyId), pWarId,
                Store.TryGet(pWarId, pArmyId,
                    out ArmyRtsWarLifecycleRecord record)
                    ? record
                    : null);
            return changed;
        }

        public static void OnArmyDestroyed(Army pArmy)
        {
            if (pArmy == null) return;
            Store.RemoveArmy(pArmy.id);
            ClearPersisted(pArmy);
        }

        public static void OnWarEnded(War pWar)
        {
            long warId = pWar?.data?.id ?? -1L;
            if (warId < 0L) return;
            Store.ClearWar(warId);
            if (World.world?.armies == null) return;
            foreach (Army army in World.world.armies)
            {
                if (army?.data == null) continue;
                army.data.get(LineageKeys.AW_RTS_LIFECYCLE_WAR_ID,
                    out long persistedWarId, -1L);
                if (persistedWarId == warId) ClearPersisted(army);
            }
        }

        public static void ClearRuntime()
        {
            Store.Clear();
        }

        private static ArmyRtsWarLifecycleRecord EnsureForArmy(long pWarId,
            Army pArmy, ArmyRtsWarPhase pPhase)
        {
            if (pWarId < 0L || pArmy?.data == null) return null;
            if (Store.TryGet(pWarId, pArmy.id,
                    out ArmyRtsWarLifecycleRecord existing)) return existing;
            pArmy.data.get(LineageKeys.AW_RTS_LIFECYCLE_WAR_ID,
                out long persistedWarId, -1L);
            pArmy.data.get(LineageKeys.AW_RTS_LIFECYCLE_BASELINE,
                out int persistedBaseline, 0);
            pArmy.data.get(LineageKeys.AW_RTS_LIFECYCLE_PHASE,
                out int persistedPhase, (int)pPhase);
            int living = persistedWarId == pWarId && persistedBaseline > 0
                ? persistedBaseline
                : SafeUnitCount(pArmy);
            ArmyRtsWarPhase phase = persistedWarId == pWarId &&
                                     Enum.IsDefined(
                                         typeof(ArmyRtsWarPhase),
                                         persistedPhase)
                ? (ArmyRtsWarPhase)persistedPhase
                : pPhase;
            ArmyRtsWarLifecycleRecord record = Store.Ensure(pWarId,
                pArmy.id, living, phase);
            Persist(pArmy, pWarId, record);
            return record;
        }

        private static void Persist(Army pArmy, long pWarId,
            ArmyRtsWarLifecycleRecord pRecord)
        {
            if (pArmy?.data == null || pRecord == null) return;
            pArmy.data.set(LineageKeys.AW_RTS_LIFECYCLE_WAR_ID, pWarId);
            pArmy.data.set(LineageKeys.AW_RTS_LIFECYCLE_BASELINE,
                pRecord.BaselineStrength);
            pArmy.data.set(LineageKeys.AW_RTS_LIFECYCLE_PHASE,
                (int)pRecord.Phase);
        }

        private static void ClearPersisted(Army pArmy)
        {
            if (pArmy?.data == null) return;
            pArmy.data.removeLong(LineageKeys.AW_RTS_LIFECYCLE_WAR_ID);
            pArmy.data.removeInt(LineageKeys.AW_RTS_LIFECYCLE_BASELINE);
            pArmy.data.removeInt(LineageKeys.AW_RTS_LIFECYCLE_PHASE);
        }

        private static int SafeUnitCount(Army pArmy)
        {
            try { return Math.Max(0, pArmy?.countUnits() ?? 0); }
            catch { return 0; }
        }

        private static Army FindArmy(long pArmyId)
        {
            try { return World.world?.armies?.get(pArmyId); }
            catch { return null; }
        }
    }
#endif
}
