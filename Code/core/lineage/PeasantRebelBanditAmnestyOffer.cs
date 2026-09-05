namespace AncientWarfare3.core.lineage
{
    public enum BanditAmnestyRewardKind
    {
        None,
        Office,
        VirtualTitle,
        Fief,
        OfficeAndFief
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
        public long FiefCityId = -1L;
        public string TitleText = "";
        public bool Hereditary = true;
    }
}
