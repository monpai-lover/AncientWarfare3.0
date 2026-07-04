using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.utils;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal sealed class CityTechReport
    {
        public int adopted_count;
        public int total_count;
        public float adoption_score;
        public string spreading_tech = "";
        public float spreading_progress;
        public string source_city_name = "";
        public string source_kingdom_name = "";
        public float neighbor_bonus = 1f;
    }

    internal static class CityTechService
    {
        private const double ADOPTED = 100.0;
        private const double MAX_EXPOSURE = 60.0;
        private const float SAME_KINGDOM_BASE_GAIN = 9f;
        private const float MAX_YEARLY_GAIN = 28f;
        private const int NEIGHBOR_RANGE = 75;

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

        public static void OnNationalTechCompleted(Kingdom pKingdom, KingdomPolicyDef pTech)
        {
            if (pKingdom?.data == null || pTech == null || pTech.Kind != PolicyNodeKind.Tech) return;
            City capital = pKingdom.capital;
            if (capital?.data == null) return;

            bool changed = UpsertProgress(capital, pTech.Id, ADOPTED, 0, "capital", capital, pKingdom, true);
            if (!changed) return;

            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.CITY_TECH_ADOPTED,
                HistoryText.Kingdom(pKingdom) + " \u5B8C\u6210\u79D1\u6280 " +
                HistoryText.PlainText(pTech.FallbackName) + "\uFF0C\u5148\u884C\u4F20\u5165" +
                HistoryText.City(capital, pKingdom),
                HistoryTarget.City(capital));
            HistoryWriter.RecordCity(capital, pKingdom, CityEvent.TECH_ADOPTED,
                HistoryText.City(capital, pKingdom) + " \u9996\u5148\u91C7\u7EB3\u79D1\u6280 " +
                HistoryText.PlainText(pTech.FallbackName));
            TechMapModeService.DirtyMapIfActive();
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            if (!KingdomPolicyService.IsPolicyEnabledForKingdom(pKingdom)) return;
            if (!Ready) return;

            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.CITY_TECH_LAST_YEAR, out int lastYear, int.MinValue);
            if (lastYear == year) return;
            pKingdom.data.set(LineageKeys.CITY_TECH_LAST_YEAR, year);

            foreach (string techId in CompletedTechIds(pKingdom))
            {
                KingdomPolicyDef tech = KingdomPolicyDefs.Get(techId);
                if (tech == null || tech.Kind != PolicyNodeKind.Tech) continue;
                SpreadCompletedTech(pKingdom, tech);
            }

            string current = KingdomPolicyService.GetCurrent(pKingdom, PolicyNodeKind.Tech);
            if (!string.IsNullOrEmpty(current))
                AddNeighborExposure(pKingdom, current);
        }

        public static float GetNeighborTechResearchBonus(Kingdom pKingdom, string pTechId)
        {
            if (pKingdom?.data == null || string.IsNullOrEmpty(pTechId) || !Ready) return 1f;
            NeighborInfluence best = FindBestNeighborInfluence(pKingdom, pTechId);
            if (best == null) return 1f;

            float closeness = Mathf.Clamp01(1f - best.distance / NEIGHBOR_RANGE);
            float relation = RelationFactor(pKingdom, best.sourceKingdom);
            return Mathf.Clamp(1f + 0.35f * closeness * relation, 1f, 1.35f);
        }

        public static string BuildNeighborBonusTooltip(Kingdom pKingdom, string pTechId)
        {
            if (pKingdom?.data == null || string.IsNullOrEmpty(pTechId) || !Ready) return "";
            NeighborInfluence best = FindBestNeighborInfluence(pKingdom, pTechId);
            if (best == null) return "";
            int pct = Mathf.RoundToInt((GetNeighborTechResearchBonus(pKingdom, pTechId) - 1f) * 100f);
            if (pct <= 0) return "";
            return "\u90BB\u56FD\u601D\u6F6E +" + pct + "%: " +
                   (best.sourceKingdom?.name ?? "") + " " + (best.sourceCity?.data?.name ?? "");
        }

        public static CityTechReport GetCityReport(City pCity)
        {
            var report = new CityTechReport();
            report.total_count = KingdomPolicyDefs.Techs.Count();
            if (pCity?.data == null || !Ready) return report;

            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT TECH_ID,ADOPTED,ADOPTION_PROGRESS,EXPOSURE_PROGRESS,SOURCE_CITY_ID,SOURCE_KINGDOM_ID " +
                                  "FROM " + CityTechStateTableItem.GetTableName() + " WHERE CITY_ID=@city";
                cmd.Parameters.AddWithValue("@city", pCity.id);
                using SQLiteDataReader reader = cmd.ExecuteReader();
                double bestProgress = 0;
                while (reader.Read())
                {
                    string techId = ToString(reader, 0);
                    bool adopted = ToInt(reader, 1) == 1;
                    double adoption = ToDouble(reader, 2);
                    double exposure = ToDouble(reader, 3);
                    if (adopted)
                    {
                        report.adopted_count++;
                        report.adoption_score += 1f;
                        continue;
                    }

                    double progress = Math.Max(adoption, exposure);
                    report.adoption_score += (float)(progress / ADOPTED) * 0.55f;
                    if (progress <= bestProgress) continue;
                    bestProgress = progress;
                    report.spreading_tech = techId;
                    report.spreading_progress = (float)(progress / ADOPTED);
                    report.source_city_name = FindCity(ToLong(reader, 4))?.data?.name ?? "";
                    report.source_kingdom_name = FindKingdom(ToLong(reader, 5))?.name ?? "";
                }
            }
            catch { }

            report.neighbor_bonus = GetNeighborTechResearchBonus(pCity.kingdom, KingdomPolicyService.GetCurrent(pCity.kingdom, PolicyNodeKind.Tech));
            return report;
        }

        public static Color32 GetCityMapColor(City pCity)
        {
            if (pCity?.data == null || pCity.kingdom?.data == null) return new Color32(0, 0, 0, 0);
            if (!KingdomPolicyService.CanUsePolicySystem(pCity.kingdom)) return new Color32(0, 0, 0, 0);

            CityTechReport report = GetCityReport(pCity);
            float max = Mathf.Max(1f, report.total_count);
            float score = Mathf.Clamp01(report.adoption_score / max);
            Color32 color;
            if (score < 0.05f) color = new Color32(179, 58, 46, 215);
            else if (score < 0.22f) color = new Color32(201, 107, 44, 215);
            else if (score < 0.45f) color = new Color32(201, 164, 44, 215);
            else if (score < 0.70f) color = new Color32(116, 168, 74, 215);
            else color = new Color32(47, 155, 87, 215);
            return color;
        }

        public static string BuildCityTooltip(City pCity)
        {
            if (pCity?.data == null) return "";
            CityTechReport report = GetCityReport(pCity);
            string text = "\u57CE\u5E02\u79D1\u6280: " + report.adopted_count + "/" + report.total_count;
            if (!string.IsNullOrEmpty(report.spreading_tech))
            {
                KingdomPolicyDef def = KingdomPolicyDefs.Get(report.spreading_tech);
                text += "\n\u4F20\u64AD\u4E2D: " + (def?.FallbackName ?? report.spreading_tech) +
                        " " + Mathf.RoundToInt(report.spreading_progress * 100f) + "%";
            }
            if (report.neighbor_bonus > 1.001f)
                text += "\n\u90BB\u56FD\u601D\u6F6E: +" + Mathf.RoundToInt((report.neighbor_bonus - 1f) * 100f) + "%";
            return text;
        }

        public static void OnCityChangedKingdom(City pCity, Kingdom pKingdom)
        {
            if (pCity?.data == null || pKingdom?.data == null || !Ready) return;
            try
            {
                DB.UpdateValue(CityTechStateTableItem.GetTableName(),
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("CITY_ID", pCity.id) },
                    ColumnVal.Create("KINGDOM_ID", pKingdom.id),
                    ColumnVal.Create("KINGDOM_NAME", pKingdom.name ?? ""),
                    ColumnVal.Create("CITY_NAME", pCity.data.name ?? ""),
                    ColumnVal.Create("UPDATED_TIME", LineageService.CurTime()));
            }
            catch { }
        }

        public static void AdjustInheritedSnapshotFromCities(Kingdom pNewKingdom, KingdomPolicySnapshot pSnapshot)
        {
            if (pNewKingdom?.data == null || pSnapshot == null || !Ready) return;
            List<City> cities = GetCities(pNewKingdom);
            if (cities.Count == 0) return;

            var completed = new HashSet<string>(SplitCompleted(pSnapshot.completed_techs));
            bool sawAnyCityRows = false;
            string bestCurrent = "";
            float bestCurrentProgress = 0f;

            foreach (KingdomPolicyDef tech in KingdomPolicyDefs.Techs)
            {
                bool hasRowsForTech = false;
                int adopted = 0;
                double bestLocalProgress = 0;
                foreach (City city in cities)
                {
                    if (!HasRecord(city, tech.Id)) continue;
                    hasRowsForTech = true;
                    sawAnyCityRows = true;
                    if (IsAdopted(city, tech.Id)) adopted++;
                    bestLocalProgress = Math.Max(bestLocalProgress,
                        Math.Max(ReadAdoption(city, tech.Id), ReadExposure(city, tech.Id)));
                }

                if (!hasRowsForTech) continue;

                bool capitalAdopted = IsAdopted(pNewKingdom.capital, tech.Id);
                int threshold = Math.Max(1, (int)Math.Ceiling(cities.Count * 0.4f));
                if (capitalAdopted && adopted >= threshold)
                {
                    completed.Add(tech.Id);
                    continue;
                }

                if (completed.Contains(tech.Id) || bestLocalProgress <= 0) continue;
                if (!RequirementsMetFromSet(tech, completed)) continue;

                float progress = Mathf.Clamp((float)(bestLocalProgress / ADOPTED) * tech.Cost * 0.75f,
                    0f, Mathf.Max(0f, tech.Cost - 0.01f));
                if (progress <= bestCurrentProgress) continue;
                bestCurrent = tech.Id;
                bestCurrentProgress = progress;
            }

            if (!sawAnyCityRows) return;

            pSnapshot.completed_techs = string.Join(";", completed.ToArray());
            if (!string.IsNullOrEmpty(pSnapshot.current_tech) && completed.Contains(pSnapshot.current_tech))
            {
                pSnapshot.current_tech = "";
                pSnapshot.tech_progress = 0f;
            }

            if (!string.IsNullOrEmpty(bestCurrent) &&
                (string.IsNullOrEmpty(pSnapshot.current_tech) || bestCurrentProgress > pSnapshot.tech_progress))
            {
                pSnapshot.current_tech = bestCurrent;
                pSnapshot.tech_progress = bestCurrentProgress;
            }
        }

        private static void SpreadCompletedTech(Kingdom pKingdom, KingdomPolicyDef pTech)
        {
            City capital = pKingdom.capital;
            if (capital?.data != null)
                UpsertProgress(capital, pTech.Id, ADOPTED, 0, "capital", capital, pKingdom, true);

            List<City> cities = GetCities(pKingdom);
            List<City> adopted = cities.Where(p => IsAdopted(p, pTech.Id)).ToList();
            if (adopted.Count == 0 && capital?.data != null) adopted.Add(capital);
            if (adopted.Count == 0) return;

            foreach (City city in cities)
            {
                if (city?.data == null || city == capital || IsAdopted(city, pTech.Id)) continue;
                City source = FindNearest(city, adopted);
                if (source?.data == null) continue;

                double oldProgress = Math.Max(ReadAdoption(city, pTech.Id), ReadExposure(city, pTech.Id));
                float gain = CalculateSameKingdomGain(city, source, pKingdom, source == capital);
                double next = Math.Min(ADOPTED, oldProgress + gain);
                bool becameAdopted = oldProgress < ADOPTED && next >= ADOPTED;
                UpsertProgress(city, pTech.Id, next, 0, source == capital ? "capital" : "same_kingdom",
                    source, pKingdom, becameAdopted);

                if (!becameAdopted) continue;
                HistoryWriter.RecordCity(city, pKingdom, CityEvent.TECH_ADOPTED,
                    HistoryText.City(city, pKingdom) + " \u91C7\u7EB3\u79D1\u6280 " +
                    HistoryText.PlainText(pTech.FallbackName));
                TechMapModeService.DirtyMapIfActive();
            }
        }

        private static float CalculateSameKingdomGain(City pCity, City pSource, Kingdom pKingdom, bool pCapitalSource)
        {
            float distance = Distance(pCity, pSource);
            float distanceFactor = Mathf.Clamp(1f / (1f + distance / 45f), 0.12f, 1f);
            float capitalBonus = pCapitalSource ? 1.25f : 1f;
            float policyBonus = 1f;
            if (KingdomPolicyService.IsCompleted(pKingdom, PolicyNodeKind.Social, "aw_policy_household_registry"))
                policyBonus = 1.15f;
            if (KingdomPolicyService.IsCompleted(pKingdom, PolicyNodeKind.Social, "aw_policy_early_law"))
                policyBonus = 1.25f;
            return Mathf.Min(MAX_YEARLY_GAIN, SAME_KINGDOM_BASE_GAIN * distanceFactor * capitalBonus * policyBonus);
        }

        private static void AddNeighborExposure(Kingdom pKingdom, string pTechId)
        {
            NeighborInfluence best = FindBestNeighborInfluence(pKingdom, pTechId);
            if (best == null || best.targetCity?.data == null) return;

            double old = ReadExposure(best.targetCity, pTechId);
            if (old >= MAX_EXPOSURE || IsAdopted(best.targetCity, pTechId)) return;
            float bonus = GetNeighborTechResearchBonus(pKingdom, pTechId);
            double next = Math.Min(MAX_EXPOSURE, old + 4.0 * bonus);
            UpsertProgress(best.targetCity, pTechId, ReadAdoption(best.targetCity, pTechId), next, "neighbor",
                best.sourceCity, best.sourceKingdom, false);
        }

        private sealed class NeighborInfluence
        {
            public City targetCity;
            public City sourceCity;
            public Kingdom sourceKingdom;
            public float distance;
        }

        private static NeighborInfluence FindBestNeighborInfluence(Kingdom pKingdom, string pTechId)
        {
            if (pKingdom?.data == null || string.IsNullOrEmpty(pTechId) || World.world?.kingdoms == null) return null;
            List<City> ownCities = GetCities(pKingdom);
            if (ownCities.Count == 0) return null;

            NeighborInfluence best = null;
            foreach (Kingdom other in World.world.kingdoms)
            {
                if (other?.data == null || other == pKingdom || other.isRekt() || other.isNeutral()) continue;
                foreach (City source in GetCities(other))
                {
                    if (!IsAdopted(source, pTechId)) continue;
                    foreach (City target in ownCities)
                    {
                        float dist = Distance(target, source);
                        if (dist > NEIGHBOR_RANGE) continue;
                        if (best != null && dist >= best.distance) continue;
                        best = new NeighborInfluence
                        {
                            targetCity = target,
                            sourceCity = source,
                            sourceKingdom = other,
                            distance = dist
                        };
                    }
                }
            }
            return best;
        }

        private static float RelationFactor(Kingdom pKingdom, Kingdom pOther)
        {
            if (pKingdom?.data == null || pOther?.data == null) return 0.65f;
            try
            {
                if (pKingdom.isEnemy(pOther)) return 0.45f;
                int opinion = World.world.diplomacy.getOpinion(pKingdom, pOther).total;
                if (opinion > 25) return 1f;
                if (opinion < -25) return 0.6f;
            }
            catch { }
            return 0.8f;
        }

        private static bool UpsertProgress(City pCity, string pTechId, double pAdoption, double pExposure,
            string pSourceType, City pSourceCity, Kingdom pSourceKingdom, bool pAdopted)
        {
            if (pCity?.data == null || string.IsNullOrEmpty(pTechId) || !Ready) return false;
            long existing = FindRecordId(pCity.id, pTechId);
            double now = LineageService.CurTime();
            bool alreadyAdopted = existing >= 0 && IsAdopted(pCity, pTechId);
            double adoption = Math.Max(0, Math.Min(ADOPTED, pAdoption));
            double exposure = Math.Max(0, Math.Min(MAX_EXPOSURE, pExposure));
            bool adopted = pAdopted || adoption >= ADOPTED;

            var values = new[]
            {
                ColumnVal.Create("CITY_ID", pCity.id),
                ColumnVal.Create("CITY_NAME", pCity.data.name ?? ""),
                ColumnVal.Create("KINGDOM_ID", pCity.kingdom?.id ?? -1L),
                ColumnVal.Create("KINGDOM_NAME", pCity.kingdom?.name ?? ""),
                ColumnVal.Create("TECH_ID", pTechId),
                ColumnVal.Create("ADOPTED", adopted ? 1 : 0),
                ColumnVal.Create("ADOPTION_PROGRESS", adopted ? ADOPTED : adoption),
                ColumnVal.Create("EXPOSURE_PROGRESS", exposure),
                ColumnVal.Create("SOURCE_TYPE", pSourceType ?? ""),
                ColumnVal.Create("SOURCE_CITY_ID", pSourceCity?.id ?? -1L),
                ColumnVal.Create("SOURCE_KINGDOM_ID", pSourceKingdom?.id ?? -1L),
                ColumnVal.Create("ADOPTED_TIME", adopted && !alreadyAdopted ? now : ReadAdoptedTime(existing)),
                ColumnVal.Create("UPDATED_TIME", now)
            };

            try
            {
                if (existing >= 0)
                {
                    DB.UpdateValue(CityTechStateTableItem.GetTableName(),
                        new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("RECORD_ID", existing) },
                        values);
                    return adopted && !alreadyAdopted;
                }

                long id = TableIdAllocator.Next(DB, CityTechStateTableItem.GetTableName(), "RECORD_ID");
                var insert = new List<ColumnVal>
                {
                    ColumnVal.Create("RECORD_ID", id),
                    ColumnVal.Create("FIRST_SEEN_TIME", now)
                };
                insert.AddRange(values);
                DB.Insert(CityTechStateTableItem.GetTableName(), insert.ToArray());
                return adopted;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("CityTechState upsert failed: " + e.Message);
                return false;
            }
        }

        private static long FindRecordId(long pCityId, string pTechId)
        {
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT RECORD_ID FROM " + CityTechStateTableItem.GetTableName() +
                                  " WHERE CITY_ID=@city AND TECH_ID=@tech LIMIT 1";
                cmd.Parameters.AddWithValue("@city", pCityId);
                cmd.Parameters.AddWithValue("@tech", pTechId);
                object result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? -1L : Convert.ToInt64(result);
            }
            catch { return -1L; }
        }

        private static bool HasRecord(City pCity, string pTechId)
        {
            return pCity?.data != null && FindRecordId(pCity.id, pTechId) >= 0;
        }

        private static bool IsAdopted(City pCity, string pTechId)
        {
            if (pCity?.data == null || !Ready) return false;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT ADOPTED FROM " + CityTechStateTableItem.GetTableName() +
                                  " WHERE CITY_ID=@city AND TECH_ID=@tech LIMIT 1";
                cmd.Parameters.AddWithValue("@city", pCity.id);
                cmd.Parameters.AddWithValue("@tech", pTechId);
                object result = cmd.ExecuteScalar();
                return result != null && result != DBNull.Value && Convert.ToInt32(result) == 1;
            }
            catch { return false; }
        }

        private static double ReadAdoption(City pCity, string pTechId)
        {
            return ReadDouble(pCity, pTechId, "ADOPTION_PROGRESS");
        }

        private static double ReadExposure(City pCity, string pTechId)
        {
            return ReadDouble(pCity, pTechId, "EXPOSURE_PROGRESS");
        }

        private static double ReadDouble(City pCity, string pTechId, string pColumn)
        {
            if (pCity?.data == null || !Ready) return 0;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT " + pColumn + " FROM " + CityTechStateTableItem.GetTableName() +
                                  " WHERE CITY_ID=@city AND TECH_ID=@tech LIMIT 1";
                cmd.Parameters.AddWithValue("@city", pCity.id);
                cmd.Parameters.AddWithValue("@tech", pTechId);
                object result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? 0 : Convert.ToDouble(result);
            }
            catch { return 0; }
        }

        private static double ReadAdoptedTime(long pRecordId)
        {
            if (pRecordId < 0 || !Ready) return -1;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT ADOPTED_TIME FROM " + CityTechStateTableItem.GetTableName() +
                                  " WHERE RECORD_ID=@id LIMIT 1";
                cmd.Parameters.AddWithValue("@id", pRecordId);
                object result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? -1 : Convert.ToDouble(result);
            }
            catch { return -1; }
        }

        private static IEnumerable<string> CompletedTechIds(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) yield break;
            pKingdom.data.get(LineageKeys.TECH_COMPLETED, out string raw, "");
            if (string.IsNullOrEmpty(raw)) yield break;
            foreach (string id in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                yield return id;
        }

        private static IEnumerable<string> SplitCompleted(string pRaw)
        {
            if (string.IsNullOrEmpty(pRaw)) return Array.Empty<string>();
            return pRaw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool RequirementsMetFromSet(KingdomPolicyDef pDef, HashSet<string> pCompleted)
        {
            foreach (string id in pDef.RequiredTechs ?? Array.Empty<string>())
                if (!pCompleted.Contains(id)) return false;
            return true;
        }

        private static List<City> GetCities(Kingdom pKingdom)
        {
            var result = new List<City>();
            if (pKingdom?.data == null) return result;
            foreach (City city in pKingdom.getCities())
                if (city?.data != null && !city.isRekt() && city.isAlive()) result.Add(city);
            return result;
        }

        private static City FindNearest(City pCity, List<City> pSources)
        {
            City best = null;
            float bestDist = float.MaxValue;
            foreach (City source in pSources)
            {
                if (source?.data == null || source == pCity) continue;
                float dist = Distance(pCity, source);
                if (dist >= bestDist) continue;
                best = source;
                bestDist = dist;
            }
            return best;
        }

        private static float Distance(City pA, City pB)
        {
            WorldTile a = pA?.getTile();
            WorldTile b = pB?.getTile();
            if (a == null || b == null) return 9999f;
            return Vector2.Distance(a.pos, b.pos);
        }

        private static City FindCity(long pId)
        {
            if (pId < 0 || World.world?.cities == null) return null;
            try { return World.world.cities.get(pId); }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pId)
        {
            if (pId < 0 || World.world?.kingdoms == null) return null;
            try { return World.world.kingdoms.get(pId); }
            catch { return null; }
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
