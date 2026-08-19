using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class LineageDispositionService
    {
        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        public static bool TryGrantSurname(Kingdom pKingdom, Actor pRuler,
            Actor pTarget, out int pMigratedCount)
        {
            pMigratedCount = 0;
            if (!ValidActors(pKingdom, pRuler, pTarget) ||
                pRuler.clan?.data == null) return false;
            pRuler.data.get(LineageKeys.LINEAGE_ID,
                out long lineageId, -1L);
            pRuler.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            pRuler.data.get(LineageKeys.FAMILY_NAME,
                out string familyName, "");
            pRuler.data.get(LineageKeys.CLAN_NAME,
                out string clanName, "");
            if (lineageId < 0 || shiId < 0 ||
                string.IsNullOrWhiteSpace(familyName) ||
                string.IsNullOrWhiteSpace(clanName)) return false;
            pTarget.data.get(LineageKeys.SHI_ID, out long oldShiId, -1L);
            pTarget.data.get(LineageKeys.CLAN_NAME,
                out string oldClanName, "");

            List<Actor> migrants = LoadMigrants(pTarget);
            if (migrants.Count == 0) return false;
            long[] ids = ActorIds(migrants);
            if (!LineageDispositionPersistence.GrantSurname(DB, ids,
                    lineageId, shiId, familyName, clanName,
                    LineageService.CurTime())) return false;

            for (int i = 0; i < migrants.Count; i++)
            {
                Actor actor = migrants[i];
                actor.data.set(LineageKeys.FAMILY_NAME, familyName);
                actor.data.set(LineageKeys.LINEAGE_ID, lineageId);
                actor.data.set(LineageKeys.CLAN_NAME, clanName);
                actor.data.set(LineageKeys.SHI_ID, shiId);
                actor.data.set(LineageKeys.NOBLE_DISTANCE, 0);
                actor.data.set(LineageKeys.LINEAGE_STATUS,
                    LineageStatus.NOBLE);
                if (!actor.hasTrait(LineageKeys.TRAIT_GUIZU))
                    actor.addTrait(LineageKeys.TRAIT_GUIZU);
                if (actor.clan != pRuler.clan) actor.setClan(pRuler.clan);
                LineageService.ArchiveActor(actor, pAlive: true);
                CityShiInfluenceSnapshotService.MarkActorDirty(actor);
            }
            pMigratedCount = migrants.Count;
            HeirService.RefreshHeir(pKingdom);
            ChronicleEvents.OnCourtSurnameGranted(pKingdom, pRuler, pTarget,
                oldShiId, shiId, oldClanName, clanName);
            return true;
        }

        public static bool TryExpel(Kingdom pKingdom, Actor pRuler,
            Actor pTarget, out long pNewShiId, out int pMigratedCount)
        {
            pNewShiId = -1L;
            pMigratedCount = 0;
            if (!ValidActors(pKingdom, pRuler, pTarget)) return false;
            pTarget.data.get(LineageKeys.LINEAGE_ID,
                out long lineageId, -1L);
            pTarget.data.get(LineageKeys.SHI_ID, out long oldShiId, -1L);
            pTarget.data.get(LineageKeys.CLAN_NAME,
                out string clanName, "");
            if (lineageId < 0 || oldShiId < 0 ||
                string.IsNullOrWhiteSpace(clanName)) return false;

            List<Actor> migrants = LoadMigrants(pTarget);
            if (migrants.Count == 0) return false;
            long newShiId = LineageIdAllocator.NextShiId();
            if (newShiId < 0) return false;
            long[] ids = ActorIds(migrants);
            if (!LineageDispositionPersistence.Expel(DB, ids, newShiId,
                    lineageId, oldShiId, clanName, pTarget.data.id,
                    pKingdom.id, pTarget.city?.data?.id ?? -1L,
                    pTarget.clan?.data?.id ?? -1L, Date.getCurrentYear(),
                    LineageService.CurTime())) return false;

            Clan independentClan = null;
            try
            {
                independentClan = World.world?.clans?.newClan(pTarget,
                    pAddDefaultTraits: true);
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Expelled clan materialization failed: " +
                                    exception.Message);
            }

            for (int i = 0; i < migrants.Count; i++)
            {
                Actor actor = migrants[i];
                actor.data.set(LineageKeys.CLAN_NAME, clanName);
                actor.data.set(LineageKeys.SHI_ID, newShiId);
                actor.data.set(LineageKeys.NOBLE_DISTANCE, 99);
                actor.data.set(LineageKeys.LINEAGE_STATUS,
                    LineageStatus.COMMON);
                actor.data.set(LineageKeys.IS_HEIR, false);
                if (actor.hasTrait(LineageKeys.TRAIT_GUIZU))
                    actor.removeTrait(LineageKeys.TRAIT_GUIZU);
                NobleRankService.ClearRevokedProjection(actor);
                if (independentClan?.data != null &&
                    actor.clan != independentClan)
                    actor.setClan(independentClan);
                LineageService.ArchiveActor(actor, pAlive: true);
                CityShiInfluenceSnapshotService.MarkActorDirty(actor);
            }
            if (independentClan?.data != null)
                LineageService.RenameClanByLeader(independentClan, pTarget);
            ShiBranchRuntimeMetadataCache.Invalidate(newShiId);
            pNewShiId = newShiId;
            pMigratedCount = migrants.Count;
            HeirService.RefreshHeir(pKingdom);
            ChronicleEvents.OnCourtLineageExpelled(pKingdom, pRuler,
                pTarget, oldShiId, newShiId, clanName, clanName);
            return true;
        }

        private static bool ValidActors(Kingdom pKingdom, Actor pRuler,
            Actor pTarget)
        {
            return DB != null && pKingdom?.data != null &&
                   pRuler?.data != null && pTarget?.data != null &&
                   !pKingdom.isRekt() && pKingdom.king == pRuler &&
                   pRuler != pTarget && pTarget.isAlive() &&
                   !pTarget.isRekt();
        }

        private static List<Actor> LoadMigrants(Actor pRoot)
        {
            var facts = new List<LineageDispositionCandidate>(
                LineageDispositionRules.MaximumMigrants);
            var actors = new Dictionary<long, Actor>();
            var visited = new HashSet<long>();
            var queue = new Queue<long>();
            actors[pRoot.data.id] = pRoot;
            facts.Add(Fact(pRoot, -1L));
            visited.Add(pRoot.data.id);
            if (pRoot.isSexMale()) queue.Enqueue(pRoot.data.id);

            while (queue.Count > 0 &&
                   facts.Count < LineageDispositionRules.MaximumMigrants)
            {
                long parentId = queue.Dequeue();
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT E.CHILD_ID," +
                    "IFNULL(A.SEX,-1),IFNULL(A.IS_ALIVE,0) FROM " +
                    FamilyEdgeTableItem.GetTableName() + " E LEFT JOIN " +
                    ActorArchiveTableItem.GetTableName() +
                    " A ON A.ID=E.CHILD_ID WHERE E.PARENT_ID=@parent" +
                    " ORDER BY E.CREATED_TIME,E.CHILD_ID LIMIT @limit";
                command.Parameters.AddWithValue("@parent", parentId);
                command.Parameters.AddWithValue("@limit",
                    LineageDispositionRules.MaximumMigrants - facts.Count);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read() &&
                       facts.Count < LineageDispositionRules.MaximumMigrants)
                {
                    long childId = reader.GetInt64(0);
                    if (!visited.Add(childId)) continue;
                    Actor child = World.world?.units?.get(childId);
                    bool alive = child?.data != null && child.isAlive() &&
                                 !child.isRekt();
                    int sex = alive
                        ? child.isSexMale() ? 0 : 1
                        : reader.GetInt32(1);
                    bool married = alive && child.hasLover();
                    facts.Add(new LineageDispositionCandidate(childId,
                        parentId, sex, alive, married));
                    if (alive) actors[childId] = child;
                    if (sex == 0) queue.Enqueue(childId);
                }
            }

            IReadOnlyList<long> selected =
                LineageDispositionRules.SelectMigrants(facts,
                    pRoot.data.id,
                    LineageDispositionRules.MaximumMigrants);
            var result = new List<Actor>(selected.Count);
            for (int i = 0; i < selected.Count; i++)
                if (actors.TryGetValue(selected[i], out Actor actor))
                    result.Add(actor);
            return result;
        }

        private static LineageDispositionCandidate Fact(Actor pActor,
            long pFatherId)
        {
            return new LineageDispositionCandidate(pActor.data.id, pFatherId,
                pActor.isSexMale() ? 0 : 1, pActor.isAlive() &&
                !pActor.isRekt(), pActor.hasLover());
        }

        private static long[] ActorIds(IReadOnlyList<Actor> pActors)
        {
            var ids = new long[pActors.Count];
            for (int i = 0; i < pActors.Count; i++)
                ids[i] = pActors[i].data.id;
            return ids;
        }
    }
}
