using System;

namespace AncientWarfare3.core.lineage
{
    public static class ManualAllianceToolRules
    {
        public static bool IsVanillaAllianceTool(string pEntryPoint)
        {
            return string.Equals(pEntryPoint,
                "ActionLibrary.clickUnity", StringComparison.Ordinal);
        }
    }
}
