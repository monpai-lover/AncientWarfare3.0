using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using AncientWarfare3.patch;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.lineage
{
    internal static class ActorKingdomSafetyService
    {
        private const int DefaultDrainBudget = 32;
        private static readonly ConcurrentQueue<Actor> PendingRepairs =
            new ConcurrentQueue<Actor>();
        private static readonly ConcurrentDictionary<long, byte> QueuedIds =
            new ConcurrentDictionary<long, byte>();
        private static readonly ConcurrentDictionary<long, string> LastFailures =
            new ConcurrentDictionary<long, string>();
        private static readonly HashSet<long> ReportedFailures =
            new HashSet<long>();

        internal static int PendingRepairCount => QueuedIds.Count;

        public static void QueueRepair(Actor pActor)
        {
            long id = ActorId(pActor);
            if (id < 0L || !QueuedIds.TryAdd(id, 0)) return;
            PendingRepairs.Enqueue(pActor);
        }

        /// <summary>
        /// Clears an actor's kingdom pointer for a native affiliation
        /// transfer, while ensuring a failed transfer is repaired before the
        /// actor reaches kingdom-dependent vanilla systems.
        /// </summary>
        public static void DetachForTransfer(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.kingdom = null;
            QueueRepair(pActor);
        }

        public static bool RepairLoadedActor(Actor pActor)
        {
            return TryRepairLoadedActor(pActor);
        }

        private static bool TryRepairLoadedActor(Actor pActor)
        {
            long actorId = ActorId(pActor);
            ActorKingdomRepairSource source = ActorKingdomRepairSource.None;
            try
            {
                if (!ActorKingdomSafetyRules.ShouldRepairActor(
                        pActor?.data != null, pActor?.isAlive() == true,
                        pActor == null || pActor.isRekt()))
                    return true;
                City city = pActor?.city ?? ResolvePersistedCity(pActor);
                Kingdom cityKingdom = city?.kingdom;

                // Actor.loadFromSave can skip setCity when City.isNeutral()
                // sees an unresolved kingdom.  Once the city relation is
                // valid, restore that persisted membership before choosing a
                // fallback kingdom for the actor.
                RestorePersistedCityAssociation(pActor, city, cityKingdom);
                city = pActor?.city ?? city;
                cityKingdom = city?.kingdom;
                bool cityKingdomIsRekt = city?.data == null ||
                                          city.isRekt() ||
                                          cityKingdom?.data == null ||
                                          cityKingdom.asset == null ||
                                          cityKingdom.isRekt();
                source =
                    ActorKingdomSafetyRules.SelectRepairSource(
                        actorExists: pActor?.data != null,
                        actorAssetExists: pActor?.asset != null,
                        kingdomDataExists: pActor?.kingdom?.data != null,
                        kingdomAssetExists: pActor?.kingdom?.asset != null,
                        cityKingdomAssetExists:
                            cityKingdom?.data != null &&
                            cityKingdom.asset != null,
                        cityKingdomIsRekt: cityKingdomIsRekt,
                        wildKingdomIdExists: !string.IsNullOrEmpty(
                            pActor?.asset?.kingdom_id_wild));
                Kingdom target = null;
                switch (source)
                {
                    case ActorKingdomRepairSource.City:
                        target = cityKingdom;
                        break;
                    case ActorKingdomRepairSource.Wild:
                        target = AW_WildKingdomPatch.
                            EnsureWildKingdom(pActor.asset);
                        break;
                }

                if (target != null)
                    AttachRepairTarget(pActor, target, city);

                bool repaired = pActor?.kingdom?.data != null &&
                                pActor.kingdom.asset != null;
                if (repaired)
                {
                    if (actorId >= 0L) LastFailures.TryRemove(actorId, out _);
                    return true;
                }

                RememberFailure(actorId, "source=" + source +
                    " target=" + DescribeKingdom(target) +
                    " current=" + DescribeKingdom(pActor?.kingdom));
                return false;
            }
            catch (Exception error)
            {
                if (pActor?.kingdom?.data != null &&
                    pActor.kingdom.asset != null)
                {
                    if (actorId >= 0L) LastFailures.TryRemove(actorId, out _);
                    return true;
                }
                RememberFailure(actorId, "source=" + source + " " + error);
                return false;
            }
        }

        private static void AttachRepairTarget(Actor pActor, Kingdom pTarget,
            City pCity)
        {
            if (pActor?.data == null || pTarget?.data == null ||
                pTarget.asset == null) return;
            if (ActorKingdomSafetyRules.ShouldDetachInvalidKingdomBeforeRepair(
                    pActor.kingdom != null,
                    pActor.kingdom?.data != null,
                    pActor.kingdom?.asset != null))
            {
                // Vanilla joinKingdom calls isCiv() on the old kingdom first.
                pActor.kingdom = null;
            }

            using (FormalAffiliationTransferScope.Open(
                       pActor.data.id, pTarget.id,
                       pCity?.data?.id ?? -1L))
            {
                pActor.joinKingdom(pTarget);
            }
        }

        private static City ResolvePersistedCity(Actor pActor)
        {
            try
            {
                long cityId = pActor?.data?.cityID ?? -1L;
                return cityId >= 0L
                    ? World.world?.cities?.get(cityId)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static void RestorePersistedCityAssociation(Actor pActor,
            City pCity, Kingdom pCityKingdom)
        {
            if (pActor?.city != null || pCity == null) return;

            bool canRestore = ActorLoadCitySafetyRules.
                CanRestorePersistedCity(
                    cityExists: true,
                    cityKingdomExists: pCityKingdom != null,
                    cityKingdomAssetExists: pCityKingdom?.asset != null,
                    actorKingdomExists: pActor.kingdom != null,
                    sameKingdom: pActor.kingdom == pCityKingdom);
            if (!canRestore) return;

            try
            {
                pActor.setCity(pCity);
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Persisted actor city association restore failed: " +
                    error.Message);
            }
        }

        public static void DrainPendingRepairs(
            int pBudget = DefaultDrainBudget)
        {
            for (int i = 0; i < pBudget &&
                            PendingRepairs.TryDequeue(out Actor actor); i++)
            {
                long id = ActorId(actor);
                if (id >= 0L) QueuedIds.TryRemove(id, out _);
                if (RepairLoadedActor(actor) || id < 0L ||
                    !ReportedFailures.Add(id)) continue;
                ModClass.LogWarning(
                    "Actor kingdom repair failed; enemy checks quarantined: " +
                    DescribeActor(actor) + " failure=" + DescribeFailure(id));
            }
        }

        private static void RememberFailure(long pActorId, string pFailure)
        {
            if (pActorId < 0L) return;
            LastFailures[pActorId] = string.IsNullOrEmpty(pFailure)
                ? "unknown"
                : pFailure;
        }

        private static string DescribeFailure(long pActorId)
        {
            return pActorId >= 0L && LastFailures.TryGetValue(pActorId,
                out string failure)
                ? failure
                : "unknown";
        }

        private static string DescribeKingdom(Kingdom pKingdom)
        {
            try
            {
                return pKingdom == null
                    ? "<null>"
                    : pKingdom.id + ":" + (pKingdom.asset?.id ?? "<no-asset>");
            }
            catch
            {
                return "<invalid>";
            }
        }

        private static string DescribeActor(Actor pActor)
        {
            try
            {
                return "actor=" + ActorId(pActor) + " asset=" +
                       (pActor?.asset?.id ?? "<null>") + " wild=" +
                       (pActor?.asset?.kingdom_id_wild ?? "<null>") +
                       " city=" + (pActor?.city?.id ?? -1L);
            }
            catch
            {
                return "actor=" + ActorId(pActor) +
                       " <invalid runtime references>";
            }
        }

        public static void ClearRuntime()
        {
            while (PendingRepairs.TryDequeue(out _)) { }
            QueuedIds.Clear();
            LastFailures.Clear();
            ReportedFailures.Clear();
        }

        private static long ActorId(Actor pActor)
        {
            try { return pActor?.data?.id ?? -1L; }
            catch { return -1L; }
        }
    }
}
