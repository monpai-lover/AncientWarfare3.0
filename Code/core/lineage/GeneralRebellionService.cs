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

        public static void OnKingdomRiskCheck(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || pKingdom.isNeutral()) return;
            foreach (Actor general in GeneralService.GetActiveGenerals(pKingdom))
            {
                if (general?.data == null) continue;
                int power = GeneralService.CountPersonalPower(general);
                GeneralService.UpdateTroopPower(general, power);
                int risk = CalculateRisk(general, pKingdom, power);
                if (risk >= 65) RecordHighRisk(general, pKingdom, risk);
                if (risk >= REBELLION_RISK_THRESHOLD) TryRebel(general, pKingdom, risk);
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

        private static void RecordHighRisk(Actor pGeneral, Kingdom pKingdom, int pRisk)
        {
            int year = Date.getCurrentYear();
            pGeneral.data.get(LineageKeys.GENERAL_RISK_RECORDED_YEAR, out int lastYear, -99999);
            if (year - lastYear < HIGH_RISK_RECORD_COOLDOWN) return;
            pGeneral.data.set(LineageKeys.GENERAL_RISK_RECORDED_YEAR, year);

            City city = FiefService.GetFiefCity(pGeneral) ?? pGeneral.city;
            HistoryWriter.RecordPerson(pGeneral.data.id, pKingdom, pGeneral.getName(),
                PersonEvent.GENERAL_RISK,
                HistoryText.Actor(pGeneral) + " \u62E5\u5175\u81EA\u91CD\uFF0C\u671D\u91CE\u4FA7\u76EE",
                ChronicleCategory.WAR,
                city?.data != null ? HistoryTarget.City(city) : HistoryTarget.Actor(pGeneral));

            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.GENERAL_RISK,
                HistoryText.Actor(pGeneral) + " \u62E5\u5175\u81EA\u91CD\uFF0C\u98CE\u9669 " +
                HistoryText.PlainText(pRisk.ToString()),
                HistoryTarget.Actor(pGeneral));

            if (city?.data != null)
                HistoryWriter.RecordCity(city, pKingdom, CityEvent.GENERAL_RISK,
                    HistoryText.Actor(pGeneral) + " \u5728" + HistoryText.City(city, pKingdom) + " \u62E5\u5175\u81EA\u91CD",
                    HistoryTarget.Actor(pGeneral));
        }

        private static bool TryRebel(Actor pGeneral, Kingdom pOldKingdom, int pRisk)
        {
            if (pGeneral?.data == null || pOldKingdom?.data == null) return false;
            if (IsAtWar(pOldKingdom)) return false;
            pGeneral.data.get("aw_general_rebelled_once", out bool rebelledOnce, false);
            if (rebelledOnce) return false;

            bool hadFief = FiefService.GetFiefCityId(pGeneral) >= 0;
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
                HistoryText.Actor(pGeneral) + " \u636E" + HistoryText.City(pBaseCity, pRebel) +
                " \u8D77\u5175\u81EA\u7ACB",
                ChronicleCategory.WAR,
                HistoryTarget.Kingdom(pRebel));

            HistoryWriter.RecordKingdom(pOldKingdom, KingdomEvent.GENERAL_REBELLION,
                HistoryText.Actor(pGeneral) + " \u636E" + HistoryText.City(pBaseCity, pOldKingdom) +
                " \u8D77\u5175\u53DB\u4E71",
                HistoryTarget.Kingdom(pRebel));
            HistoryWriter.RecordKingdom(pRebel, KingdomEvent.GENERAL_REBELLION,
                HistoryText.Kingdom(pRebel) + " \u7531" + HistoryText.Actor(pGeneral) + " \u8D77\u5175\u5EFA\u7ACB",
                HistoryTarget.Actor(pGeneral));
            HistoryWriter.RecordCity(pBaseCity, pRebel, CityEvent.GENERAL_REBELLION,
                HistoryText.City(pBaseCity, pRebel) + " \u6210\u4E3A" + HistoryText.Actor(pGeneral) + " \u53DB\u519B\u6839\u636E\u5730",
                HistoryTarget.Actor(pGeneral));
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
