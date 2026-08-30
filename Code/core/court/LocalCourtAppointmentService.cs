using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using AncientWarfare3.core.db;
using AncientWarfare3.core.county;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class LocalCourtAppointmentService
    {
        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        private sealed class ActiveLocalOfficer
        {
            public long ActorId;
            public string OfficeId = "";
        }

        internal static CourtVacancyOutcome TryFillRegisteredLocalVacancy(
            Kingdom pKingdom, City pCity, CourtVacancyKey pVacancy,
            CourtCandidateSession pSession)
        {
            if (pKingdom?.data == null || pCity?.data == null ||
                pCity.isRekt() || pCity.kingdom != pKingdom ||
                pSession == null || pVacancy.KingdomId != pKingdom.id ||
                pVacancy.CityId != pCity.data.id)
                return CourtVacancyOutcome.Invalid;

            int year = Date.getCurrentYear();
            if (pVacancy.Layer == CourtOfficeLayer.County)
            {
                CountyFillDiagnostics.Count("attempt");
                if (pVacancy.OfficeId != CourtOfficeId.CountyMagistrate)
                    return CountyFillDiagnostics.Report("bad_office",
                        CourtVacancyOutcome.Invalid);
                if (pVacancy.CountyId < 0L)
                    return CountyFillDiagnostics.Report("bad_county_id",
                        CourtVacancyOutcome.Invalid);
                // 县还在不在、是不是在役,必须在这里判定。以前这一条藏在
                // CourtService.TryAssignCountyMagistrate 内部,和「写库失败」
                // 共用同一个 false —— 于是登记表里一个已经不存在的县会被判成
                // 可重试的 TechnicalFailure:条目不被摘除,每帧重挂一次重试票,
                // 每次重试都重扫一遍全国候选。这正是 8k 人口存档上那串固定周期
                // 卡顿的来源。死县是永久无效,必须报 Invalid 让它被摘掉。
                if (!CountyAdministrationService
                        .CountiesForCity(pCity.data.id)
                        .Any(county => county != null && county.Active &&
                             county.CountyId == pVacancy.CountyId))
                    return CountyFillDiagnostics.Report("county_inactive",
                        CourtVacancyOutcome.Invalid);
                if (LoadCountyOfficerIds(pKingdom.id, pCity.data.id)
                        .Contains(pVacancy.CountyId))
                    return CountyFillDiagnostics.Report("already_held",
                        CourtVacancyOutcome.Invalid);

                // 合格候选只取决于「王国 + 城」,与具体 CountyId 无关,所以整条
                // 筛选链按城缓存在本次 session 里。原来一个城有 N 个县就要把全国
                // 目录连同其中的逐人资格判定重跑 N 遍。
                //
                // 两遍:严格通道只看官员候选池(有功名/有品级),兜底才扫全量。
                // 三张表按 (城, 模式) 分别缓存。
                IReadOnlyList<Actor> countyCandidates =
                    pSession.CountyCandidatesForCity(pCity,
                        CountyCandidateMode.Strict,
                        () => BuildCountyCandidates(pSession, pKingdom, pCity,
                            pStrict: true, pLimit: CountyShortlistLimit));
                Actor countyCandidate = FirstAvailable(pSession,
                    countyCandidates, pVacancy);
                if (countyCandidate == null)
                {
                    CountyFillDiagnostics.Count("fallback_used", 1);
                    countyCandidates = pSession.CountyCandidatesForCity(pCity,
                        CountyCandidateMode.Fallback,
                        () => BuildCountyCandidates(pSession, pKingdom, pCity,
                            pStrict: false, pLimit: CountyShortlistLimit));
                    if (countyCandidates.Count == 0)
                        return CountyFillDiagnostics.Report("no_qualified",
                            CourtVacancyOutcome.NoCandidate);
                    // 占用是逐个座位变化的(Reserve 会往 ReservedActorIds 里加人),
                    // 所以可用性只能在取人的时候判,不进缓存。
                    countyCandidate = FirstAvailable(pSession,
                        countyCandidates, pVacancy);
                    // 短名单整条被占满 —— 它可能截断过,后面还有人。这时才付
                    // 全量重建的代价。
                    if (countyCandidate == null &&
                        countyCandidates.Count >= CountyShortlistLimit)
                    {
                        CountyFillDiagnostics.Count("shortlist_exhausted", 1);
                        countyCandidates = pSession.CountyCandidatesForCity(
                            pCity, CountyCandidateMode.FallbackExhaustive,
                            () => BuildCountyCandidates(pSession, pKingdom,
                                pCity, pStrict: false,
                                pLimit: int.MaxValue));
                        countyCandidate = FirstAvailable(pSession,
                            countyCandidates, pVacancy);
                    }
                }
                if (countyCandidate == null)
                    return CountyFillDiagnostics.Report("all_reserved",
                        CourtVacancyOutcome.NoCandidate);
                if (!CourtService.TryAssignCountyMagistrate(countyCandidate,
                        pKingdom, pCity, pVacancy.CountyId, true))
                    return CountyFillDiagnostics.Report("assign_failed",
                        CourtVacancyOutcome.TechnicalFailure);
                // 缓存下来的候选表在本轮内不会重建,所以刚上任的人必须显式登记
                // 为已占用。以前靠的是下一个县重跑 CanUseCandidateFacts 时读到
                // 他新写入的 COURT_OFFICE_ID —— 那条副作用现在不再每次都跑。
                pSession.Reserve(countyCandidate, pVacancy);
                return CountyFillDiagnostics.Report("filled",
                    CourtVacancyOutcome.Filled);
            }

            if (pVacancy.Layer != CourtOfficeLayer.City ||
                string.IsNullOrEmpty(pVacancy.OfficeId))
                return CourtVacancyOutcome.Invalid;
            if (!TryLoadActive(pKingdom.id, pCity.data.id,
                    out List<ActiveLocalOfficer> active))
                return CourtVacancyOutcome.TechnicalFailure;
            int desiredSeats = DesiredSeats(pKingdom, pCity,
                    ResolveCurrentCapacity(pKingdom, pCity))
                .Count(officeId => officeId == pVacancy.OfficeId);
            int occupiedSeats = active.Count(row =>
                row.OfficeId == pVacancy.OfficeId);
            if (desiredSeats <= occupiedSeats)
                return CourtVacancyOutcome.Invalid;

            long leaderNativeCityId = NativeCityId(pCity.leader);
            CountyFillDiagnostics.Count("city_attempt");
            // 与候选人无关的四问在循环外算一次,同时它也是行为类的来源 ——
            // 候选表按 (品级, 方镇标志, 通道) 缓存,同类的所有城、所有官职
            // 共用一张。见 CourtAppointmentContext / CandidatePoolBehavior。
            CourtAppointmentContext context = CourtAppointmentContext.Build(
                pKingdom, CourtOfficeLayer.City, pVacancy.OfficeId, pCity);
            // 两遍选择:先在官员候选池里按严格资历找,找不到才用空缺兜底扫全量。
            // 设计见 docs/superpowers/plans/2026-08-19-nine-rank-vacancy-fallback.md
            // Task 3 Step 4 —— 中央层一直是这么做的(CourtService 里那两遍),
            // 局部层曾经把严格通道整条丢了,于是科举资格和品级在城/县任命上
            // 完全不起作用,而且每个席位都要付全量代价。
            Actor candidate = SelectCandidate(pSession.StrictCandidates(
                    pKingdom, actor => CanUseCandidateFacts(actor, pKingdom)),
                pKingdom, pCity, leaderNativeCityId, pVacancy.OfficeId,
                pAllowVacancyPromotion: false, pSession, context,
                pStrict: true);
            if (candidate == null)
            {
                CountyFillDiagnostics.Count("city_fallback_used");
                candidate = SelectCandidate(pSession.FactsCandidates(
                        pKingdom,
                        actor => CanUseCandidateFacts(actor, pKingdom)),
                    pKingdom, pCity, leaderNativeCityId, pVacancy.OfficeId,
                    pAllowVacancyPromotion: true, pSession, context,
                    pStrict: false);
            }
            if (candidate == null)
                return CountyFillDiagnostics.Report("city_no_candidate",
                    CourtVacancyOutcome.NoCandidate);
            if (pVacancy.IsLocalChief)
            {
                bool committed = ManualLocalChiefAppointmentService.TryAppoint(
                    pKingdom, pCity, candidate, () =>
                        CourtService.TryAssignLocalOfficerRecord(candidate,
                            pKingdom, pCity, pVacancy.OfficeId, true));
                if (committed)
                {
                    pSession.Reserve(candidate, pVacancy);
                    CityCandidatePool.Remove(pKingdom, candidate);
                }
                return committed ? CourtVacancyOutcome.Filled :
                    CourtVacancyOutcome.TechnicalFailure;
            }
            if (!CourtService.TryAssignLocalOfficer(candidate, pKingdom,
                    pCity, pVacancy.OfficeId, true))
                return CountyFillDiagnostics.Report("city_assign_failed",
                    CourtVacancyOutcome.TechnicalFailure);
            // 候选表跨轮持久,刚上任的人必须显式摘出池 —— 他已经有官职了,
            // 下一轮不该再被选中。Reserve 只在本轮有效。
            pSession.Reserve(candidate, pVacancy);
            CityCandidatePool.Remove(pKingdom, candidate);
            return CountyFillDiagnostics.Report("city_filled",
                CourtVacancyOutcome.Filled);
        }

        /// <summary>
        /// 兜底通道候选短名单的长度上限。取到第一个可用的人就停,所以只需要
        /// 「本轮可能被占用的人数」的余量。短名单被取空时会退回全量重建
        /// (<see cref="CountyCandidateMode.FallbackExhaustive"/>),所以这个
        /// 上限不影响结果,只影响什么时候要多付一次。
        /// </summary>
        private const int CountyShortlistLimit = 64;

        private static class CountyCandidateMode
        {
            internal const int Strict = 0;
            internal const int Fallback = 1;
            internal const int FallbackExhaustive = 2;
        }

        private static Actor FirstAvailable(CourtCandidateSession pSession,
            IReadOnlyList<Actor> pCandidates, CourtVacancyKey pVacancy)
        {
            for (int index = 0; index < pCandidates.Count; index++)
                if (pSession.IsAvailable(pCandidates[index], pVacancy))
                    return pCandidates[index];
            return null;
        }

        /// <summary>
        /// 一个城的县令候选短名单,已按择优顺序(主属性降序、同分按 id 升序)排好。
        /// 每轮 Reconcile 按 (城, 模式) 各建一次。
        ///
        /// 逐级记账保留:候选被清零时必须知道是哪一级清零的。旧存档里县令一直
        /// 显示空缺却永不自动补人,而这条链路的每一环单独看都没有针对县的硬阻断,
        /// 所以只能按级观测。
        ///
        /// 单遍完成过滤 + 择优。以前是「三遍列表 + 三次分配 + 全量排序」,而实测
        /// 兜底通道里 pool / after_facts / after_qualified 三个数几乎完全相等
        /// (grade 30 的无资格兜底对所有人放行),等于为了取第一个人把两千多人
        /// 排了一遍序。
        /// </summary>
        /// <param name="pStrict">
        /// 严格通道:只在官员候选池(有功名/有品级)里找,且不开空缺兜底,
        /// 于是科举资格与品级真的起作用。兜底通道保留原样 —— 早期或落后国家
        /// 一个够格的都没有时,县令仍然补得上。
        /// </param>
        /// <param name="pLimit">
        /// 短名单上限。<see cref="int.MaxValue"/> 表示全量 —— 那时用排序而不是
        /// 逐个插入,否则退化成 O(n²)。
        /// </param>
        private static IReadOnlyList<Actor> BuildCountyCandidates(
            CourtCandidateSession pSession, Kingdom pKingdom, City pCity,
            bool pStrict, int pLimit)
        {
            IReadOnlyList<Actor> pool = pStrict
                ? pSession.StrictCandidates(pKingdom,
                    actor => CanUseCandidateFacts(actor, pKingdom))
                : pSession.FactsCandidates(pKingdom,
                    actor => CanUseCandidateFacts(actor, pKingdom));
            CountyFillDiagnostics.Count(pStrict ? "strict_pool" : "pool",
                pool.Count);

            bool bounded = pLimit != int.MaxValue;
            // 与候选人无关的四问在循环外算一次。原来每名候选都要重算科举/九品
            // 两问和品级、方镇两问,而内层是两三千人。
            CourtAppointmentContext context = CourtAppointmentContext.Build(
                pKingdom, CourtOfficeLayer.County,
                CourtOfficeId.CountyMagistrate, pCity);
            int officeGrade = context.OfficeGrade;
            // 门第档次只在最低一级地方官上起作用 —— 和城分支的 lowOffice 同一条。
            bool lowOffice = LocalLowOfficeVacancyRules.IsLowestLocalGrade(
                officeGrade);
            var shortlist = new List<Actor>();
            var abilities = new List<int>();
            var tiers = new List<int>();
            var unbounded = bounded
                ? null
                : new List<(Actor Actor, int Tier, int Ability)>();
            int factsCount = 0;
            int qualifiedCount = 0;
            int clanCount = 0;
            for (int index = 0; index < pool.Count; index++)
            {
                Actor actor = pool[index];
                if (actor?.data == null) continue;
                if (!CourtManualAppointmentRules.CanUseLayerCandidate(
                        CourtOfficeLayer.County, actor.isCityLeader()))
                    continue;
                factsCount++;
                CivilServiceQualificationRecord qualification =
                    pSession.Qualification(actor, pKingdom);
                if (!CivilServiceQualificationService.
                        CanReceiveFormalCivilAppointment(actor, pKingdom,
                            CourtOfficeLayer.County,
                            CourtOfficeId.CountyMagistrate,
                            pAllowVacancyPromotion: !pStrict,
                            qualification,
                            pQualificationsCaptured: true,
                            pAllowLocalLowerQualification: true,
                            pCity: pCity,
                            pServiceHistorySession: pSession,
                            pContext: context)) continue;
                qualifiedCount++;
                int tier = 0;
                if (lowOffice)
                {
                    bool formalLocalQualification = qualification != null &&
                        LocalOfficialCandidateRules.IsLocalQualification(
                            qualification.Qualification);
                    tier = (int)LocalLowOfficeVacancyRules.CandidateTier(
                        formalLocalQualification, HasClanOrShi(actor));
                    if (tier == (int)LocalLowOfficeCandidateTier.Clan)
                        clanCount++;
                }
                int ability = MainAbility(actor);
                if (bounded)
                    InsertRanked(shortlist, tiers, abilities, actor, tier,
                        ability, pLimit);
                else unbounded.Add((actor, tier, ability));
            }
            if (!pStrict)
            {
                CountyFillDiagnostics.Count("after_facts", factsCount);
                CountyFillDiagnostics.Count("clan_tier", clanCount);
            }
            CountyFillDiagnostics.Count(
                pStrict ? "strict_qualified" : "after_qualified",
                qualifiedCount);

            if (bounded) return shortlist;
            unbounded.Sort((left, right) =>
                CountyShortlistRules.SortsBefore(left.Tier, left.Ability,
                    left.Actor.data.id, right.Tier, right.Ability,
                    right.Actor.data.id)
                    ? -1
                    : 1);
            return unbounded.Select(entry => entry.Actor).ToList();
        }

        /// <summary>
        /// 把一个人插进按「门第档次升序、主属性降序、同分按 id 升序」排好的
        /// 短名单,超出上限就丢掉末尾。等价于对全体排序后取前 pLimit 个 ——
        /// id 唯一,所以这个序是全序,前缀唯一确定。
        /// </summary>
        private static void InsertRanked(List<Actor> pShortlist,
            List<int> pTiers, List<int> pAbilities, Actor pActor, int pTier,
            int pAbility, int pLimit)
        {
            long actorId = pActor.data.id;
            int count = pShortlist.Count;
            if (count >= pLimit &&
                CountyShortlistRules.CanSkipWhenFull(pTier, pAbility, actorId,
                    pTiers[count - 1], pAbilities[count - 1],
                    pShortlist[count - 1].data.id)) return;
            int position = count;
            while (position > 0 &&
                   !CountyShortlistRules.SortsBefore(pTiers[position - 1],
                       pAbilities[position - 1],
                       pShortlist[position - 1].data.id, pTier, pAbility,
                       actorId)) position--;
            pShortlist.Insert(position, pActor);
            pAbilities.Insert(position, pAbility);
            pTiers.Insert(position, pTier);
            if (pShortlist.Count > pLimit)
            {
                pShortlist.RemoveAt(pShortlist.Count - 1);
                pAbilities.RemoveAt(pAbilities.Count - 1);
                pTiers.RemoveAt(pTiers.Count - 1);
            }
        }

        internal static IReadOnlyList<CourtVacancyKey> DiscoverVacancies(
            Kingdom pKingdom, City pCity, int pCapacity, int pYear)
        {
            var result = new List<CourtVacancyKey>();
            if (pKingdom?.data == null || pCity?.data == null ||
                pCity.isRekt() || pCity.kingdom != pKingdom) return result;
            if (!TryLoadActive(pKingdom.id, pCity.data.id,
                    out List<ActiveLocalOfficer> active)) return result;
            IReadOnlyList<string> seats = DesiredSeats(pKingdom, pCity,
                Math.Max(0, pCapacity));
            var occupied = active.GroupBy(row => row.OfficeId,
                    StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(),
                    StringComparer.Ordinal);
            foreach (string officeId in seats)
            {
                occupied.TryGetValue(officeId, out int count);
                if (count > 0)
                {
                    occupied[officeId] = count - 1;
                    continue;
                }
                result.Add(new CourtVacancyKey(pKingdom.id, pCity.data.id,
                    -1L, CourtOfficeLayer.City, officeId,
                    pIsLocalChief: officeId == CourtService.ResolveCityOffice(
                        pKingdom, pCity)));
            }
            result.AddRange(DiscoverCountyVacancies(pKingdom, pCity));
            return result;
        }

        /// <summary>
        /// 只找县级空缺。抽出来是为了让轮转扫描能单独调用 —— 它不需要城内席位
        /// 那一段(那段要额外一次 TryLoadActive 查询和容量计算,而城内席位本来
        /// 就有多条发现路径)。
        /// </summary>
        internal static IReadOnlyList<CourtVacancyKey> DiscoverCountyVacancies(
            Kingdom pKingdom, City pCity)
        {
            var result = new List<CourtVacancyKey>();
            if (pKingdom?.data == null || pCity?.data == null ||
                pCity.isRekt() || pCity.kingdom != pKingdom) return result;
            HashSet<long> occupiedCounties = LoadCountyOfficerIds(
                pKingdom.id, pCity.data.id);
            foreach (CountyRecord county in CountyAdministrationService.
                         CountiesForCity(pCity.data.id))
            {
                if (county == null || !county.Active || county.CountyId < 0L)
                    continue;
                if (occupiedCounties.Contains(county.CountyId)) continue;
                result.Add(new CourtVacancyKey(pKingdom.id, pCity.data.id,
                    county.CountyId, CourtOfficeLayer.County,
                    CourtOfficeId.CountyMagistrate));
            }
            return result;
        }

        private static HashSet<long> LoadCountyOfficerIds(long pKingdomId,
            long pCityId)
        {
            var result = new HashSet<long>();
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT COUNTY_ID FROM " +
                    CourtOfficerTableItem.GetTableName() +
                    " WHERE KINGDOM_ID=@kingdom AND CITY_ID=@city " +
                    "AND LAYER=@layer AND OFFICE_ID=@office AND ACTIVE=1";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@city", pCityId);
                command.Parameters.AddWithValue("@layer", CourtOfficeLayer.County);
                command.Parameters.AddWithValue("@office",
                    CourtOfficeId.CountyMagistrate);
                using SQLiteDataReader reader = command.ExecuteReader();
                // IsDBNull 原本写在循环条件里:遇到一行 COUNTY_ID 为 NULL 就整
                // 个中断,后面的行全被静默丢掉,已占用的县会被误判成空缺。
                while (reader.Read())
                {
                    if (reader.IsDBNull(0)) continue;
                    result.Add(Convert.ToInt64(reader.GetValue(0)));
                }
            }
            catch { }
            return result;
        }

        private static List<string> DesiredSeats(Kingdom pKingdom, City pCity,
            int pCapacity)
        {
            return LocalChiefOfficeResolver.ResolveOrderedSeats(pKingdom,
                pCity, pCapacity).ToList();
        }

        private static int ResolveCurrentCapacity(Kingdom pKingdom,
            City pCity)
        {
            try
            {
                return CourtRules.CityOfficeSlots(
                    pCity.getPopulationPeople(), pCity.countZones(),
                    pKingdom.capital == pCity);
            }
            catch { return 0; }
        }

        private static bool TryLoadActive(long pKingdomId, long pCityId,
            out List<ActiveLocalOfficer> pRows)
        {
            pRows = new List<ActiveLocalOfficer>();
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT ACTOR_ID,OFFICE_ID FROM " +
                    CourtOfficerTableItem.GetTableName() +
                    " WHERE KINGDOM_ID=@kingdom AND CITY_ID=@city " +
                    "AND LAYER=@layer AND ACTIVE=1 " +
                    "ORDER BY APPOINTED_TIME,OFFICER_ID LIMIT 64";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@city", pCityId);
                command.Parameters.AddWithValue("@layer",
                    CourtOfficeLayer.City);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                    pRows.Add(new ActiveLocalOfficer
                    {
                        ActorId = Convert.ToInt64(reader.GetValue(0)),
                        OfficeId = reader.IsDBNull(1)
                            ? ""
                            : Convert.ToString(reader.GetValue(1)) ?? ""
                    });
                return true;
            }
            catch
            {
                pRows.Clear();
                return false;
            }
        }

        private static bool CanUseCandidateFacts(Actor pActor,
            Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                !pActor.isSexMale() || pActor.hasTrait("madness") ||
                pActor.isCityLeader() ||
                !CourtAffiliationResolver.CanServe(pActor, pKingdom,
                    CourtOfficeLayer.City) ||
                !RoyalGuardOfficeRules.CanAppearInOfficeCandidateList(
                    RoyalGuardService.IsRoyalGuard(pActor)) ||
                !RoyalAsylumRules.CanPerformProtectedRole(
                    RoyalAsylumService.IsActive(pActor))) return false;
            pActor.data.get(LineageKeys.COURT_OFFICE_ID,
                out string officeId, "");
            return LocalOfficialCandidateRules.CanEnter(
                pActor.isAlive() && !pActor.isRekt(), pActor.isAdult(),
                SlaveService.IsSlave(pActor),
                !string.IsNullOrEmpty(officeId), pActor.isKing(),
                HeirService.PeekRegisteredHeir(pKingdom) == pActor,
                examinationEnabled: false, qualification: "none",
                participatedAndFailedHigherStage: false);
        }

        internal static void ClearRuntime()
        {
            OfficerCandidateCatalog.ClearRuntime();
            AncientWarfare3.core.lineage.SuccessionPoolService.ClearRuntime();
            AncientWarfare3.core.lineage.HeirService.ClearSuccessionPoolRepairs();
            AncientWarfare3.core.lineage.GeneralService.ClearCandidatePools();
        }

        /// <summary>
        /// 从候选池里取这个席位最合适的人。
        ///
        /// 顺序**按行为类定一次**:候选表按 (品级, 方镇标志, 通道) 缓存,
        /// 同类的所有城、所有官职共用一张。之后每个席位只扫这张表的**表头**,
        /// 把按城变化的籍贯加成算回去 —— 不再每个席位把整个候选池重扫一遍、
        /// 逐人重算档次与评分。
        ///
        /// 表头能扫多短由 <see cref="CityShortlistRules"/> 决定:籍贯加成有
        /// 上限,所以答案必然落在「无加成分 &gt;= 最佳有加成分 - 上限」这一段
        /// 里,再往后的人拿满加成也追不上。截断不改变结果。
        ///
        /// 占用只能在取人时判:<c>Reserve</c> 会随着席位一个个填上而变化,
        /// 建表时滤掉就等于把表绑死在建表那一刻。
        /// </summary>
        private static Actor SelectCandidate(IReadOnlyList<Actor> pCandidates,
            Kingdom pKingdom, City pCity, long pLeaderNativeCityId,
            string pOfficeId, bool pAllowVacancyPromotion,
            CourtCandidateSession pSession, CourtAppointmentContext pContext,
            bool pStrict)
        {
            CityCandidatePool.Table table;
            if (pSession == null)
            {
                table = BuildCityCandidates(pCandidates, pKingdom, pCity,
                    pOfficeId, pAllowVacancyPromotion, null, pContext);
            }
            else
            {
                var behavior = new CandidatePoolBehavior(pContext.OfficeGrade,
                    pContext.RegionalGovernor, pAllowVacancyPromotion,
                    pStrict);
                // 跨轮持久:顺序算一次就存着,之后靠事件补入/摘出维护,
                // 不再每轮重建。见 CityCandidatePool。
                table = CityCandidatePool.GetOrBuild(pKingdom, behavior,
                    () => BuildCityCandidates(pCandidates, pKingdom, pCity,
                        pOfficeId, pAllowVacancyPromotion, pSession,
                        pContext));
            }

            int picked = PickForCity(table, pLeaderNativeCityId, pSession,
                pKingdom);
            return picked < 0 ? null : table.Actors[picked];
        }

        /// <summary>
        /// 在共享表上把籍贯加成算回去,取第一名。
        ///
        /// 表按无加成分降序排好,所以一旦「下一个人拿满加成也追不上当前最佳」
        /// 就可以停 —— 见 <see cref="CityShortlistRules"/>。跨到更差的门第档次
        /// 同样可以停:档次优先于分,而加成不改变档次。
        ///
        /// 表是跨轮持久的,所以取人时必须逐个复核基础事实:池子靠事件维护,
        /// 可能多收了已经不合格的人(死了、离境了、已上任)。多收由这里滤掉,
        /// 少收由「补不上 → 重建」兜住,两个方向都能自愈。
        /// </summary>
        private static int PickForCity(CityCandidatePool.Table pTable,
            long pLeaderNativeCityId, CourtCandidateSession pSession,
            Kingdom pKingdom)
        {
            if (pTable == null) return -1;
            int best = -1;
            int bestTier = 0;
            int bestScore = 0;
            List<Actor> stale = null;
            for (int index = 0; index < pTable.Actors.Count; index++)
            {
                if (best >= 0 && pTable.Tiers[index] > bestTier) break;
                if (best >= 0 && pTable.Tiers[index] == bestTier &&
                    !CityShortlistRules.NeedsMoreForHometownBonus(bestScore,
                        pTable.Scores[index],
                        LocalOfficialCandidateRules.HometownBonus)) break;
                Actor actor = pTable.Actors[index];
                if (actor?.data == null) continue;
                if (pSession != null &&
                    pSession.ReservedActorIds.Contains(pTable.Ids[index]))
                    continue;
                // 持久池可能多收 —— 复核基础事实,不合格的顺手摘掉。
                if (!CanUseCandidateFacts(actor, pKingdom))
                {
                    (stale ??= new List<Actor>()).Add(actor);
                    continue;
                }

                int score = pTable.Scores[index] +
                    (pLeaderNativeCityId >= 0L &&
                     pTable.NativeCityIds[index] == pLeaderNativeCityId
                        ? LocalOfficialCandidateRules.HometownBonus
                        : 0);
                if (best < 0 || CityShortlistRules.SortsBefore(
                        pTable.Tiers[index], score, pTable.Ids[index],
                        bestTier, bestScore, pTable.Ids[best]))
                {
                    best = index;
                    bestTier = pTable.Tiers[index];
                    bestScore = score;
                }
            }

            // 摘人会换表,所以必须在遍历结束后做 —— 正在遍历的是旧表。
            // 选中的那个人由 Reserve 负责,不在这里摘。
            if (stale != null)
                for (int index = 0; index < stale.Count; index++)
                    CityCandidatePool.Remove(pKingdom, stale[index]);
            return best;
        }

        /// <summary>
        /// 一个**行为类**的城官候选表,按「门第档次升序、无加成分降序、
        /// 同分按 id 升序」排好。籍贯加成不进表 —— 它按城变,由
        /// <see cref="PickForCity"/> 在取人时补回。
        ///
        /// 和县令短名单共用 <see cref="CountyShortlistRules"/> 那一个比较器,
        /// 所以两条分支的择优口径是同一套。
        ///
        /// 不截断:这张表要服务同类的所有城、所有席位,截断会让后面的城取不到
        /// 本该属于它的人。塌缩本身已经把建表次数从几十降到个位数,一次全排
        /// 比几十次有界插入便宜。
        /// </summary>
        private static CityCandidatePool.Table
            BuildCityCandidates(IReadOnlyList<Actor> pPool, Kingdom pKingdom,
                City pCity, string pOfficeId, bool pAllowVacancyPromotion,
                CourtCandidateSession pSession,
                CourtAppointmentContext pContext)
        {
            int officeGrade = pContext.OfficeGrade;
            bool regionalGovernor = pContext.RegionalGovernor;
            bool nineRankSystem = pContext.NineRankSystem;
            bool lowOffice = LocalLowOfficeVacancyRules.IsLowestLocalGrade(
                officeGrade);
            var table = new CityCandidatePool.Table();
            // 建表次数和累计遍历行数。按行为类缓存后,city_build 应当远小于
            // city_attempt —— 它们相等就说明塌缩没生效。
            CountyFillDiagnostics.Count("city_build");
            CountyFillDiagnostics.Count("city_build_rows", pPool?.Count ?? 0);
            var entries =
                new List<(Actor Actor, int Tier, int Score, long Native)>();
            foreach (Actor actor in pPool)
            {
                if (actor?.data == null) continue;
                // 资格记录按轮缓存,顺带喂给资格判定 —— 否则这一个席位就要为每个
                // 幸存候选各发一条 SQL,而同城的每个席位都要重发一遍。
                CivilServiceQualificationRecord qualification =
                    pSession?.Qualification(actor, pKingdom);
                if (!CivilServiceQualificationService.
                        CanReceiveFormalCivilAppointment(actor, pKingdom,
                            CourtOfficeLayer.City, pOfficeId,
                            pAllowVacancyPromotion, qualification,
                            pQualificationsCaptured: pSession != null,
                            pAllowLocalLowerQualification: true,
                            pCity: pCity,
                            pServiceHistorySession: pSession,
                            pContext: pContext)) continue;
                actor.data.get(LineageKeys.OFFICER_MERIT,
                    out float merit, 0f);
                bool formalLocalQualification = pSession != null
                    ? qualification != null &&
                      LocalOfficialCandidateRules.IsLocalQualification(
                          qualification.Qualification)
                    : HasFormalLocalQualification(actor, pKingdom);
                // 籍贯加成留到取人时算 —— 它是这里唯一按城变的项。
                int score = LocalOfficialCandidateRules.Score(
                    MainAbility(actor), (int)Math.Max(0f, merit),
                    sameNativeCity: false);
                int tier = lowOffice
                    ? (int)LocalLowOfficeVacancyRules.CandidateTier(
                        formalLocalQualification,
                        HasClanOrShi(actor))
                    : 0;
                if (lowOffice && pAllowVacancyPromotion)
                {
                    int resolvedRank =
                        OfficialCareerRankRules.ResolveLocalVacancyPromotionRank(
                            OfficialCareerStateService.ReadRankFast(actor),
                            officeGrade, nineRankSystem,
                            formalLocalQualification ||
                            LocalLowOfficeVacancyRules.CanUseUnqualifiedFallback(
                                isCityLayer: true, officeGrade: officeGrade,
                                vacancyPromotion: true),
                            vacancyPromotion: true,
                            regionalGovernor: regionalGovernor);
                    score += Math.Max(0, resolvedRank);
                }

                entries.Add((actor, tier, score, NativeCityId(actor)));
            }

            entries.Sort((left, right) =>
                CountyShortlistRules.SortsBefore(left.Tier, left.Score,
                    left.Actor.data.id, right.Tier, right.Score,
                    right.Actor.data.id)
                    ? -1
                    : 1);
            for (int index = 0; index < entries.Count; index++)
            {
                table.Actors.Add(entries[index].Actor);
                table.Tiers.Add(entries[index].Tier);
                table.Scores.Add(entries[index].Score);
                table.Ids.Add(entries[index].Actor.data.id);
                table.NativeCityIds.Add(entries[index].Native);
                table.Members.Add(entries[index].Actor.data.id);
            }

            return table;
        }

        /// <summary>
        /// 一个人在某个行为类下的排序键 —— 事件补入时只算他一个人的这一份。
        /// 判据和 <see cref="BuildCityCandidates"/> 的循环体逐行对应,两处必须
        /// 保持一致,否则插进去的位置和全量建表的位置会不同。
        ///
        /// 档次返回负数表示「这个行为类不收他」,调用方跳过。
        /// </summary>
        internal static CityCandidatePool.Ranked RankForBehavior(Actor pActor,
            Kingdom pKingdom, CandidatePoolBehavior pBehavior)
        {
            if (pActor?.data == null || pKingdom?.data == null)
                return new CityCandidatePool.Ranked(-1, 0);
            if (!CanUseCandidateFacts(pActor, pKingdom))
                return new CityCandidatePool.Ranked(-1, 0);
            // 严格通道只收官员候选池的人:有功名或有品级。
            if (pBehavior.Strict && !IsStrictPoolMember(pActor, pKingdom))
                return new CityCandidatePool.Ranked(-1, 0);
            var context = new CourtAppointmentContext(pBehavior.OfficeGrade,
                pBehavior.RegionalGovernor, pKingdom);
            if (!CivilServiceQualificationService.
                    CanReceiveFormalCivilAppointment(pActor, pKingdom,
                        CourtOfficeLayer.City, pOfficeId: null,
                        pBehavior.VacancyPromotion, pQualification: null,
                        pQualificationsCaptured: false,
                        pAllowLocalLowerQualification: true, pCity: null,
                        pServiceHistorySession: null, pContext: context))
                return new CityCandidatePool.Ranked(-1, 0);
            pActor.data.get(LineageKeys.OFFICER_MERIT, out float merit, 0f);
            bool formalLocalQualification = HasFormalLocalQualification(
                pActor, pKingdom);
            int score = LocalOfficialCandidateRules.Score(MainAbility(pActor),
                (int)Math.Max(0f, merit), sameNativeCity: false);
            bool lowOffice = LocalLowOfficeVacancyRules.IsLowestLocalGrade(
                pBehavior.OfficeGrade);
            int tier = lowOffice
                ? (int)LocalLowOfficeVacancyRules.CandidateTier(
                    formalLocalQualification, HasClanOrShi(pActor))
                : 0;
            if (lowOffice && pBehavior.VacancyPromotion)
            {
                int resolvedRank =
                    OfficialCareerRankRules.ResolveLocalVacancyPromotionRank(
                        OfficialCareerStateService.ReadRankFast(pActor),
                        pBehavior.OfficeGrade,
                        CourtService.HasNineRankSystem(pKingdom),
                        formalLocalQualification ||
                        LocalLowOfficeVacancyRules.CanUseUnqualifiedFallback(
                            isCityLayer: true,
                            officeGrade: pBehavior.OfficeGrade,
                            vacancyPromotion: true),
                        vacancyPromotion: true,
                        regionalGovernor: pBehavior.RegionalGovernor);
                score += Math.Max(0, resolvedRank);
            }

            return new CityCandidatePool.Ranked(tier, score);
        }

        /// <summary>
        /// 严格池的成员判定:有地方科举功名,或已有品级。和
        /// <see cref="CourtCandidateSession.StrictCandidates"/> 的两部分并集
        /// 对应 —— 那里一半来自索引查询,这里按人现判,结论相同。
        /// </summary>
        private static bool IsStrictPoolMember(Actor pActor, Kingdom pKingdom)
        {
            if (OfficialCareerStateService.ReadRankFast(pActor) >
                OfficialCareerRankRules.Unranked) return true;
            return HasFormalLocalQualification(pActor, pKingdom);
        }

        /// <summary>
        /// 候选池的维护入口:一个人的状态变了,把他在各行为类表里的位置更新掉。
        /// 只算他一个人,不重建整池。
        /// </summary>
        internal static void OnCandidateChanged(Kingdom pKingdom,
            Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null) return;
            CityCandidatePool.Reposition(pKingdom, pActor,
                behavior => RankForBehavior(pActor, pKingdom, behavior),
                NativeCityId(pActor));
        }

        /// <summary>一个人彻底出池(死亡、离境)。</summary>
        internal static void OnCandidateLost(Kingdom pKingdom, Actor pActor)
        {
            CityCandidatePool.Remove(pKingdom, pActor);
        }

        private static bool HasFormalLocalQualification(Actor pActor,
            Kingdom pKingdom)
        {
            CivilServiceQualificationRecord qualification =
                CivilServiceQualificationService.LoadOrRepair(pActor,
                    pKingdom);
            return qualification != null &&
                   LocalOfficialCandidateRules.IsLocalQualification(
                       qualification.Qualification);
        }

        private static bool HasClanOrShi(Actor pActor)
        {
            if (pActor?.data == null) return false;
            try
            {
                if (pActor.hasClan() && pActor.clan?.data != null)
                    return true;
            }
            catch { }
            pActor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            return shiId >= 0L;
        }

        private static Actor FindActor(long pActorId)
        {
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static long NativeCityId(Actor pActor)
        {
            if (pActor?.data == null) return -1L;
            pActor.data.get(LineageKeys.OFFICER_NATIVE_CITY_ID,
                out long cityId, -1L);
            return cityId;
        }

        private static int MainAbility(Actor pActor)
        {
            try
            {
                return (int)Math.Max(Math.Max(
                        pActor.stats?["intelligence"] ?? 0f,
                        pActor.stats?["stewardship"] ?? 0f),
                    Math.Max(pActor.stats?["warfare"] ?? 0f,
                        pActor.stats?["diplomacy"] ?? 0f));
            }
            catch { return 0; }
        }
    }
}
