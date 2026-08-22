using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    public static class LocalChiefOfficeRules
    {
        public static string SelectChiefOffice(
            IReadOnlyList<string> pOrderedCustomOffices,
            string pBuiltInOffice)
        {
            if (pOrderedCustomOffices != null &&
                pOrderedCustomOffices.Count > 0)
                return pOrderedCustomOffices[0] ?? "";
            return pBuiltInOffice ?? "";
        }
    }
}
