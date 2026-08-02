using AncientWarfare3.core.naming;

namespace AncientWarfare3.core.lineage
{
    public enum WesternLineageAdmissionAction
    {
        Reject,
        ReuseComplete,
        InheritRelative,
        CompletePartialBranch,
        CreateRoot
    }

    public enum WesternOriginalClanSyncAction
    {
        None,
        RenameExisting,
        BindFamilyClan,
        CreateClan
    }

    public static class WesternLineageAdmissionRules
    {
        public static bool ShouldRunPromotionHook(
            NamingProfileId pProfile, bool isXiaActor,
            bool isXiaKingdom, bool isForeignPseudoDynasty)
        {
            if (pProfile == NamingProfileId.Western ||
                pProfile == NamingProfileId.OrcNomadic)
                return true;
            return isXiaActor || isXiaKingdom || isForeignPseudoDynasty;
        }

        public static bool ShouldRunKingAdmission(bool fromLoad,
            bool actorIsActualKing, NamingProfileId pProfile)
        {
            if (!actorIsActualKing) return false;
            return !fromLoad || pProfile == NamingProfileId.Western ||
                   pProfile == NamingProfileId.OrcNomadic;
        }

        public static bool IsRoleAdmission(bool ruler, bool heir,
            bool noble, bool official)
        {
            return ruler || heir || noble || official;
        }

        public static WesternOriginalClanSyncAction ResolveOriginalClanSync(
            NamingProfileId pProfile, bool ruler, bool hasActorClan,
            bool hasMatchingFamilyClan)
        {
            bool supported = pProfile == NamingProfileId.Western ||
                             pProfile == NamingProfileId.OrcNomadic;
            if (!ruler || !supported)
                return WesternOriginalClanSyncAction.None;
            if (hasActorClan)
                return WesternOriginalClanSyncAction.RenameExisting;
            return hasMatchingFamilyClan
                ? WesternOriginalClanSyncAction.BindFamilyClan
                : WesternOriginalClanSyncAction.CreateClan;
        }

        public static WesternLineageAdmissionAction Resolve(
            NamingProfileId pProfile, bool valid, bool civilized,
            bool rekt, bool canonicalMaster,
            bool requiresCompleteFamily, bool hasStableLineage,
            bool hasCompleteLineageAndShi,
            bool hasSameProfileRelativeCompleteSource)
        {
            bool supportedProfile =
                pProfile == NamingProfileId.Western ||
                pProfile == NamingProfileId.OrcNomadic;
            if (!supportedProfile || !valid || !civilized || rekt ||
                canonicalMaster || !requiresCompleteFamily)
                return WesternLineageAdmissionAction.Reject;

            if (hasCompleteLineageAndShi)
                return WesternLineageAdmissionAction.ReuseComplete;
            if (hasSameProfileRelativeCompleteSource)
                return WesternLineageAdmissionAction.InheritRelative;
            if (hasStableLineage)
                return WesternLineageAdmissionAction.CompletePartialBranch;
            return WesternLineageAdmissionAction.CreateRoot;
        }
    }
}
