using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal sealed class FiefInfo
    {
        public long city_id = -1;
        public string city_name = "";
        public long general_actor_id = -1;
        public string general_name = "";
    }

    internal static class FiefService
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

        public static bool TryGrantBestFief(Kingdom pKingdom, Actor pGeneral, string pReason)
        {
            if (pKingdom?.data == null || pGeneral?.data == null || !Ready) return false;
            if (GetFiefCityId(pGeneral) >= 0) return false;
            City city = PickBestCity(pKingdom, pGeneral);
            return city != null && GrantFief(pKingdom, pGeneral, city, pReason);
        }

        public static bool GrantFief(Kingdom pKingdom, Actor pGeneral, City pCity, string pReason)
        {
            if (pKingdom?.data == null || pGeneral?.data == null || pCity?.data == null || !Ready) return false;
            if (!CanGrantCity(pKingdom, pGeneral, pCity)) return false;

            long grantId = TableIdAllocator.Next(DB, FiefGrantTableItem.GetTableName(), "GRANT_ID");
            double now = LineageService.CurTime();
            Actor king = pKingdom.king;

            try
            {
                DB.Insert(FiefGrantTableItem.GetTableName(),
                    ColumnVal.Create("GRANT_ID", grantId),
                    ColumnVal.Create("GENERAL_ACTOR_ID", pGeneral.data.id),
                    ColumnVal.Create("GENERAL_NAME", pGeneral.getName() ?? ""),
                    ColumnVal.Create("KINGDOM_ID", pKingdom.id),
                    ColumnVal.Create("KINGDOM_NAME", pKingdom.name ?? ""),
                    ColumnVal.Create("KINGDOM_COLOR", HistoryColors.FromKingdom(pKingdom)),
                    ColumnVal.Create("KING_ACTOR_ID", king?.data?.id ?? -1L),
                    ColumnVal.Create("KING_NAME", king?.getName() ?? ""),
                    ColumnVal.Create("CITY_ID", pCity.id),
                    ColumnVal.Create("CITY_NAME", pCity.data.name ?? ""),
                    ColumnVal.Create("CITY_COLOR", HistoryColors.FromCity(pCity, pKingdom)),
                    ColumnVal.Create("GRANT_REASON", pReason ?? ""),
                    ColumnVal.Create("START_TIME", now),
                    ColumnVal.Create("END_TIME", -1.0),
                    ColumnVal.Create("REVOKED", 0),
                    ColumnVal.Create("REVOKE_REASON", ""));

                pGeneral.data.set(LineageKeys.GENERAL_FIEF_CITY_ID, pCity.id);
                UpdateGeneralFief(pGeneral, pCity, now);
                RecordFiefGranted(pKingdom, pGeneral, pCity);
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("FiefGrant insert failed: " + e.Message);
                return false;
            }
        }

        public static FiefInfo GetActiveFief(City pCity)
        {
            if (!Ready || pCity?.data == null) return null;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT CITY_ID,CITY_NAME,GENERAL_ACTOR_ID,GENERAL_NAME FROM " +
                                  FiefGrantTableItem.GetTableName() +
                                  " WHERE CITY_ID=@c AND REVOKED=0 AND END_TIME<0 ORDER BY GRANT_ID DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@c", pCity.id);
                using SQLiteDataReader reader = cmd.ExecuteReader();
                if (!reader.Read()) return null;
                return new FiefInfo
                {
                    city_id = ToLong(reader, 0),
                    city_name = ToString(reader, 1),
                    general_actor_id = ToLong(reader, 2),
                    general_name = ToString(reader, 3)
                };
            }
            catch { return null; }
        }

        public static long GetFiefCityId(Actor pGeneral)
        {
            if (pGeneral?.data == null) return -1L;
            pGeneral.data.get(LineageKeys.GENERAL_FIEF_CITY_ID, out long liveId, -1L);
            if (liveId >= 0) return liveId;
            if (!Ready) return -1L;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT CITY_ID FROM " + FiefGrantTableItem.GetTableName() +
                                  " WHERE GENERAL_ACTOR_ID=@a AND REVOKED=0 AND END_TIME<0 ORDER BY GRANT_ID DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@a", pGeneral.data.id);
                object value = cmd.ExecuteScalar();
                long cityId = value == null || value == DBNull.Value ? -1L : Convert.ToInt64(value);
                if (cityId >= 0) pGeneral.data.set(LineageKeys.GENERAL_FIEF_CITY_ID, cityId);
                return cityId;
            }
            catch { return -1L; }
        }

        public static City GetFiefCity(Actor pGeneral)
        {
            long id = GetFiefCityId(pGeneral);
            if (id < 0 || World.world?.cities == null) return null;
            try { return World.world.cities.get(id); }
            catch { return null; }
        }

        public static bool IsActiveFief(City pCity)
        {
            return GetActiveFief(pCity) != null;
        }

        public static void OnCityTransferred(City pCity, Kingdom pOldKingdom, Kingdom pNewKingdom)
        {
            if (!Ready || pCity?.data == null || pOldKingdom == pNewKingdom) return;
            FiefInfo info = GetActiveFief(pCity);
            if (info == null) return;
            RevokeFief(pCity, "owner_changed");
        }

        public static void RevokeFief(City pCity, string pReason)
        {
            if (!Ready || pCity?.data == null) return;
            FiefInfo info = GetActiveFief(pCity);
            if (info == null) return;
            try
            {
                DB.UpdateValue(FiefGrantTableItem.GetTableName(),
                    new List<SimpleColumnConstraint>
                    {
                        SimpleColumnConstraint.CreateEq("CITY_ID", pCity.id),
                        SimpleColumnConstraint.CreateEq("REVOKED", 0)
                    },
                    ColumnVal.Create("END_TIME", LineageService.CurTime()),
                    ColumnVal.Create("REVOKED", 1),
                    ColumnVal.Create("REVOKE_REASON", pReason ?? ""));

                Actor general = FindActor(info.general_actor_id);
                if (general?.data != null)
                    general.data.set(LineageKeys.GENERAL_FIEF_CITY_ID, -1L);
            }
            catch { }
        }

        private static City PickBestCity(Kingdom pKingdom, Actor pGeneral)
        {
            City best = null;
            float bestScore = float.MinValue;
            foreach (City city in pKingdom.getCities())
            {
                if (!CanGrantCity(pKingdom, pGeneral, city)) continue;
                float score = ScoreCity(city, pGeneral);
                if (score <= bestScore) continue;
                best = city;
                bestScore = score;
            }
            return best;
        }

        private static bool CanGrantCity(Kingdom pKingdom, Actor pGeneral, City pCity)
        {
            if (pKingdom?.data == null || pGeneral?.data == null || pCity?.data == null) return false;
            if (pCity.kingdom != pKingdom || pCity.isRekt() || !pCity.isAlive()) return false;
            if (pKingdom.capital == pCity) return false;
            if (IsActiveFief(pCity)) return false;
            if (HeirService.IsCurrentHeir(pKingdom, pCity.leader)) return false;
            if (pGeneral.kingdom != pKingdom) return false;
            return true;
        }

        private static float ScoreCity(City pCity, Actor pGeneral)
        {
            float score = 10f;
            try { score += pCity.countZones() * 0.05f; } catch { }
            try { score += pCity.getPopulationPeople() * 0.1f; } catch { }
            if (pGeneral.city == pCity) score += 40f;
            if (pCity.hasArmy()) score += 20f;
            if (MandateService.IsLegalCoreCity(pCity)) score += 12f;
            return score;
        }

        private static void UpdateGeneralFief(Actor pGeneral, City pCity, double pTime)
        {
            if (!Ready || pGeneral?.data == null) return;
            try
            {
                DB.UpdateValue(GeneralStateTableItem.GetTableName(),
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("ACTOR_ID", pGeneral.data.id) },
                    ColumnVal.Create("FIEF_CITY_ID", pCity?.id ?? -1L),
                    ColumnVal.Create("FIEF_CITY_NAME", pCity?.data?.name ?? ""),
                    ColumnVal.Create("GRANTED_TIME", pTime),
                    ColumnVal.Create("LAST_REWARD_TIME", pTime),
                    ColumnVal.Create("LOYALTY_SCORE", Math.Min(100, GeneralService.GetLoyalty(pGeneral) + 15)),
                    ColumnVal.Create("AMBITION_SCORE", Math.Min(100, GeneralService.GetAmbition(pGeneral) + 5)));
            }
            catch { }
        }

        private static void RecordFiefGranted(Kingdom pKingdom, Actor pGeneral, City pCity)
        {
            HistoryWriter.RecordPerson(pGeneral.data.id, pKingdom, pGeneral.getName(),
                PersonEvent.FIEF_GRANTED,
                HistoryText.Actor(pGeneral) + " \u53D7\u5C01\u4E8E" + HistoryText.City(pCity, pKingdom),
                ChronicleCategory.HONOR,
                HistoryTarget.City(pCity));

            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.FIEF_GRANTED,
                HistoryText.Kingdom(pKingdom) + " \u5C01" + HistoryText.Actor(pGeneral) +
                " \u4E8E" + HistoryText.City(pCity, pKingdom),
                HistoryTarget.Actor(pGeneral));

            HistoryWriter.RecordCity(pCity, pKingdom, CityEvent.FIEF_GRANTED,
                HistoryText.City(pCity, pKingdom) + " \u6210\u4E3A" + HistoryText.Actor(pGeneral) + " \u5C01\u5730",
                HistoryTarget.Actor(pGeneral));
        }

        private static Actor FindActor(long pId)
        {
            if (pId < 0) return null;
            try { return World.world?.units?.get(pId); }
            catch { return null; }
        }

        private static long ToLong(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? -1L : Convert.ToInt64(pReader.GetValue(pIndex));
        }

        private static string ToString(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? "" : Convert.ToString(pReader.GetValue(pIndex));
        }
    }
}
