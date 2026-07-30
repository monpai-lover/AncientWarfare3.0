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
        pCultureIntegrated: true),
    "an integrated founding culture may inherit from the captured mother");
False(KingdomPolicySplitInheritanceRules.ShouldInheritFromSplit(
        pHasCapturedSource: true, pNewKingdomValid: true,
        pSourceValid: true, pSourceAlive: true,
        pCultureIntegrated: false),
    "an unintegrated culture remains unchanged even when splitting from Xia");
False(KingdomPolicySplitInheritanceRules.ShouldInheritFromSplit(
        pHasCapturedSource: false, pNewKingdomValid: true,
        pSourceValid: true, pSourceAlive: true,
        pCultureIntegrated: true),
    "an integrated culture does not authorize regional or parent guessing");

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

Equal(0,
    KingdomPolicySplitInheritanceRules.NormalizeInheritedXiaizationLevel(-4),
    "negative source levels normalize to none");
Equal(3,
    KingdomPolicySplitInheritanceRules.NormalizeInheritedXiaizationLevel(3),
    "split inheritance copies the exact mother level");
Equal(5,
    KingdomPolicySplitInheritanceRules.NormalizeInheritedXiaizationLevel(8),
    "corrupt source levels cannot exceed the supported maximum");

Console.WriteLine("Kingdom policy split inheritance rules passed.");
