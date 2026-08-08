using System;
using System.IO;
using System.Linq;

namespace AncientWarfare3.Tests
{
    internal static class IntegrationSourceTests
    {
        internal static void Run()
        {
            ActorReadPathOnlyProjectsStoredIdentity();
            FullScanWritesRouteThroughRetryQueue();
            MigrationDoesNotFabricateHumanGenerators();
            ManualActorNameIsProtectedAcrossProjectionAndRestore();
            ManualRenameServiceOwnsStructuredPersistence();
        }

        private static void ActorReadPathOnlyProjectsStoredIdentity()
        {
            string source = Read("Code", "patch", "naming",
                "AW_ActorLocalizedNamePatch.cs");
            string compact = Compact(source);
            AssertEx.True(compact.Contains(
                    "AWLocalizedNameService.ProjectStored(__instance?.data)"),
                "Actor.getName must only project an already-stored identity.");
            AssertEx.True(!compact.Contains(
                    "stringprojected=AWLocalizedNameService.ProjectActor(" +
                    "__instance)"),
                "Actor.getName must not initialize, generate, or persist identity.");
        }

        private static void FullScanWritesRouteThroughRetryQueue()
        {
            string source = Read("Code", "core", "naming",
                "AWLocalizedNameMigrationService.cs");
            AssertEx.True(!source.Contains(
                    "AWLocalizedNamePersistence.Upsert(metaType, objectId, data)"),
                "Full-scan writes must not bypass the retry queue.");
            AssertEx.True(source.Contains(
                    "Enqueue(metaType, objectId, data)"),
                "Full-scan writes must capture and enqueue an immutable snapshot.");
        }

        private static void MigrationDoesNotFabricateHumanGenerators()
        {
            string source = Read("Code", "core", "naming",
                "AWLocalizedNameMigrationService.cs");
            AssertEx.True(!source.Contains("human_") &&
                          !source.Contains("EnsureGenerator"),
                "Migration must never fabricate a fallback generator id.");
        }

        private static void ManualActorNameIsProtectedAcrossProjectionAndRestore()
        {
            string service = Read("Code", "core", "naming",
                "AWLocalizedNameService.cs");
            string migration = Read("Code", "core", "naming",
                "AWLocalizedNameMigrationService.cs");
            string patch = Read("Code", "patch", "naming",
                "AW_ActorLocalizedNamePatch.cs");
            string lineage = Read("Code", "core", "lineage",
                "LineageService.cs");

            AssertEx.True(Compact(service).Contains("pData.custom_name"),
                "localized projection must inspect custom actor names");
            AssertEx.True(Compact(migration).Contains("custom_name"),
                "restore migration must reconcile custom actor names");
            AssertEx.True(Compact(patch).Contains("custom_name"),
                "Actor.getName must preserve custom actor names");
            AssertEx.True(Compact(lineage).Contains(
                    "HasProtectedAuthoredName(pActor)"),
                "lineage recomposition must respect authored names");
        }

        private static void ManualRenameServiceOwnsStructuredPersistence()
        {
            string path = PathFor("Code", "core", "naming",
                "ActorManualRenameService.cs");
            AssertEx.True(File.Exists(path),
                "manual actor rename service must exist");
            string compact = Compact(File.ReadAllText(path));
            AssertEx.True(compact.Contains("custom_name") &&
                          compact.Contains("AWNameDataKeys.NativeName") &&
                          compact.Contains("AWNameDataKeys.ChineseName") &&
                          compact.Contains("LineageService.ArchiveActor") &&
                          compact.Contains(
                              "AWLocalizedNameMigrationService.Enqueue"),
                "manual rename service must own every persistence boundary");
        }

        private static string Read(params string[] pParts)
        {
            return File.ReadAllText(PathFor(pParts));
        }

        private static string PathFor(params string[] pParts)
        {
            string path = Environment.CurrentDirectory;
            foreach (string part in pParts) path = Path.Combine(path, part);
            return path;
        }

        private static string Compact(string pSource)
        {
            return new string((pSource ?? string.Empty)
                .Where(value => !char.IsWhiteSpace(value)).ToArray());
        }
    }
}
