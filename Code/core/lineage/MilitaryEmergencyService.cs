namespace AncientWarfare3.core.lineage
{
    internal static class MilitaryEmergencyService
    {
        public static bool HasAny(Kingdom pKingdom)
        {
            try
            {
                return pKingdom?.data != null &&
                       World.world?.wars?.hasWars(pKingdom) == true;
            }
            catch
            {
                return true;
            }
        }

        public static bool HasDefensive(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || World.world?.wars == null) return false;
            try
            {
                foreach (War war in World.world.wars.getWars(pKingdom))
                    if (war?.data != null && !war.hasEnded() && war.isDefender(pKingdom))
                        return true;
            }
            catch { }
            return false;
        }
    }
}
