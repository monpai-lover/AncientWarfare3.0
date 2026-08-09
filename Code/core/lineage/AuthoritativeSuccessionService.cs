using AncientWarfare3.core.asyncwork;

namespace AncientWarfare3.core.lineage
{
    internal static class AuthoritativeSuccessionService
    {
        private static readonly SuccessionFallbackAttemptState
            FallbackAttempts = new SuccessionFallbackAttemptState();

        internal static Actor EnsureRegisteredCandidate(Kingdom pKingdom,
            Actor pPredecessor)
        {
            if (pKingdom?.data == null || pPredecessor?.data == null)
                return null;

            Actor registered = RepublicGovernmentService.IsRepublic(pKingdom)
                ? RepublicGovernmentService.GetRegisteredSuccessor(pKingdom)
                : HeirService.PeekRegisteredHeir(pKingdom);
            if (registered?.data != null) return registered;

            var key = new KingSuccessionKey(AWAsyncRuntime.WorldGeneration,
                pKingdom.id, pPredecessor.data.id);
            if (!FallbackAttempts.TryBegin(key)) return null;

            if (RepublicGovernmentService.IsRepublic(pKingdom))
                return RepublicGovernmentService.ResolveRulerForVacancy(
                    pKingdom);

            HeirService.RefreshHeir(pKingdom);
            return HeirService.PeekRegisteredHeir(pKingdom);
        }

        internal static void OnSuccessorInstalled(Kingdom pKingdom,
            Actor pPredecessor)
        {
            if (pKingdom?.data == null || pPredecessor?.data == null) return;
            FallbackAttempts.Complete(new KingSuccessionKey(
                AWAsyncRuntime.WorldGeneration, pKingdom.id,
                pPredecessor.data.id));
        }

        internal static void Reset()
        {
            FallbackAttempts.Clear();
        }
    }
}
