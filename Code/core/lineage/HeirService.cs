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
        /// <summary>重选继承人并写入 kingdom.data。新王即位后调用。</summary>
        public static void RefreshHeir(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;

            Actor heir = FindHeir(pKingdom);
            pKingdom.data.set(LineageKeys.KINGDOM_HEIR_ID, heir?.data?.id ?? -1L);
        }

        /// <summary>读 kingdom.data 取继承人 actor。失格(死亡/不存在)返回 null。</summary>
        public static Actor GetHeir(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID, out long id, -1L);
            if (id < 0) return null;

            var actor = World.world.units.get(id);
            if (actor == null || actor.isRekt()) return null;
            return actor;
        }

        public static bool HasHeir(Kingdom pKingdom)
        {
            return GetHeir(pKingdom) != null;
        }

        public static void ClearHeir(Kingdom pKingdom)
        {
            pKingdom?.data?.set(LineageKeys.KINGDOM_HEIR_ID, -1L);
        }

        /// <summary>从 royal_clan 选最合适的继承人(|age-18| 最小)。无候选返回 null。</summary>
        private static Actor FindHeir(Kingdom pKingdom)
        {
            long royalClanId = pKingdom.data.royal_clan_id;
            if (royalClanId < 0) return null;

            var clan = World.world.clans.get(royalClanId);
            if (clan == null) return null;

            Actor best = null;
            int bestDist = int.MaxValue;
            Actor king = pKingdom.king;

            foreach (var member in new List<Actor>(clan.units))
            {
                if (!IsSuitableHeir(member, king)) continue;

                int dist = System.Math.Abs(member.getAge() - 18);
                if (dist < bestDist)
                {
                    bestDist = dist;
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
