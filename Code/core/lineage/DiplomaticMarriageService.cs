using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.historyapi;
using AncientWarfare3.ui;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    internal sealed class DiplomaticMarriagePreview
    {
        public bool Available;
        public string Reason = "invalid";
        public long RequesterActorId = -1;
        public long ResponderActorId = -1;
        public string RequesterActorName = "";
        public string ResponderActorName = "";
        public bool DirectRoyalMarriage;
    }

    internal sealed class DiplomaticMarriageCandidate
    {
        public Actor Actor;
        public RoyalMarriageCandidateFacts Facts;
        public RoyalMarriageKinship Kinship;
        public int GenerationDistance;
        public int Merit;

        public long ActorId => Actor?.data?.id ?? -1L;
        public bool DirectRoyalKinship => DiplomacyActionExpansionRules
            .IsDirectMarriageKinship(Kinship);
    }

    internal sealed class DiplomaticMarriageCandidatePools
    {
        public string Reason = "invalid";
        public readonly List<DiplomaticMarriageCandidate>
            RequesterCandidates = new();
        public readonly List<DiplomaticMarriageCandidate>
            ResponderCandidates = new();
    }

    internal static class DiplomaticMarriageService
    {
        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null &&
                                     LineageArchiveManager.Instance
                                         .InitializeSuccessful;

        public static DiplomaticMarriagePreview Prepare(Kingdom pRequester,
            Kingdom pResponder)
        {
            var preview = new DiplomaticMarriagePreview();
            DiplomaticMarriageCandidatePools pools = BuildCandidatePools(
                pRequester, pResponder);
            preview.Reason = pools.Reason;
            bool found = TryFindBestPair(pools.RequesterCandidates,
                pools.ResponderCandidates, out DiplomaticMarriageCandidate
                    bestRequester, out DiplomaticMarriageCandidate bestResponder);
            if (!found) return preview;
            preview.Available = true;
            preview.Reason = "";
            preview.RequesterActorId = bestRequester.ActorId;
            preview.ResponderActorId = bestResponder.ActorId;
            preview.RequesterActorName = bestRequester.Actor.getName() ?? "";
            preview.ResponderActorName = bestResponder.Actor.getName() ?? "";
            preview.DirectRoyalMarriage =
                bestRequester.DirectRoyalKinship &&
                bestResponder.DirectRoyalKinship;
            return preview;
        }

        internal static DiplomaticMarriageCandidatePools BuildCandidatePools(
            Kingdom pRequester, Kingdom pResponder)
        {
            var pools = new DiplomaticMarriageCandidatePools();
            if (!Ready || pRequester?.data == null ||
                pResponder?.data == null || pRequester == pResponder ||
                pRequester.isRekt() || pResponder.isRekt())
                return pools;

            bool atWar = SafeEnemy(pRequester, pResponder);
            bool bothHaveKings = pRequester.hasKing() &&
                                 pResponder.hasKing() &&
                                 pRequester.king?.data != null &&
                                 pResponder.king?.data != null;
            bool activeMarriage = HasActiveRealmMarriage(
                pRequester.id, pResponder.id);
            if (atWar || !bothHaveKings || activeMarriage)
            {
                pools.Reason = DiplomacyActionExpansionRules
                    .MarriageUnavailableReason(atWar, bothHaveKings,
                        activeMarriage, hasCandidatePair: false);
                return pools;
            }

            long requesterActorLineage = LineageQuery.GetActorLineageId(
                pRequester.king.data.id);
            long responderActorLineage = LineageQuery.GetActorLineageId(
                pResponder.king.data.id);
            long requesterKingdomLineage = ReadKingdomLegitimateLineage(
                pRequester);
            long responderKingdomLineage = ReadKingdomLegitimateLineage(
                pResponder);
            long requesterLineage = DiplomacyActionExpansionRules
                .ResolveRoyalMarriageLineage(requesterActorLineage,
                    requesterKingdomLineage);
            long responderLineage = DiplomacyActionExpansionRules
                .ResolveRoyalMarriageLineage(responderActorLineage,
                    responderKingdomLineage);
            if (requesterLineage < 0 || responderLineage < 0)
            {
                pools.Reason = "missing_royal_house";
                return pools;
            }

            var query = new DiplomaticMarriageQuery(DB);
            pools.RequesterCandidates.AddRange(ResolveCandidates(
                pRequester, requesterLineage,
                query.ReadCandidateIds(requesterLineage, pRequester.id,
                    DiplomacyActionExpansionRules
                        .MaximumRoyalArchiveIdsScannedPerRealm)));
            pools.ResponderCandidates.AddRange(ResolveCandidates(
                pResponder, responderLineage,
                query.ReadCandidateIds(responderLineage, pResponder.id,
                    DiplomacyActionExpansionRules
                        .MaximumRoyalArchiveIdsScannedPerRealm)));
            if (pools.RequesterCandidates.Count == 0)
                pools.Reason = "no_requester_royal_candidate";
            else if (pools.ResponderCandidates.Count == 0)
                pools.Reason = "no_responder_royal_candidate";
            else if (!HasCompatiblePair(pools.RequesterCandidates,
                         pools.ResponderCandidates))
                pools.Reason = "no_compatible_royal_pair";
            else
                pools.Reason = "";
            return pools;
        }

        private static long ReadKingdomLegitimateLineage(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return -1L;
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID,
                out long lineageId, -1L);
            return lineageId;
        }

        internal static DiplomaticMarriagePreview PrepareSelection(
            Kingdom pRequester, Kingdom pResponder, long pRequesterActorId,
            long pResponderActorId)
        {
            DiplomaticMarriageCandidatePools pools = BuildCandidatePools(
                pRequester, pResponder);
            var preview = new DiplomaticMarriagePreview
            {
                Reason = pools.Reason
            };
            DiplomaticMarriageCandidate requester = FindCandidate(
                pools.RequesterCandidates, pRequesterActorId);
            DiplomaticMarriageCandidate responder = FindCandidate(
                pools.ResponderCandidates, pResponderActorId);
            if (requester == null || responder == null ||
                !CanPair(requester, responder))
            {
                if (string.IsNullOrEmpty(preview.Reason))
                    preview.Reason = "marriage_candidate_stale";
                return preview;
            }
            preview.Available = true;
            preview.Reason = "";
            preview.RequesterActorId = requester.ActorId;
            preview.ResponderActorId = responder.ActorId;
            preview.RequesterActorName = requester.Actor.getName() ?? "";
            preview.ResponderActorName = responder.Actor.getName() ?? "";
            preview.DirectRoyalMarriage = requester.DirectRoyalKinship &&
                                          responder.DirectRoyalKinship;
            return preview;
        }

        internal static bool CanPair(DiplomaticMarriageCandidate pRequester,
            DiplomaticMarriageCandidate pResponder)
        {
            return pRequester?.Actor?.data != null &&
                   pResponder?.Actor?.data != null &&
                   DiplomacyActionExpansionRules.CanPairMarriage(
                       pRequester.Facts, pResponder.Facts,
                       SafeRelated(pRequester.Actor, pResponder.Actor));
        }

        internal static bool CanPairInDirection(
            DiplomaticMarriageCandidate pRequester,
            DiplomaticMarriageCandidate pResponder,
            RoyalMarriageDirection pDirection)
        {
            return pRequester?.Actor?.data != null &&
                   pResponder?.Actor?.data != null &&
                   DiplomacyActionExpansionRules.CanPairMarriageInDirection(
                       pRequester.Facts, pResponder.Facts,
                       SafeRelated(pRequester.Actor, pResponder.Actor),
                       pDirection);
        }

        public static bool TryCommit(DiplomacyProposal pProposal,
            out string pReason)
        {
            pReason = "marriage_candidate_stale";
            if (!Ready || pProposal == null ||
                pProposal.Type != DiplomacyProposalType.RoyalMarriage)
                return false;
            if (HasMarriageForProposal(pProposal.ProposalId))
            {
                pReason = "";
                return true;
            }

            Kingdom requester = FindKingdom(pProposal.RequesterKingdomId);
            Kingdom responder = FindKingdom(pProposal.ResponderKingdomId);
            DiplomaticMarriagePreview selected = PrepareSelection(requester,
                responder, pProposal.RequesterActorId,
                pProposal.ResponderActorId);
            if (!selected.Available)
            {
                pReason = selected.Reason;
                return false;
            }
            Actor requesterActor = FindActor(pProposal.RequesterActorId);
            Actor responderActor = FindActor(pProposal.ResponderActorId);
            if (requesterActor == requester.king ||
                responderActor == responder.king ||
                requesterActor?.isKing() == true ||
                responderActor?.isKing() == true)
                return false;
            if (!IsStillEligible(requesterActor, requester) ||
                !IsStillEligible(responderActor, responder) ||
                requesterActor.isSexMale() == responderActor.isSexMale() ||
                SafeRelated(requesterActor, responderActor))
                return false;

            Actor requesterPreviousPartner = requesterActor.lover;
            Actor responderPreviousPartner = responderActor.lover;
            try
            {
                DiplomacyKingdomPair pair =
                    DiplomacyConversationRules.NormalizePair(
                        requester.id, responder.id);
                bool requesterFirst = pair.FirstKingdomId == requester.id;
                long actorA = requesterFirst
                    ? requesterActor.data.id
                    : responderActor.data.id;
                long actorB = requesterFirst
                    ? responderActor.data.id
                    : requesterActor.data.id;
                long marriageId = TableIdAllocator.Next(DB,
                    DiplomaticMarriageTableItem.GetTableName(),
                    "MARRIAGE_ID");
                long modifierId = TableIdAllocator.Next(DB,
                    DiplomaticRelationModifierTableItem.GetTableName(),
                    "MODIFIER_ID");
                int year = SafeYear();
                DetachPartnership(requesterActor);
                DetachPartnership(responderActor);
                requesterActor.becomeLoversWith(responderActor);
                using SQLiteTransaction transaction = DB.BeginTransaction();
                InsertMarriage(transaction, marriageId, pair, actorA, actorB,
                    year, pProposal.ProposalId);
                if (!DiplomaticRelationModifierService.Upsert(transaction,
                        modifierId, requester.id, responder.id,
                        "royal_marriage", marriageId, 20, year,
                        int.MaxValue))
                    throw new InvalidOperationException(
                        "royal marriage relation modifier write failed");
                transaction.Commit();
                AW3HistoryEventPublisher.PublishDiplomacy(marriageId,
                    "DiplomaticMarriageStart", "marriage_started",
                    pair.FirstKingdomId, pair.SecondKingdomId,
                    LineageService.CurTime(), year, "", "active", "");
                pReason = "";
            }
            catch (Exception exception)
            {
                ClearMarriagePair(requesterActor, responderActor);
                RestorePartnership(requesterActor,
                    requesterPreviousPartner);
                RestorePartnership(responderActor,
                    responderPreviousPartner);
                ModClass.LogWarning("Royal marriage commit failed: " +
                                    exception.Message);
                pReason = "marriage_write_failed";
                return false;
            }
            try
            {
                RecordHistory(requester, responder, requesterActor,
                    responderActor);
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Royal marriage history failed: " +
                                    exception.Message);
            }
            return true;
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!Ready || pKingdom?.data == null) return;
            try
            {
                pKingdom.data.get(LineageKeys.DIPLOMACY_MARRIAGE_CURSOR,
                    out long cursor, -1L);
                using var command = new SQLiteCommand(
                    "SELECT MARRIAGE_ID,ACTOR_A_ID,ACTOR_B_ID FROM (" +
                    "SELECT MARRIAGE_ID,ACTOR_A_ID,ACTOR_B_ID FROM " +
                    "DiplomaticMarriage WHERE KINGDOM_A_ID=@id AND STATUS=0 " +
                    "AND MARRIAGE_ID>@cursor " +
                    "UNION ALL SELECT MARRIAGE_ID,ACTOR_A_ID,ACTOR_B_ID FROM " +
                    "DiplomaticMarriage WHERE KINGDOM_B_ID=@id AND STATUS=0 " +
                    "AND MARRIAGE_ID>@cursor) " +
                    "ORDER BY MARRIAGE_ID LIMIT @limit", DB);
                command.Parameters.AddWithValue("@id", pKingdom.id);
                command.Parameters.AddWithValue("@cursor", cursor);
                command.Parameters.AddWithValue("@limit",
                    DiplomacyActionExpansionRules
                        .MaximumMarriageMaintenanceRows);
                using SQLiteDataReader reader = command.ExecuteReader();
                var stale = new List<long>();
                var inspected = new List<long>();
                while (reader.Read())
                {
                    long marriageId = reader.GetInt64(0);
                    inspected.Add(marriageId);
                    Actor first = FindActor(reader.GetInt64(1));
                    Actor second = FindActor(reader.GetInt64(2));
                    if (first?.data == null || second?.data == null ||
                        !first.isAlive() || !second.isAlive() ||
                        first.lover != second || second.lover != first)
                        stale.Add(marriageId);
                }
                reader.Close();
                for (int i = 0; i < stale.Count; i++)
                    CloseStaleMarriage(stale[i]);
                pKingdom.data.set(LineageKeys.DIPLOMACY_MARRIAGE_CURSOR,
                    DiplomacyActionExpansionRules
                        .NextMarriageMaintenanceCursor(inspected));
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Royal marriage maintenance failed: " +
                                    exception.Message);
            }
        }

        private static void CloseStaleMarriage(long pMarriageId)
        {
            long firstKingdomId = -1L;
            long secondKingdomId = -1L;
            double endTime = LineageService.CurTime();
            try
            {
                using var read = new SQLiteCommand(
                    "SELECT KINGDOM_A_ID,KINGDOM_B_ID FROM " +
                    "DiplomaticMarriage WHERE MARRIAGE_ID=@id LIMIT 1", DB);
                read.Parameters.AddWithValue("@id", pMarriageId);
                using SQLiteDataReader reader = read.ExecuteReader();
                if (reader.Read())
                {
                    firstKingdomId = reader.IsDBNull(0) ? -1L : reader.GetInt64(0);
                    secondKingdomId = reader.IsDBNull(1) ? -1L : reader.GetInt64(1);
                }
            }
            catch { }
            using var command = new SQLiteCommand(
                "UPDATE DiplomaticMarriage SET STATUS=1,END_TIME=@time " +
                "WHERE MARRIAGE_ID=@id AND STATUS=0", DB);
            command.Parameters.AddWithValue("@time", endTime);
            command.Parameters.AddWithValue("@id", pMarriageId);
            if (command.ExecuteNonQuery() == 1 && firstKingdomId >= 0L &&
                secondKingdomId >= 0L)
                AW3HistoryEventPublisher.PublishDiplomacy(pMarriageId,
                    "DiplomaticMarriageEnd", "marriage_ended",
                    firstKingdomId, secondKingdomId, endTime, SafeYear(),
                    "", "ended", "");
            DiplomaticRelationModifierService.DeactivateSource(
                "royal_marriage", pMarriageId);
        }

        private static void InsertMarriage(SQLiteTransaction pTransaction,
            long pMarriageId, DiplomacyKingdomPair pPair,
            long pActorAId, long pActorBId, int pYear,
            long pProposalId)
        {
            using var command = new SQLiteCommand(DB)
            {
                Transaction = pTransaction,
                CommandText =
                    "INSERT INTO DiplomaticMarriage " +
                    "(MARRIAGE_ID,KINGDOM_A_ID,KINGDOM_B_ID,ACTOR_A_ID," +
                    "ACTOR_B_ID,START_YEAR,START_TIME,END_TIME,STATUS," +
                    "SOURCE_PROPOSAL_ID) VALUES " +
                    "(@id,@a,@b,@actor_a,@actor_b,@year,@time,-1,0," +
                    "@proposal)"
            };
            command.Parameters.AddWithValue("@id", pMarriageId);
            command.Parameters.AddWithValue("@a", pPair.FirstKingdomId);
            command.Parameters.AddWithValue("@b", pPair.SecondKingdomId);
            command.Parameters.AddWithValue("@actor_a", pActorAId);
            command.Parameters.AddWithValue("@actor_b", pActorBId);
            command.Parameters.AddWithValue("@year", pYear);
            command.Parameters.AddWithValue("@time", LineageService.CurTime());
            command.Parameters.AddWithValue("@proposal", pProposalId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "royal marriage ledger insert failed");
        }

        private static void ClearMarriagePair(Actor pRequesterActor,
            Actor pResponderActor)
        {
            if (pRequesterActor?.lover == pResponderActor)
                pRequesterActor.setLover(null);
            if (pResponderActor?.lover == pRequesterActor)
                pResponderActor.setLover(null);
        }

        private static List<DiplomaticMarriageCandidate> ResolveCandidates(
            Kingdom pKingdom, long pLineageId,
            IReadOnlyList<long> pActorIds)
        {
            var result = new List<DiplomaticMarriageCandidate>(Math.Min(
                pActorIds.Count, DiplomacyActionExpansionRules
                    .MaximumRoyalCandidatesPerRealm));
            for (int i = 0; i < pActorIds.Count; i++)
            {
                if (result.Count >= DiplomacyActionExpansionRules
                        .MaximumRoyalCandidatesPerRealm)
                    break;
                Actor actor = FindActor(pActorIds[i]);
                if (!IsStillEligible(actor, pKingdom)) continue;
                if (actor == pKingdom.king || actor.isKing()) continue;
                actor.data.get(LineageKeys.LINEAGE_ID,
                    out long liveLineage, -1L);
                if (liveLineage != pLineageId) continue;
                RoyalMarriageKinship kinship = DiplomacyActionExpansionRules
                    .ClassifyMarriageKinship(actor.data.id,
                        pKingdom.king.data.id, actor.data.parent_id_1,
                        actor.data.parent_id_2);
                bool marriageAvailable = DiplomacyActionExpansionRules
                    .IsAvailableForDynasticMarriage(actor.hasLover(),
                        hasActiveDynasticMarriage: false);
                var facts = new RoyalMarriageCandidateFacts(
                    actor.data.id, actor.isAlive(), actor.isAdult(),
                    actor.isBreedingAge(), marriageAvailable,
                    royalLineage: true, actor.isSexMale(),
                    reigningRuler: false);
                if (!DiplomacyActionExpansionRules
                        .IsEligibleMarriageCandidate(facts))
                    continue;
                result.Add(new DiplomaticMarriageCandidate
                {
                    Actor = actor,
                    Facts = facts,
                    Kinship = kinship,
                    GenerationDistance = DiplomacyActionExpansionRules
                        .MarriageGenerationDistance(kinship),
                    Merit = Math.Max(0, actor.diplomacy) +
                            Math.Max(0, actor.intelligence) +
                            Math.Max(0, actor.stewardship) +
                            Math.Max(0, actor.warfare)
                });
            }
            return result;
        }

        private static bool HasCompatiblePair(
            IReadOnlyList<DiplomaticMarriageCandidate> pRequesterCandidates,
            IReadOnlyList<DiplomaticMarriageCandidate> pResponderCandidates)
        {
            for (int i = 0; i < pRequesterCandidates.Count; i++)
            for (int j = 0; j < pResponderCandidates.Count; j++)
                if (CanPair(pRequesterCandidates[i], pResponderCandidates[j]))
                    return true;
            return false;
        }

        private static bool TryFindBestPair(
            IReadOnlyList<DiplomaticMarriageCandidate> pRequesterCandidates,
            IReadOnlyList<DiplomaticMarriageCandidate> pResponderCandidates,
            out DiplomaticMarriageCandidate pRequester,
            out DiplomaticMarriageCandidate pResponder)
        {
            pRequester = null;
            pResponder = null;
            RoyalMarriagePairScore bestScore = default;
            bool found = false;
            for (int i = 0; i < pRequesterCandidates.Count; i++)
            for (int j = 0; j < pResponderCandidates.Count; j++)
            {
                DiplomaticMarriageCandidate requester =
                    pRequesterCandidates[i];
                DiplomaticMarriageCandidate responder =
                    pResponderCandidates[j];
                if (!CanPair(requester, responder)) continue;
                var score = new RoyalMarriagePairScore(
                    requester.ActorId, responder.ActorId,
                    (requester.DirectRoyalKinship ? 1 : 0) +
                    (responder.DirectRoyalKinship ? 1 : 0),
                    requester.GenerationDistance +
                    responder.GenerationDistance,
                    Math.Abs(requester.Actor.getAge() -
                             responder.Actor.getAge()),
                    requester.Merit + responder.Merit);
                if (found && DiplomacyActionExpansionRules
                        .CompareMarriagePair(score, bestScore) >= 0)
                    continue;
                found = true;
                bestScore = score;
                pRequester = requester;
                pResponder = responder;
            }
            return found;
        }

        private static DiplomaticMarriageCandidate FindCandidate(
            IReadOnlyList<DiplomaticMarriageCandidate> pCandidates,
            long pActorId)
        {
            if (pCandidates == null || pActorId < 0) return null;
            for (int i = 0; i < pCandidates.Count; i++)
                if (pCandidates[i]?.ActorId == pActorId)
                    return pCandidates[i];
            return null;
        }

        private static bool IsStillEligible(Actor pActor, Kingdom pKingdom)
        {
            return pActor?.data != null && pKingdom?.data != null &&
                   pActor.isAlive() && !pActor.isRekt() &&
                   pActor.kingdom == pKingdom &&
                   pActor.isAdult() && pActor.isBreedingAge();
        }

        private static void DetachPartnership(Actor pActor)
        {
            if (pActor == null) return;
            Actor partner = pActor.lover;
            pActor.setLover(null);
            if (partner?.lover == pActor) partner.setLover(null);
        }

        private static void RestorePartnership(Actor pActor,
            Actor pPreviousPartner)
        {
            if (pActor?.data == null || pPreviousPartner?.data == null ||
                pActor.lover != null || pPreviousPartner.lover != null)
                return;
            pActor.setLover(pPreviousPartner);
            pPreviousPartner.setLover(pActor);
        }

        private static bool HasActiveRealmMarriage(long pKingdomA,
            long pKingdomB)
        {
            DiplomacyKingdomPair pair =
                DiplomacyConversationRules.NormalizePair(
                    pKingdomA, pKingdomB);
            using var command = new SQLiteCommand(
                "SELECT 1 FROM DiplomaticMarriage WHERE KINGDOM_A_ID=@a " +
                "AND KINGDOM_B_ID=@b AND STATUS=0 AND END_TIME<0 LIMIT 1", DB);
            command.Parameters.AddWithValue("@a", pair.FirstKingdomId);
            command.Parameters.AddWithValue("@b", pair.SecondKingdomId);
            return command.ExecuteScalar() != null;
        }

        private static bool HasMarriageForProposal(long pProposalId)
        {
            using var command = new SQLiteCommand(
                "SELECT 1 FROM DiplomaticMarriage WHERE " +
                "SOURCE_PROPOSAL_ID=@proposal LIMIT 1", DB);
            command.Parameters.AddWithValue("@proposal", pProposalId);
            return command.ExecuteScalar() != null;
        }

        private static bool SafeRelated(Actor pFirst, Actor pSecond)
        {
            try { return pFirst.isRelatedTo(pSecond) ||
                         pSecond.isRelatedTo(pFirst); }
            catch { return true; }
        }

        private static void RecordHistory(Kingdom pRequester,
            Kingdom pResponder, Actor pRequesterActor,
            Actor pResponderActor)
        {
            string firstName = pRequesterActor.getName() ?? "";
            string secondName = pResponderActor.getName() ?? "";
            string suffix = AW_L10n.Text(
                "aw_hist_royal_marriage_suffix", "");
            string firstText = firstName + AW_L10n.Text(
                "aw_hist_royal_marriage_mid", " married ") + secondName +
                suffix;
            string secondText = secondName + AW_L10n.Text(
                "aw_hist_royal_marriage_mid", " married ") + firstName +
                suffix;
            HistoryWriter.RecordPerson(pRequesterActor.data.id,
                pRequester, firstName, PersonEvent.ROYAL_MARRIAGE,
                firstText, ChronicleCategory.BOND,
                HistoryTarget.Actor(pResponderActor));
            HistoryWriter.RecordPerson(pResponderActor.data.id,
                pResponder, secondName, PersonEvent.ROYAL_MARRIAGE,
                secondText, ChronicleCategory.BOND,
                HistoryTarget.Actor(pRequesterActor));
            HistoryWriter.RecordKingdom(pRequester,
                KingdomEvent.ROYAL_MARRIAGE, firstText,
                HistoryTarget.Kingdom(pResponder));
            HistoryWriter.RecordKingdom(pResponder,
                KingdomEvent.ROYAL_MARRIAGE, secondText,
                HistoryTarget.Kingdom(pRequester));
        }

        private static bool SafeEnemy(Kingdom pFirst, Kingdom pSecond)
        {
            try { return pFirst.isEnemy(pSecond); }
            catch { return true; }
        }

        private static Actor FindActor(long pActorId)
        {
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static int SafeYear()
        {
            try { return Date.getCurrentYear(); }
            catch { return 0; }
        }

    }
}
