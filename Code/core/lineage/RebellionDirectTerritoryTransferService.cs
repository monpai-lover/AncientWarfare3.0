namespace AncientWarfare3.core.lineage
{
    public static class RebellionDirectTerritoryTransferService
    {
        public static bool TryResolve(City pCity, Kingdom pCapturer,
            out War pWar)
        {
            pWar = null;
            Kingdom owner = pCity?.kingdom;
            if (pCity?.data == null || owner?.data == null ||
                pCapturer?.data == null || owner == pCapturer) return false;
            try
            {
                foreach (War war in pCapturer.getWars())
                {
                    bool active = war?.data != null && !war.hasEnded();
                    bool opponents = active &&
                                     war.isInWarWith(owner, pCapturer);
                    bool rebellion = active &&
                                     war.getAsset()?.rebellion == true;
                    if (!RebellionDirectTerritoryTransferRules.
                            ShouldTransfer(
                                pCityValid: true,
                                pOwnerValid: true,
                                pCapturerValid: true,
                                pSameKingdom: false,
                                pActiveWar: active,
                                pOpposingSides: opponents,
                                pAuthoritativeRebellion: rebellion))
                        continue;
                    pWar = war;
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
