using System;
using System.Collections.Generic;
using AncientWarfare3.core.court;

namespace AncientWarfare3.core.lineage
{
    internal sealed class InheritanceCandidateSelection
    {
        public Actor Actor;
        public InheritanceCandidateFacts Facts;
        public InheritanceLaw Law;
        public int Score = int.MinValue;
        public int SupporterCount;
        public IReadOnlyDictionary<long, long> SupportTargetBySupporterId;
    }

    internal sealed class InheritanceFactionSupport
    {
        public readonly Dictionary<InheritanceLaw,
            InheritanceCandidateSelection> Selections = new();
        public readonly Dictionary<long, int> SupportByActorId = new();
        public readonly Dictionary<long, long>
            MilitarySupportTargetByActorId = new();
        public readonly Dictionary<long, long>
            CivilSupportTargetByActorId = new();
        public long DesignatedHeirId = -1L;
        public Actor LeaderActor;
        public Actor RunnerUpActor;
        public int LeaderSupport;
        public int RunnerUpSupport;
        public int DesignatedHeirSupport;
        public SuccessionClaimantKind LeaderKind =
            SuccessionClaimantKind.None;
        public string LeaderMode = SuccessionMode.NONE;
    }

    internal static class InheritanceCandidateService
    {
        public static List<Actor> CollectRoyalCandidates(Kingdom pKingdom,
            Actor pReferenceKing = null, long pReferenceKingId = -1L)
        {
            var result = new List<Actor>(
                InheritanceCandidateRules.MaximumArchiveIds);
            var seen = new HashSet<long>();
            if (pKingdom?.data == null) return result;

            Actor king = pReferenceKing ?? pKingdom.king;
            long referenceKingId = king?.data?.id ?? pReferenceKingId;
            AddIndexedCloseKin(referenceKingId, king, result, seen);

            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID,
                out long registeredHeirId, -1L);
            AddLive(ResolveActor(registeredHeirId), result, seen);

            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID,
                out long lineageId, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID,
                out long shiId, -1L);
            if (lineageId < 0 && king?.data != null)
                king.data.get(LineageKeys.LINEAGE_ID, out lineageId, -1L);
            if (shiId < 0 && king?.data != null)
                king.data.get(LineageKeys.SHI_ID, out shiId, -1L);

            if (!AtLimit(result) && lineageId >= 0)
                AddArchived(LineageQuery.GetLivingLineageMemberIds(lineageId,
                    InheritanceCandidateRules.MaximumArchiveIds), result, seen);
            if (!AtLimit(result) && shiId >= 0)
                AddArchived(LineageQuery.GetLivingShiMemberIds(shiId,
                    InheritanceCandidateRules.MaximumArchiveIds), result, seen);

