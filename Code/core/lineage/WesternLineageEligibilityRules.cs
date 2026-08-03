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

        /// <summary>
        /// Selects the western family source without assuming that the
        /// lineage-bearing parent must be male. A male source remains the
        /// preferred convention, but a complete female ruler is a valid
        /// fallback when the father has no compatible identity.
        /// </summary>
        public static int SelectParentSourceSlot(bool parent1Male,
            bool parent1HasLineage, bool parent1Complete, bool parent2Male,
            bool parent2HasLineage, bool parent2Complete,
            bool requireComplete)
        {
            if (requireComplete)
            {
                if (parent1Male && parent1Complete) return 1;
                if (parent2Male && parent2Complete) return 2;
                if (parent1Complete) return 1;
                if (parent2Complete) return 2;
                return -1;
            }

            if (parent1Male && parent1HasLineage) return 1;
            if (parent2Male && parent2HasLineage) return 2;
            if (parent1HasLineage) return 1;
            if (parent2HasLineage) return 2;
            return -1;
        }

        public static bool ShouldEscalateRoyalChildBirth(
            bool parentIsRuler, bool childRequiresFullArchive)
        {
            return parentIsRuler || childRequiresFullArchive;
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
