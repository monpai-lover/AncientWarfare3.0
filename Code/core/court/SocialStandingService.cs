using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    /// <summary>
    ///     门第判定的唯一入口。科举名单、人物面板「身份」行、史料归类
    ///     三处都从这里取值，口径必须一致。
    ///
    ///     判据见 <see cref="SocialStandingRules"/>。这里只负责取数与缓存。
    ///
    ///     <b>性能</b>：一个宗族的门第对全族成员是同一个值，而科举名单一次
    ///     要评上百人 —— 逐人回溯全族是不能接受的。按 shi 缓存，朝廷任免与
    ///     城主变动时整体置脏（<see cref="MarkDirty"/>），读档换世界时清空。
    /// </summary>
    internal static class SocialStandingService
    {
        /// <summary>
        ///     回溯一个氏时最多看多少活人。氏可以很大，而这是 UI 路径，
        ///     不能无界遍历；够到高位的人通常就在前列（按出生时间排），
        ///     漏掉的那点尾巴不值得为它把开销翻上去。
        /// </summary>
        private const int MaximumMembersPerShi = 64;

        private static readonly Dictionary<long, ShiOfficeReach> ReachByShi =
            new Dictionary<long, ShiOfficeReach>();
        private static readonly object Gate = new object();

        /// <summary>
        ///     朝廷任免、城主更替之后调用：门第可能整体变了。
        ///     代价只是下一次读取重算一遍，不做增量。
        /// </summary>
        internal static void MarkDirty()
        {
            lock (Gate) ReachByShi.Clear();
        }

        internal static void ClearRuntime()
        {
            lock (Gate) ReachByShi.Clear();
        }

        /// <summary>
        ///     这个人的门第，返回
        ///     <see cref="CivilServiceExamRules"/> 里的出身常量之一。
        /// </summary>
        internal static string Resolve(Actor pActor)
        {
            if (pActor?.data == null)
                return CivilServiceExamRules.CommonerOrigin;
            try
            {
                pActor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
                bool hasShi = shiId >= 0L;
                if (!hasShi)
                    return CivilServiceExamRules.CommonerOrigin;
                return SocialStandingRules.Resolve(true,
                    IsRoyalShi(pActor, shiId), ResolveReach(shiId));
            }
            catch { return CivilServiceExamRules.CommonerOrigin; }
        }

        /// <summary>
        ///     本人的氏是否就是所在国当今王室的氏。
        ///
        ///     以王国记录的正统氏为准（<c>KINGDOM_LEGITIMATE_SHI_ID</c>），
        ///     取不到就退回当今君主本人的氏 —— 开国之初、或旧存档里那个字段
        ///     可能还没落下来。
        /// </summary>
        private static bool IsRoyalShi(Actor pActor, long pShiId)
        {
            Kingdom kingdom = pActor.kingdom;
            if (kingdom?.data == null || pShiId < 0L) return false;
            kingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID,
                out long royalShiId, -1L);
            if (royalShiId < 0L)
            {
                Actor king = kingdom.king;
                if (king?.data == null) return false;
                king.data.get(LineageKeys.SHI_ID, out royalShiId, -1L);
            }
            return royalShiId >= 0L && royalShiId == pShiId;
        }

        /// <summary>
        ///     全族够到的最高官职层级，按 shi 缓存。
        /// </summary>
        private static ShiOfficeReach ResolveReach(long pShiId)
        {
            lock (Gate)
            {
                if (ReachByShi.TryGetValue(pShiId,
                        out ShiOfficeReach cached)) return cached;
            }
            ShiOfficeReach reach = ComputeReach(pShiId);
            lock (Gate) ReachByShi[pShiId] = reach;
            return reach;
        }

        private static ShiOfficeReach ComputeReach(long pShiId)
        {
            ShiOfficeReach reach = ShiOfficeReach.None;
            try
            {
                List<long> memberIds = LineageQuery.GetLivingShiMemberIds(
                    pShiId, MaximumMembersPerShi);
                if (memberIds == null) return reach;
                foreach (long id in memberIds)
                {
                    Actor member = FindActor(id);
                    if (member?.data == null || member.isRekt()) continue;
                    reach = SocialStandingRules.Max(reach, ReachOf(member));
                    // 已经到顶就不必再看下去。
                    if (reach == ShiOfficeReach.HighOffice) break;
                }
            }
            catch { }
            return reach;
        }

        private static ShiOfficeReach ReachOf(Actor pMember)
        {
            bool cityLeader = false;
            try { cityLeader = pMember.isCityLeader(); }
            catch { }
            return SocialStandingRules.ReachOf(cityLeader,
                ResolveOfficeLayer(pMember));
        }

        /// <summary>
        ///     这个人当前所任官职的层级；没有官职返回空串。
        ///
        ///     直接读 actor 上的 <c>COURT_LAYER</c> —— 任命时和官职 id 一起
        ///     写下的，不必再去官职表解析一次定义。
        /// </summary>
        private static string ResolveOfficeLayer(Actor pMember)
        {
            try
            {
                pMember.data.get(LineageKeys.COURT_LAYER, out string layer,
                    "");
                if (!string.IsNullOrWhiteSpace(layer)) return layer;
                // 旧存档里可能只有官职 id 没有层级,退回查表。
                pMember.data.get(LineageKeys.COURT_OFFICE_ID,
                    out string officeId, "");
                if (string.IsNullOrWhiteSpace(officeId)) return "";
                CourtOfficeDefinition definition =
                    CourtProfileRegistry.FindOffice(pMember.kingdom,
                        officeId) ??
                    CourtProfileRegistry.FindOfficeAcrossProfiles(officeId);
                return definition?.Layer ?? "";
            }
            catch { return ""; }
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
