using AncientWarfare3.content;
using AncientWarfare3.core.lineage;

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
}

static void True(bool value, string name)
{
    if (!value) throw new InvalidOperationException($"{name}: expected true");
}

Equal(RoyalLineageSourceKind.Self,
    RoyalLineageResolutionRules.Resolve(true, true, true, true, true),
    "self branch wins");
Equal(RoyalLineageSourceKind.Father,
    RoyalLineageResolutionRules.Resolve(false, true, true, true, true),
    "father branch precedes royal and sibling");
Equal(RoyalLineageSourceKind.CurrentRoyal,
    RoyalLineageResolutionRules.Resolve(false, false, true, true, true),
    "related current royal precedes sibling");
Equal(RoyalLineageSourceKind.Sibling,
    RoyalLineageResolutionRules.Resolve(false, false, true, false, true),
    "unrelated current royal is ignored");
Equal(RoyalLineageSourceKind.Create,
    RoyalLineageResolutionRules.Resolve(false, false, false, false, false),
    "new branch is last resort");
True(RoyalLineageResolutionRules.SharesKnownFather(17, 17),
    "brothers with the same known father are related");
Equal(false, RoyalLineageResolutionRules.SharesKnownFather(-1, -1),
    "unknown parents never create a false sibling relation");
Equal(false, RoyalLineageResolutionRules.SharesKnownFather(17, 18),
    "different fathers remain separate");

True(XiaNameRepairRules.IsInvalidGeneratedMetaName("NAME"), "NAME is invalid");
True(XiaNameRepairRules.IsInvalidGeneratedMetaName("#NO_NAME#"), "#NO_NAME# is invalid");
True(XiaNameRepairRules.IsInvalidGeneratedMetaName("无名"), "anonymous shi is invalid");
True(XiaNameRepairRules.IsInvalidGeneratedMetaName("无名氏"), "anonymous clan is invalid");
Equal(false, XiaNameRepairRules.IsInvalidGeneratedMetaName("孔氏"), "historical clan is valid");

Console.WriteLine("Rule tests passed.");
