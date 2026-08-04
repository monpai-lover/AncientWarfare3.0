using System;
using System.Collections;
using System.Collections.Generic;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.naming
{
    internal static class AWLocalizedNameMigrationService
    {
        private static readonly object Gate = new object();
        private static bool _pending;
        private static int _phase;
        private static IEnumerator _enumerator;
        private static IAWLocalizedNameMigrationReadiness _readiness =
            new RuntimeReadiness();
        private static readonly AWBoundedLocalizedNameWriteQueue PendingWrites =
            new AWBoundedLocalizedNameWriteQueue(
                AWLocalizedNameMigrationLimits.PendingWriteCapacity);

        internal static bool Enqueue(string pMetaType, long pObjectId,
            BaseSystemData pData)
        {
            if (pData == null || pObjectId < 0) return false;
            AWLocalizedNameIdentitySnapshot snapshot =
                AWLocalizedNamePersistence.Capture(pData);
            lock (Gate)
            {
                bool accepted = PendingWrites.Enqueue(pMetaType, pObjectId,
                    snapshot);
                if (!accepted && PendingWrites.FullRescanRequired)
                {
                    DisposeEnumerator();
                    _phase = 0;
                    _pending = true;
                }
                return accepted;
            }
        }

        internal static void Request()
        {
            lock (Gate)
            {
                DisposeEnumerator();
                _phase = 0;
                _pending = true;
            }
        }

        internal static void Reset()
        {
            lock (Gate)
            {
                DisposeEnumerator();
                _phase = 0;
                _pending = false;
                PendingWrites.Clear();
            }
        }

        internal static void RebuildVisibleProjections()
        {
            AWLocalizedNameRefreshService.Request();
            Request();
        }

        internal static void ProcessAuthorityCycle()
        {
            ProcessAuthorityCycle(
                AWLocalizedNameMigrationLimits.DefaultBatchSize);
        }

        internal static void ProcessAuthorityCycle(int pBudget)
        {
            if (pBudget <= 0 || World.world == null) return;
            lock (Gate)
            {
                int remaining = pBudget;
                while (remaining > 0)
                {
                    if (PendingWrites.Count > 0)
                    {
                        int completed = PendingWrites.Flush(remaining,
                            pending => AWLocalizedNamePersistence.Upsert(
                                pending.MetaType, pending.ObjectId,
                                pending.Snapshot));
                        remaining -= completed;
                        if (PendingWrites.Count > 0) return;
                        continue;
                    }
                    if (!_pending) return;
                    if (_enumerator == null)
                    {
                        IEnumerable collection = ResolveCollection(_phase);
                        if (collection == null)
                        {
                            AdvancePhase();
                            continue;
                        }
                        _enumerator = collection.GetEnumerator();
                    }

                    bool hasNext;
                    try { hasNext = _enumerator.MoveNext(); }
                    catch
                    {
                        AdvancePhase();
                        continue;
                    }
                    if (!hasNext)
                    {
                        AdvancePhase();
                        continue;
                    }

                    Migrate(_enumerator.Current);
                    remaining--;
                }
            }
        }

        private static IEnumerable ResolveCollection(int pPhase)
        {
            return pPhase switch
            {
                0 => World.world.units?.units_only_alive,
                1 => World.world.cities,
                2 => World.world.kingdoms,
                3 => World.world.clans,
                4 => World.world.cultures,
                5 => World.world.languages,
                6 => World.world.religions,
                7 => World.world.subspecies,
                8 => World.world.alliances,
                9 => World.world.wars,
                10 => World.world.books,
                11 => World.world.items,
                _ => null
            };
        }

        private static void Migrate(object pObject)
        {
            if (!TryResolve(pObject, out string metaType, out long objectId,
                    out BaseSystemData data))
                return;

            if (pObject is Kingdom restoringKingdom)
                StateNameService.ReconcileLocalizedIdentityBeforeRestore(
                    restoringKingdom);

            AWLocalizedNameIdentitySnapshot savedIdentity =
                AWLocalizedNamePersistence.Capture(data);
            string displayBefore = data.name ?? string.Empty;
            bool hasStoredIdentity = AWLocalizedNamePersistence.TryLoad(
                metaType, objectId,
                out AWLocalizedNameIdentitySnapshot databaseIdentity);
            AWLocalizedNameIdentitySnapshot identity = hasStoredIdentity
                ? AWLocalizedNameRestoreRules.Merge(savedIdentity,
                    databaseIdentity,
                    AWLocalizedNameMigrationRules.CurrentSchemaVersion)
                : savedIdentity;
            bool databaseStale = hasStoredIdentity &&
                !AWLocalizedNameRestoreRules.Same(identity,
                    databaseIdentity);
            AWLocalizedNameMigrationDecision decision =
                AWLocalizedNameMigrationRules.Resolve(data.name,
                    identity.NativeName, identity.ChineseName,
                    identity.SchemaVersion,
                    AWLocalizedNameLegacySource.Unknown,
                    AWLocalizedNameService.CurrentLanguage());
            if (decision.DeferredForEvidence) return;
            identity = identity.WithNamesAndSchema(decision.NativeName,
                decision.ChineseName, decision.SchemaVersion);
            AWLocalizedNamePersistence.Apply(data, identity);

            bool generated = false;
            if (decision.NeedsChineseGeneration ||
                decision.NeedsNativeGeneration)
            {
                AWLocalizedNameGenerationAdmission admission =
                    AWLocalizedNameMigrationAdmissionRules.Resolve(
                        identity.GeneratorId, metaType, objectId,
                        identity.CultureId, _readiness);
                if (admission.IsAdmitted)
                    generated = GenerateMissingComponent(data, identity,
                        objectId, decision.NeedsChineseGeneration);
            }

            if (generated)
                AWLocalizedNameService.ProjectStored(data);

            if (pObject is Kingdom kingdom)
                AWLocalizedKingdomNameService.ProjectStored(kingdom,
                    displayBefore);

            if (!hasStoredIdentity || databaseStale ||
                decision.NeedsPersistence || generated)
                Enqueue(metaType, objectId, data);
        }

        private static bool GenerateMissingComponent(BaseSystemData pData,
            AWLocalizedNameIdentitySnapshot pIdentity, long pObjectId,
            bool pChineseComponent)
        {
            if (pData.custom_name && pChineseComponent &&
                !string.IsNullOrWhiteSpace(pIdentity.NativeName))
            {
                pData.set(AWNameDataKeys.ChineseName, pIdentity.NativeName);
                return true;
            }

            return AWLocalizedNameService.TryGenerateIdentityComponent(pData,
                pIdentity.GeneratorId, pObjectId, pIdentity.CultureId,
                pChineseComponent);
        }

        private static bool TryResolve(object pObject, out string pMetaType,
            out long pObjectId, out BaseSystemData pData)
        {
            pMetaType = string.Empty;
            pObjectId = -1;
            pData = null;
            switch (pObject)
            {
                case Actor actor:
                    pMetaType = "Unit"; pObjectId = actor.getID();
                    pData = actor.data; break;
                case City city:
                    pMetaType = "City"; pObjectId = city.getID();
                    pData = city.data; break;
                case Kingdom kingdom:
                    pMetaType = "Kingdom"; pObjectId = kingdom.getID();
                    pData = kingdom.data; break;
                case Clan clan:
                    pMetaType = "Clan"; pObjectId = clan.getID();
                    pData = clan.data; break;
                case Culture culture:
                    pMetaType = "Culture"; pObjectId = culture.getID();
                    pData = culture.data; break;
                case Language language:
                    pMetaType = "Language"; pObjectId = language.getID();
                    pData = language.data; break;
                case Religion religion:
                    pMetaType = "Religion"; pObjectId = religion.getID();
                    pData = religion.data; break;
                case Subspecies subspecies:
                    pMetaType = "Subspecies"; pObjectId = subspecies.getID();
                    pData = subspecies.data; break;
                case Alliance alliance:
                    pMetaType = "Alliance"; pObjectId = alliance.getID();
                    pData = alliance.data; break;
                case War war:
                    pMetaType = "War"; pObjectId = war.getID();
                    pData = war.data; break;
                case Book book:
                    pMetaType = "Book"; pObjectId = book.getID();
                    pData = book.data; break;
                case Item item:
                    pMetaType = "Item"; pObjectId = item.getID();
                    pData = item.data; break;
            }
            return pData != null && pObjectId >= 0;
        }

        private static void AdvancePhase()
        {
            DisposeEnumerator();
            _phase++;
            if (_phase > 11)
            {
                _phase = 0;
                _pending = false;
                PendingWrites.ClearFullRescanRequired();
            }
        }

        private static void DisposeEnumerator()
        {
            if (_enumerator is IDisposable disposable) disposable.Dispose();
            _enumerator = null;
        }

        private sealed class RuntimeReadiness :
            IAWLocalizedNameMigrationReadiness
        {
            public bool IsGeneratorAvailable(string pGeneratorId)
            {
                return AWNameGeneratorLibrary.Get(pGeneratorId) != null;
            }

            public bool IsPersistedTraditionProfileReady(string pMetaType,
                long pObjectId, long pCultureId, string pGeneratorId)
            {
                if (AWLocalizedNameProfileReadinessRules.IsReady(
                        pGeneratorId, string.Empty))
                    return true;

                long cultureId = pCultureId;
                if (cultureId < 0L && string.Equals(pMetaType, "Culture",
                        StringComparison.Ordinal))
                    cultureId = pObjectId;
                Culture culture = null;
                try { culture = World.world?.cultures?.get(cultureId); }
                catch { }
                if (culture?.data == null) return false;

                AWCultureNamingTraditionService.Ensure(culture);
                culture.data.get(LineageKeys.NAMING_PROFILE,
                    out string persistedProfile, string.Empty);
                return AWLocalizedNameProfileReadinessRules.IsReady(
                    pGeneratorId, persistedProfile);
            }
        }
    }
}
