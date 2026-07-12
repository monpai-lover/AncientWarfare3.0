using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class RoyalMedicalCareService
    {
        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.COURT_MEDICAL_LAST_YEAR, out int lastYear, -1);
            if (lastYear == year) return;
            pKingdom.data.set(LineageKeys.COURT_MEDICAL_LAST_YEAR, year);
            ReconcileTargets(pKingdom, pApplyTreatment: true);
        }

        public static void ReconcileTargets(Kingdom pKingdom)
        {
            ReconcileTargets(pKingdom, pApplyTreatment: false);
        }

        private static void ReconcileTargets(Kingdom pKingdom, bool pApplyTreatment)
        {
            if (pKingdom?.data == null) return;
            Actor physician = FindActivePhysician(pKingdom);
            var targets = new Dictionary<long, Actor>();
            AddTarget(targets, pKingdom.king, physician, pKingdom);
            AddTarget(targets, HeirService.PeekRegisteredHeir(pKingdom), physician, pKingdom);

            pKingdom.data.get(LineageKeys.COURT_MEDICAL_KING_ID, out long oldKingId, -1L);
            pKingdom.data.get(LineageKeys.COURT_MEDICAL_HEIR_ID, out long oldHeirId, -1L);
            long[] currentIds = targets.Keys.ToArray();
            foreach (long removedId in RoyalMedicalCareRules.RemovedTargetIds(
                         oldKingId, oldHeirId, currentIds))
                FinishCare(removedId);

            foreach (Actor target in targets.Values)
            {
                target.addStatusEffect(CourtStatusId.RoyalMedicalCare, 120f, pColorEffect: false);
                if (pApplyTreatment) Treat(physician, target, pKingdom);
            }

            long kingId = targets.ContainsKey(pKingdom.king?.data?.id ?? -1L)
                ? pKingdom.king.data.id
                : -1L;
            Actor heir = HeirService.PeekRegisteredHeir(pKingdom);
            long heirId = heir?.data != null && targets.ContainsKey(heir.data.id) ? heir.data.id : -1L;
            pKingdom.data.set(LineageKeys.COURT_MEDICAL_KING_ID, kingId);
            pKingdom.data.set(LineageKeys.COURT_MEDICAL_HEIR_ID, heirId);
        }

        private static Actor FindActivePhysician(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            pKingdom.data.get(LineageKeys.COURT_IMPERIAL_PHYSICIAN_ID,
                out long cachedActorId, -1L);
            if (cachedActorId < 0) return null;

            Actor actor = null;
            try
            {
                actor = World.world?.units?.get(cachedActorId);
            }
            catch { }

            if (actor?.data != null)
            {
                actor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
                actor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
                if (RoyalMedicalCareRules.IsCachedPhysicianValid(
                        cachedActorId, actor.data.id,
                        actor.isAlive() && !actor.isRekt(),
                        CourtAffiliationResolver.CanServe(actor, pKingdom,
                            CourtOfficeLayer.Central),
                        courtKingdomId, pKingdom.id, office))
                    return actor;
            }

            pKingdom.data.set(LineageKeys.COURT_IMPERIAL_PHYSICIAN_ID, -1L);
            return null;
        }

        private static void AddTarget(Dictionary<long, Actor> pTargets, Actor pTarget,
            Actor pPhysician, Kingdom pKingdom)
        {
            if (pTarget?.data == null || pPhysician?.data == null) return;
            if (!RoyalMedicalCareRules.ShouldTreat(
                    pPhysician.isAlive() && !pPhysician.isRekt(), physicianActive: true,
                    pTarget.kingdom == pKingdom, pTarget.isAlive() && !pTarget.isRekt()))
                return;
            pTargets[pTarget.data.id] = pTarget;
        }

        private static void FinishCare(long pActorId)
        {
            if (pActorId < 0) return;
            Actor actor = World.world?.units?.get(pActorId);
            if (actor?.data != null) actor.finishStatusEffect(CourtStatusId.RoyalMedicalCare);
        }

        private static void Treat(Actor pPhysician, Actor pTarget, Kingdom pKingdom)
        {
            pTarget.restoreHealthPercent(1f);
            int removed = 0;
            foreach (ActorTrait trait in pTarget.getTraits().ToList())
            {
                if (!trait.can_be_cured) continue;
                pTarget.removeTrait(trait.id);
                removed++;
            }
            if (RoyalMedicalCareRules.ShouldRecordCure(removed))
                ChronicleEvents.OnRoyalMedicalCure(pPhysician, pTarget, pKingdom);
        }
    }
}
