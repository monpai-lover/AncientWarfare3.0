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

    public enum SchoolPersistenceOutcome
    {
        Committed,
        CleanFailure,
        Unknown
    }

    public enum SchoolPersistenceRowState
    {
        Missing,
        Exact,
        Conflict
    }

    public enum SchoolDeathPersistenceRowState
    {
        Unchanged,
        Original,
        Committed,
        Conflict
    }

    public enum SchoolDeathOutcome
    {
        NotApplicable,
        Committed,
        Failed
    }

    public static class HistoricalSchoolPersistenceRules
    {
        public static SchoolPersistenceOutcome Resolve(bool pQuerySucceeded,
            SchoolPersistenceRowState pMembership, SchoolPersistenceRowState pMaster,
            SchoolPersistenceRowState pAffiliation)
        {
            if (!pQuerySucceeded) return SchoolPersistenceOutcome.Unknown;
            if (pMembership == SchoolPersistenceRowState.Exact &&
                pMaster == SchoolPersistenceRowState.Exact &&
                pAffiliation == SchoolPersistenceRowState.Exact)
                return SchoolPersistenceOutcome.Committed;
            if (pMembership == SchoolPersistenceRowState.Missing &&
                pMaster == SchoolPersistenceRowState.Missing &&
                pAffiliation == SchoolPersistenceRowState.Missing)
                return SchoolPersistenceOutcome.CleanFailure;
            return SchoolPersistenceOutcome.Unknown;
        }

        public static bool CanDestroy(SchoolPersistenceOutcome pOutcome)
        {
            return pOutcome == SchoolPersistenceOutcome.CleanFailure;
        }
    }

    public static class HistoricalSchoolDeathPersistenceRules
    {
        public static SchoolPersistenceOutcome Resolve(bool pQuerySucceeded,
            SchoolDeathPersistenceRowState pMembership,
            SchoolDeathPersistenceRowState pAffiliation,
            SchoolDeathPersistenceRowState pMaster)
        {
            if (!pQuerySucceeded) return SchoolPersistenceOutcome.Unknown;
            if (pMembership == SchoolDeathPersistenceRowState.Committed &&
                IsCommitted(pAffiliation) &&
                IsCommitted(pMaster)) return SchoolPersistenceOutcome.Committed;
            if (pMembership == SchoolDeathPersistenceRowState.Original &&
                IsOriginal(pAffiliation) && IsOriginal(pMaster))
                return SchoolPersistenceOutcome.CleanFailure;
            return SchoolPersistenceOutcome.Unknown;
        }

        private static bool IsCommitted(SchoolDeathPersistenceRowState pState)
        {
            return pState == SchoolDeathPersistenceRowState.Committed ||
                   pState == SchoolDeathPersistenceRowState.Unchanged;
        }

        private static bool IsOriginal(SchoolDeathPersistenceRowState pState)
        {
            return pState == SchoolDeathPersistenceRowState.Original ||
                   pState == SchoolDeathPersistenceRowState.Unchanged;
        }
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

    public sealed class HistoricalSchoolHomeCandidate
    {
        public HistoricalSchoolHomeCandidate(long pKingdomId, long pCityId, string pKingdomName,
            bool pLivingXia, int pExistingMasterCount, bool pCapital, float pDevelopment,
            int pPopulation)
        {
            KingdomId = pKingdomId;
            CityId = pCityId;
            KingdomName = pKingdomName ?? "";
            LivingXia = pLivingXia;
            ExistingMasterCount = Math.Max(0, pExistingMasterCount);
            Capital = pCapital;
            Development = Math.Max(0f, pDevelopment);
            Population = Math.Max(0, pPopulation);
        }

        public long KingdomId { get; }
        public long CityId { get; }
        public string KingdomName { get; }
        public bool LivingXia { get; }
        public int ExistingMasterCount { get; }
        public bool Capital { get; }
        public float Development { get; }
        public int Population { get; }
    }

    public sealed class HistoricalSchoolAffiliationSnapshot
    {
        public HistoricalSchoolAffiliationSnapshot(long pActorId, long pHomeKingdomId,
            string pHomeKingdomName, long pHometownCityId, long pResidenceCityId,
            long pPreviousResidenceCityId, long pDestinationCityId, long pServiceKingdomId,
            HistoricalSchoolLifecycleState pLifecycleState, int pServiceStartYear,
            int pServiceEndYear, int pLastTravelYear, int pTravelWaitStartYear,
            int pVoyageStartYear, int pVoyageArrivalYear, int pTransportFailures)
        {
            ActorId = pActorId;
            HomeKingdomId = pHomeKingdomId;
            HomeKingdomName = pHomeKingdomName ?? "";
            HometownCityId = pHometownCityId;
            ResidenceCityId = pResidenceCityId;
            PreviousResidenceCityId = pPreviousResidenceCityId;
            DestinationCityId = pDestinationCityId;
            ServiceKingdomId = pServiceKingdomId;
            LifecycleState = pLifecycleState;
            ServiceStartYear = pServiceStartYear;
            ServiceEndYear = pServiceEndYear;
            LastTravelYear = pLastTravelYear;
            TravelWaitStartYear = pTravelWaitStartYear;
            VoyageStartYear = pVoyageStartYear;
            VoyageArrivalYear = pVoyageArrivalYear;
            TransportFailures = Math.Max(0, pTransportFailures);
        }

        public long ActorId { get; }
        public long HomeKingdomId { get; }
        public string HomeKingdomName { get; }
        public long HometownCityId { get; }
        public long ResidenceCityId { get; }
        public long PreviousResidenceCityId { get; }
        public long DestinationCityId { get; }
        public long ServiceKingdomId { get; }
        public HistoricalSchoolLifecycleState LifecycleState { get; }
        public int ServiceStartYear { get; }
        public int ServiceEndYear { get; }
        public int LastTravelYear { get; }
        public int TravelWaitStartYear { get; }
        public int VoyageStartYear { get; }
        public int VoyageArrivalYear { get; }
        public int TransportFailures { get; }

        public static HistoricalSchoolAffiliationSnapshot CreateHome(long pActorId,
            long pHomeKingdomId, string pHomeKingdomName, long pHometownCityId, int pYear)
        {
            return new HistoricalSchoolAffiliationSnapshot(pActorId, pHomeKingdomId,
                pHomeKingdomName, pHometownCityId, pHometownCityId, -1, -1, -1,
                HistoricalSchoolLifecycleState.AtHome, -1, -1, pYear, -1, -1, -1, 0);
        }

        public HistoricalSchoolAffiliationSnapshot ChooseDestination(long pDestinationCityId,
            int pYear)
        {
            if (!HistoricalSchoolRules.CanTravelTransition(LifecycleState,
                    HistoricalSchoolLifecycleState.ChoosingDestination) ||
                pDestinationCityId < 0 || pDestinationCityId == ResidenceCityId)
                return this;
            return Copy(pResidenceCityId: ResidenceCityId,
                pPreviousResidenceCityId: PreviousResidenceCityId,
                pDestinationCityId: pDestinationCityId, pServiceKingdomId: ServiceKingdomId,
                pState: HistoricalSchoolLifecycleState.ChoosingDestination,
                pLastTravelYear: pYear, pTravelWaitStartYear: pYear,
                pTransportFailures: 0);
        }

        public HistoricalSchoolAffiliationSnapshot StartTravel()
        {
            if (!HistoricalSchoolRules.CanTravelTransition(LifecycleState,
                    HistoricalSchoolLifecycleState.Travelling) || DestinationCityId < 0)
                return this;
            return Copy(ResidenceCityId, PreviousResidenceCityId, DestinationCityId,
                ServiceKingdomId, HistoricalSchoolLifecycleState.Travelling,
                LastTravelYear, TravelWaitStartYear, TransportFailures);
        }

        public HistoricalSchoolAffiliationSnapshot Arrive(long pResidenceCityId, int pYear)
        {
            if ((LifecycleState != HistoricalSchoolLifecycleState.Travelling &&
                 LifecycleState != HistoricalSchoolLifecycleState.Voyage) ||
                pResidenceCityId < 0 || pResidenceCityId != DestinationCityId) return this;
            return Copy(pResidenceCityId, ResidenceCityId, -1, -1,
                HistoricalSchoolLifecycleState.Resident, pYear, -1, 0,
                pVoyageStartYear: -1, pVoyageArrivalYear: -1);
        }

        public HistoricalSchoolAffiliationSnapshot BeginService(long pKingdomId,
            int pStartYear, int pEndYear)
        {
            if (pKingdomId < 0 || pEndYear <= pStartYear ||
                LifecycleState == HistoricalSchoolLifecycleState.Travelling ||
                LifecycleState == HistoricalSchoolLifecycleState.Voyage ||
                LifecycleState == HistoricalSchoolLifecycleState.Dead) return this;
            return Copy(ResidenceCityId, PreviousResidenceCityId, -1, pKingdomId,
                HistoricalSchoolLifecycleState.Serving, LastTravelYear,
                TravelWaitStartYear, TransportFailures, pStartYear, pEndYear);
        }

        public HistoricalSchoolAffiliationSnapshot EndService(int pYear)
        {
            if (ServiceKingdomId < 0 || LifecycleState != HistoricalSchoolLifecycleState.Serving)
                return this;
            HistoricalSchoolLifecycleState state = ResidenceCityId == HometownCityId
                ? HistoricalSchoolLifecycleState.AtHome
                : HistoricalSchoolLifecycleState.Resident;
            return Copy(ResidenceCityId, PreviousResidenceCityId, -1, -1, state,
                LastTravelYear, TravelWaitStartYear, TransportFailures,
                pServiceStartYear: -1, pServiceEndYear: Math.Max(ServiceStartYear, pYear));
        }

        public HistoricalSchoolAffiliationSnapshot CancelTravel()
        {
            if (LifecycleState != HistoricalSchoolLifecycleState.Travelling &&
                LifecycleState != HistoricalSchoolLifecycleState.Voyage) return this;
            HistoricalSchoolLifecycleState state = ResidenceCityId == HometownCityId
                ? HistoricalSchoolLifecycleState.AtHome
                : HistoricalSchoolLifecycleState.Resident;
            return Copy(ResidenceCityId, PreviousResidenceCityId, -1, -1, state,
                LastTravelYear, -1, 0, -1, -1, -1, -1);
        }

        public HistoricalSchoolAffiliationSnapshot RegisterTransportFailure(int pYear)
        {
            int waitStart = TravelWaitStartYear >= 0 ? TravelWaitStartYear : pYear;
            return Copy(ResidenceCityId, PreviousResidenceCityId, DestinationCityId,
                ServiceKingdomId, LifecycleState, LastTravelYear, waitStart,
                TransportFailures + 1, ServiceStartYear, ServiceEndYear,
                VoyageStartYear, VoyageArrivalYear);
        }

        public HistoricalSchoolAffiliationSnapshot BeginVoyage(int pStartYear,
            int pArrivalYear)
        {
            if (LifecycleState != HistoricalSchoolLifecycleState.Travelling ||
                pArrivalYear <= pStartYear) return this;
            return Copy(ResidenceCityId, PreviousResidenceCityId, DestinationCityId, -1,
                HistoricalSchoolLifecycleState.Voyage, LastTravelYear, TravelWaitStartYear,
                TransportFailures, -1, -1, pStartYear, pArrivalYear);
        }

        private HistoricalSchoolAffiliationSnapshot Copy(long pResidenceCityId,
            long pPreviousResidenceCityId, long pDestinationCityId, long pServiceKingdomId,
            HistoricalSchoolLifecycleState pState, int pLastTravelYear,
            int pTravelWaitStartYear, int pTransportFailures, int? pServiceStartYear = null,
            int? pServiceEndYear = null, int? pVoyageStartYear = null,
            int? pVoyageArrivalYear = null)
        {
            return new HistoricalSchoolAffiliationSnapshot(ActorId, HomeKingdomId,
                HomeKingdomName, HometownCityId, pResidenceCityId,
                pPreviousResidenceCityId, pDestinationCityId, pServiceKingdomId, pState,
                pServiceStartYear ?? ServiceStartYear, pServiceEndYear ?? ServiceEndYear,
                pLastTravelYear, pTravelWaitStartYear,
                pVoyageStartYear ?? VoyageStartYear,
                pVoyageArrivalYear ?? VoyageArrivalYear, pTransportFailures);
        }
    }

    public sealed class HistoricalSchoolTravelContext
    {
        public HistoricalSchoolTravelContext(long pActorId, long pResidenceCityId,
            long pPreviousResidenceCityId, int pLastTravelYear, int pCurrentYear,
            bool pServing)
        {
            ActorId = pActorId;
            ResidenceCityId = pResidenceCityId;
            PreviousResidenceCityId = pPreviousResidenceCityId;
            LastTravelYear = pLastTravelYear;
            CurrentYear = pCurrentYear;
            Serving = pServing;
        }

        public long ActorId { get; }
        public long ResidenceCityId { get; }
        public long PreviousResidenceCityId { get; }
        public int LastTravelYear { get; }
        public int CurrentYear { get; }
        public bool Serving { get; }
    }

    public sealed class HistoricalSchoolTravelCandidate
    {
        public HistoricalSchoolTravelCandidate(long pCityId, long pKingdomId, int pPopulation,
            float pDevelopment, bool pCapital, float pSchoolUnderrepresentation,
            int pDebateRivals, int pDiscipleCandidates, bool pReceptiveRuler,
            bool pOpenOffice, float pProblemMatch, bool pTransportAvailable, bool pAtWar,
            bool pOccupied, bool pDisaster, int pSquaredDistance)
        {
            CityId = pCityId;
            KingdomId = pKingdomId;
            Population = Math.Max(0, pPopulation);
            Development = Math.Max(0f, pDevelopment);
            Capital = pCapital;
            SchoolUnderrepresentation = Bound01(pSchoolUnderrepresentation);
            DebateRivals = Math.Max(0, pDebateRivals);
            DiscipleCandidates = Math.Max(0, pDiscipleCandidates);
            ReceptiveRuler = pReceptiveRuler;
            OpenOffice = pOpenOffice;
            ProblemMatch = Bound01(pProblemMatch);
            TransportAvailable = pTransportAvailable;
            AtWar = pAtWar;
            Occupied = pOccupied;
            Disaster = pDisaster;
            SquaredDistance = Math.Max(0, pSquaredDistance);
        }

        public long CityId { get; }
        public long KingdomId { get; }
        public int Population { get; }
        public float Development { get; }
        public bool Capital { get; }
        public float SchoolUnderrepresentation { get; }
        public int DebateRivals { get; }
        public int DiscipleCandidates { get; }
        public bool ReceptiveRuler { get; }
        public bool OpenOffice { get; }
        public float ProblemMatch { get; }
        public bool TransportAvailable { get; }
        public bool AtWar { get; }
        public bool Occupied { get; }
        public bool Disaster { get; }
        public int SquaredDistance { get; }

        public HistoricalSchoolTravelCandidate WithCity(long pCityId)
        {
            return new HistoricalSchoolTravelCandidate(pCityId, KingdomId, Population,
                Development, Capital, SchoolUnderrepresentation, DebateRivals,
                DiscipleCandidates, ReceptiveRuler, OpenOffice, ProblemMatch,
                TransportAvailable, AtWar, Occupied, Disaster, SquaredDistance);
        }

        private static float Bound01(float pValue)
        {
            if (float.IsNaN(pValue) || float.IsInfinity(pValue)) return 0f;
            return Math.Max(0f, Math.Min(1f, pValue));
        }
    }

    public sealed class SchoolLineageCandidate
    {
        public SchoolLineageCandidate(long pActorId, bool pAlive, bool pDirectDisciple,
            float pReputation, float pLearning, int pDebateWins, int pFollowerCount)
        {
            ActorId = pActorId;
            Alive = pAlive;
            DirectDisciple = pDirectDisciple;
            Reputation = Math.Max(0f, pReputation);
            Learning = Math.Max(0f, pLearning);
            DebateWins = Math.Max(0, pDebateWins);
            FollowerCount = Math.Max(0, pFollowerCount);
        }

        public long ActorId { get; }
        public bool Alive { get; }
        public bool DirectDisciple { get; }
        public float Reputation { get; }
        public float Learning { get; }
        public int DebateWins { get; }
        public int FollowerCount { get; }
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

        public SchoolMembershipRecord WithReputation(float pReputation)
        {
            return new SchoolMembershipRecord(MembershipId, ActorId, SchoolId, Source, SourceId,
                TeacherActorId, CityId, Generation, Math.Max(0f, Math.Min(100f, pReputation)),
                StartYear, EndYear, Active, EndReason);
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

        public long Version { get; private set; }

        public bool TryJoin(SchoolMembershipRecord pRecord)
        {
            if (pRecord == null || !pRecord.Active || !pRecord.IsValid ||
                _activeByActor.ContainsKey(pRecord.ActorId)) return false;
            AddActive(pRecord);
            MarkChanged();
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
            AddActive(pReplacement);
            MarkChanged();
            return true;
        }

        public bool RollbackConvert(long pActorId, SchoolMembershipRecord pOriginal,
            SchoolMembershipRecord pReplacement)
        {
            if (pOriginal == null || pReplacement == null ||
                !pOriginal.Active || pOriginal.ActorId != pActorId ||
                !_activeByActor.TryGetValue(pActorId, out SchoolMembershipRecord current) ||
                current.MembershipId != pReplacement.MembershipId)
                return false;
            RemoveActive(current);
            AddActive(pOriginal);
            MarkChanged();
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
            MarkChanged();
            return true;
        }

        public bool CloseExpected(long pActorId, long pMembershipId, int pYear,
            string pReason, out SchoolMembershipRecord pClosed)
        {
            pClosed = null;
            if (!_activeByActor.TryGetValue(pActorId, out SchoolMembershipRecord current) ||
                current.MembershipId != pMembershipId) return false;
            pClosed = current.Close(pYear, pReason);
            RemoveActive(current);
            MarkChanged();
            return true;
        }

        public bool RollbackJoin(long pActorId)
        {
            if (!_activeByActor.TryGetValue(pActorId, out SchoolMembershipRecord current))
                return false;
            RemoveActive(current);
            MarkChanged();
            return true;
        }

        public bool UpdateReputation(long pActorId, float pDelta)
        {
            if (!_activeByActor.TryGetValue(pActorId, out SchoolMembershipRecord current))
                return false;
            _activeByActor[pActorId] = current.WithReputation(current.Reputation + pDelta);
            MarkChanged();
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

        public IEnumerable<SchoolMembershipRecord> ActiveRecords()
        {
            foreach (SchoolMembershipRecord record in _activeByActor.Values)
                yield return record;
        }

        public void Clear()
        {
            _activeByActor.Clear();
            _actorsBySchool.Clear();
            MarkChanged();
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

        private void MarkChanged()
        {
            Version = Version == long.MaxValue ? 1L : Version + 1L;
        }
    }
}
