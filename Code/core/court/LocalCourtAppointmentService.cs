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
            // 两遍选择:先在官员候选池里按严格资历找,找不到才用空缺兜底扫全量。
            // 设计见 docs/superpowers/plans/2026-08-19-nine-rank-vacancy-fallback.md
            // Task 3 Step 4 —— 中央层一直是这么做的(CourtService 里那两遍),
            // 局部层曾经把严格通道整条丢了,于是科举资格和品级在城/县任命上
            // 完全不起作用,而且每个席位都要付全量代价。
            Actor candidate = SelectCandidate(pSession.StrictCandidates(
                    pKingdom, actor => CanUseCandidateFacts(actor, pKingdom)),
                pKingdom, pCity, leaderNativeCityId, pVacancy.OfficeId,
                pAllowVacancyPromotion: false, pSession);
            if (candidate == null)
                candidate = SelectCandidate(pSession.FactsCandidates(
                        pKingdom,
                        actor => CanUseCandidateFacts(actor, pKingdom)),
                    pKingdom, pCity, leaderNativeCityId, pVacancy.OfficeId,
                    pAllowVacancyPromotion: true, pSession);
            if (candidate == null)
                return CourtVacancyOutcome.NoCandidate;
            if (pVacancy.IsLocalChief)
            {
                bool committed = ManualLocalChiefAppointmentService.TryAppoint(
                    pKingdom, pCity, candidate, () =>
                        CourtService.TryAssignLocalOfficerRecord(candidate,
                            pKingdom, pCity, pVacancy.OfficeId, true));
                if (committed) pSession.Reserve(candidate, pVacancy);
                return committed ? CourtVacancyOutcome.Filled :
                    CourtVacancyOutcome.TechnicalFailure;
            }
            if (!CourtService.TryAssignLocalOfficer(candidate, pKingdom,
                    pCity, pVacancy.OfficeId, true))
                return CourtVacancyOutcome.TechnicalFailure;
            // 候选表按轮缓存,刚上任的人必须显式登记占用 —— 原来靠下一个席位
            // 重跑 CanUseCandidateFacts 时读到他新写入的 COURT_OFFICE_ID。
            pSession.Reserve(candidate, pVacancy);
            return CourtVacancyOutcome.Filled;
        }

        /// <summary>
        /// 兜底通道候选短名单的长度上限。取到第一个可用的人就停,所以只需要
        /// 「本轮可能被占用的人数」的余量。短名单被取空时会退回全量重建
        /// (<see cref="CountyCandidateMode.FallbackExhaustive"/>),所以这个
        /// 上限不影响结果,只影响什么时候要多付一次。
        /// </summary>
        private const int CountyShortlistLimit = 64;

        /// <summary>
        /// 城官候选短名单的长度上限。同一个城同一种官职的席位数远小于这个数,
        /// 整条被占满时会退回全量重建,所以这个上限不影响结果。
        /// </summary>
        private const int CityShortlistLimit = 64;

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
            int officeGrade = OfficialCareerStateService.OfficeGradeForOffice(
                pKingdom, CourtOfficeLayer.County,
                CourtOfficeId.CountyMagistrate, pCity);
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
                            pServiceHistorySession: pSession)) continue;
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
        }

        /// <summary>
        /// 从候选池里取下一个可用的人。
        ///
        /// 顺序**只定一次**:按 (城, 官职, 通道) 把候选排好缓存在本轮 session 里,
        /// 之后同城同职的每个席位直接顺着往下取第一个没被占用的 —— 不再每个席位
        /// 把整个候选池重扫一遍、逐人重算档次与评分。
        ///
        /// 占用只能在取人时判:<c>Reserve</c> 会随着席位一个个填上而变化,建表时
        /// 滤掉就等于把表绑死在建表那一刻。
        /// </summary>
        private static Actor SelectCandidate(IReadOnlyList<Actor> pCandidates,
            Kingdom pKingdom, City pCity, long pLeaderNativeCityId,
            string pOfficeId, bool pAllowVacancyPromotion,
            CourtCandidateSession pSession)
        {
            if (pSession == null)
                return FirstUnreserved(null, BuildCityCandidates(pCandidates,
                    pKingdom, pCity, pLeaderNativeCityId, pOfficeId,
                    pAllowVacancyPromotion, null, int.MaxValue));
            IReadOnlyList<Actor> ordered = pSession.CityCandidatesFor(pCity,
                pOfficeId, CityCandidateMode(pAllowVacancyPromotion,
                    pExhaustive: false),
                () => BuildCityCandidates(pCandidates, pKingdom, pCity,
                    pLeaderNativeCityId, pOfficeId, pAllowVacancyPromotion,
                    pSession, CityShortlistLimit));
            Actor picked = FirstUnreserved(pSession, ordered);
            if (picked != null || ordered.Count < CityShortlistLimit)
                return picked;
            // 短名单整条被占满 —— 它可能截断过,后面还有人。
            ordered = pSession.CityCandidatesFor(pCity, pOfficeId,
                CityCandidateMode(pAllowVacancyPromotion, pExhaustive: true),
                () => BuildCityCandidates(pCandidates, pKingdom, pCity,
                    pLeaderNativeCityId, pOfficeId, pAllowVacancyPromotion,
                    pSession, int.MaxValue));
            return FirstUnreserved(pSession, ordered);
        }

        /// <summary>候选表按「通道 + 是否全量」分开缓存,四种互不覆盖。</summary>
        private static int CityCandidateMode(bool pAllowVacancyPromotion,
            bool pExhaustive)
        {
            return (pAllowVacancyPromotion ? 1 : 0) + (pExhaustive ? 2 : 0);
        }

        /// <summary>
        /// 城官候选短名单,按「门第档次升序、评分降序、同分按 id 升序」排好。
        /// 和县令短名单共用 <see cref="CountyShortlistRules"/> 那一个比较器,
        /// 所以两条分支的择优口径是同一套。
        /// </summary>
        private static IReadOnlyList<Actor> BuildCityCandidates(
            IReadOnlyList<Actor> pPool, Kingdom pKingdom, City pCity,
            long pLeaderNativeCityId, string pOfficeId,
            bool pAllowVacancyPromotion, CourtCandidateSession pSession,
            int pLimit)
        {
            int officeGrade = OfficialCareerStateService.OfficeGradeForOffice(
                pKingdom, CourtOfficeLayer.City, pOfficeId, pCity);
            bool regionalGovernor = OfficialCareerStateService.
                IsRegionalGovernorSeat(pKingdom, CourtOfficeLayer.City,
                    pOfficeId, pCity);
            bool lowOffice = LocalLowOfficeVacancyRules.IsLowestLocalGrade(
                officeGrade);
            bool bounded = pLimit != int.MaxValue;
            var shortlist = new List<Actor>();
            var scores = new List<int>();
            var tiers = new List<int>();
            var unbounded = bounded
                ? null
                : new List<(Actor Actor, int Tier, int Ability)>();
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
                            pServiceHistorySession: pSession)) continue;
                actor.data.get(LineageKeys.OFFICER_MERIT,
                    out float merit, 0f);
                bool formalLocalQualification = pSession != null
                    ? qualification != null &&
                      LocalOfficialCandidateRules.IsLocalQualification(
                          qualification.Qualification)
                    : HasFormalLocalQualification(actor, pKingdom);
                int score = LocalOfficialCandidateRules.Score(
                    MainAbility(actor), (int)Math.Max(0f, merit),
                    pLeaderNativeCityId >= 0L &&
                    NativeCityId(actor) == pLeaderNativeCityId);
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
                            officeGrade, CourtService.HasNineRankSystem(
                                pKingdom),
                            formalLocalQualification ||
                            LocalLowOfficeVacancyRules.CanUseUnqualifiedFallback(
                                isCityLayer: true, officeGrade: officeGrade,
                                vacancyPromotion: true),
                            vacancyPromotion: true,
                            regionalGovernor: regionalGovernor);
                    score += Math.Max(0, resolvedRank);
                }

                if (bounded)
                    InsertRanked(shortlist, tiers, scores, actor, tier, score,
                        pLimit);
                else unbounded.Add((actor, tier, score));
            }

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
        /// 顺着排好的表取第一个没被占用的人。城分支判的是
        /// <see cref="CourtCandidateSession.ReservedActorIds"/> 本身,和原来
        /// 逐席位扫描时的判据一致 —— 不走县分支那条允许兼任的
        /// <c>IsAvailable</c>。
        /// </summary>
        private static Actor FirstUnreserved(CourtCandidateSession pSession,
            IReadOnlyList<Actor> pCandidates)
        {
            for (int index = 0; index < pCandidates.Count; index++)
            {
                Actor actor = pCandidates[index];
                if (actor?.data == null) continue;
                if (pSession != null &&
                    pSession.ReservedActorIds.Contains(actor.data.id)) continue;
                return actor;
            }

            return null;
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
