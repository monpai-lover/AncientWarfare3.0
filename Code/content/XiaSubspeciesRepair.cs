namespace AncientWarfare3.content
{
    /// <summary>
    ///     Repairs Xia subspecies created before Xia inherited human-like reproduction traits.
    /// </summary>
    internal static class XiaSubspeciesRepair
    {
        public static int EnsureWorldTraits()
        {
            if (World.world?.subspecies == null) return 0;

            int repaired = 0;
            foreach (Subspecies subspecies in World.world.subspecies)
            {
                if (subspecies == null || !subspecies.isSpecies(XiaRace.ID)) continue;
                if (EnsureTraits(subspecies)) repaired++;
            }
            return repaired;
        }

        internal static bool EnsureTraits(Subspecies pSubspecies)
        {
            if (pSubspecies == null || !pSubspecies.isSpecies(XiaRace.ID)) return false;

            bool changed = false;
            foreach (string traitId in XiaRace.HumanLikeSubspeciesTraits)
            {
                if (pSubspecies.hasTrait(traitId)) continue;
                changed |= pSubspecies.addTrait(traitId, pRemoveOpposites: true);
            }
            return changed;
        }
    }
}
