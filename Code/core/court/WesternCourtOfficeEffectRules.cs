using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    public readonly struct WesternCourtOfficeEffects
    {
        public WesternCourtOfficeEffects(float pCentral,
            float pMilitary, float pLocal)
        {
            CentralAdministrationBonus = pCentral;
            MilitaryOrganizationBonus = pMilitary;
            LocalAdministrationBonus = pLocal;
        }

        public float CentralAdministrationBonus { get; }
        public float MilitaryOrganizationBonus { get; }
        public float LocalAdministrationBonus { get; }
    }

    public static class WesternCourtOfficeEffectRules
    {
        public const float CentralOfficeBonus = 0.02f;
        public const float MilitaryOfficeBonus = 0.02f;
        public const float MayorOfficeBonus = 0.01f;
        public const float MaximumCentralAdministrationBonus = 0.10f;
        public const float MaximumMilitaryOrganizationBonus = 0.10f;
        public const float MaximumLocalAdministrationBonus = 0.10f;

        public static WesternCourtOfficeEffects Resolve(
            IEnumerable<string> pActiveOfficeIds)
        {
            float central = 0f;
            float military = 0f;
            float local = 0f;
            foreach (string office in pActiveOfficeIds ??
                     Array.Empty<string>())
            {
                if (IsCentralOffice(office)) central += CentralOfficeBonus;
                if (IsMilitaryOffice(office)) military += MilitaryOfficeBonus;
                if (office == CourtOfficeId.WestMayor ||
                    office == CourtOfficeId.WestCount)
                    local += MayorOfficeBonus;
            }
            return new WesternCourtOfficeEffects(
                Math.Min(MaximumCentralAdministrationBonus, central),
                Math.Min(MaximumMilitaryOrganizationBonus, military),
                Math.Min(MaximumLocalAdministrationBonus, local));
        }

        private static bool IsCentralOffice(string pOfficeId)
        {
            return pOfficeId == CourtOfficeId.WestExecutive ||
                   pOfficeId == CourtOfficeId.WestSenateElder ||
                   pOfficeId == CourtOfficeId.WestHighPriest ||
                   pOfficeId == CourtOfficeId.WestHighJustice ||
                   pOfficeId == CourtOfficeId.WestTreasurer ||
                   pOfficeId == CourtOfficeId.WestPalaceSteward ||
                   pOfficeId == CourtOfficeId.WestRoyalConstable ||
                   pOfficeId == CourtOfficeId.WestSecretary;
        }

        private static bool IsMilitaryOffice(string pOfficeId)
        {
            return pOfficeId == CourtOfficeId.WestFieldGeneral ||
                   pOfficeId == CourtOfficeId.WestMarshal;
        }
    }
}
