using AncientWarfare3.core.policy;

namespace AncientWarfare3.ui
{
    internal static class SchoolActorNavigation
    {
        public static void Open(Actor pActor)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt()) return;
            SchoolMapModeService.EndWindowMode();
            SelectedUnit.clear();
            SelectedUnit.select(pActor);
            ScrollWindow.showWindow("unit");
        }
    }
}
