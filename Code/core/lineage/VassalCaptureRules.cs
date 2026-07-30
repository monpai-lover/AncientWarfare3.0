namespace AncientWarfare3.core.lineage
{
    public static class VassalCaptureRules
    {
        public static bool ShouldRedirectToRootSuzerain(
            bool capturerIsVassal, bool formerOwnerIsSuzerain,
            bool independenceWarAgainstSuzerain)
        {
            return capturerIsVassal && !formerOwnerIsSuzerain &&
                   !independenceWarAgainstSuzerain;
        }
    }
}
