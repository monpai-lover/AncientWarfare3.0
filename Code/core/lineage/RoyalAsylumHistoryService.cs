using System;

namespace AncientWarfare3.core.lineage
{
    internal static class RoyalAsylumHistoryService
    {
        private static HistoryText H(string pKey) => HistoryLocalizationRules.H(pKey);

        public static void RecordStarted(Actor pActor, Kingdom pHome, City pHostCity)
        {
            if (pActor?.data == null || pHome?.data == null || pHostCity?.data == null) return;
            HistoryText text = HistoryText.Actor(pActor) +
                               H("aw_hist_asylum_left_home") +
                               HistoryText.Kingdom(pHome) +
                               H("aw_hist_asylum_went_to") +
                               HistoryText.City(pHostCity, pHostCity.kingdom) +
                               H("aw_hist_asylum_shelter_suffix");
            Record(pActor, pHome, PersonEvent.ROYAL_ASYLUM_STARTED, text,
                HistoryTarget.City(pHostCity));
        }

        public static void RecordRelocated(Actor pActor, Kingdom pHome, City pHostCity)
        {
            if (pActor?.data == null || pHome?.data == null || pHostCity?.data == null) return;
            HistoryText text = HistoryText.Actor(pActor) +
                               H("aw_hist_asylum_relocated_to") +
                               HistoryText.City(pHostCity, pHostCity.kingdom) +
                               H("aw_hist_asylum_continue_suffix");
            Record(pActor, pHome, PersonEvent.ROYAL_ASYLUM_RELOCATED, text,
                HistoryTarget.City(pHostCity));
        }

        public static void RecordReturned(Actor pActor, Kingdom pHome, City pDestination)
        {
            if (pActor?.data == null || pHome?.data == null || pDestination?.data == null) return;
            HistoryText text = HistoryText.Actor(pActor) +
                               H("aw_hist_asylum_returned_to") +
                               HistoryText.City(pDestination, pHome);
            Record(pActor, pHome, PersonEvent.ROYAL_ASYLUM_RETURNED, text,
                HistoryTarget.City(pDestination));
        }

        public static void RecordNaturalized(Actor pActor, string pHomeName,
            Kingdom pHost, City pHostCity)
        {
            if (pActor?.data == null || pHost?.data == null || pHostCity?.data == null) return;
            string homeName = string.IsNullOrWhiteSpace(pHomeName)
                ? HistoryLocalizationRules.Text("aw_unknown_kingdom")
                : pHomeName;
            HistoryText text = HistoryText.Actor(pActor) +
                               H("aw_hist_asylum_after_home_fall") +
                               HistoryText.PlainText(homeName) +
                               H("aw_hist_asylum_joined_host") +
                               HistoryText.Kingdom(pHost) +
                               H("aw_hist_asylum_settled_at") +
                               HistoryText.City(pHostCity, pHost);
            Record(pActor, pHost, PersonEvent.ROYAL_ASYLUM_NATURALIZED, text,
                HistoryTarget.City(pHostCity));
        }

        private static void Record(Actor pActor, Kingdom pContext, string pEvent,
            HistoryText pText, HistoryTarget pTarget)
        {
            try
            {
                HistoryWriter.RecordPerson(pActor.data.id, pContext, pActor.getName(),
                    pEvent, pText, ChronicleCategory.LIFE, pTarget);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Royal asylum history failed actor=" +
                                    pActor.data.id + " event=" + pEvent + ": " +
                                    error.Message);
            }
        }
    }
}
