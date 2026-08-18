namespace AncientWarfare3.core.lineage
{
    public enum BanditAmnestyRewardKind
    {
        None,
        Office,
        VirtualTitle
    }

    public enum BanditAmnestySettlementPhase
    {
        Prepared,
        TerritorialSettlement,
        RewardPending,
        Completed,
        Failed
    }

    internal sealed class PeasantRebelBanditAmnestyOffer
    {
        public BanditAmnestyRewardKind RewardKind =
            BanditAmnestyRewardKind.None;
        public string OfficeId = "";
        public string TitleText = "";
        public bool Hereditary = true;
    }
}
