using System;

namespace AncientWarfare3.core.schools
{
    internal readonly struct HistoricalSchoolTeachingDbResult
    {
        public HistoricalSchoolTeachingDbResult(
            HistoricalSchoolTeachingPersistenceOutcome pOutcome)
        {
            Outcome = pOutcome;
        }

        public HistoricalSchoolTeachingPersistenceOutcome Outcome { get; }
        public bool IsCommitted => Outcome ==
            HistoricalSchoolTeachingPersistenceOutcome.Committed ||
            Outcome == HistoricalSchoolTeachingPersistenceOutcome.Replayed;
        public bool PersistedNew => Outcome ==
            HistoricalSchoolTeachingPersistenceOutcome.Committed;
    }

    internal sealed class HistoricalSchoolTeachingEventRow
    {
        public long EventId = -1L;
        public string OperationKey = "";
        public string EventType = "";
        public long ActorId = -1L;
        public long TargetActorId = -1L;
        public string SchoolId = "";
        public long CityId = -1L;
        public long KingdomId = -1L;
        public int EventYear = -1;
        public string Payload = "";
        public int Importance;
        public double WorldTime;

        public bool Exact(HistoricalSchoolTeachingEventRow pOther,
            bool pRequireEventId)
        {
            return pOther != null && (!pRequireEventId || EventId == pOther.EventId) &&
                   OperationKey == pOther.OperationKey && EventType == pOther.EventType &&
                   ActorId == pOther.ActorId && TargetActorId == pOther.TargetActorId &&
                   SchoolId == pOther.SchoolId && CityId == pOther.CityId &&
                   KingdomId == pOther.KingdomId && EventYear == pOther.EventYear &&
                   Payload == pOther.Payload && Importance == pOther.Importance &&
                   WorldTime.Equals(pOther.WorldTime);
        }

        public bool MatchesStableReplay(HistoricalSchoolTeachingEventRow pOther,
            bool pRequireEventId)
        {
            return pOther != null &&
                   (!pRequireEventId || EventId == pOther.EventId) &&
                   OperationKey == pOther.OperationKey &&
                   EventType == pOther.EventType &&
                   ActorId == pOther.ActorId &&
                   TargetActorId == pOther.TargetActorId &&
                   SchoolId == pOther.SchoolId &&
                   CityId == pOther.CityId &&
                   KingdomId == pOther.KingdomId &&
                   EventYear == pOther.EventYear &&
                   Importance == pOther.Importance;
        }
    }

    internal sealed class HistoricalSchoolTeachingLedgerRow
    {
        public string LedgerKey = "";
        public long CityId = -1L;
        public string SchoolId = "";
        public double Tradition;
        public double Membership;
        public double Institutions;
        public double ActivePresence;
        public double Momentum;
        public int LastActiveYear = -1;
        public int LastDecayYear = -1;
        public double UpdatedTime;

        public HistoricalSchoolTeachingLedgerRow Copy()
        {
            return (HistoricalSchoolTeachingLedgerRow)MemberwiseClone();
        }

        public bool Exact(HistoricalSchoolTeachingLedgerRow pOther)
        {
            return pOther != null && LedgerKey == pOther.LedgerKey &&
                   CityId == pOther.CityId && SchoolId == pOther.SchoolId &&
                   Tradition.Equals(pOther.Tradition) &&
                   Membership.Equals(pOther.Membership) &&
                   Institutions.Equals(pOther.Institutions) &&
                   ActivePresence.Equals(pOther.ActivePresence) &&
                   Momentum.Equals(pOther.Momentum) &&
                   LastActiveYear == pOther.LastActiveYear &&
                   LastDecayYear == pOther.LastDecayYear &&
                   UpdatedTime.Equals(pOther.UpdatedTime);
        }
    }

    internal sealed class HistoricalSchoolTeachingDbRequest
    {
        public HistoricalSchoolTeachingDbRequest(HistoricalSchoolTeachingPlan pPlan,
            string pActorName, long pTargetActorId, string pTargetActorName,
            double pWorldTime)
        {
            Plan = pPlan;
            ActorName = pActorName ?? "";
            TargetActorId = pTargetActorId;
            TargetActorName = pTargetActorName ?? "";
            WorldTime = pWorldTime;
            LectureOperationKey = pPlan.OperationKey + "|event=lecture";
            PersuasionOperationKey = pPlan.OperationKey + "|event=persuasion";
            Lecture = Event("lecture", LectureOperationKey, -1L, ActorName);
            if (pPlan.IncludePersuasion)
                Persuasion = Event("persuasion", PersuasionOperationKey,
                    TargetActorId, ActorName + "|" + TargetActorName);
        }

        public HistoricalSchoolTeachingPlan Plan { get; }
        public string ActorName { get; }
        public long TargetActorId { get; }
        public string TargetActorName { get; }
        public double WorldTime { get; }
        public string LectureOperationKey { get; }
        public string PersuasionOperationKey { get; }
        public HistoricalSchoolTeachingEventRow Lecture { get; }
        public HistoricalSchoolTeachingEventRow Persuasion { get; }
        public HistoricalSchoolTeachingLedgerRow OriginalLedger { get; set; }
        public HistoricalSchoolTeachingLedgerRow DesiredLedger { get; set; }
        public bool OriginalLedgerCaptured { get; set; }
        public bool IdsFrozen => Lecture.EventId >= 0 &&
            (!Plan.IncludePersuasion || Persuasion?.EventId >= 0);

        public void FreezeIds(long pLectureEventId, long pPersuasionEventId)
        {
            if (pLectureEventId < 0 ||
                (Plan.IncludePersuasion && pPersuasionEventId < 0))
                throw new ArgumentOutOfRangeException(nameof(pLectureEventId));
            if (IdsFrozen && (Lecture.EventId != pLectureEventId ||
                (Plan.IncludePersuasion && Persuasion.EventId != pPersuasionEventId)))
                throw new InvalidOperationException("teaching event ids are already frozen");
            Lecture.EventId = pLectureEventId;
            if (Plan.IncludePersuasion) Persuasion.EventId = pPersuasionEventId;
        }

        public HistoricalSchoolTeachingDbRequest CloneForBackground()
        {
            var clone = new HistoricalSchoolTeachingDbRequest(Plan, ActorName,
                TargetActorId, TargetActorName, WorldTime)
            {
                OriginalLedger = OriginalLedger?.Copy(),
                DesiredLedger = DesiredLedger?.Copy(),
                OriginalLedgerCaptured = OriginalLedgerCaptured
            };
            if (IdsFrozen)
                clone.FreezeIds(Lecture.EventId,
                    Plan.IncludePersuasion ? Persuasion.EventId : -1L);
            return clone;
        }

        private HistoricalSchoolTeachingEventRow Event(string pType, string pKey,
            long pTargetActorId, string pPayload)
        {
            HistoricalSchoolLectureCandidate candidate = Plan.Candidate;
            return new HistoricalSchoolTeachingEventRow
            {
                OperationKey = pKey,
                EventType = pType,
                ActorId = candidate.ActorId,
                TargetActorId = pTargetActorId,
                SchoolId = candidate.SchoolId,
                CityId = candidate.CityId,
                KingdomId = candidate.KingdomId,
                EventYear = Plan.Year,
                Payload = pPayload ?? "",
                Importance = 1,
                WorldTime = WorldTime
            };
        }
    }
}
