using System;
using AncientWarfare3.core.lineage;

namespace SuccessionGovernmentRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                ExpectSuccessionTransitionRules();
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

        private static void Expect(bool pCondition, string pMessage)
        {
            if (!pCondition) throw new Exception(pMessage);
        }
    }
}
