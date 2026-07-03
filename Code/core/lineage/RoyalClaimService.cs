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

        public static void OnReclaimWarWon(Kingdom pHost, Kingdom pDefender, long pWarId)
        {
            if (pHost?.data == null || !Ready) return;
            RefreshActiveClaimHosts();
            ClaimRow claim = FindBestHostedClaim(pHost.id);
            if (claim.claimId < 0) return;

            Actor claimant = World.world?.units?.get(claim.claimantId);
            HistoryText text = HistoryText.Kingdom(pHost) + " 以 " +
                               HistoryText.Actor(claimant, claim.claimantName) +
                               " 的旧国血统发起复国战争，宣称恢复 " +
                               HistoryText.Colored(claim.originalKingdomName, "") + " 旧统";
            HistoryWriter.RecordKingdom(pHost, "reclaim_restore", text, HistoryTarget.Actor(claim.claimantId));
            if (claimant?.data != null)
                HistoryWriter.RecordPerson(claimant.data.id, pHost, claimant.getName(), "reclaim_claim",
                    text, ChronicleCategory.HONOR, HistoryTarget.Kingdom(pHost));

            ResolveClaim(claim.claimId, "reclaim_won");
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
                HistoryText.Actor(pClaimant) + " 获得 " + HistoryText.Kingdom(pOriginalKingdom) + " 复国宣称",
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
