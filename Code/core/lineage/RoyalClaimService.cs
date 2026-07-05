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
            public int strength;
        }

        public static void CreateClaimsFromFallenKingdom(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || !Ready) return;
            var candidates = new HashSet<long>();
            if (pKingdom.king?.data != null) candidates.Add(pKingdom.king.data.id);

            try
            {
                Actor heir = HeirService.GetHeir(pKingdom);
                if (heir?.data != null) candidates.Add(heir.data.id);
            }
            catch { }

            try
            {
                if (pKingdom.data.royal_clan_id.hasValue())
                {
                    Clan clan = World.world?.clans?.get(pKingdom.data.royal_clan_id);
                    if (clan != null)
                    {
                        foreach (Actor unit in clan.units)
                            if (unit?.data != null) candidates.Add(unit.data.id);
                    }
                }
            }
            catch { }

            foreach (long actorId in candidates)
            {
                Actor actor = World.world?.units?.get(actorId);
                if (actor?.data == null || actor.isRekt()) continue;
                CreateClaim(actor, pKingdom);
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
            if (pHost?.data == null || pClaimant?.data == null || pClaimant.isRekt()) return null;
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

        private static void CreateClaim(Actor pClaimant, Kingdom pOriginalKingdom)
        {
            if (pClaimant?.data == null || pOriginalKingdom?.data == null) return;
            if (HasActiveClaim(pClaimant.data.id, pOriginalKingdom.id)) return;

            pClaimant.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            pClaimant.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            pClaimant.data.get(LineageKeys.CLAN_NAME, out string clanName, "");
            int strength = ClaimStrength(pClaimant, pOriginalKingdom);
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
                ColumnVal.Create("CLAIM_STRENGTH", strength),
                ColumnVal.Create("ACTIVE", 1),
                ColumnVal.Create("CREATED_TIME", LineageService.CurTime()),
                ColumnVal.Create("RESOLVED_TIME", -1.0),
                ColumnVal.Create("RESOLVED_REASON", ""));

            HistoryWriter.RecordPerson(pClaimant.data.id, pClaimant.kingdom, pClaimant.getName(), "royal_claim",
                HistoryText.Actor(pClaimant) + " \u83b7\u5f97 " + HistoryText.Kingdom(pOriginalKingdom) +
                " \u590d\u56fd\u5ba3\u79f0",
                ChronicleCategory.HONOR, HistoryTarget.Kingdom(pOriginalKingdom));
        }

        private static int ClaimStrength(Actor pActor, Kingdom pOriginalKingdom)
        {
            if (pActor == pOriginalKingdom.king) return 100;
            try
            {
                Actor heir = HeirService.GetHeir(pOriginalKingdom);
                if (heir?.data != null && heir.data.id == pActor.data.id) return 85;
            }
            catch { }
            if (pActor.isCityLeader()) return 60;
            if (pActor.hasTrait(LineageKeys.TRAIT_GUIZU)) return 45;
            return 25;
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
            using (var reader = (SQLiteDataReader)cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    long claimId = reader.GetInt64(0);
                    long actorId = reader.GetInt64(1);
                    Actor actor = World.world?.units?.get(actorId);
                    if (actor?.kingdom?.data == null) continue;
                    updates.Add((claimId, actor.kingdom.id, actor.kingdom.name ?? ""));
                }
            }

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
    }
}
