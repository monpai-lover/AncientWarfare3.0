using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class MilitaryEmergencyService
    {
        private static readonly Dictionary<long, HashSet<long>> WarIdsByKingdom =
            new Dictionary<long, HashSet<long>>();
        private static readonly Dictionary<long, HashSet<long>> DefensiveWarIdsByKingdom =
            new Dictionary<long, HashSet<long>>();

        public static bool HasAny(Kingdom pKingdom)
        {
            if (WarNoticeService.HasActiveNotice(pKingdom)) return true;
            return pKingdom?.data != null &&
                   WarIdsByKingdom.TryGetValue(pKingdom.id, out HashSet<long> wars) &&
                   wars.Count > 0;
        }

        public static bool HasDefensive(Kingdom pKingdom)
        {
            return pKingdom?.data != null &&
                   DefensiveWarIdsByKingdom.TryGetValue(pKingdom.id, out HashSet<long> wars) &&
                   wars.Count > 0;
        }

        public static bool TryGetActiveWarId(Kingdom pKingdom, out long pWarId)
        {
            pWarId = -1L;
            if (pKingdom?.data == null ||
                !WarIdsByKingdom.TryGetValue(pKingdom.id, out HashSet<long> wars)) return false;
            foreach (long warId in wars)
            {
                pWarId = warId;
                return true;
            }
            return false;
        }

        public static void OnWarStarted(War pWar)
        {
            if (pWar?.data == null || pWar.hasEnded()) return;
            long warId = pWar.data.id;
            foreach (Kingdom kingdom in pWar.getAttackers())
            {
                Add(WarIdsByKingdom, kingdom, warId);
                ArmyDeploymentService.OnKingdomEnteredWar(kingdom);
            }
            foreach (Kingdom kingdom in pWar.getDefenders())
            {
                Add(WarIdsByKingdom, kingdom, warId);
                Add(DefensiveWarIdsByKingdom, kingdom, warId);
                ArmyDeploymentService.OnKingdomEnteredWar(kingdom);
            }
        }

        public static void OnKingdomJoinedWar(War pWar, Kingdom pKingdom, bool pDefender)
        {
            if (pWar?.data == null || pWar.hasEnded() || pKingdom?.data == null) return;
            Add(WarIdsByKingdom, pKingdom, pWar.data.id);
            if (pDefender) Add(DefensiveWarIdsByKingdom, pKingdom, pWar.data.id);
            ArmyDeploymentService.OnKingdomEnteredWar(pKingdom);
        }

        public static void OnKingdomLeftWar(War pWar, Kingdom pKingdom)
        {
            if (pWar?.data == null || pKingdom?.data == null) return;
            Remove(WarIdsByKingdom, pKingdom, pWar.data.id);
            Remove(DefensiveWarIdsByKingdom, pKingdom, pWar.data.id);
        }

        public static void OnWarEnded(War pWar)
        {
            if (pWar?.data == null) return;
            long warId = pWar.data.id;
            foreach (Kingdom kingdom in pWar.getAttackers()) Remove(WarIdsByKingdom, kingdom, warId);
            foreach (Kingdom kingdom in pWar.getDefenders())
            {
                Remove(WarIdsByKingdom, kingdom, warId);
                Remove(DefensiveWarIdsByKingdom, kingdom, warId);
            }
        }

        public static void RebuildRuntime()
        {
            ClearRuntime();
            if (World.world?.wars == null) return;
            foreach (War war in World.world.wars)
                OnWarStarted(war);
        }

        public static void ClearRuntime()
        {
            WarIdsByKingdom.Clear();
            DefensiveWarIdsByKingdom.Clear();
        }

        private static void Add(Dictionary<long, HashSet<long>> pIndex, Kingdom pKingdom, long pWarId)
        {
            if (pKingdom?.data == null || pWarId < 0) return;
            if (!pIndex.TryGetValue(pKingdom.id, out HashSet<long> wars))
            {
                wars = new HashSet<long>();
                pIndex[pKingdom.id] = wars;
            }
            wars.Add(pWarId);
        }

        private static void Remove(Dictionary<long, HashSet<long>> pIndex, Kingdom pKingdom, long pWarId)
        {
            if (pKingdom?.data == null || !pIndex.TryGetValue(pKingdom.id, out HashSet<long> wars)) return;
            wars.Remove(pWarId);
            if (wars.Count == 0) pIndex.Remove(pKingdom.id);
        }
    }
}
