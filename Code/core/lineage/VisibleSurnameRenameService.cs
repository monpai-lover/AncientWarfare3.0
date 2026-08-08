using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.naming;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    internal static class VisibleSurnameRenameService
    {
        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        private static bool Ready => DB != null &&
            LineageArchiveManager.Instance.InitializeSuccessful;

        public static int RenamePatrilinealBranch(long pRootActorId,
            string pRawFamilyName)
        {
            if (!Ready || pRootActorId < 0 ||
                !VisibleSurnameRenameRules.TryNormalizeFamilyName(
                    pRawFamilyName, out string familyName)) return 0;

            Actor root = FindActor(pRootActorId);
            if (root?.data != null && !root.isRekt())
                LineageService.ArchiveActor(root, pAlive: root.isAlive());

            List<SurnameRelationNode> relations = LoadArchivedRelations(
                pRootActorId);
            MergeLivingRelations(relations, pRootActorId);
            IReadOnlyList<long> ids =
                VisibleSurnameRenameRules.CollectPatrilinealRenameIds(
                    pRootActorId, relations);
            if (ids.Count == 0) return 0;

            int changed = 0;
            for (int i = 0; i < ids.Count; i++)
                if (RenameActor(ids[i], familyName)) changed++;
            return changed;
        }

        private static List<SurnameRelationNode> LoadArchivedRelations(
            long pRootActorId)
        {
            var result = new List<SurnameRelationNode>();
            using var command = new SQLiteCommand(
                VisibleSurnameRenameSqlRules.DescendantRelationQuery, DB);
            command.Parameters.AddWithValue("@root", pRootActorId);
            command.Parameters.AddWithValue("@maxDepth",
                VisibleSurnameRenameSqlRules.MaxDepth);
            command.Parameters.AddWithValue("@limit",
                VisibleSurnameRenameRules.MaxRenameActors);
            using var reader = (SQLiteDataReader)command.ExecuteReader();
            while (reader.Read())
                result.Add(new SurnameRelationNode(reader.GetInt64(0),
                    reader.GetInt32(1) == 0, reader.GetInt64(2)));
            return result;
        }

        private static void MergeLivingRelations(
            List<SurnameRelationNode> pRelations, long pRootActorId)
        {
            if (pRelations == null || World.world?.units == null) return;
            var known = new HashSet<long>();
            for (int i = 0; i < pRelations.Count; i++)
                known.Add(pRelations[i].ActorId);

            var pending = new Queue<Actor>();
            Actor root = FindActor(pRootActorId);
            if (root?.data != null && !root.isRekt()) pending.Enqueue(root);
            var visited = new HashSet<long>();
            while (pending.Count > 0 &&
                   visited.Count < VisibleSurnameRenameRules.MaxRenameActors)
            {
                Actor actor = pending.Dequeue();
                if (actor?.data == null || actor.isRekt() ||
                    !visited.Add(actor.data.id)) continue;
                if (known.Add(actor.data.id))
                    pRelations.Add(new SurnameRelationNode(actor.data.id,
                        actor.isSexMale(), ResolveLivingFatherId(actor)));
                if (!actor.isSexMale()) continue;
                try
                {
                    foreach (Actor child in actor.getChildren(
                                 pOnlyCurrentFamily: false))
                        if (child?.data != null && !child.isRekt())
                            pending.Enqueue(child);
                }
                catch { }
            }
        }

        private static long ResolveLivingFatherId(Actor pActor)
        {
            if (pActor?.data == null) return -1L;
            long first = pActor.data.parent_id_1;
            if (IsMaleActor(first)) return first;
            long second = pActor.data.parent_id_2;
            return IsMaleActor(second) ? second : -1L;
        }

        private static bool IsMaleActor(long pActorId)
        {
            Actor live = FindActor(pActorId);
            if (live?.data != null) return live.isSexMale();
            ActorArchiveTableItem row = LineageArchiveReader.ReadRow(pActorId);
            return row != null && row.sex == 0;
        }

        private static bool RenameActor(long pActorId, string pFamilyName)
        {
            bool changed = false;
            Actor live = FindActor(pActorId);
            if (live?.data != null && !live.isRekt())
            {
                live.data.set(LineageKeys.FAMILY_NAME, pFamilyName);
                live.data.set(LineageKeys.CHINESE_FAMILY_NAME, pFamilyName);
                live.data.set(AWNameDataKeys.FamilyComponent, pFamilyName);
                ActorManualRenameService.ApplyInheritedFamily(live,
                    pFamilyName);
                LineageService.ArchiveActor(live, pAlive: live.isAlive());
                try { live.clearGraphicsFully(); } catch { }
                changed = true;
            }

            ActorArchiveTableItem row = LineageArchiveReader.ReadRow(pActorId);
            if (row == null) return changed;
            string displayName = BuildArchivedDisplayName(row, pFamilyName);
            HistoricalContentRevision.AdvanceAfterSuccessfulSynchronousWrite(
                () => DB.UpdateValue(
                    ActorArchiveTableItem.GetTableName(),
                    new List<SimpleColumnConstraint>
                    {
                        SimpleColumnConstraint.CreateEq("ID", pActorId)
                    },
                    ColumnVal.Create("FAMILY_NAME", pFamilyName),
                    ColumnVal.Create("DISPLAY_NAME", displayName)));
            return true;
        }

        private static string BuildArchivedDisplayName(
            ActorArchiveTableItem pRow, string pFamilyName)
        {
            string given = pRow?.given_name ?? "";
            if (string.IsNullOrEmpty(given)) given = pRow?.display_name ?? "";
            if (string.IsNullOrEmpty(given)) return "";
            return LineageDisplayNameRules.Build(given, pFamilyName,
                pRow?.clan_name ?? "", pRow?.status == LineageStatus.NOBLE,
                pRow?.sex == 0, pRow?.name_integrated != 0);
        }

        private static Actor FindActor(long pActorId)
        {
            try
            {
                return pActorId >= 0 ? World.world?.units?.get(pActorId) : null;
            }
            catch { return null; }
        }
    }
}
