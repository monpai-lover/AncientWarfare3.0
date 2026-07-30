using System;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct FeudatoryJingnanRiskReport
    {
        public FeudatoryJingnanRiskReport(int risk, int ambition,
            int rulerDeterrence, int centralCrisis,
            bool rulerIsDirectAgnaticAncestor)
        {
            Risk = risk;
            Ambition = ambition;
            RulerDeterrence = rulerDeterrence;
            CentralCrisis = centralCrisis;
            RulerIsDirectAgnaticAncestor =
                rulerIsDirectAgnaticAncestor;
        }

        public int Risk { get; }
        public int Ambition { get; }
        public int RulerDeterrence { get; }
        public int CentralCrisis { get; }
        public bool RulerIsDirectAgnaticAncestor { get; }
    }

    internal static class FeudatoryJingnanRiskService
    {
        public static FeudatoryJingnanRiskReport Evaluate(Kingdom pEmpire,
            FeudatorySnapshot pSnapshot, int pRevocationIntensity,
            int pCentralWarriors)
        {
            Actor prince = FindActor(pSnapshot?.PrinceActorId ?? -1L);
            if (pEmpire?.data == null || pSnapshot == null ||
                prince?.data == null)
                return new FeudatoryJingnanRiskReport(0, 0, 0, 0, false);

            prince.data.get(LineageKeys.FEUDATORY_AMBITION,
                out int baseAmbition, 20);
            int ambition = FeudatoryJingnanRiskRules.PersonalityAmbition(
                baseAmbition, prince.hasTrait("ambitious"),
                prince.hasTrait("content"), prince.hasTrait("greedy"),
                prince.hasTrait("deceitful"));

            Actor ruler = pEmpire.king;
            bool directAncestor = false;
            int kinshipDeterrence = 0;
            if (ruler?.data != null)
            {
                ResolveKinship(prince, ruler, out int rulerDepth,
                    out int princeDepth);
                directAncestor =
                    FeudatoryJingnanRiskRules.IsDirectAgnaticAncestor(
                        rulerDepth, princeDepth);
                if (rulerDepth >= 0 && princeDepth >= 0)
                {
                    bool rulerOlder = ruler.data.created_time <=
                                      prince.data.created_time;
                    kinshipDeterrence =
                        FeudatoryJingnanRiskRules.KinshipDeterrence(
                            princeDepth - rulerDepth, rulerOlder);
                }
            }

            int mandateValue = ReadInt(pEmpire, LineageKeys.MANDATE_VALUE, 50);
            int authority = ReadInt(pEmpire, LineageKeys.MANDATE_AUTHORITY, 45);
            int rulerDeterrence = kinshipDeterrence +
                FeudatoryJingnanRiskRules.LegitimacyDeterrence(
                    mandateValue, authority);
            if (ruler?.data != null)
                rulerDeterrence +=
                    FeudatoryJingnanRiskRules.AbilityDeterrence(
                        SafeStat(ruler, "warfare"),
                        SafeStat(ruler, "diplomacy"),
                        SafeStat(ruler, "stewardship"));
            if (HasRoyalGuardHint(pEmpire)) rulerDeterrence += 5;

            int ministerialPower = ReadInt(pEmpire,
                LineageKeys.MINISTERIAL_PREMIER_POWER, 0);
            bool successionUnstable = IsSuccessionUnstable(pEmpire);
            int centralCrisis = FeudatoryJingnanRiskRules.CentralCrisis(
                ruler?.data != null, SafeAdult(ruler), SafeAge(ruler),
                successionUnstable, ministerialPower, IsAtWar(pEmpire));
            int risk = FeudatoryJingnanRiskRules.CalculateRisk(
                ambition, pSnapshot.Loyalty, pSnapshot.Autonomy,
                pSnapshot.GarrisonSize, pCentralWarriors,
                pRevocationIntensity, centralCrisis, rulerDeterrence);
            prince.data.set(LineageKeys.JINGNAN_LAST_RISK, risk);
            return new FeudatoryJingnanRiskReport(risk, ambition,
                rulerDeterrence, centralCrisis, directAncestor);
        }

        public static int CountCentralWarriors(Kingdom pEmpire)
        {
            try { return Math.Max(0, pEmpire?.countTotalWarriors() ?? 0); }
            catch { return 0; }
        }

        public static bool HasActiveWar(Kingdom pEmpire)
        {
            return IsAtWar(pEmpire);
        }

        private static void ResolveKinship(Actor pPrince, Actor pRuler,
            out int pRulerDepth, out int pPrinceDepth)
        {
            pRulerDepth = -1;
            pPrinceDepth = -1;
            if (pPrince?.data == null || pRuler?.data == null) return;
            pPrince.data.get(LineageKeys.JINGNAN_KIN_RULER_ID,
                out long cachedRulerId, -1L);
            if (cachedRulerId == pRuler.data.id)
            {
                pPrince.data.get(LineageKeys.JINGNAN_KIN_RULER_DEPTH,
                    out pRulerDepth, -1);
                pPrince.data.get(LineageKeys.JINGNAN_KIN_PRINCE_DEPTH,
                    out pPrinceDepth, -1);
                return;
            }

            LineageQuery.NearestCommonAgnaticAncestor(
                pRuler.data.id, pPrince.data.id, out pRulerDepth,
                out pPrinceDepth);
            pPrince.data.set(LineageKeys.JINGNAN_KIN_RULER_ID,
                pRuler.data.id);
            pPrince.data.set(LineageKeys.JINGNAN_KIN_RULER_DEPTH,
                pRulerDepth);
            pPrince.data.set(LineageKeys.JINGNAN_KIN_PRINCE_DEPTH,
                pPrinceDepth);
        }

        private static bool IsSuccessionUnstable(Kingdom pEmpire)
        {
            if (pEmpire?.data == null) return true;
            bool pending = SuccessionTransitionRules.IsPending(
                pEmpire.data.timer_new_king);
            pEmpire.data.get(LineageKeys.KINGDOM_HEIR_ID,
                out long heirId, -1L);
            return SuccessionTransitionRules.ShouldTreatMissingHeirAsUnstable(
                pending, heirId >= 0);
        }

        private static bool HasRoyalGuardHint(Kingdom pEmpire)
        {
            if (pEmpire?.data == null) return false;
            pEmpire.data.get(LineageKeys.ROYAL_GUARD_RECORDED,
                out bool recorded, false);
            pEmpire.data.get(LineageKeys.ROYAL_GUARD_ARMY_ID,
                out long armyId, -1L);
            return recorded || armyId >= 0;
        }

        private static bool IsAtWar(Kingdom pEmpire)
        {
            try
            {
                foreach (War war in pEmpire.getWars())
                    if (war?.data != null) return true;
            }
            catch { }
            return false;
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static int ReadInt(Kingdom pKingdom, string pKey,
            int pFallback)
        {
            if (pKingdom?.data == null) return pFallback;
            pKingdom.data.get(pKey, out int value, pFallback);
            return value;
        }

        private static float SafeStat(Actor pActor, string pKey)
        {
            try { return pActor?.stats?[pKey] ?? 0f; }
            catch { return 0f; }
        }

        private static bool SafeAdult(Actor pActor)
        {
            try { return pActor?.isAdult() == true; }
            catch { return false; }
        }

        private static int SafeAge(Actor pActor)
        {
            try { return pActor?.getAge() ?? 0; }
            catch { return 0; }
        }
    }
}
