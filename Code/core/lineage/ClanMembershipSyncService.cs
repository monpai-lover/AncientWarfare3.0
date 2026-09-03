using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     让原版 <c>Clan</c> 的成员集合贴合我们的氏（宗族）。
    ///
    ///     两边本来各记各的：我们用 actor data 上的
    ///     <see cref="LineageKeys.SHI_ID"/>，原版用 <c>Actor.clan</c> 引用 +
    ///     <c>Clan.units</c>。血缘路径上虽然有几处「跟父亲的 clan 走」
    ///     （<see cref="LineageService"/>），但没有任何一处以氏为准做对齐 ——
    ///     于是同一个氏的活人会散落在多个原版 Clan 里，而
    ///     <c>LineageArchiveWriter</c>、王室认定、贵族继承都在读那个对象。
    ///
    ///     对齐方向是单向的：氏 → Clan。反向不成立，原版把人踢出 Clan
    ///     不应该改变他的宗族归属 —— 氏是血缘事实，Clan 只是它的运行时投影。
    ///
    ///     不新建 Clan：建族有它自己的时机（登基、开宗、历史人物落地），
    ///     这里只把人挪进**已经存在**的那个族。找不到锚点族就什么都不做。
    /// </summary>
    internal static class ClanMembershipSyncService
    {
        /// <summary>
        ///     一次对齐的成员上限。氏可以很大，而这条路径跑在改氏、开宗这类
        ///     交互点上，不能无界遍历。超出的人由后续的血缘路径（出生跟父亲）
        ///     自然收敛。
        /// </summary>
        private const int MaximumMembersPerSync = 256;

        /// <summary>
        ///     出生路径上沿父系向上找同氏族的最大跳数。有界即可 —— 直系
        ///     父亲入族是绝大多数情况,再往上几代就够兜住「父亲刚好没入族」
        ///     这种边角。走深了没有收益,只有开销。
        /// </summary>
        private const int MaximumAncestorHops = 4;

        private static readonly object Gate = new object();
        private static int _lastWorldIdentity;
        private static bool _completed;

        /// <summary>
        ///     读档后把全世界的氏一次性对齐到各自的原版 Clan。
        ///
        ///     运行时只在改氏、出生这些单点上做增量对齐,存量碎片修不掉 ——
        ///     老存档里同一个氏早就散在一堆单人 Clan 里。全量扫描是
        ///     O(氏数 × 成员数),放在读档里跑;运行时一次都不跑。
        ///
        ///     与 <see cref="SocialIdentityMigrationService"/> 用同一套防重入:
        ///     世界身份没变且已经跑过就直接返回,读档路径上那几个重复调用点
        ///     不会各扫一遍。
        /// </summary>
        internal static int RepairAfterWorldLoaded()
        {
            if (World.world?.units == null) return 0;
            int worldIdentity = World.world.GetHashCode();
            lock (Gate)
            {
                if (_completed && _lastWorldIdentity == worldIdentity)
                    return 0;
            }

            int moved = 0;
            try
            {
                // 按氏分组:遍历一次活人就够,不查库。早先是「收集氏 id →
                // 每个氏一条 SQL 拿成员」,几千个氏就是几千条查询 —— 而这些
                // 人本来就都在内存里,那趟往返是白付的。
                foreach (KeyValuePair<long, List<Actor>> group in
                         GroupLivingActorsByShi())
                    moved += AlignMembers(group.Value);
                lock (Gate)
                {
                    _completed = true;
                    _lastWorldIdentity = worldIdentity;
                }
                if (moved > 0)
                    ModClass.LogInfo("[AW3] 读档宗族成员对齐: 移动 " + moved +
                        " 人");
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "[AW3] 读档宗族成员对齐失败: " + error.Message);
            }
            return moved;
        }

        internal static void ClearRuntime()
        {
            lock (Gate)
            {
                _completed = false;
                _lastWorldIdentity = 0;
            }
        }

        /// <summary>
        ///     活人按氏分组。单趟遍历,无查询。只收有氏的人 —— 无氏者不参与
        ///     对齐。
        /// </summary>
        private static Dictionary<long, List<Actor>> GroupLivingActorsByShi()
        {
            var result = new Dictionary<long, List<Actor>>();
            List<Actor> units;
            try { units = World.world?.units?.units_only_alive; }
            catch { return result; }
            if (units == null) return result;
            for (int i = 0; i < units.Count; i++)
            {
                Actor actor = units[i];
                if (actor?.data == null || actor.isRekt()) continue;
                actor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
                if (shiId < 0L) continue;
                if (!result.TryGetValue(shiId, out List<Actor> bucket))
                {
                    bucket = new List<Actor>();
                    result[shiId] = bucket;
                }
                bucket.Add(actor);
            }
            return result;
        }

        /// <summary>
        ///     把某个氏的活人全部对齐到同一个原版 Clan。
        ///
        ///     锚点族取族长所在的那个；没有族长就取成员里第一个有 Clan 的人。
        ///     取不到就放弃 —— 宁可不动，也不凭空建族。
        /// </summary>
        internal static int AlignShi(long pShiId)
        {
            if (pShiId < 0L) return 0;
            try
            {
                List<long> memberIds = LineageQuery.GetLivingShiMemberIds(
                    pShiId, MaximumMembersPerSync);
                if (memberIds == null || memberIds.Count == 0) return 0;
                var members = new List<Actor>(memberIds.Count);
                foreach (long id in memberIds)
                {
                    Actor actor = FindActor(id);
                    if (actor?.data != null && !actor.isRekt() &&
                        actor.isAlive()) members.Add(actor);
                }
                return AlignMembers(members);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("[AW3] 宗族成员对齐失败: " + error.Message);
                return 0;
            }
        }

        /// <summary>
        ///     把一组同氏的人并到同一个族。锚点族取族长所在的那个;没有族长
        ///     就取第一个有族的人。取不到锚点就整组跳过 —— 宁可不动,也不
        ///     凭空建族。
        /// </summary>
        private static int AlignMembers(List<Actor> pMembers)
        {
            if (pMembers == null || pMembers.Count == 0) return 0;
            Clan anchor = ResolveAnchorClan(pMembers);
            if (anchor?.data == null || anchor.isRekt()) return 0;

            int moved = 0;
            for (int i = 0; i < pMembers.Count; i++)
            {
                Actor member = pMembers[i];
                if (member?.data == null || ReferenceEquals(member.clan, anchor))
                    continue;
                // 满员就停手:原版 isFull 是玩家给宗族选的「五人组/
                // 十二门徒」特质造成的硬上限,不该被我们绕过。
                if (anchor.isFull()) break;
                member.setClan(anchor);
                moved++;
            }
            return moved;
        }

        /// <summary>
        ///     把一个人对齐到他氏所在的 Clan。
        ///
        ///     **只走内存中的父系链,不查库**:这条路跑在出生上,是热路径。
        ///     早先的实现是「查该氏全部活人 → 找锚点族」,一条 SQL 加最多
        ///     256 次 units.get,每个新生儿一遍 —— 那是纯粹的性能负担,已删。
        ///
        ///     沿父系向上找最近一个「同氏且已入族」的祖先,最多走
        ///     <see cref="MaximumAncestorHops"/> 跳。找不到就返回 false,
        ///     调用方照原样建新族。代价是偶尔多建一个族,由读档全量修复收敛;
        ///     换来的是出生路径上零查询。
        /// </summary>
        internal static bool AlignActor(Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt() ||
                !pActor.isAlive()) return false;
            try
            {
                pActor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
                if (shiId < 0L) return false;
                Clan target = FindAncestorClan(pActor, shiId);
                if (target?.data == null || target.isRekt() ||
                    ReferenceEquals(pActor.clan, target)) return false;
                if (target.isFull()) return false;
                pActor.setClan(target);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        ///     沿父系向上找同氏祖先的族。全内存,无查询。
        /// </summary>
        private static Clan FindAncestorClan(Actor pActor, long pShiId)
        {
            Actor current = pActor;
            for (int hop = 0; hop < MaximumAncestorHops; hop++)
            {
                Actor father = FindFather(current);
                if (father?.data == null || father.isRekt()) return null;
                father.data.get(LineageKeys.SHI_ID, out long fatherShi, -1L);
                if (fatherShi != pShiId) return null;
                Clan clan = father.clan;
                if (clan?.data != null && !clan.isRekt()) return clan;
                current = father;
            }
            return null;
        }

        private static Actor FindFather(Actor pActor)
        {
            if (pActor?.data == null) return null;
            Actor first = FindActor(pActor.data.parent_id_1);
            if (first?.data != null && first.isSexMale()) return first;
            Actor second = FindActor(pActor.data.parent_id_2);
            return second?.data != null && second.isSexMale() ? second : null;
        }

        /// <summary>
        ///     成员里挑锚点族：优先某人自己就是族长的那个族（族长在哪族,
        ///     哪族就是本宗）,否则退回第一个有族的人。
        /// </summary>
        private static Clan ResolveAnchorClan(List<Actor> pMembers)
        {
            Clan fallback = null;
            foreach (Actor member in pMembers)
            {
                Clan clan = member?.clan;
                if (clan?.data == null || clan.isRekt()) continue;
                try
                {
                    if (clan.getChief() == member) return clan;
                }
                catch { }
                fallback ??= clan;
            }
            return fallback;
        }

        private static Actor FindActor(long pActorId)
        {
            try
            {
                return pActorId >= 0L
                    ? World.world?.units?.get(pActorId)
                    : null;
            }
            catch { return null; }
        }
    }
}
