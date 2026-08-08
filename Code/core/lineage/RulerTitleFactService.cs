using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.schools;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    internal static class RulerTitleFactService
    {
        private static readonly KeyValuePair<string, RulerTraitFlags>[] TraitMap =
        {
            T("ambitious", RulerTraitFlags.Ambitious),
            T("content", RulerTraitFlags.Content),
            T("honest", RulerTraitFlags.Honest),
            T("deceitful", RulerTraitFlags.Deceitful),
            T("greedy", RulerTraitFlags.Greedy),
            T("lustful", RulerTraitFlags.Lustful),
            T("gluttonous", RulerTraitFlags.Gluttonous),
            T("paranoid", RulerTraitFlags.Paranoid),
            T("peaceful", RulerTraitFlags.Peaceful),
            T("evil", RulerTraitFlags.Evil),
            T("psychopath", RulerTraitFlags.Psychopath),
            T("bloodlust", RulerTraitFlags.Bloodlust),
            T("strong", RulerTraitFlags.Strong),
            T("weak", RulerTraitFlags.Weak),
            T("fragile_health", RulerTraitFlags.FragileHealth),
            T("genius", RulerTraitFlags.Genius),
            T("wise", RulerTraitFlags.Wise),
            T("stupid", RulerTraitFlags.Stupid),
            T("veteran", RulerTraitFlags.Veteran),
            T("kingslayer", RulerTraitFlags.Kingslayer),
            T("madness", RulerTraitFlags.Madness),
            T("attractive", RulerTraitFlags.Attractive),
            T("hotheaded", RulerTraitFlags.Hotheaded),
            T("patient", RulerTraitFlags.Patient),
            T("compassionate", RulerTraitFlags.Compassionate),
            T("generous", RulerTraitFlags.Generous),
            T("diligent", RulerTraitFlags.Diligent),
            T("just", RulerTraitFlags.Just),
            T("tough", RulerTraitFlags.Tough),
            T("fertile", RulerTraitFlags.Fertile),
            T("cruel", RulerTraitFlags.Cruel),
            T("crippled", RulerTraitFlags.Crippled),
            T("slow", RulerTraitFlags.Slow),
            T("strong_minded", RulerTraitFlags.StrongMinded),
            T("pacifist", RulerTraitFlags.Pacifist)
        };

        private static readonly string[] ExcludedStatTraits =
        {
            MandateService.TRAIT_TIANMING,
            "first",
            LineageKeys.TRAIT_ZHUHOU,
            LineageKeys.TRAIT_GUIZU,
            LineageKeys.TRAIT_GENERAL,
            LineageKeys.TRAIT_ARMY_COMMANDER,
            LineageKeys.TRAIT_GUARD,
            LineageKeys.TRAIT_FIEF_SOLDIER,
            LineageKeys.MANDATE_REBEL,
            LineageKeys.MANDATE_REBEL_LEADER,
            LineageKeys.TEMPORARY_LEVY,
            LineageKeys.TEMPORARY_SLAVE_VANGUARD_MEMBER
        };

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

        public static RulerTitleFacts BuildAtReignEnd(Kingdom pKingdom, Actor pActor,
            ReignRecordWriter.ReignInfo pReign, string pEndReason)
        {
            var facts = new RulerTitleFacts();
            if (pActor?.data == null ||
                !RulerTitleFactRules.CanBuildReignSnapshot(
                    pReign.ReignId, pReign.KingdomId)) return facts;

            double endTime = pReign.EndTime > 0 ? pReign.EndTime : LineageService.CurTime();
            facts.ActorId = pActor.data.id;
            facts.KingdomId = pReign.KingdomId >= 0 ? pReign.KingdomId : pKingdom?.id ?? -1L;
            facts.ReignId = pReign.ReignId;
            facts.ShiId = pReign.ShiId;
            if (facts.ShiId < 0)
                pActor.data.get(LineageKeys.SHI_ID, out facts.ShiId, -1L);
            facts.DynastyId = pReign.DynastyId;
            facts.MandatePeriodId = pReign.MandatePeriodId;
            if (facts.MandatePeriodId < 0 && pKingdom?.data != null)
                pKingdom.data.get(LineageKeys.MANDATE_PERIOD_ID,
                    out facts.MandatePeriodId, -1L);
            string mandateOrigin = ReadMandateOrigin(facts.MandatePeriodId);
            facts.ActorName = pActor.getName() ?? "";
            facts.StateName = string.IsNullOrEmpty(pReign.StateNameSnapshot)
                ? pKingdom?.name ?? ""
                : pReign.StateNameSnapshot;
            NobleTitleSnapshot noble = NobleRankService.ReadHot(pActor);
            facts.StateName = PosthumousStateNameRules.Resolve(
                facts.StateName, pKingdom?.name, noble.TitleName, noble.Rank,
                pKingdom == null
                    ? (int)KingdomTitle.Baron
                    : (int)KingdomTitleService.GetTitle(pKingdom),
                noble.KingdomId, pKingdom?.id ?? -1L);
            facts.KingdomColor = HistoryColors.FromKingdom(pKingdom);
            facts.EndReason = pEndReason ?? pReign.EndReason ?? "";
            facts.DeathCause = pReign.DeathCause ?? "";
            facts.HighestTitle = RulerTitleFactRules.ResolveSavedHighestTitle(
                pReign.HighestTitle, facts.MandatePeriodId);
            facts.Age = SafeAge(pActor);
            facts.StartYear = Date.getYear(pReign.StartTime);
            facts.EndYear = Date.getYear(endTime);
            facts.ReignYears = Math.Max(1, facts.EndYear - facts.StartYear + 1);
            facts.ReignIndex = pReign.ReignIndex;
            facts.Diplomacy = PersonalStat(pActor, "diplomacy");
            facts.Warfare = PersonalStat(pActor, "warfare");
            facts.Stewardship = PersonalStat(pActor, "stewardship");
            facts.Intelligence = PersonalStat(pActor, "intelligence");
            facts.Health = RawStat(pActor, "health");
            facts.Combat = RawStat(pActor, "damage");
            facts.StartPopulation = pReign.StartPopulation;
            facts.EndPopulation = pReign.EndPopulation;
            facts.EndCityCount = pReign.EndCityCount;
            facts.CityDelta = pReign.EndCityCount - pReign.StartCityCount;
            facts.WarWins = pReign.WarWins;
            facts.WarLosses = pReign.WarLosses;
            facts.OffensiveWars = WarRecordWriter.GetOffensiveWarCount(
                facts.KingdomId, pReign.StartTime, endTime);
            facts.MajorReforms = CountReformEvents(facts.KingdomId, pReign.StartTime, endTime);
            facts.CapitalMoves = CountCapitalMoveEvents(
                facts.KingdomId, pReign.StartTime, endTime);
            facts.OrderDelta = pReign.LostCapital != 0 || facts.EndReason == "kingdom_fell" ? -1 : 0;
            facts.IsMandate = facts.MandatePeriodId >= 0;
            facts.IsFounder = pReign.IsFounder != 0 ||
                              IsDynastyFounder(facts.DynastyId, facts.ActorId);
            facts.IsLowOrigin = mandateOrigin == "rebel";
            RulerTitleRestorationState restoration =
                RulerTitleRestorationStateService.Read(facts.ShiId);
            facts.IsAutonomousRefounder =
                restoration.SelfRestorationActorId == facts.ActorId;
            facts.WasFormerMandateShi = restoration.WasFormerMandateShi;
            facts.RegainedMandate = restoration.RegainedMandate && facts.IsMandate &&
                                    restoration.RegainedMandateActorId == facts.ActorId;
            facts.RestoredLegalCore = restoration.SelfRestorationActorId == facts.ActorId;
            facts.IsFounderDirectHeir = IsDirectHeirOfPreviousRuler(pActor, pReign);
            facts.LostCapital = pReign.LostCapital != 0;
            facts.HasBiologicalChildren = LineageQuery.GetChildIds(pActor.data.id).Count > 0;
            facts.HasKnownPatriline = pActor.data.parent_id_1 >= 0;
            facts.HasSchoolIdentity = SchoolMembershipService.GetActive(pActor.data.id) != null;
            pActor.data.get(LineageKeys.COLLATERAL_NONAGNATIC, out facts.ForeignLineAdoption, false);
            pActor.data.get(LineageKeys.RESTORED_SHI_ID, out long restoredShiId, -1L);
            facts.CollateralSuccession = restoredShiId >= 0;
            pActor.data.get(LineageKeys.FOUNDED_BRANCH_SHI_ID, out long foundedShiId, -1L);
            facts.FoundedCadetBranch = foundedShiId >= 0;
            facts.Traits = ReadTraits(pActor);

            if (pKingdom?.data != null)
            {
                pKingdom.data.get(LineageKeys.MANDATE_AUTHORITY, out facts.ImperialAuthority, 0);
                pKingdom.data.get(LineageKeys.MANDATE_VALUE, out facts.MandateValue, 0);
                pKingdom.data.get(LineageKeys.POLICY_COMPLETED, out string completed, "");
                facts.CentralizationRaised = ContainsToken(completed, "aw_policy_centralization") ||
                                             ContainsToken(completed, "aw_policy_imperial_bureaucracy");
                facts.RitualPolicyComplete = ContainsToken(completed, "aw_policy_mandate_rites");
            }
            return facts;
        }

        public static void ArchivePersonalSnapshot(Actor pActor)
        {
            if (!Ready || pActor?.data == null || !ShouldArchive(pActor)) return;
            RulerPersonalFacts facts = BuildPersonalFacts(pActor);
            string table = ActorTitleFactSnapshotTableItem.GetTableName();
            ColumnVal[] values = PersonalColumns(facts);
            var updates = new HistoricalSqlColumn[values.Length];
            var inserts = new HistoricalSqlColumn[values.Length + 1];
            inserts[0] = new HistoricalSqlColumn("ACTOR_ID", facts.ActorId);
            for (int i = 0; i < values.Length; i++)
            {
                HistoricalSqlColumn column = new HistoricalSqlColumn(
                    values[i].Name, values[i].Value);
                updates[i] = column;
                inserts[i + 1] = column;
            }
            if (HistoricalWriteService.TryUpsertState(
                    "ruler-personal-facts:" + facts.ActorId, table,
                    new[]
                    {
                        new HistoricalSqlColumn("ACTOR_ID", facts.ActorId)
                    }, updates, inserts, pOnCommitted: null,
                    out _, out string error)) return;
            ModClass.LogWarning("Queue ruler fact snapshot failed: " + error);
        }

        public static bool TryReadPersonalSnapshot(long pActorId, out RulerPersonalFacts pFacts)
        {
            pFacts = null;
            if (!Ready || pActorId < 0) return false;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT DIPLOMACY,WARFARE,STEWARDSHIP,INTELLIGENCE," +
                                  "HEALTH,COMBAT,TRAIT_FLAGS,DECIDED_TIME FROM " +
                                  ActorTitleFactSnapshotTableItem.GetTableName() +
                                  " WHERE ACTOR_ID=@actor LIMIT 1";
                cmd.Parameters.AddWithValue("@actor", pActorId);
                using SQLiteDataReader reader = cmd.ExecuteReader();
                if (!reader.Read()) return false;
                pFacts = new RulerPersonalFacts
                {
                    ActorId = pActorId,
                    Diplomacy = ValueInt(reader, 0),
                    Warfare = ValueInt(reader, 1),
                    Stewardship = ValueInt(reader, 2),
                    Intelligence = ValueInt(reader, 3),
                    Health = ValueInt(reader, 4),
                    Combat = ValueInt(reader, 5),
                    Traits = (RulerTraitFlags)ValueLong(reader, 6),
                    DecidedTime = ValueDouble(reader, 7)
                };
                return true;
            }
            catch { return false; }
        }

        private static RulerPersonalFacts BuildPersonalFacts(Actor pActor)
        {
            return new RulerPersonalFacts
            {
                ActorId = pActor.data.id,
                Diplomacy = PersonalStat(pActor, "diplomacy"),
                Warfare = PersonalStat(pActor, "warfare"),
                Stewardship = PersonalStat(pActor, "stewardship"),
                Intelligence = PersonalStat(pActor, "intelligence"),
                Health = RawStat(pActor, "health"),
                Combat = RawStat(pActor, "damage"),
                Traits = ReadTraits(pActor),
                DecidedTime = LineageService.CurTime()
            };
        }

        private static ColumnVal[] PersonalColumns(RulerPersonalFacts pFacts)
        {
            return new[]
            {
                ColumnVal.Create("DIPLOMACY", pFacts.Diplomacy),
                ColumnVal.Create("WARFARE", pFacts.Warfare),
                ColumnVal.Create("STEWARDSHIP", pFacts.Stewardship),
                ColumnVal.Create("INTELLIGENCE", pFacts.Intelligence),
                ColumnVal.Create("HEALTH", pFacts.Health),
                ColumnVal.Create("COMBAT", pFacts.Combat),
                ColumnVal.Create("TRAIT_FLAGS", (long)pFacts.Traits),
                ColumnVal.Create("DECIDED_TIME", pFacts.DecidedTime)
            };
        }

        private static bool ShouldArchive(Actor pActor)
        {
            pActor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            pActor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            pActor.data.get(LineageKeys.IS_HEIR, out bool isHeir, false);
            return lineageId >= 0 || shiId >= 0 || isHeir || pActor.isKing() ||
                   pActor.hasTrait(LineageKeys.TRAIT_FORMER_KING);
        }

        private static int PersonalStat(Actor pActor, string pStat)
        {
            return RulerTitleFactRules.NormalizeStat(
                RawStat(pActor, pStat), ExcludedBonus(pActor, pStat));
        }

        private static int RawStat(Actor pActor, string pStat)
        {
            try { return Math.Max(0, (int)Math.Round(pActor.stats?[pStat] ?? 0f)); }
            catch { return 0; }
        }

        private static int ExcludedBonus(Actor pActor, string pStat)
        {
            int total = 0;
            foreach (string traitId in ExcludedStatTraits)
            {
                try
                {
                    if (!pActor.hasTrait(traitId)) continue;
                    ActorTrait trait = AssetManager.traits.get(traitId);
                    if (trait?.base_stats == null) continue;
                    total += Math.Max(0, (int)Math.Round(trait.base_stats[pStat]));
                }
                catch { }
            }
            return total;
        }

        private static RulerTraitFlags ReadTraits(Actor pActor)
        {
            RulerTraitFlags result = RulerTraitFlags.None;
            foreach (KeyValuePair<string, RulerTraitFlags> pair in TraitMap)
            {
                try
                {
                    if (pActor.hasTrait(pair.Key)) result |= pair.Value;
                }
                catch { }
            }
            return result;
        }

        private static int CountReformEvents(long pKingdomId, double pStart, double pEnd)
        {
            if (!Ready || pKingdomId < 0) return 0;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT COUNT(*) FROM " + KingdomHistoryTableItem.GetTableName() +
                                  " WHERE KINGDOM_ID=@kingdom AND WORLD_TIME>=@start " +
                                  "AND WORLD_TIME<=@end AND EVENT_TYPE IN (@policy,@tech,@court)";
                cmd.Parameters.AddWithValue("@kingdom", pKingdomId);
                cmd.Parameters.AddWithValue("@start", pStart);
                cmd.Parameters.AddWithValue("@end", pEnd);
                cmd.Parameters.AddWithValue("@policy", KingdomEvent.POLICY_COMPLETED);
                cmd.Parameters.AddWithValue("@tech", KingdomEvent.TECH_COMPLETED);
                cmd.Parameters.AddWithValue("@court", KingdomEvent.COURT_TIER_UPGRADED);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch { return 0; }
        }

        private static int CountCapitalMoveEvents(long pKingdomId, double pStart, double pEnd)
        {
            if (!Ready || pKingdomId < 0) return 0;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT COUNT(*) FROM " +
                                      KingdomHistoryTableItem.GetTableName() +
                                      " WHERE KINGDOM_ID=@kingdom AND WORLD_TIME>=@start " +
                                      "AND WORLD_TIME<=@end AND EVENT_TYPE=@event";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@start", pStart);
                command.Parameters.AddWithValue("@end", pEnd);
                command.Parameters.AddWithValue("@event", KingdomEvent.CAPITAL_MOVED);
                return Convert.ToInt32(command.ExecuteScalar());
            }
            catch { return 0; }
        }

        private static string ReadMandateOrigin(long pPeriodId)
        {
            if (!Ready || pPeriodId < 0) return "";
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT IFNULL(ORIGIN_TYPE, '') FROM " +
                                  MandatePeriodTableItem.GetTableName() +
                                  " WHERE PERIOD_ID=@period LIMIT 1";
                cmd.Parameters.AddWithValue("@period", pPeriodId);
                return Convert.ToString(cmd.ExecuteScalar()) ?? "";
            }
            catch { return ""; }
        }

        private static bool IsDynastyFounder(long pDynastyId, long pActorId)
        {
            if (!Ready || pDynastyId < 0 || pActorId < 0) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT 1 FROM " + DynastyPeriodTableItem.GetTableName() +
                                      " WHERE DYNASTY_ID=@dynasty " +
                                      "AND FOUNDER_KING_ACTOR_ID=@actor LIMIT 1";
                command.Parameters.AddWithValue("@dynasty", pDynastyId);
                command.Parameters.AddWithValue("@actor", pActorId);
                return command.ExecuteScalar() != null;
            }
            catch { return false; }
        }

        private static bool IsDirectHeirOfPreviousRuler(Actor pActor,
            ReignRecordWriter.ReignInfo pReign)
        {
            if (!Ready || pActor?.data == null || pReign.ReignIndex <= 1) return false;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT KING_ACTOR_ID FROM " + KingdomReignTableItem.GetTableName() +
                                  " WHERE KINGDOM_ID=@kingdom AND REIGN_INDEX<@idx " +
                                  "ORDER BY REIGN_INDEX DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@kingdom", pReign.KingdomId);
                cmd.Parameters.AddWithValue("@idx", pReign.ReignIndex);
                object value = cmd.ExecuteScalar();
                if (value == null || value == DBNull.Value) return false;
                long priorActorId = Convert.ToInt64(value);
                return pActor.data.parent_id_1 == priorActorId ||
                       pActor.data.parent_id_2 == priorActorId;
            }
            catch { return false; }
        }

        private static bool ContainsToken(string pValues, string pNeedle)
        {
            if (string.IsNullOrEmpty(pValues) || string.IsNullOrEmpty(pNeedle)) return false;
            foreach (string value in pValues.Split(';'))
                if (string.Equals(value.Trim(), pNeedle, StringComparison.Ordinal)) return true;
            return false;
        }

        private static int SafeAge(Actor pActor)
        {
            try { return Math.Max(0, pActor.getAge()); }
            catch { return 0; }
        }

        private static KeyValuePair<string, RulerTraitFlags> T(string pId, RulerTraitFlags pFlag)
        {
            return new KeyValuePair<string, RulerTraitFlags>(pId, pFlag);
        }

        private static int ValueInt(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? 0 : Convert.ToInt32(pReader.GetValue(pIndex));
        }

        private static long ValueLong(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? 0L : Convert.ToInt64(pReader.GetValue(pIndex));
        }

        private static double ValueDouble(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? 0.0 : Convert.ToDouble(pReader.GetValue(pIndex));
        }
    }
}
