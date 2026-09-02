using System;
using System.Collections.Generic;
using AncientWarfare3.content;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     土匪 / 义军转为正规政权时的国号更换。
    ///
    ///     问题：走完 土匪 → 义军 → 正规国家 之后，国名仍然是「赤眉」「黄巾」
    ///     这类**双字匪号**，而不是汉式单字国号。
    ///
    ///     成因：两条落定路径都直接把匪号词根当国名用 ——
    ///       <c>data.get(MANDATE_REBEL_NAME_ROOT, out root); TryApplyRouteName(k, root);</c>
    ///     而 <c>word_libraries/default/土匪名根.txt</c> 里的词根全是双字
    ///     （赤眉、黄巾、绿林、梁山、洞庭…），
    ///     <c>PeasantRebelOutlawNameRules.NormalizeRoot</c> 又只剥「义军」「贼」
    ///     两个**后缀**、从不动词根本身。于是匪号原样留了下来。
    ///
    ///     两条路径各有一份一模一样的代码：
    ///       · <see cref="PeasantRebelBanditAmnestyService"/>（招安）
    ///       · <see cref="MandateRebelService"/>（义军打赢后落定 —— 玩家实际
    ///         遇到的就是这条）
    ///     所以逻辑收在这里，两边共用，避免只修一处又漏另一处。
    ///
    ///     正式国号取自 <see cref="XiaPreQinKingdomNameRules"/>：先秦国名池，
    ///     绝大多数是单字，和其他所有国家同源（StateNameService 用的也是它）。
    /// </summary>
    internal static class PeasantRebelStateNameService
    {
        /// <summary>
        ///     换上正式国号。取不到名字时保留原名并告警而不是失败 ——
        ///     顶着匪号，也好过把整条落定流程回滚掉。
        /// </summary>
        internal static bool ApplyCanonical(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;

            string canonical;
            try
            {
                canonical = SelectCanonicalName(pKingdom);
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "[AW3] 改国号失败,保留原名: " + error.Message);
                return true;
            }

            if (!StateNameRules.IsValid(canonical))
            {
                ModClass.LogWarning(
                    "[AW3] 改国号:候选池取不到可用国名,保留原名。");
                return true;
            }

            string previous = pKingdom.name;
            if (!PeasantRebelRouteService.TryApplyRouteName(pKingdom,
                    canonical))
            {
                ModClass.LogWarning(
                    "[AW3] 改国号:重命名未生效 -> " + canonical);
                return true;
            }

            // 匪号词根留着会让后续路径再拿它去改名,清掉。
            pKingdom.data.set(LineageKeys.MANDATE_REBEL_NAME_ROOT, "");
            ModClass.LogInfo("[AW3] 义军/土匪落定改国号: " + previous +
                " -> " + canonical);
            return true;
        }

        private static string SelectCanonicalName(Kingdom pKingdom)
        {
            var active = new HashSet<string>(StringComparer.Ordinal);
            if (World.world?.kingdoms != null)
                foreach (Kingdom other in World.world.kingdoms.list)
                {
                    if (other?.data == null || other == pKingdom) continue;
                    string name = other.name;
                    if (!string.IsNullOrWhiteSpace(name))
                        active.Add(name.Trim());
                }

            // 稳定种子:同一个王国每次算出来的名字一致,读档/重算不会漂。
            long founderId = pKingdom.king?.data?.id ?? -1L;
            return StateNameRules.SelectFirstAvailable(
                XiaPreQinKingdomNameRules.All(), active,
                pKingdom.getID(), founderId, pKingdom.getID());
        }
    }
}
