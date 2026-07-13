using System;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolFailedActorCleanup
    {
        internal static bool Remove(ActorManager pUnits, Actor pActor,
            string pExpectedActorAssetId)
        {
            if (pUnits == null || pActor?.data == null ||
                pUnits.get(pActor.data.id) != pActor) return false;

            if (!EnsureDestroyableActor(pUnits, pActor, pExpectedActorAssetId))
            {
                RemoveUnbatchedActor(pUnits, pActor);
                return true;
            }

            pActor.setAlive(pValue: false);
            pActor.skipUpdates();
            pUnits.scheduleDestroyOnPlay(pActor);
            return true;
        }

        private static bool EnsureDestroyableActor(ActorManager pUnits, Actor pActor,
            string pExpectedActorAssetId)
        {
            ActorAsset asset = pActor.asset ??
                AssetManager.actor_library.get(pExpectedActorAssetId);
            if (asset == null)
                throw new InvalidOperationException("failed actor asset is unavailable");
            if (pActor.asset == null) pActor.asset = asset;
            if (!asset.units.Contains(pActor)) asset.units.Add(pActor);

            if (pActor.batch != null) return true;
            try
            {
                pUnits._job_manager.addNewObject(pActor);
            }
            catch
            {
                if (pActor.batch == null) return false;
            }
            return pActor.batch != null;
        }

        private static void RemoveUnbatchedActor(ActorManager pUnits, Actor pActor)
        {
            if (pActor.batch != null)
                throw new InvalidOperationException("failed actor unexpectedly has a batch");
            pActor.setAlive(pValue: false);
            pActor.skipUpdates();
            pActor.asset?.units.Remove(pActor);
            pUnits.removeObject(pActor);
        }
    }
}
