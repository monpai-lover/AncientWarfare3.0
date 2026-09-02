using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    /// Single read-only title boundary used by archives and genealogy.
    /// Virtual titles are deliberately below formal hereditary titles and
    /// above ordinary office text, so a granted title remains the primary
    /// ceremonial identity without replacing imperial/posthumous styles.
    /// </summary>
    internal static class CeremonialTitleResolver
    {
        internal static string Resolve(Actor pActor)
        {
            if (pActor?.data == null) return "";
            try
            {
                if (pActor.isKing() && pActor.kingdom?.data != null)
                    return RulerAppellationService.GetFullLivingAppellation(
                        pActor.kingdom) ?? "";
            }
            catch { }

            try
            {
                // 登记在册的继承人 —— 太子/储君/世子/留后,由**他所继承的那个国**
                // 定称谓(见 HeirService.ResolveHeirKingdom:归化可能还没落定,
                // 拿 actor.kingdom 会算错甚至算没)。
                //
                // 这一段原来只认流寇政权(IsBandit),正常王朝的太子在这里一无所获,
                // 于是 actor 面板那行身份是空的,存档里的 primary_ceremonial_title
                // 也跟着空 —— 族谱 tooltip 一并看不到。BuildSocialTitle 自己
                // 开头就分流了流寇,所以一条通用分支就够,不必特判。
                Kingdom heirKingdom = HeirService.ResolveHeirKingdom(pActor);
                if (heirKingdom?.data != null)
                    return HeirTitleRules.BuildSocialTitle("", heirKingdom);
            }
            catch { }

            try
            {
                string formal = NobleRankService.GetDisplayTitle(pActor);
                if (!string.IsNullOrWhiteSpace(formal)) return formal;
            }
            catch { }

            string virtualTitle = VirtualNobleTitleService.GetPrimaryTitle(pActor);
            if (!string.IsNullOrWhiteSpace(virtualTitle)) return virtualTitle;

            try
            {
                string dynastic = DynasticTitleService.ResolveLivingTitle(pActor);
                if (!string.IsNullOrWhiteSpace(dynastic)) return dynastic;
            }
            catch { }
            return "";
        }

        internal static string ResolveArchive(Actor pActor,
            ActorArchiveTableItem pPrevious)
        {
            if (pActor?.data != null)
            {
                string current = Resolve(pActor);
                if (!string.IsNullOrWhiteSpace(current)) return current;
            }
            return pPrevious?.primary_ceremonial_title ?? "";
        }

        internal static string ResolveArchived(ActorArchiveTableItem pRow)
        {
            return pRow?.primary_ceremonial_title ?? "";
        }
    }
}
