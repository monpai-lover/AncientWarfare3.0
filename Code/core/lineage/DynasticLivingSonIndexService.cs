namespace AncientWarfare3.core.lineage
{
    internal static class DynasticLivingSonIndexService
    {
        private const string CACHE_INITIALIZED =
            "aw_dynastic_living_sons_initialized";
        private const string LIVING_SONS = "aw_dynastic_living_sons";
        private const string CACHE_VERSION =
            "aw_dynastic_living_sons_version";
        private const int CurrentCacheVersion = 2;

        public static bool HasLivingSon(Actor pParent)
        {
            if (pParent?.data == null) return false;
            if (pParent.current_children_count <= 0)
            {
                Store(pParent, 0);
                return false;
            }

            pParent.data.get(CACHE_INITIALIZED,
                out bool initialized, false);
            pParent.data.get(CACHE_VERSION, out int version, 0);
            if (initialized && version == CurrentCacheVersion)
            {
                pParent.data.get(LIVING_SONS, out int cached, 0);
                return cached > 0;
            }

            int livingSons = 0;
            try
            {
                foreach (Actor child in pParent.getChildren(
                             pOnlyCurrentFamily: false))
                    if (child?.data != null && child.isAlive() &&
                        !child.isRekt() && child.isSexMale())
                        livingSons++;
            }
            catch { }
            Store(pParent, livingSons);
            return livingSons > 0;
        }

        public static void OnChildBorn(Actor pChild,
            Actor pParent1, Actor pParent2)
        {
            if (pChild?.data == null || !pChild.isSexMale()) return;
            IncrementIfInitialized(pParent1);
            if (pParent2 != pParent1) IncrementIfInitialized(pParent2);
        }

        public static void OnActorDying(Actor pActor)
        {
            if (pActor?.data == null || !pActor.isSexMale()) return;
            DynasticMaleLineContinuityService.OnActorDying(pActor);
            DecrementParent(pActor.data.parent_id_1);
            if (pActor.data.parent_id_2 != pActor.data.parent_id_1)
                DecrementParent(pActor.data.parent_id_2);
        }

        private static void IncrementIfInitialized(Actor pParent)
        {
            if (pParent?.data == null) return;
            pParent.data.get(CACHE_INITIALIZED,
                out bool initialized, false);
            pParent.data.get(CACHE_VERSION, out int version, 0);
            if (!initialized || version != CurrentCacheVersion) return;
            pParent.data.get(LIVING_SONS, out int count, 0);
            Store(pParent, count == int.MaxValue ? count : count + 1);
        }

        private static void DecrementParent(long pParentId)
        {
            if (pParentId < 0) return;
            Actor parent;
            try { parent = World.world?.units?.get(pParentId); }
            catch { parent = null; }
            if (parent?.data == null) return;
            parent.data.get(CACHE_INITIALIZED,
                out bool initialized, false);
            parent.data.get(CACHE_VERSION, out int version, 0);
            if (!initialized || version != CurrentCacheVersion) return;
            parent.data.get(LIVING_SONS, out int count, 0);
            Store(parent, count > 0 ? count - 1 : 0);
        }

        private static void Store(Actor pParent, int pCount)
        {
            if (pParent?.data == null) return;
            pParent.data.set(LIVING_SONS,
                pCount < 0 ? 0 : pCount);
            pParent.data.set(CACHE_INITIALIZED, true);
            pParent.data.set(CACHE_VERSION, CurrentCacheVersion);
        }
    }
}
