using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.lineage;
using UnityEngine;

namespace AncientWarfare3.core.court
{
    internal static class CourtVacancyReconciliationService
    {
        private sealed class RetryTicket
        {
            internal long KingdomId;
            internal int NotBeforeFrame;
        }

        private static readonly Dictionary<long, RetryTicket> RetryTickets =
            new Dictionary<long, RetryTicket>();
        /// <summary>
        /// 每个空缺上一次技术性失败发生的年份。没有这层记账,一个永远填不上的
        /// 席位会自我维持:TechnicalFailure 重挂重试票 → 下一帧重跑 Reconcile
        /// → 又失败 → 再挂票,而每一轮都要重建候选会话并重扫候选。
        /// </summary>
        private static readonly Dictionary<CourtVacancyKey, int> FailureYears =
            new Dictionary<CourtVacancyKey, int>();
        /// <summary>
        /// 每个王国的候选池代际号。任何可能改变「谁能当官」的事件都会递增它。
        /// </summary>
        private static readonly Dictionary<long, int> PoolGenerations =
            new Dictionary<long, int>();
        /// <summary>
        /// 每个王国上一次「候选池自愈重建」发生在哪个代际。候选池靠事件维护、
        /// 不再定期重建,这是唯一的兜底入口 —— 见
        /// <see cref="TryRepairCandidatePool"/>。
        /// </summary>
        private static readonly Dictionary<long, int> PoolRepairs =
            new Dictionary<long, int>();
        /// <summary>
        /// 每个空缺上一次「没有合适人选」时的候选池代际与年份。
        ///
        /// 没有这层记账,一个「确实空着但没人能补」的席位会永久留在注册表里,
        /// 而它的 FailureYears 永远是 -1 —— 于是 HasAttemptableEntry 恒为真,
        /// **每一次唤醒都重建候选会话、重扫一遍全国**。这是补缺路径上最后一处
        /// 无上限重复劳动。
        ///
        /// 用代际号而不是固定年数冷却:池一变就立刻重试,不会漏补。年份是兜底,
        /// 万一某个改变候选池的事件没接上 CandidatePoolChanged,最多推迟到下一年
        /// (和 CityBureauAnnualWorkService 的年度 Request 同一量级)。
        /// </summary>
        private static readonly Dictionary<CourtVacancyKey, (int Generation,
            int Year)> NoCandidateMemos =
            new Dictionary<CourtVacancyKey, (int, int)>();
        /// <summary>
        /// 载入存档后的县令空缺初始化:走一遍所有城市,把缺人的县令席位标记为
        /// 空缺并请求补缺。之后不再扫描 —— 席位空出来由事件驱动(officer 离任
        /// 与死亡都会走 RegisterVacancy(OfficialCareerPrior),该重载已处理
        /// county 层),空一个补一个。
        ///
        /// 必须在 CountyAdministrationStore.RepairAfterWorldLoaded 之后调用。
        /// 原来的问题正是顺序:AW3RuntimeRestorePipeline 的 court_vacancies 阶段
        /// 跑在管线内部,而县级重建在管线之后,于是「发现县令空缺」发生在「县被
        /// 重建出来」之前 —— 存档里已有的县能被找到,靠 zone 重新推导出来的县
        /// 则永远登记不上。
        /// </summary>
        internal static void InitializeCountyVacancies()
        {
            List<City> cities;
            try { cities = World.world?.cities?.ToList(); }
            catch { return; }
            if (cities == null || cities.Count == 0) return;

            var touched = new HashSet<long>();
            for (int index = 0; index < cities.Count; index++)
            {
                City city = cities[index];
                Kingdom kingdom = city?.kingdom;
                if (city?.data == null || city.isRekt() ||
                    kingdom?.data == null || kingdom.isRekt() ||
                    kingdom.isNeutral()) continue;
                IReadOnlyList<CourtVacancyKey> vacancies =
                    LocalCourtAppointmentService.DiscoverCountyVacancies(
                        kingdom, city);
                if (vacancies.Count == 0) continue;
                for (int i = 0; i < vacancies.Count; i++)
                    CourtVacancyRegistry.Register(vacancies[i]);
                touched.Add(kingdom.id);
            }

            // 每个王国只请求一次:Request 是按王国合并的,循环里逐城请求只会
            // 白跑一堆入队。
            foreach (long kingdomId in touched)
                Request(FindKingdom(kingdomId));
        }

        internal static void RegisterVacancy(CourtVacancyKey pKey,
            int pMissingSeats = 1)
        {
            if (pKey.KingdomId < 0L || pMissingSeats <= 0) return;
            // 重新登记的空缺一律给一次新机会:上一次「没人够格」的记账可能是
            // 上一任在职时留下的,那时这个席位的处境和现在不同。
            NoCandidateMemos.Remove(pKey);
            CourtVacancyRegistry.Register(pKey, pMissingSeats);
            Request(FindKingdom(pKey.KingdomId));
        }

