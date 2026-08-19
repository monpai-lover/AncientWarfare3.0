using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class SpecialGovernmentWarParticipationService
    {
        private const int ReconcileBatch = 64;
        private const int ParticipationBatch = 64;
        private sealed class CountCache
        {
            public SpecialWarGovernmentKind Kind;
            public int UnitCount;
            public int NextIndex;
            public int Additional;
            public bool Complete;
        }

        private sealed class ParticipationWork
        {
            public long KingdomId;
            public int NextIndex;
            public bool Attach;
        }

        private static readonly Dictionary<long, CountCache> Counts =
            new Dictionary<long, CountCache>();
        private static readonly Dictionary<long, int> ActiveWarCounts =
            new Dictionary<long, int>();
        private static readonly HashSet<long> AttachedActors =
            new HashSet<long>();
        private static readonly Queue<ParticipationWork> ParticipationQueue =
            new Queue<ParticipationWork>();
        private static readonly HashSet<string> ParticipationKeys =
            new HashSet<string>();

        public static void ClearRuntime()
        {
            Counts.Clear();
            ActiveWarCounts.Clear();
            AttachedActors.Clear();
            ParticipationQueue.Clear();
            ParticipationKeys.Clear();
        }

        public static void OnWarStarted(War pWar)
        {
            if (pWar?.data == null || pWar.hasEnded()) return;
            RefreshActiveWarCounts(pWar);
            ScheduleSide(pWar.getAttackers(), pAttach: true);
            ScheduleSide(pWar.getDefenders(), pAttach: true);
        }

        public static void OnWarEnded(War pWar)
        {
            RefreshActiveWarCounts(pWar);
            ScheduleSide(pWar.getAttackers(), pAttach: false);
            ScheduleSide(pWar.getDefenders(), pAttach: false);
        }

        public static void ProcessAuthorityCycle()
        {
            int remaining = ParticipationBatch;
            int workItems = ParticipationQueue.Count;
            while (remaining > 0 && workItems-- > 0 &&
                   ParticipationQueue.Count > 0)
            {
                ParticipationWork work = ParticipationQueue.Dequeue();
                string key = ParticipationKey(work.KingdomId, work.Attach);
                Kingdom kingdom = FindKingdom(work.KingdomId);
                if (kingdom?.data == null || kingdom.units == null)
                {
                    ParticipationKeys.Remove(key);
                    continue;
                }
                int end = Math.Min(kingdom.units.Count,
                    work.NextIndex + remaining);
                for (int i = work.NextIndex; i < end; i++)
                {
                    Actor actor = kingdom.units[i];
                    if (work.Attach) TryAttach(actor);
                    else TryDetach(actor);
                }
                remaining -= Math.Max(0, end - work.NextIndex);
                work.NextIndex = end;
                if (work.NextIndex < kingdom.units.Count)
                    ParticipationQueue.Enqueue(work);
                else
                    ParticipationKeys.Remove(key);
            }
        }

        public static SpecialWarGovernmentKind ResolveKind(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt())
                return SpecialWarGovernmentKind.Ordinary;
            if (PeasantRebelRouteService.IsBandit(pKingdom))
                return SpecialWarGovernmentKind.Bandit;
            if (MandateRebelService.IsRebelKingdom(pKingdom))
                return SpecialWarGovernmentKind.PeasantRebel;
            if (VassalService.GetSubjectKind(pKingdom) ==
                VassalSubjectKind.MilitaryGovernorate)
                return SpecialWarGovernmentKind.MilitaryGovernorate;
            return SpecialWarGovernmentKind.Ordinary;
        }

        public static bool CanParticipateInRts(Actor pActor)
        {
            if (pActor?.data == null || !IsAlive(pActor) ||
                pActor.asset?.is_boat == true) return false;
            Kingdom kingdom = pActor.kingdom;
            SpecialWarGovernmentKind kind = ResolveKind(kingdom);
            if (kind == SpecialWarGovernmentKind.Ordinary ||
                !HasActiveWar(kingdom)) return false;
            if (kind != SpecialWarGovernmentKind.MilitaryGovernorate &&
                !pActor.is_profession_warrior)
                return SpecialGovernmentWarParticipationRules
                    .CanFightAsCivilian(kind, true, true, false);
            bool heir = ReadHeirId(kingdom) == pActor.data.id;
            bool command = pActor.isKing() || heir || pActor.isCityLeader();
            return SpecialGovernmentWarParticipationRules.CanCommand(kind,
                true, true, pActor.isKing(), heir,
                pActor.isCityLeader(), false) && command;
        }

        public static bool IsEligibleAngryInteraction(Actor pFirst,
            Actor pSecond)
        {
            if (pFirst?.data == null || pSecond?.data == null ||
                pFirst.kingdom?.data == null || pSecond.kingdom?.data == null ||
                pFirst.kingdom == pSecond.kingdom ||
                !AreFoes(pFirst.kingdom, pSecond.kingdom)) return false;
            return IsScopedCivilian(pFirst) || IsScopedCivilian(pSecond);
        }

        internal static bool TryAddSpecialGovernmentCombatants(War pWar,
            out int pAttackers, out int pDefenders)
        {
            pAttackers = 0;
            pDefenders = 0;
            if (pWar?.data == null || pWar.hasEnded()) return false;
            if (!TryReadSide(pWar.getAttackers(), out pAttackers) ||
                !TryReadSide(pWar.getDefenders(), out pDefenders)) return false;
            return true;
        }

        public static void OnActorDied(Actor pActor)
        {
            long id = pActor?.kingdom?.data?.id ?? -1L;
            if (id >= 0) Counts.Remove(id);
            if (pActor?.data?.id >= 0) AttachedActors.Remove(pActor.data.id);
        }

        private static void ScheduleSide(IEnumerable<Kingdom> pKingdoms,
            bool pAttach)
        {
            foreach (Kingdom kingdom in pKingdoms)
            {
                if (kingdom?.data == null || kingdom.units == null ||
                    pAttach && ResolveKind(kingdom) ==
                    SpecialWarGovernmentKind.Ordinary) continue;
                string key = ParticipationKey(kingdom.id, pAttach);
                if (!ParticipationKeys.Add(key)) continue;
                ParticipationQueue.Enqueue(new ParticipationWork
                {
                    KingdomId = kingdom.id,
                    Attach = pAttach
                });
            }
        }

        private static void TryAttach(Actor pActor)
        {
            if (pActor?.data == null || !IsAlive(pActor) ||
                !CanParticipateInRts(pActor) || pActor.hasArmy()) return;
            Army army = null;
            try { army = pActor.city?.getArmy(); }
            catch { }
            if (army?.data == null) return;
            AWArmyService.AddToArmy(pActor, army);
            try
            {
                if (pActor.army == army) AttachedActors.Add(pActor.data.id);
            }
            catch { }
        }

        private static void TryDetach(Actor pActor)
        {
            if (pActor?.data == null ||
                !AttachedActors.Contains(pActor.data.id) ||
                CanParticipateInRts(pActor)) return;
            AttachedActors.Remove(pActor.data.id);
            try
            {
                if (pActor.hasArmy()) pActor.removeFromArmy();
            }
            catch { }
        }

        private static string ParticipationKey(long pKingdomId,
            bool pAttach)
        {
            return pKingdomId + (pAttach ? ":attach" : ":detach");
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static bool TryReadSide(IEnumerable<Kingdom> pKingdoms,
            out int pAdditional)
        {
            pAdditional = 0;
            foreach (Kingdom kingdom in pKingdoms)
            {
                if (!TryReadKingdom(kingdom, out int count)) return false;
                pAdditional = SaturatingAdd(pAdditional, count);
            }
            return true;
        }

        private static bool TryReadKingdom(Kingdom pKingdom,
            out int pAdditional)
        {
            pAdditional = 0;
            SpecialWarGovernmentKind kind = ResolveKind(pKingdom);
            if (kind == SpecialWarGovernmentKind.Ordinary) return true;
            if (pKingdom?.units == null) return false;
            int unitCount = pKingdom.units.Count;
            if (!Counts.TryGetValue(pKingdom.id, out CountCache cache) ||
                cache.Kind != kind || cache.UnitCount != unitCount)
            {
                cache = new CountCache { Kind = kind,
                    UnitCount = unitCount };
                Counts[pKingdom.id] = cache;
            }
            if (!cache.Complete)
            {
                long heirId = ReadHeirId(pKingdom);
                int end = Math.Min(unitCount,
                    cache.NextIndex + ReconcileBatch);
                for (int i = cache.NextIndex; i < end; i++)
                {
                    Actor actor = pKingdom.units[i];
                    if (actor == null) continue;
                    bool heir = heirId == actor.data.id;
                    if (SpecialGovernmentWarParticipationRules
                            .CountsAsAdditionalCombatant(kind,
                                IsAlive(actor), true, actor.isKing(), heir,
                                actor.isCityLeader(),
                                actor.asset?.is_boat == true,
                                actor.is_profession_warrior))
                        cache.Additional = SaturatingAdd(cache.Additional, 1);
                }
                cache.NextIndex = end;
                cache.Complete = end >= unitCount;
            }
            if (!cache.Complete) return false;
            pAdditional = cache.Additional;
            return true;
        }

        private static bool IsScopedCivilian(Actor pActor)
        {
            if (!IsAlive(pActor) || pActor.asset?.is_boat == true ||
                pActor.is_profession_warrior) return false;
            SpecialWarGovernmentKind kind = ResolveKind(pActor.kingdom);
            return kind != SpecialWarGovernmentKind.MilitaryGovernorate &&
                   SpecialGovernmentWarParticipationRules.CanFightAsCivilian(
                       kind, true, HasActiveWar(pActor.kingdom), false);
        }

        private static bool HasActiveWar(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            if (ActiveWarCounts.TryGetValue(pKingdom.id, out int cached))
                return cached > 0;
            int count = CountActiveWars(pKingdom);
            ActiveWarCounts[pKingdom.id] = count;
            return count > 0;
        }

        private static long ReadHeirId(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return -1L;
            try
            {
                pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID,
                    out long heirId, -1L);
                return heirId;
            }
            catch { return -1L; }
        }

        private static int CountActiveWars(Kingdom pKingdom)
        {
            int count = 0;
            try
            {
                foreach (War war in pKingdom.getWars())
                    if (war?.data != null && !war.hasEnded()) count++;
            }
            catch { }
            return count;
        }

        private static void RefreshActiveWarCounts(War pWar)
        {
            if (pWar?.data == null) return;
            var seen = new HashSet<long>();
            RefreshSideActiveWarCounts(pWar.getAttackers(), seen);
            RefreshSideActiveWarCounts(pWar.getDefenders(), seen);
        }

        private static void RefreshSideActiveWarCounts(
            IEnumerable<Kingdom> pKingdoms, HashSet<long> pSeen)
        {
            foreach (Kingdom kingdom in pKingdoms)
            {
                if (kingdom?.data == null || !pSeen.Add(kingdom.id)) continue;
                ActiveWarCounts[kingdom.id] = CountActiveWars(kingdom);
            }
        }

        private static bool AreFoes(Kingdom pFirst, Kingdom pSecond)
        {
            try { return pFirst.isEnemy(pSecond); }
            catch { return false; }
        }

        private static bool IsAlive(Actor pActor)
        {
            try { return pActor?.data != null && pActor.isAlive() &&
                         !pActor.isRekt(); }
            catch { return false; }
        }

        private static int SaturatingAdd(int pFirst, int pSecond)
        {
            long total = (long)Math.Max(0, pFirst) + Math.Max(0, pSecond);
            return total >= int.MaxValue ? int.MaxValue : (int)total;
        }
    }
}
