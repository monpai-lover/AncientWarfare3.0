using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.db;
using AncientWarfare3.core.court;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;
using AncientWarfare3.utils;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal sealed class GeneralReadModelEntry
    {
        public Actor Actor;
        public int Merit;
        public int Loyalty = 50;
        public int Ambition = 20;
        public int AppointmentYear = -1;
    }

    internal static class GeneralService
    {
        private const int REFRESH_INTERVAL_YEARS = 3;
        private const int RISK_INTERVAL_YEARS = 5;

        private static readonly CourtRepairCursorStore<int>
            DetachedProjectionRepairCursorByKingdom = new();
        private static long _detachedProjectionRepairDatabaseEpoch = -1L;
        private static object _detachedProjectionRepairWorld;

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

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

            if (YearsSince(pKingdom, LineageKeys.GENERAL_LAST_RISK_YEAR, -99999) >= RISK_INTERVAL_YEARS)
            {
                pKingdom.data.set(LineageKeys.GENERAL_LAST_RISK_YEAR, year);
                GeneralRebellionService.OnKingdomRiskCheck(pKingdom);
            }
        }

        public static void OnKingdomDestroying(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            CourtRepairOrchestration.ClearKingdomCursors(pKingdom.id,
                DetachedProjectionRepairCursorByKingdom.Remove,
                CourtMeritRewardService.RemoveRepairCursor,
                (stage, error) => ModClass.LogWarning(
                    "Court repair cursor cleanup failed kingdom=" +
                    pKingdom.id + " stage=" + stage + ": " +
                    error.Message));
            List<GeneralReadModelEntry> active =
                GetActiveGeneralsForReadModel(pKingdom,
                    pAllowUnitFallback: true);
            for (int i = 0; i < active.Count; i++)
            {
                Actor actor = active[i]?.Actor;
                if (actor?.data == null) continue;
                FiefService.RevokeActorFief(actor, "kingdom_fell");
                EndGeneral(actor, "kingdom_fell");
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

        internal static void RepairDetachedProjection(Actor pActor)
        {
            if (!Ready || pActor?.data == null ||
                pActor.kingdom?.data == null)
                return;
            pActor.data.get(LineageKeys.GENERAL_ACTIVE,
                out bool liveActive, false);
            UpsertGeneral(pActor, pActor.kingdom, liveActive);
        }

        internal static int RepairDetachedProjections(Kingdom pKingdom,
            int pMaximumInspections, int pMaximumRepairs)
        {
            if (!Ready || pKingdom?.data == null ||
                pMaximumInspections <= 0 || pMaximumRepairs <= 0)
                return 0;

            ResetDetachedProjectionRepairCursorsIfNeeded();
            List<Actor> units = pKingdom.units;
            int count = units?.Count ?? 0;
            if (count == 0)
            {
                DetachedProjectionRepairCursorByKingdom.Remove(pKingdom.id);
                return 0;
            }

            DetachedProjectionRepairCursorByKingdom.TryGet(pKingdom.id,
                out int rawCursor);
            CourtRepairScanResult result =
                CourtRepairOrchestration.ScanBounded(units, rawCursor,
                    pMaximumInspections, pMaximumRepairs,
                    actor => NeedsDetachedProjectionRepair(actor, pKingdom),
                    RepairDetachedProjection,
                    (actor, stage, error) =>
                        LogDetachedProjectionRepairFailure(actor, pKingdom,
                            stage, error),
                    nextCursor => DetachedProjectionRepairCursorByKingdom.Set(
                        pKingdom.id, nextCursor));
            return result.RepairAttempts;
        }

        private static bool NeedsDetachedProjectionRepair(Actor pActor,
            Kingdom pKingdom)
        {
            if (pActor?.data == null || pActor.isRekt() ||
                !pActor.isAlive() || pActor.asset?.is_boat == true ||
                pActor.kingdom != pKingdom)
                return false;
            bool liveActive = IsActiveGeneralFast(pActor);
            return CourtMeritRewardCandidateQuery
                .NeedsGeneralProjectionRepair(DB,
                    GeneralStateTableItem.GetTableName(), pActor.data.id,
                    pKingdom.id, liveActive);
        }

        private static void LogDetachedProjectionRepairFailure(Actor pActor,
            Kingdom pKingdom, CourtRepairFailureStage pStage,
            Exception pError)
        {
            ModClass.LogWarning("General projection repair failed actor=" +
                (pActor?.data?.id ?? -1L) + " kingdom=" +
                (pKingdom?.id ?? -1L) + " stage=" + pStage + ": " +
                (pError?.Message ?? "unknown error"));
        }

        private static void ResetDetachedProjectionRepairCursorsIfNeeded()
        {
            long databaseEpoch = LineageArchiveManager.RuntimeDatabaseEpoch;
            object world = World.world;
            if (_detachedProjectionRepairDatabaseEpoch == databaseEpoch &&
                ReferenceEquals(_detachedProjectionRepairWorld, world))
                return;
            DetachedProjectionRepairCursorByKingdom.Clear();
            _detachedProjectionRepairDatabaseEpoch = databaseEpoch;
            _detachedProjectionRepairWorld = world;
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

        public static void RetireForCivilOffice(Actor pActor)
        {
            if (pActor?.data == null || !IsGeneral(pActor)) return;
            EndGeneral(pActor, "civil_office");
        }

        public static void RetireForMilitaryGovernorate(Actor pActor)
        {
            if (pActor?.data == null || !IsGeneral(pActor)) return;
            FiefService.RevokeActorFief(pActor, "military_governorate");
            EndGeneral(pActor, "military_governorate");
            pActor.data.set(LineageKeys.GENERAL_FIEF_CITY_ID, -1L);
        }

        internal static void RetireForCardDeployment(Actor pActor)
        {
            if (pActor?.data == null || !IsGeneral(pActor)) return;
            EndGeneral(pActor, "card_deployment_failed");
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
            bool pAllowUnitFallback = true, int pLimit = 0)
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
                    if (pLimit > 0 && result.Count >= pLimit) break;
                }
                return result;
            }

            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT ACTOR_ID,MERIT_SCORE," +
                                  "APPOINTED_TIME,LOYALTY_SCORE," +
                                  "AMBITION_SCORE FROM " +
                                  GeneralStateTableItem.GetTableName() +
                                  " WHERE KINGDOM_ID=@k AND ACTIVE=1 ORDER BY MERIT_SCORE DESC, ACTOR_ID" +
                                  (pLimit > 0 ? " LIMIT @limit" : "");
                cmd.Parameters.AddWithValue("@k", pKingdom.id);
                if (pLimit > 0)
                    cmd.Parameters.AddWithValue("@limit", pLimit);
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
                        Loyalty = reader.IsDBNull(3) ? 50 : Convert.ToInt32(reader.GetValue(3)),
                        Ambition = reader.IsDBNull(4) ? 20 :
                            Convert.ToInt32(reader.GetValue(4)),
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

            // 功绩是稳定分的大头(×2),涨了就得换位,否则候选表会随着战功
            // 慢慢失序 —— 那正是「按部就班」要避免的重排来源。
            SyncCandidatePool(pActor);
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
            // 席位没空就一步都不做。以前这里也是先返回,但下面那条全量扫描
            // 每三年每王国照跑 —— 现在扫描本身也被持久池吃掉了。
            if (active.Count >= limit) return;

            var taken = new HashSet<long>();
            for (int i = 0; i < active.Count; i++)
                if (active[i]?.data != null) taken.Add(active[i].data.id);

            // 持久池:顺序只算一次,之后按部就班。挑不出人时兜底重建一次 ——
            // 那正是「少收了人」这个漂移方向唯一会显形的地方。
            bool repaired = false;
            while (active.Count < limit)
            {
                Actor picked = PickFromPool(pKingdom, taken);
                if (picked == null)
                {
                    if (repaired) break;
                    repaired = true;
                    GeneralCandidatePool.Invalidate(pKingdom);
                    picked = PickFromPool(pKingdom, taken);
                    if (picked == null) break;
                }

                taken.Add(picked.data.id);
                // 无论任命成不成,都不能再从池里挑到他:成功了他已在职,
                // 失败说明他其实不合格,留在池里只会让这个循环空转。
                GeneralCandidatePool.Remove(pKingdom, picked);
                if (!AppointGeneral(picked, CandidateScore(picked))) continue;
                active.Add(picked);
            }
        }

        /// <summary>
        /// 从持久池里取当前全分最高、且仍然合格的人。
        ///
        /// 表按<b>稳定分</b>降序,漂移项(军队长、职业、战斗属性)在这里补回。
        /// 扫描在「后面的人把漂移项拿满也追不上」时停止 —— 判据见
        /// <see cref="GeneralShortlistRules.NeedsMoreForVolatile"/>,所以这和
        /// 「对全池算全分后排序取第一」结果相同,只是不用碰整张表。
        /// </summary>
        private static Actor PickFromPool(Kingdom pKingdom,
            HashSet<long> pTaken)
        {
            GeneralCandidatePool.Table table =
                GeneralCandidatePool.GetOrBuild(pKingdom,
                    () => BuildCandidateTable(pKingdom));
            Actor best = null;
            int bestFull = 0;
            var stale = new List<Actor>();
            for (int index = 0; index < table.Count; index++)
            {
                int stable = table.Stable[index];
                if (best != null &&
                    !GeneralShortlistRules.NeedsMoreForVolatile(bestFull,
                        stable)) break;
                if (stable + GeneralShortlistRules.VolatileCap <
                    GeneralShortlistRules.MinimumAppointScore) break;
                Actor actor = table.Actors[index];
                if (actor?.data == null || pTaken.Contains(table.Ids[index]))
                    continue;
                // 「多收了人」这个漂移方向就在这里收口:资格是逐个复核的,
                // 所以漏接一次摘除事件不会让不合格的人真的上任。
                if (!CanRemainGeneral(actor, pKingdom))
                {
                    stale.Add(actor);
                    continue;
                }

                int full = GeneralShortlistRules.FullScore(stable,
                    IsArmyCaptain(actor), SafeIsWarrior(actor),
                    CombatBonus(actor));
                if (full < GeneralShortlistRules.MinimumAppointScore) continue;
                // 并列时取表里在前的那个。表已经是全序(稳定分降序、同分 id
                // 升序),所以位置本身就是判据;这里再按 id 比一次等于同一次
                // 选择里叠了第二套并列规则,前缀扫描和全量扫描会给出不同的人。
                // 对拍见 GeneralShortlistRulesTests.BoundedScanMatchesFullScan。
                if (best == null || full > bestFull)
                {
                    best = actor;
                    bestFull = full;
                }
            }

            for (int index = 0; index < stale.Count; index++)
                GeneralCandidatePool.Remove(pKingdom, stale[index]);
            return best;
        }

        /// <summary>
        /// 全量建表 —— 每王国**一次**,之后靠事件维护。功绩一条 SQL 批量读入,
        /// 不再每人一条(那是 general_refresh 191ms 的主要来源)。
        /// </summary>
        private static GeneralCandidatePool.Table BuildCandidateTable(
            Kingdom pKingdom)
        {
            var table = new GeneralCandidatePool.Table();
            if (pKingdom?.data == null) return table;
            Dictionary<long, int> merits =
                GeneralMeritIndex.LoadForKingdom(pKingdom.id);
            var scored = new List<KeyValuePair<long, int>>();
            var byId = new Dictionary<long, Actor>();
            List<Actor> units;
            try { units = pKingdom.getUnits()?.ToList() ?? new List<Actor>(); }
            catch { return table; }

            for (int index = 0; index < units.Count; index++)
            {
                Actor unit = units[index];
                if (unit?.data == null || byId.ContainsKey(unit.data.id))
                    continue;
                int merit = GeneralMeritIndex.Merit(merits, unit);
                if (!CanEnterPool(unit, pKingdom, merit)) continue;
                int stable = StableScore(unit, merit);
                if (stable + GeneralShortlistRules.VolatileCap <= 0) continue;
                byId[unit.data.id] = unit;
                scored.Add(new KeyValuePair<long, int>(unit.data.id, stable));
            }

            scored.Sort((left, right) =>
                GeneralShortlistRules.SortsBefore(left.Value, left.Key,
                    right.Value, right.Key) ? -1
                : left.Key == right.Key && left.Value == right.Value ? 0 : 1);
            for (int index = 0; index < scored.Count; index++)
            {
                table.Actors.Add(byId[scored[index].Key]);
                table.Stable.Add(scored[index].Value);
                table.Ids.Add(scored[index].Key);
                table.Members.Add(scored[index].Key);
            }

            return table;
        }

        private static bool AppointGeneral(Actor pActor, int pScore,
            bool pBypassEligibility = false)
        {
            if (pActor?.data == null || pActor.kingdom?.data == null ||
                !pBypassEligibility && !CanRemainGeneral(pActor, pActor.kingdom))
                return false;
            bool already = IsGeneral(pActor);
            pActor.data.set(LineageKeys.GENERAL_ACTIVE, true);
            ApplyGeneralTrait(pActor);
            LineageService.EnsureOfficialShiAndClan(pActor, CourtPyramidRoleId.General);
            UpsertGeneral(pActor, pActor.kingdom, pActive: true, pInitialScore: pScore);
            string school = CourtService.EnsurePersonalSchool(pActor);
            OfficialCareerAppointmentResult career = OfficialCareerService.Appoint(pActor,
                pActor.kingdom, CourtOfficeLayer.Military, CourtPyramidRoleId.General,
                school, pActor.city);
            if (career.IsCommitted)
                OfficialCareerStateService.ProjectAppointment(pActor, pActor.kingdom,
                    CourtOfficeLayer.Military, CourtPyramidRoleId.General, pActor.city);
            CitySchoolSnapshotService.MarkActorDirty(pActor);
            if (already) return false;

            HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom, pActor.getName(),
                PersonEvent.GENERAL_APPOINTED,
                HistoryText.Actor(pActor) +
                HistoryLocalizationRules.H("aw_hist_general_appointed_person"),
                ChronicleCategory.WAR,
                HistoryTarget.Actor(pActor));
            HistoryWriter.RecordKingdom(pActor.kingdom, KingdomEvent.GENERAL_APPOINTED,
                HistoryText.Kingdom(pActor.kingdom) +
                HistoryLocalizationRules.H("aw_hist_general_appointed_kingdom_mid") +
                HistoryText.Actor(pActor) +
                HistoryLocalizationRules.H("aw_hist_general_appointed_kingdom_suffix"),
                HistoryTarget.Actor(pActor));
            CourtDirectionService.MarkDirty(pActor.kingdom);
            return true;
        }

        internal static bool PromoteToGeneral(Actor pActor)
        {
            if (pActor?.data == null || pActor.kingdom?.data == null)
                return false;
            return AppointGeneral(pActor, CandidateScore(pActor),
                pBypassEligibility: true);
        }

        private static int CandidateScore(Actor pActor)
        {
            if (pActor?.data == null) return 0;
            return GeneralShortlistRules.FullScore(
                StableScore(pActor, GetMerit(pActor)), IsArmyCaptain(pActor),
                SafeIsWarrior(pActor), CombatBonus(pActor));
        }

        /// <summary>
        /// 评分里**由离散事件改变**的那一半 —— 功绩、城主、爵位、宗室。
        /// 这些能在改变时换位,所以进持久表。
        ///
        /// 余下三项(军队长、职业、战斗属性)会自己漂移,没有事件可挂,
        /// 由 <see cref="GeneralShortlistRules.FullScore"/> 在取人时补回。
        /// </summary>
        private static int StableScore(Actor pActor, int pMerit)
        {
            if (pActor?.data == null) return 0;
            int score = pMerit * 2;
            try { if (pActor.isCityLeader()) score += 20; } catch { }
            try { if (ChronicleGate.IsNobleActor(pActor)) score += 15; }
            catch { }
            if (IsRoyalAdultNonHeir(pActor)) score += 10;
            return score;
        }

        private static bool SafeIsWarrior(Actor pActor)
        {
            try { return pActor != null && pActor.isWarrior(); }
            catch { return false; }
        }

        private static int CombatBonus(Actor pActor)
        {
            if (pActor?.data == null) return 0;
            return Math.Min(GeneralShortlistRules.CombatCap,
                Mathf.RoundToInt(CombatScore(pActor) * 0.04f));
        }

        /// <summary>
        /// 建表时的入池判定。传入已经批量读好的功绩,免得
        /// <see cref="CanRemainGeneral"/> 内部再为每个人问一条 SQL ——
        /// 全国几千人时那是几千次往返。
        /// </summary>
        private static bool CanEnterPool(Actor pActor, Kingdom pKingdom,
            int pMerit)
        {
            if (!CanRemainGeneralCore(pActor, pKingdom)) return false;
            return HasGeneralQualification(pActor, pMerit);
        }

        /// <summary>
        /// 入池 / 换位的统一入口。事件方只要说「这个人变了」,
        /// 该进的进、该摘的摘、该换位的换位,判定只写在这一处。
        /// </summary>
        internal static void SyncCandidatePool(Actor pActor)
        {
            if (pActor?.data == null) return;
            Kingdom kingdom = pActor.kingdom;
            if (kingdom?.data == null) return;
            // 没建过表的王国不用管 —— 它第一次建表时自然包含当前状态。
            if (!GeneralCandidatePool.HasTable(kingdom)) return;
            if (IsGeneral(pActor) || !CanRemainGeneral(pActor, kingdom))
            {
                GeneralCandidatePool.Remove(kingdom, pActor);
                return;
            }

            GeneralCandidatePool.Reposition(kingdom, pActor,
                StableScore(pActor, GetMerit(pActor)));
        }

        /// <summary>换了国籍 / 死了 —— 从旧国的池里摘掉,再按新状态入池。</summary>
        internal static void ForgetCandidate(long pKingdomId, Actor pActor)
        {
            if (pActor?.data == null) return;
            GeneralCandidatePool.RemoveById(pKingdomId, pActor.data.id);
        }

        internal static void ClearCandidatePools() =>
            GeneralCandidatePool.ClearRuntime();

        private static bool CanRemainGeneral(Actor pActor, Kingdom pKingdom)
        {
            if (!CanRemainGeneralCore(pActor, pKingdom)) return false;
            return HasGeneralQualification(pActor, GetMerit(pActor));
        }

        /// <summary>硬性排除项 —— 与功绩无关,所以不碰数据库。</summary>
        private static bool CanRemainGeneralCore(Actor pActor,
            Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            if (!RoyalAsylumRules.CanPerformProtectedRole(
                    RoyalAsylumService.IsActive(pActor))) return false;
            if (!HistoricalMasterVocationService.CanEnter(pActor,
                    HistoricalMasterMilitaryContext.General)) return false;
            if (pActor.kingdom != pKingdom) return false;
            if (pActor.isRekt() || !pActor.isAdult()) return false;
            if (pActor.isKing()) return false;
            if (SlaveService.IsSlave(pActor) || SlaveService.IsRetiredSoldier(pActor)) return false;
            if (HeirService.IsCurrentHeir(pKingdom, pActor)) return false;
            if (RoyalGuardService.IsRoyalGuard(pActor)) return false;
            if (DynasticReproductionService
                .ShouldProtectFromOrdinaryMilitaryService(pActor)) return false;
            if (pActor.hasTrait("madness")) return false;
            return true;
        }

        /// <summary>
        /// 「凭什么当将领」这一问。功绩由调用方传入 —— 建表时那是批量读来的,
        /// 逐人复核时才现读。
        /// </summary>
        private static bool HasGeneralQualification(Actor pActor, int pMerit)
        {
            if (pActor?.data == null) return false;
            if (pMerit >= 20) return true;
            if (IsArmyCaptain(pActor)) return true;
            try { if (pActor.isCityLeader()) return true; } catch { }
            if (SafeIsWarrior(pActor)) return true;
            try { if (ChronicleGate.IsNobleActor(pActor)) return true; }
            catch { }
            return IsRoyalAdultNonHeir(pActor);
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

        internal static void SuspendForAsylum(Actor pActor)
        {
            if (IsGeneral(pActor)) EndGeneral(pActor, "royal_asylum");
        }

        private static void EndGeneral(Actor pActor, string pReason)
        {
            if (pActor?.data == null) return;
            Kingdom kingdom = pActor.kingdom;
            pActor.data.set(LineageKeys.GENERAL_ACTIVE, false);
            ClearGeneralTrait(pActor);
            bool careerEnded = OfficialCareerService.End(pActor, CourtOfficeLayer.Military,
                CourtPyramidRoleId.General, pReason ?? "");
            OfficialCareerStateService.ClearCurrentOffice(pActor,
                kingdom?.id ?? -1L, CourtPyramidRoleId.General);
            // 卸任的人重新变成候选 —— 除非他已经不合格(死了、被贬为奴、
            // 换了国籍),SyncCandidatePool 会自己分辨。
            SyncCandidatePool(pActor);
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
                HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom, pActor.getName(),
                    PersonEvent.GENERAL_MERIT,
                    HistoryText.Actor(pActor) +
                    HistoryLocalizationRules.H("aw_hist_general_merit_reached") +
                    HistoryText.PlainText(threshold.ToString()),
                    ChronicleCategory.WAR,
                    HistoryTarget.Actor(pActor));

                if (threshold >= 60)
                    HistoryWriter.RecordKingdom(pActor.kingdom, KingdomEvent.GENERAL_MERIT,
                        HistoryText.Actor(pActor) +
                        HistoryLocalizationRules.H("aw_hist_general_merit_accumulated") +
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
