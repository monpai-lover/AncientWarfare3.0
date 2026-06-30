using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     继承人系统(参考 AW2 AW_Kingdom.FindHeir/SetHeir,但不用子类夺舍 —— 新版不可行,
    ///     改用 kingdom.data 自定义字段 aw_heir_id 存继承人 + Harmony patch 接管继位)。
    ///
    ///     选择规则(AW2 同):royal_clan 成员中 活着∧非王∧成年∧非疯狂;按 |age-18| 最小优先
    ///     (越接近成年越优先,避免选老人或孩子)。
    /// </summary>
    internal static class HeirService
    {
        /// <summary>重选继承人并写入 kingdom.data。新王即位后调用。同步维护 actor.data 的 IS_HEIR 标记(unit_heir 皮肤 + minimap 用)。</summary>
        public static void RefreshHeir(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;

            // 先清旧继承人的 IS_HEIR 标记(卸任 → 恢复普通皮肤/图标)。
            ClearOldHeirFlag(pKingdom);

            Actor heir = FindHeir(pKingdom);
            pKingdom.data.set(LineageKeys.KINGDOM_HEIR_ID, heir?.data?.id ?? -1L);
            if (heir?.data != null) heir.data.set(LineageKeys.IS_HEIR, true);
        }

        /// <summary>清掉 kingdom 当前登记继承人 actor 的 IS_HEIR 标记(若该 actor 仍在)。</summary>
        private static void ClearOldHeirFlag(Kingdom pKingdom)
        {
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID, out long oldId, -1L);
            if (oldId < 0) return;
            var old = World.world.units.get(oldId);
            if (old?.data != null) old.data.set(LineageKeys.IS_HEIR, false);
        }

        /// <summary>
        ///     取继承人:**现任优先稳定**——已登记继承人且**仍合格**(活/非王/成年/非疯)则保持不变;
        ///     现任失格(死/继位/失格)或从未登记 → 才 FindHeir 重选并写回。
        ///     兼顾两个需求:① 已有合格继承人不被频繁改选(用户报"已存在还被重选");
        ///     ② 即位时儿子还小、后来成年 → 现任为空/失格时重选会把成年儿子选上。
        /// </summary>
        public static Actor GetHeir(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;

            // 1) 现任继承人仍合格 → 保持不变(稳定,不乱换)。
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID, out long curId, -1L);
            if (curId >= 0)
            {
                var cur = World.world.units.get(curId);
                if (cur != null && IsSuitableHeir(cur, pKingdom.king))
                    return cur;
            }

            // 2) 现任失格/无 → 重选并写回缓存。同步维护 IS_HEIR 标记(旧清、新设)。
            ClearOldHeirFlag(pKingdom);
            Actor heir = FindHeir(pKingdom);
            pKingdom.data.set(LineageKeys.KINGDOM_HEIR_ID, heir?.data?.id ?? -1L);
            if (heir?.data != null) heir.data.set(LineageKeys.IS_HEIR, true);
            return heir;
        }

        public static bool HasHeir(Kingdom pKingdom)
        {
            return GetHeir(pKingdom) != null;
        }

        public static void ClearHeir(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            ClearOldHeirFlag(pKingdom);                       // 清旧继承人 IS_HEIR 标记
            pKingdom.data.set(LineageKeys.KINGDOM_HEIR_ID, -1L);
        }

        /// <summary>选最合适继承人:**优先国王直系成年子女**(男性优先、接近成年优先);
        /// 无合格子女则 fallback 到 royal_clan 成员。无候选返回 null。</summary>
        private static Actor FindHeir(Kingdom pKingdom)
        {
            Actor king = pKingdom.king;

            // 1) 优先:国王的直系成年子女(getChildren(false)=所有家庭的活子女)。儿子优先于女儿。
            if (king != null)
            {
                Actor best = PickClosest(king.getChildren(false), king, pPreferMale: true);
                if (best != null) return best;
            }

            // 2) fallback:royal_clan 成员(原逻辑)。
            long royalClanId = pKingdom.data.royal_clan_id;
            if (royalClanId < 0) return null;
            var clan = World.world.clans.get(royalClanId);
            if (clan == null) return null;

            return PickClosest(new List<Actor>(clan.units), king, pPreferMale: false);
        }

        /// <summary>从候选里挑:合格(活∧非王∧成年∧非疯)中 |age-18| 最小;pPreferMale 时男性获 -1000 偏置(必优先于女性)。</summary>
        private static Actor PickClosest(System.Collections.Generic.IEnumerable<Actor> pCandidates, Actor pKing, bool pPreferMale)
        {
            Actor best = null;
            int bestScore = int.MaxValue;
            foreach (var member in pCandidates)
            {
                if (!IsSuitableHeir(member, pKing)) continue;
                int score = System.Math.Abs(member.getAge() - 18);
                if (pPreferMale && member.isSexMale()) score -= 1000; // 男性优先(成年儿子优先于女儿)
                if (score < bestScore)
                {
                    bestScore = score;
                    best = member;
                }
            }
            return best;
        }

        /// <summary>继承人资格:活着∧非现任王∧成年∧非疯狂。</summary>
        private static bool IsSuitableHeir(Actor pActor, Actor pKing)
        {
            if (pActor == null || pActor.isRekt()) return false;
            if (pActor == pKing) return false;
            if (pActor.isKing()) return false;
            if (!pActor.isAdult()) return false;
            if (pActor.hasTrait("madness")) return false;
            return true;
        }
    }
}
