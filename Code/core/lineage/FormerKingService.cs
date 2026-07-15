namespace AncientWarfare3.core.lineage
{
    internal static class FormerKingService
    {
        private static HistoryText H(string pKey) => HistoryLocalizationRules.H(pKey);
        private static string T(string pKey) => HistoryLocalizationRules.Text(pKey);

        public static void OnKingdomDestroyed(Kingdom pKingdom, Actor pLastKing, bool pWasMandateKingdom)
        {
            if (pKingdom?.data == null || pLastKing?.data == null) return;
            bool alive = IsAlive(pLastKing);
            if (!FormerKingTraitRules.ShouldMarkFormerKing(
                    pKingdomDestroyed: true,
                    pWasLastKing: pKingdom.king == pLastKing,
                    pFormerKingAlive: alive))
                return;

            string color = HistoryColors.FromKingdom(pKingdom);
            string title = pWasMandateKingdom
                ? FormerKingTraitRules.BuildMandateDeposedTitle(pKingdom.name)
                : T("aw_hist_former_king_title_common");

            if (!pLastKing.hasTrait(LineageKeys.TRAIT_FORMER_KING))
                pLastKing.addTrait(LineageKeys.TRAIT_FORMER_KING);
            pLastKing.data.set(LineageKeys.FORMER_KINGDOM_ID, pKingdom.id);
            pLastKing.data.set(LineageKeys.FORMER_KINGDOM_NAME, pKingdom.name ?? "");
            pLastKing.data.set(LineageKeys.FORMER_KINGDOM_COLOR, color);
            pLastKing.data.set(LineageKeys.FORMER_KING_TITLE, title);
            pLastKing.data.set(LineageKeys.FORMER_KING_MANDATE, pWasMandateKingdom);

            HistoryText titleText = HistoryText.Colored(title, color);
            HistoryText message = HistoryLocalizationRules.CurrentLanguage() == "en"
                ? HistoryText.Actor(pLastKing) + H("aw_hist_former_king_after_fall_mid") +
                  titleText + H("aw_hist_former_king_at") + HistoryText.Kingdom(pKingdom) +
                  H("aw_hist_former_king_fell")
                : HistoryText.Actor(pLastKing) + H("aw_hist_former_king_at") +
                  HistoryText.Kingdom(pKingdom) + H("aw_hist_former_king_after_fall_mid") +
                  titleText;
            HistoryWriter.RecordPerson(pLastKing.data.id, pKingdom, pLastKing.getName(),
                PersonEvent.FORMER_KING,
                message,
                ChronicleCategory.HONOR,
                HistoryTarget.Kingdom(pKingdom));

            LineageService.ArchiveActor(pLastKing, pAlive: true);
        }

        public static void StoreSnapshot(Actor pActor, long pKingdomId, string pKingdomName,
            string pKingdomColor, string pTitle, bool pWasMandateKingdom)
        {
            if (pActor?.data == null || string.IsNullOrEmpty(pTitle)) return;
            if (!pActor.hasTrait(LineageKeys.TRAIT_FORMER_KING))
                pActor.addTrait(LineageKeys.TRAIT_FORMER_KING);
            pActor.data.set(LineageKeys.FORMER_KINGDOM_ID, pKingdomId);
            pActor.data.set(LineageKeys.FORMER_KINGDOM_NAME, pKingdomName ?? "");
            pActor.data.set(LineageKeys.FORMER_KINGDOM_COLOR, pKingdomColor ?? "");
            pActor.data.set(LineageKeys.FORMER_KING_TITLE, pTitle);
            pActor.data.set(LineageKeys.FORMER_KING_MANDATE, pWasMandateKingdom);
            LineageService.ArchiveActor(pActor, pAlive: true);
        }

        public static void ClearSnapshot(Actor pActor)
        {
            if (pActor?.data == null) return;
            if (pActor.hasTrait(LineageKeys.TRAIT_FORMER_KING))
                pActor.removeTrait(LineageKeys.TRAIT_FORMER_KING);
            pActor.data.set(LineageKeys.FORMER_KINGDOM_ID, -1L);
            pActor.data.set(LineageKeys.FORMER_KINGDOM_NAME, "");
            pActor.data.set(LineageKeys.FORMER_KINGDOM_COLOR, "");
            pActor.data.set(LineageKeys.FORMER_KING_TITLE, "");
            pActor.data.set(LineageKeys.FORMER_KING_MANDATE, false);
        }

        private static bool IsAlive(Actor pActor)
        {
            try { return pActor?.data != null && !pActor.isRekt() && pActor.isAlive(); }
            catch { return false; }
        }
    }
}
