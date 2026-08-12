namespace AncientWarfare3.ui
{
    internal static class SchoolActorNavigation
    {
        public static void Open(Actor pActor)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt()) return;
            MetaTypeAsset unitMeta = MetaType.Unit.getAsset();
            if (unitMeta == null) return;
            // UnitWindow reads only SelectedUnit.unit during OnEnable. The
            // school map mode can leave a stale city selection behind, so
            // establish the native unit selection before it is shown.
            ScrollWindow.finishAnimations();
            SelectedUnit.clear();
            SelectedUnit.select(pActor);
            unitMeta.selectAndInspect(pActor, pFromNameplate: false,
                pCheckNameplate: false, pClearAction: false);
        }
    }
}
