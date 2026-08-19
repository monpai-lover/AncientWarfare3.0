using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.court
{
    public static partial class CourtProfileRegistry
    {
        private static readonly ICourtProfile Xia = new XiaCourtProfile();
        private static readonly ICourtProfile Western =
            new WesternCourtProfile();

        public static ICourtProfile For(CourtProfileId profileId)
        {
            switch (profileId)
            {
                case CourtProfileId.Xia:
                    return Xia;
                case CourtProfileId.Western:
                    return Western;
                default:
                    return null;
            }
        }

        public static ICourtProfile For(KingdomPolicyProfileId profileId)
        {
            switch (profileId)
            {
                case KingdomPolicyProfileId.Xia:
                    return Xia;
                case KingdomPolicyProfileId.WesternGeneral:
                    return Western;
                default:
                    return null;
            }
        }
    }
}
