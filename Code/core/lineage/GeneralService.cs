using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using AncientWarfare3.core.db;
using AncientWarfare3.core.court;
using AncientWarfare3.core.policy;
using AncientWarfare3.utils;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal sealed class GeneralReadModelEntry
    {
        public Actor Actor;
        public int Merit;
        public int AppointmentYear = -1;
    }

    internal static class GeneralService
    {
        private const int REFRESH_INTERVAL_YEARS = 3;
        private const int FIEF_INTERVAL_YEARS = 8;
        private const int RISK_INTERVAL_YEARS = 5;

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

        private sealed class GeneralCandidate
        {
            public Actor actor;
            public int score;
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || pKingdom.isNeutral() || !pKingdom.isCiv()) return;
            if (!ShouldUseGeneralSystem(pKingdom)) return;
            if (!Ready) return;

            int year = Date.getCurrentYear();
            if (YearsSince(pKingdom, LineageKeys.GENERAL_LAST_REFRESH_YEAR, -99999) >= REFRESH_INTERVAL_YEARS)
            {
                pKingdom.data.set(LineageKeys.GENERAL_LAST_REFRESH_YEAR, year);
                RefreshGenerals(pKingdom);
            }

            if (YearsSince(pKingdom, LineageKeys.GENERAL_LAST_FIEF_YEAR, -99999) >= FIEF_INTERVAL_YEARS)
            {
                pKingdom.data.set(LineageKeys.GENERAL_LAST_FIEF_YEAR, year);
                TryGrantFiefs(pKingdom);
            }

            if (YearsSince(pKingdom, LineageKeys.GENERAL_LAST_RISK_YEAR, -99999) >= RISK_INTERVAL_YEARS)
            {
                pKingdom.data.set(LineageKeys.GENERAL_LAST_RISK_YEAR, year);
                GeneralRebellionService.OnKingdomRiskCheck(pKingdom);
            }
        }

        public static void OnWarEnded(War pWar, WarWinner pWinner)
        {
            if (pWar?.data == null) return;
            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();
            string type = GetWarType(pWar);
            int winPoints = WarWinMerit(type);

            if (attacker?.data != null)
                AwardMerit(FindBestCaptain(attacker), pWinner == WarWinner.Attackers ? winPoints : 3,
                    pWinner == WarWinner.Attackers ? "war_win" : "war_loss");
            if (defender?.data != null)
                AwardMerit(FindBestCaptain(defender), pWinner == WarWinner.Defenders ? winPoints : 3,
                    pWinner == WarWinner.Defenders ? "war_win" : "war_loss");
        }

        public static void OnCityTransferred(City pCity, Kingdom pOldKingdom, Kingdom pNewKingdom)
        {
            if (pCity?.data == null) return;
            if (pNewKingdom?.data != null)
            {
                Actor captain = FindCityCaptain(pCity);
                if (captain?.kingdom == pNewKingdom)
                    AwardMerit(captain, 6, "city_gained");
            }

            Actor leader = pCity.leader;
            if (leader?.data != null && pOldKingdom?.data != null && leader.kingdom == pOldKingdom)
                AwardMerit(leader, 2, "city_defense");

            FiefService.OnCityTransferred(pCity, pOldKingdom, pNewKingdom);
        }

        public static bool IsGeneral(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.GENERAL_ACTIVE, out bool active, false);
            if (active) return true;
            return ReadGeneralActive(pActor.data.id);
        }

        /// <summary>
        ///     热路径专用:只读 live 的 GENERAL_ACTIVE 标志,**不查 DB**。将领/封君在运行时都会置此标志,
        ///     足以在批量筛选里排除。读档瞬时可能漏判(下一维护轮自愈),换取零 DB 开销——
        ///     用于奴隶军/禁卫军等 per-unit 循环,避免每人一次 SQLite 查询。
        /// </summary>
        public static bool IsActiveGeneralFast(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.GENERAL_ACTIVE, out bool active, false);
            return active;
        }

        public static bool IsFiefHolder(Actor pActor)
        {
            return IsGeneral(pActor) && FiefService.GetFiefCityId(pActor) >= 0;
        }

        public static int GetMerit(Actor pActor)
        {
            if (pActor?.data == null) return 0;
            pActor.data.get(LineageKeys.GENERAL_MERIT, out int merit, 0);
            return Math.Max(merit, ReadGeneralInt(pActor.data.id, "MERIT_SCORE", 0));
        }

        public static int GetLoyalty(Actor pActor)
        {
            return pActor?.data == null ? 50 : ReadGeneralInt(pActor.data.id, "LOYALTY_SCORE", 50);
        }

        public static int GetAmbition(Actor pActor)
        {
            return pActor?.data == null ? 20 : ReadGeneralInt(pActor.data.id, "AMBITION_SCORE", 20);
        }

        public static void RetireForSuccession(Actor pActor)
        {
            if (pActor?.data == null) return;
            FiefService.RevokeActorFief(pActor, "succession");
            EndGeneral(pActor, "succession");
            pActor.data.set(LineageKeys.GENERAL_FIEF_CITY_ID, -1L);
        }

        public static List<Actor> GetActiveGenerals(Kingdom pKingdom)
        {
            var result = new List<Actor>();
            if (pKingdom?.data == null) return result;
            foreach (Actor unit in pKingdom.getUnits())
            {
                if (unit?.data == null || unit.isRekt()) continue;
                if (!IsGeneral(unit)) continue;
                if (!CanRemainGeneral(unit, pKingdom))
                {
                    EndGeneral(unit, "invalid");
                    continue;
                }
                result.Add(unit);
            }
            return result;
        }

        public static List<GeneralReadModelEntry> GetActiveGeneralsForReadModel(Kingdom pKingdom,
            bool pAllowUnitFallback = true)
        {
            var result = new List<GeneralReadModelEntry>();
            if (pKingdom?.data == null) return result;
            if (!Ready)
            {
                if (!pAllowUnitFallback) return result;
                foreach (Actor unit in pKingdom.getUnits())
                {
                    if (unit?.data == null || unit.kingdom != pKingdom || unit.isRekt() || !unit.isAlive()) continue;
                    unit.data.get(LineageKeys.GENERAL_ACTIVE, out bool active, false);
                    unit.data.get(LineageKeys.GENERAL_MERIT, out int merit, 0);
                    if (active) result.Add(new GeneralReadModelEntry { Actor = unit, Merit = merit });
                }
                return result;
            }

            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT ACTOR_ID, MERIT_SCORE, APPOINTED_TIME FROM " +
                                  GeneralStateTableItem.GetTableName() +
                                  " WHERE KINGDOM_ID=@k AND ACTIVE=1 ORDER BY MERIT_SCORE DESC, ACTOR_ID";
                cmd.Parameters.AddWithValue("@k", pKingdom.id);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                while (reader.Read())
                {
                    long actorId = reader.GetInt64(0);
                    Actor actor = World.world?.units?.get(actorId);
                    if (actor?.data == null || actor.kingdom != pKingdom || actor.isRekt() || !actor.isAlive()) continue;
                    double appointedTime = reader.IsDBNull(2) ? -1d : Convert.ToDouble(reader.GetValue(2));
                    result.Add(new GeneralReadModelEntry
                    {
                        Actor = actor,
                        Merit = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1)),
                        AppointmentYear = appointedTime > 0d ? Date.getYear(appointedTime) : -1
                    });
                }
            }
            catch { }
            return result;
        }

        public static void AwardMerit(Actor pActor, int pPoints, string pReason)
        {
            if (pActor?.data == null || pPoints <= 0) return;
            if (pActor.isRekt() || pActor.kingdom?.data == null) return;
            if (SlaveService.IsSlave(pActor)) return;

            int old = GetMerit(pActor);
            int next = Math.Min(999, old + pPoints);
            pActor.data.set(LineageKeys.GENERAL_MERIT, next);
            if (IsGeneral(pActor))
                UpsertGeneral(pActor, pActor.kingdom, pActive: true);

            RecordMeritMilestone(pActor, old, next, pPoints, pReason);
        }

        internal static void UpdateTroopPower(Actor pGeneral, int pPower)
        {
            if (pGeneral?.data == null || !Ready) return;
            try
            {
                DB.UpdateValue(GeneralStateTableItem.GetTableName(),
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("ACTOR_ID", pGeneral.data.id) },
                    ColumnVal.Create("TROOP_POWER_SNAPSHOT", pPower),
                    ColumnVal.Create("LAST_RISK_CHECK_TIME", LineageService.CurTime()));
            }
            catch { }
        }

        internal static void MarkRebelled(Actor pGeneral)
        {
            if (pGeneral?.data == null || !Ready) return;
            pGeneral.data.set(LineageKeys.GENERAL_ACTIVE, false);
            ClearGeneralTrait(pGeneral);
            try
            {
                DB.UpdateValue(GeneralStateTableItem.GetTableName(),
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("ACTOR_ID", pGeneral.data.id) },
                    ColumnVal.Create("ACTIVE", 0),
                    ColumnVal.Create("REBELLED", 1),
                    ColumnVal.Create("END_REASON", "rebelled"));
            }
            catch { }
        }

        private static void RefreshGenerals(Kingdom pKingdom)
        {
            RefreshArmyCommanderTraits(pKingdom);
            List<Actor> active = GetActiveGenerals(pKingdom);
            int limit = MaxGeneralCount(pKingdom);
            if (active.Count >= limit) return;

            List<GeneralCandidate> candidates = CollectCandidates(pKingdom, active);
            foreach (GeneralCandidate candidate in candidates)
            {
                if (active.Count >= limit) break;
                if (candidate.score < 45) break;
                if (!AppointGeneral(candidate.actor, candidate.score)) continue;
                active.Add(candidate.actor);
            }
        }

        private static bool AppointGeneral(Actor pActor, int pScore)
        {
            if (pActor?.data == null || pActor.kingdom?.data == null || !CanRemainGeneral(pActor, pActor.kingdom)) return false;
            bool already = IsGeneral(pActor);
            pActor.data.set(LineageKeys.GENERAL_ACTIVE, true);
            ApplyGeneralTrait(pActor);
            LineageService.EnsureOfficialShiAndClan(pActor);
            UpsertGeneral(pActor, pActor.kingdom, pActive: true, pInitialScore: pScore);
            string school = CourtService.EnsurePersonalSchool(pActor);
            OfficialCareerService.Appoint(pActor, pActor.kingdom, CourtOfficeLayer.Military,
                CourtPyramidRoleId.General, school, pActor.city);
            CitySchoolSnapshotService.MarkActorDirty(pActor);
            if (already) return false;

            HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom, pActor.getName(),
                PersonEvent.GENERAL_APPOINTED,
                HistoryText.Actor(pActor) + " \u4EE5\u519B\u529F\u53D7\u4EFB\u4E3A\u5927\u5C06",
                ChronicleCategory.WAR,
                HistoryTarget.Actor(pActor));
            HistoryWriter.RecordKingdom(pActor.kingdom, KingdomEvent.GENERAL_APPOINTED,
                HistoryText.Kingdom(pActor.kingdom) + " \u4EFB" + HistoryText.Actor(pActor) + " \u4E3A\u5927\u5C06",
                HistoryTarget.Actor(pActor));
            CourtDirectionService.MarkDirty(pActor.kingdom);
            return true;
        }

        private static void TryGrantFiefs(Kingdom pKingdom)
        {
            foreach (Actor general in GetActiveGenerals(pKingdom)
                         .OrderByDescending(GetMerit)
                         .ThenByDescending(CandidateScore))
            {
                if (GetMerit(general) < FiefGrantRules.MinimumMeritForFief) continue;
                if (FiefService.GetFiefCityId(general) >= 0) continue;
                if (FiefService.TryGrantBestFief(pKingdom, general, "military_merit")) break;
            }
        }

        private static List<GeneralCandidate> CollectCandidates(Kingdom pKingdom, List<Actor> pActive)
        {
            var activeIds = new HashSet<long>(pActive.Where(a => a?.data != null).Select(a => a.data.id));
            var list = new List<GeneralCandidate>();
            foreach (Actor unit in pKingdom.getUnits())
            {
                if (unit?.data == null || activeIds.Contains(unit.data.id)) continue;
                if (!CanRemainGeneral(unit, pKingdom)) continue;
                int score = CandidateScore(unit);
                if (score <= 0) continue;
                list.Add(new GeneralCandidate { actor = unit, score = score });
            }
            list.Sort((a, b) => b.score.CompareTo(a.score));
            return list;
        }

        private static int CandidateScore(Actor pActor)
        {
            if (pActor?.data == null) return 0;
            int score = GetMerit(pActor) * 2;
            if (IsArmyCaptain(pActor)) score += 30;
            if (pActor.isCityLeader()) score += 20;
            if (ChronicleGate.IsNobleActor(pActor)) score += 15;
            if (IsRoyalAdultNonHeir(pActor)) score += 10;
            if (pActor.isWarrior()) score += 8;
            score += Math.Min(15, Mathf.RoundToInt(CombatScore(pActor) * 0.04f));
            return score;
        }

        private static bool CanRemainGeneral(Actor pActor, Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            if (pActor.kingdom != pKingdom) return false;
            if (pActor.isRekt() || !pActor.isAdult()) return false;
            if (pActor.isKing()) return false;
            if (SlaveService.IsSlave(pActor) || SlaveService.IsRetiredSoldier(pActor)) return false;
            if (HeirService.IsCurrentHeir(pKingdom, pActor)) return false;
            if (RoyalGuardService.IsRoyalGuard(pActor)) return false;
            if (pActor.hasTrait("madness")) return false;
            return IsArmyCaptain(pActor) || pActor.isCityLeader() || pActor.isWarrior() ||
                   ChronicleGate.IsNobleActor(pActor) || GetMerit(pActor) >= 20 || IsRoyalAdultNonHeir(pActor);
        }

        private static bool IsRoyalAdultNonHeir(Actor pActor)
        {
            if (pActor?.data == null || pActor.kingdom?.king?.data == null) return false;
            if (!pActor.isAdult() || HeirService.IsCurrentHeir(pActor.kingdom, pActor)) return false;
            long kingId = pActor.kingdom.king.data.id;
            return pActor.data.parent_id_1 == kingId || pActor.data.parent_id_2 == kingId;
        }

        private static bool IsArmyCaptain(Actor pActor)
        {
            if (pActor?.data == null) return false;
            try { if (pActor.isArmyGroupLeader()) return true; } catch { }
            try { return pActor.hasArmy() && pActor.army?.getCaptain() == pActor; }
            catch { return false; }
        }

        private static Actor FindBestCaptain(Kingdom pKingdom)
        {
            Actor best = null;
            int bestUnits = -1;
            foreach (City city in pKingdom.getCities())
            {
                Actor captain = FindCityCaptain(city);
                if (captain?.data == null) continue;
                int units = CountArmyUnits(captain.army);
                if (units <= bestUnits) continue;
                best = captain;
                bestUnits = units;
            }
            return best;
        }

        private static Actor FindCityCaptain(City pCity)
        {
            if (pCity?.data == null || !pCity.hasArmy()) return null;
            try { return pCity.getArmy()?.getCaptain(); }
            catch { return null; }
        }

        private static int MaxGeneralCount(Kingdom pKingdom)
        {
            int cities = 0;
            try { cities = pKingdom.countCities(); } catch { }
            if (cities >= 6) return 3;
            if (cities >= 3) return 2;
            return 1;
        }

        private static void UpsertGeneral(Actor pActor, Kingdom pKingdom, bool pActive, int pInitialScore = 0)
        {
            if (!Ready || pActor?.data == null || pKingdom?.data == null) return;
            string table = GeneralStateTableItem.GetTableName();
            City home = pActor.city;
            City fief = FiefService.GetFiefCity(pActor);
            int merit = GetMerit(pActor);
            double now = LineageService.CurTime();
            int loyalty = Math.Max(30, ReadGeneralInt(pActor.data.id, "LOYALTY_SCORE", 55));
            int ambition = Math.Max(20, ReadGeneralInt(pActor.data.id, "AMBITION_SCORE", 20 + merit / 5 + pInitialScore / 20));

            var values = new[]
            {
                ColumnVal.Create("ACTOR_NAME", pActor.getName() ?? ""),
                ColumnVal.Create("KINGDOM_ID", pKingdom.id),
                ColumnVal.Create("KINGDOM_NAME", pKingdom.name ?? ""),
                ColumnVal.Create("KINGDOM_COLOR", HistoryColors.FromKingdom(pKingdom)),
                ColumnVal.Create("HOME_CITY_ID", home?.id ?? -1L),
                ColumnVal.Create("HOME_CITY_NAME", home?.data?.name ?? ""),
                ColumnVal.Create("FIEF_CITY_ID", fief?.id ?? -1L),
                ColumnVal.Create("FIEF_CITY_NAME", fief?.data?.name ?? ""),
                ColumnVal.Create("PERSONAL_ARMY_ID", pActor.hasArmy() ? pActor.army?.id ?? -1L : -1L),
                ColumnVal.Create("MERIT_SCORE", merit),
                ColumnVal.Create("LOYALTY_SCORE", loyalty),
                ColumnVal.Create("AMBITION_SCORE", ambition),
                ColumnVal.Create("TROOP_POWER_SNAPSHOT", CountPersonalPower(pActor)),
                ColumnVal.Create("ACTIVE", pActive ? 1 : 0),
                ColumnVal.Create("END_REASON", pActive ? "" : "inactive")
            };

            try
            {
                if (DB.CheckKeyExist(table, SimpleColumnConstraint.CreateEq("ACTOR_ID", pActor.data.id)))
                {
                    DB.UpdateValue(table,
                        new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("ACTOR_ID", pActor.data.id) },
                        values);
                }
                else
                {
                    var insert = new List<ColumnVal>
                    {
                        ColumnVal.Create("ACTOR_ID", pActor.data.id),
                        ColumnVal.Create("APPOINTED_TIME", now),
                        ColumnVal.Create("GRANTED_TIME", -1.0),
                        ColumnVal.Create("LAST_REWARD_TIME", -1.0),
                        ColumnVal.Create("LAST_RISK_CHECK_TIME", -1.0),
                        ColumnVal.Create("REBELLED", 0)
                    };
                    insert.AddRange(values);
                    DB.Insert(table, insert.ToArray());
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("GeneralState upsert failed: " + e.Message);
            }
        }

        private static void EndGeneral(Actor pActor, string pReason)
        {
            if (pActor?.data == null) return;
            Kingdom kingdom = pActor.kingdom;
            pActor.data.set(LineageKeys.GENERAL_ACTIVE, false);
            ClearGeneralTrait(pActor);
            bool careerEnded = OfficialCareerService.End(pActor, CourtOfficeLayer.Military,
                CourtPyramidRoleId.General, pReason ?? "");
            if (careerEnded && kingdom?.data != null)
                ChronicleEvents.OnCourtOfficerDismissed(pActor, kingdom,
                    CourtPyramidRoleId.General, pReason ?? "");
            CitySchoolSnapshotService.MarkActorDirty(pActor);
            CourtDirectionService.MarkDirty(kingdom);
            if (!Ready) return;
            try
            {
                DB.UpdateValue(GeneralStateTableItem.GetTableName(),
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("ACTOR_ID", pActor.data.id) },
                    ColumnVal.Create("ACTIVE", 0),
                    ColumnVal.Create("END_REASON", pReason ?? ""));
            }
            catch { }
        }

        private static void RefreshArmyCommanderTraits(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            foreach (Actor unit in pKingdom.getUnits())
            {
                if (unit?.data == null) continue;
                bool shouldHave = ShouldHaveArmyCommanderTrait(unit, pKingdom);
                bool hasTrait = unit.hasTrait(LineageKeys.TRAIT_ARMY_COMMANDER);
                if (shouldHave && !hasTrait)
                    unit.addTrait(LineageKeys.TRAIT_ARMY_COMMANDER);
                else if (!shouldHave && hasTrait)
                    unit.removeTrait(LineageKeys.TRAIT_ARMY_COMMANDER);
            }
        }

        private static bool ShouldHaveArmyCommanderTrait(Actor pActor, Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            if (pActor.kingdom != pKingdom || pActor.isRekt() || !pActor.isAlive()) return false;
            if (!pActor.isAdult() || pActor.isKing()) return false;
            if (IsGeneral(pActor) || SlaveService.IsSlave(pActor)) return false;
            if (pActor.hasTrait("madness")) return false;
            return IsArmyCaptain(pActor);
        }

        private static void ApplyGeneralTrait(Actor pActor)
        {
            if (pActor?.data == null) return;
            if (pActor.hasTrait(LineageKeys.TRAIT_ARMY_COMMANDER))
                pActor.removeTrait(LineageKeys.TRAIT_ARMY_COMMANDER);
            if (!pActor.hasTrait(LineageKeys.TRAIT_GENERAL))
                pActor.addTrait(LineageKeys.TRAIT_GENERAL);
        }

        private static void ClearGeneralTrait(Actor pActor)
        {
            if (pActor?.data == null) return;
            if (pActor.hasTrait(LineageKeys.TRAIT_GENERAL))
                pActor.removeTrait(LineageKeys.TRAIT_GENERAL);
        }

        internal static int CountPersonalPower(Actor pGeneral)
        {
            if (pGeneral?.data == null) return 0;
            int power = 0;
            if (pGeneral.hasArmy()) power += CountArmyUnits(pGeneral.army);
            City city = pGeneral.city;
            if (pGeneral.isCityLeader() && city?.data != null && city.hasArmy())
                power += Mathf.RoundToInt(CountArmyUnits(city.getArmy()) * 0.3f);
            City fief = FiefService.GetFiefCity(pGeneral);
            if (fief?.data != null && fief.hasArmy())
                power += Mathf.RoundToInt(CountArmyUnits(fief.getArmy()) * 0.3f);
            return power;
        }

        private static int CountArmyUnits(Army pArmy)
        {
            try { return pArmy?.countUnits() ?? 0; }
            catch { return 0; }
        }

        private static int WarWinMerit(string pType)
        {
            switch (pType)
            {
                case "vassal_war":
                case "independence_war":
                    return 14;
                case "reclaim":
                case "restoration_war":
                    return 16;
                default:
                    return 10;
            }
        }

        private static void RecordMeritMilestone(Actor pActor, int pOld, int pNext, int pPoints, string pReason)
        {
            if (pActor?.data == null || pActor.kingdom?.data == null) return;
            foreach (int threshold in new[] { 30, 60, 100 })
            {
                if (pOld >= threshold || pNext < threshold) continue;
                string content = "\u4EE5\u519B\u529F\u95FB\u540D\uFF0C\u519B\u529F\u8FBE " + threshold;
                HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom, pActor.getName(),
                    PersonEvent.GENERAL_MERIT,
                    HistoryText.Actor(pActor) + " " + HistoryText.PlainText(content),
                    ChronicleCategory.WAR,
                    HistoryTarget.Actor(pActor));

                if (threshold >= 60)
                    HistoryWriter.RecordKingdom(pActor.kingdom, KingdomEvent.GENERAL_MERIT,
                        HistoryText.Actor(pActor) + " \u519B\u529F\u7D2F\u81F3 " +
                        HistoryText.PlainText(threshold.ToString()),
                        HistoryTarget.Actor(pActor));
            }
        }

        private static bool ReadGeneralActive(long pActorId)
        {
            return ReadGeneralInt(pActorId, "ACTIVE", 0) == 1;
        }

        private static int ReadGeneralInt(long pActorId, string pColumn, int pFallback)
        {
            if (!Ready || pActorId < 0 || string.IsNullOrEmpty(pColumn)) return pFallback;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT " + pColumn + " FROM " + GeneralStateTableItem.GetTableName() +
                                  " WHERE ACTOR_ID=@a LIMIT 1";
                cmd.Parameters.AddWithValue("@a", pActorId);
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? pFallback : Convert.ToInt32(value);
            }
            catch { return pFallback; }
        }

        private static bool ShouldUseGeneralSystem(Kingdom pKingdom)
        {
            return LineageService.IsXiaKingdom(pKingdom) || XiaizationService.CanUsePolicySystem(pKingdom);
        }

        private static int YearsSince(Kingdom pKingdom, string pKey, int pFallback)
        {
            pKingdom.data.get(pKey, out int lastYear, pFallback);
            return Date.getCurrentYear() - lastYear;
        }

        private static string GetWarType(War pWar)
        {
            try { return pWar?.getAsset()?.id ?? ""; }
            catch { return ""; }
        }

        private static float CombatScore(Actor pActor)
        {
            if (pActor?.stats == null) return 0f;
            return SafeStat(pActor, "damage")
                   + SafeStat(pActor, "warfare") * 2f
                   + SafeStat(pActor, "health") * 0.05f
                   + SafeStat(pActor, "armor") * 1.5f
                   + SafeStat(pActor, "speed") * 0.2f;
        }

        private static float SafeStat(Actor pActor, string pKey)
        {
            try { return pActor.stats[pKey]; }
            catch { return 0f; }
        }
    }
}
