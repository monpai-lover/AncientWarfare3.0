using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.schools
{
    /// <summary>
    /// Runs the bounded, yearly debate pass for real school members already living in
    /// world cities. Persistence is deliberately completed before actor/UI side effects.
    /// </summary>
    internal static class HistoricalSchoolDebateService
    {
        public const int MaxDebatesPerYear = 48;
        private const int MaxCitiesPerYear = 256;
        private const int MaxActorsPerCity = 96;
        private const int MaxDebateCandidatesPerCity = 32;
        private const int MaxIndexedActorsPerYear = 4096;

        private static int _lastProcessedYear = -1;
        private static readonly Dictionary<long, int> DebateWins =
            new Dictionary<long, int>();

        public static void ClearRuntime()
        {
            _lastProcessedYear = -1;
            DebateWins.Clear();
        }

        public static void LoadState()
        {
            ClearRuntime();
            foreach (KeyValuePair<long, int> entry in HistoricalSchoolStore.LoadDebateWins())
                if (entry.Key >= 0 && entry.Value > 0) DebateWins[entry.Key] = entry.Value;
        }

        public static void ProcessYear(int pYear,
            HistoricalSchoolAnnualMemberSnapshot<Actor> pMembers)
        {
            if (pYear < 0 || World.world == null || pMembers == null ||
                pYear == _lastProcessedYear) return;
            _lastProcessedYear = pYear;

            int budget = MaxDebatesPerYear;
            int institutionBudget = 4;
            int processedCities = 0;
            var usedActors = new HashSet<long>();
            IReadOnlyDictionary<long, int> directDiscipleCounts =
                pMembers.DirectDiscipleCounts;
            Dictionary<long, List<Actor>> actorsByResidence =
                BuildResidenceActorIndex(pMembers);
            foreach (City city in LivingCities())
            {
                if (budget <= 0 || processedCities++ >= MaxCitiesPerYear) break;
                if (city?.data == null || city.isRekt()) continue;

                // Cheap filtering (real actors, membership, residence, lifecycle) happens
                // before any ledger reads or score calculations.
                actorsByResidence.TryGetValue(city.data.id, out List<Actor> residentActors);
                List<DebateCandidateContext> contexts = CollectCandidates(city, usedActors,
                    directDiscipleCounts, residentActors);
                if (contexts.Count < 2) continue;

                Dictionary<string, HistoricalSchoolLedgerSnapshot> cityLedgers =
                    HistoricalSchoolStore.LoadLedgersForCity(city.data.id);
                HistoricalSchoolDebatePair pair = SelectPairByLedger(contexts, cityLedgers);
                if (pair == null) continue;
                DebateCandidateContext first = contexts.FirstOrDefault(p =>
                    p.Candidate.ActorId == pair.First.ActorId);
                DebateCandidateContext second = contexts.FirstOrDefault(p =>
                    p.Candidate.ActorId == pair.Second.ActorId);
                if (first == null || second == null ||
                    usedActors.Contains(first.Candidate.ActorId) ||
                    usedActors.Contains(second.Candidate.ActorId)) continue;

                HistoricalSchoolMasterDefinition firstDefinition = DefinitionFor(first.Actor,
                    first.Membership.SchoolId);
                HistoricalSchoolMasterDefinition secondDefinition = DefinitionFor(second.Actor,
                    second.Membership.SchoolId);
                string topic = HistoricalSchoolDebateRules.SelectTopic(firstDefinition,
                    secondDefinition, CityTopics(city));
                if (string.IsNullOrWhiteSpace(topic)) continue;

                HistoricalSchoolLedgerSnapshot firstLedger = LedgerFor(city.data.id,
                    first.Membership.SchoolId, cityLedgers);
                HistoricalSchoolLedgerSnapshot secondLedger = LedgerFor(city.data.id,
                    second.Membership.SchoolId, cityLedgers);
                HistoricalSchoolDebateScore firstScore = HistoricalSchoolDebateRules.Score(
                    first.Candidate, topic, firstLedger);
                HistoricalSchoolDebateScore secondScore = HistoricalSchoolDebateRules.Score(
                    second.Candidate, topic, secondLedger);
                SchoolDebateOutcome outcome = HistoricalSchoolDebateRules.ResolveOutcome(
                    firstScore.Value, secondScore.Value);
                long seed = DebateSeed(city.data.id, pYear, first.Candidate.ActorId,
                    second.Candidate.ActorId, topic);
                double worldTime = World.world.getCurWorldTime();
                var debate = new HistoricalSchoolDebateRecord(-1L, city.data.id, pYear,
                    topic, first.Candidate.ActorId, first.Membership.SchoolId,
                    second.Candidate.ActorId, second.Membership.SchoolId, seed,
                    firstScore.Value, secondScore.Value, outcome, pUpdatedTime: worldTime);
                HistoricalSchoolLedgerDelta firstDelta = HistoricalSchoolDebateRules.LedgerDelta(
                    outcome, pFirstWon: true, pYear).ForSchool(first.Membership.SchoolId);
                HistoricalSchoolLedgerDelta secondDelta = HistoricalSchoolDebateRules.LedgerDelta(
                    outcome, pFirstWon: false, pYear).ForSchool(second.Membership.SchoolId);

                if (!HistoricalSchoolStore.TryRecordDebateAndLedger(debate, firstDelta,
                        secondDelta, worldTime)) continue;

                usedActors.Add(first.Candidate.ActorId);
                usedActors.Add(second.Candidate.ActorId);
                if (institutionBudget > 0 && TryFoundInstitutionAfterDebate(first, second,
                        firstDefinition, secondDefinition, city, pYear, worldTime))
                    institutionBudget--;
                ApplyCommittedDebate(first.Actor, second.Actor, city, outcome, topic, pYear);
                budget--;
            }
        }

        private static List<DebateCandidateContext> CollectCandidates(City pCity,
            ISet<long> pUsedActors, IReadOnlyDictionary<long, int> pDirectDiscipleCounts,
            IEnumerable<Actor> pResidentActors)
        {
            var result = new List<DebateCandidateContext>();
            int scanned = 0;
            try
            {
                var actors = new Dictionary<long, Actor>();
                if (pResidentActors != null)
                    foreach (Actor actor in pResidentActors)
                        if (actor?.data != null) actors[actor.data.id] = actor;
                foreach (Actor actor in pCity.units)
                {
                    if (++scanned > MaxActorsPerCity) break;
                    if (actor?.data != null && !actors.ContainsKey(actor.data.id))
                        actors.Add(actor.data.id, actor);
                }
                foreach (Actor actor in actors.Values.OrderBy(p => p.data.id))
                {
                    if (actor?.data == null || actor.isRekt() || !actor.isAlive() ||
                        actor.isBaby() || pUsedActors.Contains(actor.data.id)) continue;

                    SchoolMembershipRecord membership = SchoolMembershipService.GetActive(
                        actor.data.id);
                    if (membership == null || CourtSchoolRegistry.Find(membership.SchoolId) == null ||
                        !SchoolLineageService.IsQualifiedTeacher(actor)) continue;

                    City residence = HistoricalAffiliationService.ResidenceCity(actor);
                    if (residence == null) residence = actor.city;
                    if (residence?.data == null || residence.isRekt() ||
                        residence.data.id != pCity.data.id ||
                        !HistoricalAffiliationService.IsPresentForInfluence(actor) ||
                        IsExcludedLifecycle(actor)) continue;

                    HistoricalSchoolMasterDefinition definition = DefinitionFor(actor,
                        membership.SchoolId);
                    if (definition == null) continue;
                    int directDisciples = pDirectDiscipleCounts != null &&
                        pDirectDiscipleCounts.TryGetValue(actor.data.id, out int count)
                        ? count
                        : 0;
                    float problemMatch = ProblemMatch(definition, pCity);
                    bool canonical = HistoricalSchoolDescentService.IsCanonicalMaster(actor);
                    float diplomacy = canonical ? definition.Abilities.Diplomacy :
                        SafeStat(actor, "diplomacy");
                    float intelligence = canonical ? definition.Abilities.Intelligence :
                        SafeStat(actor, "intelligence");
                    float warfare = canonical ? definition.Abilities.Warfare :
                        SafeStat(actor, "warfare");
                    float stewardship = canonical ? definition.Abilities.Stewardship :
                        SafeStat(actor, "stewardship");
                    var candidate = new HistoricalSchoolDebateCandidate(actor.data.id,
                        membership.SchoolId, Reputation(actor, membership),
                        canonical ? definition.Abilities.Intelligence : Learning(actor),
                        diplomacy, intelligence, warfare, stewardship,
                        directDisciples, DebateWinCount(actor.data.id), problemMatch,
                        definition.DebateTopics, pAlive: true, pPresent: true);
                    result.Add(new DebateCandidateContext(actor, membership, candidate));
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school debate candidate scan failed: " +
                                    error.Message);
            }

            return LimitCandidatesBySchool(result);
        }

        private static List<DebateCandidateContext> LimitCandidatesBySchool(
            IEnumerable<DebateCandidateContext> pCandidates)
        {
            DebateCandidateContext[] candidates = (pCandidates ??
                    Array.Empty<DebateCandidateContext>())
                .Where(p => p?.Candidate != null && p.Membership != null)
                .OrderBy(p => p.Membership.SchoolId, StringComparer.Ordinal)
                .ThenBy(p => p.Candidate.ActorId)
                .ToArray();
            var selected = new List<DebateCandidateContext>(MaxDebateCandidatesPerCity);
            var selectedIds = new HashSet<long>();
            foreach (IGrouping<string, DebateCandidateContext> group in candidates.GroupBy(
                         p => p.Membership.SchoolId, StringComparer.Ordinal))
            {
                DebateCandidateContext first = group.FirstOrDefault();
                if (first == null || !selectedIds.Add(first.Candidate.ActorId)) continue;
                selected.Add(first);
                if (selected.Count >= MaxDebateCandidatesPerCity) return selected;
            }
            foreach (DebateCandidateContext candidate in candidates.OrderBy(
                         p => p.Candidate.ActorId).ThenBy(p => p.Membership.SchoolId,
                         StringComparer.Ordinal))
            {
                if (selected.Count >= MaxDebateCandidatesPerCity) break;
                if (selectedIds.Add(candidate.Candidate.ActorId)) selected.Add(candidate);
            }
            return selected.OrderBy(p => p.Candidate.ActorId)
                .ThenBy(p => p.Membership.SchoolId, StringComparer.Ordinal).ToList();
        }

        private static HistoricalSchoolDebatePair SelectPairByLedger(
            IEnumerable<DebateCandidateContext> pContexts,
            IReadOnlyDictionary<string, HistoricalSchoolLedgerSnapshot> pLedgers)
        {
            if (pContexts == null) return null;
            var groups = pContexts.Where(p => p?.Candidate != null && p.Membership != null)
                .GroupBy(p => p.Membership.SchoolId, StringComparer.Ordinal)
                .Select(group => new
                {
                    SchoolId = group.Key,
                    Candidate = group.OrderBy(p => p.Candidate.ActorId).First(),
                    Ledger = pLedgers != null && pLedgers.TryGetValue(group.Key,
                        out HistoricalSchoolLedgerSnapshot ledger)
                        ? ledger
                        : null
                })
                .OrderByDescending(p => LedgerPresence(p.Ledger))
                .ThenBy(p => p.SchoolId, StringComparer.Ordinal)
                .Take(2)
                .ToArray();
            if (groups.Length != 2) return null;
            return HistoricalSchoolDebateRules.SelectPair(groups.Select(p => p.Candidate.Candidate));
        }

        private static HistoricalSchoolLedgerSnapshot LedgerFor(long pCityId, string pSchoolId,
            IReadOnlyDictionary<string, HistoricalSchoolLedgerSnapshot> pLedgers)
        {
            if (pLedgers != null && pLedgers.TryGetValue(pSchoolId ?? "",
                    out HistoricalSchoolLedgerSnapshot ledger)) return ledger;
            return HistoricalSchoolStore.LoadLedger(pCityId, pSchoolId);
        }

        private static double LedgerPresence(HistoricalSchoolLedgerSnapshot pLedger)
        {
            if (pLedger == null) return 0d;
            return pLedger.Tradition + pLedger.ActivePresence + pLedger.Momentum;
        }

        private static Dictionary<long, List<Actor>> BuildResidenceActorIndex(
            HistoricalSchoolAnnualMemberSnapshot<Actor> pMembers)
        {
            var result = new Dictionary<long, List<Actor>>();
            try
            {
                int perSchoolLimit = Math.Max(1, MaxIndexedActorsPerYear /
                    Math.Max(1, CourtSchoolRegistry.All.Count));
                foreach (CourtSchoolDefinition school in CourtSchoolRegistry.All)
                {
                    foreach (Actor actor in pMembers.EnumerateLivingMembers(school.Id,
                                 perSchoolLimit, IsDebateIndexEligible))
                    {
                        City residence = HistoricalAffiliationService.ResidenceCity(actor) ??
                                         actor.city;
                        if (residence?.data == null || residence.isRekt()) continue;
                        if (!result.TryGetValue(residence.data.id, out List<Actor> members))
                        {
                            members = new List<Actor>();
                            result[residence.data.id] = members;
                        }
                        members.Add(actor);
                    }
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school residence actor index failed: " +
                                    error.Message);
            }

            foreach (long cityId in result.Keys.ToArray())
                result[cityId] = LimitResidentActors(result[cityId]);
            return result;
        }

        private static List<Actor> LimitResidentActors(IEnumerable<Actor> pActors)
        {
            Actor[] actors = (pActors ?? Array.Empty<Actor>())
                .Where(IsDebateIndexEligible)
                .OrderBy(p => p.data.id)
                .ToArray();
            var selected = new List<Actor>(MaxActorsPerCity);
            var selectedIds = new HashSet<long>();
            foreach (IGrouping<string, Actor> group in actors.GroupBy(p =>
                         SchoolMembershipService.GetActive(p.data.id)?.SchoolId ?? "",
                         StringComparer.Ordinal))
            {
                Actor first = group.FirstOrDefault();
                if (first == null || string.IsNullOrEmpty(group.Key) ||
                    !selectedIds.Add(first.data.id)) continue;
                selected.Add(first);
                if (selected.Count >= MaxActorsPerCity) return selected;
            }
            foreach (Actor actor in actors)
            {
                if (selected.Count >= MaxActorsPerCity) break;
                if (selectedIds.Add(actor.data.id)) selected.Add(actor);
            }
            return selected;
        }

        private static bool IsDebateIndexEligible(Actor pActor)
        {
            return pActor?.data != null && pActor.isAlive() && !pActor.isRekt() &&
                   !pActor.isBaby() && SchoolMembershipService.GetActive(pActor.data.id) != null &&
                   SchoolLineageService.IsQualifiedTeacher(pActor) &&
                   HistoricalAffiliationService.IsPresentForInfluence(pActor) &&
                   !IsExcludedLifecycle(pActor);
        }

        private static void ApplyCommittedDebate(Actor pFirst, Actor pSecond, City pCity,
            SchoolDebateOutcome pOutcome, string pTopic, int pYear)
        {
            try
            {
                pFirst.addStatusEffect(HistoricalSchoolContent.DebateStatusId, 20f,
                    pColorEffect: false);
                pSecond.addStatusEffect(HistoricalSchoolContent.DebateStatusId, 20f,
                    pColorEffect: false);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school debate status failed: " + error.Message);
            }

            SchoolMembershipService.ApplyReputationDelta(pFirst.data.id,
                HistoricalSchoolDebateRules.ReputationDelta(pOutcome, pFirst: true));
            SchoolMembershipService.ApplyReputationDelta(pSecond.data.id,
                HistoricalSchoolDebateRules.ReputationDelta(pOutcome, pFirst: false));

            string firstName = SafeName(pFirst);
            string secondName = SafeName(pSecond);
            string text = firstName + " and " + secondName + " debated " + pTopic +
                          " (" + pOutcome + ")";
            try
            {
                HistoryWriter.RecordPerson(pFirst.data.id,
                    HistoricalAffiliationService.HomeKingdom(pFirst) ?? pFirst.kingdom,
                    firstName, "school_debate", text, ChronicleCategory.HONOR);
                HistoryWriter.RecordPerson(pSecond.data.id,
                    HistoricalAffiliationService.HomeKingdom(pSecond) ?? pSecond.kingdom,
                    secondName, "school_debate", text, ChronicleCategory.HONOR);
                HistoryWriter.RecordCity(pCity, pCity.kingdom, "school_debate", text);
                HistoricalSchoolContent.AnnounceDebate(pFirst, pSecond, pCity, pOutcome);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school debate history failed: " + error.Message);
            }

            bool firstWon = pOutcome == SchoolDebateOutcome.NarrowFirstWin ||
                            pOutcome == SchoolDebateOutcome.DecisiveFirstWin;
            bool secondWon = pOutcome == SchoolDebateOutcome.NarrowSecondWin ||
                             pOutcome == SchoolDebateOutcome.DecisiveSecondWin;
            if (firstWon) IncrementDebateWins(pFirst);
            if (secondWon) IncrementDebateWins(pSecond);
            CitySchoolSnapshotService.MarkDirty(pCity);
            SchoolMapModeService.DirtyMapIfActive();
        }

        private static bool TryFoundInstitutionAfterDebate(DebateCandidateContext pFirst,
            DebateCandidateContext pSecond, HistoricalSchoolMasterDefinition pFirstDefinition,
            HistoricalSchoolMasterDefinition pSecondDefinition, City pCity, int pYear,
            double pWorldTime)
        {
            // Prefer the first canonical master for deterministic yearly results, then let
            // the second canonical master use the same small budget.  Non-historical teachers
            // never become institution founders through this path.
            DebateCandidateContext founder = HistoricalSchoolDescentService.IsCanonicalMaster(
                pFirst?.Actor) ? pFirst :
                (HistoricalSchoolDescentService.IsCanonicalMaster(pSecond?.Actor)
                    ? pSecond : null);
            if (founder?.Actor?.data == null || pCity?.data == null) return false;
            HistoricalSchoolMasterDefinition definition = founder == pFirst
                ? pFirstDefinition
                : pSecondDefinition;
            HistoricalSchoolMasterDefinition canonical =
                HistoricalSchoolDescentService.DefinitionFor(founder.Actor);
            if (canonical == null || definition == null ||
                !ReferenceEquals(canonical, definition)) definition = canonical;
            return HistoricalSchoolStore.TryFoundInstitution(definition, founder.Actor.data.id,
                pCity.data.id, pYear, pWorldTime);
        }

        private static IEnumerable<City> LivingCities()
        {
            var result = new HistoricalSchoolBoundedCitySelector<City>(MaxCitiesPerYear,
                p => p.data.id, p => p?.data != null && !p.isRekt());
            try
            {
                if (World.world?.kingdoms == null) return result.Ascending();
                foreach (Kingdom kingdom in World.world.kingdoms)
                {
                    if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral()) continue;
                    foreach (City city in kingdom.getCities())
                        result.Consider(city);
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school debate city scan failed: " +
                                    error.Message);
            }
            return result.Ascending();
        }

        private static IEnumerable<string> CityTopics(City pCity)
        {
            var topics = new List<string>();
            try
            {
                int population = Math.Max(1, pCity.getPopulationPeople());
                if (pCity.status != null)
                {
                    if (pCity.status.hungry > 0)
                    {
                        topics.Add(HistoricalDebateTopicId.Livelihood);
                        topics.Add(HistoricalDebateTopicId.Famine);
                    }
                    if (pCity.status.sick > 0)
                    {
                        topics.Add(HistoricalDebateTopicId.Medicine);
                        topics.Add(HistoricalDebateTopicId.Epidemic);
                    }
                    if (pCity.status.hungry >= population / 3)
                        topics.Add(HistoricalDebateTopicId.Commerce);
                }
                if (pCity.kingdom?.hasEnemies() == true)
                {
                    topics.Add(HistoricalDebateTopicId.War);
                    topics.Add(HistoricalDebateTopicId.Defense);
                    topics.Add(HistoricalDebateTopicId.Peace);
                    topics.Add(HistoricalDebateTopicId.Diplomacy);
                }
            }
            catch { }

            // A peaceful, healthy city still has an administrative agenda.  This keeps
            // debates possible without inventing a school or a synthetic participant.
            if (topics.Count == 0) topics.Add(HistoricalDebateTopicId.Order);
            return topics.Distinct(StringComparer.Ordinal);
        }

        private static HistoricalSchoolMasterDefinition DefinitionFor(Actor pActor,
            string pSchoolId)
        {
            HistoricalSchoolMasterDefinition definition =
                HistoricalSchoolDescentService.DefinitionFor(pActor);
            if (definition != null && definition.SchoolId == pSchoolId) return definition;
            return HistoricalSchoolMasterRegistry.All
                .Where(p => p.SchoolId == pSchoolId)
                .OrderBy(p => p.Order)
                .FirstOrDefault();
        }

        private static bool IsExcludedLifecycle(Actor pActor)
        {
            HistoricalSchoolAffiliationSnapshot state =
                HistoricalAffiliationService.Get(pActor?.data?.id ?? -1L);
            if (state == null) return false;
            return state.LifecycleState == HistoricalSchoolLifecycleState.Serving ||
                   state.LifecycleState == HistoricalSchoolLifecycleState.ChoosingDestination ||
                   state.LifecycleState == HistoricalSchoolLifecycleState.Travelling ||
                   state.LifecycleState == HistoricalSchoolLifecycleState.Voyage ||
                   state.LifecycleState == HistoricalSchoolLifecycleState.Dead;
        }

        private static float ProblemMatch(HistoricalSchoolMasterDefinition pDefinition, City pCity)
        {
            if (pDefinition == null) return 0f;
            HashSet<string> cityTopics = new HashSet<string>(CityTopics(pCity),
                StringComparer.Ordinal);
            int matches = pDefinition.DebateTopics.Count(p => cityTopics.Contains(p));
            return pDefinition.DebateTopics.Count == 0
                ? 0f
                : Math.Max(0f, Math.Min(1f, matches / (float)pDefinition.DebateTopics.Count));
        }

        private static float Reputation(Actor pActor, SchoolMembershipRecord pMembership)
        {
            float value = pMembership?.Reputation ?? 0f;
            HistoricalSchoolMasterDefinition definition =
                HistoricalSchoolDescentService.DefinitionFor(pActor);
            if (definition != null) value = Math.Max(value, definition.Abilities.Intelligence);
            return Math.Max(0f, Math.Min(100f, value));
        }

        private static float Learning(Actor pActor)
        {
            float value = SafeStat(pActor, "learning");
            return value > 0f ? value : SafeStat(pActor, "intelligence");
        }

        private static float SafeStat(Actor pActor, string pKey)
        {
            try { return Math.Max(0f, Math.Min(100f, pActor?.stats?[pKey] ?? 0f)); }
            catch { return 0f; }
        }

        private static int DebateWinCount(long pActorId)
        {
            return DebateWins.TryGetValue(pActorId, out int value) ? value : 0;
        }

        private static void IncrementDebateWins(Actor pActor)
        {
            if (pActor?.data == null) return;
            DebateWins.TryGetValue(pActor.data.id, out int value);
            DebateWins[pActor.data.id] = Math.Min(100000, value + 1);
        }

        private static long DebateSeed(long pCityId, int pYear, long pFirstActorId,
            long pSecondActorId, string pTopic)
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                hash = (hash ^ pCityId) * 1099511628211L;
                hash = (hash ^ pYear) * 1099511628211L;
                hash = (hash ^ pFirstActorId) * 1099511628211L;
                hash = (hash ^ pSecondActorId) * 1099511628211L;
                foreach (char value in pTopic ?? "") hash = (hash ^ value) * 1099511628211L;
                return hash;
            }
        }

        private static string SafeName(Actor pActor)
        {
            try { return pActor?.getName() ?? pActor?.data?.name ?? ""; }
            catch { return pActor?.data?.name ?? ""; }
        }

        private sealed class DebateCandidateContext
        {
            public DebateCandidateContext(Actor pActor, SchoolMembershipRecord pMembership,
                HistoricalSchoolDebateCandidate pCandidate)
            {
                Actor = pActor;
                Membership = pMembership;
                Candidate = pCandidate;
            }

            public Actor Actor { get; }
            public SchoolMembershipRecord Membership { get; }
            public HistoricalSchoolDebateCandidate Candidate { get; }
        }
    }
}
