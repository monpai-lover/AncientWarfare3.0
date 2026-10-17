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
            TargetedWritesRouteThroughRetryQueue();
            PersistenceUsesLegacyCompatibleAtomicUpdate();
            MigrationDoesNotFabricateHumanGenerators();
            ManualActorNameIsProtectedAcrossProjectionAndRestore();
            ManualRenameServiceOwnsStructuredPersistence();
            SplitActorNameEditorReplacesVanillaInput();
        }

        private static void PersistenceUsesLegacyCompatibleAtomicUpdate()
        {
            string source = Read("Code", "core", "naming",
                "AWLocalizedNamePersistence.cs");
            AssertEx.True(!source.Contains("ON CONFLICT") &&
                          !source.Contains("OR REPLACE"),
                "Localized identity writes must support bundled SQLite without replacing rows.");
            AssertEx.True(source.Contains("BeginTransaction") &&
                          source.Contains("command.Transaction = transaction") &&
                          source.Contains("transaction.Commit()"),
                "The synchronous update/insert fallback must commit atomically.");
        }

        private static void ActorReadPathOnlyProjectsStoredIdentity()
        {
            string source = Read("Code", "patch", "naming",
                "AW_ActorLocalizedNamePatch.cs");
            string compact = Compact(Between(source,
                "private static void GetName_Postfix(",
                "private static bool IsHistoricalFigure("));
            AssertEx.True(compact.Contains(
                    "AWLocalizedNameService.ProjectStored(__instance?.data)"),
                "Actor.getName postfix must project an already-stored identity.");
            AssertEx.True(!compact.Contains("ProjectActor(") &&
                          !compact.Contains(".Enqueue(") &&
                          !compact.Contains(".Upsert("),
                "Actor.getName postfix must not initialize or persist identity.");
        }

        private static void TargetedWritesRouteThroughRetryQueue()
        {
            string compact = Compact(Read("Code", "core", "naming",
                "AWLocalizedNameMigrationService.cs"));
            AssertEx.True(!compact.Contains("AWLocalizedNamePersistence.Upsert("),
                "Targeted writes must not bypass the asynchronous retry queue.");
            int capture = compact.IndexOf("AWLocalizedNamePersistence.Capture(pData)",
                StringComparison.Ordinal);
            int enqueue = compact.IndexOf(
                "PendingWrites.Enqueue(pMetaType,pObjectId,snapshot)",
                StringComparison.Ordinal);
            AssertEx.True(capture >= 0 && enqueue > capture,
                "Writes must capture an immutable snapshot before enqueueing it.");
            AssertEx.True(compact.Contains("PendingWrites.Flush(pBudget,TrySubmitAsync)") &&
                          compact.Contains("HistoricalWriteService.Ready") &&
                          compact.Contains("HistoricalWriteService.TryUpsertState("),
                "The retry queue must use a bounded flush into the ready async writer.");
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
            string persistence = Read("Code", "core", "naming",
                "AWLocalizedNamePersistence.cs");
            string refresh = Read("Code", "core", "naming",
                "AWLocalizedNameRefreshService.cs");
            string restore = Read("Code", "core", "multiplayer",
                "AW3RuntimeRestorePipeline.cs");
            string patch = Read("Code", "patch", "naming",
                "AW_ActorLocalizedNamePatch.cs");
            string lineage = Read("Code", "core", "lineage",
                "LineageService.cs");

            string projectStored = Compact(Between(service,
                "internal static string ProjectStored(",
                "internal static string CurrentLanguage("));
            AssertEx.True(projectStored.Contains(
                    "if(pData.custom_name&&!string.IsNullOrWhiteSpace(pData.name))returnpData.name;"),
                "localized projection must preserve a nonempty custom name before selecting a stored slot");
            AssertEx.True(Compact(persistence).Contains(
                    "AWLocalizedNameService.ProjectStored(pData)"),
                "restored identity data must project through the custom-name protection");
            AssertEx.True(Compact(restore).Contains(
                    "AWLocalizedNameMigrationService.RebuildVisibleProjections") &&
                          Compact(migration).Contains("AWLocalizedNameRefreshService.Request()") &&
                          Compact(refresh).Contains("AWLocalizedNameService.ProjectStored(data)"),
                "runtime restore must request visible refresh through protected projection");
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

        private static void SplitActorNameEditorReplacesVanillaInput()
        {
            string path = PathFor("Code", "patch", "naming",
                "AW_ActorManualNamePatch.cs");
            AssertEx.True(File.Exists(path),
                "split actor name editor patch must exist");
            string compact = Compact(File.ReadAllText(path));
            AssertEx.True(compact.Contains(
                    "AW_ActorManualNameSecondInput") &&
                compact.Contains("ActorManualRenameService") &&
                compact.Contains("ActorManualRenameService.ResolveMode(pActor)") &&
                compact.Contains("ActorManualRenameRules.UsesFamilyFirst(pState.Mode)") &&
                compact.Contains("onEndEdit.RemoveAllListeners") &&
                compact.Contains("pFirst.can_be_empty=familyFirst") &&
                compact.Contains("pState.Second.can_be_empty=!familyFirst") &&
                compact.Contains("ActorManualNameEditorState.Display") &&
                compact.Contains("EnterEditing") &&
                compact.Contains("CommitAndCollapse"),
                "split editor must replace vanilla listener, support both orders, and expand only while editing");
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

        private static string Between(string pSource, string pStart, string pEnd)
        {
            int start = pSource.IndexOf(pStart, StringComparison.Ordinal);
            int end = start < 0 ? -1 : pSource.IndexOf(pEnd, start + pStart.Length,
                StringComparison.Ordinal);
            AssertEx.True(start >= 0 && end > start,
                "Expected source boundaries must exist: " + pStart + " / " + pEnd);
            return pSource.Substring(start, end - start);
        }
    }
}
