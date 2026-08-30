using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    /// <summary>
    /// 每个王国的官员候选目录 —— 也就是「等待池」。补缺时从这里取人,
    /// 资格判定仍然在任命那一刻实时做,这里只负责免掉重复的全国扫描。
    ///
    /// **靠事件维护,不定期重建。** 以前这张表按年作废,于是每个王国每年都要
    /// 重走一遍 <c>getUnits()</c> 再对全国两千多人排一次序。现在:
    ///
    ///   入池  <see cref="EnsurePresent"/>  成年、中举、改籍
    ///   出池  <see cref="Remove"/>          死亡、离境
    ///   换位  <see cref="Reposition"/>      品级变动(排序键变了)
    ///
    /// 兜底保留,但只在**出错时**才重建:补缺走到「没人可补」而席位确实空着,
    /// 说明池子可能漏了人 —— 那时才 <see cref="Invalidate"/> 一次重来。
    /// 调用方(CourtVacancyReconciliationService)保证同一代际只自愈一次,
    /// 否则「重建 → 还是没人 → 再重建」会变成无限重建。
    ///
    /// 两个方向的漂移都能自愈,所以漏接一个事件只会损失一点性能,不会让官位
    /// 永远补不上:
    ///   多收了人(死了没摘干净)—— 取人时 CanUseCandidateFacts 会滤掉;
    ///   少收了人(某个入池事件没接上)—— 触发上面那次重建。
    /// </summary>
    internal static class OfficerCandidateCatalog
    {
        private sealed class Entry
        {
            internal List<Actor> Actors = new List<Actor>();
            internal HashSet<long> Ids = new HashSet<long>();
        }

        private static readonly Dictionary<long, Entry> Entries =
            new Dictionary<long, Entry>();

        internal static List<Actor> GetOrBuild(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return new List<Actor>();
            if (Entries.TryGetValue(pKingdom.id, out Entry existing))
                return existing.Actors;
            var actors = new List<Actor>();
            try
            {
                foreach (Actor actor in pKingdom.getUnits())
                    if (actor?.data != null) actors.Add(actor);
            }
            catch { }
            actors = actors.OrderByDescending(p =>
                    OfficialCareerStateService.ReadRankFast(p))
                .ThenBy(p => p.data.id).ToList();
            var ids = new HashSet<long>();
            for (int index = 0; index < actors.Count; index++)
                ids.Add(actors[index].data.id);
            Entries[pKingdom.id] =
                new Entry { Actors = actors, Ids = ids };
            return actors;
        }

        /// <summary>
        ///     把一个人补进名单,而不是把整张表丢掉。
        ///
        ///     换一张新表而不是就地 Insert:<see cref="GetOrBuild"/> 把内部列表
        ///     原样交给 <c>CourtCandidateSession.Actors</c>,就地改会让正在遍历它
        ///     的补缺轮次抛 <c>Collection was modified</c>。换表的语义是:已经取到
        ///     名单的那一轮看不到这次改动,下一轮才看到。
        /// </summary>
        internal static void EnsurePresent(Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null) return;
            if (!Entries.TryGetValue(pKingdom.id, out Entry entry)) return;
            long actorId = pActor.data.id;
            if (entry.Ids.Contains(actorId)) return;
            // 表按 rank 降序、同 rank 按 id 升序。二分定位保持这个不变量,
            // 免得为了加一个人再排一次序。
            int rank = OfficialCareerStateService.ReadRankFast(pActor);
            int low = 0;
            int high = entry.Actors.Count;
            while (low < high)
            {
                int middle = low + ((high - low) >> 1);
                Actor other = entry.Actors[middle];
                int otherRank = OfficialCareerStateService.ReadRankFast(other);
                bool sortsBefore = otherRank > rank ||
                                   (otherRank == rank &&
                                    other.data.id < actorId);
                if (sortsBefore) low = middle + 1;
                else high = middle;
            }
            var next = new List<Actor>(entry.Actors.Count + 1);
            next.AddRange(entry.Actors);
            next.Insert(low, pActor);
            var nextIds = new HashSet<long>(entry.Ids) { actorId };
            Entries[pKingdom.id] = new Entry
            {
                Actors = next, Ids = nextIds
            };
            // 城官候选池同样是事件维护的 —— 只算这一个人的排序键插进去,
            // 不重建。见 CityCandidatePool。
            LocalCourtAppointmentService.OnCandidateChanged(pKingdom, pActor);
        }

        /// <summary>
        ///     把一个人摘出名单(死亡、离境)。没有这一步,不再定期重建的表会把
        ///     每一个曾经活过的人永远留着。
        ///
        ///     同样换表 —— 理由见 <see cref="EnsurePresent"/>。
        /// </summary>
        internal static void Remove(Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null) return;
            if (!Entries.TryGetValue(pKingdom.id, out Entry entry)) return;
            long actorId = pActor.data.id;
            if (!entry.Ids.Contains(actorId)) return;
            var next = new List<Actor>(Math.Max(0, entry.Actors.Count - 1));
            for (int index = 0; index < entry.Actors.Count; index++)
            {
                Actor other = entry.Actors[index];
                if (other?.data != null && other.data.id == actorId) continue;
                next.Add(other);
            }
            var nextIds = new HashSet<long>(entry.Ids);
            nextIds.Remove(actorId);
            Entries[pKingdom.id] = new Entry
            {
                Actors = next, Ids = nextIds
            };
            LocalCourtAppointmentService.OnCandidateLost(pKingdom, pActor);
        }

        /// <summary>
        ///     品级变了,排序键就变了 —— 摘掉再按新键插回去,保持表有序。
        ///     不这么做,表会随着升迁慢慢失序,而中央补缺的择优依赖
        ///     「有品级的人排在前面」这个前提提前收敛。
        /// </summary>
        internal static void Reposition(Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null) return;
            if (!Entries.TryGetValue(pKingdom.id, out Entry entry)) return;
            if (!entry.Ids.Contains(pActor.data.id)) return;
            Remove(pKingdom, pActor);
            EnsurePresent(pKingdom, pActor);
        }

        internal static void Invalidate(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            Entries.Remove(pKingdom.id);
            // 兜底重建是整套池子的,城官池一起丢 —— 否则「补不上 → 重建」
            // 只重建了一半,漏收的人仍然进不来。
            CityCandidatePool.Invalidate(pKingdom);
        }

        internal static void ClearRuntime()
        {
            Entries.Clear();
            CityCandidatePool.ClearRuntime();
        }
    }
}
