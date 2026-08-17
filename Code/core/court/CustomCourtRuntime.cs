using System.Globalization;

namespace AncientWarfare3.core.court
{
    public static class CustomCourtRuntime
    {
        public static readonly CustomCourtInstanceService Instances =
            new CustomCourtInstanceService();

        public static readonly CourtDefinitionResolver Resolver =
            new CourtDefinitionResolver(Instances);

        public static string KingdomKey(Kingdom kingdom)
        {
            return kingdom == null
                ? string.Empty
                : kingdom.id.ToString(CultureInfo.InvariantCulture);
        }

        public static bool HasInstance(Kingdom kingdom)
        {
            CustomCourtInstance instance;
            return kingdom != null && Instances.TryGet(KingdomKey(kingdom),
                out instance);
        }
    }
}
