using System;
using System.IO;

namespace AncientWarfare3.core.db
{
    public static class LineageRuntimePathRules
    {
        public const string DbFileName = "aw3_lineage_archive.db";

        public static string Resolve(string pModFolder, int processId)
        {
            if (string.IsNullOrWhiteSpace(pModFolder))
                throw new ArgumentException("Mod folder is required.",
                    nameof(pModFolder));
            if (processId <= 0)
                throw new ArgumentOutOfRangeException(nameof(processId));

            return Path.Combine(Path.GetFullPath(pModFolder), ".runtime",
                "process-" + processId,
                DbFileName);
        }
    }
}
