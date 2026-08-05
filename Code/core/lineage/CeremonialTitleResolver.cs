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
