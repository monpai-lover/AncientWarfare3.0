using AncientWarfare3.core.lineage;

Equal(true, RestorationRebellionRedirectRules.CanUseRequiredSeed(
    RestorationRebellionSeedMode.ExternalBandit, true, false, false));
Equal(false, RestorationRebellionRedirectRules.CanUseRequiredSeed(
    RestorationRebellionSeedMode.Core, true, false, false));
Equal(true, RestorationRebellionRedirectRules.CanUseRequiredSeed(
    RestorationRebellionSeedMode.Core, true, true, false));
Equal(false, RestorationRebellionRedirectRules.CanUseRequiredSeed(
    RestorationRebellionSeedMode.ExternalBandit, false, true, true));

Equal(false, RestorationRebellionRedirectRules.ShouldCountSeedAsCore(
    RestorationRebellionSeedMode.ExternalBandit, true));
Equal(true, RestorationRebellionRedirectRules.ShouldCountSeedAsCore(
    RestorationRebellionSeedMode.Core, true));
Equal(false, RestorationRebellionRedirectRules.ShouldCountSeedAsCore(
    RestorationRebellionSeedMode.Core, false));

Equal(true, RestorationRebellionRedirectRules.ShouldInspectBanditFounder(
    true, true, true));
Equal(false, RestorationRebellionRedirectRules.ShouldInspectBanditFounder(
    false, true, true));
Equal(false, RestorationRebellionRedirectRules.ShouldInspectBanditFounder(
    true, false, true));

Equal(-1, RestorationRebellionRedirectRules.CompareCoreTargets(
    25, 9, 36, 2));
Equal(1, RestorationRebellionRedirectRules.CompareCoreTargets(
    49, 1, 36, 9));
Equal(-1, RestorationRebellionRedirectRules.CompareCoreTargets(
    25, 2, 25, 9));
Equal(0, RestorationRebellionRedirectRules.CompareCoreTargets(
    25, 2, 25, 2));

Equal(true, RestorationRebellionRedirectRules.
    ShouldRetryCommittedInitialization(
        RestorationRebellionSeedMode.ExternalBandit,
        identityCommitted: true, contextValid: true));
Equal(false, RestorationRebellionRedirectRules.
    ShouldRetryCommittedInitialization(
        RestorationRebellionSeedMode.Core,
        identityCommitted: true, contextValid: true));
Equal(false, RestorationRebellionRedirectRules.
    ShouldRetryCommittedInitialization(
        RestorationRebellionSeedMode.ExternalBandit,
        identityCommitted: true, contextValid: false));

Console.WriteLine("Restoration bandit redirect rules passed.");

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"Expected {expected}, got {actual}");
}
