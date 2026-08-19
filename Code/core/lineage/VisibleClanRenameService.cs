using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.naming;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    internal static class VisibleClanRenameService
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

        public static int RenameVisibleMembers(IEnumerable<long> pVisibleActorIds, long pShiId,
            bool pModeIsBigTree, string pRawClanName)
        {
            if (!Ready) return 0;
            if (!VisibleClanRenameRules.TryNormalizeClanName(pRawClanName, out string clanName)) return 0;

            List<long> ids = VisibleClanRenameRules.ShouldUseWholeShiTreeScope(pModeIsBigTree, pShiId)
                ? CollectShiTreeActorIds(pShiId)
                : VisibleClanRenameRules.CollectValidVisibleActorIds(pVisibleActorIds);
            if (ids.Count == 0) return 0;

            if (VisibleClanRenameRules.ShouldUpdateBranchName(pModeIsBigTree, pShiId, ids.Count))
                UpdateBranchName(pShiId, clanName);

            int changed = 0;
            var vanillaClans = new Dictionary<Clan, Actor>();
            foreach (long id in ids)
            {
                Actor live = FindActor(id);
                if (live?.clan != null &&
                    !vanillaClans.ContainsKey(live.clan))
                    vanillaClans.Add(live.clan, live);
                if (RenameActor(id, clanName))
                    changed++;
            }
            foreach (KeyValuePair<Clan, Actor> pair in vanillaClans)
            {
                Actor leader = null;
                try { leader = pair.Key.getChief(); } catch { }
                LineageService.RenameClanByLeader(pair.Key,
                    leader ?? pair.Value);
            }
            return changed;
        }

        public static int RenameWholeShiTree(long pShiId, string pRawClanName)
        {
            return RenameVisibleMembers(null, pShiId, pModeIsBigTree: true, pRawClanName);
        }

        private static List<long> CollectShiTreeActorIds(long pShiId)
        {
            var archiveIds = new List<long>();
            var liveIds = new List<long>();
            if (pShiId < 0) return archiveIds;

            using (var cmd = new SQLiteCommand(DB))
            {
                cmd.CommandText =
                    $"SELECT ID FROM {ActorArchiveTableItem.GetTableName()} " +
                    $"WHERE SHI_ID=@sid ORDER BY BIRTH_TIME ASC, ID ASC";
                cmd.Parameters.AddWithValue("@sid", pShiId);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                while (reader.Read())
                    archiveIds.Add(reader.GetInt64(0));
            }

            var units = World.world?.units;
            if (units != null)
            {
                foreach (Actor unit in units)
                {
                    if (unit?.data == null || unit.isRekt()) continue;
                    unit.data.get(LineageKeys.SHI_ID, out long unitShiId, -1L);
                    if (unitShiId == pShiId)
                        liveIds.Add(unit.data.id);
                }
            }

            return VisibleClanRenameRules.MergeMemberIds(
                LineageQuery.GetShiBranchFounderId(pShiId), archiveIds, liveIds);
        }

        private static bool RenameActor(long pActorId, string pClanName)
        {
            bool changed = false;
            Actor live = FindActor(pActorId);
            if (live?.data != null && !live.isRekt())
            {
                ActorManualRenameService.ApplyExplicitClan(live, pClanName);
                LineageService.ArchiveActor(live, pAlive: live.isAlive());
                try { live.clearGraphicsFully(); } catch { }
                changed = true;
            }

            ActorArchiveTableItem row = LineageArchiveReader.ReadRow(pActorId);
            if (row != null)
            {
                UpdateArchivedActor(row, pClanName);
                changed = true;
            }

            return changed;
        }

        private static Actor FindActor(long pActorId)
        {
            try
            {
                return pActorId >= 0 ? World.world?.units?.get(pActorId) : null;
            }
            catch { return null; }
        }

        private static void UpdateBranchName(long pShiId, string pClanName)
        {
            HistoricalContentRevision.AdvanceAfterSuccessfulSynchronousWrite(
                () => DB.UpdateValue(ShiBranchTableItem.GetTableName(),
                    new List<SimpleColumnConstraint>
                    {
                        SimpleColumnConstraint.CreateEq("SHI_ID", pShiId)
                    },
                    ColumnVal.Create("CLAN_NAME", pClanName)));
            ShiBranchRuntimeMetadataCache.Invalidate(pShiId);
        }

        private static void UpdateArchivedActor(ActorArchiveTableItem pRow, string pClanName)
        {
            HistoricalContentRevision.AdvanceAfterSuccessfulSynchronousWrite(
                () => DB.UpdateValue(ActorArchiveTableItem.GetTableName(),
                    new List<SimpleColumnConstraint>
                    {
                        SimpleColumnConstraint.CreateEq("ID", pRow.id)
                    },
                    ColumnVal.Create("CLAN_NAME", pClanName),
                    ColumnVal.Create("DISPLAY_NAME",
                        BuildArchivedDisplayName(pRow, pClanName))));
        }

        private static string BuildArchivedDisplayName(ActorArchiveTableItem pRow, string pClanName)
        {
            string given = pRow.given_name ?? "";
            if (string.IsNullOrEmpty(given)) given = pRow.display_name ?? "";
            if (string.IsNullOrEmpty(given)) return "";

            return LineageDisplayNameRules.Build(given, pRow.family_name,
                pClanName, pRow.status == LineageStatus.NOBLE,
                pRow.sex == 0, pRow.name_integrated != 0);
        }
    }
}
