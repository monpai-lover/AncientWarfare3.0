using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class SlaveKingAbdicationService
    {
        private static readonly Dictionary<long, string> PendingReasons = new Dictionary<long, string>();

        public static void ClearRuntime()
        {
            PendingReasons.Clear();
        }

        public static bool TryForceAbdicate(Actor pActor, string pReason, bool pWasKing, bool pWasSlave,
            Kingdom pKingdom)
        {
            if (pActor?.data == null) return false;
            bool isSlaveNow = SlaveService.IsSlave(pActor);
            if (!SlaveKingAbdicationRules.ShouldForceAbdicate(pWasKing, pWasSlave, isSlaveNow,
                    pKingdom?.data != null))
                return false;
            if (pKingdom.king != pActor) return false;
            if (TryConvertSlaveOnlyKingdom(pKingdom, pActor, isSlaveNow)) return true;

            PendingReasons[pActor.data.id] = pReason ?? "";
            pKingdom.kingLeftEvent();
            return true;
        }

        public static bool TryForceCurrentSlaveKing(Kingdom pKingdom, Actor pKing, string pReason)
        {
            bool isKing = pKingdom?.data != null && pKing?.data != null && pKingdom.king == pKing;
            return TryForceAbdicate(pKing, pReason, isKing, pWasSlave: true, pKingdom);
        }

        public static bool TryConsumeReason(long pActorId, out string pReason)
        {
            if (PendingReasons.TryGetValue(pActorId, out pReason))
            {
                PendingReasons.Remove(pActorId);
                return true;
            }

            pReason = "";
            return false;
        }

        private static bool TryConvertSlaveOnlyKingdom(Kingdom pKingdom, Actor pKing, bool pIsSlaveNow)
        {
            if (pKingdom?.data == null || pKing?.data == null) return false;

            CountLivingCandidates(pKingdom, out int livingCandidates, out int freeCandidates);
            if (!SlaveKingAbdicationRules.ShouldConvertSlaveOnlyKingdomToRebel(
                    pIsKing: pKingdom.king == pKing,
                    pIsSlaveNow: pIsSlaveNow,
                    pHasKingdom: true,
                    pLivingCandidates: livingCandidates,
                    pFreeCandidates: freeCandidates))
                return false;

            pKingdom.data.get(LineageKeys.SLAVE_ONLY_REBEL_CONVERTED, out bool converted, false);
            SlaveService.SetSlaveryEnabled(pKingdom, false);
            FreeKingdomSlaves(pKingdom);
            MandateRebelService.MarkRebelKingdom(pKingdom, pKing, pKingdom);
            pKingdom.data.set(LineageKeys.SLAVE_ONLY_REBEL_CONVERTED, true);

            if (!converted)
            {
                HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.MANDATE_REBELLION,
                    HistoryText.Kingdom(pKingdom) +
                    HistoryLocalizationRules.H("aw_hist_slave_only_rebel_kingdom"),
                    HistoryTarget.Actor(pKing));
                HistoryWriter.RecordPerson(pKing.data.id, pKingdom, pKing.getName(),
                    PersonEvent.MANDATE_REBEL_LEADER,
                    HistoryText.Actor(pKing) +
                    HistoryLocalizationRules.H("aw_hist_slave_only_rebel_leader"),
                    ChronicleCategory.HONOR,
                    HistoryTarget.Kingdom(pKingdom));
            }

            return true;
        }

        private static void CountLivingCandidates(Kingdom pKingdom, out int pLivingCandidates, out int pFreeCandidates)
        {
            pLivingCandidates = 0;
            pFreeCandidates = 0;
            if (pKingdom?.data == null) return;

            foreach (Actor unit in pKingdom.getUnits())
            {
                if (!CanConsiderLivingCandidate(unit, pKingdom)) continue;
                pLivingCandidates++;
                if (!SlaveService.IsSlave(unit)) pFreeCandidates++;
            }
        }

        private static void FreeKingdomSlaves(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;

            var slaves = new List<Actor>();
            foreach (Actor unit in pKingdom.getUnits())
            {
                if (unit?.data == null || unit.kingdom != pKingdom) continue;
                if (!SlaveService.IsSlave(unit)) continue;
                slaves.Add(unit);
            }

            foreach (Actor slave in slaves)
                SlaveService.FreeSlave(slave, "slave_only_rebel");
        }

        private static bool CanConsiderLivingCandidate(Actor pActor, Kingdom pKingdom)
        {
            if (pActor?.data == null || pActor.kingdom != pKingdom) return false;
            if (pActor.isRekt() || !pActor.isAlive()) return false;
            if (pActor.asset?.is_boat == true) return false;
            return true;
        }
    }
}
