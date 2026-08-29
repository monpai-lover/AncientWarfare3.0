using System;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     史载双亲的 SQL 落地。三处必须一起改 —— live actor data、FamilyEdge、
    ///     ActorArchive.PARENT_ID_1/2 —— 因为 <see cref="LineageQuery.GetParentIds"/>
    ///     返回的是这三源的**并集**,漏一处旧双亲就会复活。
    ///
    ///     边键复用 <see cref="LineageBirthArchivePersistence.UpsertParentEdge"/>,
    ///     不自己算 EDGE_ID。
    /// </summary>
    internal static class HistoricalAncestorPersistence
    {
        private const string ActorArchive = "ActorArchive";

        /// <summary>
        ///     插入/更新一条合成祖先档案行。走出生归档的同一套 upsert,所以列处理
        ///     与真人档案完全一致(ARCHIVE_RESOLUTION 取表默认 "resolved",
        ///     合成祖先由 id 区间识别,不占额外列)。
        /// </summary>
        internal static void UpsertAncestor(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, ActorArchiveTableItem pAncestor,
            double pCreatedTime)
        {
            if (pAncestor == null) throw new ArgumentNullException(nameof(pAncestor));
            LineageBirthArchivePersistence.Execute(pDb, pTransaction,
                new LineageBirthArchiveWrite(pAncestor, -1L, -1L, pCreatedTime));
        }

        /// <summary>
        ///     把历史人物的档案双亲与亲子边改成史载双亲。
        ///     两个槽位都无条件写:传 -1 即删边,所以不会留下旧槽位的残边。
        ///
        ///     档案行不存在时 UPDATE 影响 0 行,这是允许的 —— 该人物还没入档,
        ///     等首次入档时 <see cref="LineageArchiveWriter"/> 会从 data 键上取到
        ///     同样的 id(见 HistoricalAncestorRules.ResolveArchiveParentId)。
        /// </summary>
        internal static void ApplyChildParents(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pChildId, long pFatherId,
            long pMotherId, long pChildLineageId, double pCreatedTime)
        {
            using (var update = new SQLiteCommand(pDb)
                   { Transaction = pTransaction })
            {
                update.CommandText = "UPDATE " + ActorArchive +
                    " SET PARENT_ID_1=@parent1,PARENT_ID_2=@parent2" +
                    " WHERE ID=@id";
                update.Parameters.AddWithValue("@parent1", pFatherId);
                update.Parameters.AddWithValue("@parent2", pMotherId);
                update.Parameters.AddWithValue("@id", pChildId);
                int affected = update.ExecuteNonQuery();
                if (affected > 1)
                    throw new InvalidOperationException(
                        "historical parentage update affected multiple rows");
            }

            LineageBirthArchivePersistence.UpsertParentEdge(pDb, pTransaction,
                pChildId, pFatherId, HistoricalAncestorRules.FatherSlot,
                pChildLineageId, pCreatedTime);
            LineageBirthArchivePersistence.UpsertParentEdge(pDb, pTransaction,
                pChildId, pMotherId, HistoricalAncestorRules.MotherSlot,
                pChildLineageId, pCreatedTime);
        }
    }
}
