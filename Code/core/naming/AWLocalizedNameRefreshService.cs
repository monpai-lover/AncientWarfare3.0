using System;
using System.Collections;

namespace AncientWarfare3.core.naming
{
    internal static class AWLocalizedNameRefreshService
    {
        private const int DefaultBudget = 64;
        private static readonly object Gate = new object();
        private static bool _pending;
        private static int _phase;
        private static IEnumerator _enumerator;

        internal static void Request()
        {
            lock (Gate)
            {
                DisposeEnumerator();
                _phase = 0;
                _pending = true;
            }
        }

        internal static void ProcessFrame()
        {
            ProcessFrame(DefaultBudget);
        }

        internal static void ProcessFrame(int pBudget)
        {
            if (pBudget <= 0 || World.world == null) return;
            lock (Gate)
            {
                if (!_pending) return;
                int remaining = pBudget;
                while (remaining > 0 && _pending)
                {
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

                    ProjectObject(_enumerator.Current);
                    remaining--;
                }
            }
        }

        internal static void Clear()
        {
            lock (Gate)
            {
                DisposeEnumerator();
                _phase = 0;
                _pending = false;
            }
            AWLocalizedNameService.ClearRuntime();
        }

        private static IEnumerable ResolveCollection(int pPhase)
        {
            return pPhase switch
            {
                0 => World.world.cities,
                1 => World.world.kingdoms,
                2 => World.world.clans,
                3 => World.world.cultures,
                4 => World.world.languages,
                5 => World.world.religions,
                6 => World.world.subspecies,
                7 => World.world.alliances,
                8 => World.world.wars,
                9 => World.world.books,
                10 => World.world.items,
                _ => null
            };
        }

        private static void ProjectObject(object pObject)
        {
            switch (pObject)
            {
                case Kingdom kingdom:
                    AWLocalizedMottoService.ProjectKingdom(kingdom,
                        kingdom.data?.motto);
                    break;
                case Clan clan:
                    AWLocalizedMottoService.ProjectClan(clan,
                        clan.data?.motto);
                    break;
                case Alliance alliance:
                    AWLocalizedMottoService.ProjectAlliance(alliance,
                        alliance.data?.motto);
                    break;
            }

            BaseSystemData data = pObject switch
            {
                City city => city.data,
                Kingdom kingdom => kingdom.data,
                Clan clan => clan.data,
                Culture culture => culture.data,
                Language language => language.data,
                Religion religion => religion.data,
                Subspecies subspecies => subspecies.data,
                Alliance alliance => alliance.data,
                War war => war.data,
                Book book => book.data,
                Item item => item.data,
                _ => null
            };
            if (data != null) AWLocalizedNameService.ProjectStored(data);
        }

        private static void AdvancePhase()
        {
            DisposeEnumerator();
            _phase++;
            if (_phase > 10)
            {
                _phase = 0;
                _pending = false;
            }
        }

        private static void DisposeEnumerator()
        {
            if (_enumerator is IDisposable disposable) disposable.Dispose();
            _enumerator = null;
        }
    }
}
