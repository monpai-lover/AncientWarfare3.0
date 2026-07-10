using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.utils;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class GeneralRebellionService
    {
        public const string WAR_GENERAL_REBELLION = "general_rebellion_war";
        public const string WAR_FIEF_INDEPENDENCE = "fief_independence_war";

        private const int HIGH_RISK_RECORD_COOLDOWN = 15;
        private const int REBELLION_RISK_THRESHOLD = 92;
        private static HistoryText H(string pKey) => HistoryLocalizationRules.H(pKey);

        public static void OnKingdomRiskCheck(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || pKingdom.isNeutral()) return;
            int crisis = CalculateKingdomCrisis(pKingdom);
            foreach (Actor general in GeneralService.GetActiveGenerals(pKingdom))
            {
                if (general?.data == null) continue;
                int power = GeneralService.CountPersonalPower(general);
                GeneralService.UpdateTroopPower(general, power);
                int risk = CalculateRisk(general, pKingdom, power);
                if (risk >= 65) RecordHighRisk(general, pKingdom, risk);
                if (risk >= REBELLION_RISK_THRESHOLD || (risk + crisis) / 2 >= 70)
                {
                    GeneralRebellionBranch branch = GeneralRebellionRules.SelectBranch(crisis, risk,
                        FiefService.GetFiefCityId(general) >= 0,
                        IsNearCapital(general, pKingdom),
                        IsBorderFief(general, pKingdom),
                        FindStrongNeighbor(general, pKingdom)?.data != null,
                        RoyalClaimService.HasHostedClaim(pKingdom));
                    if (branch != GeneralRebellionBranch.None) TryRebel(general, pKingdom, risk, branch);
                }
            }
        }

        public static int CalculateRisk(Actor pGeneral, Kingdom pKingdom, int pTroopPower)
        {
            if (pGeneral?.data == null || pKingdom?.data == null) return 0;
            int loyalty = GeneralService.GetLoyalty(pGeneral);
            int ambition = GeneralService.GetAmbition(pGeneral);
            int risk = ambition - loyalty;

            int kingdomArmy = CountWarriors(pKingdom);
            if (kingdomArmy > 0 && pTroopPower >= kingdomArmy * 0.35f) risk += 25;

            City fief = FiefService.GetFiefCity(pGeneral);
            if (fief?.data != null)
            {
                int kingdomPop = CountPopulation(pKingdom);
                int fiefPop = SafePop(fief);
                if (kingdomPop > 0 && fiefPop >= kingdomPop * 0.25f) risk += 15;
                risk += 8;
            }

            Actor king = pKingdom.king;
            if (king?.data != null)
            {
                int age = SafeAge(king);
                if (!king.isAdult() || age >= 75) risk += 15;
                if (SameParentLine(pGeneral, king)) risk -= 15;
                else if (DifferentShi(pGeneral, king)) risk += 10;
                risk -= Mathf.RoundToInt((SafeStat(king, "diplomacy") + SafeStat(king, "stewardship")) * 0.03f);
            }
            else
            {
                risk += 20;
            }

            if (GeneralService.GetMerit(pGeneral) >= 80) risk += 10;
            if (RoyalGuardExists(pKingdom)) risk -= 10;
            if (VassalService.IsVassalKingdom(pKingdom)) risk += 5;
            return Mathf.Clamp(risk, 0, 120);
        }

        private static int CalculateKingdomCrisis(Kingdom pKingdom)
        {
            Actor king = pKingdom?.king;
            int weakKingScore = 20;
            bool childOrOldRuler = false;
            if (king?.data != null)
            {
                int age = SafeAge(king);
                childOrOldRuler = !king.isAdult() || age >= 75;
                weakKingScore = Mathf.Clamp(55 - Mathf.RoundToInt(
                    (SafeStat(king, "diplomacy") + SafeStat(king, "stewardship") + SafeStat(king, "warfare")) * 0.08f),
                    0, 55);
            }

            bool successionPending = SuccessionTransitionRules.IsPending(pKingdom.data.timer_new_king);
            bool hasRegisteredHeir = HeirService.FindHeirReadOnly(pKingdom)?.data != null;
            bool successionUnstable = SuccessionTransitionRules.ShouldTreatMissingHeirAsUnstable(
                successionPending, hasRegisteredHeir);
            bool recentWarDefeat = false;
            bool capitalThreatened = IsAtWar(pKingdom);
            return GeneralRebellionRules.CalculateKingdomCrisis(weakKingScore, childOrOldRuler,
                successionUnstable, recentWarDefeat, capitalThreatened, CountOwnedNonCoreCities(pKingdom),
                CountDisloyalVassals(pKingdom), MandateValueFor(pKingdom), RoyalGuardExists(pKingdom));
        }

        private static void RecordHighRisk(Actor pGeneral, Kingdom pKingdom, int pRisk)
        {
            int year = Date.getCurrentYear();
            pGeneral.data.get(LineageKeys.GENERAL_RISK_RECORDED_YEAR, out int lastYear, -99999);
            if (year - lastYear < HIGH_RISK_RECORD_COOLDOWN) return;
            pGeneral.data.set(LineageKeys.GENERAL_RISK_RECORDED_YEAR, year);

            City city = FiefService.GetFiefCity(pGeneral) ?? pGeneral.city;
            HistoryWriter.RecordPerson(pGeneral.data.id, pKingdom, pGeneral.getName(),
                PersonEvent.GENERAL_RISK,
                HistoryText.Actor(pGeneral) + H("aw_hist_general_high_risk_person"),
                ChronicleCategory.WAR,
                city?.data != null ? HistoryTarget.City(city) : HistoryTarget.Actor(pGeneral));

            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.GENERAL_RISK,
                HistoryText.Actor(pGeneral) + H("aw_hist_general_high_risk_kingdom") +
                HistoryText.PlainText(pRisk.ToString()),
                HistoryTarget.Actor(pGeneral));

            if (city?.data != null)
                HistoryWriter.RecordCity(city, pKingdom, CityEvent.GENERAL_RISK,
                    HistoryText.Actor(pGeneral) + H("aw_hist_general_in_city") +
                    HistoryText.City(city, pKingdom) + H("aw_hist_general_high_risk_city"),
                    HistoryTarget.Actor(pGeneral));
        }

        private static bool TryRebel(Actor pGeneral, Kingdom pOldKingdom, int pRisk, GeneralRebellionBranch pBranch)
        {
            if (pGeneral?.data == null || pOldKingdom?.data == null) return false;
            if (IsAtWar(pOldKingdom)) return false;
            pGeneral.data.get("aw_general_rebelled_once", out bool rebelledOnce, false);
            if (rebelledOnce) return false;

            if (pBranch == GeneralRebellionBranch.PalaceCoup)
                return TryPalaceCoup(pGeneral, pOldKingdom, pRisk);
            if (pBranch == GeneralRebellionBranch.DefectToNeighbor)
                return TryDefectToNeighbor(pGeneral, pOldKingdom, pRisk);
            if (pBranch == GeneralRebellionBranch.SupportRestoration && TrySupportRestoration(pGeneral, pOldKingdom, pRisk))
                return true;

            bool hadFief = pBranch == GeneralRebellionBranch.FiefIndependence ||
                           FiefService.GetFiefCityId(pGeneral) >= 0;
            City baseCity = FiefService.GetFiefCity(pGeneral);
            if (baseCity == null && pGeneral.isCityLeader()) baseCity = pGeneral.city;
            if (baseCity?.data == null || baseCity.kingdom != pOldKingdom || baseCity == pOldKingdom.capital) return false;
            if (SafePop(baseCity) < 25) return false;

            Kingdom rebel = null;
            try
            {
                rebel = baseCity.makeOwnKingdom(pGeneral, pRebellion: true, pFellApart: false);
            }
            catch (Exception e)
            {
                ModClass.LogWarning("General rebellion makeOwnKingdom failed: " + e.Message);
                return false;
            }

            if (rebel?.data == null) return false;
            pGeneral.data.set("aw_general_rebelled_once", true);
            GeneralService.MarkRebelled(pGeneral);
            FiefService.RevokeFief(baseCity, "general_rebellion");
            StartRebellionWar(pOldKingdom, rebel, hadFief ? WAR_FIEF_INDEPENDENCE : WAR_GENERAL_REBELLION);
            RecordRebellion(pGeneral, pOldKingdom, rebel, baseCity, pRisk);
            return true;
        }

        private static bool TryPalaceCoup(Actor pGeneral, Kingdom pKingdom, int pRisk)
        {
            bool success = pRisk >= 100 || CalculateKingdomCrisis(pKingdom) >= 75 || !RoyalGuardExists(pKingdom);
            pGeneral.data.set("aw_general_rebelled_once", true);
            GeneralService.MarkRebelled(pGeneral);

            if (success)
            {
                try { pKingdom.setKing(pGeneral); }
                catch (Exception e)
                {
                    ModClass.LogWarning("General palace coup setKing failed: " + e.Message);
                    return false;
                }
            }

            RecordPalaceCoup(pGeneral, pKingdom, pRisk, success);
            return success;
        }

        private static bool TryDefectToNeighbor(Actor pGeneral, Kingdom pOldKingdom, int pRisk)
        {
            City baseCity = FiefService.GetFiefCity(pGeneral);
            if (baseCity == null && pGeneral.isCityLeader()) baseCity = pGeneral.city;
            if (baseCity?.data == null || baseCity.kingdom != pOldKingdom || baseCity == pOldKingdom.capital) return false;
            Kingdom neighbor = FindStrongNeighbor(pGeneral, pOldKingdom);
            if (neighbor?.data == null) return false;

            try
            {
                pGeneral.data.set("aw_general_rebelled_once", true);
                GeneralService.MarkRebelled(pGeneral);
                FiefService.RevokeFief(baseCity, "general_defection");
                baseCity.joinAnotherKingdom(neighbor);
                pGeneral.joinCity(baseCity);
                baseCity.setLeader(pGeneral, pNew: true);
                WarDecisionService.TryStartSystemWar(neighbor, pOldKingdom, WAR_GENERAL_REBELLION, "general_defection");
                RecordDefection(pGeneral, pOldKingdom, neighbor, baseCity, pRisk);
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("General defection failed: " + e.Message);
                return false;
            }
        }

        private static bool TrySupportRestoration(Actor pGeneral, Kingdom pKingdom, int pRisk)
        {
            if (!RoyalClaimService.HasHostedClaim(pKingdom)) return false;
            foreach (WarTerritoryService.TargetReport report in WarTerritoryService.BuildTargetReports(pKingdom))
            {
                if (report?.target?.data == null || !report.can_restore) continue;
                WarTerritoryService.WarTargetOption option =
                    WarTerritoryService.FindBestTargetOption(pKingdom, report.target, WarTerritoryService.GOAL_RESTORE_KINGDOM);
                if (option == null) continue;
                Actor claimant = FindActor(option.claimant_actor_id);
                if (!WarTerritoryService.TryDeclareRestorationWar(pKingdom, report.target, option.target_city,
                        option.restoration_claim_id, claimant)) continue;

                pGeneral.data.set("aw_general_rebelled_once", true);
                GeneralService.MarkRebelled(pGeneral);
                RecordRestorationSupport(pGeneral, pKingdom, report.target, option.target_city, claimant, pRisk);
                return true;
            }
            return false;
        }

        private static void StartRebellionWar(Kingdom pOldKingdom, Kingdom pRebel, string pWarType)
        {
            try
            {
                WarDecisionService.TryStartSystemWar(pRebel, pOldKingdom, pWarType, "general_rebellion");
            }
            catch (Exception e)
            {
                ModClass.LogWarning("General rebellion war failed: " + e.Message);
            }
        }

        private static void RecordRebellion(Actor pGeneral, Kingdom pOldKingdom, Kingdom pRebel, City pBaseCity, int pRisk)
        {
            HistoryWriter.RecordPerson(pGeneral.data.id, pRebel, pGeneral.getName(),
                PersonEvent.GENERAL_REBELLION,
                HistoryText.Actor(pGeneral) + H("aw_hist_general_based_on") +
                HistoryText.City(pBaseCity, pRebel) + H("aw_hist_general_self_rule"),
                ChronicleCategory.WAR,
                HistoryTarget.Kingdom(pRebel));

            HistoryWriter.RecordKingdom(pOldKingdom, KingdomEvent.GENERAL_REBELLION,
                HistoryText.Actor(pGeneral) + H("aw_hist_general_based_on") +
                HistoryText.City(pBaseCity, pOldKingdom) + H("aw_hist_general_rebelled"),
                HistoryTarget.Kingdom(pRebel));
            HistoryWriter.RecordKingdom(pRebel, KingdomEvent.GENERAL_REBELLION,
                HistoryText.Kingdom(pRebel) + H("aw_hist_general_founded_by") +
                HistoryText.Actor(pGeneral) + H("aw_hist_general_rebel_founded"),
                HistoryTarget.Actor(pGeneral));
            HistoryWriter.RecordCity(pBaseCity, pRebel, CityEvent.GENERAL_REBELLION,
                HistoryText.City(pBaseCity, pRebel) + H("aw_hist_general_became") +
                HistoryText.Actor(pGeneral) + H("aw_hist_general_rebel_base"),
                HistoryTarget.Actor(pGeneral));
        }

        private static void RecordPalaceCoup(Actor pGeneral, Kingdom pKingdom, int pRisk, bool pSuccess)
        {
            string eventKey = pSuccess ? "general_palace_coup_success" : "general_palace_coup_failed";
            HistoryText text = HistoryText.Actor(pGeneral) +
                               H(pSuccess ? "aw_hist_general_palace_coup_success" : "aw_hist_general_palace_coup_failed") +
                               H("aw_hist_general_risk_label") + HistoryText.PlainText(pRisk.ToString());
            HistoryWriter.RecordPerson(pGeneral.data.id, pKingdom, pGeneral.getName(), eventKey,
                text, ChronicleCategory.WAR, HistoryTarget.Kingdom(pKingdom));
            HistoryWriter.RecordKingdom(pKingdom, eventKey, text, HistoryTarget.Actor(pGeneral));
        }

        private static void RecordDefection(Actor pGeneral, Kingdom pOldKingdom, Kingdom pNewKingdom, City pCity, int pRisk)
        {
            HistoryText text = HistoryText.Actor(pGeneral) + H("aw_hist_general_with_city") +
                               HistoryText.City(pCity, pNewKingdom) +
                               H("aw_hist_general_defected_to") + HistoryText.Kingdom(pNewKingdom) +
                               H("aw_hist_general_power_risk") + HistoryText.PlainText(pRisk.ToString());
            HistoryWriter.RecordPerson(pGeneral.data.id, pNewKingdom, pGeneral.getName(), "general_defection",
                text, ChronicleCategory.WAR, HistoryTarget.Kingdom(pNewKingdom));
            HistoryWriter.RecordKingdom(pOldKingdom, "general_defection_lost", text, HistoryTarget.City(pCity));
            HistoryWriter.RecordKingdom(pNewKingdom, "general_defection_gain", text, HistoryTarget.City(pCity));
            HistoryWriter.RecordCity(pCity, pNewKingdom, "general_defection",
                HistoryText.City(pCity, pNewKingdom) + H("aw_hist_general_followed") +
                HistoryText.Actor(pGeneral) + H("aw_hist_general_changed_to") +
                HistoryText.Kingdom(pNewKingdom), HistoryTarget.Actor(pGeneral));
        }

        private static void RecordRestorationSupport(Actor pGeneral, Kingdom pKingdom, Kingdom pTarget,
            City pCity, Actor pClaimant, int pRisk)
        {
            HistoryText text = HistoryText.Actor(pGeneral) + H("aw_hist_general_supports") +
                               HistoryText.Actor(pClaimant, pClaimant?.getName() ?? "") +
                               H("aw_hist_general_restore_mid") + HistoryText.Kingdom(pTarget) +
                               H("aw_hist_general_declare_war_risk") + HistoryText.PlainText(pRisk.ToString());
            HistoryWriter.RecordPerson(pGeneral.data.id, pKingdom, pGeneral.getName(), "general_support_restoration",
                text, ChronicleCategory.WAR, pCity?.data != null ? HistoryTarget.City(pCity) : HistoryTarget.Kingdom(pTarget));
            HistoryWriter.RecordKingdom(pKingdom, "general_support_restoration",
                text, pCity?.data != null ? HistoryTarget.City(pCity) : HistoryTarget.Kingdom(pTarget));
        }

        private static bool IsAtWar(Kingdom pKingdom)
        {
            try { return pKingdom.getWars().Any(); }
            catch { return false; }
        }

        private static int CountWarriors(Kingdom pKingdom)
        {
            try { return pKingdom.countTotalWarriors(); }
            catch { return 0; }
        }

        private static int CountPopulation(Kingdom pKingdom)
        {
            int count = 0;
            foreach (City city in pKingdom.getCities())
                count += SafePop(city);
            return count;
        }

        private static int SafePop(City pCity)
        {
            try { return pCity?.getPopulationPeople() ?? 0; }
            catch { return 0; }
        }

        private static int SafeAge(Actor pActor)
        {
            try { return pActor.getAge(); }
            catch { return 0; }
        }

        private static bool RoyalGuardExists(Kingdom pKingdom)
        {
            foreach (Actor unit in pKingdom.getUnits())
                if (RoyalGuardService.IsRoyalGuard(unit)) return true;
            return false;
        }

        private static int CountOwnedNonCoreCities(Kingdom pKingdom)
        {
            int count = 0;
            foreach (City city in pKingdom.getCities())
            {
                try
                {
                    if (WarTerritoryService.GetCoreStatus(pKingdom, city).status == "owned_non_core") count++;
                }
                catch { }
            }
            return count;
        }

        private static int CountDisloyalVassals(Kingdom pKingdom)
        {
            float own = Mathf.Max(1f, VassalService.GetPowerScore(pKingdom, pIncludeVassals: false));
            int count = 0;
            foreach (Kingdom vassal in VassalService.GetVassals(pKingdom))
            {
                if (vassal?.data == null || vassal.isRekt()) continue;
                if (VassalService.GetPowerScore(vassal, pIncludeVassals: true) >= own * 0.55f) count++;
            }
            return count;
        }

        private static int MandateValueFor(Kingdom pKingdom)
        {
            try
            {
                Kingdom mandate = MandateService.GetCurrentMandateKingdom();
                return mandate?.data != null && mandate.id == pKingdom.id ? MandateService.ReadReport().mandate_value : 80;
            }
            catch { return 80; }
        }

        private static bool IsNearCapital(Actor pGeneral, Kingdom pKingdom)
        {
            City city = FiefService.GetFiefCity(pGeneral) ?? pGeneral?.city;
            return city?.data != null && pKingdom?.capital?.data != null && city == pKingdom.capital;
        }

        private static bool IsBorderFief(Actor pGeneral, Kingdom pKingdom)
        {
            City city = FiefService.GetFiefCity(pGeneral);
            return city?.data != null && pKingdom?.capital?.data != null &&
                   city != pKingdom.capital && pKingdom.countCities() > 1;
        }

        private static Kingdom FindStrongNeighbor(Actor pGeneral, Kingdom pKingdom)
        {
            if (pKingdom?.data == null || World.world?.kingdoms == null) return null;
            float own = Mathf.Max(1f, VassalService.GetPowerScore(pKingdom, pIncludeVassals: true));
            Kingdom best = null;
            float bestScore = own * 1.15f;
            Kingdom ownRoot = VassalService.GetRootSuzerain(pKingdom);
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom == pKingdom || kingdom.isRekt() || kingdom.isNeutral() || !kingdom.isCiv()) continue;
                if (VassalService.GetRootSuzerain(kingdom) == ownRoot) continue;
                float score = VassalService.GetPowerScore(kingdom, pIncludeVassals: true);
                if (score <= bestScore) continue;
                best = kingdom;
                bestScore = score;
            }
            return best;
        }

        private static Actor FindActor(long pId)
        {
            if (pId < 0 || World.world?.units == null) return null;
            try { return World.world.units.get(pId); }
            catch { return null; }
        }

        private static bool SameParentLine(Actor pA, Actor pB)
        {
            if (pA?.data == null || pB?.data == null) return false;
            long a1 = pA.data.parent_id_1;
            long a2 = pA.data.parent_id_2;
            long b1 = pB.data.parent_id_1;
            long b2 = pB.data.parent_id_2;
            return a1 > 0 && (a1 == b1 || a1 == b2) || a2 > 0 && (a2 == b1 || a2 == b2);
        }

        private static bool DifferentShi(Actor pA, Actor pB)
        {
            if (pA?.data == null || pB?.data == null) return false;
            pA.data.get(LineageKeys.SHI_ID, out long aShi, -1L);
            pB.data.get(LineageKeys.SHI_ID, out long bShi, -1L);
            return aShi >= 0 && bShi >= 0 && aShi != bShi;
        }

        private static float SafeStat(Actor pActor, string pKey)
        {
            try { return pActor.stats[pKey]; }
            catch { return 0f; }
        }
    }
}
