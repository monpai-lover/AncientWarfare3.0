using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    internal sealed class DiplomaticCoalitionPreview
    {
        public bool Available;
        public string Reason = "invalid_coalition_target";
        public long TargetKingdomId = -1L;
        public string TargetKingdomName = "";
        public int RequesterActiveCount;
        public int ResponderActiveCount;
    }

    internal static class DiplomaticCoalitionService
    {
        private const int MaximumWarRows = 32;
        private const int MaximumMaintenanceRows = 8;

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null &&
                                     LineageArchiveManager.Instance
                                         .InitializeSuccessful;

        public static DiplomaticCoalitionPreview Prepare(Kingdom pRequester,
            Kingdom pResponder, Kingdom pTarget)
        {
            var preview = new DiplomaticCoalitionPreview
            {
                TargetKingdomId = pTarget?.id ?? -1L,
                TargetKingdomName = pTarget?.name ?? ""
            };
            if (!Ready || !IsLiveRealm(pRequester) ||
                !IsLiveRealm(pResponder) || !IsLiveRealm(pTarget) ||
                pRequester == pResponder || pRequester == pTarget ||
                pResponder == pTarget)
                return preview;

            int year = SafeYear();
            preview.RequesterActiveCount = CountActiveForMember(
                pRequester.id, year);
            preview.ResponderActiveCount = CountActiveForMember(
                pResponder.id, year);
            bool memberSubject = GetAnySuzerain(pRequester)?.data != null;
            bool responderSubject = GetAnySuzerain(pResponder)?.data != null;
            bool subjectConflict = IsSubjectRelated(pRequester, pTarget) ||
                                   IsSubjectRelated(pResponder, pTarget);
            bool validTarget = DiplomacyActionExpansionRules
                .IsEligibleCoalitionTarget(new CoalitionTargetFacts(
                    distinctRealms: true,
                    targetAlive: !pTarget.isRekt(),
                    targetCivilized: pTarget.isCiv(),
                    subjectConflict: subjectConflict,
                    servingTargetInWar: ServesTargetInWar(
                        pRequester, pTarget) || ServesTargetInWar(
                        pResponder, pTarget),
                    targetHasMandate: MandateService.IsMandateKingdom(pTarget),
                    targetPower: SafePower(pTarget),
                    strongerMemberPower: Math.Max(SafePower(pRequester),
                        SafePower(pResponder))));
            preview.Reason = DiplomacyActionExpansionRules
                .CoalitionUnavailableReason(
                    membersAtWar: SafeEnemy(pRequester, pResponder),
                    requesterSubject: memberSubject,
                    responderSubject: responderSubject,
                    requesterActiveCount: preview.RequesterActiveCount,
                    responderActiveCount: preview.ResponderActiveCount,
                    duplicateTarget: HasActivePairTarget(pRequester.id,
                        pResponder.id, pTarget.id, year),
                    validTarget: validTarget);
            preview.Available = string.IsNullOrEmpty(preview.Reason);
            return preview;
        }

        public static DiplomaticCoalitionPreview PrepareReadOnly(
            Kingdom pRequester, Kingdom pResponder, Kingdom pTarget,
            MandateReport pMandateReport)
        {
            return PrepareReadOnly(pRequester, pResponder, pTarget,
                pMandateReport, out _);
        }

        internal static DiplomaticCoalitionPreview PrepareReadOnly(
            Kingdom pRequester, Kingdom pResponder, Kingdom pTarget,
            MandateReport pMandateReport,
            out AsyncDiplomacySelectionTargetFacts pFacts)
        {
            var preview = new DiplomaticCoalitionPreview
            {
                TargetKingdomId = pTarget?.id ?? -1L,
                TargetKingdomName = pTarget?.name ?? ""
            };
            bool serviceReady = Ready;
            bool requesterAlive = IsLiveRealm(pRequester);
            bool responderAlive = IsLiveRealm(pResponder);
            bool targetAlive = pTarget?.data != null && !pTarget.isRekt();
            bool targetCivilized = pTarget?.data != null && pTarget.isCiv();
            bool targetNeutral = pTarget?.data == null || pTarget.isNeutral();
            bool distinctRealms = pRequester != pResponder &&
                                  pRequester != pTarget &&
                                  pResponder != pTarget;
            bool targetHasMandate = pTarget?.data != null &&
                MandateService.IsMandateKingdomReadOnly(pTarget,
                    pMandateReport);
            float targetPower = SafePower(pTarget);
            float strongerMemberPower = Math.Max(SafePower(pRequester),
                SafePower(pResponder));
            bool requesterAtWar = SafeEnemyReadOnly(pRequester, pTarget);
            bool responderAtWar = SafeEnemyReadOnly(pResponder, pTarget);
            bool membersAtWar = SafeEnemyReadOnly(pRequester, pResponder);
            bool requesterSubject = false;
            bool responderSubject = false;
            bool subjectConflict = false;
            bool servingTargetInWar = false;
            bool duplicateTarget = false;
            int requesterActiveCount = 0;
            int responderActiveCount = 0;
            bool eligible = false;
            if (serviceReady && requesterAlive && responderAlive &&
                targetAlive && targetCivilized && !targetNeutral &&
                distinctRealms)
            {
                int year = SafeYear();
                requesterActiveCount = CountActiveForMember(pRequester.id,
                    year);
                responderActiveCount = CountActiveForMember(pResponder.id,
                    year);
                requesterSubject = GetAnySuzerain(pRequester)?.data != null;
                responderSubject = GetAnySuzerain(pResponder)?.data != null;
                subjectConflict = IsSubjectRelated(pRequester, pTarget) ||
                                  IsSubjectRelated(pResponder, pTarget);
                servingTargetInWar = ServesTargetInWar(pRequester, pTarget) ||
                                     ServesTargetInWar(pResponder, pTarget);
                duplicateTarget = HasActivePairTarget(pRequester.id,
                    pResponder.id, pTarget.id, year);
                bool validTarget = DiplomacyActionExpansionRules
                    .IsEligibleCoalitionTarget(new CoalitionTargetFacts(
                        distinctRealms, targetAlive, targetCivilized,
                        subjectConflict, servingTargetInWar,
                        targetHasMandate, targetPower,
                        strongerMemberPower));
                preview.Reason = DiplomacyActionExpansionRules
                    .CoalitionUnavailableReason(membersAtWar,
                        requesterSubject, responderSubject,
                        requesterActiveCount, responderActiveCount,
                        duplicateTarget, validTarget);
                eligible = string.IsNullOrEmpty(preview.Reason);
            }
            preview.RequesterActiveCount = requesterActiveCount;
            preview.ResponderActiveCount = responderActiveCount;
            preview.Available = eligible;
            pFacts = new AsyncDiplomacySelectionTargetFacts(
                pTarget?.data == null ? -1L : pTarget.id, targetPower,
                targetHasMandate, requesterAtWar, responderAtWar,
                targetAlive, targetCivilized, targetNeutral,
                subjectConflict, servingTargetInWar, duplicateTarget,
                membersAtWar, requesterSubject, responderSubject,
                requesterActiveCount, responderActiveCount,
                strongerMemberPower, eligible, serviceReady,
                requesterAlive, responderAlive, distinctRealms);
            return preview;
        }

        private static bool SafeEnemyReadOnly(Kingdom pFirst,
            Kingdom pSecond)
        {
            try { return pFirst?.isEnemy(pSecond) == true; }
            catch { return false; }
        }

        public static bool TryCommit(DiplomacyProposal pProposal,
            out string pReason)
        {
            pReason = "invalid_coalition_target";
            if (!Ready || pProposal == null ||
                pProposal.Type != DiplomacyProposalType.Coalition)
                return false;
            if (HasCoalitionForProposal(pProposal.ProposalId))
            {
                pReason = "";
                return true;
            }

            Kingdom requester = FindKingdom(pProposal.RequesterKingdomId);
            Kingdom responder = FindKingdom(pProposal.ResponderKingdomId);
            Kingdom target = FindKingdom(pProposal.TargetKingdomId);
            DiplomaticCoalitionPreview preview = Prepare(requester,
                responder, target);
            if (!preview.Available)
            {
                pReason = preview.Reason;
                return false;
            }

            DiplomacyKingdomPair pair = DiplomacyConversationRules
                .NormalizePair(requester.id, responder.id);
            try
            {
                int year = SafeYear();
                long coalitionId = TableIdAllocator.Next(DB,
                    DiplomaticCoalitionTableItem.GetTableName(),
                    "COALITION_ID");
                long modifierId = TableIdAllocator.Next(DB,
                    DiplomaticRelationModifierTableItem.GetTableName(),
                    "MODIFIER_ID");
                using SQLiteTransaction transaction = DB.BeginTransaction();
                InsertCoalition(transaction, coalitionId, pair, target.id,
                    year, pProposal.ProposalId);
                if (!DiplomaticRelationModifierService.Upsert(transaction,
                        modifierId, requester.id, responder.id, "coalition",
                        coalitionId, 15, year, year +
                        DiplomacyActionExpansionRules.CoalitionYears))
                    throw new InvalidOperationException(
                        "coalition relation modifier write failed");
                transaction.Commit();
                pReason = "";
                return true;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Coalition commit failed: " +
                                    exception.Message);
                pReason = "coalition_write_failed";
                return false;
            }
        }

        public static void OnWarStarted(War pWar)
        {
            if (!Ready || pWar?.data == null || pWar.hasEnded()) return;
            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();
            if (!IsLiveRealm(attacker) || !IsLiveRealm(defender)) return;
            int year = SafeYear();
            foreach (CoalitionRow row in ReadWarRows(attacker.id,
                         defender.id, year))
                TryJoinWar(row, pWar, year);
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!Ready || pKingdom?.data == null) return;
            int year = SafeYear();
            foreach (CoalitionRow row in ReadMemberRows(pKingdom.id))
            {
                Kingdom first = FindKingdom(row.MemberAId);
                Kingdom second = FindKingdom(row.MemberBId);
                Kingdom target = FindKingdom(row.TargetKingdomId);
                if (!IsLiveRealm(first) || !IsLiveRealm(second) ||
                    !IsLiveRealm(target) || year > row.EndYear ||
                    GetAnySuzerain(first)?.data != null ||
                    GetAnySuzerain(second)?.data != null ||
                    IsSubjectRelated(first, target) ||
                    IsSubjectRelated(second, target))
                    Close(row.CoalitionId);
            }
        }

        public static void OnKingdomDestroyed(Kingdom pKingdom)
        {
            if (!Ready || pKingdom?.data == null) return;
            try
            {
                using var command = new SQLiteCommand(
                    "UPDATE DiplomaticCoalition SET STATUS=1,END_TIME=@time " +
                    "WHERE STATUS=0 AND (MEMBER_A_ID=@id OR MEMBER_B_ID=@id " +
                    "OR TARGET_KINGDOM_ID=@id)", DB);
                command.Parameters.AddWithValue("@time", LineageService.CurTime());
                command.Parameters.AddWithValue("@id", pKingdom.id);
                command.ExecuteNonQuery();
                using var modifier = new SQLiteCommand(
                    "UPDATE DiplomaticRelationModifier SET ACTIVE=0 WHERE " +
                    "SOURCE_TYPE='coalition' AND SOURCE_ID IN (SELECT " +
                    "COALITION_ID FROM DiplomaticCoalition WHERE " +
                    "MEMBER_A_ID=@id OR MEMBER_B_ID=@id OR " +
                    "TARGET_KINGDOM_ID=@id)", DB);
                modifier.Parameters.AddWithValue("@id", pKingdom.id);
                modifier.ExecuteNonQuery();
                DiplomaticRelationModifierService.ClearRuntime();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Coalition extinction cleanup failed: " +
                                    exception.Message);
            }
        }

        private static void TryJoinWar(CoalitionRow pRow, War pWar,
            int pYear)
        {
            Kingdom target = FindKingdom(pRow.TargetKingdomId);
            Kingdom first = FindKingdom(pRow.MemberAId);
            Kingdom second = FindKingdom(pRow.MemberBId);
            if (!IsLiveRealm(target) || !IsLiveRealm(first) ||
                !IsLiveRealm(second))
            {
                Close(pRow.CoalitionId);
                return;
            }

            bool firstInWar = IsWarParticipant(pWar, first);
            bool secondInWar = IsWarParticipant(pWar, second);
            if (firstInWar == secondInWar) return;
            Kingdom member = firstInWar ? first : second;
            Kingdom partner = firstInWar ? second : first;
            bool subjectConflict = GetAnySuzerain(member)?.data != null ||
                                   GetAnySuzerain(partner)?.data != null ||
                                   IsSubjectRelated(member, target) ||
                                   IsSubjectRelated(partner, target);
            CoalitionWarJoinSide side = DiplomacyActionExpansionRules
                .ResolveCoalitionWarJoin(active: true,
                    currentYear: pYear, endYear: pRow.EndYear,
                    targetIsAttacker: SafeAttacker(pWar, target),
                    targetIsDefender: SafeDefender(pWar, target),
                    memberIsAttacker: SafeAttacker(pWar, member),
                    memberIsDefender: SafeDefender(pWar, member),
                    partnerAlreadyInWar: IsWarParticipant(pWar, partner),
                    subjectConflict: subjectConflict);
            if (side == CoalitionWarJoinSide.None)
            {
                if (subjectConflict || pYear > pRow.EndYear)
                    Close(pRow.CoalitionId);
                return;
            }

            try
            {
                using (WarParticipantEntrySourceScope.Open(pWar, partner,
                           WarParticipantEntrySourceKind.AllianceCall,
                           member))
                {
                    if (side == CoalitionWarJoinSide.Attackers)
                        pWar.joinAttackers(partner);
                    else
                        pWar.joinDefenders(partner);
                }
                bool joined = side == CoalitionWarJoinSide.Attackers
                    ? SafeAttacker(pWar, partner)
                    : SafeDefender(pWar, partner);
                if (!joined) return;
                using var command = new SQLiteCommand(
                    "UPDATE DiplomaticCoalition SET JOINED_WAR_ID=@war " +
                    "WHERE COALITION_ID=@id AND STATUS=0", DB);
                command.Parameters.AddWithValue("@war", pWar.data.id);
                command.Parameters.AddWithValue("@id", pRow.CoalitionId);
                command.ExecuteNonQuery();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Coalition war support failed: " +
                                    exception.Message);
            }
        }

        private static List<CoalitionRow> ReadWarRows(long pAttackerId,
            long pDefenderId, int pYear)
        {
            var rows = new List<CoalitionRow>();
            try
            {
                using var command = new SQLiteCommand(
                    "SELECT COALITION_ID,MEMBER_A_ID,MEMBER_B_ID," +
                    "TARGET_KINGDOM_ID,END_YEAR FROM DiplomaticCoalition " +
                    "WHERE TARGET_KINGDOM_ID IN (@attacker,@defender) AND " +
                    "STATUS=0 AND END_YEAR>=@year " +
                    "ORDER BY END_YEAR,COALITION_ID LIMIT @limit", DB);
                command.Parameters.AddWithValue("@attacker", pAttackerId);
                command.Parameters.AddWithValue("@defender", pDefenderId);
                command.Parameters.AddWithValue("@year", pYear);
                command.Parameters.AddWithValue("@limit", MaximumWarRows);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) rows.Add(ReadRow(reader));
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Coalition war query failed: " +
                                    exception.Message);
            }
            return rows;
        }

        private static List<CoalitionRow> ReadMemberRows(long pKingdomId)
        {
            var rows = new List<CoalitionRow>();
            try
            {
                using var command = new SQLiteCommand(
                    "SELECT COALITION_ID,MEMBER_A_ID,MEMBER_B_ID," +
                    "TARGET_KINGDOM_ID,END_YEAR FROM DiplomaticCoalition " +
                    "WHERE STATUS=0 AND (MEMBER_A_ID=@id OR " +
                    "MEMBER_B_ID=@id) " +
                    "ORDER BY END_YEAR,COALITION_ID LIMIT @limit", DB);
                command.Parameters.AddWithValue("@id", pKingdomId);
                command.Parameters.AddWithValue("@limit",
                    MaximumMaintenanceRows);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) rows.Add(ReadRow(reader));
            }
            catch { }
            return rows;
        }

        private static CoalitionRow ReadRow(SQLiteDataReader pReader)
        {
            return new CoalitionRow
            {
                CoalitionId = pReader.GetInt64(0),
                MemberAId = pReader.GetInt64(1),
                MemberBId = pReader.GetInt64(2),
                TargetKingdomId = pReader.GetInt64(3),
                EndYear = pReader.GetInt32(4)
            };
        }

        private static int CountActiveForMember(long pKingdomId, int pYear)
        {
            using var command = new SQLiteCommand(
                "SELECT COUNT(*) FROM DiplomaticCoalition WHERE STATUS=0 " +
                "AND END_YEAR>=@year AND (MEMBER_A_ID=@id OR " +
                "MEMBER_B_ID=@id)", DB);
            command.Parameters.AddWithValue("@year", pYear);
            command.Parameters.AddWithValue("@id", pKingdomId);
            return Convert.ToInt32(command.ExecuteScalar());
        }

        private static bool HasActivePairTarget(long pKingdomA,
            long pKingdomB, long pTargetId, int pYear)
        {
            DiplomacyKingdomPair pair = DiplomacyConversationRules
                .NormalizePair(pKingdomA, pKingdomB);
            using var command = new SQLiteCommand(
                "SELECT 1 FROM DiplomaticCoalition WHERE MEMBER_A_ID=@a " +
                "AND MEMBER_B_ID=@b AND TARGET_KINGDOM_ID=@target AND " +
                "STATUS=0 AND END_YEAR>=@year LIMIT 1", DB);
            command.Parameters.AddWithValue("@a", pair.FirstKingdomId);
            command.Parameters.AddWithValue("@b", pair.SecondKingdomId);
            command.Parameters.AddWithValue("@target", pTargetId);
            command.Parameters.AddWithValue("@year", pYear);
            return command.ExecuteScalar() != null;
        }

        private static bool HasCoalitionForProposal(long pProposalId)
        {
            using var command = new SQLiteCommand(
                "SELECT 1 FROM DiplomaticCoalition WHERE " +
                "SOURCE_PROPOSAL_ID=@proposal LIMIT 1", DB);
            command.Parameters.AddWithValue("@proposal", pProposalId);
            return command.ExecuteScalar() != null;
        }

        private static void Close(long pCoalitionId)
        {
            try
            {
                using var command = new SQLiteCommand(
                    "UPDATE DiplomaticCoalition SET STATUS=1,END_TIME=@time " +
                    "WHERE COALITION_ID=@id AND STATUS=0", DB);
                command.Parameters.AddWithValue("@time", LineageService.CurTime());
                command.Parameters.AddWithValue("@id", pCoalitionId);
                command.ExecuteNonQuery();
                DiplomaticRelationModifierService.DeactivateSource(
                    "coalition", pCoalitionId);
            }
            catch { }
        }

        private static void InsertCoalition(SQLiteTransaction pTransaction,
            long pCoalitionId, DiplomacyKingdomPair pPair,
            long pTargetKingdomId, int pYear, long pProposalId)
        {
            using var command = new SQLiteCommand(DB)
            {
                Transaction = pTransaction,
                CommandText =
                    "INSERT INTO DiplomaticCoalition " +
                    "(COALITION_ID,MEMBER_A_ID,MEMBER_B_ID," +
                    "TARGET_KINGDOM_ID,START_YEAR,END_YEAR,START_TIME," +
                    "END_TIME,STATUS,SOURCE_PROPOSAL_ID,JOINED_WAR_ID) " +
                    "VALUES (@id,@a,@b,@target,@start,@end,@time,-1,0," +
                    "@proposal,-1)"
            };
            command.Parameters.AddWithValue("@id", pCoalitionId);
            command.Parameters.AddWithValue("@a", pPair.FirstKingdomId);
            command.Parameters.AddWithValue("@b", pPair.SecondKingdomId);
            command.Parameters.AddWithValue("@target", pTargetKingdomId);
            command.Parameters.AddWithValue("@start", pYear);
            command.Parameters.AddWithValue("@end", pYear +
                DiplomacyActionExpansionRules.CoalitionYears);
            command.Parameters.AddWithValue("@time", LineageService.CurTime());
            command.Parameters.AddWithValue("@proposal", pProposalId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "coalition ledger insert failed");
        }

        private static bool IsSubjectRelated(Kingdom pFirst,
            Kingdom pSecond)
        {
            return HasSuzerainInChain(pFirst, pSecond) ||
                   HasSuzerainInChain(pSecond, pFirst);
        }

        private static bool HasSuzerainInChain(Kingdom pSubject,
            Kingdom pCandidate)
        {
            Kingdom current = pSubject;
            var visited = new HashSet<long>();
            for (int i = 0; i < 16 && current?.data != null &&
                            visited.Add(current.id); i++)
            {
                current = GetAnySuzerain(current);
                if (current == pCandidate) return true;
            }
            return false;
        }

        private static Kingdom GetAnySuzerain(Kingdom pKingdom)
        {
            return VassalService.GetSuzerain(pKingdom) ??
                   VassalService.GetTributarySuzerain(pKingdom);
        }

        private static bool ServesTargetInWar(Kingdom pMember,
            Kingdom pTarget)
        {
            try
            {
                foreach (War war in pMember.getWars())
                    if (war?.data != null && !war.hasEnded() &&
                        IsWarParticipant(war, pTarget) &&
                        SafeAttacker(war, pMember) ==
                        SafeAttacker(war, pTarget)) return true;
            }
            catch { }
            return false;
        }

        private static bool IsWarParticipant(War pWar, Kingdom pKingdom)
        {
            return SafeAttacker(pWar, pKingdom) ||
                   SafeDefender(pWar, pKingdom);
        }

        private static bool SafeAttacker(War pWar, Kingdom pKingdom)
        {
            try { return pWar?.isAttacker(pKingdom) == true; }
            catch { return false; }
        }

        private static bool SafeDefender(War pWar, Kingdom pKingdom)
        {
            try { return pWar?.isDefender(pKingdom) == true; }
            catch { return false; }
        }

        private static bool SafeEnemy(Kingdom pFirst, Kingdom pSecond)
        {
            try { return pFirst.isEnemy(pSecond); }
            catch { return true; }
        }

        private static float SafePower(Kingdom pKingdom)
        {
            try { return Math.Max(0f, pKingdom?.power ?? 0f); }
            catch { return 0f; }
        }

        private static bool IsLiveRealm(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   pKingdom.isCiv() && !pKingdom.isNeutral();
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

        private sealed class CoalitionRow
        {
            public long CoalitionId;
            public long MemberAId;
            public long MemberBId;
            public long TargetKingdomId;
            public int EndYear;
        }
    }
}
