using AncientWarfare3.core.policy;

static void True(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

static void False(bool value, string message)
{
    True(!value, message);
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{message}: expected {expected}, got {actual}");
}

True(KingdomPolicySplitInheritanceRules.ShouldCaptureSplitSource(
        pRebellion: true, pFellApart: false,
        pIsIdentityRestoration: false, pFounderValid: true,
        pSourceValid: true, pSourceAlive: true),
    "a rebellion captures its exact living mother kingdom");
True(KingdomPolicySplitInheritanceRules.ShouldCaptureSplitSource(
        pRebellion: false, pFellApart: true,
        pIsIdentityRestoration: false, pFounderValid: true,
        pSourceValid: true, pSourceAlive: true),
    "a vanilla kingdom collapse split captures its mother kingdom");
False(KingdomPolicySplitInheritanceRules.ShouldCaptureSplitSource(
        pRebellion: false, pFellApart: false,
        pIsIdentityRestoration: false, pFounderValid: true,
        pSourceValid: true, pSourceAlive: true),
    "ordinary kingdom creation is not a split inheritance source");
False(KingdomPolicySplitInheritanceRules.ShouldCaptureSplitSource(
        pRebellion: true, pFellApart: false,
        pIsIdentityRestoration: true, pFounderValid: true,
        pSourceValid: true, pSourceAlive: true),
    "identity restoration never enters new-kingdom inheritance");

True(KingdomPolicySplitInheritanceRules.ShouldInheritFromSplit(
        pHasCapturedSource: true, pNewKingdomValid: true,
        pSourceValid: true, pSourceAlive: true,
        pChildHasPolicyProfile: true),
    "a Xia-profile split may inherit from the captured mother");
True(KingdomPolicySplitInheritanceRules.ShouldInheritFromSplit(
        pHasCapturedSource: true, pNewKingdomValid: true,
        pSourceValid: true, pSourceAlive: true,
        pChildHasPolicyProfile: true),
    "a non-Xia western-profile split inherits its mother's progress");
False(KingdomPolicySplitInheritanceRules.ShouldInheritFromSplit(
        pHasCapturedSource: true, pNewKingdomValid: true,
        pSourceValid: true, pSourceAlive: true,
        pChildHasPolicyProfile: false),
    "a non-civilized split without an AW3 profile cannot inherit nodes");
False(KingdomPolicySplitInheritanceRules.ShouldInheritFromSplit(
        pHasCapturedSource: false, pNewKingdomValid: true,
        pSourceValid: true, pSourceAlive: true,
        pChildHasPolicyProfile: true),
    "a valid profile does not authorize regional or parent guessing");

False(KingdomPolicySplitInheritanceRules.ShouldMarkCultureIntegrated(
        pNativeXiaCulture: false, pPersistedXiaizationLevel: 3),
    "temporary Xia contact and adopted rites do not mark a culture");
True(KingdomPolicySplitInheritanceRules.ShouldMarkCultureIntegrated(
        pNativeXiaCulture: false, pPersistedXiaizationLevel: 4),
    "persisted Xia institutions migrate an old culture marker");
False(KingdomPolicySplitInheritanceRules.ShouldMarkCultureIntegrated(
        pNativeXiaCulture: false, pPersistedXiaizationLevel: 0),
    "a policy key without the canonical Xiaization level cannot mark a possibly changed culture");
True(KingdomPolicySplitInheritanceRules.ShouldMarkCultureIntegrated(
        pNativeXiaCulture: true, pPersistedXiaizationLevel: 0),
    "native Xia culture projects the same authoritative marker");
False(KingdomPolicySplitInheritanceRules.ShouldMarkCultureFullyIntegrated(4),
    "Xia institutions alone do not mark full Xia entry");
True(KingdomPolicySplitInheritanceRules.ShouldMarkCultureFullyIntegrated(5),
    "the completed Xiaized-dynasty level marks full Xia entry");
True(KingdomPolicySplitInheritanceRules.ShouldMarkCultureFullyIntegrated(8),
    "a corrupt high persisted level still restores the one-way full marker");

Equal(0,
    KingdomPolicySplitInheritanceRules.NormalizeInheritedXiaizationLevel(-4),
    "negative source levels normalize to none");
Equal(3,
    KingdomPolicySplitInheritanceRules.NormalizeInheritedXiaizationLevel(3),
    "split inheritance copies the exact mother level");
Equal(5,
    KingdomPolicySplitInheritanceRules.NormalizeInheritedXiaizationLevel(8),
    "corrupt source levels cannot exceed the supported maximum");

Equal("western_feudal",
    KingdomPolicySplitInheritanceRules.ResolveInheritedGovernmentState(
        "western_general", "western_feudal"),
    "a western child keeps a valid western government institution");
Equal("default",
    KingdomPolicySplitInheritanceRules.ResolveInheritedGovernmentState(
        "xia", "western_feudal"),
    "a child that entered Xia cannot retain a western government state");
Equal("default",
    KingdomPolicySplitInheritanceRules.ResolveInheritedGovernmentState(
        "western_general", "invalid_state"),
    "unknown inherited government states are repaired");

Equal(15,
    KingdomPolicySplitInheritanceRules.ResolveInheritedRoyalAuthority(
        "western_general", "western_general", 30),
    "a western split inherits half of the mother realm's authority reserve");
Equal(0,
    KingdomPolicySplitInheritanceRules.ResolveInheritedRoyalAuthority(
        "xia", "western_general", 30),
    "cross-profile splits do not retain western royal authority");
Equal(0,
    KingdomPolicySplitInheritanceRules.ResolveInheritedRoyalAuthority(
        "western_general", "western_general", -20),
    "invalid negative authority cannot be inherited");

Console.WriteLine("Kingdom policy split inheritance rules passed.");
