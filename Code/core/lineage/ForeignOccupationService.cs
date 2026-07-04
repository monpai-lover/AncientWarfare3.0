using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal sealed class ForeignOccupationReport
    {
        public int active_count;
        public int pseudo_count;
        public int slave_converted_count;
        public float max_resentment;
        public string strongest_city_name = "";
        public string strongest_owner_name = "";
        public string strongest_type = "";
        public float strongest_assimilation;
    }

    internal static class ForeignOccupationService
    {
        private const string TYPE_FOREIGN_ENTRY = "foreign_entry";
        private const string TYPE_PSEUDO_DYNASTY = "pseudo_dynasty";
        private const string TYPE_NORMAL_CONQUEST = "normal_conquest";
        private const int MAX_SLAVES_PER_CITY_YEAR = 3;
        private const int MAX_OCCUPATION_SLAVES_PER_CITY = 12;

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

        private sealed class OccupationRow
        {
            public long id = -1;
            public string type = "";
            public double progress;
            public double resentment;
            public int slaves;
            public bool leaderReplaced;
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || pKingdom.isNeutral() || !Ready) return;

            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.FOREIGN_OCCUPATION_LAST_YEAR, out int lastYear, int.MinValue);
            if (lastYear == year) return;
            pKingdom.data.set(LineageKeys.FOREIGN_OCCUPATION_LAST_YEAR, year);

            foreach (City city in pKingdom.getCities())
                TickCity(city, pKingdom, null);
        }

        public static void OnCityTransferred(City pCity, Kingdom pOldKingdom, Kingdom pNewKingdom)
        {
            if (pCity?.data == null || !Ready) return;
            if (pOldKingdom?.data != null && pOldKingdom != pNewKingdom)
                RememberOriginalKingdom(pCity, pOldKingdom);

            CloseIfOwnerChanged(pCity, pNewKingdom);
            TickCity(pCity, pNewKingdom ?? pCity.kingdom, pOldKingdom);
        }

        public static float GetResentment(City pCity)
        {
            OccupationRow row = ReadActive(pCity);
            return row == null ? 0f : (float)row.resentment;
        }

        public static void AdjustResentment(City pCity, double pDelta)
        {
            OccupationRow row = ReadActive(pCity);
            if (row == null || !Ready) return;
            double next = Math.Max(0.0, Math.Min(100.0, row.resentment + pDelta));
            UpdateOccupation(row.id, pCity, pCity?.kingdom, row.type, row.progress, next, row.slaves, row.leaderReplaced);
        }

        public static ForeignOccupationReport ReadReport()
        {
            var report = new ForeignOccupationReport();
            if (!Ready) return report;

            try
            {
                using (var cmd = new SQLiteCommand(DB))
                {
                    cmd.CommandText = "SELECT COUNT(*),COALESCE(SUM(SLAVE_CONVERTED_COUNT),0)," +
                                      "COALESCE(MAX(RESENTMENT),0),SUM(CASE WHEN OCCUPATION_TYPE=@pseudo THEN 1 ELSE 0 END) " +
                                      "FROM " + CityOccupationStateTableItem.GetTableName() + " WHERE END_TIME<0";
                    cmd.Parameters.AddWithValue("@pseudo", TYPE_PSEUDO_DYNASTY);
                    using SQLiteDataReader reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        report.active_count = ToInt(reader, 0);
                        report.slave_converted_count = ToInt(reader, 1);
                        report.max_resentment = (float)ToDouble(reader, 2);
                        report.pseudo_count = ToInt(reader, 3);
                    }
                }

                using (var cmd = new SQLiteCommand(DB))
                {
                    cmd.CommandText = "SELECT CITY_NAME,OCCUPIER_KINGDOM_NAME,OCCUPATION_TYPE,RESENTMENT,ASSIMILATION_PROGRESS " +
                                      "FROM " + CityOccupationStateTableItem.GetTableName() +
                                      " WHERE END_TIME<0 ORDER BY RESENTMENT DESC,OCCUPATION_ID DESC LIMIT 1";
                    using SQLiteDataReader reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        report.strongest_city_name = ToString(reader, 0);
                        report.strongest_owner_name = ToString(reader, 1);
                        report.strongest_type = ToString(reader, 2);
                        report.max_resentment = (float)ToDouble(reader, 3);
                        report.strongest_assimilation = (float)ToDouble(reader, 4);
                    }
                }
            }
            catch { }

            return report;
        }

        private static void TickCity(City pCity, Kingdom pOwner, Kingdom pPreviousOwner)
        {
            if (pCity?.data == null || pOwner?.data == null || pCity.isRekt() || pOwner.isNeutral())
            {
                CloseActive(pCity, "no_owner");
                return;
            }

            if (!TryDetectOccupation(pCity, pOwner, out string type))
            {
                CloseActive(pCity, "liberated");
                return;
            }

            OccupationRow row = ReadActive(pCity);
            if (row == null)
                row = OpenOccupation(pCity, pOwner, pPreviousOwner, type);

            if (row == null) return;
            if (row.type != type)
                row.type = type;

            bool leaderReplaced = row.leaderReplaced || TryReplaceLocalLeader(pCity, pOwner);
            int newSlaves = ConvertOccupationSlaves(pCity, pOwner, row.slaves);
            AdvanceAssimilation(pCity, pOwner, row, leaderReplaced, newSlaves);

            if (type == TYPE_PSEUDO_DYNASTY && !MandateService.Exists &&
                MandateService.GetCoreControlRatioFor(pOwner) >= 0.65f)
            {
                MandateService.TryDeclareMandate(pOwner, "pseudo_foreign", "pseudo_foreign", "foreign_pseudo");
            }
        }

        private static bool TryDetectOccupation(City pCity, Kingdom pOwner, out string pType)
        {
            pType = "";
            if (pCity?.data == null || pOwner?.data == null) return false;

            bool ownerXia = LineageService.IsXiaKingdom(pOwner);
            bool legalCore = MandateService.IsLegalCoreCity(pCity);
            bool cityXia = IsXiaOriginCity(pCity) || HasXiaCultureOrLanguage(pCity) || HasXiaResidents(pCity);

            if (!ownerXia && legalCore && MandateService.GetCoreControlRatioFor(pOwner) >= 0.65f)
            {
                pType = TYPE_PSEUDO_DYNASTY;
                return true;
            }

            if (!ownerXia && (legalCore || cityXia))
            {
                pType = TYPE_FOREIGN_ENTRY;
                return true;
            }

            if (!ownerXia && HasDifferentCultureOrLanguage(pCity, pOwner))
            {
                pType = TYPE_NORMAL_CONQUEST;
                return true;
            }

            return false;
        }

        private static OccupationRow OpenOccupation(City pCity, Kingdom pOwner, Kingdom pPreviousOwner, string pType)
        {
            if (pCity?.data == null || pOwner?.data == null || !Ready) return null;

            Kingdom original = pPreviousOwner?.data != null ? pPreviousOwner : ResolveOriginalKingdom(pCity);
            long id = TableIdAllocator.Next(DB, CityOccupationStateTableItem.GetTableName(), "OCCUPATION_ID");
            double now = LineageService.CurTime();
            var row = new OccupationRow { id = id, type = pType, resentment = InitialResentment(pType) };

            try
            {
                DB.Insert(CityOccupationStateTableItem.GetTableName(),
                    ColumnVal.Create("OCCUPATION_ID", id),
                    ColumnVal.Create("CITY_ID", pCity.id),
                    ColumnVal.Create("CITY_NAME", pCity.data.name ?? ""),
                    ColumnVal.Create("ORIGINAL_KINGDOM_ID", original?.id ?? -1L),
                    ColumnVal.Create("ORIGINAL_KINGDOM_NAME", original?.name ?? ""),
                    ColumnVal.Create("ORIGINAL_KINGDOM_COLOR", HistoryColors.FromKingdom(original)),
                    ColumnVal.Create("OCCUPIER_KINGDOM_ID", pOwner.id),
                    ColumnVal.Create("OCCUPIER_KINGDOM_NAME", pOwner.name ?? ""),
                    ColumnVal.Create("OCCUPIER_KINGDOM_COLOR", HistoryColors.FromKingdom(pOwner)),
                    ColumnVal.Create("ORIGINAL_CULTURE_ID", IdOf(pCity.culture)),
                    ColumnVal.Create("ORIGINAL_LANGUAGE_ID", IdOf(pCity.language)),
                    ColumnVal.Create("OCCUPIER_CULTURE_ID", IdOf(pOwner.culture)),
                    ColumnVal.Create("OCCUPIER_LANGUAGE_ID", IdOf(pOwner.language)),
                    ColumnVal.Create("OCCUPATION_TYPE", pType ?? ""),
                    ColumnVal.Create("ASSIMILATION_PROGRESS", 0.0),
                    ColumnVal.Create("RESENTMENT", row.resentment),
                    ColumnVal.Create("SLAVE_CONVERTED_COUNT", 0),
                    ColumnVal.Create("LEADER_REPLACED", 0),
                    ColumnVal.Create("START_TIME", now),
                    ColumnVal.Create("END_TIME", -1.0),
                    ColumnVal.Create("UPDATED_TIME", now));
                pCity.data.set(LineageKeys.FOREIGN_OCCUPATION_ID, id);
                RecordOccupationStarted(pCity, pOwner, pType);
                return row;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("CityOccupationState insert failed: " + e.Message);
                return null;
            }
        }

        private static void AdvanceAssimilation(City pCity, Kingdom pOwner, OccupationRow pRow,
            bool pLeaderReplaced, int pNewSlaves)
        {
            double old = pRow.progress;
            double gain = CalculateAssimilationGain(pCity, pOwner, pRow, pLeaderReplaced);
            double next = Math.Min(100.0, old + gain);
            double resentment = Math.Min(100.0, pRow.resentment + ResentmentGain(pRow.type, pNewSlaves));
            int slaveTotal = pRow.slaves + pNewSlaves;

            UpdateOccupation(pRow.id, pCity, pOwner, pRow.type, next, resentment, slaveTotal, pLeaderReplaced);
            XiaizationService.OnForeignOccupationTick(pCity, pOwner, pRow.type, next, resentment);

            RecordMilestoneIfCrossed(pCity, pOwner, old, next, 25);
            RecordMilestoneIfCrossed(pCity, pOwner, old, next, 50);
            RecordMilestoneIfCrossed(pCity, pOwner, old, next, 75);

            if (old < 100.0 && next >= 100.0)
                CompleteAssimilation(pCity, pOwner);
        }

        private static double CalculateAssimilationGain(City pCity, Kingdom pOwner, OccupationRow pRow,
            bool pLeaderReplaced)
        {
            double gain = 2.0;
            if (pRow.type == TYPE_FOREIGN_ENTRY) gain *= 2.0;
            if (pRow.type == TYPE_PSEUDO_DYNASTY) gain *= 3.0;
            if (pLeaderReplaced) gain *= 1.25;
            if (pCity != null && pCity.hasArmy()) gain *= 1.15;
            if (pRow.resentment >= 50.0) gain *= 0.65;
            return gain;
        }

        private static double ResentmentGain(string pType, int pNewSlaves)
        {
            double value = pType == TYPE_PSEUDO_DYNASTY ? 3.0 : pType == TYPE_FOREIGN_ENTRY ? 2.0 : 1.0;
            return value + pNewSlaves * 1.5;
        }

        private static int ConvertOccupationSlaves(City pCity, Kingdom pOwner, int pAlreadyConverted)
        {
            if (pCity?.data == null || pOwner?.data == null) return 0;
            if (pAlreadyConverted >= MAX_OCCUPATION_SLAVES_PER_CITY) return 0;

            int converted = 0;
            foreach (Actor unit in new List<Actor>(pCity.getUnits()))
            {
                if (converted >= MAX_SLAVES_PER_CITY_YEAR) break;
                if (pAlreadyConverted + converted >= MAX_OCCUPATION_SLAVES_PER_CITY) break;
                if (!CanConvertOccupationSlave(unit)) continue;
                if (SlaveService.EnslaveByOccupation(unit, pCity, pOwner, pImportantRecord: false))
                    converted++;
            }

            if (converted > 0)
            {
                HistoryWriter.RecordCity(pCity, pOwner, CityEvent.FOREIGN_OCCUPATION,
                    HistoryText.City(pCity, pOwner) + " \u5916\u65CF\u5360\u9886\u671F\u95F4\u7F16\u5165\u5974\u96B6 " +
                    converted.ToString() + " \u540D",
                    HistoryTarget.City(pCity));
            }

            return converted;
        }

        private static bool CanConvertOccupationSlave(Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt() || !pActor.isAdult()) return false;
            if (!LineageService.IsXia(pActor)) return false;
            if (pActor.isKing() || pActor.isCityLeader()) return false;
            if (HeirService.IsCurrentHeir(pActor.kingdom, pActor)) return false;
            if (pActor.hasTrait("figure") || pActor.hasTrait("first")) return false;
            if (SlaveService.IsSlave(pActor) || SlaveService.IsRetiredSoldier(pActor)) return false;
            if (RoyalGuardService.IsRoyalGuard(pActor)) return false;
            return true;
        }

        private static bool TryReplaceLocalLeader(City pCity, Kingdom pOwner)
        {
            if (pCity?.data == null || pOwner?.data == null) return false;
            Actor leader = pCity.leader;
            if (leader?.data == null || !LineageService.IsXia(leader)) return false;

            Actor replacement = FindOccupierLeaderCandidate(pCity, pOwner);
            if (replacement?.data == null) return false;
            if (replacement.city != pCity)
                replacement.joinCity(pCity);
            pCity.setLeader(replacement, pNew: true);

            HistoryWriter.RecordCity(pCity, pOwner, CityEvent.FOREIGN_OCCUPATION,
                HistoryText.City(pCity, pOwner) + " \u7684\u672C\u5730\u57CE\u4E3B " +
                HistoryText.Actor(leader) + " \u88AB" + HistoryText.Actor(replacement) + " \u53D6\u4EE3",
                HistoryTarget.Actor(replacement));
            return true;
        }

        private static Actor FindOccupierLeaderCandidate(City pCity, Kingdom pOwner)
        {
            foreach (Actor unit in pCity.getUnits())
                if (CanBeOccupierLeader(unit, pOwner)) return unit;
            foreach (Actor unit in pOwner.getUnits())
                if (CanBeOccupierLeader(unit, pOwner)) return unit;
            return null;
        }

        private static bool CanBeOccupierLeader(Actor pActor, Kingdom pOwner)
        {
            if (pActor?.data == null || pActor.kingdom != pOwner || pActor.isRekt() || !pActor.isAdult()) return false;
            if (LineageService.IsXia(pActor)) return false;
            if (pActor.isKing() || pActor.isCityLeader()) return false;
            if (SlaveService.IsSlave(pActor) || RoyalGuardService.IsRoyalGuard(pActor)) return false;
            return pActor.isUnitFitToRule();
        }

        private static void CompleteAssimilation(City pCity, Kingdom pOwner)
        {
            if (pCity?.data == null || pOwner?.data == null) return;
            OccupationRow row = ReadActive(pCity);
            if (row != null && (row.type == TYPE_FOREIGN_ENTRY || row.type == TYPE_PSEUDO_DYNASTY))
            {
                XiaizationService.CompleteXiaizedCity(pCity, pOwner, row.type);
                return;
            }

            if (pOwner.culture != null)
                pCity.setCulture(pOwner.culture);
            if (pOwner.language != null)
                pCity.setLanguage(pOwner.language);

            HistoryWriter.RecordCity(pCity, pOwner, CityEvent.CULTURE_ASSIMILATED,
                HistoryText.City(pCity, pOwner) + " \u88AB\u540C\u5316\u4E3A" +
                HistoryText.Kingdom(pOwner) + " \u7684\u6587\u5316\u4E0E\u8BED\u8A00",
                HistoryTarget.City(pCity));
            HistoryWriter.RecordKingdom(pOwner, KingdomEvent.FOREIGN_OCCUPATION,
                HistoryText.Kingdom(pOwner) + " \u5B8C\u6210\u5BF9" +
                HistoryText.City(pCity, pOwner) + " \u7684\u6587\u5316\u540C\u5316",
                HistoryTarget.City(pCity));
        }

        private static void RecordMilestoneIfCrossed(City pCity, Kingdom pOwner, double pOld, double pNext, int pMilestone)
        {
            if (pOld >= pMilestone || pNext < pMilestone) return;
            HistoryWriter.RecordCity(pCity, pOwner, CityEvent.FOREIGN_OCCUPATION,
                HistoryText.City(pCity, pOwner) + " \u5916\u65CF\u5360\u9886\u540C\u5316\u8FDB\u5EA6\u8FBE\u5230 " +
                pMilestone.ToString() + "%",
                HistoryTarget.City(pCity));
        }

        private static void RecordOccupationStarted(City pCity, Kingdom pOwner, string pType)
        {
            string label = pType == TYPE_PSEUDO_DYNASTY
                ? "\u4F2A\u671D\u5165\u636E"
                : pType == TYPE_FOREIGN_ENTRY
                    ? "\u5916\u65CF\u5165\u636E"
                    : "\u5F02\u6587\u5316\u5360\u9886";
            HistoryWriter.RecordCity(pCity, pOwner, CityEvent.FOREIGN_OCCUPATION,
                HistoryText.City(pCity, pOwner) + " \u88AB" + HistoryText.Kingdom(pOwner) +
                " " + HistoryText.PlainText(label),
                HistoryTarget.City(pCity));
            HistoryWriter.RecordKingdom(pOwner, KingdomEvent.FOREIGN_OCCUPATION,
                HistoryText.Kingdom(pOwner) + " \u5360\u636E " + HistoryText.City(pCity, pOwner) +
                "\uFF08" + HistoryText.PlainText(label) + "\uFF09",
                HistoryTarget.City(pCity));
        }

        private static void RememberOriginalKingdom(City pCity, Kingdom pOldKingdom)
        {
            if (pCity?.data == null || pOldKingdom?.data == null) return;
            pCity.data.get(LineageKeys.CITY_ORIGINAL_KINGDOM_ID, out long existing, -1L);
            if (existing >= 0) return;
            pCity.data.set(LineageKeys.CITY_ORIGINAL_KINGDOM_ID, pOldKingdom.id);
        }

        private static void CloseIfOwnerChanged(City pCity, Kingdom pNewKingdom)
        {
            OccupationRow row = ReadActive(pCity);
            if (row == null || pNewKingdom?.data == null) return;
            long occupier = ReadLong(row.id, "OCCUPIER_KINGDOM_ID");
            if (occupier >= 0 && occupier != pNewKingdom.id)
                CloseActive(pCity, "owner_changed");
        }

        private static void CloseActive(City pCity, string pReason)
        {
            OccupationRow row = ReadActive(pCity);
            if (row == null || !Ready) return;
            try
            {
                DB.UpdateValue(CityOccupationStateTableItem.GetTableName(),
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("OCCUPATION_ID", row.id) },
                    ColumnVal.Create("END_TIME", LineageService.CurTime()),
                    ColumnVal.Create("UPDATED_TIME", LineageService.CurTime()));
                if (pCity?.data != null) pCity.data.set(LineageKeys.FOREIGN_OCCUPATION_ID, -1L);
            }
            catch { }
        }

        private static void UpdateOccupation(long pId, City pCity, Kingdom pOwner, string pType, double pProgress,
            double pResentment, int pSlaves, bool pLeaderReplaced)
        {
            if (pId < 0 || !Ready) return;
            try
            {
                DB.UpdateValue(CityOccupationStateTableItem.GetTableName(),
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("OCCUPATION_ID", pId) },
                    ColumnVal.Create("CITY_NAME", pCity?.data?.name ?? ""),
                    ColumnVal.Create("OCCUPIER_KINGDOM_ID", pOwner?.id ?? -1L),
                    ColumnVal.Create("OCCUPIER_KINGDOM_NAME", pOwner?.name ?? ""),
                    ColumnVal.Create("OCCUPIER_KINGDOM_COLOR", HistoryColors.FromKingdom(pOwner)),
                    ColumnVal.Create("OCCUPIER_CULTURE_ID", IdOf(pOwner?.culture)),
                    ColumnVal.Create("OCCUPIER_LANGUAGE_ID", IdOf(pOwner?.language)),
                    ColumnVal.Create("OCCUPATION_TYPE", pType ?? ""),
                    ColumnVal.Create("ASSIMILATION_PROGRESS", pProgress),
                    ColumnVal.Create("RESENTMENT", pResentment),
                    ColumnVal.Create("SLAVE_CONVERTED_COUNT", pSlaves),
                    ColumnVal.Create("LEADER_REPLACED", pLeaderReplaced ? 1 : 0),
                    ColumnVal.Create("UPDATED_TIME", LineageService.CurTime()));
            }
            catch (Exception e)
            {
                ModClass.LogWarning("CityOccupationState update failed: " + e.Message);
            }
        }

        private static OccupationRow ReadActive(City pCity)
        {
            if (pCity?.data == null || !Ready) return null;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT OCCUPATION_ID,OCCUPATION_TYPE,ASSIMILATION_PROGRESS,RESENTMENT," +
                                  "SLAVE_CONVERTED_COUNT,LEADER_REPLACED FROM " +
                                  CityOccupationStateTableItem.GetTableName() +
                                  " WHERE CITY_ID=@city AND END_TIME<0 ORDER BY OCCUPATION_ID DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@city", pCity.id);
                using SQLiteDataReader reader = cmd.ExecuteReader();
                if (!reader.Read()) return null;
                return new OccupationRow
                {
                    id = ToLong(reader, 0),
                    type = ToString(reader, 1),
                    progress = ToDouble(reader, 2),
                    resentment = ToDouble(reader, 3),
                    slaves = ToInt(reader, 4),
                    leaderReplaced = ToInt(reader, 5) == 1
                };
            }
            catch { return null; }
        }

        private static long ReadLong(long pOccupationId, string pColumn)
        {
            if (pOccupationId < 0 || string.IsNullOrEmpty(pColumn) || !Ready) return -1;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT " + pColumn + " FROM " + CityOccupationStateTableItem.GetTableName() +
                                  " WHERE OCCUPATION_ID=@id LIMIT 1";
                cmd.Parameters.AddWithValue("@id", pOccupationId);
                object result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? -1L : Convert.ToInt64(result);
            }
            catch { return -1; }
        }

        private static Kingdom ResolveOriginalKingdom(City pCity)
        {
            if (pCity?.data == null) return null;
            pCity.data.get(LineageKeys.CITY_ORIGINAL_KINGDOM_ID, out long id, -1L);
            if (id < 0) return null;
            try { return World.world?.kingdoms?.get(id); }
            catch { return null; }
        }

        private static bool IsXiaOriginCity(City pCity)
        {
            if (pCity?.data == null) return false;
            return pCity.data.original_actor_asset == LineageService.XIA_ASSET_ID;
        }

        private static bool HasXiaCultureOrLanguage(City pCity)
        {
            try
            {
                if (pCity?.culture?.data?.original_actor_asset == LineageService.XIA_ASSET_ID) return true;
                if (pCity?.culture?.data?.creator_species_id == LineageService.XIA_ASSET_ID) return true;
                if (pCity?.language?.data?.creator_species_id == LineageService.XIA_ASSET_ID) return true;
            }
            catch { }
            return false;
        }

        private static bool HasXiaResidents(City pCity)
        {
            if (pCity?.data == null) return false;
            foreach (Actor unit in pCity.getUnits())
                if (LineageService.IsXia(unit)) return true;
            return false;
        }

        private static bool HasDifferentCultureOrLanguage(City pCity, Kingdom pOwner)
        {
            if (pCity?.data == null || pOwner?.data == null) return false;
            bool cultureDiff = pCity.culture != null && pOwner.culture != null && pCity.culture != pOwner.culture;
            bool languageDiff = pCity.language != null && pOwner.language != null && pCity.language != pOwner.language;
            return cultureDiff || languageDiff;
        }

        private static double InitialResentment(string pType)
        {
            if (pType == TYPE_PSEUDO_DYNASTY) return 35.0;
            if (pType == TYPE_FOREIGN_ENTRY) return 25.0;
            return 12.0;
        }

        private static string IdOf(Culture pCulture)
        {
            return pCulture == null ? "" : pCulture.id.ToString();
        }

        private static string IdOf(Language pLanguage)
        {
            return pLanguage == null ? "" : pLanguage.id.ToString();
        }

        private static int ToInt(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? 0 : Convert.ToInt32(pReader.GetValue(pIndex));
        }

        private static long ToLong(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? -1L : Convert.ToInt64(pReader.GetValue(pIndex));
        }

        private static double ToDouble(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? 0.0 : Convert.ToDouble(pReader.GetValue(pIndex));
        }

        private static string ToString(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? "" : Convert.ToString(pReader.GetValue(pIndex));
        }
    }
}
