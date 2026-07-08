namespace AncientWarfare3.core.lineage
{
    public static class AncestryOriginRules
    {
        public static string SelectLineageCity(string pLiveOrArchiveCity, string pBranchOriginCity,
            string pRootOriginCity)
        {
            string root = (pRootOriginCity ?? "").Trim();
            if (!string.IsNullOrEmpty(root)) return root;

            string branch = (pBranchOriginCity ?? "").Trim();
            if (!string.IsNullOrEmpty(branch)) return branch;

            return (pLiveOrArchiveCity ?? "").Trim();
        }
    }
}
