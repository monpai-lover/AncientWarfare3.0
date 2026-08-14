namespace AncientWarfare3.ui
{
    internal static class SchoolActorNavigation
    {
        public static void Open(Actor pActor)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt()) return;
            ScrollWindow.finishAnimations();
            ActionLibrary.openUnitWindow(pActor);
        }
    }
}
