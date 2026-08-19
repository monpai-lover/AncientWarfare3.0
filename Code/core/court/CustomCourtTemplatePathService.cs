using System.IO;

namespace AncientWarfare3.core.court
{
    public static class CustomCourtTemplatePathService
    {
        public static string RootPath { get; private set; } = string.Empty;
        public static string CentralRootPath { get; private set; } =
            string.Empty;
        public static string LocalRootPath { get; private set; } = string.Empty;

        public static void Initialize(string modFolder)
        {
            RootPath = ResolveCourtJsonRoot(modFolder);
            CentralRootPath = ResolveCentralRoot(modFolder);
            LocalRootPath = ResolveLocalRoot(modFolder);
        }

        public static string ResolveCourtJsonRoot(string modFolder)
        {
            if (string.IsNullOrWhiteSpace(modFolder)) return string.Empty;
            return Path.Combine(Path.GetFullPath(modFolder), "Courtjson");
        }

        public static string ResolveCentralRoot(string modFolder)
        {
            string root = ResolveCourtJsonRoot(modFolder);
            return string.IsNullOrEmpty(root)
                ? string.Empty
                : Path.Combine(root, "Central");
        }

        public static string ResolveLocalRoot(string modFolder)
        {
            string root = ResolveCourtJsonRoot(modFolder);
            return string.IsNullOrEmpty(root)
                ? string.Empty
                : Path.Combine(root, "Local");
        }
    }
}
