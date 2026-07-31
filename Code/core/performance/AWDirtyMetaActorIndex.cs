using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AncientWarfare3.core.performance
{
    internal static class AWDirtyMetaActorIndex
    {
        private const int SubspeciesIndex = 0;
        private const int FamilyIndex = 1;
        private const int ArmyIndex = 2;
        private const int LanguageIndex = 3;
        private const int ReligionIndex = 4;
        private const int CityIndex = 5;
        private const int ClanIndex = 6;
        private const int KingdomIndex = 7;
        private const int WildKingdomIndex = 8;
        private const int CultureIndex = 9;
        private const int PlotIndex = 10;
        private const int KindCount = 11;

        private const int SubspeciesBit = 1 << SubspeciesIndex;
        private const int FamilyBit = 1 << FamilyIndex;
        private const int ArmyBit = 1 << ArmyIndex;
        private const int LanguageBit = 1 << LanguageIndex;
        private const int ReligionBit = 1 << ReligionIndex;
        private const int CityBit = 1 << CityIndex;
        private const int ClanBit = 1 << ClanIndex;
        private const int KingdomBit = 1 << KingdomIndex;
        private const int WildKingdomBit = 1 << WildKingdomIndex;
        private const int CultureBit = 1 << CultureIndex;
        private const int PlotBit = 1 << PlotIndex;

        private static readonly Action<int> ClassifyWorkItemAction =
            ClassifyWorkItem;
        private static readonly ParallelOptions ParallelOptions =
            new ParallelOptions
            {
                MaxDegreeOfParallelism =
                    AWPerformanceSettings.ForegroundParallelism
            };
        private static readonly object[] Managers = new object[KindCount];
        private static readonly Actor[][] ActorBuffers =
            new Actor[KindCount][];
        private static readonly int[] ActorCounts = new int[KindCount];

        private static Actor[] _aliveSource;
        private static Actor[] _dyingSource;
        private static int[] _partitionCounts = Array.Empty<int>();
        private static int[] _partitionFlags = Array.Empty<int>();
        private static int _aliveSourceCount;
        private static int _dyingSourceCount;
        private static int _workItemCount;
        private static int _preparingMask;
        private static int _activeMask;
        private static int _kingdomHasBoatsMask;

        static AWDirtyMetaActorIndex()
        {
            for (int i = 0; i < KindCount; i++)
                ActorBuffers[i] = Array.Empty<Actor>();
        }

        internal static void Prepare(
            IReadOnlyList<BaseSystemManager> pManagers,
            Actor[] pAliveActors,
            int pAliveCount,
            Actor[] pDyingActors,
            int pDyingCount)
        {
            End();
            int enabledMask = 0;
            for (int i = 0; i < pManagers.Count; i++)
            {
                BaseSystemManager manager = pManagers[i];
                if (!manager.isUnitsDirty()) continue;

                switch (manager)
                {
                    case SubspeciesManager:
                        Enable(SubspeciesIndex, manager, ref enabledMask);
                        break;
                    case FamilyManager:
                        Enable(FamilyIndex, manager, ref enabledMask);
                        break;
                    case ArmyManager:
                        Enable(ArmyIndex, manager, ref enabledMask);
                        break;
                    case LanguageManager:
                        Enable(LanguageIndex, manager, ref enabledMask);
                        break;
                    case ReligionManager:
                        Enable(ReligionIndex, manager, ref enabledMask);
                        break;
                    case CityManager:
                        Enable(CityIndex, manager, ref enabledMask);
                        break;
                    case ClanManager:
                        Enable(ClanIndex, manager, ref enabledMask);
                        break;
                    case KingdomManager:
                        Enable(KingdomIndex, manager, ref enabledMask);
                        break;
                    case WildKingdomsManager:
                        Enable(WildKingdomIndex, manager, ref enabledMask);
                        break;
                    case CultureManager:
                        Enable(CultureIndex, manager, ref enabledMask);
                        break;
                    case PlotManager:
                        Enable(PlotIndex, manager, ref enabledMask);
                        break;
                }
            }

            if (enabledMask == 0) return;

            _aliveSource = pAliveActors;
            _aliveSourceCount = pAliveCount;
            _dyingSource = pDyingActors;
            _dyingSourceCount = pDyingCount;
            _preparingMask = enabledMask;

            int batchSize = AWPerformanceSettings.SimulationBatchSize;
            _workItemCount = (pAliveCount + batchSize - 1) / batchSize;
            EnsurePartitionCapacity(_workItemCount * KindCount);
            EnsurePartitionFlagCapacity(_workItemCount);
            for (int kind = 0; kind < KindCount; kind++)
            {
                if ((enabledMask & (1 << kind)) != 0)
                    EnsureActorBufferCapacity(kind, pAliveCount);
            }

            if (_workItemCount > 1)
            {
                Parallel.For(0, _workItemCount, ParallelOptions,
                    ClassifyWorkItemAction);
            }
            else if (_workItemCount == 1)
            {
                ClassifyWorkItem(0);
            }

            for (int kind = 0; kind < KindCount; kind++)
            {
                if ((enabledMask & (1 << kind)) == 0) continue;

                Actor[] buffer = ActorBuffers[kind];
                int totalCount = 0;
                for (int workIndex = 0;
                     workIndex < _workItemCount;
                     workIndex++)
                {
                    int count = _partitionCounts[
                        workIndex * KindCount + kind];
                    if (count == 0) continue;

                    int sourceIndex = workIndex * batchSize;
                    if (sourceIndex != totalCount)
                    {
                        Array.Copy(buffer, sourceIndex, buffer,
                            totalCount, count);
                    }

                    totalCount += count;
                }

                ActorCounts[kind] = totalCount;
            }

            int actorFlags = 0;
            for (int i = 0; i < _workItemCount; i++)
                actorFlags |= _partitionFlags[i];

            _kingdomHasBoatsMask = actorFlags;
            _aliveSource = null;
            _aliveSourceCount = 0;
            _workItemCount = 0;
            Volatile.Write(ref _activeMask, enabledMask);
        }

        internal static void End()
        {
            Volatile.Write(ref _activeMask, 0);
            _aliveSource = null;
            _dyingSource = null;
            _aliveSourceCount = 0;
            _dyingSourceCount = 0;
            _workItemCount = 0;
            _preparingMask = 0;
            _kingdomHasBoatsMask = 0;
        }

        internal static void ClearWorldState()
        {
            End();
            for (int kind = 0; kind < KindCount; kind++)
            {
                if (ActorBuffers[kind].Length > 0)
                {
                    Array.Clear(ActorBuffers[kind], 0,
                        ActorBuffers[kind].Length);
                }

                Managers[kind] = null;
                ActorCounts[kind] = 0;
            }
        }

        internal static bool TryApply(SubspeciesManager pManager)
        {
            if (!IsActive(SubspeciesIndex, pManager)) return false;

            Actor[] dying = _dyingSource;
            for (int i = 0; i < _dyingSourceCount; i++)
            {
                Subspecies subspecies = dying[i].subspecies;
                subspecies?.preserveAlive();
            }

            Actor[] actors = ActorBuffers[SubspeciesIndex];
            int count = ActorCounts[SubspeciesIndex];
            for (int i = 0; i < count; i++)
            {
                Actor actor = actors[i];
                actor.subspecies.listUnit(actor);
            }

            return true;
        }

        internal static bool TryApply(FamilyManager pManager)
        {
            if (!IsActive(FamilyIndex, pManager)) return false;

            Actor[] actors = ActorBuffers[FamilyIndex];
            int count = ActorCounts[FamilyIndex];
            for (int i = 0; i < count; i++)
            {
                Actor actor = actors[i];
                actor.family.listUnit(actor);
            }

            return true;
        }

        internal static bool TryApply(ArmyManager pManager)
        {
            if (!IsActive(ArmyIndex, pManager)) return false;

            Actor[] actors = ActorBuffers[ArmyIndex];
            int count = ActorCounts[ArmyIndex];
            for (int i = 0; i < count; i++)
            {
                Actor actor = actors[i];
                actor.army.listUnit(actor);
            }

            return true;
        }

        internal static bool TryApply(LanguageManager pManager)
        {
            if (!IsActive(LanguageIndex, pManager)) return false;

            Actor[] actors = ActorBuffers[LanguageIndex];
            int count = ActorCounts[LanguageIndex];
            for (int i = 0; i < count; i++)
            {
                Actor actor = actors[i];
                actor.language.listUnit(actor);
            }

            return true;
        }

        internal static bool TryApply(ReligionManager pManager)
        {
            if (!IsActive(ReligionIndex, pManager)) return false;

            Actor[] actors = ActorBuffers[ReligionIndex];
            int count = ActorCounts[ReligionIndex];
            for (int i = 0; i < count; i++)
            {
                Actor actor = actors[i];
                actor.religion.listUnit(actor);
            }

            return true;
        }

        internal static bool TryApply(CityManager pManager)
        {
            if (!IsActive(CityIndex, pManager)) return false;

            Actor[] actors = ActorBuffers[CityIndex];
            int count = ActorCounts[CityIndex];
            for (int i = 0; i < count; i++)
            {
                Actor actor = actors[i];
                actor.city.listUnit(actor);
            }

            return true;
        }

        internal static bool TryApply(ClanManager pManager)
        {
            if (!IsActive(ClanIndex, pManager)) return false;

            Actor[] actors = ActorBuffers[ClanIndex];
            int count = ActorCounts[ClanIndex];
            for (int i = 0; i < count; i++)
            {
                Actor actor = actors[i];
                actor.clan.listUnit(actor);
            }

            return true;
        }

        internal static bool TryApply(KingdomManager pManager)
        {
            if (!IsActive(KingdomIndex, pManager)) return false;

            Actor[] dying = _dyingSource;
            for (int i = 0; i < _dyingSourceCount; i++)
                dying[i].kingdom.preserveAlive();

            Actor[] actors = ActorBuffers[KingdomIndex];
            int count = ActorCounts[KingdomIndex];
            if ((_kingdomHasBoatsMask & KingdomBit) == 0 &&
                TryGetSingleDirtyKingdom(pManager,
                    out Kingdom soleKingdom))
            {
                AddActorRange(soleKingdom.units, actors, count);
                return true;
            }

            AppendKingdomUnits(actors, count);
            return true;
        }

        internal static bool TryApply(WildKingdomsManager pManager)
        {
            if (!IsActive(WildKingdomIndex, pManager)) return false;

            Actor[] actors = ActorBuffers[WildKingdomIndex];
            int count = ActorCounts[WildKingdomIndex];
            if ((_kingdomHasBoatsMask & WildKingdomBit) == 0 &&
                TryGetSingleDirtyKingdom(pManager,
                    out Kingdom soleKingdom))
            {
                AddActorRange(soleKingdom.units, actors, count);
                return true;
            }

            AppendKingdomUnits(actors, count);
            return true;
        }

        internal static bool TryApply(CultureManager pManager)
        {
            if (!IsActive(CultureIndex, pManager)) return false;

            Actor[] actors = ActorBuffers[CultureIndex];
            int count = ActorCounts[CultureIndex];
            for (int i = 0; i < count; i++)
            {
                Actor actor = actors[i];
                actor.culture.listUnit(actor);
            }

            return true;
        }

        internal static bool TryApply(PlotManager pManager)
        {
            if (!IsActive(PlotIndex, pManager)) return false;

            Actor[] actors = ActorBuffers[PlotIndex];
            int count = ActorCounts[PlotIndex];
            for (int i = 0; i < count; i++)
            {
                Actor actor = actors[i];
                actor.plot.listUnit(actor);
            }

            using (IEnumerator<Plot> enumerator = pManager.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    Plot plot = enumerator.Current;
                    if (plot.isActive() &&
                        plot.isDirtyUnits() &&
                        plot.units.Count == 0)
                        pManager.cancelPlot(plot);
                }
            }

            return true;
        }

        private static void Enable(
            int pKind,
            object pManager,
            ref int pMask)
        {
            Managers[pKind] = pManager;
            pMask |= 1 << pKind;
        }

        private static bool IsActive(int pKind, object pManager)
        {
            int mask = Volatile.Read(ref _activeMask);
            return (mask & (1 << pKind)) != 0 &&
                   ReferenceEquals(Managers[pKind], pManager);
        }

        private static void AppendKingdomUnits(Actor[] pActors, int pCount)
        {
            for (int i = 0; i < pCount; i++)
            {
                Actor pActor = pActors[i];
                if (pActor.asset.is_boat)
                {
                    pActor.kingdom.listUnit(pActor);
                    continue;
                }

                pActor.kingdom.units.Add(pActor);
            }
        }

        private static bool TryGetSingleDirtyKingdom(
            IEnumerable<Kingdom> pKingdoms,
            out Kingdom pResult)
        {
            pResult = null;
            foreach (Kingdom kingdom in pKingdoms)
            {
                if (!kingdom.isDirtyUnits()) continue;
                if (pResult != null)
                {
                    pResult = null;
                    return false;
                }

                pResult = kingdom;
            }

            return pResult != null;
        }

        private static void AddActorRange(
            List<Actor> pTarget,
            Actor[] pSource,
            int pCount)
        {
            if (pCount == 0) return;
            pTarget.AddRange(new ArraySegment<Actor>(pSource, 0, pCount));
        }

        private static void ClassifyWorkItem(int pWorkIndex)
        {
            int slot = pWorkIndex * KindCount;
            for (int kind = 0; kind < KindCount; kind++)
                _partitionCounts[slot + kind] = 0;

            int actorFlags = 0;
            int batchSize = AWPerformanceSettings.SimulationBatchSize;
            int start = pWorkIndex * batchSize;
            int end = Math.Min(_aliveSourceCount, start + batchSize);
            int enabledMask = _preparingMask;
            for (int i = start; i < end; i++)
            {
                Actor pActor = _aliveSource[i];
                if ((enabledMask & SubspeciesBit) != 0)
                {
                    Subspecies subspecies = pActor.subspecies;
                    if (subspecies != null && subspecies.isDirtyUnits())
                    {
                        ActorBuffers[SubspeciesIndex][start +
                            _partitionCounts[slot + SubspeciesIndex]++] =
                            pActor;
                    }
                }

                if ((enabledMask & FamilyBit) != 0)
                {
                    Family family = pActor.family;
                    if (family != null && family.isDirtyUnits())
                    {
                        ActorBuffers[FamilyIndex][start +
                            _partitionCounts[slot + FamilyIndex]++] = pActor;
                    }
                }

                if ((enabledMask & ArmyBit) != 0)
                {
                    Army army = pActor.army;
                    if (army != null && army.isDirtyUnits())
                    {
                        ActorBuffers[ArmyIndex][start +
                            _partitionCounts[slot + ArmyIndex]++] = pActor;
                    }
                }

                if ((enabledMask & LanguageBit) != 0)
                {
                    Language language = pActor.language;
                    if (language != null && language.isDirtyUnits())
                    {
                        ActorBuffers[LanguageIndex][start +
                            _partitionCounts[slot + LanguageIndex]++] =
                            pActor;
                    }
                }

                if ((enabledMask & ReligionBit) != 0)
                {
                    Religion religion = pActor.religion;
                    if (religion != null && religion.isDirtyUnits())
                    {
                        ActorBuffers[ReligionIndex][start +
                            _partitionCounts[slot + ReligionIndex]++] =
                            pActor;
                    }
                }

                if ((enabledMask & CityBit) != 0)
                {
                    City city = pActor.city;
                    if (city != null && city.isDirtyUnits())
                    {
                        ActorBuffers[CityIndex][start +
                            _partitionCounts[slot + CityIndex]++] = pActor;
                    }
                }

                if ((enabledMask & ClanBit) != 0)
                {
                    Clan clan = pActor.clan;
                    if (clan != null && clan.isDirtyUnits())
                    {
                        ActorBuffers[ClanIndex][start +
                            _partitionCounts[slot + ClanIndex]++] = pActor;
                    }
                }

                int kingdomMask = enabledMask &
                                  (KingdomBit | WildKingdomBit);
                if (kingdomMask != 0)
                {
                    Kingdom kingdom = pActor.kingdom;
                    if (kingdom != null && kingdom.isDirtyUnits())
                    {
                        int kingdomBit = kingdom.wild
                            ? WildKingdomBit
                            : KingdomBit;
                        if ((kingdomMask & kingdomBit) != 0)
                        {
                            int kingdomIndex = kingdom.wild
                                ? WildKingdomIndex
                                : KingdomIndex;
                            ActorBuffers[kingdomIndex][start +
                                _partitionCounts[slot + kingdomIndex]++] =
                                pActor;
                            if (pActor.asset.is_boat)
                                actorFlags |= kingdomBit;
                        }
                    }
                }

                if ((enabledMask & CultureBit) != 0)
                {
                    Culture culture = pActor.culture;
                    if (culture != null && culture.isDirtyUnits())
                    {
                        ActorBuffers[CultureIndex][start +
                            _partitionCounts[slot + CultureIndex]++] =
                            pActor;
                    }
                }

                if ((enabledMask & PlotBit) != 0)
                {
                    Plot plot = pActor.plot;
                    if (plot != null && plot.isDirtyUnits())
                    {
                        ActorBuffers[PlotIndex][start +
                            _partitionCounts[slot + PlotIndex]++] = pActor;
                    }
                }
            }

            _partitionFlags[pWorkIndex] = actorFlags;
        }

        private static void EnsurePartitionCapacity(int pRequired)
        {
            if (_partitionCounts.Length < pRequired)
                _partitionCounts = new int[pRequired];
        }

        private static void EnsurePartitionFlagCapacity(int pRequired)
        {
            if (_partitionFlags.Length < pRequired)
                _partitionFlags = new int[pRequired];
        }

        private static void EnsureActorBufferCapacity(
            int pKind,
            int pRequired)
        {
            if (ActorBuffers[pKind].Length >= pRequired) return;
            ActorBuffers[pKind] = new Actor[Math.Max(
                AWPerformanceSettings.SimulationBatchSize,
                pRequired)];
        }
    }
}
