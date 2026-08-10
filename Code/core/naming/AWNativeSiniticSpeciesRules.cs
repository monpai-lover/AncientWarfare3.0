using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.naming
{
    public static class AWNativeSiniticSpeciesRules
    {
        private static readonly HashSet<string> SpeciesIds =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "civ_dog",
                "civ_fox",
                "civ_lemon_man",
                "civ_rabbit",
                "civ_turtle"
            };

        public static bool IsNativeSiniticSpecies(string pActorAssetId)
        {
            return pActorAssetId != null && SpeciesIds.Contains(pActorAssetId);
        }
    }
}
