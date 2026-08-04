namespace AncientWarfare3.core.lineage
{
    public static class PreparationRecruitmentProtectionRules
    {
        public static bool IsProtected(bool anyRecognizedHeir, bool king,
            bool cityLeader, bool hasOffice, bool currentArmyCaptain,
            bool existingProtection)
        {
            return anyRecognizedHeir || king || cityLeader || hasOffice ||
                   currentArmyCaptain || existingProtection;
        }
    }
}
