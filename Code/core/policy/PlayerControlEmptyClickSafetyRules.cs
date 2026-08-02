namespace AncientWarfare3.core.policy
{
    internal static class PlayerControlEmptyClickSafetyRules
    {
        public static bool CanInvokeActor(bool actorPresent,
            bool tilePresent, bool assetPresent)
        {
            return actorPresent && tilePresent && assetPresent;
        }
    }
}
