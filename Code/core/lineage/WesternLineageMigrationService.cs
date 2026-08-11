using System;
using System.Collections;
using System.Collections.Generic;
using AncientWarfare3.core.db;
using AncientWarfare3.core.naming;

namespace AncientWarfare3.core.lineage
{
    internal static class WesternLineageMigrationService
    {
        private const int DefaultBudget = 8;
        private static bool _pending;
        private static readonly Queue<long> PendingKingdomIds =
            new Queue<long>();
        private static readonly HashSet<long> QueuedKingdomIds =
            new HashSet<long>();
        private static IEnumerator _kingdoms;
        private static IEnumerator _actors;
        private static Kingdom _kingdom;
        private static long _kingId = -1L;
        private static long _heirId = -1L;

        internal static void Request()
        {
            DisposeEnumerators();
            PendingKingdomIds.Clear();
            QueuedKingdomIds.Clear();
            _pending = true;
        }

        internal static void Request(Kingdom pKingdom)
        {
            long kingdomId = pKingdom?.data?.id ?? -1L;
            if (kingdomId < 0L || !QueuedKingdomIds.Add(kingdomId)) return;
            PendingKingdomIds.Enqueue(kingdomId);
        }

        internal static void Reset()
        {
            DisposeEnumerators();
            PendingKingdomIds.Clear();
            QueuedKingdomIds.Clear();
            _pending = false;
        }

        internal static void ProcessAuthorityCycle()
        {
            ProcessAuthorityCycle(DefaultBudget);
        }

        internal static void ProcessAuthorityCycle(int pBudget)
        {
            if (pBudget <= 0 || World.world == null ||
                !_pending && _actors == null &&
                PendingKingdomIds.Count == 0) return;
            if (!WesternLineageAdmissionRules.ShouldProcessMigration(
                    LineageArchiveManager.Instance.IsOperational)) return;
            int remaining = pBudget;
            while (remaining > 0)
            {
                if (_actors != null)
                {
                    if (!MoveNext(_actors, out object actorObject))
                    {
                        DisposeActors();
                        continue;
                    }
                    ProcessActor(actorObject as Actor);
                    remaining--;
                    continue;
                }

                if (TryBeginPendingKingdom()) continue;
                if (!_pending) return;
                if (_kingdoms == null)
                    _kingdoms = World.world.kingdoms?.GetEnumerator();
                if (_kingdoms == null ||
                    !MoveNext(_kingdoms, out object kingdomObject))
                {
                    DisposeEnumerators();
                    _pending = false;
                    return;
                }
                BeginKingdom(kingdomObject as Kingdom);
            }
        }

        private static bool TryBeginPendingKingdom()
        {
            int available = PendingKingdomIds.Count;
            while (available-- > 0 && PendingKingdomIds.Count > 0)
            {
                long kingdomId = PendingKingdomIds.Dequeue();
                QueuedKingdomIds.Remove(kingdomId);
                Kingdom kingdom = null;
                try { kingdom = World.world?.kingdoms?.get(kingdomId); }
                catch { }
                BeginKingdom(kingdom);
                if (_actors != null) return true;
            }
            return false;
        }

        private static void BeginKingdom(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                pKingdom.isNeutral()) return;
            AWCultureNamingTradition naming =
                AWCultureNamingTraditionService.ResolveForCulture(
                    pKingdom.culture);
            if (naming.Profile != NamingProfileId.Western &&
                naming.Profile != NamingProfileId.OrcNomadic &&
                naming.Profile != NamingProfileId.NativeSinitic) return;
            if (naming.Profile == NamingProfileId.Western &&
                WesternLineageAdmissionRules.ShouldDeferKingdomMigration(
                    pKingdom.king?.data != null,
                    pKingdom.king?.city?.data != null))
            {
                Request(pKingdom);
                return;
            }

            _kingdom = pKingdom;
            _kingId = pKingdom.king?.data?.id ?? -1L;
            _heirId = HeirService.PeekRegisteredHeir(pKingdom)?.data?.id ??
                      -1L;
            IEnumerable units = pKingdom.getUnits();
            _actors = units?.GetEnumerator();
        }

        private static void ProcessActor(Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt() ||
                pActor.kingdom != _kingdom) return;
            NamingProfileId profile = AWCultureNamingTraditionService.ResolveForActorReadOnly(pActor).Profile;
            if (profile == NamingProfileId.NativeSinitic)
            {
                LineageService.RepairNativeSiniticIdentity(pActor);
                return;
            }
            if (profile != NamingProfileId.Western &&
                profile != NamingProfileId.OrcNomadic) return;

            bool ruler = pActor.data.id == _kingId;
            bool heir = pActor.data.id == _heirId;
            bool cityLeader = pActor.city?.leader == pActor;
            bool noble = cityLeader;
            try { noble |= pActor.hasTrait(LineageKeys.TRAIT_GUIZU); }
            catch { }
            pActor.data.get(LineageKeys.LINEAGE_STATUS,
                out string status, LineageStatus.NONE);
            noble |= string.Equals(status, LineageStatus.NOBLE,
                StringComparison.Ordinal);
            pActor.data.get(LineageKeys.COURT_OFFICE_ID,
                out string officeId, string.Empty);
            bool official = !string.IsNullOrWhiteSpace(officeId);
            if (!ruler && !heir && !noble && !official) return;

            bool admitted = WesternLineageAdmissionService.TryEnsure(
                pActor, ruler, heir,
                pNoble: ruler || heir || noble || official,
                pOfficial: official, pSourceType: "old_save_migration");
            if (admitted && ruler)
                LineageService.SyncExistingChildrenAfterLineageChange(
                    pActor);
        }

        private static bool MoveNext(IEnumerator pEnumerator,
            out object pCurrent)
        {
            pCurrent = null;
            try
            {
                if (!pEnumerator.MoveNext()) return false;
                pCurrent = pEnumerator.Current;
                return true;
            }
            catch { return false; }
        }

        private static void DisposeActors()
        {
            if (_actors is IDisposable disposable) disposable.Dispose();
            _actors = null;
            _kingdom = null;
            _kingId = -1L;
            _heirId = -1L;
        }

        private static void DisposeEnumerators()
        {
            DisposeActors();
            if (_kingdoms is IDisposable disposable) disposable.Dispose();
            _kingdoms = null;
        }
    }
}
