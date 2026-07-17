using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class RetrospectiveTitleService
    {
        private readonly struct AncestorArchive
        {
            public readonly long ActorId;
            public readonly string Name;
            public readonly string Color;
            public readonly int Age;

            public AncestorArchive(long pActorId, string pName, string pColor, int pAge)
            {
                ActorId = pActorId;
                Name = pName ?? "";
                Color = pColor ?? "";
                Age = pAge;
            }
        }

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

        public static void TryAwardFirstImperialAncestors(Kingdom pKingdom, Actor pEmperor,
            long pShiId, long pDynastyId)
        {
            if (!Ready || pKingdom?.data == null || pEmperor?.data == null || pShiId < 0)
                return;
            if (HasEarlierImperialRuler(pShiId)) return;

            long fatherId = ResolveMaleParent(pEmperor.data.id);
            if (fatherId < 0) return;
            TryAward(pKingdom, pEmperor, pShiId, pDynastyId, fatherId, "father");

            long grandfatherId = ResolveMaleParent(fatherId);
            if (grandfatherId >= 0)
            {
                TryAward(pKingdom, pEmperor, pShiId, pDynastyId,
                    grandfatherId, "paternal_grandfather");
            }
        }

        private static void TryAward(Kingdom pKingdom, Actor pEmperor,
            long pShiId, long pDynastyId, long pActorId, string pRelation)
        {
            if (HasFormalReignTitle(pActorId) || HasRetrospectiveTitle(pShiId, pActorId)) return;
            if (!TryReadAncestor(pActorId, out AncestorArchive ancestor)) return;
            if (!RulerTitleFactService.TryReadPersonalSnapshot(
                    pActorId, out RulerPersonalFacts personal)) return;

            int civil = personal.Diplomacy + personal.Stewardship + personal.Intelligence;
            int martial = personal.Warfare * 2 + personal.Combat;
            bool healthy = personal.Health >= 10 &&
                           (personal.Traits & (RulerTraitFlags.Weak |
                                               RulerTraitFlags.FragileHealth |
                                               RulerTraitFlags.Crippled)) == 0;
            string preferred = TempleTitleRules.SelectRetrospectiveAncestor(
                civil, martial, healthy);
            int cycleNo = DynastyTitleRegistryService.ReadLatestCycle(pShiId, "temple");
            HashSet<string> used = DynastyTitleRegistryService.ReadUsed(
                pShiId, "temple", cycleNo);
            string temple = FirstUnusedRetrospective(preferred, used);
            if (string.IsNullOrEmpty(temple)) return;

            ShiBranchInfo branch = LineageQuery.GetShiBranchInfo(pShiId);
            string stateName = string.IsNullOrEmpty(branch?.state_name)
                ? pKingdom.name ?? ""
                : branch.state_name;
            string kingdomColor = HistoryColors.FromKingdom(pKingdom);
            var facts = new RulerTitleFacts
            {
                ActorId = ancestor.ActorId,
                ActorName = ancestor.Name,
                Age = ancestor.Age,
                KingdomId = pKingdom.id,
                ReignId = -1,
                ShiId = pShiId,
                DynastyId = pDynastyId,
                StateName = stateName,
                KingdomColor = string.IsNullOrEmpty(kingdomColor)
                    ? ancestor.Color
                    : kingdomColor,
                Diplomacy = personal.Diplomacy,
                Warfare = personal.Warfare,
                Stewardship = personal.Stewardship,
                Intelligence = personal.Intelligence,
                Health = personal.Health,
                Combat = personal.Combat,
                Traits = personal.Traits
            };
            RulerTitleDecision decision = RulerTitleDecision.ForRetrospective(
                facts, temple, cycleNo, pRelation);
            HistoryText history = HistoryText.Actor(pEmperor) +
                                  HistoryText.PlainText("追尊" + RelationLabel(pRelation) +
                                                        ancestor.Name + "为") +
                                  HistoryText.Colored(temple, facts.KingdomColor);
            double now = LineageService.CurTime();
            decision.HistoryPlain = history.Plain;
            decision.HistoryRich = history.Rich;
            decision.YearPrefix = HistoryWriter.BuildYearPrefix(now, pKingdom);
            decision.YearPrefixRich = HistoryWriter.BuildYearPrefixRich(now, pKingdom);
            decision.Reason = "retrospective_relation=" + pRelation;
            RulerTitleCommitService.Commit(decision);
        }

        private static long ResolveMaleParent(long pChildId)
        {
            if (!Ready || pChildId < 0) return -1;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT edge.PARENT_ID FROM " +
                                      FamilyEdgeTableItem.GetTableName() + " edge JOIN " +
                                      ActorArchiveTableItem.GetTableName() +
                                      " parent ON parent.ID=edge.PARENT_ID " +
                                      "WHERE edge.CHILD_ID=@child AND parent.SEX=0 " +
                                      "ORDER BY edge.PARENT_SLOT ASC LIMIT 1";
                command.Parameters.AddWithValue("@child", pChildId);
                object value = command.ExecuteScalar();
                if (value != null && value != DBNull.Value) return Convert.ToInt64(value);
            }
            catch { }
            return -1;
        }

        private static bool TryReadAncestor(long pActorId, out AncestorArchive pAncestor)
        {
            pAncestor = default;
            if (!Ready || pActorId < 0 || IsLivingActor(pActorId)) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT IFNULL(DISPLAY_NAME,'')," +
                                      "IFNULL(KINGDOM_COLOR,''),IFNULL(BIRTH_TIME,0)," +
                                      "IFNULL(DEATH_TIME,-1),IFNULL(IS_ALIVE,1),IFNULL(SEX,1) FROM " +
                                      ActorArchiveTableItem.GetTableName() +
                                      " WHERE ID=@actor LIMIT 1";
                command.Parameters.AddWithValue("@actor", pActorId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read() || ValueInt(reader, 4) != 0 || ValueInt(reader, 5) != 0)
                    return false;
                double birth = ValueDouble(reader, 2);
                double death = ValueDouble(reader, 3);
                int age = death < 0 ? -1 : Math.Max(0, Date.getYear(death) - Date.getYear(birth));
                pAncestor = new AncestorArchive(pActorId,
                    ValueString(reader, 0), ValueString(reader, 1), age);
                return !string.IsNullOrEmpty(pAncestor.Name);
            }
            catch { return false; }
        }

        private static bool IsLivingActor(long pActorId)
        {
            try
            {
                Actor actor = World.world?.units?.get(pActorId);
                return actor?.data != null && !actor.isRekt() && actor.isAlive();
            }
            catch { return false; }
        }

        private static bool HasEarlierImperialRuler(long pShiId)
        {
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT 1 FROM " + KingdomReignTableItem.GetTableName() +
                                      " WHERE SHI_ID=@shi AND HIGHEST_TITLE>=4 LIMIT 1";
                command.Parameters.AddWithValue("@shi", pShiId);
                return command.ExecuteScalar() != null;
            }
            catch { return false; }
        }

        private static bool HasFormalReignTitle(long pActorId)
        {
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT 1 FROM " + PosthumousTitleTableItem.GetTableName() +
                                      " WHERE ACTOR_ID=@actor AND IS_RETROSPECTIVE=0 LIMIT 1";
                command.Parameters.AddWithValue("@actor", pActorId);
                return command.ExecuteScalar() != null;
            }
            catch { return true; }
        }

        private static bool HasRetrospectiveTitle(long pShiId, long pActorId)
        {
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT 1 FROM " + PosthumousTitleTableItem.GetTableName() +
                                      " WHERE SHI_ID=@shi AND ACTOR_ID=@actor " +
                                      "AND IS_RETROSPECTIVE=1 LIMIT 1";
                command.Parameters.AddWithValue("@shi", pShiId);
                command.Parameters.AddWithValue("@actor", pActorId);
                return command.ExecuteScalar() != null;
            }
            catch { return true; }
        }

        private static string FirstUnusedRetrospective(string pPreferred, HashSet<string> pUsed)
        {
            string[] order = pPreferred switch
            {
                "宣祖" => new[] { "宣祖", "德祖", "景祖" },
                "景祖" => new[] { "景祖", "德祖", "宣祖" },
                _ => new[] { "德祖", "宣祖", "景祖" }
            };
            foreach (string value in order)
                if (pUsed == null || !pUsed.Contains(value)) return value;
            return "";
        }

        private static string RelationLabel(string pRelation)
        {
            return pRelation == "paternal_grandfather" ? "父系祖父" : "父亲";
        }

        private static string ValueString(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? "" : Convert.ToString(pReader.GetValue(pIndex)) ?? "";
        }

        private static int ValueInt(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? 0 : Convert.ToInt32(pReader.GetValue(pIndex));
        }

        private static double ValueDouble(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? 0.0 : Convert.ToDouble(pReader.GetValue(pIndex));
        }
    }
}
