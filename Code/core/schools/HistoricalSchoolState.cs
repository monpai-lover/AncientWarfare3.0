using System;
using System.Collections.Generic;
using AncientWarfare3.content.schools;

namespace AncientWarfare3.core.schools
{
    public enum HistoricalSchoolLifecycleState
    {
        Queued,
        Descended,
        AtHome,
        ChoosingDestination,
        Travelling,
        Resident,
        Lecturing,
        Persuading,
        Recruiting,
        Debating,
        Serving,
        Voyage,
        Retired,
        Dead
    }

    public enum SchoolMembershipSource
    {
        HistoricalDescent,
        DirectDiscipleship,
        LaterDiscipleship,
        ExplicitConversion,
        PreservedWork,
        AuthoredEvent
    }

    public enum HistoricalSchoolActionType
    {
        None,
        Lecture,
        Discipleship,
        Persuasion,
        Writing,
        FoundInstitution,
        Debate,
        Travel,
        Service,
        Rest,
        Retirement
    }

    public enum SchoolDebateOutcome
    {
        Draw,
        NarrowFirstWin,
        DecisiveFirstWin,
        NarrowSecondWin,
        DecisiveSecondWin
    }

    public sealed class HistoricalSchoolDescentLedger
    {
        private readonly HashSet<string> _spawned =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _counts =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _lastSelectionYears =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public int SpawnedCount => _spawned.Count;

        public bool IsSpawned(string pMasterId)
        {
            return !string.IsNullOrEmpty(pMasterId) && _spawned.Contains(pMasterId);
        }

        public int CountForSchool(string pSchoolId)
        {
            return !string.IsNullOrEmpty(pSchoolId) && _counts.TryGetValue(pSchoolId,
                out int count) ? count : 0;
        }

        public int LastSelectionYear(string pSchoolId)
        {
            return !string.IsNullOrEmpty(pSchoolId) && _lastSelectionYears.TryGetValue(pSchoolId,
                out int year) ? year : int.MinValue;
        }

        public bool MarkSpawned(HistoricalSchoolMasterDefinition pMaster, int pEligibleYear)
        {
            if (pMaster == null || string.IsNullOrEmpty(pMaster.Id) ||
                string.IsNullOrEmpty(pMaster.SchoolId) || !_spawned.Add(pMaster.Id)) return false;
            _counts[pMaster.SchoolId] = CountForSchool(pMaster.SchoolId) + 1;
            _lastSelectionYears[pMaster.SchoolId] = Math.Max(0, pEligibleYear);
            return true;
        }
    }

    public static class HistoricalDebateTopicId
    {
        public const string Livelihood = "livelihood";
        public const string Famine = "famine";
        public const string War = "war";
        public const string Defense = "defense";
        public const string Aggression = "aggression";
        public const string Peace = "peace";
        public const string Diplomacy = "diplomacy";
        public const string Order = "order";
        public const string Commerce = "commerce";
        public const string Technology = "technology";
        public const string Institutions = "institutions";
        public const string Medicine = "medicine";
        public const string Epidemic = "epidemic";
    }
}
