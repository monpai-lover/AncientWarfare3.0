using System;
using System.Collections.Generic;
using AncientWarfare3.core.asyncwork;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     首都沦陷之后的小朝廷分裂。
    ///
    ///     都城一破，君主退守残土，朝中有继承权的人便各自盘算 —— 谁手上有城、
    ///     有将、有朝官支持，谁就敢自立。这里只负责**判断该不该裂**并把它
    ///     交给既有的继承争议链路；建国、割城、内战一步都不自己实现：
    ///
    ///     <list type="number">
    ///     <item><see cref="SuccessionDisputeService.BuildPreparationFacts"/>
    ///           挑挑战者（<see cref="InheritanceCandidateService.ResolveFactionSupport"/>）
    ///           并按城主/将领/朝官的倾向选出归附的城</item>
    ///     <item><see cref="SuccessionDisputePersistenceService"/> 落库</item>
    ///     <item>争议泵推进：建国 → 割城 → 内战</item>
    ///     </list>
    ///
    ///     传参上做了一处取巧：把**在位君主同时当作前任与继任**传进去。
    ///     争议链路本来服务于「新君即位、旁支不服」，派系支持是相对于
    ///     「坐在位子上的那个人」算的 —— 首都沦陷这个语境里，坐在位子上的
    ///     正是那位弃都出逃的君主，口径天然一致，不必另造一套。
    /// </summary>
    internal static class RumpCourtSplitService
    {
        /// <summary>
        ///     待判定的有效期（年）。过了还没裂就作罢 —— 都城丢了很久还没
        ///     闹起来，说明这个朝廷稳住了。
        /// </summary>
        private const int PendingYears = 3;

        /// <summary>
        ///     首都失陷时记一笔待办，真正的判定推到年度那一拍。
        ///
        ///     破城当帧不能直接判：原版此时往往还没另立临时都城，而挑选归附
        ///     城池是以都城为锚点算方位与支持度的（见
        ///     <c>SuccessionDisputeService.SelectSupportCities</c>）。当场判
        ///     多半因为「没有都城」而直接放弃，且没有第二次机会。
        /// </summary>
        internal static void OnCapitalLost(Kingdom pFallenKingdom,
            City pLostCapital, Kingdom pConqueror)
        {
            if (pFallenKingdom?.data == null || pLostCapital?.data == null ||
                pFallenKingdom.isRekt()) return;
            try
            {
                pFallenKingdom.data.set(LineageKeys.RUMP_SPLIT_PENDING_YEAR,
                    Date.getCurrentYear());
                ModClass.LogInfo("[AW3] 小朝廷分裂待判: " +
                    (pFallenKingdom.name ?? "?") + " 失都 " +
                    (pLostCapital.data.name ?? "?") + " 于 " +
                    (pConqueror?.name ?? "?"));
            }
            catch { }
        }

        /// <summary>
        ///     年度那一拍：有待办就试着裂一次。
        /// </summary>
        internal static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            pKingdom.data.get(LineageKeys.RUMP_SPLIT_PENDING_YEAR,
                out int pendingYear, int.MinValue);
            if (pendingYear == int.MinValue) return;
            int year = Date.getCurrentYear();
            if (year - pendingYear > PendingYears)
            {
                ClearPending(pKingdom);
                return;
            }
            if (TrySplit(pKingdom)) ClearPending(pKingdom);
        }

        private static void ClearPending(Kingdom pKingdom)
        {
            try
            {
                pKingdom.data.removeInt(
                    LineageKeys.RUMP_SPLIT_PENDING_YEAR);
            }
            catch { }
        }

        /// <summary>
        ///     成功开出一场分裂返回 true；条件不满足返回 false，
        ///     留着待办等下一年再看。
        /// </summary>
        private static bool TrySplit(Kingdom pFallenKingdom)
        {
            try
            {
                Actor ruler = pFallenKingdom.king;
                bool kingdomAlive = !pFallenKingdom.isRekt() &&
                                    pFallenKingdom.hasCities();
                int remaining = SafeCityCount(pFallenKingdom);
                // 临时都城:原版在丢掉都城之后会另立一座。它是支持度计算的
                // 锚点(SelectSupportCities 以 kingdom.capital 为基准判方位),
                // 还没立出来就等下一次,别硬裂。
                City rumpCapital = pFallenKingdom.capital;
                bool hasRumpCapital = rumpCapital?.data != null &&
                                      !rumpCapital.isRekt() &&
                                      rumpCapital.kingdom == pFallenKingdom;
                bool hasDispute = SuccessionDisputeService
                    .TryGetCachedByKingdom(pFallenKingdom.id, out _);

                if (!RumpCourtSplitRules.ShouldSplit(kingdomAlive, remaining,
                        hasRumpCapital, ruler?.data != null && ruler.isAlive(),
                        hasDispute))
                {
                    // 年度一拍一行,查起来才知道卡在哪一条。
                    ModClass.LogInfo("[AW3] 小朝廷分裂未成: " +
                        (pFallenKingdom.name ?? "?") + " alive=" +
                        kingdomAlive + " cities=" + remaining +
                        " rump_capital=" + hasRumpCapital + " ruler=" +
                        (ruler?.data != null && ruler.isAlive()) +
                        " dispute=" + hasDispute);
                    return false;
                }

                // 争议链路要一个「前任 + 在位者」。首都沦陷没有换人这一步,
                // 两边都传在位君主 —— 派系支持本来就是相对他算的。
                SuccessionDisputePreparationFacts facts =
                    SuccessionDisputeService.BuildPreparationFacts(
                        pFallenKingdom, ruler, ruler, SuccessionMode.NONE,
                        InheritanceLawService.GetEffectiveLaw(
                            pFallenKingdom));
                if (facts == null)
                {
                    // BuildPreparationFacts 只在没有合法的另立人选、
                    // 或割不出均衡的支持城池时返回空。后者是小朝廷场景里
                    // 最常见的：都城刚失、残土只有 3-4 座城，全部臣民忠诚度
                    // 相仿，SelectSupportCities 算不出支持挑战者的多数 ——
                    // 结果是 0 城，CanFormBalancedTerritorialSplit 直接拒绝。
                    //
                    // 降级方案：找一个合法的继承候选人，按地理距离把残土
                    // 大致对半切，不依赖支持度打分。这比「什么都没有」强，
                    // 而且在真实历史里更合理：流亡君主手下的人往往不是因为
                    // 支持率才分裂，而是「你在西、我在东」。
                    ModClass.LogInfo("[AW3] 小朝廷分裂(地理兜底): " +
                        (pFallenKingdom.name ?? "?") + " 残土 " + remaining +
                        " 城，尝试地理分割");
                    facts = TryBuildGeographicRumpFacts(pFallenKingdom, ruler,
                        remaining);
                    if (facts == null)
                    {
                        ModClass.LogInfo("[AW3] 小朝廷分裂未成(无可立之人): " +
                            (pFallenKingdom.name ?? "?") + " 残土 " + remaining +
                            " 城");
                        return false;
                    }
                }

                SuccessionDisputePersistenceService.QueueRumpCourtSplit(
                    facts);
                try { WorldLog.logFracturedKingdom(pFallenKingdom); }
                catch { }
                ModClass.LogInfo("[AW3] 小朝廷分裂: " +
                    (pFallenKingdom.name ?? "?") + " 残土 " + remaining +
                    " 城, 挑战者 " + facts.ClaimantActorId);
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("[AW3] 小朝廷分裂判定失败: " +
                                    error.Message);
                return false;
            }
        }

        private static int SafeCityCount(Kingdom pKingdom)
        {
            try { return pKingdom.countCities(); }
            catch { return 0; }
        }

        /// <summary>
        ///     支持度打分走不通时的地理兜底：找一个合法候选人，按与候选人
        ///     所在城的距离把残余城池对半分。
        /// </summary>
        private static SuccessionDisputePreparationFacts
            TryBuildGeographicRumpFacts(Kingdom pKingdom, Actor pRuler,
                int pCityCount)
        {
            if (pKingdom?.data == null || pRuler?.data == null ||
                pCityCount < 2) return null;
            try
            {
                // 找最年轻、综合属性最好的合法成年宗室男性。
                // 不用派系支持得分——那是按嫡长子顺序排的，和「谁有能力
                // 在乱局里自立」没有关系。
                Actor claimant = FindYoungBestClaimant(pKingdom, pRuler);
                if (claimant == null) return null;

                City anchor = claimant.city?.data != null &&
                              claimant.city.kingdom == pKingdom
                    ? claimant.city
                    : pKingdom.capital;
                if (anchor?.data == null) return null;

                var cities = new List<City>();
                foreach (City city in pKingdom.getCities())
                {
                    if (city?.data == null || city.isRekt() ||
                        city == pKingdom.capital ||
                        city.kingdom != pKingdom) continue;
                    cities.Add(city);
                }
                if (cities.Count == 0) return null;

                cities.Sort((a, b) =>
                    TileDistance(anchor, a).CompareTo(TileDistance(anchor, b)));

                int half = Math.Max(1, cities.Count / 2);
                if (!SuccessionDisputeRules.CanFormBalancedTerritorialSplit(
                        pCityCount, half))
                {
                    if (half + 1 < cities.Count &&
                        SuccessionDisputeRules.CanFormBalancedTerritorialSplit(
                            pCityCount, half + 1))
                        half++;
                    else if (half > 1 &&
                             SuccessionDisputeRules
                                 .CanFormBalancedTerritorialSplit(
                                     pCityCount, half - 1))
                        half--;
                    else return null;
                }

                var supportCityIds = new long[half];
                for (int i = 0; i < half; i++)
                    supportCityIds[i] = cities[i].id;

                pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID,
                    out long lineageId, -1L);
                pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID,
                    out long shiId, -1L);

                SuccessionDirection rivalDir = TryResolveDirection(
                    pKingdom.capital, cities[0]);

                return new SuccessionDisputePreparationFacts
                {
                    WorldGeneration = AWAsyncRuntime.WorldGeneration,
                    Revision =
                        SuccessionDisputePersistenceService.CurrentRevision(
                            pKingdom.id),
                    KingdomId = pKingdom.id,
                    PredecessorActorId = pRuler.data.id,
                    SuccessorActorId = pRuler.data.id,
                    ClaimantActorId = claimant.data.id,
                    LegitimateClaimantId = -1L,
                    MilitaryClaimantId = -1L,
                    CivilClaimantId = -1L,
                    SuccessorMode = SuccessionMode.NONE,
                    ClaimantMode = SuccessionMode.NONE,
                    ClaimantKind = SuccessionClaimantKind.FirstCollateral,
                    SuccessorSupport = 0,
                    ClaimantSupport = 1,
                    RunnerUpSupport = 0,
                    AccessionLaw = InheritanceLawService.GetEffectiveLaw(
                        pKingdom),
                    OriginalLineageId = lineageId,
                    OriginalShiId = shiId,
                    OriginalStateName = pKingdom.name ?? string.Empty,
                    OriginalQualifier = SuccessionDisputeRules.DirectionId(
                        Opposite(rivalDir)),
                    RivalQualifier = SuccessionDisputeRules.DirectionId(
                        rivalDir),
                    SupportCityIds = supportCityIds
                };
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "[AW3] 小朝廷地理分割失败: " + error.Message);
                return null;
            }
        }

        private static bool IsLegalRumpClaimant(Actor pActor,
            Actor pRuler, Kingdom pKingdom)
        {
            return pActor?.data != null && pActor != pRuler &&
                   pActor.kingdom == pKingdom && pActor.isAlive() &&
                   !pActor.isRekt() && pActor.isSexMale() &&
                   pActor.isAdult() && !pActor.isKing() &&
                   !SlaveService.IsSlave(pActor) &&
                   !pActor.hasTrait("madness");
        }

        /// <summary>
        ///     从王国里扫出最年轻的合法成年男性，年龄相仿时取属性更好的。
        ///
        ///     「最年轻」是为了选出精力充沛、执政时间长的潜力股 —— 战乱里
        ///     自立的人往往是年轻宗室，不是活得最久的元老。属性排序用
        ///     warfare + intelligence + diplomacy 三项之和作权重，粗略代表
        ///     „军事+内政+外交"的综合能力；和而不同、不放大单一属性。
        /// </summary>
        private static Actor FindYoungBestClaimant(Kingdom pKingdom,
            Actor pRuler)
        {
            if (pKingdom?.data == null) return null;
            Actor best = null;
            float bestAge = float.MaxValue;
            int bestStats = int.MinValue;
            try
            {
                foreach (Actor actor in World.world?.units ??
                    (System.Collections.Generic.IEnumerable<Actor>)
                    new Actor[0])
                {
                    if (!IsLegalRumpClaimant(actor, pRuler, pKingdom))
                        continue;
                    float age = SafeAge(actor);
                    int stats = SafeStat(actor, "warfare") +
                                SafeStat(actor, "intelligence") +
                                SafeStat(actor, "diplomacy");
                    bool younger = age < bestAge - 2f;
                    bool sameAge = !younger && bestAge - 2f <= age &&
                                   age <= bestAge + 2f;
                    if (younger || sameAge && stats > bestStats)
                    {
                        best = actor;
                        bestAge = age;
                        bestStats = stats;
                    }
                }
            }
            catch { }
            return best;
        }

        private static float SafeAge(Actor pActor)
        {
            try { return pActor.getAge(); }
            catch { return float.MaxValue; }
        }

        private static int SafeStat(Actor pActor, string pKey)
        {
            try
            {
                switch (pKey)
                {
                    case "warfare":     return pActor.warfare;
                    case "intelligence":return pActor.intelligence;
                    case "diplomacy":   return pActor.diplomacy;
                    case "stewardship": return pActor.stewardship;
                    default:            return 0;
                }
            }
            catch { return 0; }
        }

        private static float TileDistance(City pA, City pB)
        {
            try
            {
                var posA = pA.getTile()?.pos;
                var posB = pB.getTile()?.pos;
                if (posA == null || posB == null) return float.MaxValue;
                float dx = posA.Value.x - posB.Value.x;
                float dy = posA.Value.y - posB.Value.y;
                return dx * dx + dy * dy;
            }
            catch { return float.MaxValue; }
        }

        private static SuccessionDirection TryResolveDirection(
            City pCapital, City pRivalSeat)
        {
            try
            {
                var posC = pCapital?.getTile()?.pos;
                var posR = pRivalSeat?.getTile()?.pos;
                if (posC == null || posR == null)
                    return SuccessionDirection.Later;
                return SuccessionDisputeRules.ResolveDirection(
                    posR.Value.x - posC.Value.x,
                    posR.Value.y - posC.Value.y,
                    claimantAccededLater: true);
            }
            catch { return SuccessionDirection.Later; }
        }

        private static SuccessionDirection Opposite(
            SuccessionDirection pDirection)
        {
            switch (pDirection)
            {
                case SuccessionDirection.East:  return SuccessionDirection.West;
                case SuccessionDirection.West:  return SuccessionDirection.East;
                case SuccessionDirection.North: return SuccessionDirection.South;
                case SuccessionDirection.South: return SuccessionDirection.North;
                default: return pDirection;
            }
        }
    }
}
