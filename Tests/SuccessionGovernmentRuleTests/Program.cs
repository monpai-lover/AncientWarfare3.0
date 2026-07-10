using System;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

namespace SuccessionGovernmentRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                ExpectSuccessionTransitionRules();
                ExpectRepublicRules();
                ExpectFragmentationAndInheritanceRules();
                Console.WriteLine("Succession/government rule tests passed.");
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.GetType().FullName + ": " + e.Message);
                return 1;
            }
        }

        private static void ExpectSuccessionTransitionRules()
        {
            long reference = SuccessionTransitionRules.ResolveReferenceKingId(
                pCurrentKingId: -1, pCurrentKingValid: false, pPreviousKingId: 100);
            Expect(reference == 100,
                "Temporary vacancies must retain the dead king ID as succession reference.");

            int survivingBrotherTier = HeirGenerationRules.ClassifyTier(
                pIsAgnaticDescendantOfKing: true, pGenerationDelta: 1);
            Expect(survivingBrotherTier == HeirGenerationRules.TierDirectDescendant,
                "Another living son must remain a direct heir after the crown-prince branch dies.");

            Expect(SuccessionTransitionRules.IsOfficialRoleEligible(
                    pIsKing: false, pIsCityLeader: true, pIsGeneral: true,
                    pIsArmyCaptain: true, pHasFief: true),
                "Leaders, generals, captains, and fief holders must remain succession eligible.");
            Expect(!SuccessionTransitionRules.IsOfficialRoleEligible(
                    pIsKing: true, pIsCityLeader: false, pIsGeneral: false,
                    pIsArmyCaptain: false, pHasFief: false),
                "Only an actor already serving as king is excluded by office.");

            Expect(!SuccessionTransitionRules.ShouldTreatMissingHeirAsUnstable(
                    pSuccessionPending: true, pHasHeir: false),
                "The timer_new_king vacancy must not become a succession crisis.");
            Expect(SuccessionTransitionRules.ShouldBlockVanillaMassFragmentation(
                    pUsesManagedLineage: true),
                "Managed lineage kingdoms must block vanilla all-city fragmentation.");

            Expect(SuccessionTransitionRules.ShouldUseCachedHeir(
                    pSuccessionPending: true, pCachedHeirEligible: true),
                "A prepared heir must survive timer_new_king.");
            Expect(!SuccessionTransitionRules.ShouldOverwriteCachedHeir(
                    pSuccessionPending: true, pHasReferenceKing: true),
                "Read-only vacancy lookup must not overwrite aw_heir_id.");
            Expect(SuccessionTransitionRules.ShouldOverwriteCachedHeir(
                    pSuccessionPending: false, pHasReferenceKing: true),
                "An explicit refresh with a valid reference king may update aw_heir_id.");
        }

        private static void ExpectRepublicRules()
        {
            var strongest = new RepublicCandidateScore(11, diplomacy: 8, warfare: 7, stewardship: 6,
                level: 4, combatStrength: 20f, age: 30);
            var weaker = new RepublicCandidateScore(12, diplomacy: 6, warfare: 6, stewardship: 6,
                level: 9, combatStrength: 90f, age: 50);
            Expect(RepublicGovernmentRules.CompareCandidates(strongest, weaker) < 0,
                "The three governing attributes must be the primary republic election score.");

            var tieLowId = new RepublicCandidateScore(20, 6, 6, 6, 3, 20f, 30);
            var tieHighId = new RepublicCandidateScore(21, 6, 6, 6, 3, 20f, 30);
            Expect(RepublicGovernmentRules.CompareCandidates(tieLowId, tieHighId) < 0,
                "Actor ID must make exact election ties deterministic.");

            Expect(RepublicGovernmentRules.ShouldEnterRepublic(
                    pSuccessionPending: false, pHasMonarchyHeir: false, pElectableCount: 2),
                "True extinction with electable people must create a republic.");
            Expect(!RepublicGovernmentRules.ShouldEnterRepublic(
                    pSuccessionPending: true, pHasMonarchyHeir: false, pElectableCount: 2),
                "A temporary vacancy must not create a republic.");
            Expect(!RepublicGovernmentRules.ShouldEnterRepublic(
                    pSuccessionPending: false, pHasMonarchyHeir: false, pElectableCount: 0),
                "Government state must not change before an electable leader exists.");

            Expect(RepublicGovernmentRules.ShouldPreserveRepublicOnSetKing(
                    pWasRepublic: true, pWasRegisteredRepublicSuccessor: true,
                    pActorMarkedRepublicLeader: false),
                "A registered republican successor must keep republic state on accession.");
            Expect(!RepublicGovernmentRules.ShouldPreserveRepublicOnSetKing(
                    pWasRepublic: true, pWasRegisteredRepublicSuccessor: false,
                    pActorMarkedRepublicLeader: false),
                "An unrelated restored king must end republic government.");

            Expect(RepublicGovernmentRules.IsEligibleLeader(
                    pInLineageSystem: true, pIsMale: true, pIsAdult: true,
                    pIsAlive: true, pIsSlave: false, pIsKing: false),
                "Eligible nobles and office holders must not be filtered out of republic elections.");
        }

        private static void ExpectFragmentationAndInheritanceRules()
        {
            Expect(!SuccessionTransitionRules.ShouldBlockShatteredCrownEvent(
                    pUsesManagedLineage: true),
                "The explicit shattered_crown culture event must remain available.");
            Expect(KingdomPolicyInheritanceRules.SanitizeClassStateForNewKingdom(
                    pSourceClass: "republic", pDefaultClass: "default") == "default",
                "Split kingdoms must not inherit republic government wholesale.");
            Expect(KingdomPolicyInheritanceRules.SanitizeClassStateForNewKingdom(
                    pSourceClass: "aristocrat", pDefaultClass: "default") == "aristocrat",
                "Transferable class states must remain unchanged.");
        }

        private static void Expect(bool pCondition, string pMessage)
        {
            if (!pCondition) throw new Exception(pMessage);
        }
    }
}
