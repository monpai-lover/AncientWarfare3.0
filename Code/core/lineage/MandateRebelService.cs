using System;
using System.Collections.Generic;
using AncientWarfare3.content.policies;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.schools;
using AncientWarfare3.utils;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal sealed class MandateRebelReport
    {
        public int active_count;
        public string strongest_name = "";
        public float strongest_core_control;
        public string strongest_king_name = "";
    }

    internal static class MandateRebelService
    {
        private const float CLAIM_THRESHOLD = 0.65f;
        private const int MAX_COLLAPSE_REBELS = 3;
        private const int REBEL_BUFF_YEARS = 25;
        private const float MOBILIZATION_TARGET = 0.80f;
        private const int MOBILIZE_CAP_PER_YEAR = 30;
        private static int _activeClaimantCacheYear = int.MinValue;
        private static int _activeClaimantCacheKingdomCount = -1;
        private static bool _activeClaimantCacheValue;

        public static void ClearRuntime()
        {
            _activeClaimantCacheYear = int.MinValue;
            _activeClaimantCacheKingdomCount = -1;
            _activeClaimantCacheValue = false;
        }

        public static bool IsRebelKingdom(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return false;
            pKingdom.data.get(LineageKeys.MANDATE_REBEL, out bool rebel, false);
            pKingdom.data.get(LineageKeys.POLICY_CLASS_STATE, out string classState, "");
            pKingdom.data.get(LineageKeys.MANDATE_ORIGIN_TYPE, out string origin, "");
            pKingdom.data.get(LineageKeys.MANDATE_CLAIMANT_KIND, out string claimant, "");
            return MandateRebelStateRules.IsCurrentRebelGovernment(rebel, classState, origin, claimant);
        }

        public static bool IsRebelLeader(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.MANDATE_REBEL_LEADER, out bool leader, false);
            return leader || pActor.hasTrait("rebel");
        }

        public static bool HasActiveRebelClaimants()
        {
            if (World.world?.kingdoms == null) return false;
            int year = Date.getCurrentYear();
            int kingdomCount = World.world.kingdoms.list?.Count ?? -1;
            if (MandateRebelStateRules.ShouldUseActiveClaimantCache(_activeClaimantCacheYear, year,
                    _activeClaimantCacheKingdomCount, kingdomCount))
                return _activeClaimantCacheValue;

            _activeClaimantCacheYear = year;
            _activeClaimantCacheKingdomCount = kingdomCount;
            _activeClaimantCacheValue = false;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (!IsRebelKingdom(kingdom)) continue;
                _activeClaimantCacheValue = true;
                break;
            }
            return _activeClaimantCacheValue;
        }

        public static MandateRebelReport ReadReport()
        {
            var report = new MandateRebelReport();
            if (World.world?.kingdoms == null) return report;

            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (!IsRebelKingdom(kingdom)) continue;
                report.active_count++;
                float ratio = MandateService.GetCoreControlRatioFor(kingdom);
                if (ratio < report.strongest_core_control && !string.IsNullOrEmpty(report.strongest_name)) continue;
                report.strongest_core_control = ratio;
                report.strongest_name = kingdom.name ?? "";
                report.strongest_king_name = kingdom.king?.getName() ?? "";
            }

            return report;
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!IsRebelKingdom(pKingdom)) return;

            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.MANDATE_REBEL_LAST_YEAR, out int lastYear, int.MinValue);
            if (lastYear == year) return;
            pKingdom.data.set(LineageKeys.MANDATE_REBEL_LAST_YEAR, year);
            PeasantRebelRouteService.OnKingdomYear(pKingdom);
        }

        internal static void RunFoundingRouteYear(Kingdom pKingdom)
        {
            if (!IsRebelKingdom(pKingdom)) return;
            EnsureRebelGovernment(pKingdom);
            MobilizeRebelForces(pKingdom);
            TryClaimMandate(pKingdom);
        }

        internal static void RunBanditRouteYear(Kingdom pKingdom)
        {
            if (!IsRebelKingdom(pKingdom)) return;
            EnsureRebelGovernment(pKingdom);
            MobilizeRebelForces(pKingdom);
        }

        public static void OnMandateCollapse(Kingdom pMandateKingdom, string pReason)
        {
            if (pMandateKingdom?.data == null || pMandateKingdom.isRekt()) return;
            if (MandateService.HasMandateProtection(pMandateKingdom)) return;

            List<City> candidates = PickCollapseCities(pMandateKingdom);
            if (candidates.Count == 0) return;

            int target = Mathf.Clamp(Mathf.CeilToInt(candidates.Count / 3f), 1, MAX_COLLAPSE_REBELS);
            int spawned = 0;
            var oldKingdom = pMandateKingdom;
            foreach (City city in candidates)
            {
                if (spawned >= target) break;
                if (city?.data == null || city.kingdom != oldKingdom || city.isRekt()) continue;
                Actor founder = PickFounder(city);
                if (founder?.data == null) continue;
                Kingdom rebel = CreateRebelKingdom(city, founder, oldKingdom, pReason);
                if (rebel?.data == null) continue;
                spawned++;
            }

            if (spawned > 0)
            {
                MandateService.RecordMandateEvent("mandate_rebellion_wave", oldKingdom, oldKingdom.king, null,
                    -10, MandateService.ReadReport().mandate_value,
                    oldKingdom.name + " \u5929\u547D\u5D29\u89E3\uFF0C\u6CD5\u7406\u57CE\u9091\u7206\u53D1\u4E49\u519B " + spawned + " \u8D77");
            }
        }

        public static Kingdom CreateRebelKingdom(City pCity, Actor pFounder, Kingdom pOriginKingdom, string pReason)
        {
            if (pCity?.data == null || pFounder?.data == null || pOriginKingdom?.data == null) return null;

            Kingdom rebel = null;
            try
            {
                rebel = pCity.makeOwnKingdom(pFounder, pRebellion: true, pFellApart: false);
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Mandate rebel makeOwnKingdom failed: " + e.Message);
                return null;
            }

            if (rebel?.data == null) return null;
            MarkRebelKingdom(rebel, pFounder, pOriginKingdom);
            if (!PeasantRebelRouteService.InitializeAndEnter(rebel,
                    pOriginKingdom, pCity, pFounder))
                PeasantRebelRouteService.EnterFoundingFallback(rebel,
                    pOriginKingdom, pCity);

            HistoryWriter.RecordKingdom(rebel, KingdomEvent.MANDATE_REBELLION,
                HistoryText.Kingdom(rebel) +
                HistoryLocalizationRules.H("aw_hist_mandate_rebel_rose_mid") +
                HistoryText.City(pCity, rebel) +
                HistoryLocalizationRules.H("aw_hist_mandate_rebel_rose_suffix"),
                HistoryTarget.Kingdom(pOriginKingdom));
            HistoryWriter.RecordCity(pCity, rebel, CityEvent.CITY_TRANSFER,
                HistoryText.City(pCity, rebel) +
                HistoryLocalizationRules.H("aw_hist_mandate_rebel_city_mid") +
                HistoryText.Kingdom(pOriginKingdom),
                HistoryTarget.Kingdom(rebel));
            if (pFounder.data != null)
            {
                HistoryWriter.RecordPerson(pFounder.data.id, rebel, pFounder.getName(),
                    PersonEvent.MANDATE_REBEL_LEADER,
                    HistoryText.Actor(pFounder) +
                    HistoryLocalizationRules.H("aw_hist_mandate_rebel_leader"),
                    ChronicleCategory.WAR,
                    HistoryTarget.Kingdom(rebel));
            }

            MandateService.RecordMandateEvent("mandate_rebellion", rebel, pFounder, pCity, -8,
                MandateService.ReadReport().mandate_value,
                (rebel.name ?? "") +
                HistoryLocalizationRules.Text("aw_hist_mandate_rebel_rose_mid") +
                (pCity.data.name ?? "") +
                HistoryLocalizationRules.Text("aw_hist_mandate_rebel_rose_suffix"));
            return rebel;
        }

        internal static void EnterFoundingRoute(Kingdom pRebel,
            Kingdom pOriginKingdom, City pFoundingCity)
        {
            if (pRebel?.data == null || pOriginKingdom?.data == null ||
                pFoundingCity?.data == null) return;
            TryPullAlignedCities(pRebel, pOriginKingdom, pFoundingCity);
            StartExistingRebelWar(pOriginKingdom, pRebel);
        }

        public static void MarkRebelKingdom(Kingdom pKingdom, Actor pLeader, Kingdom pOriginKingdom)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.MANDATE_REBEL, true);
            pKingdom.data.set(LineageKeys.MANDATE_ORIGIN_TYPE, "rebel");
            pKingdom.data.set(LineageKeys.MANDATE_CLAIMANT_KIND, "rebel");
            pKingdom.data.set(LineageKeys.MANDATE_MAP_MARKER_KIND, "rebel_claimant");
            pKingdom.data.set(LineageKeys.POLICY_CLASS_STATE, KingdomPolicyDefs.ClassRebel);
            pKingdom.data.set(LineageKeys.MANDATE_REBEL_ORIGIN_KINGDOM_ID,
                pOriginKingdom?.id ?? -1L);
            pKingdom.data.set(LineageKeys.MANDATE_REBEL_BUFF_UNTIL, Date.getCurrentYear() + REBEL_BUFF_YEARS);
            VassalService.EnforceNoVassalRelationsForRebel(pKingdom, "rebel_government");

            Actor king = pLeader?.data != null ? pLeader : pKingdom.king;
            if (king?.data != null)
            {
                king.data.set(LineageKeys.MANDATE_REBEL_LEADER, true);
                if (!king.hasTrait("rebel")) king.addTrait("rebel");
                if (king.hasTrait(LineageKeys.TRAIT_GUIZU)) king.removeTrait(LineageKeys.TRAIT_GUIZU);
                king.data.set(LineageKeys.LINEAGE_STATUS, LineageStatus.COMMON);
                try { king.clearGraphicsFully(); } catch { }
            }
            RulerAppellationService.RefreshLivingProjection(pKingdom);
        }

        private static void TryClaimMandate(Kingdom pKingdom)
        {
            MandateReport previous = MandateService.ReadReport();
            pKingdom.data.get(LineageKeys.MANDATE_REBEL_ORIGIN_KINGDOM_ID,
                out long originId, -1L);
            Kingdom origin = FindKingdomFromRebelOrigin(pKingdom);
            float ratio = MandateService.GetCoreControlRatioFor(pKingdom);
            bool originAlive = origin?.data != null && !origin.isRekt() &&
                               origin.isCiv() && !origin.isNeutral() &&
                               SafeCountCities(origin) > 0;
            if (!MandateRebelStateRules.CanClaimFormerDynastyMandate(
                    previous.active, previous.kingdom_id, originId,
                    originAlive, IsActiveRebellionAgainst(pKingdom, origin),
                    previous.core_count, ratio, CLAIM_THRESHOLD)) return;
            if (!MandateService.TryDeclareMandate(pKingdom, "rebel_claim", "rebel", "rebel", origin)) return;

            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.MANDATE_CLAIMED,
                HistoryText.Kingdom(pKingdom) +
                HistoryLocalizationRules.H("aw_hist_mandate_rebel_claimed"),
                HistoryTarget.Kingdom(pKingdom));
            if (pKingdom.king?.data != null)
                HistoryWriter.RecordPerson(pKingdom.king.data.id, pKingdom, pKingdom.king.getName(),
                    PersonEvent.MANDATE_REBEL_LEADER,
                    HistoryText.Actor(pKingdom.king) +
                    HistoryLocalizationRules.H("aw_hist_mandate_rebel_emperor"),
                    ChronicleCategory.HONOR,
                    HistoryTarget.Kingdom(pKingdom));
            MandateService.RecordMandateEvent("mandate_rebel_claimed", pKingdom, pKingdom.king, pKingdom.capital,
                20, MandateService.ReadReport().mandate_value,
                pKingdom.name + HistoryLocalizationRules.Text("aw_hist_mandate_rebel_claimed"));
            SettleRebelGovernment(pKingdom, "mandate_claimed");
        }

        public static void OnWarEnded(War pWar, WarWinner pWinner)
        {
            if (pWar?.data == null) return;
            string type = GetWarType(pWar);
            bool rebelWar = type == MandateService.WAR_TIANMING_REBEL || pWar.getAsset()?.rebellion == true;
            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();
            if (!rebelWar && !IsRebelKingdom(attacker) && !IsRebelKingdom(defender)) return;

            SettleRebelGovernment(attacker, "rebellion_war_end");
            SettleRebelGovernment(defender, "rebellion_war_end");
        }

        public static void SettleRebelGovernment(Kingdom pKingdom, string pReason)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            if (PeasantRebelRouteService.IsBanditOrEntering(pKingdom)) return;
            pKingdom.data.get(LineageKeys.MANDATE_REBEL, out bool rebel, false);
            pKingdom.data.get(LineageKeys.POLICY_CLASS_STATE, out string classState, "");
            pKingdom.data.get(LineageKeys.MANDATE_ORIGIN_TYPE, out string origin, "");
            pKingdom.data.get(LineageKeys.MANDATE_CLAIMANT_KIND, out string claimant, "");
            if (!MandateRebelStateRules.IsCurrentRebelGovernment(rebel, classState, origin, claimant)) return;

            pKingdom.data.set(LineageKeys.MANDATE_REBEL, false);
            pKingdom.data.set(LineageKeys.MANDATE_REBEL_ORIGIN_KINGDOM_ID,
                -1L);
            pKingdom.data.set(LineageKeys.POLICY_CLASS_STATE,
                MandateRebelStateRules.SettledClassAfterRebellion(classState));
            pKingdom.data.set(LineageKeys.MANDATE_REBEL_BUFF_UNTIL, 0);
            pKingdom.data.get(LineageKeys.MANDATE_MAP_MARKER_KIND, out string marker, "");
            if (marker == "rebel_claimant") pKingdom.data.set(LineageKeys.MANDATE_MAP_MARKER_KIND, "");
            MandateService.NormalizeMapMarkerAfterRebelSettlement(pKingdom);

            foreach (Actor unit in pKingdom.getUnits())
            {
                if (unit?.data == null) continue;
                unit.data.set(LineageKeys.MANDATE_REBEL, false);
                unit.data.set(LineageKeys.MANDATE_REBEL_LEADER, false);
                if (unit.hasTrait("rebel")) unit.removeTrait("rebel");
            }

            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.MANDATE_REBELLION,
                HistoryText.Kingdom(pKingdom) +
                HistoryLocalizationRules.H("aw_hist_mandate_rebel_settled"),
                HistoryTarget.Kingdom(pKingdom));
            RulerAppellationService.RefreshLivingProjection(pKingdom);
        }

        private static List<City> PickCollapseCities(Kingdom pMandateKingdom)
        {
            var result = new List<City>();
            var scored = new List<Tuple<City, float>>();
            City capital = pMandateKingdom.capital;
            foreach (long id in MandateService.GetCurrentCoreCityIds())
            {
                City city = FindCity(id);
                if (city?.data == null || city.isRekt() || city.kingdom != pMandateKingdom) continue;
                if (city == capital || city.isCapitalCity()) continue;
                float score = 10f + ForeignOccupationService.GetResentment(city);
                try
                {
                    city.recalculateNeighbourCities();
                    if (capital != null && city.neighbours_cities_kingdom.Contains(capital)) score -= 20f;
                    if (city.hasArmy()) score += 6f;
                    if (!city.hasAnyFood()) score += 14f;
                    score += city.getPopulationPeople() * 0.15f;
                }
                catch { }
                scored.Add(Tuple.Create(city, score));
            }

            scored.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            foreach (var item in scored)
                result.Add(item.Item1);
            return result;
        }

        private static Actor PickFounder(City pCity)
        {
            if (CanFoundRebellion(pCity?.leader)) return pCity.leader;

            Actor best = null;
            float bestScore = float.MinValue;
            foreach (Actor unit in pCity.getUnits())
            {
                if (!CanFoundRebellion(unit)) continue;
                float score = ScoreFounder(unit);
                if (score <= bestScore) continue;
                bestScore = score;
                best = unit;
            }
            return best;
        }

        private static bool CanFoundRebellion(Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt() || !pActor.isAdult()) return false;
            if (pActor.asset?.is_boat == true) return false;
            if (pActor.hasTrait("figure") || pActor.hasTrait("first")) return false;
            if (SlaveService.IsSlave(pActor)) return false;
            return pActor.isUnitFitToRule() || pActor.isWarrior() || pActor.isCityLeader();
        }

        private static float ScoreFounder(Actor pActor)
        {
            float score = 0f;
            try { if (pActor.isCityLeader()) score += 40f; } catch { }
            try { if (pActor.isWarrior()) score += 20f; } catch { }
            try { score += pActor.stats["warfare"] * 3f + pActor.stats["diplomacy"] + pActor.stats["stewardship"]; } catch { }
            try { if (pActor.isSexMale()) score += 6f; } catch { }
            return score;
        }

        private static void TryPullAlignedCities(Kingdom pRebel, Kingdom pOld, City pSeed)
        {
            if (pRebel?.data == null || pOld?.data == null || pSeed?.data == null) return;
            int oldCount = SafeCountCities(pOld);
            int maxJoin = Mathf.Clamp(oldCount / 3, 0, Math.Max(1, pRebel.getMaxCities() - SafeCountCities(pRebel)));
            if (maxJoin <= 0) return;

            var options = new List<City>();
            try
            {
                pSeed.recalculateNeighbourCities();
                options.AddRange(pSeed.neighbours_cities);
            }
            catch { }
            foreach (City city in pOld.getCities())
                if (city?.data != null && !options.Contains(city)) options.Add(city);

            int joined = 0;
            foreach (City city in options)
            {
                if (joined >= maxJoin) break;
                if (city?.data == null || city.kingdom != pOld || city.isCapitalCity()) continue;
                if (ShouldJoinRebellion(city))
                {
                    city.joinAnotherKingdom(pRebel, pCaptured: false, pRebellion: true);
                    joined++;
                }
            }
        }

        private static bool ShouldJoinRebellion(City pCity)
        {
            float resentment = ForeignOccupationService.GetResentment(pCity);
            if (resentment >= 40f) return true;
            try { if (!pCity.hasAnyFood()) return true; } catch { }
            return Randy.randomChance(0.18f);
        }

        internal static void StartExistingRebelWar(Kingdom pOld,
            Kingdom pRebel)
        {
            if (pOld?.data == null || pRebel?.data == null || pOld == pRebel) return;
            WarTypeAsset asset = AssetManager.war_types_library.get(MandateService.WAR_TIANMING_REBEL)
                                 ?? WarTypeLibrary.rebellion;
            War war = FindExistingRebelWar(pOld, asset);
            if (war != null)
            {
                try
                {
                    using (WarParticipantEntrySourceScope.Open(war, pRebel,
                               WarParticipantEntrySourceKind.ScriptedJoin,
                               pOld))
                        war.joinDefenders(pRebel);
                }
                catch { }
                return;
            }

            try
            {
                war = WarDecisionService.TryStartInternalSystemWar(pOld,
                    pRebel, asset.id, "mandate_rebel");
                if (pOld.hasAlliance())
                {
                    foreach (Kingdom ally in pOld.getAlliance().kingdoms_hashset)
                    {
                        if (ally == pOld || ally?.data == null || !ally.isOpinionTowardsKingdomGood(pOld)) continue;
                        try
                        {
                            using (WarParticipantEntrySourceScope.Open(war,
                                       ally,
                                       WarParticipantEntrySourceKind.AllianceCall,
                                       pOld))
                                war?.joinAttackers(ally);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Mandate rebel war failed: " + e.Message);
            }
        }

        private static War FindExistingRebelWar(Kingdom pOld, WarTypeAsset pAsset)
        {
            try
            {
                foreach (War war in pOld.getWars())
                    if (war != null && war.isMainAttacker(pOld) && war.getAsset() == pAsset) return war;
            }
            catch { }
            return null;
        }

        private static void EnsureRebelGovernment(Kingdom pKingdom)
        {
            pKingdom.data.set(LineageKeys.MANDATE_REBEL, true);
            pKingdom.data.set(LineageKeys.MANDATE_ORIGIN_TYPE, "rebel");
            pKingdom.data.set(LineageKeys.MANDATE_CLAIMANT_KIND, "rebel");
            pKingdom.data.set(LineageKeys.MANDATE_MAP_MARKER_KIND, "rebel_claimant");
            pKingdom.data.set(LineageKeys.POLICY_CLASS_STATE, KingdomPolicyDefs.ClassRebel);
            VassalService.EnforceNoVassalRelationsForRebel(pKingdom, "rebel_government");
            if (pKingdom.king?.data != null)
            {
                pKingdom.king.data.set(LineageKeys.MANDATE_REBEL_LEADER, true);
                if (!pKingdom.king.hasTrait("rebel")) pKingdom.king.addTrait("rebel");
            }
        }

        private static void MobilizeRebelForces(Kingdom pKingdom)
        {
            int adultMale = 0;
            int warriors = 0;
            foreach (Actor unit in pKingdom.getUnits())
            {
                if (!CanMobilize(unit, pKingdom)) continue;
                adultMale++;
                if (unit.isWarrior()) warriors++;
            }

            int target = Mathf.FloorToInt(adultMale * MOBILIZATION_TARGET);
            int need = Mathf.Clamp(target - warriors, 0, MOBILIZE_CAP_PER_YEAR);
            if (need <= 0) return;

            int changed = 0;
            foreach (Actor unit in pKingdom.getUnits())
            {
                if (changed >= need) break;
                if (!CanMobilize(unit, pKingdom) || unit.isWarrior()) continue;
                try { unit.setProfession(UnitProfession.Warrior); }
                catch { continue; }
                unit.data.set(LineageKeys.MANDATE_REBEL, true);
                if (ShouldApplyRebelBuff(pKingdom) && !unit.hasTrait("rebel")) unit.addTrait("rebel");
                changed++;
            }

            if (changed > 0)
                HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.MANDATE_REBELLION,
                    HistoryText.Kingdom(pKingdom) +
                    HistoryLocalizationRules.H("aw_hist_mandate_rebel_mobilized_mid") +
                    HistoryText.PlainText(changed.ToString()) +
                    HistoryLocalizationRules.H("aw_hist_mandate_rebel_mobilized_suffix"),
                    HistoryTarget.Kingdom(pKingdom));
        }

        private static bool CanMobilize(Actor pActor, Kingdom pKingdom)
        {
            if (pActor?.data == null || pActor.kingdom != pKingdom || pActor.isRekt() || !pActor.isAdult()) return false;
            if (!HistoricalMasterVocationService.CanEnter(pActor,
                    HistoricalMasterMilitaryContext.RebelLevy)) return false;
            if (pActor.asset?.is_boat == true) return false;
            try { if (!pActor.isSexMale()) return false; } catch { }
            if (pActor.isKing() || pActor.isCityLeader()) return false;
            if (SlaveService.IsSlave(pActor) || RoyalGuardService.IsRoyalGuard(pActor)) return false;
            if (pActor.hasTrait("figure") || pActor.hasTrait("first")) return false;
            return true;
        }

        private static bool ShouldApplyRebelBuff(Kingdom pKingdom)
        {
            pKingdom.data.get(LineageKeys.MANDATE_REBEL_BUFF_UNTIL, out int until, -1);
            return until >= Date.getCurrentYear();
        }

        private static string GetWarType(War pWar)
        {
            try { return pWar?.getAsset()?.id ?? ""; }
            catch { return ""; }
        }

        private static Kingdom FindKingdomFromRebelOrigin(Kingdom pRebel)
        {
            long id = -1L;
            pRebel?.data?.get(LineageKeys.MANDATE_REBEL_ORIGIN_KINGDOM_ID,
                out id, -1L);
            if (id >= 0)
            {
                try { return World.world?.kingdoms?.get(id); } catch { }
            }
            return null;
        }

        private static bool IsActiveRebellionAgainst(Kingdom pRebel,
            Kingdom pOrigin)
        {
            if (pRebel?.data == null || pOrigin?.data == null ||
                pRebel == pOrigin) return false;
            try
            {
                foreach (War war in pRebel.getWars())
                {
                    if (war?.data == null || war.hasEnded()) continue;
                    bool pair = (war.isAttacker(pRebel) &&
                                 war.isDefender(pOrigin)) ||
                                (war.isDefender(pRebel) &&
                                 war.isAttacker(pOrigin));
                    if (!pair) continue;
                    WarTypeAsset asset = war.getAsset();
                    if (asset?.id == MandateService.WAR_TIANMING_REBEL ||
                        asset?.rebellion == true) return true;
                }
            }
            catch { }
            return false;
        }

        private static City FindCity(long pId)
        {
            if (pId < 0 || World.world?.cities == null) return null;
            try
            {
                City city = World.world.cities.get(pId);
                if (city?.data != null) return city;
            }
            catch { }
            foreach (City city in World.world.cities)
                if (city?.data != null && city.id == pId) return city;
            return null;
        }

        private static int SafeCountCities(Kingdom pKingdom)
        {
            try { return pKingdom?.countCities() ?? 0; }
            catch { return 0; }
        }
    }
}
