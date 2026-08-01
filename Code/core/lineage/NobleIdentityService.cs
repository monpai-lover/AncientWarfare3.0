namespace AncientWarfare3.core.lineage
{
    internal static class NobleIdentityService
    {
        internal static bool IsNobleActor(Actor pActor)
        {
            if (pActor?.data == null) return false;

            bool isCurrentRuler = pActor.kingdom?.king == pActor;
            int formalRank = 0;
            try { formalRank = NobleRankService.ReadHot(pActor).Rank; }
            catch { }
            pActor.data.get(LineageKeys.LINEAGE_STATUS,
                out string lineageStatus, LineageStatus.NONE);
            return NobleIdentityRules.IsNobleIdentity(isCurrentRuler,
                formalRank, lineageStatus);
        }
    }
}