            if (!AtLimit(result)) AddRoyalClanMembers(pKingdom, result, seen);
            return result;
        }

        private static void AddIndexedCloseKin(long pReferenceKingId,
            Actor pReferenceKing, List<Actor> pResult, HashSet<long> pSeen)
        {
            if (pReferenceKingId < 0 || AtLimit(pResult)) return;

            bool foundIndexedChild = AddIndexedChildren(pReferenceKingId,
                pResult, pSeen);
            if (!foundIndexedChild && pReferenceKing?.data != null)
            {
                // FamilyEdge is authoritative for AW3 births. This fallback only
                // covers the short creation window before the archive transaction.
                try
                {
                    foreach (Actor child in pReferenceKing.getChildren(false))
                    {
                        AddLive(child, pResult, pSeen);
                        if (AtLimit(pResult)) return;
                    }
                }
                catch { }
            }

            long fatherId = LineageQuery.GetFatherId(pReferenceKingId);
            AddSiblingBranches(fatherId, pReferenceKingId, pResult, pSeen);
            if (AtLimit(pResult) || fatherId < 0) return;

            long grandfatherId = LineageQuery.GetFatherId(fatherId);
            AddSiblingBranches(grandfatherId, fatherId, pResult, pSeen);
        }

        private static void AddSiblingBranches(long pParentId,
            long pExcludedChildId, List<Actor> pResult, HashSet<long> pSeen)
        {
            if (pParentId < 0 || AtLimit(pResult)) return;
            IReadOnlyList<long> siblingIds = LineageQuery.GetChildIds(pParentId);
            for (int i = 0; i < siblingIds.Count && !AtLimit(pResult); i++)
            {
                long siblingId = siblingIds[i];
                if (siblingId < 0 || siblingId == pExcludedChildId) continue;
                AddLive(ResolveActor(siblingId), pResult, pSeen);
                AddIndexedChildren(siblingId, pResult, pSeen);
            }
        }

        private static bool AddIndexedChildren(long pParentId,
            List<Actor> pResult, HashSet<long> pSeen)
        {
            if (pParentId < 0 || AtLimit(pResult)) return false;
            IReadOnlyList<long> childIds = LineageQuery.GetChildIds(pParentId);
            bool found = childIds.Count > 0;
            for (int i = 0; i < childIds.Count && !AtLimit(pResult); i++)
                AddLive(ResolveActor(childIds[i]), pResult, pSeen);
            return found;
        }

        public static int CountAdultRoyalCandidates(Kingdom pKingdom,
            Actor pReferenceKing = null)
        {
            int count = 0;
            Actor king = pReferenceKing ?? pKingdom?.king;
            KinshipContext kinship = BuildKinshipContext(king);
            foreach (Actor actor in CollectRoyalCandidates(pKingdom,
                         pReferenceKing))
            {
                InheritanceCandidateFacts facts = BuildFacts(actor, pKingdom,
                    king, groupSupport: 0, pKinship: kinship);
                if (InheritanceCandidateRules.IsEligible(facts,
                        InheritanceLaw.MilitaryAcclaim))
                    count++;
            }
            return count;
        }

        public static bool HasAdultRoyalCandidate(Kingdom pKingdom,
            Actor pReferenceKing = null)
        {
            if (pKingdom?.data == null) return false;
            Actor king = pReferenceKing ?? pKingdom.king;
            Actor registered = HeirService.PeekRegisteredHeir(pKingdom);

            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID,
                out long lineageId, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID,
                out long shiId, -1L);
            if (lineageId < 0 && king?.data != null)
                king.data.get(LineageKeys.LINEAGE_ID, out lineageId, -1L);
            if (shiId < 0 && king?.data != null)
                king.data.get(LineageKeys.SHI_ID, out shiId, -1L);

            if (IsFastAdultRoyalCandidate(registered, pKingdom, lineageId,
                    shiId)) return true;

            int scanned = 0;
            try
            {
                long clanId = pKingdom.data.royal_clan_id;
                Clan clan = clanId < 0 ? null : World.world?.clans?.get(clanId);
                if (clan?.units != null)
                {
                    foreach (Actor actor in clan.units)
                    {
                        if (scanned++ >=
                            InheritanceCandidateRules.MaximumArchiveIds) break;
                        if (IsFastAdultRoyalCandidate(actor, pKingdom,
                                lineageId, shiId)) return true;
                    }
                }
            }
            catch { }

            IReadOnlyList<long> archiveIds = lineageId >= 0
                ? LineageQuery.GetLivingLineageMemberIds(lineageId,
                    InheritanceCandidateRules.MaximumArchiveIds)
                : shiId >= 0
                    ? LineageQuery.GetLivingShiMemberIds(shiId,
                        InheritanceCandidateRules.MaximumArchiveIds)
                    : null;
            if (archiveIds == null) return false;
            for (int i = 0; i < archiveIds.Count; i++)
                if (IsFastAdultRoyalCandidate(ResolveActor(archiveIds[i]),
                        pKingdom, lineageId, shiId)) return true;
            return false;
        }

        private static bool IsFastAdultRoyalCandidate(Actor pActor,
            Kingdom pKingdom, long pLegitimateLineage, long pLegitimateShi)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            pActor.data.get(LineageKeys.LINEAGE_ID,
                out long actorLineage, -1L);
            pActor.data.get(LineageKeys.SHI_ID, out long actorShi, -1L);
            return InheritanceCandidateRules.IsFastAdultRoyalCandidate(
                pActor.isAlive() && !pActor.isRekt(), pActor.isSexMale(),
                pActor.isAdult(), pActor.isKing(), SlaveService.IsSlave(pActor),
                pActor.hasTrait("madness"), pActor.kingdom == pKingdom,
                pLegitimateLineage >= 0 && actorLineage == pLegitimateLineage,
                pLegitimateShi >= 0 && actorShi == pLegitimateShi);
        }

        public static bool IsEligibleActor(Actor pActor, Kingdom pKingdom,
            InheritanceLaw pLaw, Actor pReferenceKing = null)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            Actor king = pReferenceKing ?? pKingdom.king;
            return InheritanceCandidateRules.IsEligible(BuildFacts(pActor,
                pKingdom, king, groupSupport: 0,
                pKinship: BuildKinshipContext(king)), pLaw);
        }

        public static InheritanceCandidateSelection SelectCandidate(
            Kingdom pKingdom, InheritanceLaw pLaw,
            Actor pReferenceKing = null)
        {
            if (pKingdom?.data == null) return null;
            Actor king = pReferenceKing ?? pKingdom.king;
            KinshipContext kinship = BuildKinshipContext(king);
            if (pLaw == InheritanceLaw.Primogeniture)
            {
                Actor hereditary = HeirService.PreviewPrimogenitureCandidate(
                    pKingdom, king);
                InheritanceCandidateFacts hereditaryFacts = BuildFacts(
                    hereditary, pKingdom, king, groupSupport: 0,
                    pKinship: kinship);
                if (!InheritanceCandidateRules.IsEligible(hereditaryFacts,
                        pLaw)) return null;
                return new InheritanceCandidateSelection
                {
                    Actor = hereditary,
                    Facts = hereditaryFacts,
                    Law = pLaw,
                    Score = InheritanceCandidateRules.Score(hereditaryFacts,
                        pLaw),
                    SupporterCount = 0
                };
            }
            List<InheritanceCandidateSelection> finalists =
                CollectSupportedFinalists(pKingdom, pLaw, king);
            return finalists.Count > 0 ? finalists[0] : null;
        }

        public static List<InheritanceCandidateSelection> CollectFinalists(
            Kingdom pKingdom, InheritanceLaw pLaw,
            Actor pReferenceKing = null)
        {
            if (pKingdom?.data == null)
                return new List<InheritanceCandidateSelection>();
            Actor king = pReferenceKing ?? pKingdom.king;
            if (pLaw != InheritanceLaw.Primogeniture)
                return CollectSupportedFinalists(pKingdom, pLaw, king);

            KinshipContext kinship = BuildKinshipContext(king);
            List<Actor> actors = CollectRoyalCandidates(pKingdom, king);
            var actorsById = new Dictionary<long, Actor>();
            var facts = new List<InheritanceCandidateFacts>(actors.Count);
            foreach (Actor actor in actors)
            {
                InheritanceCandidateFacts candidate = BuildFacts(actor,
                    pKingdom, king, groupSupport: 0, pKinship: kinship);
                if (!InheritanceCandidateRules.IsEligible(candidate, pLaw))
                    continue;
                actorsById[candidate.ActorId] = actor;
                facts.Add(candidate);
            }

            InheritanceCandidateFacts[] finalists =
                InheritanceCandidateRules.SelectFinalists(facts, pLaw);
            var result = new List<InheritanceCandidateSelection>(
                finalists.Length);
            for (var index = 0; index < finalists.Length; index++)
            {
                InheritanceCandidateFacts candidate = finalists[index];
                if (!actorsById.TryGetValue(candidate.ActorId,
                        out Actor actor)) continue;
                result.Add(new InheritanceCandidateSelection
                {
                    Actor = actor,
                    Facts = candidate,
                    Law = pLaw,
                    Score = InheritanceCandidateRules.Score(candidate, pLaw),
                    SupporterCount = 0
                });
            }
            return result;
        }

        private static List<InheritanceCandidateSelection>
            CollectSupportedFinalists(Kingdom pKingdom, InheritanceLaw pLaw,
                Actor pReferenceKing)
        {
            Actor king = pReferenceKing ?? pKingdom.king;
            KinshipContext kinship = BuildKinshipContext(king);
            List<Actor> actors = CollectRoyalCandidates(pKingdom, king);
            var actorsById = new Dictionary<long, Actor>();
            var initialFacts = new List<InheritanceCandidateFacts>(actors.Count);
            foreach (Actor actor in actors)
            {
                InheritanceCandidateFacts facts = BuildFacts(actor, pKingdom,
                    king, groupSupport: 0, pKinship: kinship);
                if (!InheritanceCandidateRules.IsEligible(facts, pLaw)) continue;
                actorsById[facts.ActorId] = actor;
                initialFacts.Add(facts);
            }

            InheritanceCandidateFacts[] finalists =
                InheritanceCandidateRules.SelectFinalists(initialFacts, pLaw);
            if (finalists.Length == 0)
                return new List<InheritanceCandidateSelection>();

            var support = new Dictionary<long, int>();
            var supporterCount = new Dictionary<long, int>();
            var supportTargets = new Dictionary<long, long>();
            if (pLaw == InheritanceLaw.MilitaryAcclaim)
                AddMilitarySupport(pKingdom, finalists, support,
                    supporterCount, supportTargets);
            else
                AddCivilSupport(pKingdom, finalists, support,
                    supporterCount, supportTargets);

            var finalFacts = new List<InheritanceCandidateFacts>(finalists.Length);
            foreach (InheritanceCandidateFacts facts in finalists)
            {
                support.TryGetValue(facts.ActorId, out int weight);
                finalFacts.Add(BuildFacts(actorsById[facts.ActorId], pKingdom,
                    king, weight, kinship));
            }
            InheritanceCandidateFacts[] ranked =
                InheritanceCandidateRules.SelectFinalists(finalFacts, pLaw);
            var result = new List<InheritanceCandidateSelection>(
                ranked.Length);
            for (var index = 0; index < ranked.Length; index++)
            {
                InheritanceCandidateFacts selected = ranked[index];
                if (!actorsById.TryGetValue(selected.ActorId,
                        out Actor selectedActor)) continue;
                supporterCount.TryGetValue(selected.ActorId,
                    out int supporters);
                result.Add(new InheritanceCandidateSelection
                {
                    Actor = selectedActor,
                    Facts = selected,
                    Law = pLaw,
                    Score = InheritanceCandidateRules.Score(selected, pLaw),
                    SupporterCount = supporters,
                    SupportTargetBySupporterId = supportTargets
                });
            }
            return result;
        }

        public static InheritanceFactionSupport ResolveFactionSupport(
            Kingdom pKingdom, Actor pReferenceKing = null,
            Actor pDesignatedHeir = null)
        {
            var result = new InheritanceFactionSupport();
            if (pKingdom?.data == null) return result;

            Actor referenceKing = pReferenceKing ?? pKingdom.king;
            Actor designatedHeir = pDesignatedHeir ??
                                   HeirService.PeekStoredHeirForMinimap(
                                       pKingdom);
            result.DesignatedHeirId = designatedHeir?.data?.id ?? -1L;

            pKingdom.data.get(LineageKeys.INHERITANCE_MILITARY_UNLOCKED,
                out bool militaryUnlocked, false);
            pKingdom.data.get(LineageKeys.INHERITANCE_CIVIL_UNLOCKED,
                out bool civilUnlocked, false);
            InheritanceCandidateSelection orthodox = SelectCandidate(
                pKingdom, InheritanceLaw.Primogeniture, referenceKing);
            InheritanceCandidateSelection military = militaryUnlocked
                ? SelectCandidate(pKingdom, InheritanceLaw.MilitaryAcclaim,
                    referenceKing)
                : null;
            InheritanceCandidateSelection civil = civilUnlocked
                ? SelectCandidate(pKingdom, InheritanceLaw.CivilAcclaim,
                    referenceKing)
                : null;
            CopySupportTargets(military?.SupportTargetBySupporterId,
                result.MilitarySupportTargetByActorId);
            CopySupportTargets(civil?.SupportTargetBySupporterId,
                result.CivilSupportTargetByActorId);
            result.Selections[InheritanceLaw.Primogeniture] = orthodox;
            result.Selections[InheritanceLaw.MilitaryAcclaim] = military;
            result.Selections[InheritanceLaw.CivilAcclaim] = civil;

            pKingdom.data.get(LineageKeys.INHERITANCE_SCORE_PRIMOGENITURE,
                out int orthodoxInfluence, 0);
            pKingdom.data.get(LineageKeys.INHERITANCE_SCORE_MILITARY,
                out int militaryInfluence, 0);
            pKingdom.data.get(LineageKeys.INHERITANCE_SCORE_CIVIL,
                out int civilInfluence, 0);
            long orthodoxId = orthodox?.Actor?.data?.id ?? -1L;
            long militaryId = military?.Actor?.data?.id ?? -1L;
            long civilId = civil?.Actor?.data?.id ?? -1L;

            var actors = new Dictionary<long, Actor>();
            AddFactionActor(designatedHeir, actors);
            AddFactionActor(orthodox?.Actor, actors);
            AddFactionActor(military?.Actor, actors);
            AddFactionActor(civil?.Actor, actors);
            foreach (KeyValuePair<long, Actor> pair in actors)
            {
                result.SupportByActorId[pair.Key] =
                    InheritanceLawRules.AggregateCandidateSupport(pair.Key,
                        result.DesignatedHeirId, orthodoxId,
                        orthodoxInfluence, militaryId, militaryInfluence,
                        civilId, civilInfluence);
            }

            var ranking = new List<KeyValuePair<long, int>>(
                result.SupportByActorId);
            ranking.Sort((left, right) =>
            {
                int supportOrder = right.Value.CompareTo(left.Value);
                if (supportOrder != 0) return supportOrder;
                bool leftIsHeir = left.Key == result.DesignatedHeirId;
                bool rightIsHeir = right.Key == result.DesignatedHeirId;
                if (leftIsHeir != rightIsHeir)
                    return leftIsHeir ? -1 : 1;
                return left.Key.CompareTo(right.Key);
            });
            if (ranking.Count > 0)
            {
                KeyValuePair<long, int> leader = ranking[0];
                result.LeaderActor = actors[leader.Key];
                result.LeaderSupport = leader.Value;
                ResolveLeaderBacking(leader.Key, orthodoxId,
                    orthodoxInfluence, militaryId, militaryInfluence,
                    civilId, civilInfluence, out result.LeaderKind,
                    out result.LeaderMode);
            }
            if (ranking.Count > 1)
            {
                KeyValuePair<long, int> runnerUp = ranking[1];
                result.RunnerUpActor = actors[runnerUp.Key];
                result.RunnerUpSupport = runnerUp.Value;
            }
            if (result.DesignatedHeirId >= 0 &&
                result.SupportByActorId.TryGetValue(
                    result.DesignatedHeirId, out int heirSupport))
                result.DesignatedHeirSupport = heirSupport;
            return result;
        }

        private static void AddFactionActor(Actor pActor,
            IDictionary<long, Actor> pActors)
        {
            if (pActor?.data == null || pActors == null) return;
            pActors[pActor.data.id] = pActor;
        }

        private static void CopySupportTargets(
            IReadOnlyDictionary<long, long> pSource,
            IDictionary<long, long> pDestination)
        {
            if (pSource == null || pDestination == null) return;
            foreach (KeyValuePair<long, long> pair in pSource)
                pDestination[pair.Key] = pair.Value;
        }

        private static void ResolveLeaderBacking(long pLeaderId,
            long pOrthodoxId, int pOrthodoxInfluence,
            long pMilitaryId, int pMilitaryInfluence,
            long pCivilId, int pCivilInfluence,
            out SuccessionClaimantKind pKind, out string pMode)
        {
            pKind = SuccessionClaimantKind.FirstCollateral;
            pMode = SuccessionMode.COLLATERAL_RESTORE;
            int strongest = pLeaderId == pOrthodoxId
                ? pOrthodoxInfluence
                : int.MinValue;
            if (pLeaderId == pMilitaryId && pMilitaryInfluence > strongest)
            {
                strongest = pMilitaryInfluence;
                pKind = SuccessionClaimantKind.MilitaryDesignate;
                pMode = SuccessionMode.MILITARY_ACCLAIM;
            }
            if (pLeaderId == pCivilId && pCivilInfluence > strongest)
            {
                pKind = SuccessionClaimantKind.CivilDesignate;
                pMode = SuccessionMode.CIVIL_ACCLAIM;
            }
        }

        private static void AddMilitarySupport(Kingdom pKingdom,
            IReadOnlyList<InheritanceCandidateFacts> pFinalists,
            IDictionary<long, int> pSupport,
            IDictionary<long, int> pSupporterCount,
            IDictionary<long, long> pSupportTargets)
        {
            List<GeneralReadModelEntry> generals =
                GeneralService.GetActiveGeneralsForReadModel(pKingdom,
                    pAllowUnitFallback: false);
            int limit = Math.Min(InheritanceCandidateRules.MaximumArchiveIds,
                generals.Count);
            for (int i = 0; i < limit; i++)
            {
                GeneralReadModelEntry general = generals[i];
                long supporterId = general.Actor?.data?.id ?? -1L;
                long selectedId = SelectSupportTarget(pFinalists,
                    InheritanceLaw.MilitaryAcclaim,
                    supporterId);
                if (selectedId < 0) continue;
                AddSupport(selectedId, 2 + Math.Min(8,
                    Math.Max(0, general.Merit) / 10), pSupport,
                    pSupporterCount, supporterId, pSupportTargets);
            }
        }

        private static void AddCivilSupport(Kingdom pKingdom,
            IReadOnlyList<InheritanceCandidateFacts> pFinalists,
            IDictionary<long, int> pSupport,
            IDictionary<long, int> pSupporterCount,
            IDictionary<long, long> pSupportTargets)
        {
            AddCivilCityLeaderSupport(pKingdom, pFinalists, pSupport,
                pSupporterCount, pSupportTargets);
            List<CourtOfficerView> officers = CourtService.GetActiveOfficers(
                pKingdom, InheritanceCandidateRules.MaximumOfficerSupporters);
            int limit = Math.Min(
                InheritanceCandidateRules.MaximumOfficerSupporters,
                officers.Count);
            for (int i = 0; i < limit; i++)
            {
                CourtOfficerView officer = officers[i];
                long selectedId = SelectSupportTarget(pFinalists,
                    InheritanceLaw.CivilAcclaim, officer.actor_id);
                if (selectedId < 0) continue;
                AddSupport(selectedId, 1 + Math.Min(7,
                    Math.Max(0, (int)Math.Round(officer.influence)) / 10),
                    pSupport, pSupporterCount, officer.actor_id,
                    pSupportTargets);
            }
        }

        private static void AddCivilCityLeaderSupport(Kingdom pKingdom,
            IReadOnlyList<InheritanceCandidateFacts> pFinalists,
            IDictionary<long, int> pSupport,
            IDictionary<long, int> pSupporterCount,
            IDictionary<long, long> pSupportTargets)
        {
            int inspected = 0;
            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    if (inspected++ >=
                        InheritanceCandidateRules.MaximumOfficerSupporters)
                        break;
                    long supporterId = city?.leader?.data?.id ?? -1L;
                    if (supporterId < 0) continue;
                    long selectedId = SelectSupportTarget(pFinalists,
                        InheritanceLaw.CivilAcclaim, supporterId);
                    if (selectedId < 0) continue;
                    AddSupport(selectedId, 8, pSupport, pSupporterCount,
                        supporterId, pSupportTargets);
                }
            }
            catch { }
        }

        private static long SelectSupportTarget(
            IReadOnlyList<InheritanceCandidateFacts> pFinalists,
            InheritanceLaw pLaw, long pSupporterId)
        {
            long selectedId = -1L;
            int selectedScore = int.MinValue;
            for (int i = 0; i < pFinalists.Count; i++)
            {
                InheritanceCandidateFacts candidate = pFinalists[i];
                int affinity = StableAffinity(pSupporterId,
                    candidate.ActorId);
                int score = InheritanceCandidateRules.Score(candidate,
                    pLaw) + affinity;
                if (score < selectedScore ||
                    (score == selectedScore && selectedId >= 0 &&
                     candidate.ActorId >= selectedId))
                    continue;
                selectedScore = score;
                selectedId = candidate.ActorId;
            }
            return selectedId;
        }

        private static int StableAffinity(long pSupporterId,
            long pCandidateId)
        {
            unchecked
            {
                long mixed = pSupporterId * 397L ^ pCandidateId * 17L;
                int value = (int)(mixed % 11L);
                return value < 0 ? -value : value;
            }
        }

        private static void AddSupport(long pActorId, int pWeight,
            IDictionary<long, int> pSupport,
            IDictionary<long, int> pSupporterCount, long pSupporterId,
            IDictionary<long, long> pSupportTargets)
        {
            pSupport.TryGetValue(pActorId, out int current);
            pSupport[pActorId] = current + Math.Max(0, pWeight);
            pSupporterCount.TryGetValue(pActorId, out int count);
            pSupporterCount[pActorId] = count + 1;
            if (pSupporterId >= 0 && pSupportTargets != null)
                pSupportTargets[pSupporterId] = pActorId;
        }

        private static InheritanceCandidateFacts BuildFacts(Actor pActor,
            Kingdom pKingdom, Actor pReferenceKing, int groupSupport,
            KinshipContext pKinship)
        {
            if (pActor?.data == null)
                return default;
            int distance = 8;
            int legitimacy = 0;
            if (pReferenceKing?.data != null)
            {
                if (TryResolveKinship(pActor.data.id, pKinship,
                        out int kingDepth, out int actorDepth))
                {
                    distance = kingDepth + actorDepth;
                    legitimacy = kingDepth == 0 ? 20 : 10;
                }
            }
            bool royal = IsRoyal(pActor, pKingdom, pReferenceKing,
                ancestorDistance: distance);
            bool general = GeneralService.IsGeneral(pActor);
            pActor.data.get(LineageKeys.BIRTH_LEGITIMACY,
                out bool legitimateBirth, true);
            return new InheritanceCandidateFacts(
                pActor.data.id, pActor.isAlive() && !pActor.isRekt(),
                pActor.isSexMale(), pActor.isAdult(), royal,
                pActor.isKing(), SlaveService.IsSlave(pActor),
                pActor.hasTrait("madness"), pActor.kingdom == pKingdom,
                distance, legitimacy,
                SafeStat(pActor, "warfare"), SafeStat(pActor, "damage"),
                SafeStat(pActor, "speed"), general ? 1 : 0,
                SafeStat(pActor, "stewardship"),
                SafeStat(pActor, "intelligence"),
                SafeStat(pActor, "diplomacy"),
                OfficialCareerStateService.ReadRankFast(pActor),
                (int)Math.Round(
                    OfficialCareerStateService.ReadMeritFast(pActor)),
                evaluationGrade: 0, groupSupport: groupSupport,
                legitimateBirth: legitimateBirth);
        }

        private static KinshipContext BuildKinshipContext(Actor pReference)
        {
            var context = new KinshipContext();
            long current = pReference?.data?.id ?? -1L;
            for (int depth = 0; depth <= 96 && current >= 0; depth++)
            {
                if (context.ReferenceDepths.ContainsKey(current)) break;
                context.ReferenceDepths[current] = depth;
                long father = CachedFatherId(current, context.FatherByActorId);
                if (father < 0 || father == current) break;
                current = father;
            }
            return context;
        }

        private static bool TryResolveKinship(long pCandidateId,
            KinshipContext pContext, out int pReferenceDepth,
            out int pCandidateDepth)
        {
            pReferenceDepth = -1;
            pCandidateDepth = -1;
            if (pCandidateId < 0 || pContext == null) return false;
            var visited = new HashSet<long>();
            long current = pCandidateId;
            for (int depth = 0; depth <= 96 && current >= 0; depth++)
            {
                if (pContext.ReferenceDepths.TryGetValue(current,
                        out pReferenceDepth))
                {
                    pCandidateDepth = depth;
                    return true;
                }
                if (!visited.Add(current)) break;
                long father = CachedFatherId(current,
                    pContext.FatherByActorId);
                if (father < 0 || father == current) break;
                current = father;
            }
            pReferenceDepth = -1;
            return false;
        }

        private static long CachedFatherId(long pActorId,
            IDictionary<long, long> pCache)
        {
            if (pActorId < 0 || pCache == null) return -1L;
            if (pCache.TryGetValue(pActorId, out long cached)) return cached;
            long father;
            try { father = LineageQuery.GetFatherId(pActorId); }
            catch { father = -1L; }
            pCache[pActorId] = father;
            return father;
        }

        private static bool IsRoyal(Actor pActor, Kingdom pKingdom,
            Actor pReferenceKing, int ancestorDistance)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID,
                out long legitimateLineage, -1L);
            pActor.data.get(LineageKeys.LINEAGE_ID, out long actorLineage,
                -1L);
            if (legitimateLineage >= 0 && actorLineage == legitimateLineage)
                return true;
            return pReferenceKing?.data != null && ancestorDistance < 8;
        }

        private static int SafeStat(Actor pActor, string pKey)
        {
            try { return Math.Max(0, (int)Math.Round(pActor.stats[pKey])); }
            catch { return 0; }
        }

        private static void AddArchived(IReadOnlyList<long> pIds,
            List<Actor> pResult, HashSet<long> pSeen)
        {
            if (pIds == null) return;
            int limit = Math.Min(pIds.Count,
                InheritanceCandidateRules.MaximumArchiveIds);
            for (int i = 0; i < limit && !AtLimit(pResult); i++)
                AddLive(ResolveActor(pIds[i]), pResult, pSeen);
        }

        private static void AddRoyalClanMembers(Kingdom pKingdom,
            List<Actor> pResult, HashSet<long> pSeen)
        {
            try
            {
                long clanId = pKingdom.data.royal_clan_id;
                if (clanId < 0) return;
                Clan clan = World.world?.clans?.get(clanId);
                if (clan?.units == null) return;
                foreach (Actor actor in clan.units)
                {
                    AddLive(actor, pResult, pSeen);
                    if (AtLimit(pResult)) break;
                }
            }
            catch { }
        }

        private static void AddLive(Actor pActor, List<Actor> pResult,
            HashSet<long> pSeen)
        {
            if (AtLimit(pResult) || pActor?.data == null ||
                !pSeen.Add(pActor.data.id))
                return;
            pResult.Add(pActor);
        }

        private static Actor ResolveActor(long pActorId)
        {
            return pActorId < 0 ? null : World.world?.units?.get(pActorId);
        }

        private static bool AtLimit(ICollection<Actor> pActors)
        {
            return pActors.Count >=
                   InheritanceCandidateRules.MaximumLiveResolutions;
        }

        private sealed class KinshipContext
        {
            public readonly Dictionary<long, long> FatherByActorId =
                new Dictionary<long, long>();
            public readonly Dictionary<long, int> ReferenceDepths =
                new Dictionary<long, int>();
        }
    }
}
