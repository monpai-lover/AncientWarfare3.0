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
        private static HistoryText H(string pKey) => HistoryLocalizationRules.H(pKey);

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
            if (!CanGrantFief(pKingdom, pGeneral, pCity)) return false;

            return GrantFiefCore(pKingdom, pGeneral, pCity, pReason,
                pBypassGeneralQualification: false);
        }

        /// <summary>
        ///     Amnesty has already selected and validated the city before the
        ///     leader is naturalized. The leader is promoted to a general by
        ///     the amnesty service, but starts without ordinary merit history;
        ///     this entry point keeps the normal city/fief safety checks while
        ///     allowing that one-time political grant.
        /// </summary>
        internal static bool GrantFiefForAmnesty(Kingdom pKingdom,
            Actor pGeneral, City pCity, string pReason)
        {
            if (pKingdom?.data == null || pGeneral?.data == null ||
                pCity?.data == null || !Ready || pGeneral.kingdom != pKingdom ||
                !GeneralService.IsGeneral(pGeneral) ||
                !CanGrantAmnestyFief(pKingdom, pGeneral, pCity)) return false;

            return GrantFiefCore(pKingdom, pGeneral, pCity, pReason,
                pBypassGeneralQualification: true);
        }

        internal static bool CanGrantAmnestyFief(Kingdom pKingdom,
            Actor pRecipient, City pCity)
        {
            return Ready && pRecipient?.data != null && pCity?.data != null &&
                   !pRecipient.isRekt() && pRecipient.isAlive() &&
                   pRecipient.isAdult() &&
                   GetFiefCityId(pRecipient) < 0 &&
                   CanGrantCity(pKingdom, pRecipient, pCity,
                       pRequireGeneralQualification: false);
        }

        private static bool GrantFiefCore(Kingdom pKingdom, Actor pGeneral,
            City pCity, string pReason, bool pBypassGeneralQualification)
        {
            if (pKingdom?.data == null || pGeneral?.data == null ||
                pCity?.data == null || !Ready ||
                !CanGrantCity(pKingdom, pGeneral, pCity,
                    !pBypassGeneralQualification)) return false;

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
                CacheActiveFief(pCity, pGeneral);
                UpdateGeneralFief(pGeneral, pCity, now);
                FiefMilitaryService.EnsureFiefCommand(pCity, pGeneral);
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
            if (TryReadCachedFief(pCity, out FiefInfo cached))
                return cached;

            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT CITY_ID,CITY_NAME,GENERAL_ACTOR_ID,GENERAL_NAME FROM " +
                                  FiefGrantTableItem.GetTableName() +
                                  " WHERE CITY_ID=@c AND REVOKED=0 AND END_TIME<0 ORDER BY GRANT_ID DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@c", pCity.id);
                using SQLiteDataReader reader = cmd.ExecuteReader();
                if (!reader.Read())
                {
                    CacheNoActiveFief(pCity);
                    return null;
                }

                var info = new FiefInfo
                {
                    city_id = ToLong(reader, 0),
                    city_name = ToString(reader, 1),
                    general_actor_id = ToLong(reader, 2),
                    general_name = ToString(reader, 3)
                };
                CacheFiefInfo(pCity, info);
                return info;
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
            TryRevokeFief(pCity, pReason);
        }

        public static void RevokeActorFief(Actor pActor, string pReason)
        {
            TryRevokeActorFief(pActor, pReason);
        }

        public static bool TryRevokeActorFief(Actor pActor, string pReason)
        {
            if (!Ready || pActor?.data == null) return false;
            City city = GetFiefCity(pActor);
            if (city?.data != null) return TryRevokeFief(city, pReason);

            try
            {
                using var update = new SQLiteCommand(DB);
                update.CommandText = "UPDATE " +
                    FiefGrantTableItem.GetTableName() +
                    " SET END_TIME=@time,REVOKED=1,REVOKE_REASON=@reason" +
                    " WHERE GENERAL_ACTOR_ID=@actor AND REVOKED=0" +
                    " AND END_TIME<0";
                update.Parameters.AddWithValue("@time", LineageService.CurTime());
                update.Parameters.AddWithValue("@reason", pReason ?? "");
                update.Parameters.AddWithValue("@actor", pActor.data.id);
                if (update.ExecuteNonQuery() != 1) return false;
                pActor.data.set(LineageKeys.GENERAL_FIEF_CITY_ID, -1L);
                return true;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Actor fief revocation failed: " +
                                    exception.Message);
                return false;
            }
        }

        private static bool TryRevokeFief(City pCity, string pReason)
        {
            if (!Ready || pCity?.data == null) return false;
            FiefInfo info = GetActiveFief(pCity);
            if (info == null) return false;
            try
            {
                using var update = new SQLiteCommand(DB);
                update.CommandText = "UPDATE " +
                    FiefGrantTableItem.GetTableName() +
                    " SET END_TIME=@time,REVOKED=1,REVOKE_REASON=@reason" +
                    " WHERE CITY_ID=@city AND GENERAL_ACTOR_ID=@actor" +
                    " AND REVOKED=0 AND END_TIME<0";
                update.Parameters.AddWithValue("@time", LineageService.CurTime());
                update.Parameters.AddWithValue("@reason", pReason ?? "");
                update.Parameters.AddWithValue("@city", pCity.id);
                update.Parameters.AddWithValue("@actor", info.general_actor_id);
                if (update.ExecuteNonQuery() != 1) return false;
                Actor general = FindActor(info.general_actor_id);
                if (general?.data != null)
                    general.data.set(LineageKeys.GENERAL_FIEF_CITY_ID, -1L);
                CacheNoActiveFief(pCity);
                return true;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Fief revocation failed: " +
                                    exception.Message);
                return false;
            }
        }

        private static bool TryReadCachedFief(City pCity, out FiefInfo pInfo)
        {
            pInfo = null;
            if (pCity?.data == null) return false;

            pCity.data.get(LineageKeys.CITY_FIEF_GENERAL_ID, out long cachedGeneralId,
                FiefCacheRules.UnknownGeneralId);
            if (FiefCacheRules.IsUnknown(cachedGeneralId)) return false;
            if (!FiefCacheRules.HasActiveFief(cachedGeneralId)) return true;

            pCity.data.get(LineageKeys.CITY_FIEF_GENERAL_NAME, out string generalName, "");
            pInfo = new FiefInfo
            {
                city_id = pCity.id,
                city_name = pCity.data.name ?? "",
                general_actor_id = cachedGeneralId,
                general_name = generalName ?? ""
            };
            return true;
        }

        private static void CacheActiveFief(City pCity, Actor pGeneral)
        {
            if (pCity?.data == null || pGeneral?.data == null) return;
            pCity.data.set(LineageKeys.CITY_FIEF_GENERAL_ID, pGeneral.data.id);
            pCity.data.set(LineageKeys.CITY_FIEF_GENERAL_NAME, pGeneral.getName() ?? "");
        }

        private static void CacheFiefInfo(City pCity, FiefInfo pInfo)
        {
            if (pCity?.data == null || pInfo == null) return;
            if (!FiefCacheRules.HasActiveFief(pInfo.general_actor_id))
            {
                CacheNoActiveFief(pCity);
                return;
            }
            pCity.data.set(LineageKeys.CITY_FIEF_GENERAL_ID, pInfo.general_actor_id);
            pCity.data.set(LineageKeys.CITY_FIEF_GENERAL_NAME, pInfo.general_name ?? "");
        }

        private static void CacheNoActiveFief(City pCity)
        {
            if (pCity?.data == null) return;
            pCity.data.set(LineageKeys.CITY_FIEF_GENERAL_ID, FiefCacheRules.NoActiveFiefGeneralId);
            pCity.data.set(LineageKeys.CITY_FIEF_GENERAL_NAME, "");
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

        private static bool CanGrantCity(Kingdom pKingdom, Actor pGeneral,
            City pCity, bool pRequireGeneralQualification = true)
        {
            if (pKingdom?.data == null || pGeneral?.data == null || pCity?.data == null) return false;
            if (pCity.kingdom != pKingdom || pCity.isRekt() || !pCity.isAlive()) return false;
            if (pKingdom.capital == pCity) return false;
            if (IsActiveFief(pCity)) return false;
            if (HeirService.IsCurrentHeir(pKingdom, pCity.leader)) return false;
            if (pRequireGeneralQualification)
            {
                if (pGeneral.kingdom != pKingdom) return false;
                if (!FiefGrantRules.CanGrantToGeneral(
                        GeneralService.IsGeneral(pGeneral),
                        GeneralService.GetMerit(pGeneral))) return false;
            }
            return true;
        }

        public static bool CanGrantFief(Kingdom pKingdom, Actor pGeneral,
            City pCity)
        {
            return Ready && pGeneral?.data != null &&
                   GetFiefCityId(pGeneral) < 0 &&
                   CanGrantCity(pKingdom, pGeneral, pCity);
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
            HistoryText content = H("aw_hist_edict_fief_grant_prefix") +
                                  HistoryText.Actor(pGeneral) +
                                  H("aw_hist_edict_fief_grant_mid") +
                                  HistoryText.City(pCity, pKingdom) +
                                  H("aw_hist_edict_fief_grant_suffix");
            HistoryWriter.RecordPerson(pGeneral.data.id, pKingdom, pGeneral.getName(),
                PersonEvent.FIEF_GRANTED, content,
                ChronicleCategory.HONOR,
                HistoryTarget.City(pCity));

            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.FIEF_GRANTED,
                content,
                HistoryTarget.Actor(pGeneral));

            HistoryWriter.RecordCity(pCity, pKingdom, CityEvent.FIEF_GRANTED,
                content,
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
