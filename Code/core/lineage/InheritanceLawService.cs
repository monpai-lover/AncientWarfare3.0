using System;
using System.Collections.Generic;
using AncientWarfare3.core.court;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct InheritanceLawEvaluation
    {
        public InheritanceLawEvaluation(InheritanceLaw previous,
            InheritanceLaw effective, bool militaryUnlocked,
            bool civilUnlocked, int primogenitureScore, int militaryScore,
            int civilScore)
        {
            Previous = previous;
            Effective = effective;
            MilitaryUnlocked = militaryUnlocked;
            CivilUnlocked = civilUnlocked;
            PrimogenitureScore = primogenitureScore;
            MilitaryScore = militaryScore;
            CivilScore = civilScore;
        }

        public InheritanceLaw Previous { get; }
        public InheritanceLaw Effective { get; }
        public bool MilitaryUnlocked { get; }
        public bool CivilUnlocked { get; }
        public int PrimogenitureScore { get; }
        public int MilitaryScore { get; }
        public int CivilScore { get; }
        public bool Changed => Previous != Effective;
    }

    internal static class InheritanceLawService
    {
        private const int MaximumArmyCitiesRead = 64;

        public static bool OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                RepublicGovernmentService.IsRepublic(pKingdom))
                return false;
            int year = Date.getCurrentYear();
            pKingdom.data.get(
                LineageKeys.INHERITANCE_LAW_LAST_EVALUATION_YEAR,
                out int lastYear, -1);
            if (!InheritanceLawRules.ShouldEvaluate(year, lastYear,
                    pKingdom.id))
                return false;
            InheritanceLawEvaluation evaluation = Evaluate(pKingdom, year);
            if (evaluation.Changed)
                ChronicleEvents.OnInheritanceLawChanged(pKingdom,
                    pKingdom.king, evaluation.Previous,
                    evaluation.Effective);
            return evaluation.Changed;
        }

        public static InheritanceLawEvaluation Evaluate(Kingdom pKingdom,
            int pYear)
        {
            InheritanceLaw previous = GetEffectiveLaw(pKingdom);
            if (pKingdom?.data == null)
                return new InheritanceLawEvaluation(previous, previous,
                    false, false, 0, 0, 0);

            Actor king = pKingdom.king;
            int adultRoyals =
                InheritanceCandidateService.HasAdultRoyalCandidate(
                    pKingdom, king) ? 1 : 0;
            int generals = GeneralService.GetActiveGeneralsForReadModel(
                pKingdom, pAllowUnitFallback: false).Count;
            int armies = CountActiveArmyBodies(pKingdom);
            List<CourtOfficerView> officers = CourtService.GetActiveOfficers(
                pKingdom, InheritanceCandidateRules.MaximumOfficerSupporters);
            bool militaryUnlocked = InheritanceLawRules.CanUseMilitary(
                adultRoyals, generals, armies);
            bool civilUnlocked = InheritanceLawRules.CanUseCivil(
                adultRoyals, CourtService.HasOfficialCourt(pKingdom),
                CourtService.HasThreeDepartments(pKingdom),
                CourtAuxiliaryLawService.GetTermLaw(pKingdom) !=
                CourtTermLaw.Lifetime, officers.Count);

            CourtSnapshot court = CourtService.GetSnapshot(pKingdom);
            int rulerCourtInfluence = king?.data == null
                ? -60
                : InheritanceLawRules.ResolveRulerCourtInfluence(
                    RulerAbility(king), MinisterialPower(pKingdom),
                    StrongestRivalAristocraticPower(pKingdom),
                    RoyalGuardService.HasKingdomGuardStateHint(pKingdom));
            InheritanceLawSnapshot snapshot = new InheritanceLawSnapshot(
                MandateService.IsMandateKingdom(pKingdom)
                    ? MandatePhaseService.CurrentPhase
                    : MandatePhase.Golden,
                HasLivingDirectSon(king), StableDynasty(pKingdom, king),
                SafeAtWar(pKingdom), armies, generals, officers.Count,
                CourtInstitutionRules.Rank(
                    CourtInstitutionService.GetInstitution(pKingdom)),
                ToDirectionScore((court.war + court.aggression) * 0.5f),
                ToDirectionScore((court.order + court.peace +
                                   court.livelihood) / 3f),
                rulerCourtInfluence);
            int hereditaryScore = InheritanceLawRules.Score(
                InheritanceLaw.Primogeniture, snapshot);
            int militaryScore = InheritanceLawRules.Score(
                InheritanceLaw.MilitaryAcclaim, snapshot);
            int civilScore = InheritanceLawRules.Score(
                InheritanceLaw.CivilAcclaim, snapshot);

            InheritanceLaw? locked = GetLockedLaw(pKingdom);
            InheritanceLaw effective;
            if (locked.HasValue)
            {
                effective = InheritanceLawRules.ResolveEffective(locked,
                    militaryUnlocked, civilUnlocked);
            }
            else
            {
                InheritanceLaw availableCurrent =
                    InheritanceLawRules.ResolveEffective(previous,
                        militaryUnlocked, civilUnlocked);
                effective = InheritanceLawRules.SelectAutomatic(
                    availableCurrent, hereditaryScore, militaryScore,
                    civilScore, militaryUnlocked, civilUnlocked);
            }

            pKingdom.data.set(LineageKeys.INHERITANCE_LAW_EFFECTIVE,
                (int)effective);
            pKingdom.data.set(
                LineageKeys.INHERITANCE_LAW_LAST_EVALUATION_YEAR, pYear);
            pKingdom.data.set(LineageKeys.INHERITANCE_SCORE_PRIMOGENITURE,
                hereditaryScore);
            pKingdom.data.set(LineageKeys.INHERITANCE_SCORE_MILITARY,
                militaryScore);
            pKingdom.data.set(LineageKeys.INHERITANCE_SCORE_CIVIL,
                civilScore);
            pKingdom.data.set(
                LineageKeys.INHERITANCE_RULER_COURT_INFLUENCE,
                rulerCourtInfluence);
            pKingdom.data.set(LineageKeys.INHERITANCE_MILITARY_UNLOCKED,
                militaryUnlocked);
            pKingdom.data.set(LineageKeys.INHERITANCE_CIVIL_UNLOCKED,
                civilUnlocked);
            return new InheritanceLawEvaluation(previous, effective,
                militaryUnlocked, civilUnlocked, hereditaryScore,
                militaryScore, civilScore);
        }

        public static InheritanceLaw GetEffectiveLaw(Kingdom pKingdom)
        {
            if (pKingdom?.data == null)
                return InheritanceLaw.Primogeniture;
            pKingdom.data.get(LineageKeys.INHERITANCE_LAW_EFFECTIVE,
                out int law, (int)InheritanceLaw.Primogeniture);
            return InheritanceLawRules.Normalize(law);
        }

        public static InheritanceLaw? GetLockedLaw(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            pKingdom.data.get(LineageKeys.INHERITANCE_LAW_LOCKED,
                out int law, -1);
            return law < 0 ? null : InheritanceLawRules.Normalize(law);
        }

        public static InheritanceLawChangeResult TryChangeLock(
            Kingdom pKingdom, InheritanceLaw? pRequestedLock,
            bool pRecordHistory = true)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                RepublicGovernmentService.IsRepublic(pKingdom))
                return InheritanceLawChangeResult.InvalidKingdom;
            int year = Date.getCurrentYear();
            InheritanceLawEvaluation availability = Evaluate(pKingdom, year);
            InheritanceLaw? previousLock = GetLockedLaw(pKingdom);
            pKingdom.data.get(LineageKeys.INHERITANCE_LAW_LAST_CHANGE_YEAR,
                out int previousChangeYear, -1);
            float previousPoints = KingdomPolicyService.GetPoliticalPoints(
                pKingdom);
            InheritanceLawChangeResult validation =
                InheritanceLawRules.ValidateChange(year,
                    previousChangeYear, (int)Math.Floor(previousPoints),
                    previousLock, pRequestedLock,
                    availability.MilitaryUnlocked,
                    availability.CivilUnlocked);
            if (validation != InheritanceLawChangeResult.Success)
                return validation;

            int previousEffective = (int)GetEffectiveLaw(pKingdom);
            pKingdom.data.get(LineageKeys.INHERITANCE_CANDIDATE_ID,
                out long previousCandidateId, -1L);
            pKingdom.data.get(LineageKeys.INHERITANCE_CANDIDATE_MODE,
                out string previousCandidateMode, SuccessionMode.NONE);
            pKingdom.data.get(
                LineageKeys.INHERITANCE_CANDIDATE_REFERENCE_KING_ID,
                out long previousReferenceKingId, -1L);
            int cost = InheritanceLawRules.ChangeCost(pRequestedLock);
            if (cost > 0 && !KingdomPolicyService.TrySpendPoliticalPoints(
                    pKingdom, cost))
                return InheritanceLawChangeResult.InsufficientPoliticalPoints;

            try
            {
                pKingdom.data.set(LineageKeys.INHERITANCE_LAW_LOCKED,
                    pRequestedLock.HasValue ? (int)pRequestedLock.Value : -1);
                if (pRequestedLock.HasValue)
                    pKingdom.data.set(
                        LineageKeys.INHERITANCE_LAW_LAST_CHANGE_YEAR, year);
                Evaluate(pKingdom, year);
                HeirService.RefreshHeir(pKingdom);
                if (pRecordHistory)
                    ChronicleEvents.OnInheritanceLawChanged(pKingdom,
                        pKingdom.king, previousLock, pRequestedLock);
                return InheritanceLawChangeResult.Success;
            }
            catch (Exception exception)
            {
                pKingdom.data.set(LineageKeys.INHERITANCE_LAW_LOCKED,
                    previousLock.HasValue ? (int)previousLock.Value : -1);
                pKingdom.data.set(LineageKeys.INHERITANCE_LAW_EFFECTIVE,
                    previousEffective);
                pKingdom.data.set(
                    LineageKeys.INHERITANCE_LAW_LAST_CHANGE_YEAR,
                    previousChangeYear);
                pKingdom.data.set(LineageKeys.INHERITANCE_CANDIDATE_ID,
                    previousCandidateId);
                pKingdom.data.set(LineageKeys.INHERITANCE_CANDIDATE_MODE,
                    previousCandidateMode ?? SuccessionMode.NONE);
                pKingdom.data.set(
                    LineageKeys.INHERITANCE_CANDIDATE_REFERENCE_KING_ID,
                    previousReferenceKingId);
                KingdomPolicyService.RestorePoliticalPoints(pKingdom,
                    previousPoints);
                ModClass.LogWarning("Inheritance law change failed: " +
                                    exception.Message);
                return InheritanceLawChangeResult.PersistenceFailed;
            }
        }

        public static void SetTemporaryEffective(Kingdom pKingdom,
            InheritanceLaw pLaw)
        {
            pKingdom?.data?.set(LineageKeys.INHERITANCE_LAW_EFFECTIVE,
                (int)pLaw);
        }

        public static bool RestorePrimogenitureForDirectSon(
            Kingdom pKingdom, bool pHasLivingLegitimateDirectSon)
        {
            if (pKingdom?.data == null) return false;
            InheritanceLaw previous = GetEffectiveLaw(pKingdom);
            if (!InheritanceLawRules.ShouldRestorePrimogeniture(previous,
                    pHasLivingLegitimateDirectSon)) return false;
            CommitPrimogeniture(pKingdom);
            ChronicleEvents.OnInheritanceLawChanged(pKingdom,
                pKingdom.king, previous, InheritanceLaw.Primogeniture);
            return true;
        }

        public static bool EstablishHereditaryBranchAfterAccession(
            Kingdom pKingdom, Actor pKing, string pSuccessionMode)
        {
            if (pKingdom?.data == null || pKing?.data == null ||
                pKingdom.king != pKing ||
                !InheritanceLawRules.EstablishesHereditaryBranch(
                    pSuccessionMode)) return false;

            pKing.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            pKing.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            if (lineageId < 0 && shiId >= 0)
                lineageId = LineageQuery.GetShiBranchInfo(shiId)?.lineage_id ??
                            -1L;
            if (lineageId < 0 && shiId < 0) return false;

            if (lineageId >= 0)
                pKingdom.data.set(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID,
                    lineageId);
            if (shiId >= 0)
                pKingdom.data.set(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID,
                    shiId);
            InheritanceLaw previous = GetEffectiveLaw(pKingdom);
            CommitPrimogeniture(pKingdom);
            pKingdom.data.set(LineageKeys.KINGDOM_SUCCESSION_MODE,
                SuccessionMode.DIRECT);
            if (previous != InheritanceLaw.Primogeniture)
                ChronicleEvents.OnInheritanceLawChanged(pKingdom, pKing,
                    previous, InheritanceLaw.Primogeniture);
            return true;
        }

        private static void CommitPrimogeniture(Kingdom pKingdom)
        {
            pKingdom.data.set(LineageKeys.INHERITANCE_LAW_EFFECTIVE,
                (int)InheritanceLaw.Primogeniture);
            pKingdom.data.set(LineageKeys.INHERITANCE_LAW_LOCKED,
                (int)InheritanceLaw.Primogeniture);
            pKingdom.data.set(
                LineageKeys.INHERITANCE_LAW_LAST_EVALUATION_YEAR,
                Date.getCurrentYear());
            MirrorCandidate(pKingdom, null, SuccessionMode.NONE, -1L);
        }

        public static void MirrorCandidate(Kingdom pKingdom, Actor pCandidate,
            string pMode, long pReferenceKingId)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.INHERITANCE_CANDIDATE_ID,
                pCandidate?.data?.id ?? -1L);
            pKingdom.data.set(LineageKeys.INHERITANCE_CANDIDATE_MODE,
                pCandidate?.data == null ? SuccessionMode.NONE :
                pMode ?? SuccessionMode.NONE);
            pKingdom.data.set(
                LineageKeys.INHERITANCE_CANDIDATE_REFERENCE_KING_ID,
                pCandidate?.data == null ? -1L : pReferenceKingId);
        }

        public static string ModeForLaw(InheritanceLaw pLaw)
        {
            return pLaw == InheritanceLaw.MilitaryAcclaim
                ? SuccessionMode.MILITARY_ACCLAIM
                : pLaw == InheritanceLaw.CivilAcclaim
                    ? SuccessionMode.CIVIL_ACCLAIM
                    : SuccessionMode.NONE;
        }

        private static int CountActiveArmyBodies(Kingdom pKingdom)
        {
            int count = 0;
            int scanned = 0;
            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    if (scanned++ >= MaximumArmyCitiesRead) break;
                    if (city?.data != null && city.hasArmy() &&
                        city.getArmy()?.countUnits() > 0)
                        count++;
                }
            }
            catch { }
            if (count > 0) return count;
            count += AWArmyService.GetRoleArmies(pKingdom,
                AWArmyRole.BorderArmy).Count;
            count += AWArmyService.GetRoleArmies(pKingdom,
                AWArmyRole.FeudatoryGarrison).Count;
            return count;
        }

        private static bool HasLivingDirectSon(Actor pKing)
        {
            if (pKing?.data == null) return false;
            try
            {
                foreach (Actor child in pKing.getChildren(false))
                    if (child?.data != null && child.isSexMale() &&
                        child.isAlive() && !child.isRekt())
                        return true;
            }
            catch { }
            return false;
        }

        private static bool StableDynasty(Kingdom pKingdom, Actor pKing)
        {
            if (pKingdom?.data == null || pKing?.data == null) return false;
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID,
                out long legitimateLineage, -1L);
            pKing.data.get(LineageKeys.LINEAGE_ID, out long kingLineage,
                -1L);
            pKingdom.data.get(LineageKeys.ACTIVE_SUCCESSION_DISPUTE_ID,
                out long disputeId, -1L);
            return legitimateLineage >= 0 && kingLineage == legitimateLineage &&
                   disputeId < 0;
        }

        private static bool SafeAtWar(Kingdom pKingdom)
        {
            try { return pKingdom?.data != null && pKingdom.hasEnemies(); }
            catch { return false; }
        }

        private static int ToDirectionScore(float pNormalized)
        {
            return Math.Max(0, Math.Min(20,
                (int)Math.Round(Math.Max(0f, Math.Min(1f, pNormalized)) *
                                20f)));
        }

        private static int RulerAbility(Actor pKing)
        {
            if (pKing?.data == null) return 0;
            float value = SafeStat(pKing, "stewardship") +
                          SafeStat(pKing, "diplomacy") +
                          SafeStat(pKing, "intelligence");
            return Math.Max(0, (int)Math.Round(value / 3f));
        }

        private static int MinisterialPower(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0;
            pKingdom.data.get(LineageKeys.MINISTERIAL_PREMIER_POWER,
                out int power, 0);
            return Math.Max(0, Math.Min(100, power));
        }

        private static int StrongestRivalAristocraticPower(
            Kingdom pKingdom)
        {
            IReadOnlyList<CourtAristocraticGroup> groups =
                CourtAristocraticGroupService.GetCachedGroups(pKingdom);
            return groups != null && groups.Count > 0 && groups[0] != null
                ? Math.Max(0, groups[0].Power)
                : 0;
        }

        private static float SafeStat(Actor pActor, string pStat)
        {
            try { return pActor?.stats?[pStat] ?? 0f; }
            catch { return 0f; }
        }
    }
}
