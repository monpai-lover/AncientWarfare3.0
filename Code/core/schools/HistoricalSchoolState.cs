using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;

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

    public sealed class SchoolMembershipRecord
    {
        public SchoolMembershipRecord(long pMembershipId, long pActorId, string pSchoolId,
            SchoolMembershipSource pSource, string pSourceId, long pTeacherActorId, long pCityId,
            int pGeneration, float pReputation, int pStartYear, int pEndYear = -1,
            bool pActive = true, string pEndReason = "")
        {
            MembershipId = pMembershipId;
            ActorId = pActorId;
            SchoolId = pSchoolId ?? "";
            Source = pSource;
            SourceId = pSourceId ?? "";
            TeacherActorId = pTeacherActorId;
            CityId = pCityId;
            Generation = pGeneration;
            Reputation = pReputation;
            StartYear = pStartYear;
            EndYear = pEndYear;
            Active = pActive;
            EndReason = pEndReason ?? "";
        }

        public long MembershipId { get; }
        public long ActorId { get; }
        public string SchoolId { get; }
        public SchoolMembershipSource Source { get; }
        public string SourceId { get; }
        public long TeacherActorId { get; }
        public long CityId { get; }
        public int Generation { get; }
        public float Reputation { get; }
        public int StartYear { get; }
        public int EndYear { get; }
        public bool Active { get; }
        public string EndReason { get; }

        public bool IsValid => MembershipId >= 0 && ActorId >= 0 &&
                               CourtSchoolRegistry.Find(SchoolId) != null &&
                               !string.IsNullOrWhiteSpace(SourceId) && Generation >= 0 &&
                               (!RequiresTeacher(Source) || TeacherActorId >= 0);

        public SchoolMembershipRecord Close(int pEndYear, string pReason)
        {
            return new SchoolMembershipRecord(MembershipId, ActorId, SchoolId, Source, SourceId,
                TeacherActorId, CityId, Generation, Reputation, StartYear,
                Math.Max(StartYear, pEndYear), pActive: false, pReason);
        }

        private static bool RequiresTeacher(SchoolMembershipSource pSource)
        {
            return pSource == SchoolMembershipSource.DirectDiscipleship ||
                   pSource == SchoolMembershipSource.LaterDiscipleship;
        }
    }

    public sealed class SchoolMembershipBook
    {
        private readonly Dictionary<long, SchoolMembershipRecord> _activeByActor =
            new Dictionary<long, SchoolMembershipRecord>();
        private readonly Dictionary<string, HashSet<long>> _actorsBySchool =
            new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
        private readonly List<SchoolMembershipRecord> _closed =
            new List<SchoolMembershipRecord>();

        public bool TryJoin(SchoolMembershipRecord pRecord)
        {
            if (pRecord == null || !pRecord.Active || !pRecord.IsValid ||
                _activeByActor.ContainsKey(pRecord.ActorId)) return false;
            AddActive(pRecord);
            return true;
        }

        public bool TryConvert(long pActorId, SchoolMembershipRecord pReplacement, int pYear,
            out SchoolMembershipRecord pClosed)
        {
            pClosed = null;
            if (!_activeByActor.TryGetValue(pActorId, out SchoolMembershipRecord current) ||
                pReplacement == null || pReplacement.ActorId != pActorId ||
                pReplacement.Source != SchoolMembershipSource.ExplicitConversion ||
                !pReplacement.Active || !pReplacement.IsValid ||
                current.Source == SchoolMembershipSource.HistoricalDescent ||
                current.SchoolId == pReplacement.SchoolId) return false;
            pClosed = current.Close(pYear, "converted");
            RemoveActive(current);
            _closed.Add(pClosed);
            AddActive(pReplacement);
            return true;
        }

        public bool Close(long pActorId, int pYear, string pReason,
            out SchoolMembershipRecord pClosed)
        {
            pClosed = null;
            if (!_activeByActor.TryGetValue(pActorId, out SchoolMembershipRecord current))
                return false;
            pClosed = current.Close(pYear, pReason);
            RemoveActive(current);
            _closed.Add(pClosed);
            return true;
        }

        public SchoolMembershipRecord GetActive(long pActorId)
        {
            return _activeByActor.TryGetValue(pActorId, out SchoolMembershipRecord value)
                ? value
                : null;
        }

        public string GetSchool(long pActorId)
        {
            return GetActive(pActorId)?.SchoolId ?? CourtSchoolId.None;
        }

        public IReadOnlyList<long> Members(string pSchoolId)
        {
            if (!_actorsBySchool.TryGetValue(pSchoolId ?? "", out HashSet<long> actors))
                return Array.Empty<long>();
            long[] result = actors.ToArray();
            Array.Sort(result);
            return result;
        }

        public void Clear()
        {
            _activeByActor.Clear();
            _actorsBySchool.Clear();
            _closed.Clear();
        }

        private void AddActive(SchoolMembershipRecord pRecord)
        {
            _activeByActor[pRecord.ActorId] = pRecord;
            if (!_actorsBySchool.TryGetValue(pRecord.SchoolId, out HashSet<long> actors))
            {
                actors = new HashSet<long>();
                _actorsBySchool[pRecord.SchoolId] = actors;
            }
            actors.Add(pRecord.ActorId);
        }

        private void RemoveActive(SchoolMembershipRecord pRecord)
        {
            _activeByActor.Remove(pRecord.ActorId);
            if (!_actorsBySchool.TryGetValue(pRecord.SchoolId, out HashSet<long> actors)) return;
            actors.Remove(pRecord.ActorId);
            if (actors.Count == 0) _actorsBySchool.Remove(pRecord.SchoolId);
        }
    }
}
