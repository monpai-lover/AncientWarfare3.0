using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class CourtBorderPetitionService
    {
        public static void OnKingdomYear(Kingdom pSuzerain)
        {
            if (!IsValidIndependentRealm(pSuzerain)) return;
            int year = CurrentYear();
            pSuzerain.data.get(LineageKeys.COURT_BORDER_PETITION_LAST_YEAR,
                out int lastYear, -1);
            if (lastYear == year) return;
            pSuzerain.data.set(LineageKeys.COURT_BORDER_PETITION_LAST_YEAR,
                year);

            CourtBorderCommandLaw law =
                CourtAuxiliaryLawService.GetBorderCommandLaw(pSuzerain);
            if (!CourtAuxiliaryLawRules.AllowsBorderPetitions(law) ||
                HasEnemies(pSuzerain) ||
                DiplomaticWarDeclarationService.HasPending(pSuzerain)) return;

            CourtDirectionSnapshot direction =
                CourtDirectionService.ReadCached(pSuzerain);
            int candidates = 0;
            bool issued = TryVassalPetitions(pSuzerain, law, direction,
                ref candidates);
            if (!issued && candidates <
                CourtAuxiliaryLawRules.MaximumPetitionCandidatesPerYear)
                TryBorderGeneralPetitions(pSuzerain, law, direction,
                    ref candidates);
        }

        private static bool TryVassalPetitions(Kingdom pSuzerain,
            CourtBorderCommandLaw pLaw, CourtDirectionSnapshot pDirection,
            ref int pCandidates)
        {
            if (VassalService.GetDirectVassalCount(pSuzerain) <= 0)
                return false;
            List<Kingdom> kingdoms = World.world?.kingdoms?.list;
            int count = kingdoms?.Count ?? 0;
            if (count == 0) return false;

            pSuzerain.data.get(LineageKeys.COURT_BORDER_VASSAL_CURSOR,
                out int rawCursor, 0);
            int cursor = NormalizeCursor(rawCursor, count);
            int inspectionLimit = Math.Min(count,
                CourtAuxiliaryLawRules.MaximumVassalSlotsInspectedPerYear);
            int inspected = 0;
            int vassalCandidates = 0;
            bool issued = false;
            while (inspected < inspectionLimit &&
                   vassalCandidates <
                   CourtAuxiliaryLawRules.MaximumVassalPetitionCandidatesPerYear &&
                   pCandidates <
                   CourtAuxiliaryLawRules.MaximumPetitionCandidatesPerYear)
            {
                Kingdom candidate = kingdoms[(cursor + inspected) % count];
                inspected++;
                bool directVassal = candidate?.data != null &&
                    VassalService.GetSuzerainId(candidate) == pSuzerain.id;
                if (!IsValidRealm(candidate) || candidate == pSuzerain ||
                    !directVassal) continue;
                if (!TryFindRequestTarget(candidate, pSuzerain,
                        out Kingdom target)) continue;
                vassalCandidates++;
                pCandidates++;
                if (!TryIssue(pSuzerain, candidate, candidate.king,
                        target, pLaw, pDirection)) continue;
                issued = true;
                break;
            }

            pSuzerain.data.set(LineageKeys.COURT_BORDER_VASSAL_CURSOR,
                (cursor + inspected) % count);
            return issued;
        }

        private static bool TryBorderGeneralPetitions(Kingdom pSuzerain,
            CourtBorderCommandLaw pLaw, CourtDirectionSnapshot pDirection,
            ref int pCandidates)
        {
            List<City> cities = pSuzerain.cities;
            int count = cities?.Count ?? 0;
            if (count == 0) return false;

            pSuzerain.data.get(LineageKeys.COURT_BORDER_CITY_CURSOR,
                out int rawCursor, 0);
            int cursor = NormalizeCursor(rawCursor, count);
            int inspected = 0;
            int generalCandidates = 0;
            bool issued = false;
            while (inspected < Math.Min(count,
                       CourtAuxiliaryLawRules.MaximumBorderGeneralCandidatesPerYear) &&
                   generalCandidates <
                   CourtAuxiliaryLawRules.MaximumBorderGeneralCandidatesPerYear &&
                   pCandidates <
                   CourtAuxiliaryLawRules.MaximumPetitionCandidatesPerYear)
            {
                City city = cities[(cursor + inspected) % count];
                inspected++;
                Actor requester = BorderGeneral(city, pSuzerain);
                if (requester?.data == null ||
                    !TryFindExternalNeighbor(city, pSuzerain,
                        out Kingdom target)) continue;
                generalCandidates++;
                pCandidates++;
                if (!TryIssue(pSuzerain, pSuzerain, requester, target, pLaw,
                        pDirection)) continue;
                issued = true;
                break;
            }

            pSuzerain.data.set(LineageKeys.COURT_BORDER_CITY_CURSOR,
                (cursor + inspected) % count);
            return issued;
        }

        private static bool TryFindRequestTarget(Kingdom pRequester,
            Kingdom pSuzerain, out Kingdom pTarget)
        {
            pTarget = null;
            List<City> cities = pRequester?.cities;
            int count = cities?.Count ?? 0;
            if (count == 0) return false;
            pRequester.data.get(LineageKeys.COURT_BORDER_REQUEST_CITY_CURSOR,
                out int rawCursor, 0);
            int cursor = NormalizeCursor(rawCursor, count);
            City city = cities[cursor];
            pRequester.data.set(LineageKeys.COURT_BORDER_REQUEST_CITY_CURSOR,
                (cursor + 1) % count);
            return TryFindExternalNeighbor(city, pSuzerain, out pTarget);
        }

        private static bool TryFindExternalNeighbor(City pCity,
            Kingdom pSuzerain, out Kingdom pTarget)
        {
            pTarget = null;
            if (pCity?.data == null || pCity.isRekt() ||
                pCity.neighbours_kingdoms == null) return false;
            foreach (Kingdom neighbor in pCity.neighbours_kingdoms)
            {
                if (!IsValidRealm(neighbor) || neighbor == pSuzerain ||
                    VassalService.GetRootSuzerain(neighbor) == pSuzerain ||
                    HasEnemies(neighbor)) continue;
                pTarget = neighbor;
                return true;
            }
            return false;
        }

        private static Actor BorderGeneral(City pCity, Kingdom pSuzerain)
        {
            if (pCity?.data == null || pCity.isRekt() ||
                pCity.kingdom != pSuzerain) return null;
            try
            {
                Actor captain = pCity.hasArmy()
                    ? pCity.getArmy()?.getCaptain()
                    : null;
                if (IsValidRequester(captain, pSuzerain)) return captain;
            }
            catch { }
            Actor leader = pCity.leader;
            return IsValidRequester(leader, pSuzerain) &&
                   GeneralService.IsActiveGeneralFast(leader)
                ? leader
                : null;
        }

        private static bool TryIssue(Kingdom pSuzerain,
            Kingdom pRequesterKingdom, Actor pRequester, Kingdom pTarget,
            CourtBorderCommandLaw pLaw, CourtDirectionSnapshot pDirection)
        {
            if (!IsValidIndependentRealm(pSuzerain) ||
                !IsValidRealm(pRequesterKingdom) || !IsValidRealm(pTarget) ||
                pTarget == pSuzerain ||
                VassalService.GetRootSuzerain(pTarget) == pSuzerain ||
                HasEnemies(pTarget) ||
                DiplomaticWarDeclarationService.HasPending(pSuzerain))
                return false;

            List<WarTerritoryService.WarTargetOption> options =
                WarTerritoryService.BuildTargetOptions(pSuzerain, pTarget);
            WarTerritoryService.WarTargetOption option = null;
            for (int index = 0; index < options.Count; index++)
            {
                WarTerritoryService.WarTargetOption current = options[index];
                if (current == null ||
                    current.goal_type == WarTerritoryService.GOAL_NO_CB ||
                    current.goal_type == WarTerritoryService.GOAL_INDEPENDENCE)
                    continue;
                option = current;
                break;
            }
            if (option == null) return false;

            float ownPower = VassalService.GetPowerScore(pSuzerain, pIncludeVassals: false);
            if (pRequesterKingdom != pSuzerain)
                ownPower += VassalService.GetPowerScore(pRequesterKingdom,
                    pIncludeVassals: false) * 0.6f;
            float targetPower = VassalService.GetPowerScore(pTarget,
                pIncludeVassals: false);
            int score = CourtAuxiliaryLawRules.BorderPetitionScore(pLaw,
                ownPower, targetPower, Opinion(pSuzerain, pTarget),
                pDirection?.Aggression ?? 0.5f,
                pDirection?.War ?? 0.5f,
                pDirection?.Peace ?? 0.5f);
            if (!CourtAuxiliaryLawRules.ShouldApproveBorderPetition(pLaw,
                    score)) return false;
            if (!DiplomaticWarDeclarationService.Issue(pSuzerain, option))
                return false;
            ChronicleEvents.OnBorderPetitionApproved(pSuzerain,
                pRequesterKingdom, pRequester, pTarget, option.label);
            return true;
        }

        private static bool IsValidIndependentRealm(Kingdom pKingdom)
        {
            return IsValidRealm(pKingdom) &&
                   VassalService.GetSuzerainId(pKingdom) < 0;
        }

        private static bool IsValidRealm(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   pKingdom.isCiv() && !pKingdom.isNeutral();
        }

        private static bool IsValidRequester(Actor pActor,
            Kingdom pKingdom)
        {
            return pActor?.data != null && pActor.kingdom == pKingdom &&
                   pActor.isAlive() && !pActor.isRekt();
        }

        private static bool HasEnemies(Kingdom pKingdom)
        {
            try { return pKingdom?.hasEnemies() == true; }
            catch { return false; }
        }

        private static int Opinion(Kingdom pSource, Kingdom pTarget)
        {
            try
            {
                return World.world.diplomacy.getOpinion(pSource, pTarget)
                    .total;
            }
            catch { return 0; }
        }

        private static int NormalizeCursor(int pCursor, int pCount)
        {
            if (pCount <= 0) return 0;
            int cursor = pCursor % pCount;
            return cursor < 0 ? cursor + pCount : cursor;
        }

        private static int CurrentYear()
        {
            try { return Date.getCurrentYear(); }
            catch { return 0; }
        }
    }
}