        internal static void RegisterVacancy(OfficialCareerPrior pPrior)
        {
            if (pPrior == null || pPrior.KingdomId < 0L ||
                string.IsNullOrEmpty(pPrior.OfficeId)) return;
            bool local = pPrior.Layer == CourtOfficeLayer.City;
            bool county = pPrior.Layer == CourtOfficeLayer.County;
            if (!local && !county && pPrior.Layer != CourtOfficeLayer.Central &&
                pPrior.Layer != CourtOfficeLayer.Military) return;
            if (((local || county) && pPrior.CityId < 0L) ||
                (county && pPrior.CountyId < 0L)) return;
            City city = (local || county) && pPrior.CityId >= 0L
                ? World.world?.cities?.get(pPrior.CityId) : null;
            bool chief = local && city?.data != null &&
                CourtService.ResolveCityOffice(
                    World.world?.kingdoms?.get(pPrior.KingdomId), city) ==
                pPrior.OfficeId;
            RegisterVacancy(new CourtVacancyKey(pPrior.KingdomId,
                local || county ? pPrior.CityId : -1L,
                county ? pPrior.CountyId : -1L, pPrior.Layer,
                pPrior.OfficeId, chief));
        }

        internal static void RegisterCityVacancies(Kingdom pKingdom,
            City pCity)
        {
            if (pKingdom?.data == null || pCity?.data == null ||
                pCity.isRekt() || pCity.kingdom != pKingdom) return;
            int capacity;
            try
            {
                capacity = CourtRules.CityOfficeSlots(
                    pCity.getPopulationPeople(), pCity.countZones(),
                    pKingdom.capital == pCity);
            }
            catch { capacity = 0; }
            IReadOnlyList<CourtVacancyKey> vacancies =
                LocalCourtAppointmentService.DiscoverVacancies(
                    pKingdom, pCity, capacity, Date.getCurrentYear());
            foreach (IGrouping<CourtVacancyKey, CourtVacancyKey> group in
                     vacancies.GroupBy(key => key))
                CourtVacancyRegistry.Register(group.Key, group.Count());
            if (vacancies.Count > 0) Request(pKingdom);
        }

        internal static void RefreshKingdomDefinitions(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            var centralActive = new HashSet<string>(
                CourtService.GetActiveOfficers(pKingdom, int.MaxValue)
                    .Where(row => row != null &&
                        (row.layer == CourtOfficeLayer.Central ||
                         row.layer == CourtOfficeLayer.Military))
                    .Select(row => row.office_id), StringComparer.Ordinal);
            foreach (string officeId in CourtService.
                         CentralOfficeIdsForCurrentProfile(pKingdom))
                if (!centralActive.Contains(officeId))
                    CourtVacancyRegistry.Register(new CourtVacancyKey(
                        pKingdom.id, -1L, -1L, CourtOfficeLayer.Central,
                        officeId));
            foreach (string officeId in CourtService.
                         MilitaryOfficeIdsForCurrentProfile(pKingdom))
                if (!centralActive.Contains(officeId))
                    CourtVacancyRegistry.Register(new CourtVacancyKey(
                        pKingdom.id, -1L, -1L, CourtOfficeLayer.Military,
                        officeId));
            try
            {
                foreach (City city in pKingdom.getCities())
                    RegisterCityVacancies(pKingdom, city);
            }
            catch { }
            Request(pKingdom);
        }

        internal static void CandidatePoolChanged(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            // 代际号必须在下面的提前返回**之前**递增 —— 否则「当时没有空缺,
            // 之后才登记空缺」的池变化会丢掉,memo 就会拿旧代际把人挡在外面。
            PoolGenerations.TryGetValue(pKingdom.id, out int generation);
            PoolGenerations[pKingdom.id] = generation + 1;
            if (CourtVacancyRegistry.Snapshot(pKingdom.id).Count == 0) return;
            Request(pKingdom);
        }

        internal static void ActorLeftKingdom(Kingdom pPrevious)
        {
            RefreshKingdomDefinitions(pPrevious);
        }

        internal static void CityChangedKingdom(City pCity,
            Kingdom pPrevious, Kingdom pCurrent)
        {
            if (pPrevious?.data != null && pCity?.data != null)
                CourtVacancyRegistry.RemoveCity(pPrevious.id, pCity.data.id);
            if (pCurrent?.data != null && pCity?.data != null)
            {
                RegisterCityVacancies(pCurrent, pCity);
                Request(pCurrent);
            }
        }

        internal static void KingdomDestroyed(long pKingdomId)
        {
            CourtVacancyRegistry.RemoveKingdom(pKingdomId);
            RetryTickets.Remove(pKingdomId);
            ForgetFailures(pKingdomId);
        }

