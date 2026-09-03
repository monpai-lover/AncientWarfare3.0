using System;

namespace AncientWarfare3.core.court
{
    internal enum CityStateRenameValidation
    {
        Success = 0,
        EmptyCityName = 1,
        EmptyStateName = 2
    }

    internal static class CityStateRenameRules
    {
        internal static CityStateRenameValidation ValidateFields(
            string pCityName, string pStateName)
        {
            if (Normalize(pCityName).Length == 0)
                return CityStateRenameValidation.EmptyCityName;
            if (Normalize(pStateName).Length == 0)
                return CityStateRenameValidation.EmptyStateName;
            return CityStateRenameValidation.Success;
        }

        internal static string Normalize(string pValue)
        {
            return string.Join(" ", (pValue ?? string.Empty).Trim()
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }

        internal static bool ShouldSyncStateName(bool pIsSeat,
            bool pTrackedRename, bool pSeatLocked)
        {
            return pIsSeat && pTrackedRename && pSeatLocked;
        }
    }
}
