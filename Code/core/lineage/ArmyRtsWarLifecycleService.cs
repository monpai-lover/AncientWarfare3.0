using System;
using System.Collections.Generic;
using System.Globalization;

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
        private IReadOnlyList<ArmyRtsWarLifecycleRecord> _snapshot =
            Array.Empty<ArmyRtsWarLifecycleRecord>();
        private bool _snapshotDirty;

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
            _snapshotDirty = true;
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

        public IReadOnlyList<ArmyRtsWarLifecycleRecord> Snapshot()
        {
            if (!_snapshotDirty) return _snapshot;
            var snapshot = new ArmyRtsWarLifecycleRecord[_records.Count];
            _records.Values.CopyTo(snapshot, 0);
            Array.Sort(snapshot, (left, right) =>
            {
                int war = left.WarId.CompareTo(right.WarId);
                return war != 0
                    ? war
                    : left.ArmyId.CompareTo(right.ArmyId);
            });
            _snapshot = snapshot;
            _snapshotDirty = false;
            return _snapshot;
        }

        public int RemoveArmy(long armyId)
        {
            if (armyId < 0L || _records.Count == 0) return 0;
            var remove = new List<(long WarId, long ArmyId)>();
            foreach (var pair in _records)
                if (pair.Key.ArmyId == armyId) remove.Add(pair.Key);
            for (int i = 0; i < remove.Count; i++)
                _records.Remove(remove[i]);
            if (remove.Count > 0) _snapshotDirty = true;
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
            if (remove.Count > 0) _snapshotDirty = true;
            return remove.Count;
        }

        public void Clear()
        {
            _records.Clear();
            _snapshot = Array.Empty<ArmyRtsWarLifecycleRecord>();
            _snapshotDirty = false;
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
            if (record != null)
            {
                record.WaitReason = string.Empty;
                record.WaitDeadline = double.NaN;
                Persist(pArmy, pMission.WarId, record);
            }
            return record;
        }

        public static bool MarkWaiting(long pWarId, Army pArmy,
            string pReason, double pDeadline)
        {
            if (string.IsNullOrWhiteSpace(pReason) ||
                double.IsNaN(pDeadline) || double.IsInfinity(pDeadline))
                return false;
            ArmyRtsWarLifecycleRecord record = EnsureForArmy(pWarId,
                pArmy, ArmyRtsWarPhase.StrategicMovement);
            if (record == null) return false;
            record.WaitReason = pReason;
            record.WaitDeadline = pDeadline;
            Persist(pArmy, pWarId, record);
            return true;
        }

        public static IReadOnlyList<ArmyRtsWarLifecycleRecord> Snapshot()
        {
            return Store.Snapshot();
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

        public static bool BeginReplenishing(long pWarId, Army pArmy,
            City pCity)
        {
            if (pArmy?.data == null || pCity?.data == null ||
                !Store.TryGet(pWarId, pArmy.id,
                    out ArmyRtsWarLifecycleRecord record)) return false;
            record.Phase = ArmyRtsWarPhase.Replenishing;
            record.ReplenishmentCityId = pCity.id;
            Persist(pArmy, pWarId, record);
            return true;
        }

        public static void OnArmyDestroyed(Army pArmy)
        {
            if (pArmy == null) return;
            Store.RemoveArmy(pArmy.id);
            ClearPersisted(pArmy);
        }

        public static void ClearArmy(Army pArmy)
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
            pArmy.data.get(LineageKeys.AW_RTS_REPLENISHMENT_CITY_ID,
                out long persistedCityId, -1L);
            pArmy.data.get(LineageKeys.AW_RTS_WAIT_REASON,
                out string persistedWaitReason, string.Empty);
            pArmy.data.get(LineageKeys.AW_RTS_WAIT_DEADLINE,
                out string persistedWaitDeadline, string.Empty);
            pArmy.data.get(LineageKeys.AW_RTS_PREVIOUS_MISSION_WAR_ID,
                out long previousWarId, -1L);
            pArmy.data.get(LineageKeys.AW_RTS_PREVIOUS_MISSION_FRONT_ID,
                out long previousFrontId, -1L);
            pArmy.data.get(
                LineageKeys.AW_RTS_PREVIOUS_MISSION_TARGET_CITY_ID,
                out long previousTargetCityId, -1L);
            pArmy.data.get(
                LineageKeys.AW_RTS_PREVIOUS_MISSION_TARGET_STRENGTH,
                out int previousTargetStrength, 0);
            pArmy.data.get(
                LineageKeys.AW_RTS_PREVIOUS_MISSION_PROPOSAL_KIND,
                out string previousProposalKind, string.Empty);
            pArmy.data.get(LineageKeys.AW_RTS_PREVIOUS_MISSION_ROLE,
                out string previousRole, string.Empty);
            pArmy.data.get(LineageKeys.AW_RTS_PREVIOUS_MISSION_POSTURE,
                out string previousPosture, string.Empty);
            pArmy.data.get(
                LineageKeys.AW_RTS_PREVIOUS_MISSION_PLAYER_ORDER,
                out bool previousPlayerOrder, false);
            pArmy.data.get(
                LineageKeys.AW_RTS_PREVIOUS_MISSION_ISSUED_TIME,
                out string previousIssuedText, string.Empty);
            double.TryParse(persistedWaitDeadline, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double waitDeadline);
            double.TryParse(previousIssuedText, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double previousIssuedTime);
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
            if (record != null && persistedWarId == pWarId)
            {
                record.ReplenishmentCityId = persistedCityId;
                record.WaitReason = persistedWaitReason ?? string.Empty;
                record.WaitDeadline = string.IsNullOrWhiteSpace(
                    persistedWaitDeadline)
                    ? double.NaN
                    : waitDeadline;
                if (previousWarId >= 0L && previousFrontId >= 0L &&
                    previousTargetCityId >= 0L &&
                    Enum.TryParse(previousProposalKind, true,
                        out ArmyRtsProposalKind previousKind) &&
                    Enum.IsDefined(typeof(ArmyRtsProposalKind), previousKind) &&
                    ArmyRtsWarDoctrineRules.
                        ShouldPersistPreviousOffensiveMission(previousKind) &&
                    Enum.TryParse(previousRole, true,
                        out ArmyRtsRole previousRoleValue) &&
                    Enum.IsDefined(typeof(ArmyRtsRole), previousRoleValue) &&
                    Enum.TryParse(previousPosture, true,
                        out ArmyRtsPosture previousPostureValue) &&
                    Enum.IsDefined(typeof(ArmyRtsPosture),
                        previousPostureValue) &&
                    !double.IsNaN(previousIssuedTime) &&
                    !double.IsInfinity(previousIssuedTime))
                    record.PreviousOffensiveMission =
                        new ArmyRtsMission
                        {
                            ArmyId = pArmy.id,
                            KingdomId = SafeKingdom(pArmy)?.id ?? -1L,
                            WarId = previousWarId,
                            FrontId = previousFrontId,
                            TargetCityId = previousTargetCityId,
                            TargetStrength = Math.Max(0,
                                previousTargetStrength),
                            ProposalKind = previousKind,
                            Role = previousRoleValue,
                            Posture = previousPostureValue,
                            PlayerOrder = previousPlayerOrder,
                            IssuedTime = previousIssuedTime
                        };
            }
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
            pArmy.data.set(LineageKeys.AW_RTS_REPLENISHMENT_CITY_ID,
                pRecord.ReplenishmentCityId);
            pArmy.data.set(LineageKeys.AW_RTS_WAIT_REASON,
                pRecord.WaitReason ?? string.Empty);
            pArmy.data.set(LineageKeys.AW_RTS_WAIT_DEADLINE,
                pRecord.WaitDeadline.ToString("R",
                    CultureInfo.InvariantCulture));
            ArmyRtsMission previous = pRecord.PreviousOffensiveMission;
            if (previous != null && ArmyRtsWarDoctrineRules.
                    ShouldPersistPreviousOffensiveMission(
                        previous.ProposalKind))
            {
                pArmy.data.set(
                    LineageKeys.AW_RTS_PREVIOUS_MISSION_WAR_ID,
                    previous.WarId);
                pArmy.data.set(
                    LineageKeys.AW_RTS_PREVIOUS_MISSION_FRONT_ID,
                    previous.FrontId);
                pArmy.data.set(
                    LineageKeys.AW_RTS_PREVIOUS_MISSION_TARGET_CITY_ID,
                    previous.TargetCityId);
                pArmy.data.set(
                    LineageKeys.AW_RTS_PREVIOUS_MISSION_TARGET_STRENGTH,
                    Math.Max(0, previous.TargetStrength));
                pArmy.data.set(
                    LineageKeys.AW_RTS_PREVIOUS_MISSION_PROPOSAL_KIND,
                    previous.ProposalKind.ToString());
                pArmy.data.set(LineageKeys.AW_RTS_PREVIOUS_MISSION_ROLE,
                    previous.Role.ToString());
                pArmy.data.set(
                    LineageKeys.AW_RTS_PREVIOUS_MISSION_POSTURE,
                    previous.Posture.ToString());
                pArmy.data.set(
                    LineageKeys.AW_RTS_PREVIOUS_MISSION_PLAYER_ORDER,
                    previous.PlayerOrder);
                pArmy.data.set(
                    LineageKeys.AW_RTS_PREVIOUS_MISSION_ISSUED_TIME,
                    previous.IssuedTime.ToString("R",
                        CultureInfo.InvariantCulture));
            }
            else
                ClearPreviousMission(pArmy);
        }

        private static void ClearPersisted(Army pArmy)
        {
            if (pArmy?.data == null) return;
            pArmy.data.removeLong(LineageKeys.AW_RTS_LIFECYCLE_WAR_ID);
            pArmy.data.removeInt(LineageKeys.AW_RTS_LIFECYCLE_BASELINE);
            pArmy.data.removeInt(LineageKeys.AW_RTS_LIFECYCLE_PHASE);
            pArmy.data.removeLong(LineageKeys.AW_RTS_REPLENISHMENT_CITY_ID);
            pArmy.data.removeString(LineageKeys.AW_RTS_WAIT_REASON);
            pArmy.data.removeString(LineageKeys.AW_RTS_WAIT_DEADLINE);
            ClearPreviousMission(pArmy);
        }

        private static void ClearPreviousMission(Army pArmy)
        {
            if (pArmy?.data == null) return;
            pArmy.data.removeLong(
                LineageKeys.AW_RTS_PREVIOUS_MISSION_WAR_ID);
            pArmy.data.removeLong(
                LineageKeys.AW_RTS_PREVIOUS_MISSION_FRONT_ID);
            pArmy.data.removeLong(
                LineageKeys.AW_RTS_PREVIOUS_MISSION_TARGET_CITY_ID);
            pArmy.data.removeInt(
                LineageKeys.AW_RTS_PREVIOUS_MISSION_TARGET_STRENGTH);
            pArmy.data.removeString(
                LineageKeys.AW_RTS_PREVIOUS_MISSION_PROPOSAL_KIND);
            pArmy.data.removeString(
                LineageKeys.AW_RTS_PREVIOUS_MISSION_ROLE);
            pArmy.data.removeString(
                LineageKeys.AW_RTS_PREVIOUS_MISSION_POSTURE);
            pArmy.data.removeBool(
                LineageKeys.AW_RTS_PREVIOUS_MISSION_PLAYER_ORDER);
            pArmy.data.removeString(
                LineageKeys.AW_RTS_PREVIOUS_MISSION_ISSUED_TIME);
        }

        private static int SafeUnitCount(Army pArmy)
        {
            try { return Math.Max(0, pArmy?.countUnits() ?? 0); }
            catch { return 0; }
        }

        private static Kingdom SafeKingdom(Army pArmy)
        {
            try { return AWArmyService.GetIntendedKingdom(pArmy); }
            catch
            {
                try { return pArmy?.getKingdom(); }
                catch { return null; }
            }
        }

        private static Army FindArmy(long pArmyId)
        {
            try { return World.world?.armies?.get(pArmyId); }
            catch { return null; }
        }
    }
#endif
}
