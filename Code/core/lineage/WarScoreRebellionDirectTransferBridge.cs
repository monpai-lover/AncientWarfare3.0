namespace AncientWarfare3.core.lineage
{
    public sealed partial class WarScoreService
    {
        public static void ClearDirectRebellionTransferState(long pWarId,
            long pCityId)
        {
            try
            {
                if (pCityId < 0) return;
                PendingCityOccupations.Remove(pCityId);
                if (pWarId < 0) return;
                WarScoreService runtime = GetRuntime();
                if (runtime == null ||
                    !runtime.TryGetFrozenCityControl(pWarId, pCityId,
                        out WarScoreControlState state)) return;
                ClearGoalControlForCity(runtime, state, pCityId,
                    CurrentWorldTime());
            }
            catch { }
        }
    }
}
