using AncientWarfare3.content.figures;

namespace AncientWarfare3.core.lineage
{
    public static class HistoricalFigureCardRoleRules
    {
        public const int MinisterCandidateBonus = 50;

        public static bool MinisterChangesKingdomName => false;

        public static bool CanDeployMinister(bool pHasValidCity,
            bool pHasLivingKingdom)
        {
            return pHasValidCity && pHasLivingKingdom;
        }

        public static bool IsKingdomFoundingRole(HistoricalFigureCardRole pRole)
        {
            return pRole == HistoricalFigureCardRole.Monarch;
        }

        public static int ApplyCandidateBonus(int pScore,
            HistoricalFigureCardRole pRole)
        {
            return pScore + (pRole == HistoricalFigureCardRole.Minister
                ? MinisterCandidateBonus : 0);
        }

        public static bool IsMinister(HistoricalFigureCardDefinition pCard)
        {
            return pCard != null && pCard.Role == HistoricalFigureCardRole.Minister;
        }

        public static bool IsCivilOfficial(HistoricalFigureCardDefinition pCard)
        {
            return IsMinister(pCard) && pCard.MinisterType ==
                HistoricalFigureCardMinisterType.CivilOfficial;
        }

        public static bool IsMilitaryGeneral(HistoricalFigureCardDefinition pCard)
        {
            return pCard != null && pCard.IsMilitaryGeneral;
        }

        public static bool IsMonarch(HistoricalFigureCardDefinition pCard)
        {
            return pCard != null && pCard.Role == HistoricalFigureCardRole.Monarch;
        }
    }
}
