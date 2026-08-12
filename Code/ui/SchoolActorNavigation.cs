namespace AncientWarfare3.ui
{
    internal static class SchoolActorNavigation
    {
        public static void Open(Actor pActor)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt()) return;
            MetaTypeAsset unitMeta = MetaType.Unit.getAsset();
            if (unitMeta == null) return;
            ScrollWindow.finishAnimations();
            unitMeta.selectAndInspect(pActor, pFromNameplate: false,
                pCheckNameplate: false, pClearAction: false);
        }
    }
}