        internal static void Request(Kingdom pKingdom, int pAttempt = 0)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            long kingdomId = pKingdom.id;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "court-vacancy:" + kingdomId, DeferredWorkClass.Runtime,
                () => ExecuteRequest(kingdomId, pAttempt));
        }

        internal static void DrainDueRetryTickets()
        {
            DrainDueRetryTickets(int.MaxValue);
        }

        internal static void DrainDueRetryTickets(int pMaximumTickets)
        {
            int frame = Time.frameCount;
            int limit = Math.Max(1, pMaximumTickets);
            int processed = 0;
            var due = new List<long>(Math.Min(RetryTickets.Count, limit));
            foreach (RetryTicket ticket in RetryTickets.Values)
            {
                if (ticket == null || ticket.NotBeforeFrame > frame) continue;
                due.Add(ticket.KingdomId);
                if (++processed >= limit) break;
            }
            foreach (long kingdomId in due)
            {
                RetryTickets.Remove(kingdomId);
                Request(FindKingdom(kingdomId), 1);
            }
        }

        internal static void ClearRuntime()
        {
            CourtVacancyRegistry.ClearRuntime();
            RetryTickets.Clear();
            FailureYears.Clear();
            NoCandidateMemos.Clear();
            PoolGenerations.Clear();
            PoolRepairs.Clear();
        }

        private static void ExecuteRequest(long pKingdomId, int pAttempt)
        {
            try { Reconcile(FindKingdom(pKingdomId)); }
            catch (Exception error)
            {
                if (CourtVacancyRules.ShouldRetry(
                        CourtVacancyOutcome.TechnicalFailure, pAttempt))
                {
                    RetryTickets[pKingdomId] = new RetryTicket
                    {
                        KingdomId = pKingdomId,
                        NotBeforeFrame = Time.frameCount + 1
                    };
                    return;
                }
                ModClass.LogError("Court vacancy reconciliation failed for " +
                    pKingdomId + ": " + error);
            }
        }

        private static void Reconcile(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            IReadOnlyList<CourtVacancyEntry> entries =
                CourtVacancyRegistry.Snapshot(pKingdom.id);
            if (entries.Count == 0) return;
            int year = Date.getCurrentYear();
            int generation = PoolGeneration(pKingdom.id);
            // 剩下的条目全在补缺冷却里、或全都「上次没人且池没变」就直接返回。
            // 建 CourtCandidateSession 本身要扫一遍全国在任官表 —— 不能为了
            // 发现「没什么可做」而先付这笔钱。
            if (!HasAttemptableEntry(entries, year, generation)) return;
            var session = new CourtCandidateSession(pKingdom);
            var processed = new HashSet<CourtVacancyKey>();
            int validOfficeCount = entries.Sum(entry =>
                Math.Max(1, entry.MissingSeats));
            int processedSteps = 0;
            while (processedSteps < CourtVacancyRules.CascadeLimit(
                       validOfficeCount))
            {
                entries = CourtVacancyRegistry.Snapshot(pKingdom.id);
                CourtVacancyEntry? next = CourtVacancyCycleRules.Next(entries,
                    processed, processedSteps, validOfficeCount);
                if (!next.HasValue) break;
                CourtVacancyKey key = next.Value.Key;
                // 上一次技术性失败还在冷却里:跳过,别再为它扫一遍候选。
                if (!CourtAppointmentFailureBackoffRules.ShouldAttempt(
                        LastFailureYear(key), year))
                {
                    processed.Add(key);
                    continue;
                }
                // 上一次就没人够格,而候选池此后没变过:同样别再扫一遍。
                if (!ShouldRetryAfterNoCandidate(key, generation, year))
                {
                    processed.Add(key);
                    continue;
                }
                CourtVacancyOutcome outcome;
                if (key.Layer == CourtOfficeLayer.Central ||
                    key.Layer == CourtOfficeLayer.Military)
                    outcome = CourtService.TryFillRegisteredCentralVacancy(
                        pKingdom, key, session);
                else
                {
                    City city = World.world?.cities?.get(key.CityId);
                    outcome = LocalCourtAppointmentService.
                        TryFillRegisteredLocalVacancy(pKingdom, city, key,
                            session);
                }

                if (outcome == CourtVacancyOutcome.Filled)
                {
                    FailureYears.Remove(key);
                    NoCandidateMemos.Remove(key);
                    CourtVacancyEntry current = entries.First(entry =>
                        entry.Key.Equals(key));
                    CourtVacancyRegistry.Register(key,
                        current.MissingSeats - 1);
                    processedSteps++;
                    continue;
                }
                if (outcome == CourtVacancyOutcome.Invalid)
                {
                    FailureYears.Remove(key);
                    NoCandidateMemos.Remove(key);
                    CourtVacancyRegistry.Remove(key);
                }
                if (outcome == CourtVacancyOutcome.NoCandidate)
                {
                    // 「没人可补」是候选池漏人的唯一可观测症状 —— 池子是靠事件
                    // 维护的,不再定期重建,所以漏接了某个入池事件就长这样。
                    // 这时才重建一次(兜底),让下一次唤醒用新鲜名单再试。
                    //
                    // 同一代际只自愈一次:否则「重建 → 还是没人 → 再重建」会变成
                    // 每次唤醒都全量重建,比原来的定期重建还糟。重建本身也不递增
                    // 代际号,那会和 memo 互相触发。
                    if (TryRepairCandidatePool(pKingdom, generation))
                    {
                        processed.Add(key);
                        continue;
                    }
                    // 这一轮确实没人够格。记下当时的候选池代际与年份,池没变之前
                    // 不再为它重扫 —— 结果不会变。
                    NoCandidateMemos[key] = (generation, year);
                }
                if (outcome == CourtVacancyOutcome.TechnicalFailure)
                {
                    FailureYears[key] = year;
                    RetryTickets[pKingdom.id] = new RetryTicket
                    {
                        KingdomId = pKingdom.id,
                        NotBeforeFrame = Time.frameCount + 1
                    };
                    processed.Add(key);
                    // 一次写库失败就停掉本轮:后面的席位共用同一个候选会话和同一
                    // 条数据库连接,继续跑多半是同样的失败,而每个席位都要再扫一
                    // 遍候选。重试票会让其余席位下一帧再试。
                    if (CourtAppointmentFailureBackoffRules.
                        ShouldStopCurrentReconcile(pAttempted: true,
                            pCommitted: false)) break;
                }
                processed.Add(key);
            }
        }

        private static bool HasAttemptableEntry(
            IReadOnlyList<CourtVacancyEntry> pEntries, int pYear,
            int pGeneration)
        {
            for (int index = 0; index < pEntries.Count; index++)
            {
                if (pEntries[index].MissingSeats <= 0) continue;
                CourtVacancyKey key = pEntries[index].Key;
                if (!CourtAppointmentFailureBackoffRules.ShouldAttempt(
                        LastFailureYear(key), pYear)) continue;
                if (!ShouldRetryAfterNoCandidate(key, pGeneration, pYear))
                    continue;
                return true;
            }
            return false;
        }

        private static bool ShouldRetryAfterNoCandidate(CourtVacancyKey pKey,
            int pGeneration, int pYear)
        {
            bool hasMemo = NoCandidateMemos.TryGetValue(pKey,
                out (int Generation, int Year) memo);
            return CourtVacancyPoolMemoRules.ShouldRetry(hasMemo,
                memo.Generation, memo.Year, pGeneration, pYear);
        }

        /// <summary>
        ///     候选池的出错兜底:重建一次,并请求再跑一轮。
        ///     同一个王国的同一个代际只做一次,返回是否真的做了。
        ///
        ///     本轮的 <see cref="CourtCandidateSession"/> 已经把旧名单拿在手里了,
        ///     所以这里只能重建 + 重新入队,让**下一轮**用新表。
        /// </summary>
        private static bool TryRepairCandidatePool(Kingdom pKingdom,
            int pGeneration)
        {
            if (pKingdom?.data == null) return false;
            if (PoolRepairs.TryGetValue(pKingdom.id, out int repairedAt) &&
                repairedAt == pGeneration) return false;
            PoolRepairs[pKingdom.id] = pGeneration;
            OfficerCandidateCatalog.Invalidate(pKingdom);
            Request(pKingdom);
            return true;
        }

        private static int PoolGeneration(long pKingdomId)
        {
            return PoolGenerations.TryGetValue(pKingdomId, out int generation)
                ? generation
                : 0;
        }

        private static int LastFailureYear(CourtVacancyKey pKey)
        {
            return FailureYears.TryGetValue(pKey, out int year) ? year : -1;
        }

        private static void ForgetFailures(long pKingdomId)
        {
            var remove = new List<CourtVacancyKey>();
            foreach (CourtVacancyKey key in FailureYears.Keys)
                if (key.KingdomId == pKingdomId) remove.Add(key);
            for (int index = 0; index < remove.Count; index++)
                FailureYears.Remove(remove[index]);
            remove.Clear();
            foreach (CourtVacancyKey key in NoCandidateMemos.Keys)
                if (key.KingdomId == pKingdomId) remove.Add(key);
            for (int index = 0; index < remove.Count; index++)
                NoCandidateMemos.Remove(remove[index]);
            PoolGenerations.Remove(pKingdomId);
            PoolRepairs.Remove(pKingdomId);
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }
    }
}
