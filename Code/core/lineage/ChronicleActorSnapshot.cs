namespace AncientWarfare3.core.lineage
{
    internal sealed class ChronicleActorSnapshot
    {
        public long actor_id;
        public string actor_name;
        public string actor_color;
        public long city_id;
        public string city_name;
        public string city_color;
        public HistoryWriter.DeferredContext context;
        public HistoryWriter.PersonSnapshot person;

        public static ChronicleActorSnapshot Capture(Actor pActor, Kingdom pKingdom, City pCity)
        {
            Kingdom kingdom = pKingdom ?? pActor?.kingdom ?? pCity?.kingdom;
            City city = pCity ?? pActor?.city;
            return new ChronicleActorSnapshot
            {
                actor_id = pActor?.data?.id ?? -1L,
                actor_name = pActor?.getName() ?? "",
                actor_color = HistoryColors.FromActor(pActor),
                city_id = city?.data?.id ?? -1L,
                city_name = city?.data?.name ?? "",
                city_color = HistoryColors.FromCity(city, kingdom),
                context = HistoryWriter.CaptureDeferredContext(kingdom),
                person = HistoryWriter.CapturePersonSnapshot(pActor)
            };
        }

        public HistoryText ActorText()
        {
            return HistoryText.Reference(actor_name, actor_color, "actor", actor_id);
        }

        public HistoryText CityText()
        {
            return HistoryText.Reference(city_name, city_color, "city", city_id);
        }
    }
}
