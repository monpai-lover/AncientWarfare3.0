using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     天命易主之后的「清剿战争」驱动。
    ///
    ///     新朝刚立，前朝残余往往仍占据着若干天命法理核心城市。历史上的
    ///     统一战争不会因为一场大战就结束 —— 占了旧都也只是开始，残余势力
    ///     要一个个打灭。这里在天命易主之后的 <see cref="MopUpWindowYears"/>
    ///     年内，每年检查是否还有前朝势力占着法理核心，有就跳过正常的
    ///     18 年 AI 行动冷却直接宣战。
    ///
    ///     实现上只做最简单的事：把「这一年应该继续清剿」的判据算出来，
    ///     然后调 <see cref="WarDecisionAI"/> 已有的目标筛选 + 宣战链路。
    ///     不自己造战争类型，不绕开外交规则，只是把优先级拉高。
    /// </summary>
    internal static class MandateMopUpService
    {
        /// <summary>新朝成立后，清剿窗口持续多少年。</summary>
        private const int MopUpWindowYears = 6;

        /// <summary>清剿窗口内，两次清剿宣战之间的最短间隔（年）。</summary>
        private const int MopUpActionCooldown = 2;

        /// <summary>清剿上次宣战的年份，用来撑冷却。</summary>
        private const string MOPUP_LAST_ACTION_YEAR =
            "aw_mandate_mopup_last_action_year";

        /// <summary>
        ///     天命易主时调用：在新王国上盖一个清剿起始年份。
        ///
        ///     由 <see cref="MandateService"/> 在每次成功
        ///     <c>TryDeclareMandate</c> 之后调用。
        /// </summary>
        internal static void OnMandateEstablished(Kingdom pKingdom)
        {
            if (pKingdom?.data == null ||
                AW3MultiplayerReplicaScope.IsReplicaSession) return;
            try
            {
                pKingdom.data.set(LineageKeys.MANDATE_MOPUP_YEAR,
                    Date.getCurrentYear());
                pKingdom.data.set(MOPUP_LAST_ACTION_YEAR,
                    Date.getCurrentYear() - MopUpWindowYears);
            }
            catch { }
        }

        /// <summary>
        ///     年度检查：在清剿窗口内，如果还有前朝势力占着法理核心，
        ///     就跳过 AI 的正常冷却直接触发一次 <c>WarDecisionAI.OnKingdomYear</c>。
        /// </summary>
        internal static void OnMandateKingdomYear(Kingdom pMandate)
        {
            if (pMandate?.data == null || pMandate.isRekt() ||
                AW3MultiplayerReplicaScope.IsReplicaSession) return;
            try
            {
                pMandate.data.get(LineageKeys.MANDATE_MOPUP_YEAR,
                    out int mopupYear, int.MinValue);
                if (mopupYear == int.MinValue) return;

                int year = Date.getCurrentYear();
                if (year - mopupYear > MopUpWindowYears)
                {
                    ClearMopUp(pMandate);
                    return;
                }

                pMandate.data.get(MOPUP_LAST_ACTION_YEAR,
                    out int lastAction, int.MinValue);
                if (year - lastAction < MopUpActionCooldown) return;
                if (!HasUnconqueredCoreCity(pMandate)) return;
                if (pMandate.hasEnemies()) return;

                // 找一个占着法理核心的目标并直接宣战，绕过
                // WarDecisionAI.ACTION_COOLDOWN（18 年）但保留所有外交规则。
                Kingdom target = FindMopUpTarget(pMandate);
                if (target?.data == null) return;
                WarTerritoryService.WarTargetOption option =
                    WarDecisionAI.PickMopUpOption(pMandate, target);
                if (option == null) return;
                bool issued = DiplomaticWarDeclarationService.Issue(pMandate,
                    option);
                if (!issued) return;
                pMandate.data.set(MOPUP_LAST_ACTION_YEAR, year);
                ModClass.LogInfo("[AW3] 清剿战争: " +
                    (pMandate.name ?? "?") + " → " + (target.name ?? "?"));
            }
            catch { }
        }

        private static void ClearMopUp(Kingdom pKingdom)
        {
            try
            {
                pKingdom.data.removeInt(LineageKeys.MANDATE_MOPUP_YEAR);
                pKingdom.data.removeInt(MOPUP_LAST_ACTION_YEAR);
            }
            catch { }
        }

        /// <summary>
        ///     是否还有非天命方持有的法理核心城市。
        /// </summary>
        private static bool HasUnconqueredCoreCity(Kingdom pMandate)
        {
            try
            {
                if (World.world?.cities == null) return false;
                foreach (City city in World.world.cities)
                {
                    if (city?.data == null || city.isRekt()) continue;
                    if (!MandateService.IsLegalCoreCity(city)) continue;
                    Kingdom owner = city.kingdom;
                    if (owner?.data == null || owner == pMandate ||
                        owner.isRekt() || owner.isNeutral()) continue;
                    if (VassalService.GetRootSuzerain(owner) == pMandate)
                        continue;
                    return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        ///     在占有法理核心城的非天命国里找一个清剿目标，优先选邻国。
        /// </summary>
        private static Kingdom FindMopUpTarget(Kingdom pMandate)
        {
            try
            {
                var candidates = new Dictionary<long, Kingdom>();
                if (World.world?.cities == null) return null;
                foreach (City city in World.world.cities)
                {
                    if (city?.data == null || city.isRekt()) continue;
                    if (!MandateService.IsLegalCoreCity(city)) continue;
                    Kingdom owner = city.kingdom;
                    if (owner?.data == null || owner == pMandate ||
                        owner.isRekt() || owner.isNeutral()) continue;
                    if (VassalService.GetRootSuzerain(owner) == pMandate)
                        continue;
                    if (owner.hasEnemies()) continue;
                    candidates[owner.id] = owner;
                }
                if (candidates.Count == 0) return null;

                Kingdom nearest = null;
                float nearestDist = float.MaxValue;
                City mandateCapital = pMandate.capital;
                foreach (Kingdom candidate in candidates.Values)
                {
                    float dist = CapitalDistance(mandateCapital,
                        candidate.capital);
                    if (dist >= nearestDist) continue;
                    if (!WarDecisionAI.CanBeMopUpTarget(pMandate, candidate))
                        continue;
                    nearestDist = dist;
                    nearest = candidate;
                }
                return nearest;
            }
            catch { return null; }
        }

        private static float CapitalDistance(City pA, City pB)
        {
            try
            {
                var posA = pA?.getTile()?.pos;
                var posB = pB?.getTile()?.pos;
                if (posA == null || posB == null) return float.MaxValue;
                float dx = posA.Value.x - posB.Value.x;
                float dy = posA.Value.y - posB.Value.y;
                return dx * dx + dy * dy;
            }
            catch { return float.MaxValue; }
        }
    }
}
