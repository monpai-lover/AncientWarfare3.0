namespace AncientWarfare3.ui
{
    internal static class SchoolActorNavigation
    {
        public static void Open(Actor pActor)
        {
            if (pActor == null || pActor.isRekt()) return;
            ActionLibrary.openUnitWindow(pActor);
        }
    }
}
