using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.court
{
    internal sealed class CourtCandidateSession
    {
        internal readonly IReadOnlyList<Actor> Actors;
        internal readonly HashSet<long> ReservedActorIds;
        /// <summary>
        ///     本轮共享的候选评估缓存(资格 + 评分)。**必须按轮共享** ——
        ///     原来 TryFillRegisteredCentralVacancy 每个席位 new 一个,于是
        ///     GetCachedEligibility / GetCachedScore 跨席位一条也没命中,每个空缺
        ///     都要把全国名单连同其中的逐人数据库读重跑一遍。
        /// </summary>
        internal readonly CourtService.CandidateSelectionCache SelectionCache =
            new CourtService.CandidateSelectionCache();
        private SchoolGuestOfficeService.VacancyCandidateSession
            _guestCandidates;
        private Dictionary<(long CityId, int Mode), IReadOnlyList<Actor>>
            _countyCandidatesByCity;
        private Dictionary<int, CityCandidateTable> _cityCandidatesByBehavior;
        private List<Actor> _roster;
        private HashSet<string> _activeOfficeIds;
        private Dictionary<long, CivilServiceQualificationRecord>
            _qualifications;
        private List<Actor> _factsCandidates;
        private List<Actor> _strictCandidates;
        private Dictionary<long, CourtServiceHistory> _serviceHistories;

        internal CourtCandidateSession(Kingdom pKingdom)
        {
            // 目录本身已按「国家+年份」缓存,这里原本再 ToArray() 复制一份整表,
            // 等于每补一个座位就全量拷贝一次候选名单。Actors 已经是
            // IReadOnlyList,而三个消费点(LocalCourtAppointmentService 的 LINQ
            // 链、SelectCandidate 的 foreach、CourtService 里自己再 ToList 的那处)
            // 全是只读,所以直接引用缓存列表即可。
            Actors = OfficerCandidateCatalog.GetOrBuild(pKingdom);
            ReservedActorIds = CourtService.
                BuildActiveOfficerActorSetForKingdom(pKingdom);
        }

        internal bool IsAvailable(Actor pActor, CourtVacancyKey pVacancy)
        {
            return pActor?.data != null &&
                   (!ReservedActorIds.Contains(pActor.data.id) ||
                    CourtService.IsExplicitConcurrentOffice(pActor,
                        pVacancy));
        }

        internal void Reserve(Actor pActor, CourtVacancyKey pVacancy)
        {
            if (pActor?.data != null &&
                !CourtService.IsExplicitConcurrentOffice(pActor, pVacancy))
                ReservedActorIds.Add(pActor.data.id);
        }

        /// <summary>
        ///     名单的 List 视图,本轮只物化一次。中央补缺要 List&lt;Actor&gt;,原来
        ///     每个席位 <c>pSession.Actors.ToList()</c> 一次,等于每补一个座位就
        ///     全量拷贝一遍全国候选目录。
        /// </summary>
        internal List<Actor> Roster => _roster ??= new List<Actor>(Actors);

        /// <summary>
        ///     本轮已被占用的中央/军职 office id。<c>FillCentralOffice</c> 提交成功
        ///     时会往里加,所以按轮缓存不会漏掉刚补上的席位。
        /// </summary>
        internal HashSet<string> ActiveOfficeIds(Kingdom pKingdom)
        {
            return _activeOfficeIds ??=
                CourtService.BuildActiveOfficeSet(pKingdom, Roster);
        }

        /// <summary>
        ///     科举资格记录,本轮每人只读一次。
        ///
        ///     <c>LoadOrRepair</c> 的快路径只有在此人**已有**资格时才命中;绝大多数
        ///     没资格的候选每次都会落到一条 SQL。而 <c>SelectCandidate</c> 对每个
        ///     幸存候选都要问一次(HasFormalLocalQualification),每个席位重问一遍。
        /// </summary>
        internal CivilServiceQualificationRecord Qualification(Actor pActor,
            Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null) return null;
            _qualifications ??=
                new Dictionary<long, CivilServiceQualificationRecord>();
            long actorId = pActor.data.id;
            if (_qualifications.TryGetValue(actorId,
                    out CivilServiceQualificationRecord cached)) return cached;
            CivilServiceQualificationRecord record =
                CivilServiceQualificationService.LoadOrRepair(pActor, pKingdom);
            _qualifications[actorId] = record;
            return record;
        }

        /// <summary>
        ///     通过「基础事实」闸门的候选,本轮只筛一次。
        ///
        ///     <c>CanUseCandidateFacts</c> 只吃 (actor, kingdom),和席位、城、县
        ///     都无关,可整轮共用。原来城分支的 <c>SelectCandidate</c> 对**每个
        ///     席位**都把全国目录重扫一遍,而这一关每人要跑 PeekRegisteredHeir /
        ///     CanServe / IsRoyalGuard / IsActive / IsSlave。实测一个王国的单次
        ///     Reconcile 因此到过 1000ms。
        ///
        ///     表里可能含本轮中途刚上任的人(闸门读的是 COURT_OFFICE_ID,不会
        ///     回头重算),所以取人时必须再过一遍 <see cref="ReservedActorIds"/>。
        /// </summary>
        internal IReadOnlyList<Actor> FactsCandidates(Kingdom pKingdom,
            Func<Actor, bool> pFacts)
        {
            if (pKingdom?.data == null || pFacts == null)
                return Array.Empty<Actor>();
            if (_factsCandidates != null) return _factsCandidates;
            var filtered = new List<Actor>();
            List<Actor> evicted = null;
            for (int index = 0; index < Actors.Count; index++)
            {
                Actor actor = Actors[index];
                if (actor?.data == null)
                {
                    continue;
                }

                if (pFacts(actor))
                {
                    filtered.Add(actor);
                    continue;
                }

                // 池子靠事件维护、不再定期重建,所以**永久**失效的人必须在这里
                // 摘掉,否则每一个曾经活过的国民都会永远留在表里。
                //
                // 只摘永久条件(死了、没了、已经不是本国人)。在任、储君、城主
                // 这些都是会回来的,摘掉就等于把人弄丢 —— 那才是「县令永远补不上」
                // 的老毛病。
                if (IsPermanentlyOutOfPool(actor, pKingdom))
                    (evicted ??= new List<Actor>()).Add(actor);
            }

            if (evicted != null)
                for (int index = 0; index < evicted.Count; index++)
                    OfficerCandidateCatalog.Remove(pKingdom, evicted[index]);
            _factsCandidates = filtered;
            return _factsCandidates;
        }

        /// <summary>
        /// 这个人是不是**永远**不会再进这个王国的候选池了。只判不可逆的条件。
        /// </summary>
        private static bool IsPermanentlyOutOfPool(Actor pActor,
            Kingdom pKingdom)
        {
            try
            {
                if (!pActor.isAlive() || pActor.isRekt()) return true;
                return pActor.kingdom != pKingdom;
            }
            catch { return false; }
        }

        /// <summary>
        ///     严格通道的候选池 —— 即「官员候选池」。两部分并集:
        ///
        ///     一是索引正式候选(<see cref="CivilServiceFormalCandidateQuery"/>,
        ///     举人及以上、当前无官职,数量由 CandidateSourceLimit 封顶);
        ///     二是已有品级者(纯内存 <c>ReadRankFast</c> 读取)。
        ///
        ///     局部层原来没有严格通道,直接拿 <see cref="FactsCandidates"/> 的
        ///     两千多人跑空缺兜底,于是科举资格与品级在县令任命上完全不起作用,
        ///     而且每个席位都要付全量代价。补回严格通道后,绝大多数补缺在这个
        ///     小池子里就成交,全量扫描只在「一个够格的都没有」时才跑。
        ///
        ///     成员仍要过基础事实闸:索引来自数据库,可能含本轮中途已被占用或
        ///     状态已变的人。
        /// </summary>
        internal IReadOnlyList<Actor> StrictCandidates(Kingdom pKingdom,
            Func<Actor, bool> pFacts)
        {
            if (pKingdom?.data == null || pFacts == null)
                return Array.Empty<Actor>();
            if (_strictCandidates != null) return _strictCandidates;
            var seen = new HashSet<long>();
            var strict = new List<Actor>();
            foreach (Actor actor in CourtService.
                         LoadLocalFormalCandidates(pKingdom))
            {
                if (actor?.data == null || !seen.Add(actor.data.id)) continue;
                if (!pFacts(actor)) continue;
                strict.Add(actor);
            }
            IReadOnlyList<Actor> facts = FactsCandidates(pKingdom, pFacts);
            for (int index = 0; index < facts.Count; index++)
            {
                Actor actor = facts[index];
                if (OfficialCareerStateService.ReadRankFast(actor) <=
                    OfficialCareerRankRules.Unranked) continue;
                if (!seen.Add(actor.data.id)) continue;
                strict.Add(actor);
            }
            _strictCandidates = strict;
            return _strictCandidates;
        }

        /// <summary>
        ///     任职资历,本轮每人只读一次。严格通道对每名候选都要问两级,
        ///     而底层是同一条查询。
        /// </summary>
        internal CourtServiceHistory ServiceHistory(Actor pActor,
            Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null)
                return new CourtServiceHistory(false, false);
            _serviceHistories ??=
                new Dictionary<long, CourtServiceHistory>();
            long actorId = pActor.data.id;
            if (_serviceHistories.TryGetValue(actorId,
                    out CourtServiceHistory cached)) return cached;
            CourtServiceHistory history = CivilServiceQualificationService.
                LoadServiceHistory(pActor, pKingdom);
            _serviceHistories[actorId] = history;
            return history;
        }

        internal SchoolGuestOfficeService.VacancyCandidateSession
            GuestCandidates(Kingdom pKingdom)
        {
            return _guestCandidates ??= SchoolGuestOfficeService.
                CreateVacancyCandidateSession(pKingdom);
        }

        /// <summary>
        /// 一个**行为类**的城官候选表,按无籍贯加成的分排好,本轮只建一次。
        ///
        /// 原来按 (城, 官职, 通道) 缓存 —— 一个王国几十个 (城 × 官职) 组合就
        /// 要把候选池重扫几十遍,实测一次补缺 381 次建表、58240 行。而这几十
        /// 张表内容几乎完全一样:排序只看品级和方镇标志,同品级的两个官职
        /// 顺序逐字节相同。按行为类缓存后,组合数从几十塌缩到个位数。
        ///
        /// 籍贯加成按城变,不进表;取人时由
        /// <see cref="CityShortlistRules.PickWithHometownBonus"/> 扫表头补回。
        /// </summary>
        internal sealed class CityCandidateTable
        {
            internal readonly List<Actor> Actors = new List<Actor>();
            internal readonly List<int> Tiers = new List<int>();
            /// <summary>不含籍贯加成的分。</summary>
            internal readonly List<int> Scores = new List<int>();
            internal readonly List<long> Ids = new List<long>();
            /// <summary>此人的籍贯城 id,取人时和目标城比。</summary>
            internal readonly List<long> NativeCityIds = new List<long>();
        }

        /// <summary>
        /// 取这个行为类的候选表,没有就建。见 <see cref="CityCandidateTable"/>。
        /// </summary>
        internal CityCandidateTable CityCandidatesFor(
            CandidatePoolBehavior pBehavior,
            Func<CityCandidateTable> pBuild)
        {
            if (pBuild == null) return new CityCandidateTable();
            _cityCandidatesByBehavior ??=
                new Dictionary<int, CityCandidateTable>();
            int key = pBehavior.Key();
            if (_cityCandidatesByBehavior.TryGetValue(key,
                    out CityCandidateTable cached)) return cached;
            CityCandidateTable built = pBuild() ?? new CityCandidateTable();
            _cityCandidatesByBehavior[key] = built;
            return built;
        }

        /// <summary>
        /// 一个城的县令合格候选表,本轮内只建一次。
        ///
        /// 县令候选只取决于「王国 + 城」,和具体是哪个县无关 —— 唯一按县变化的
        /// 是占用情况,那个由 <see cref="IsAvailable"/> 在取人时判。一个城有 N 个
        /// 县时,原来要把全国候选目录连同其中的逐人资格判定重跑 N 遍。
        /// </summary>
        internal IReadOnlyList<Actor> CountyCandidatesForCity(City pCity,
            int pMode, Func<IReadOnlyList<Actor>> pBuild)
        {
            if (pCity?.data == null || pBuild == null)
                return Array.Empty<Actor>();
            _countyCandidatesByCity ??=
                new Dictionary<(long, int), IReadOnlyList<Actor>>();
            var key = (pCity.data.id, pMode);
            if (_countyCandidatesByCity.TryGetValue(key,
                    out IReadOnlyList<Actor> cached)) return cached;
            IReadOnlyList<Actor> built = pBuild() ?? Array.Empty<Actor>();
            _countyCandidatesByCity[key] = built;
            return built;
        }
    }
}
