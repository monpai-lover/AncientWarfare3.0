using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    // Kept for rules-test compatibility and old serialized type names. The
    // runtime sortie feature is retired with wartime_garrison.
    public sealed class GarrisonSortieRecord
    {
        private readonly IReadOnlyList<long> _memberIds;
        private int _returnCursor;

        internal GarrisonSortieRecord(long armyId, long kingdomId,
            long originCityId, IReadOnlyList<long> memberIds)
        {
            ArmyId = armyId;
            KingdomId = kingdomId;
            OriginCityId = originCityId;
            _memberIds = memberIds ?? Array.Empty<long>();
        }

        public long ArmyId { get; }
        public long KingdomId { get; }
        public long OriginCityId { get; }
        public int RemainingMemberCount =>
            Math.Max(0, _memberIds.Count - _returnCursor);
        public bool ReturnComplete => _returnCursor >= _memberIds.Count;

        internal IReadOnlyList<long> TakeReturnBatch(int maximum)
        {
            int count = Math.Min(Math.Max(0, maximum),
                RemainingMemberCount);
            if (count == 0) return Array.Empty<long>();
            var result = new long[count];
            for (int i = 0; i < count; i++)
                result[i] = _memberIds[_returnCursor + i];
            _returnCursor += count;
            return result;
        }

        public IReadOnlyList<long> GetRemainingMemberIds()
        {
            int count = RemainingMemberCount;
            if (count == 0) return Array.Empty<long>();
            var result = new long[count];
            for (int i = 0; i < count; i++)
                result[i] = _memberIds[_returnCursor + i];
            return result;
        }
    }

    public sealed class GarrisonSortieRuntimeIndex
    {
        private readonly Dictionary<long, GarrisonSortieRecord> _byArmy =
            new Dictionary<long, GarrisonSortieRecord>();
        private readonly Dictionary<long, long> _armyByOriginCity =
            new Dictionary<long, long>();

        public bool TryBegin(long armyId, long kingdomId,
            long originCityId, IReadOnlyList<long> memberIds)
        {
            if (armyId < 0L || kingdomId < 0L || originCityId < 0L ||
                memberIds == null || memberIds.Count == 0 ||
                _byArmy.ContainsKey(armyId) ||
                _armyByOriginCity.ContainsKey(originCityId)) return false;
            var copy = new List<long>(memberIds.Count);
            var unique = new HashSet<long>();
            for (int i = 0; i < memberIds.Count; i++)
                if (memberIds[i] >= 0L && unique.Add(memberIds[i]))
                    copy.Add(memberIds[i]);
            if (!GarrisonSortieRules.CanFormSortie(copy.Count)) return false;
            var record = new GarrisonSortieRecord(armyId, kingdomId,
                originCityId, copy);
            _byArmy[armyId] = record;
            _armyByOriginCity[originCityId] = armyId;
            return true;
        }

        public bool TryGet(long armyId, out GarrisonSortieRecord record)
        { return _byArmy.TryGetValue(armyId, out record); }
        public bool ContainsOrigin(long originCityId)
        { return _armyByOriginCity.ContainsKey(originCityId); }

        public bool TryGetByOrigin(long originCityId,
            out GarrisonSortieRecord record)
        {
            record = null;
            return _armyByOriginCity.TryGetValue(originCityId,
                       out long armyId) &&
                   _byArmy.TryGetValue(armyId, out record);
        }

        public IReadOnlyList<long> TakeReturnBatch(long armyId, int maximum)
        {
            return _byArmy.TryGetValue(armyId,
                out GarrisonSortieRecord record)
                ? record.TakeReturnBatch(maximum)
                : Array.Empty<long>();
        }

        public bool Complete(long armyId)
        {
            if (!_byArmy.TryGetValue(armyId,
                    out GarrisonSortieRecord record) ||
                !record.ReturnComplete) return false;
            _byArmy.Remove(armyId);
            _armyByOriginCity.Remove(record.OriginCityId);
            return true;
        }

        public void Clear()
        {
            _byArmy.Clear();
            _armyByOriginCity.Clear();
        }

        public IReadOnlyList<long> GetArmyIds()
        {
            if (_byArmy.Count == 0) return Array.Empty<long>();
            var result = new long[_byArmy.Count];
            _byArmy.Keys.CopyTo(result, 0);
            return result;
        }
    }

#if !AW3_RULES_TESTS
    internal static class GarrisonSortieService
    {
        public static bool IsSortieArmy(Army pArmy) { return false; }
        public static bool TryLaunch(City pCity) { return false; }

        // A stale mission using the retired role must finish immediately so
        // vanilla owns the army instead of waiting on a removed sortie loop.
        public static bool ShouldCompleteMission(Army pArmy,
            ArmyRtsMission pMission, City pTarget, Kingdom pKingdom)
        { return true; }

        public static bool OnMissionCompleted(Army pArmy) { return false; }
        public static void OnKingdomDestroying(Kingdom pKingdom) { }
        public static void OnOriginSupplyChanged(City pOrigin) { }
        public static void RebuildRuntime() { }
        public static void ClearRuntime() { }
    }
#endif
}
