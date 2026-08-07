using System;

namespace AncientWarfare3.core.policy
{
    public enum KingdomPolicyProfileId
    {
        None = 0,
        Xia = 1,
        WesternGeneral = 2,
        Common = 3
    }

    public readonly struct KingdomPolicyProfileAssignmentDecision
    {
        public KingdomPolicyProfileAssignmentDecision(
            KingdomPolicyProfileId pProfileId, string pPersistedId,
            bool pShouldWrite)
        {
            ProfileId = pProfileId;
            PersistedId = pPersistedId ?? string.Empty;
            ShouldWrite = pShouldWrite;
        }

        public KingdomPolicyProfileId ProfileId { get; }
        public string PersistedId { get; }
        public bool ShouldWrite { get; }
    }

    public static class KingdomPolicyProfileRules
    {
        public const string XiaPersistedId = "xia";
        public const string WesternGeneralPersistedId = "western_general";

        public static KingdomPolicyProfileId Resolve(bool valid,
            bool civilized, bool nativeXia, bool monkey,
            bool institutionallyXiaized)
        {
            if (!valid || !civilized)
                return KingdomPolicyProfileId.None;

            if (nativeXia || monkey || institutionallyXiaized)
                return KingdomPolicyProfileId.Xia;

            return KingdomPolicyProfileId.WesternGeneral;
        }

        public static bool IsResolvableKingdomProfile(
            KingdomPolicyProfileId pProfileId)
        {
            return pProfileId == KingdomPolicyProfileId.Xia ||
                   pProfileId == KingdomPolicyProfileId.WesternGeneral;
        }

        public static string ResolvePolicyStateLabelKey(
            KingdomPolicyProfileId pProfileId)
        {
            return "aw_policy_state_short";
        }

        public static bool TryParsePersisted(string pPersistedId,
            out KingdomPolicyProfileId pProfileId)
        {
            if (string.Equals(pPersistedId, XiaPersistedId,
                    StringComparison.Ordinal))
            {
                pProfileId = KingdomPolicyProfileId.Xia;
                return true;
            }

            if (string.Equals(pPersistedId, WesternGeneralPersistedId,
                    StringComparison.Ordinal))
            {
                pProfileId = KingdomPolicyProfileId.WesternGeneral;
                return true;
            }

            pProfileId = KingdomPolicyProfileId.None;
            return false;
        }

        public static string ToPersistedId(
            KingdomPolicyProfileId pProfileId)
        {
            switch (pProfileId)
            {
                case KingdomPolicyProfileId.Xia:
                    return XiaPersistedId;
                case KingdomPolicyProfileId.WesternGeneral:
                    return WesternGeneralPersistedId;
                default:
                    return string.Empty;
            }
        }

        public static KingdomPolicyProfileAssignmentDecision DecideAssignment(
            KingdomPolicyProfileId pRuntimeProfileId, string pStoredProfileId)
        {
            if (!IsResolvableKingdomProfile(pRuntimeProfileId))
            {
                return new KingdomPolicyProfileAssignmentDecision(
                    pRuntimeProfileId, string.Empty, false);
            }

            bool hasStoredProfile = TryParsePersisted(pStoredProfileId,
                out KingdomPolicyProfileId storedProfileId);
            if (hasStoredProfile &&
                storedProfileId == KingdomPolicyProfileId.Xia)
            {
                return new KingdomPolicyProfileAssignmentDecision(
                    KingdomPolicyProfileId.Xia, XiaPersistedId, false);
            }

            string runtimePersistedId = ToPersistedId(pRuntimeProfileId);
            bool shouldWrite = !hasStoredProfile ||
                               storedProfileId != pRuntimeProfileId;
            return new KingdomPolicyProfileAssignmentDecision(
                pRuntimeProfileId, runtimePersistedId, shouldWrite);
        }
    }
}
