using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    internal static class RoyalClaimService
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;
        private static readonly HashSet<long> ActiveClaimantIds = new HashSet<long>();
        private static bool _activeClaimantsLoaded;

        internal sealed class RoyalClaimInfo
        {
            public long claim_id = -1;
            public long claimant_actor_id = -1;
            public string claimant_name = "";
            public long original_kingdom_id = -1;
            public string original_kingdom_name = "";
            public int claim_strength;
        }

        internal struct ClaimRow
        {
            public long claimId;
            public long claimantId;
            public string claimantName;
            public long originalKingdomId;
            public string originalKingdomName;
            public string originalKingdomColor;
            public long lineageId;
            public long shiId;
            public string clanName;
            public long anchorActorId;
            public long parentClaimId;
            public int generation;
            public long originalCapitalCityId;
            public long originalMandatePeriodId;
            public int strength;
            public string restoreMode;
            public string restorationState;
        }

        public static void CreateClaimsFromFallenKingdom(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || !Ready) return;
            long anchorId = ResolveFallenKingAnchor(pKingdom);
            if (anchorId < 0) return;

            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID, out long heirId, -1L);
            long capitalId = ResolveOriginalCapitalId(pKingdom);
            long mandatePeriodId = ResolveOriginalMandatePeriodId(pKingdom);
            var frontier = new List<long> { anchorId };
            var visited = new HashSet<long> { anchorId };
            int inspected = 0;

            for (int generation = 0;
                 generation <= RoyalRestorationRules.MaxClaimGeneration && frontier.Count > 0;
                 generation++)
            {
                var next = new List<long>();
                foreach (long actorId in frontier)
                {
                    if (++inspected > RoyalRestorationRules.MaxInitialDescendants) return;
                    Actor actor = World.world?.units?.get(actorId);
                    if (IsEligibleRestorationClaimant(actor))
                    {
                        int strength = actorId == anchorId
                            ? 100
                            : actorId == heirId
                                ? 85
                                : RoyalRestorationRules.InheritedClaimStrength(85, generation);
                        CreateClaim(actor, pKingdom, anchorId, -1L, generation,
                            "kingdom_fall", capitalId, mandatePeriodId, strength);
                    }

                    if (generation >= RoyalRestorationRules.MaxClaimGeneration) continue;
                    foreach (long childId in LineageQuery.GetChildIds(actorId))
                    {
                        if (childId < 0 || !visited.Add(childId)) continue;
                        if (LineageQuery.GetFatherId(childId) != actorId) continue;
                        next.Add(childId);
                        if (visited.Count >= RoyalRestorationRules.MaxInitialDescendants) break;
                    }
                }
                frontier = next;
            }
        }

        public static void OnActorBornWithParents(Actor pBaby, Actor pParent1, Actor pParent2)
        {
            if (pBaby?.data == null || !pBaby.isSexMale() || !IsEligibleRestorationClaimant(pBaby) || !Ready)
                return;
            Actor father = PickFather(pParent1, pParent2);
            if (father?.data == null) return;

            foreach (ClaimRow claim in ReadTransferableClaims(father.data.id,
                         RoyalRestorationRules.MaxAnnualCandidates))
            {
                if (!RoyalRestorationRules.CanInheritClaim(claim.generation, true,
                        childMale: true, childValid: true))
                    continue;
                int generation = RoyalRestorationRules.NextGeneration(claim.generation);
                if (!RoyalRestorationRules.ShouldCreateClaim(
                        HasActiveClaim(pBaby.data.id, claim.originalKingdomId), true, generation))
                    continue;
                CreateInheritedClaim(pBaby, claim, generation);
            }
        }

        public static bool HasHostedClaim(Kingdom pHost)
        {
            return CountHostedClaims(pHost) > 0;
        }

        public static int CountHostedClaims(Kingdom pHost)
        {
            if (pHost?.data == null || !Ready) return 0;
            RefreshActiveClaimHosts(pHost.id);
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT CLAIMANT_ACTOR_ID FROM {RoyalClaimTableItem.GetTableName()} " +
                                  "WHERE HOST_KINGDOM_ID=@h AND ACTIVE=1 " +
                                  "AND RESTORATION_STATE='dormant' AND IFNULL(RESTORE_MODE,'')=''";
                cmd.Parameters.AddWithValue("@h", pHost.id);
                int count = 0;
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                while (reader.Read())
                    if (IsAvailableRestorationLeader(
                            World.world?.units?.get(reader.GetInt64(0))))
                        count++;
                return count;
            }
            catch { return 0; }
        }

        internal static RoyalClaimInfo GetBestHostedClaim(Kingdom pHost)
        {
            if (pHost?.data == null || !Ready) return null;
            RefreshActiveClaimHosts(pHost.id);
            ClaimRow row = FindBestHostedClaim(pHost.id);
            if (row.claimId < 0) return null;
            return new RoyalClaimInfo
            {
                claim_id = row.claimId,
                claimant_actor_id = row.claimantId,
                claimant_name = row.claimantName ?? "",
                original_kingdom_id = row.originalKingdomId,
                original_kingdom_name = row.originalKingdomName ?? "",
                claim_strength = row.strength
            };
        }

        internal static List<RoyalClaimInfo> GetHostedClaims(Kingdom pHost)
        {
            var result = new List<RoyalClaimInfo>();
            if (pHost?.data == null || !Ready) return result;
            RefreshActiveClaimHosts(pHost.id);
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText =
                    $"SELECT CLAIM_ID, CLAIMANT_ACTOR_ID, CLAIMANT_NAME, ORIGINAL_KINGDOM_ID, " +
                    $"ORIGINAL_KINGDOM_NAME, CLAIM_STRENGTH FROM {RoyalClaimTableItem.GetTableName()} " +
                    "WHERE HOST_KINGDOM_ID=@h AND ACTIVE=1 AND RESTORATION_STATE='dormant' " +
                    "AND IFNULL(RESTORE_MODE,'')='' ORDER BY CLAIM_STRENGTH DESC, CREATED_TIME ASC";
                cmd.Parameters.AddWithValue("@h", pHost.id);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                while (reader.Read())
                {
                    long claimantId = reader.GetInt64(1);
                    if (!IsAvailableRestorationLeader(
                            World.world?.units?.get(claimantId)))
                        continue;
                    result.Add(new RoyalClaimInfo
                    {
                        claim_id = reader.GetInt64(0),
                        claimant_actor_id = claimantId,
                        claimant_name = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        original_kingdom_id = reader.GetInt64(3),
                        original_kingdom_name = reader.IsDBNull(4) ? "" : reader.GetString(4),
                        claim_strength = reader.GetInt32(5)
                    });
                }
            }
            catch { }
            return result;
        }

        public static void OnReclaimWarWon(Kingdom pHost, Kingdom pDefender, long pWarId)
        {
            RecordRestoreSuccess(pHost, pDefender, "reclaim_restore", "reclaim_won");
        }

        public static void OnRestorationWarWon(Kingdom pHost, Kingdom pDefender, long pWarId)
        {
            RecordRestoreSuccess(pHost, pDefender, "restoration_won", "restoration_won", pWarId, -1L, null);
        }

        public static void OnRestorationWarWon(Kingdom pHost, Kingdom pDefender, long pWarId, long pClaimId,
            City pTargetCity)
        {
            RecordRestoreSuccess(pHost, pDefender, "restoration_won", "restoration_won", pWarId, pClaimId,
                pTargetCity);
        }

        private static void RecordRestoreSuccess(Kingdom pHost, Kingdom pDefender, string pEventType,
            string pResolveReason, long pWarId = -1L, long pClaimId = -1L, City pTargetCity = null)
        {
            if (pHost?.data == null || !Ready) return;
            RefreshActiveClaimHosts(pHost.id);
            ClaimRow claim = pClaimId >= 0 ? FindClaimById(pClaimId) : FindBestHostedClaim(pHost.id);
            if (claim.claimId < 0) return;

            Actor claimant = World.world?.units?.get(claim.claimantId);
            if (!IsAvailableRestorationLeader(claimant))
            {
                if (!IsEligibleRestorationClaimant(claimant))
                    ResolveClaim(claim.claimId, "invalid_claimant");
                return;
            }

            Kingdom restored = TryRestoreKingdomForClaim(pHost, claimant, claim, pTargetCity, pWarId);
            if (restored?.data == null) return;
            HistoryText text = HistoryText.Kingdom(pHost) + " \u4ee5" +
                               HistoryText.Actor(claimant, claim.claimantName) +
                               " \u7684\u65e7\u56fd\u8840\u7edf\u53d1\u8d77\u590d\u56fd\u6218\u4e89\uff0c\u5ba3\u79f0\u6062\u590d " +
                               HistoryText.Colored(claim.originalKingdomName, "") + " \u65e7\u7edf";
            HistoryWriter.RecordKingdom(pHost, pEventType, text, HistoryTarget.Actor(claim.claimantId));
            if (claimant?.data != null)
                HistoryWriter.RecordPerson(claimant.data.id, pHost, claimant.getName(), "reclaim_claim",
                    text, ChronicleCategory.HONOR, HistoryTarget.Kingdom(pHost));
            if (restored?.data != null)
                HistoryWriter.RecordKingdom(restored, "restoration_kingdom_restored",
                    HistoryText.Actor(claimant, claim.claimantName) + " \u590d\u5efa" +
                    HistoryText.Kingdom(restored) + "\uff0c\u5949" + HistoryText.Kingdom(pHost) + " \u4e3a\u5b97\u4e3b",
                    HistoryTarget.Kingdom(pHost));

            ResolveAllClaimsForKingdom(claim.originalKingdomId, pResolveReason);
        }

        private static Kingdom TryRestoreKingdomForClaim(Kingdom pHost, Actor pClaimant, ClaimRow pClaim,
            City pTargetCity, long pWarId)
        {
            if (pHost?.data == null || !IsAvailableRestorationLeader(pClaimant)) return null;
            if (pTargetCity?.data == null || pTargetCity.isRekt()) return null;

            var request = new KingdomRestorationRequest
            {
                claim_id = pClaim.claimId,
                original_kingdom_id = pClaim.originalKingdomId,
                original_kingdom_name = pClaim.originalKingdomName,
                original_capital_city_id = pClaim.originalCapitalCityId,
                original_mandate_period_id = pClaim.originalMandatePeriodId,
                lineage_id = pClaim.lineageId,
                shi_id = pClaim.shiId,
                clan_name = pClaim.clanName,
                state_name = pClaim.shiId >= 0
                    ? StateNameService.GetBoundStateName(pClaim.shiId)
                    : "",
                mode = "hosted_restoration"
            };
            Kingdom restored = KingdomIdentityContinuityService.RestoreFromCity(
                pTargetCity, pClaimant, request, out string error);
            if (restored?.data == null)
            {
                ModClass.LogWarning("RoyalClaimService hosted identity restoration failed: " + error);
                return null;
            }

            try { VassalService.SetVassal(restored, pHost, "restoration_war", pWarId); }
            catch (Exception e) { ModClass.LogWarning("RoyalClaimService restore vassal failed: " + e.Message); }
            return restored;
        }

        private static long CreateClaim(Actor pClaimant, Kingdom pOriginalKingdom,
            long pAnchorActorId, long pParentClaimId, int pGeneration, string pOrigin,
            long pOriginalCapitalCityId, long pOriginalMandatePeriodId, int pStrength)
        {
            if (pClaimant?.data == null || pOriginalKingdom?.data == null) return -1L;
            bool duplicate = HasActiveClaim(pClaimant.data.id, pOriginalKingdom.id);
            if (!RoyalRestorationRules.ShouldCreateClaim(
                    duplicate, IsEligibleRestorationClaimant(pClaimant), pGeneration))
                return -1L;

            pClaimant.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            pClaimant.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            pClaimant.data.get(LineageKeys.CLAN_NAME, out string clanName, "");
            long claimId = TableIdAllocator.Next(DB, RoyalClaimTableItem.GetTableName(), "CLAIM_ID");

            DB.Insert(RoyalClaimTableItem.GetTableName(),
                ColumnVal.Create("CLAIM_ID", claimId),
                ColumnVal.Create("CLAIMANT_ACTOR_ID", pClaimant.data.id),
                ColumnVal.Create("CLAIMANT_NAME", pClaimant.getName() ?? ""),
                ColumnVal.Create("ORIGINAL_KINGDOM_ID", pOriginalKingdom.id),
                ColumnVal.Create("ORIGINAL_KINGDOM_NAME", pOriginalKingdom.name ?? ""),
                ColumnVal.Create("ORIGINAL_KINGDOM_COLOR", HistoryColors.FromKingdom(pOriginalKingdom)),
                ColumnVal.Create("HOST_KINGDOM_ID", pClaimant.kingdom?.id ?? -1L),
                ColumnVal.Create("HOST_KINGDOM_NAME", pClaimant.kingdom?.name ?? ""),
                ColumnVal.Create("LINEAGE_ID", lineageId),
                ColumnVal.Create("SHI_ID", shiId),
                ColumnVal.Create("CLAN_NAME", clanName ?? ""),
                ColumnVal.Create("ANCHOR_ACTOR_ID", pAnchorActorId),
                ColumnVal.Create("PARENT_CLAIM_ID", pParentClaimId),
                ColumnVal.Create("CLAIM_GENERATION", pGeneration),
                ColumnVal.Create("CLAIM_ORIGIN", pOrigin ?? ""),
                ColumnVal.Create("ORIGINAL_CAPITAL_CITY_ID", pOriginalCapitalCityId),
                ColumnVal.Create("ORIGINAL_MANDATE_PERIOD_ID", pOriginalMandatePeriodId),
                ColumnVal.Create("CLAIM_STRENGTH", pStrength),
                ColumnVal.Create("RESTORE_MODE", ""),
                ColumnVal.Create("RESTORATION_STATE", "dormant"),
                ColumnVal.Create("RESTORED_KINGDOM_ID", -1L),
                ColumnVal.Create("UPRISING_YEAR", -1),
                ColumnVal.Create("LAST_ATTEMPT_YEAR", -1),
                ColumnVal.Create("ACTIVE", 1),
                ColumnVal.Create("CREATED_TIME", LineageService.CurTime()),
                ColumnVal.Create("RESOLVED_TIME", -1.0),
                ColumnVal.Create("RESOLVED_REASON", ""));

            ActiveClaimantIds.Add(pClaimant.data.id);
            HistoryWriter.RecordPerson(pClaimant.data.id, pClaimant.kingdom, pClaimant.getName(), "royal_claim",
                HistoryText.Actor(pClaimant) + " \u83b7\u5f97 " + HistoryText.Kingdom(pOriginalKingdom) +
                " \u590d\u56fd\u5ba3\u79f0",
                ChronicleCategory.HONOR, HistoryTarget.Kingdom(pOriginalKingdom));
            return claimId;
        }

        private static void CreateInheritedClaim(Actor pBaby, ClaimRow pParentClaim, int pGeneration)
        {
            if (pBaby?.data == null || pParentClaim.claimId < 0) return;
            pBaby.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            pBaby.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            pBaby.data.get(LineageKeys.CLAN_NAME, out string clanName, "");
            if (lineageId < 0) lineageId = pParentClaim.lineageId;
            if (shiId < 0) shiId = pParentClaim.shiId;
            if (string.IsNullOrEmpty(clanName)) clanName = pParentClaim.clanName;

            long claimId = TableIdAllocator.Next(DB, RoyalClaimTableItem.GetTableName(), "CLAIM_ID");
            int strength = RoyalRestorationRules.InheritFromParentStrength(pParentClaim.strength);
            string restorationState = RoyalRestorationRules.InheritedRestorationState(
                pParentClaim.restorationState);
            DB.Insert(RoyalClaimTableItem.GetTableName(),
                ColumnVal.Create("CLAIM_ID", claimId),
                ColumnVal.Create("CLAIMANT_ACTOR_ID", pBaby.data.id),
                ColumnVal.Create("CLAIMANT_NAME", pBaby.getName() ?? ""),
                ColumnVal.Create("ORIGINAL_KINGDOM_ID", pParentClaim.originalKingdomId),
                ColumnVal.Create("ORIGINAL_KINGDOM_NAME", pParentClaim.originalKingdomName ?? ""),
                ColumnVal.Create("ORIGINAL_KINGDOM_COLOR", pParentClaim.originalKingdomColor ?? ""),
                ColumnVal.Create("HOST_KINGDOM_ID", pBaby.kingdom?.id ?? -1L),
                ColumnVal.Create("HOST_KINGDOM_NAME", pBaby.kingdom?.name ?? ""),
                ColumnVal.Create("LINEAGE_ID", lineageId),
                ColumnVal.Create("SHI_ID", shiId),
                ColumnVal.Create("CLAN_NAME", clanName ?? ""),
                ColumnVal.Create("ANCHOR_ACTOR_ID", pParentClaim.anchorActorId),
                ColumnVal.Create("PARENT_CLAIM_ID", pParentClaim.claimId),
                ColumnVal.Create("CLAIM_GENERATION", pGeneration),
                ColumnVal.Create("CLAIM_ORIGIN", "birth_inheritance"),
                ColumnVal.Create("ORIGINAL_CAPITAL_CITY_ID", pParentClaim.originalCapitalCityId),
                ColumnVal.Create("ORIGINAL_MANDATE_PERIOD_ID", pParentClaim.originalMandatePeriodId),
                ColumnVal.Create("CLAIM_STRENGTH", strength),
                ColumnVal.Create("RESTORE_MODE", ""),
                ColumnVal.Create("RESTORATION_STATE", restorationState),
                ColumnVal.Create("RESTORED_KINGDOM_ID", -1L),
                ColumnVal.Create("UPRISING_YEAR", -1),
                ColumnVal.Create("LAST_ATTEMPT_YEAR", -1),
                ColumnVal.Create("ACTIVE", 1),
                ColumnVal.Create("CREATED_TIME", LineageService.CurTime()),
                ColumnVal.Create("RESOLVED_TIME", -1.0),
                ColumnVal.Create("RESOLVED_REASON", ""));

            ActiveClaimantIds.Add(pBaby.data.id);
            HistoryText text = HistoryText.Actor(pBaby) + " \u627f\u7eed " +
                               HistoryText.Colored(pParentClaim.originalKingdomName,
                                   pParentClaim.originalKingdomColor) + " \u590d\u56fd\u5ba3\u79f0";
            HistoryWriter.RecordPerson(pBaby.data.id, pBaby.kingdom, pBaby.getName(), "royal_claim_inherited",
                text, ChronicleCategory.HONOR,
                HistoryTarget.From("kingdom", pParentClaim.originalKingdomId));
        }

        private static List<ClaimRow> ReadTransferableClaims(long pFatherActorId, int pLimit)
        {
            var result = new List<ClaimRow>();
            if (!Ready || pFatherActorId < 0 || pLimit <= 0) return result;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText =
                    $"SELECT CLAIM_ID, CLAIMANT_ACTOR_ID, CLAIMANT_NAME, ORIGINAL_KINGDOM_ID, " +
                    $"ORIGINAL_KINGDOM_NAME, ORIGINAL_KINGDOM_COLOR, LINEAGE_ID, SHI_ID, CLAN_NAME, " +
                    $"ANCHOR_ACTOR_ID, PARENT_CLAIM_ID, CLAIM_GENERATION, ORIGINAL_CAPITAL_CITY_ID, " +
                    $"ORIGINAL_MANDATE_PERIOD_ID, CLAIM_STRENGTH, RESTORE_MODE, RESTORATION_STATE " +
                    $"FROM {RoyalClaimTableItem.GetTableName()} " +
                    "WHERE CLAIMANT_ACTOR_ID=@a AND ACTIVE=1 AND CLAIM_GENERATION<@max " +
                    "ORDER BY CLAIM_GENERATION ASC, CLAIM_STRENGTH DESC, CLAIM_ID ASC LIMIT @lim";
                cmd.Parameters.AddWithValue("@a", pFatherActorId);
                cmd.Parameters.AddWithValue("@max", RoyalRestorationRules.MaxClaimGeneration);
                cmd.Parameters.AddWithValue("@lim", pLimit);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                while (reader.Read()) result.Add(ReadFullClaimRow(reader));
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Royal claim inheritance read failed: " + e.Message);
            }
            return result;
        }

        internal static List<ClaimRow> GetAutonomousCandidates(int pYear, int pLimit)
        {
            var result = new List<ClaimRow>();
            if (!Ready || pLimit <= 0) return result;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = FullClaimSelect() +
                                  " WHERE ACTIVE=1 AND RESTORATION_STATE='dormant' " +
                                  "AND IFNULL(RESTORE_MODE,'')='' AND CLAIM_STRENGTH>=@strength " +
                                  "AND (LAST_ATTEMPT_YEAR<0 OR LAST_ATTEMPT_YEAR<=@retry) " +
                                  "ORDER BY CLAIM_STRENGTH DESC, CLAIM_GENERATION ASC, CREATED_TIME ASC, CLAIM_ID ASC " +
                                  "LIMIT @lim";
                cmd.Parameters.AddWithValue("@strength", RoyalRestorationRules.AiMinimumClaimStrength);
                cmd.Parameters.AddWithValue("@retry", pYear - RestorationCampaignRules.AiRetryCooldownYears);
                cmd.Parameters.AddWithValue("@lim", pLimit);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                while (reader.Read()) result.Add(ReadFullClaimRow(reader));
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Autonomous restoration candidate read failed: " + e.Message);
            }
            return result;
        }

        internal static ClaimRow FindDormantClaim(long pClaimId)
        {
            var result = new ClaimRow { claimId = -1L };
            if (!Ready || pClaimId < 0) return result;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = FullClaimSelect() +
                                  " WHERE CLAIM_ID=@c AND ACTIVE=1 AND RESTORATION_STATE='dormant' LIMIT 1";
                cmd.Parameters.AddWithValue("@c", pClaimId);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                return reader.Read() ? ReadFullClaimRow(reader) : result;
            }
            catch { return result; }
        }

        internal static long FindBestDormantClaimIdForActor(long pActorId)
        {
            if (!Ready || pActorId < 0) return -1L;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT CLAIM_ID FROM {RoyalClaimTableItem.GetTableName()} " +
                                  "WHERE CLAIMANT_ACTOR_ID=@a AND ACTIVE=1 " +
                                  "AND RESTORATION_STATE='dormant' AND IFNULL(RESTORE_MODE,'')='' " +
                                  "ORDER BY CLAIM_STRENGTH DESC, CLAIM_GENERATION ASC, CREATED_TIME ASC " +
                                  "LIMIT 1";
                cmd.Parameters.AddWithValue("@a", pActorId);
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value
                    ? -1L
                    : Convert.ToInt64(value);
            }
            catch { return -1L; }
        }

        internal static void MarkAutonomousAttempt(long pClaimId, int pYear)
        {
            if (!Ready || pClaimId < 0) return;
            DB.UpdateValue(RoyalClaimTableItem.GetTableName(),
                new List<SimpleColumnConstraint>
                {
                    SimpleColumnConstraint.CreateEq("CLAIM_ID", pClaimId)
                }, ColumnVal.Create("LAST_ATTEMPT_YEAR", pYear));
        }

        internal static long BeginSelfCampaign(ClaimRow pClaim, Actor pClaimant, City pSeed,
            string pCoreIds, int pControlled, int pTotal, int pYear)
        {
            if (!Ready || pClaim.claimId < 0 || pClaimant?.data == null || pSeed?.data == null) return -1L;
            long campaignId = TableIdAllocator.Next(DB,
                RestorationCampaignTableItem.GetTableName(), "CAMPAIGN_ID");
            using var transaction = DB.BeginTransaction();
            try
            {
                using (var suspend = new SQLiteCommand(DB))
                {
                    suspend.Transaction = transaction;
                    suspend.CommandText = $"UPDATE {RoyalClaimTableItem.GetTableName()} " +
                                          "SET RESTORATION_STATE='suspended' " +
                                          "WHERE ORIGINAL_KINGDOM_ID=@k AND ACTIVE=1";
                    suspend.Parameters.AddWithValue("@k", pClaim.originalKingdomId);
                    suspend.ExecuteNonQuery();
                }
                using (var start = new SQLiteCommand(DB))
                {
                    start.Transaction = transaction;
                    start.CommandText = $"UPDATE {RoyalClaimTableItem.GetTableName()} SET " +
                                        "RESTORE_MODE='self_restoration', RESTORATION_STATE='campaign', " +
                                        "RESTORED_KINGDOM_ID=@k, UPRISING_YEAR=@y, LAST_ATTEMPT_YEAR=@y " +
                                        "WHERE CLAIM_ID=@c AND ACTIVE=1";
                    start.Parameters.AddWithValue("@k", pClaim.originalKingdomId);
                    start.Parameters.AddWithValue("@y", pYear);
                    start.Parameters.AddWithValue("@c", pClaim.claimId);
                    if (start.ExecuteNonQuery() != 1) throw new InvalidOperationException("claim_start_conflict");
                }
                using (var insert = new SQLiteCommand(DB))
                {
                    insert.Transaction = transaction;
                    insert.CommandText = $"INSERT INTO {RestorationCampaignTableItem.GetTableName()} " +
                                         "(CAMPAIGN_ID, CLAIM_ID, ORIGINAL_KINGDOM_ID, CLAIMANT_ACTOR_ID, " +
                                         "CLAIMANT_NAME, SEED_CITY_ID, SEED_CITY_NAME, ORIGINAL_MANDATE_PERIOD_ID, " +
                                         "STATE, CORE_CITY_IDS, CORE_CURSOR, CONTROLLED_CORE_COUNT, TOTAL_CORE_COUNT, " +
                                         "ACTIVE_WAR_ID, TARGET_CITY_ID, TARGET_KINGDOM_ID, STARTED_YEAR, " +
                                         "LAST_ATTEMPT_YEAR, STARTED_TIME, COMPLETED_TIME, RESULT) VALUES " +
                                         "(@id,@claim,@kingdom,@actor,@actor_name,@city,@city_name,@period," +
                                         "'uprising',@cores,0,@controlled,@total,-1,-1,-1,@year,@year,@time,-1,'')";
                    insert.Parameters.AddWithValue("@id", campaignId);
                    insert.Parameters.AddWithValue("@claim", pClaim.claimId);
                    insert.Parameters.AddWithValue("@kingdom", pClaim.originalKingdomId);
                    insert.Parameters.AddWithValue("@actor", pClaimant.data.id);
                    insert.Parameters.AddWithValue("@actor_name", pClaimant.getName() ?? "");
                    insert.Parameters.AddWithValue("@city", pSeed.data.id);
                    insert.Parameters.AddWithValue("@city_name", pSeed.data.name ?? "");
                    insert.Parameters.AddWithValue("@period", pClaim.originalMandatePeriodId);
                    insert.Parameters.AddWithValue("@cores", pCoreIds ?? "");
                    insert.Parameters.AddWithValue("@controlled", pControlled);
                    insert.Parameters.AddWithValue("@total", pTotal);
                    insert.Parameters.AddWithValue("@year", pYear);
                    insert.Parameters.AddWithValue("@time", LineageService.CurTime());
                    insert.ExecuteNonQuery();
                }
                transaction.Commit();
                return campaignId;
            }
            catch (Exception e)
            {
                try { transaction.Rollback(); } catch { }
                ModClass.LogWarning("Self restoration campaign start failed: " + e.Message);
                return -1L;
            }
        }

        internal static void ResolveAllClaimsForKingdom(long pOriginalKingdomId, string pReason)
        {
            if (!Ready || pOriginalKingdomId < 0) return;
            using var cmd = new SQLiteCommand(DB);
            cmd.CommandText = $"UPDATE {RoyalClaimTableItem.GetTableName()} SET ACTIVE=0, " +
                              "RESTORATION_STATE='resolved', RESOLVED_TIME=@time, RESOLVED_REASON=@reason " +
                              "WHERE ORIGINAL_KINGDOM_ID=@k AND ACTIVE=1";
            cmd.Parameters.AddWithValue("@time", LineageService.CurTime());
            cmd.Parameters.AddWithValue("@reason", pReason ?? "restored");
            cmd.Parameters.AddWithValue("@k", pOriginalKingdomId);
            cmd.ExecuteNonQuery();
        }

        internal static bool CompleteSelfCampaign(long pCampaignId,
            long pOriginalKingdomId, string pReason)
        {
            if (!Ready || pCampaignId < 0 || pOriginalKingdomId < 0) return false;
            using var transaction = DB.BeginTransaction();
            try
            {
                using (var campaign = new SQLiteCommand(DB))
                {
                    campaign.Transaction = transaction;
                    campaign.CommandText = $"UPDATE {RestorationCampaignTableItem.GetTableName()} SET " +
                                           "STATE='completed', COMPLETED_TIME=@time, RESULT=@reason, " +
                                           "ACTIVE_WAR_ID=-1, TARGET_CITY_ID=-1, TARGET_KINGDOM_ID=-1 " +
                                           "WHERE CAMPAIGN_ID=@id AND ORIGINAL_KINGDOM_ID=@k " +
                                           "AND STATE='uprising'";
                    campaign.Parameters.AddWithValue("@time", LineageService.CurTime());
                    campaign.Parameters.AddWithValue("@reason", pReason ?? "restored");
                    campaign.Parameters.AddWithValue("@id", pCampaignId);
                    campaign.Parameters.AddWithValue("@k", pOriginalKingdomId);
                    if (campaign.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("campaign_complete_conflict");
                }

                using (var claims = new SQLiteCommand(DB))
                {
                    claims.Transaction = transaction;
                    claims.CommandText = $"UPDATE {RoyalClaimTableItem.GetTableName()} SET " +
                                         "ACTIVE=0, RESTORATION_STATE='resolved', RESOLVED_TIME=@time, " +
                                         "RESOLVED_REASON=@reason WHERE ORIGINAL_KINGDOM_ID=@k AND ACTIVE=1";
                    claims.Parameters.AddWithValue("@time", LineageService.CurTime());
                    claims.Parameters.AddWithValue("@reason", pReason ?? "restored");
                    claims.Parameters.AddWithValue("@k", pOriginalKingdomId);
                    claims.ExecuteNonQuery();
                }

                transaction.Commit();
                return true;
            }
            catch (Exception e)
            {
                try { transaction.Rollback(); } catch { }
                ModClass.LogWarning("Self restoration completion failed: " + e.Message);
                return false;
            }
        }

        internal static bool FailSelfCampaign(long pCampaignId,
            long pOriginalKingdomId, int pYear, string pReason)
        {
            if (!Ready || pCampaignId < 0 || pOriginalKingdomId < 0) return false;
            using var transaction = DB.BeginTransaction();
            try
            {
                using (var campaign = new SQLiteCommand(DB))
                {
                    campaign.Transaction = transaction;
                    campaign.CommandText = $"UPDATE {RestorationCampaignTableItem.GetTableName()} SET " +
                                           "STATE='failed', COMPLETED_TIME=@time, RESULT=@reason, " +
                                           "ACTIVE_WAR_ID=-1, TARGET_CITY_ID=-1, TARGET_KINGDOM_ID=-1 " +
                                           "WHERE CAMPAIGN_ID=@id AND ORIGINAL_KINGDOM_ID=@k " +
                                           "AND STATE='uprising'";
                    campaign.Parameters.AddWithValue("@time", LineageService.CurTime());
                    campaign.Parameters.AddWithValue("@reason", pReason ?? "restoration_regime_fell");
                    campaign.Parameters.AddWithValue("@id", pCampaignId);
                    campaign.Parameters.AddWithValue("@k", pOriginalKingdomId);
                    if (campaign.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("campaign_fail_conflict");
                }

                using (var claims = new SQLiteCommand(DB))
                {
                    claims.Transaction = transaction;
                    claims.CommandText = $"UPDATE {RoyalClaimTableItem.GetTableName()} SET " +
                                         "RESTORE_MODE='', RESTORATION_STATE='dormant', " +
                                         "RESTORED_KINGDOM_ID=-1, UPRISING_YEAR=-1, LAST_ATTEMPT_YEAR=@year " +
                                         "WHERE ORIGINAL_KINGDOM_ID=@k AND ACTIVE=1";
                    claims.Parameters.AddWithValue("@year", pYear);
                    claims.Parameters.AddWithValue("@k", pOriginalKingdomId);
                    claims.ExecuteNonQuery();
                }

                transaction.Commit();
                return true;
            }
            catch (Exception e)
            {
                try { transaction.Rollback(); } catch { }
                ModClass.LogWarning("Self restoration failure rollback failed: " + e.Message);
                return false;
            }
        }

        internal static bool RecoverOrphanedSelfCampaignClaims(
            long pOriginalKingdomId, int pYear)
        {
            if (!Ready || pOriginalKingdomId < 0) return false;
            using var transaction = DB.BeginTransaction();
            try
            {
                int activeCount;
                using (var count = new SQLiteCommand(DB))
                {
                    count.Transaction = transaction;
                    count.CommandText = $"SELECT COUNT(*) FROM {RoyalClaimTableItem.GetTableName()} " +
                                        "WHERE ORIGINAL_KINGDOM_ID=@k AND ACTIVE=1";
                    count.Parameters.AddWithValue("@k", pOriginalKingdomId);
                    activeCount = Convert.ToInt32(count.ExecuteScalar());
                }
                if (activeCount <= 0)
                {
                    transaction.Rollback();
                    return false;
                }

                using (var claims = new SQLiteCommand(DB))
                {
                    claims.Transaction = transaction;
                    claims.CommandText = $"UPDATE {RoyalClaimTableItem.GetTableName()} SET " +
                                         "RESTORE_MODE='', RESTORATION_STATE='dormant', " +
                                         "RESTORED_KINGDOM_ID=-1, UPRISING_YEAR=-1, LAST_ATTEMPT_YEAR=@year " +
                                         "WHERE ORIGINAL_KINGDOM_ID=@k AND ACTIVE=1";
                    claims.Parameters.AddWithValue("@year", pYear);
                    claims.Parameters.AddWithValue("@k", pOriginalKingdomId);
                    claims.ExecuteNonQuery();
                }
                transaction.Commit();
                return true;
            }
            catch (Exception e)
            {
                try { transaction.Rollback(); } catch { }
                ModClass.LogWarning("Orphaned restoration claim recovery failed: " + e.Message);
                return true;
            }
        }

        private static ClaimRow ReadFullClaimRow(SQLiteDataReader pReader)
        {
            return new ClaimRow
            {
                claimId = pReader.GetInt64(0),
                claimantId = pReader.GetInt64(1),
                claimantName = pReader.IsDBNull(2) ? "" : pReader.GetString(2),
                originalKingdomId = pReader.GetInt64(3),
                originalKingdomName = pReader.IsDBNull(4) ? "" : pReader.GetString(4),
                originalKingdomColor = pReader.IsDBNull(5) ? "" : pReader.GetString(5),
                lineageId = pReader.IsDBNull(6) ? -1L : pReader.GetInt64(6),
                shiId = pReader.IsDBNull(7) ? -1L : pReader.GetInt64(7),
                clanName = pReader.IsDBNull(8) ? "" : pReader.GetString(8),
                anchorActorId = pReader.IsDBNull(9) ? -1L : pReader.GetInt64(9),
                parentClaimId = pReader.IsDBNull(10) ? -1L : pReader.GetInt64(10),
                generation = pReader.IsDBNull(11) ? 0 : pReader.GetInt32(11),
                originalCapitalCityId = pReader.IsDBNull(12) ? -1L : pReader.GetInt64(12),
                originalMandatePeriodId = pReader.IsDBNull(13) ? -1L : pReader.GetInt64(13),
                strength = pReader.IsDBNull(14) ? 0 : pReader.GetInt32(14),
                restoreMode = pReader.IsDBNull(15) ? "" : pReader.GetString(15),
                restorationState = pReader.IsDBNull(16) ? "dormant" : pReader.GetString(16)
            };
        }

        private static long ResolveFallenKingAnchor(Kingdom pKingdom)
        {
            if (pKingdom?.king?.data != null) return pKingdom.king.data.id;
            if (pKingdom?.data == null) return -1L;
            pKingdom.data.get(LineageKeys.CHRONICLE_LAST_KING_ID, out long actorId, -1L);
            return actorId;
        }

        private static long ResolveOriginalCapitalId(Kingdom pKingdom)
        {
            if (pKingdom?.capital?.data != null) return pKingdom.capital.data.id;
            if (pKingdom?.data == null) return -1L;
            if (pKingdom.data.last_capital_id >= 0) return pKingdom.data.last_capital_id;
            return pKingdom.data.capitalID;
        }

        private static long ResolveOriginalMandatePeriodId(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || !MandateService.IsMandateKingdom(pKingdom)) return -1L;
            pKingdom.data.get(LineageKeys.MANDATE_PERIOD_ID, out long periodId, -1L);
            return periodId;
        }

        private static Actor PickFather(Actor pParent1, Actor pParent2)
        {
            if (pParent1?.data != null && pParent1.isSexMale()) return pParent1;
            if (pParent2?.data != null && pParent2.isSexMale()) return pParent2;
            return null;
        }

        private static bool HasActiveClaim(long pActorId, long pKingdomId)
        {
            using var cmd = new SQLiteCommand(DB);
            cmd.CommandText =
                $"SELECT 1 FROM {RoyalClaimTableItem.GetTableName()} " +
                "WHERE CLAIMANT_ACTOR_ID=@a AND ORIGINAL_KINGDOM_ID=@k AND ACTIVE=1 LIMIT 1";
            cmd.Parameters.AddWithValue("@a", pActorId);
            cmd.Parameters.AddWithValue("@k", pKingdomId);
            object value = cmd.ExecuteScalar();
            return value != null && value != DBNull.Value;
        }

        private static ClaimRow FindBestHostedClaim(long pHostKingdomId)
        {
            var result = new ClaimRow { claimId = -1 };
            using var cmd = new SQLiteCommand(DB);
            cmd.CommandText =
                FullClaimSelect() +
                " WHERE HOST_KINGDOM_ID=@h AND ACTIVE=1 AND RESTORATION_STATE='dormant' " +
                "AND IFNULL(RESTORE_MODE,'')='' ORDER BY CLAIM_STRENGTH DESC, CREATED_TIME ASC";
            cmd.Parameters.AddWithValue("@h", pHostKingdomId);
            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            while (reader.Read())
            {
                ClaimRow row = ReadFullClaimRow(reader);
                if (IsAvailableRestorationLeader(
                        World.world?.units?.get(row.claimantId)))
                    return row;
            }
            return result;
        }

        private static ClaimRow FindClaimById(long pClaimId)
        {
            var result = new ClaimRow { claimId = -1 };
            if (pClaimId < 0) return result;
            using var cmd = new SQLiteCommand(DB);
            cmd.CommandText =
                FullClaimSelect() +
                " WHERE CLAIM_ID=@c AND ACTIVE=1 AND RESTORATION_STATE='dormant' LIMIT 1";
            cmd.Parameters.AddWithValue("@c", pClaimId);
            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            if (!reader.Read()) return result;
            return ReadFullClaimRow(reader);
        }

        private static string FullClaimSelect()
        {
            return $"SELECT CLAIM_ID, CLAIMANT_ACTOR_ID, CLAIMANT_NAME, ORIGINAL_KINGDOM_ID, " +
                   $"ORIGINAL_KINGDOM_NAME, ORIGINAL_KINGDOM_COLOR, LINEAGE_ID, SHI_ID, CLAN_NAME, " +
                   $"ANCHOR_ACTOR_ID, PARENT_CLAIM_ID, CLAIM_GENERATION, ORIGINAL_CAPITAL_CITY_ID, " +
                   $"ORIGINAL_MANDATE_PERIOD_ID, CLAIM_STRENGTH, RESTORE_MODE, RESTORATION_STATE " +
                   $"FROM {RoyalClaimTableItem.GetTableName()}";
        }

        private static void RefreshActiveClaimHosts(long pHostKingdomId)
        {
            if (!Ready || pHostKingdomId < 0) return;
            using var cmd = new SQLiteCommand(DB);
            cmd.CommandText =
                $"SELECT CLAIM_ID, CLAIMANT_ACTOR_ID FROM {RoyalClaimTableItem.GetTableName()} " +
                "WHERE HOST_KINGDOM_ID=@h AND ACTIVE=1";
            cmd.Parameters.AddWithValue("@h", pHostKingdomId);
            var updates = new List<(long claimId, long hostId, string hostName)>();
            var invalidClaims = new List<long>();
            using (var reader = (SQLiteDataReader)cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    long claimId = reader.GetInt64(0);
                    long actorId = reader.GetInt64(1);
                    Actor actor = World.world?.units?.get(actorId);
                    if (!IsEligibleRestorationClaimant(actor))
                    {
                        invalidClaims.Add(claimId);
                        continue;
                    }
                    long actorHostId = actor.kingdom?.id ?? -1L;
                    if (actorHostId != pHostKingdomId)
                        updates.Add((claimId, actorHostId,
                            actor.kingdom?.name ?? ""));
                }
            }

            foreach (long claimId in invalidClaims)
                ResolveClaim(claimId, "invalid_claimant");

            foreach (var update in updates)
            {
                DB.UpdateValue(RoyalClaimTableItem.GetTableName(),
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("CLAIM_ID", update.claimId) },
                    ColumnVal.Create("HOST_KINGDOM_ID", update.hostId),
                    ColumnVal.Create("HOST_KINGDOM_NAME", update.hostName));
            }
        }

        internal static void OnActorKingdomChanged(Actor pActor)
        {
            if (!Ready || pActor?.data == null || !MayHaveActiveClaim(pActor.data.id)) return;
            try
            {
                if (!IsEligibleRestorationClaimant(pActor))
                {
                    ResolveClaimsForActor(pActor.data.id, "invalid_claimant");
                    return;
                }
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"UPDATE {RoyalClaimTableItem.GetTableName()} SET " +
                                  "HOST_KINGDOM_ID=@h, HOST_KINGDOM_NAME=@name " +
                                  "WHERE CLAIMANT_ACTOR_ID=@a AND ACTIVE=1";
                cmd.Parameters.AddWithValue("@h", pActor.kingdom?.id ?? -1L);
                cmd.Parameters.AddWithValue("@name", pActor.kingdom?.name ?? "");
                cmd.Parameters.AddWithValue("@a", pActor.data.id);
                cmd.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Royal claim host update failed: " + e.Message);
            }
        }

        internal static void OnActorDied(Actor pActor)
        {
            if (!Ready || pActor?.data == null || !MayHaveActiveClaim(pActor.data.id)) return;
            ResolveClaimsForActor(pActor.data.id, "claimant_died");
        }

        private static void ResolveClaimsForActor(long pActorId, string pReason)
        {
            if (!Ready || pActorId < 0) return;
            using var cmd = new SQLiteCommand(DB);
            cmd.CommandText = $"UPDATE {RoyalClaimTableItem.GetTableName()} SET " +
                              "ACTIVE=0, RESTORATION_STATE='resolved', RESOLVED_TIME=@time, " +
                              "RESOLVED_REASON=@reason WHERE CLAIMANT_ACTOR_ID=@a AND ACTIVE=1";
            cmd.Parameters.AddWithValue("@time", LineageService.CurTime());
            cmd.Parameters.AddWithValue("@reason", pReason ?? "invalid_claimant");
            cmd.Parameters.AddWithValue("@a", pActorId);
            cmd.ExecuteNonQuery();
            ActiveClaimantIds.Remove(pActorId);
        }

        internal static void ClearRuntime()
        {
            ActiveClaimantIds.Clear();
            _activeClaimantsLoaded = false;
        }

        private static bool MayHaveActiveClaim(long pActorId)
        {
            if (pActorId < 0 || !Ready) return false;
            EnsureActiveClaimantCache();
            return ActiveClaimantIds.Contains(pActorId);
        }

        private static void EnsureActiveClaimantCache()
        {
            if (_activeClaimantsLoaded || !Ready) return;
            ActiveClaimantIds.Clear();
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT DISTINCT CLAIMANT_ACTOR_ID FROM {RoyalClaimTableItem.GetTableName()} " +
                                  "WHERE ACTIVE=1";
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                while (reader.Read())
                {
                    long actorId = reader.GetInt64(0);
                    if (actorId >= 0) ActiveClaimantIds.Add(actorId);
                }
                _activeClaimantsLoaded = true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Royal claimant cache load failed: " + e.Message);
            }
        }

        internal static void ResolveClaim(long pClaimId, string pReason)
        {
            DB.UpdateValue(RoyalClaimTableItem.GetTableName(),
                new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("CLAIM_ID", pClaimId) },
                ColumnVal.Create("ACTIVE", 0),
                ColumnVal.Create("RESOLVED_TIME", LineageService.CurTime()),
                ColumnVal.Create("RESOLVED_REASON", pReason ?? ""));
        }

        internal static bool IsEligibleRestorationClaimant(Actor pActor)
        {
            return RoyalRestorationClaimRules.IsEligibleClaimant(
                pHasActor: pActor?.data != null,
                pIsRekt: pActor?.isRekt() ?? true,
                pIsMale: pActor?.isSexMale() ?? false,
                pIsMad: pActor?.hasTrait("madness") ?? false);
        }

        internal static bool IsAvailableRestorationLeader(Actor pActor)
        {
            bool eligible = IsEligibleRestorationClaimant(pActor);
            bool reigning = false;
            try { reigning = pActor?.isKing() ?? false; }
            catch { }
            return RoyalRestorationClaimRules.CanLeadRestoration(eligible, reigning);
        }
    }
}
