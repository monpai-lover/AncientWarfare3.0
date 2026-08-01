using AncientWarfare3.core.naming;

namespace AncientWarfare3.core.lineage
{
    public readonly struct WesternLineageBirthAdmissionDecision
    {
        public WesternLineageBirthAdmissionDecision(bool pUseFullPath,
            bool pUseLightweightEdges)
        {
            UseFullPath = pUseFullPath;
            UseLightweightEdges = pUseLightweightEdges;
        }

        public bool UseFullPath { get; }
        public bool UseLightweightEdges { get; }
    }

    public static class WesternLineageEligibilityRules
    {
        public static bool UsesWesternLineage(bool civilized,
            bool biologicalXia, bool monkey, bool valid)
        {
            return valid && civilized && !biologicalXia && !monkey;
        }

        public static bool RequiresFullArchive(bool ruler, bool heir,
            bool noble, bool official)
        {
            return ruler || heir || noble || official;
        }

        public static bool UsesAwLineageSystem(NamingProfileId pProfile,
            bool hasStableLineageId)
        {
            if (!hasStableLineageId) return false;
            return pProfile == NamingProfileId.Xia ||
                   pProfile == NamingProfileId.Monkey ||
                   pProfile == NamingProfileId.Western ||
                   pProfile == NamingProfileId.OrcNomadic;
        }

        public static bool UsesLightweightParentEdges(
            NamingProfileId pProfile)
        {
            return pProfile == NamingProfileId.Western ||
                   pProfile == NamingProfileId.OrcNomadic;
        }

        public static bool ShouldUseFullBirthPath(
            NamingProfileId pProfile, bool biologicalXia, bool monkey,
            bool civilized, bool parentHasLineage,
            bool requiresFullArchive)
        {
            if (biologicalXia || monkey) return true;
            if (!civilized || !parentHasLineage) return false;
            if (pProfile == NamingProfileId.Xia) return true;
            return requiresFullArchive &&
                   (pProfile == NamingProfileId.Western ||
                    pProfile == NamingProfileId.OrcNomadic);
        }

        public static WesternLineageBirthAdmissionDecision
            ResolveBirthAdmission(NamingProfileId pProfile,
                bool biologicalXia, bool monkey, bool civilized,
                bool parentHasLineage, bool requiresFullArchive)
        {
            bool useFullPath = ShouldUseFullBirthPath(pProfile,
                biologicalXia, monkey, civilized, parentHasLineage,
                requiresFullArchive);
            bool useLightweightEdges = !useFullPath &&
                UsesWesternLineage(civilized, biologicalXia, monkey,
                    valid: true) &&
                UsesLightweightParentEdges(pProfile);
            return new WesternLineageBirthAdmissionDecision(useFullPath,
                useLightweightEdges);
        }
    }
}
