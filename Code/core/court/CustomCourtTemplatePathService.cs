using System.IO;

namespace AncientWarfare3.core.court
{
    public static class CustomCourtTemplatePathService
    {
        public static string RootPath { get; private set; } = string.Empty;

        public static void Initialize(string modFolder)
        {
            RootPath = ResolveCourtJsonRoot(modFolder);
        }

        public static string ResolveCourtJsonRoot(string modFolder)
        {
            if (string.IsNullOrWhiteSpace(modFolder)) return string.Empty;
            return Path.Combine(Path.GetFullPath(modFolder), "Courtjson");
        }
    }
}
