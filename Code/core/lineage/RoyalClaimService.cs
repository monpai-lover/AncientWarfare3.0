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

        internal sealed class RoyalClaimInfo
        {
            public long claim_id = -1;
            public long claimant_actor_id = -1;
            public string claimant_name = "";
            public long original_kingdom_id = -1;
            public string original_kingdom_name = "";
            public int claim_strength;
        }

        private struct ClaimRow
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
            RefreshActiveClaimHosts();
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT COUNT(*) FROM {RoyalClaimTableItem.GetTableName()} " +
                                  "WHERE HOST_KINGDOM_ID=@h AND ACTIVE=1";
                cmd.Parameters.AddWithValue("@h", pHost.id);
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
            }
            catch { return 0; }
        }

        internal static RoyalClaimInfo GetBestHostedClaim(Kingdom pHost)
        {
            if (pHost?.data == null || !Ready) return null;
            RefreshActiveClaimHosts();
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
            RefreshActiveClaimHosts();
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText =
                    $"SELECT CLAIM_ID, CLAIMANT_ACTOR_ID, CLAIMANT_NAME, ORIGINAL_KINGDOM_ID, " +
                    $"ORIGINAL_KINGDOM_NAME, CLAIM_STRENGTH FROM {RoyalClaimTableItem.GetTableName()} " +
                    "WHERE HOST_KINGDOM_ID=@h AND ACTIVE=1 ORDER BY CLAIM_STRENGTH DESC, CREATED_TIME ASC";
                cmd.Parameters.AddWithValue("@h", pHost.id);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new RoyalClaimInfo
                    {
                        claim_id = reader.GetInt64(0),
                        claimant_actor_id = reader.GetInt64(1),
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
            RefreshActiveClaimHosts();
            ClaimRow claim = pClaimId >= 0 ? FindClaimById(pClaimId) : FindBestHostedClaim(pHost.id);
            if (claim.claimId < 0) return;

            Actor claimant = World.world?.units?.get(claim.claimantId);
            if (!IsEligibleRestorationClaimant(claimant))
            {
                ResolveClaim(claim.claimId, "invalid_claimant");
                return;
            }

            Kingdom restored = TryRestoreKingdomForClaim(pHost, claimant, claim, pTargetCity, pWarId);
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

            ResolveClaim(claim.claimId, pResolveReason);
        }

        private static Kingdom TryRestoreKingdomForClaim(Kingdom pHost, Actor pClaimant, ClaimRow pClaim,
            City pTargetCity, long pWarId)
        {
            if (pHost?.data == null || !IsEligibleRestorationClaimant(pClaimant)) return null;
            if (pTargetCity?.data == null || pTargetCity.isRekt()) return null;

            Kingdom restored = null;
            try
            {
                if (pClaimant.hasArmy()) pClaimant.removeFromArmy();
                bool claimantInTargetCity = pClaimant.city == pTargetCity;
                if (RestorationSettlementRules.ShouldMoveClaimantToTargetCityBeforeKingdomCreation(
                        claimantInTargetCity))
                    pClaimant.joinCity(pTargetCity);
                restored = pTargetCity.makeOwnKingdom(pClaimant, pRebellion: true, pFellApart: false);
            }
            catch (Exception e)
            {
                ModClass.LogWarning("RoyalClaimService restore makeOwnKingdom failed: " + e.Message);
                return null;
            }

            if (restored?.data == null) return null;
            try
            {
                if (!string.IsNullOrEmpty(pClaim.originalKingdomName))
                    restored.setName(pClaim.originalKingdomName);
                if (pClaimant.city != pTargetCity)
                    pClaimant.joinCity(pTargetCity);
                pTargetCity.setLeader(pClaimant, pNew: true);
            }
            catch (Exception e)
            {
                ModClass.LogWarning("RoyalClaimService restore settlement failed: " + e.Message);
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
                ColumnVal.Create("RESTORATION_STATE", "dormant"),
                ColumnVal.Create("RESTORED_KINGDOM_ID", -1L),
                ColumnVal.Create("UPRISING_YEAR", -1),
                ColumnVal.Create("LAST_ATTEMPT_YEAR", -1),
                ColumnVal.Create("ACTIVE", 1),
                ColumnVal.Create("CREATED_TIME", LineageService.CurTime()),
                ColumnVal.Create("RESOLVED_TIME", -1.0),
                ColumnVal.Create("RESOLVED_REASON", ""));

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
                $"SELECT CLAIM_ID, CLAIMANT_ACTOR_ID, CLAIMANT_NAME, ORIGINAL_KINGDOM_ID, " +
                $"ORIGINAL_KINGDOM_NAME, CLAIM_STRENGTH FROM {RoyalClaimTableItem.GetTableName()} " +
                "WHERE HOST_KINGDOM_ID=@h AND ACTIVE=1 ORDER BY CLAIM_STRENGTH DESC, CREATED_TIME ASC LIMIT 1";
            cmd.Parameters.AddWithValue("@h", pHostKingdomId);
            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            if (!reader.Read()) return result;
            result.claimId = reader.GetInt64(0);
            result.claimantId = reader.GetInt64(1);
            result.claimantName = reader.IsDBNull(2) ? "" : reader.GetString(2);
            result.originalKingdomId = reader.GetInt64(3);
            result.originalKingdomName = reader.IsDBNull(4) ? "" : reader.GetString(4);
            result.strength = reader.GetInt32(5);
            return result;
        }

        private static ClaimRow FindClaimById(long pClaimId)
        {
            var result = new ClaimRow { claimId = -1 };
            if (pClaimId < 0) return result;
            using var cmd = new SQLiteCommand(DB);
            cmd.CommandText =
                $"SELECT CLAIM_ID, CLAIMANT_ACTOR_ID, CLAIMANT_NAME, ORIGINAL_KINGDOM_ID, " +
                $"ORIGINAL_KINGDOM_NAME, CLAIM_STRENGTH FROM {RoyalClaimTableItem.GetTableName()} " +
                "WHERE CLAIM_ID=@c AND ACTIVE=1 LIMIT 1";
            cmd.Parameters.AddWithValue("@c", pClaimId);
            using var reader = (SQLiteDataReader)cmd.ExecuteReader();
            if (!reader.Read()) return result;
            result.claimId = reader.GetInt64(0);
            result.claimantId = reader.GetInt64(1);
            result.claimantName = reader.IsDBNull(2) ? "" : reader.GetString(2);
            result.originalKingdomId = reader.GetInt64(3);
            result.originalKingdomName = reader.IsDBNull(4) ? "" : reader.GetString(4);
            result.strength = reader.GetInt32(5);
            return result;
        }

        private static void RefreshActiveClaimHosts()
        {
            using var cmd = new SQLiteCommand(DB);
            cmd.CommandText =
                $"SELECT CLAIM_ID, CLAIMANT_ACTOR_ID FROM {RoyalClaimTableItem.GetTableName()} WHERE ACTIVE=1";
            var updates = new List<(long claimId, long hostId, string hostName)>();
            var invalidClaims = new List<long>();
            using (var reader = (SQLiteDataReader)cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    long claimId = reader.GetInt64(0);
                    long actorId = reader.GetInt64(1);
                    Actor actor = World.world?.units?.get(actorId);
                    if (!IsEligibleRestorationClaimant(actor) || actor.kingdom?.data == null)
                    {
                        invalidClaims.Add(claimId);
                        continue;
                    }
                    updates.Add((claimId, actor.kingdom.id, actor.kingdom.name ?? ""));
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

        private static void ResolveClaim(long pClaimId, string pReason)
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
    }
}
