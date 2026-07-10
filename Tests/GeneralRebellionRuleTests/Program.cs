using System;
using AncientWarfare3.content.figures;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

namespace GeneralRebellionRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            int stable = GeneralRebellionRules.CalculateKingdomCrisis(
                weakKingScore: 5, childOrOldRuler: false, successionUnstable: false,
                recentWarDefeat: false, capitalThreatened: false, nonCoreCityCount: 0,
                disloyalVassalCount: 0, mandateValue: 80, hasRoyalGuard: true);
            if (stable != 0) throw new Exception("Expected stable kingdom crisis to clamp to zero.");

            int crisis = GeneralRebellionRules.CalculateKingdomCrisis(
                weakKingScore: 30, childOrOldRuler: true, successionUnstable: true,
                recentWarDefeat: true, capitalThreatened: true, nonCoreCityCount: 3,
                disloyalVassalCount: 2, mandateValue: 10, hasRoyalGuard: false);
            if (crisis < 95) throw new Exception("Expected weak kingdom crisis to become severe.");

            ExpectBranch("restoration", GeneralRebellionBranch.SupportRestoration,
                crisis: 80, personalRisk: 70, hasFief: true, nearCapital: false,
                borderFief: false, strongNeighbor: false, hasRestorationClaim: true);
            ExpectBranch("defect", GeneralRebellionBranch.DefectToNeighbor,
                crisis: 75, personalRisk: 90, hasFief: true, nearCapital: false,
                borderFief: true, strongNeighbor: true, hasRestorationClaim: false);
            ExpectBranch("coup", GeneralRebellionBranch.PalaceCoup,
                crisis: 60, personalRisk: 85, hasFief: false, nearCapital: true,
                borderFief: false, strongNeighbor: false, hasRestorationClaim: false);
            ExpectBranch("fief", GeneralRebellionBranch.FiefIndependence,
                crisis: 65, personalRisk: 78, hasFief: true, nearCapital: false,
                borderFief: false, strongNeighbor: false, hasRestorationClaim: false);
            ExpectBranch("direct", GeneralRebellionBranch.DirectMilitaryRebellion,
                crisis: 40, personalRisk: 95, hasFief: false, nearCapital: false,
                borderFief: false, strongNeighbor: false, hasRestorationClaim: false);
            ExpectBranch("none", GeneralRebellionBranch.None,
                crisis: 40, personalRisk: 55, hasFief: true, nearCapital: false,
                borderFief: false, strongNeighbor: false, hasRestorationClaim: false);

            if (!FiefGrantRules.CanGrantToGeneral(isGeneral: true, merit: 45))
                throw new Exception("Expected meritorious general to qualify for fief grant.");
            if (FiefGrantRules.CanGrantToGeneral(isGeneral: true, merit: 44))
                throw new Exception("Expected low-merit general to be blocked from fief grant.");
            if (FiefGrantRules.CanGrantToGeneral(isGeneral: false, merit: 100))
                throw new Exception("Expected non-general to be blocked from fief grant.");

            if (SoldierRetirementRules.CanConsiderForRetirement(isSupportedActor: true,
                    isRekt: false, isWarrior: true, alreadyRetired: false, isGeneral: true,
                    isFiefHolder: false))
                throw new Exception("Expected active general to be excluded from retirement.");
            if (SoldierRetirementRules.CanConsiderForRetirement(isSupportedActor: true,
                    isRekt: false, isWarrior: true, alreadyRetired: false, isGeneral: false,
                    isFiefHolder: true))
                throw new Exception("Expected fief holder to be excluded from retirement.");
            if (!SoldierRetirementRules.CanConsiderForRetirement(isSupportedActor: true,
                    isRekt: false, isWarrior: true, alreadyRetired: false, isGeneral: false,
                    isFiefHolder: false))
                throw new Exception("Expected ordinary warrior to remain eligible for retirement checks.");
            if (SoldierRetirementRules.CanConsiderForRetirement(isSupportedActor: true,
                    isRekt: false, isWarrior: true, alreadyRetired: false, isGeneral: false,
                    isFiefHolder: false, isRoyalGuard: true))
                throw new Exception("Expected royal guards to be excluded from ordinary retirement checks.");

            if (!FiefMilitaryRules.ShouldApplyFiefSoldierTrait(activeFief: true, isWarrior: true,
                    alreadyHasTrait: false, isSlave: false, isRoyalGuard: false))
                throw new Exception("Expected ordinary new soldier from active fief to receive fief soldier trait.");
            if (FiefMilitaryRules.ShouldApplyFiefSoldierTrait(activeFief: false, isWarrior: true,
                    alreadyHasTrait: false, isSlave: false, isRoyalGuard: false))
                throw new Exception("Expected non-fief soldier to skip fief soldier trait.");
            if (FiefMilitaryRules.ShouldApplyFiefSoldierTrait(activeFief: true, isWarrior: true,
                    alreadyHasTrait: false, isSlave: true, isRoyalGuard: false))
                throw new Exception("Expected slave soldier to skip fief soldier trait.");

            if (FiefMilitaryRules.BuildFiefArmyName("淄川", 0) != "淄川虎贲军")
                throw new Exception("Expected fief army id 0 to use Huben name.");
            if (FiefMilitaryRules.BuildFiefArmyName("淄川", 1) != "淄川鹰扬军")
                throw new Exception("Expected fief army id 1 to use Yingyang name.");
            if (FiefMilitaryRules.BuildFiefArmyName("", 0) != "封地虎贲军")
                throw new Exception("Expected unnamed fief city to use fallback name.");
            if (!FiefMilitaryRules.ShouldRenameFiefArmy(activeFief: true, isSlaveArmy: false, isRoyalGuardArmy: false))
                throw new Exception("Expected normal active fief army to receive fief name.");
            if (FiefMilitaryRules.ShouldRenameFiefArmy(activeFief: false, isSlaveArmy: false, isRoyalGuardArmy: false))
                throw new Exception("Expected non-fief army to keep existing name.");
            if (FiefMilitaryRules.ShouldRenameFiefArmy(activeFief: true, isSlaveArmy: true, isRoyalGuardArmy: false))
                throw new Exception("Expected slave army name to take priority over fief name.");
            if (FiefMilitaryRules.ShouldRenameFiefArmy(activeFief: true, isSlaveArmy: false, isRoyalGuardArmy: true))
                throw new Exception("Expected royal guard name to take priority over fief name.");

            if (!FiefMilitaryRules.ShouldEnforceFiefCommand(activeFief: true, generalAlive: true,
                    generalInKingdom: true, generalIsSlave: false, generalIsKing: false))
                throw new Exception("Expected active valid fief general to command fief city and army.");
            if (FiefMilitaryRules.ShouldEnforceFiefCommand(activeFief: false, generalAlive: true,
                    generalInKingdom: true, generalIsSlave: false, generalIsKing: false))
                throw new Exception("Expected non-fief city to skip fief command enforcement.");
            if (FiefMilitaryRules.ShouldEnforceFiefCommand(activeFief: true, generalAlive: false,
                    generalInKingdom: true, generalIsSlave: false, generalIsKing: false))
                throw new Exception("Expected dead general to skip fief command enforcement.");
            if (FiefMilitaryRules.ShouldEnforceFiefCommand(activeFief: true, generalAlive: true,
                    generalInKingdom: false, generalIsSlave: false, generalIsKing: false))
                throw new Exception("Expected foreign general to skip fief command enforcement.");
            if (FiefMilitaryRules.ShouldEnforceFiefCommand(activeFief: true, generalAlive: true,
                    generalInKingdom: true, generalIsSlave: true, generalIsKing: false))
                throw new Exception("Expected enslaved general to skip fief command enforcement.");
            if (FiefMilitaryRules.ShouldEnforceFiefCommand(activeFief: true, generalAlive: true,
                    generalInKingdom: true, generalIsSlave: false, generalIsKing: true))
                throw new Exception("Expected king to skip fief command enforcement.");

            if (LineageBranchRules.ShouldFoundKingBranch(
                    validKingdom: true, isXiaKing: true, wasHeir: false, isCurrentHeir: false,
                    isDirectSuccessionFromPreviousKing: true, isCollateralRestoration: false,
                    hasLineage: true, hasShi: true,
                    isHistoricalFigure: false, isLineageRootFounder: false,
                    aliveInCurrentShi: 8, minAliveForNewBranch: 4,
                    currentKingdomId: 12, originKingdomId: 3, alreadyFoundedForKingdom: false,
                    cadetGenerationDistance: 6, minCadetDistanceForBranch: 4))
                throw new Exception("Expected direct parent-child royal succession to keep the main branch.");

            if (LineageBranchRules.ShouldFoundKingBranch(
                    validKingdom: true, isXiaKing: true, wasHeir: true, isCurrentHeir: false,
                    isDirectSuccessionFromPreviousKing: false, isCollateralRestoration: false,
                    hasLineage: true, hasShi: true,
                    isHistoricalFigure: false, isLineageRootFounder: false,
                    aliveInCurrentShi: 8, minAliveForNewBranch: 4,
                    currentKingdomId: 12, originKingdomId: 3, alreadyFoundedForKingdom: false,
                    cadetGenerationDistance: 6, minCadetDistanceForBranch: 4))
                throw new Exception("Expected registered heir succession to keep the main branch.");

            if (!LineageBranchRules.ShouldFoundKingBranch(
                    validKingdom: true, isXiaKing: true, wasHeir: false, isCurrentHeir: false,
                    isDirectSuccessionFromPreviousKing: false, isCollateralRestoration: false,
                    hasLineage: true, hasShi: true,
                    isHistoricalFigure: false, isLineageRootFounder: false,
                    aliveInCurrentShi: 8, minAliveForNewBranch: 4,
                    currentKingdomId: 12, originKingdomId: 3, alreadyFoundedForKingdom: false,
                    cadetGenerationDistance: 6, minCadetDistanceForBranch: 4))
                throw new Exception("Expected non-succession king in a foreign/new kingdom to found a branch.");

            if (!HistoricalFigureMinimapRules.ShouldDrawIcon(
                    isAlive: true, isInMagnet: false, hasCurrentTile: true, hasVisibleZone: true,
                    isKing: false, isCityLeader: false, hasFigureTrait: true, hasFirstTrait: false))
                throw new Exception("Expected live figure trait to draw historical figure minimap icon.");
            if (!HistoricalFigureMinimapRules.ShouldDrawIcon(
                    isAlive: true, isInMagnet: false, hasCurrentTile: true, hasVisibleZone: true,
                    isKing: false, isCityLeader: false, hasFigureTrait: false, hasFirstTrait: true))
                throw new Exception("Expected live first trait to draw historical figure minimap icon.");
            if (HistoricalFigureMinimapRules.ShouldDrawIcon(
                    isAlive: true, isInMagnet: false, hasCurrentTile: true, hasVisibleZone: true,
                    isKing: true, isCityLeader: false, hasFigureTrait: true, hasFirstTrait: true))
                throw new Exception("Expected historical figure king to keep native king icon without extra figure minimap icon.");
            if (HistoricalFigureMinimapRules.ShouldDrawIcon(
                    isAlive: true, isInMagnet: false, hasCurrentTile: true, hasVisibleZone: true,
                    isKing: false, isCityLeader: true, hasFigureTrait: true, hasFirstTrait: true))
                throw new Exception("Expected historical figure city leader to keep native leader icon without extra figure minimap icon.");
            if (HistoricalFigureMinimapRules.ShouldDrawIcon(
                    isAlive: true, isInMagnet: false, hasCurrentTile: true, hasVisibleZone: true,
                    isKing: false, isCityLeader: false, hasFigureTrait: false, hasFirstTrait: false))
                throw new Exception("Expected ordinary favorite units without figure traits to skip figure minimap icon.");

            if (!AWMapModeButtonRules.ShouldSuppressNmlAutoToggle(mapModeSwitch: true, hasCustomToggleAction: true))
                throw new Exception("Expected custom mapmode toggle actions to suppress NML's extra auto toggle.");
            if (AWMapModeButtonRules.ShouldSuppressNmlAutoToggle(mapModeSwitch: false, hasCustomToggleAction: true))
                throw new Exception("Expected non-mapmode toggle buttons to keep NML's normal toggle behavior.");
            if (AWMapModeButtonRules.ShouldSuppressNmlAutoToggle(mapModeSwitch: true, hasCustomToggleAction: false))
                throw new Exception("Expected mapmode buttons without a custom toggle action to keep NML's normal toggle behavior.");

            var mergedRelations = FamilyTreeRelationRules.MergeRelationIds(
                new long[] { 42, -1, 7 },
                new long[] { 7, 42, 9 },
                new long[] { 9, 0 });
            if (mergedRelations.Count != 4 ||
                mergedRelations[0] != 42 || mergedRelations[1] != 7 ||
                mergedRelations[2] != 9 || mergedRelations[3] != 0)
                throw new Exception("Expected family tree relation ids to merge all valid sources without duplicates.");

            var normalizedParents = FamilyTreeRelationRules.MergeParentSlots(
                currentSlot1: -1, currentSlot2: 42,
                fallbackSlot1: 42, fallbackSlot2: 7);
            if (normalizedParents.slot1 != 42 || normalizedParents.slot2 != 7)
                throw new Exception("Expected missing parent slots to be filled from explicit parent objects without duplicates.");

            normalizedParents = FamilyTreeRelationRules.MergeParentSlots(
                currentSlot1: 3, currentSlot2: 4,
                fallbackSlot1: 8, fallbackSlot2: 9);
            if (normalizedParents.slot1 != 3 || normalizedParents.slot2 != 4)
                throw new Exception("Expected existing valid parent slots to be preserved.");

            Console.WriteLine("General rebellion rule tests passed.");
            return 0;
        }

        private static void ExpectBranch(string label, GeneralRebellionBranch expected,
            int crisis, int personalRisk, bool hasFief, bool nearCapital, bool borderFief,
            bool strongNeighbor, bool hasRestorationClaim)
        {
            GeneralRebellionBranch actual = GeneralRebellionRules.SelectBranch(crisis, personalRisk,
                hasFief, nearCapital, borderFief, strongNeighbor, hasRestorationClaim);
            if (actual != expected)
                throw new Exception($"Expected {label} branch {expected}, got {actual}.");
        }
    }
}
