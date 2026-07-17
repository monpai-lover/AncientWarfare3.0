using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class RulerAppellationService
    {
        private static readonly Dictionary<long, string> CompactByKingdom =
            new Dictionary<long, string>();
        private static readonly Dictionary<long, string> PosthumousByActor =
            new Dictionary<long, string>();
        private static readonly Dictionary<long, string> PosthumousByReign =
            new Dictionary<long, string>();
        private static readonly Dictionary<long, string> RetrospectiveRelationByActor =
            new Dictionary<long, string>();
        private static readonly Dictionary<long, FamilyShiProjection> ShiProjectionById =
            new Dictionary<long, FamilyShiProjection>();

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null &&
                                     LineageArchiveManager.Instance.InitializeSuccessful;

        public static string GetFullLivingAppellation(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return "";
            bool rebel = MandateRebelService.IsRebelKingdom(pKingdom);
            bool republic = RepublicGovernmentService.IsRepublic(pKingdom);
            if (!RulerAppellationRules.ShouldProjectLiving(
                    LineageService.IsXiaKingdom(pKingdom),
                    XiaizationService.UsesXiaizedInstitutionSystem(pKingdom),
                    rebel, republic)) return "";
            if (rebel)
                return BuildCompact(pKingdom);
            if (republic)
                return RulerAppellationRules.LivingRepublic();

            string stateName = pKingdom.name ?? "";
            RulerRank rank = MapRank(KingdomTitleService.GetTitle(pKingdom));
            if (rank != RulerRank.Emperor)
                return RulerAppellationRules.LivingRanked(stateName, rank);
            EffectiveChronology chronology = YearNameService.GetEffectiveChronology(pKingdom);
            return RulerAppellationRules.LivingEmperor(
                stateName, chronology.EraName);
        }

        public static string GetCompactLivingAppellation(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            return CompactByKingdom.TryGetValue(pKingdom.id, out string value)
                ? value
                : "";
        }

        public static string GetPosthumousAppellation(long pActorId,
            long pReignId = -1)
        {
            if (pReignId >= 0 && PosthumousByReign.TryGetValue(
                    pReignId, out string reignCached)) return reignCached;
            if (pReignId < 0 && PosthumousByActor.TryGetValue(
                    pActorId, out string actorCached)) return actorCached;
            if (!Ready || pActorId < 0 && pReignId < 0) return "";

            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT REIGN_ID,ACTOR_ID,IFNULL(FULL_TITLE,'')," +
                                      "IFNULL(RETROSPECTIVE_RELATION,'') FROM " +
                                      PosthumousTitleTableItem.GetTableName() +
                                      (pReignId >= 0
                                          ? " WHERE REIGN_ID=@reign"
                                          : " WHERE ACTOR_ID=@actor") +
                                      " ORDER BY DECIDED_TIME DESC,RECORD_ID DESC LIMIT 1";
                command.Parameters.AddWithValue("@reign", pReignId);
                command.Parameters.AddWithValue("@actor", pActorId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return "";
                long reignId = reader.IsDBNull(0) ? -1 : reader.GetInt64(0);
                long actorId = reader.IsDBNull(1) ? -1 : reader.GetInt64(1);
                string title = reader.IsDBNull(2) ? "" : reader.GetString(2);
                string relation = reader.IsDBNull(3) ? "" : reader.GetString(3);
                if (string.IsNullOrWhiteSpace(title)) return "";
                if (reignId >= 0) PosthumousByReign[reignId] = title;
                if (actorId >= 0)
                {
                    PosthumousByActor[actorId] = title;
                    if (!string.IsNullOrEmpty(relation))
                        RetrospectiveRelationByActor[actorId] = relation;
                }
                return title;
            }
            catch
            {
                return "";
            }
        }

        public static EffectiveChronology GetEffectiveChronology(Kingdom pKingdom)
        {
            return YearNameService.GetEffectiveChronology(pKingdom);
        }

        public static void ProjectCommittedTitle(Kingdom pKingdom, Actor pActor,
            RulerTitleCommitResult pCommitted)
        {
            if (!pCommitted.Success || string.IsNullOrWhiteSpace(
                    pCommitted.DisplayTitle)) return;
            if (pActor?.data != null)
                PosthumousByActor[pActor.data.id] = pCommitted.DisplayTitle;
            RefreshLivingProjection(pKingdom);
        }

        public static void ProjectCommittedTitle(long pActorId,
            RulerTitleCommitResult pCommitted, string pRetrospectiveRelation)
        {
            if (!pCommitted.Success || pActorId < 0 ||
                string.IsNullOrWhiteSpace(pCommitted.DisplayTitle)) return;
            PosthumousByActor[pActorId] = pCommitted.DisplayTitle;
            if (!string.IsNullOrWhiteSpace(pRetrospectiveRelation))
                RetrospectiveRelationByActor[pActorId] =
                    pRetrospectiveRelation.Trim();
        }

        public static void EnrichFamilyTreeNode(FamilyTreeNode pNode)
        {
            if (pNode == null) return;
            Actor actor = null;
            try { actor = World.world?.units?.get(pNode.id); }
            catch { actor = null; }
            if (actor?.data != null && actor.isAlive() && !actor.isRekt() &&
                actor.kingdom?.king == actor)
            {
                pNode.ritual_appellation = GetFullLivingAppellation(actor.kingdom);
            }
            else if (PosthumousByActor.TryGetValue(pNode.id, out string title))
            {
                pNode.ritual_appellation = title;
            }
            if (RetrospectiveRelationByActor.TryGetValue(
                    pNode.id, out string relation))
                pNode.retrospective_relation = relation;

            FamilyShiProjection shi = ReadFamilyShiProjection(pNode.shi_id);
            if (shi == null) return;
            pNode.parent_shi_display = shi.ParentDisplay;
            pNode.root_shi_display = shi.RootDisplay;
            pNode.origin_city_name = shi.OriginCityName;
            pNode.state_name = shi.StateName;
            if (string.IsNullOrEmpty(pNode.branch_display))
                pNode.branch_display = shi.Display;
        }

        public static void RefreshLivingProjection(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            bool rebel = MandateRebelService.IsRebelKingdom(pKingdom);
            bool republic = RepublicGovernmentService.IsRepublic(pKingdom);
            if (!RulerAppellationRules.ShouldProjectLiving(
                    LineageService.IsXiaKingdom(pKingdom),
                    XiaizationService.UsesXiaizedInstitutionSystem(pKingdom),
                    rebel, republic))
            {
                CompactByKingdom.Remove(pKingdom.id);
                return;
            }
            CompactByKingdom[pKingdom.id] = BuildCompact(pKingdom);
        }

        public static void RemoveKingdom(long pKingdomId)
        {
            if (pKingdomId >= 0) CompactByKingdom.Remove(pKingdomId);
        }

        public static void RebuildLivingCache()
        {
            CompactByKingdom.Clear();
            RebuildPosthumousCache();
            if (World.world?.kingdoms == null) return;
            foreach (Kingdom kingdom in World.world.kingdoms)
                if (kingdom?.data != null && !kingdom.isRekt())
                    RefreshLivingProjection(kingdom);
        }

        public static void ClearRuntime()
        {
            CompactByKingdom.Clear();
            PosthumousByActor.Clear();
            PosthumousByReign.Clear();
            RetrospectiveRelationByActor.Clear();
            ShiProjectionById.Clear();
        }

        private static string BuildCompact(Kingdom pKingdom)
        {
            string stateName = pKingdom?.name ?? "";
            string suffix = KingdomTitleDisplayRules.GetNameplateTitleSuffix(
                (int)KingdomTitleService.GetTitle(pKingdom),
                MandateService.IsRuntimeMandateKingdom(pKingdom),
                MandateRebelService.IsRebelKingdom(pKingdom),
                RepublicGovernmentService.IsRepublic(pKingdom));
            return stateName + suffix;
        }

        private static RulerRank MapRank(KingdomTitle pTitle)
        {
            return pTitle switch
            {
                KingdomTitle.Baron => RulerRank.Bo,
                KingdomTitle.Marquis => RulerRank.Hou,
                KingdomTitle.Duke => RulerRank.Gong,
                KingdomTitle.King => RulerRank.King,
                KingdomTitle.Emperor => RulerRank.Emperor,
                _ => RulerRank.Bo
            };
        }

        private static void RebuildPosthumousCache()
        {
            PosthumousByActor.Clear();
            PosthumousByReign.Clear();
            RetrospectiveRelationByActor.Clear();
            ShiProjectionById.Clear();
            if (!Ready) return;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT IFNULL(REIGN_ID,-1)," +
                                      "IFNULL(ACTOR_ID,-1),IFNULL(FULL_TITLE,'')," +
                                      "IFNULL(RETROSPECTIVE_RELATION,'') FROM " +
                                      PosthumousTitleTableItem.GetTableName() +
                                      " ORDER BY DECIDED_TIME,RECORD_ID";
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long reignId = reader.GetInt64(0);
                    long actorId = reader.GetInt64(1);
                    string title = reader.GetString(2);
                    string relation = reader.GetString(3);
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    if (reignId >= 0) PosthumousByReign[reignId] = title;
                    if (actorId < 0) continue;
                    PosthumousByActor[actorId] = title;
                    if (!string.IsNullOrWhiteSpace(relation))
                        RetrospectiveRelationByActor[actorId] = relation;
                }
            }
            catch
            {
                PosthumousByActor.Clear();
                PosthumousByReign.Clear();
                RetrospectiveRelationByActor.Clear();
            }
        }

        private static FamilyShiProjection ReadFamilyShiProjection(long pShiId)
        {
            if (pShiId < 0 || !Ready) return null;
            if (ShiProjectionById.TryGetValue(pShiId, out FamilyShiProjection cached))
                return cached;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText =
                    "WITH RECURSIVE chain(SHI_ID,LINEAGE_ID,CLAN_NAME," +
                    "PARENT_SHI_ID,STATE_NAME,FOUNDER_ACTOR_ID,ORIGIN_CITY_ID,DEPTH) AS (" +
                    "SELECT SHI_ID,LINEAGE_ID,IFNULL(CLAN_NAME,'')," +
                    "IFNULL(PARENT_SHI_ID,-1),IFNULL(STATE_NAME,'')," +
                    "IFNULL(FOUNDER_ACTOR_ID,-1),IFNULL(ORIGIN_CITY_ID,-1),0 FROM " +
                    ShiBranchTableItem.GetTableName() + " WHERE SHI_ID=@shi " +
                    "UNION ALL SELECT p.SHI_ID,p.LINEAGE_ID,IFNULL(p.CLAN_NAME,'')," +
                    "IFNULL(p.PARENT_SHI_ID,-1),IFNULL(p.STATE_NAME,'')," +
                    "IFNULL(p.FOUNDER_ACTOR_ID,-1),IFNULL(p.ORIGIN_CITY_ID,-1),c.DEPTH+1 " +
                    "FROM " + ShiBranchTableItem.GetTableName() +
                    " p JOIN chain c ON p.SHI_ID=c.PARENT_SHI_ID WHERE c.DEPTH<63) " +
                    "SELECT c.SHI_ID,c.LINEAGE_ID,c.CLAN_NAME,c.PARENT_SHI_ID," +
                    "c.STATE_NAME,c.ORIGIN_CITY_ID,c.DEPTH," +
                    "IFNULL((SELECT a.CITY_NAME FROM " +
                    ActorArchiveTableItem.GetTableName() +
                    " a WHERE a.ID=c.FOUNDER_ACTOR_ID LIMIT 1),'') " +
                    "FROM chain c ORDER BY c.DEPTH";
                command.Parameters.AddWithValue("@shi", pShiId);
                var chain = new List<FamilyShiProjection>();
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long originCityId = reader.GetInt64(5);
                    string archivedCity = reader.IsDBNull(7) ? "" : reader.GetString(7);
                    string cityName = ResolveOriginCityName(originCityId, archivedCity);
                    string clanName = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    chain.Add(new FamilyShiProjection
                    {
                        ShiId = reader.GetInt64(0),
                        ParentShiId = reader.GetInt64(3),
                        StateName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                        OriginCityName = cityName,
                        Display = ShiBranchRules.BuildDisplayName(cityName, clanName)
                    });
                }
                if (chain.Count == 0) return null;
                FamilyShiProjection root = chain[chain.Count - 1];
                for (int i = 0; i < chain.Count; i++)
                {
                    FamilyShiProjection item = chain[i];
                    item.ParentDisplay = i + 1 < chain.Count
                        ? chain[i + 1].Display
                        : "";
                    item.RootDisplay = root.Display;
                    ShiProjectionById[item.ShiId] = item;
                }
                return ShiProjectionById[pShiId];
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveOriginCityName(long pCityId,
            string pArchivedName)
        {
            try
            {
                City city = pCityId < 0 ? null : World.world?.cities?.get(pCityId);
                if (!string.IsNullOrEmpty(city?.data?.name)) return city.data.name;
            }
            catch { }
            return pArchivedName ?? "";
        }

        private sealed class FamilyShiProjection
        {
            public long ShiId = -1;
            public long ParentShiId = -1;
            public string Display = "";
            public string ParentDisplay = "";
            public string RootDisplay = "";
            public string OriginCityName = "";
            public string StateName = "";
        }
    }
}
