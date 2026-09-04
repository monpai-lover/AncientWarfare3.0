using System;
using AncientWarfare3.content.figures;

namespace AncientWarfare3.core.lineage
{
    public static class HistoricalFigureCardIdentityService
    {
        public const string MinisterRole = "minister";

        public static bool IsCardActor(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.HISTORICAL_CARD_ID,
                out string cardId, "");
            return !string.IsNullOrWhiteSpace(cardId);
        }

        public static bool IsMinisterCardActor(Actor pActor)
        {
            if (!IsCardActor(pActor)) return false;
            pActor.data.get(LineageKeys.HISTORICAL_CARD_ROLE,
                out string role, "");
            return string.Equals(role, MinisterRole,
                StringComparison.OrdinalIgnoreCase);
        }

        public static string CourtDisplayName(Actor pActor)
        {
            string name = pActor?.getName() ?? "";
            if (!IsMinisterCardActor(pActor)) return name;
            pActor.data.get(LineageKeys.HISTORICAL_CARD_ID,
                out string cardId, "");
            HistoricalFigureCardDefinition card =
                HistoricalFigureCardCatalog.Get(cardId);
            string historicalKingdom = card?.HistoricalKingdomName?.Trim() ?? "";
            return string.IsNullOrEmpty(historicalKingdom) ||
                   string.IsNullOrEmpty(name)
                ? name
                : name + "\uff08" + historicalKingdom + "\uff09";
        }

        internal static void Apply(Actor pActor,
            HistoricalFigureCardDefinition pDefinition, string pDrawId,
            string pDeploymentId)
        {
            if (pActor?.data == null || pDefinition == null)
                throw new System.ArgumentNullException();

            pActor.addTrait(HistoricalFigureService.TRAIT_FIGURE);
            pActor.addTrait(HistoricalFigureService.TRAIT_FIRST);
            pActor.setHealth(Math.Max(1, pDefinition.CombatHealth));
            pActor.data.favorite = true;
            pActor.data.sex = pDefinition.Sex == HistoricalFigureSex.Female
                ? ActorSex.Female
                : ActorSex.Male;
            pActor.setName(pDefinition.DisplayName, pTrack: false);
            pActor.data.set(LineageKeys.FAMILY_NAME, pDefinition.FamilyName);
            pActor.data.set(LineageKeys.CLAN_NAME, pDefinition.ClanName);
            pActor.data.set(LineageKeys.CHINESE_FAMILY_NAME,
                pDefinition.FamilyName);
            pActor.data.set(LineageKeys.GIVEN_NAME, pDefinition.GivenName);
            pActor.data.set(LineageKeys.HISTORICAL_CARD_ID,
                pDefinition.CardId);
            pActor.data.set(LineageKeys.HISTORICAL_CARD_DRAW_ID,
                pDrawId ?? "");
            pActor.data.set(LineageKeys.HISTORICAL_CARD_DEPLOYMENT_ID,
                pDeploymentId ?? "");
            pActor.data.set(LineageKeys.HISTORICAL_CARD_ROLE,
                pDefinition.Role == HistoricalFigureCardRole.Minister
                    ? MinisterRole : "monarch");
        }
    }
}
