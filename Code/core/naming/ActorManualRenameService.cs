using System;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.naming
{
    internal static class ActorManualRenameService
    {
        internal static ActorManualNameMode ResolveMode(Actor pActor)
        {
            if (LineageService.IsXia(pActor)) return ActorManualNameMode.Xia;
            NamingProfileId profile = AWCultureNamingTraditionService
                .ResolveForActorReadOnly(pActor).Profile;
            return profile == NamingProfileId.Xia
                ? ActorManualNameMode.Xia
                : ActorManualNameMode.NonXia;
        }

        internal static ActorManualNameDraft Capture(Actor pActor)
        {
            if (pActor?.data == null)
                return ActorManualNameRules.CreateDraft(
                    ActorManualNameMode.NonXia, string.Empty, string.Empty);
            ActorManualNameMode mode = ResolveMode(pActor);
            string family = ResolveFamily(pActor, mode);
            string given = ResolveGiven(pActor, family, mode);
            return mode == ActorManualNameMode.Xia
                ? ActorManualNameRules.CreateDraft(mode, family, given)
                : ActorManualNameRules.CreateDraft(mode, given, family);
        }

        internal static bool TryCommit(Actor pActor, string pFirstField,
            string pSecondField, out string pError)
        {
            pError = string.Empty;
            if (pActor?.data == null || pActor.isRekt())
            {
                pError = "actor_invalid";
                return false;
            }
            if (HistoricalSchoolDescentService.IsCanonicalMaster(pActor))
            {
                pError = "actor_name_protected";
                return false;
            }

            ActorManualNameMode mode = ResolveMode(pActor);
            ActorManualNameDraft draft = ActorManualNameRules.CreateDraft(
                mode, pFirstField, pSecondField);
            if (!draft.IsValid)
            {
                pError = "given_name_empty";
                return false;
            }

            string currentFamily = ResolveFamily(pActor, mode);
            bool familyChanged = !string.Equals(currentFamily,
                draft.FamilyOrClanName, StringComparison.Ordinal);
            if (familyChanged && draft.FamilyOrClanName.Length > 0)
            {
                pActor.data.get(LineageKeys.LINEAGE_ID, out long lineageId,
                    -1L);
                pActor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
                if (lineageId >= 0L && shiId >= 0L &&
                    !LineageService.TryForkManualNameBranch(pActor,
                        draft.FamilyOrClanName, out _))
                {
                    pError = "branch_fork_failed";
                    return false;
                }
                VisibleSurnameRenameService.RenamePatrilinealBranch(
                    pActor.data.id, draft.FamilyOrClanName);
            }

            CommitActor(pActor, draft, pMarkCustom: true);
            RefreshVanillaReferences(pActor);
            return true;
        }

        internal static void ApplyInheritedFamily(Actor pActor,
            string pFamilyName)
        {
            if (pActor?.data == null || pActor.isRekt()) return;
            ActorManualFamilyWritePlan familyWrite = ActorManualRenameRules
                .PlanIntegratedFamilyWrite(pFamilyName);
            ActorManualNameMode mode = ResolveMode(pActor);
            string given = ResolveGiven(pActor, familyWrite.FamilyName, mode);
            string display = ActorManualNameRules.CreateDisplayName(mode,
                given, familyWrite.FamilyName);
            if (display.Length == 0) return;
            ApplyFamilyIdentity(pActor, familyWrite);
            pActor.data.set(LineageKeys.GIVEN_NAME, given);
            pActor.data.set(AWNameDataKeys.GivenName, given);
            pActor.data.set("display_name", display);
            pActor.data.set(AWNameDataKeys.NativeName, display);
            pActor.data.set(AWNameDataKeys.ChineseName, display);
            pActor.data.set(AWNameDataKeys.NamingSchemaVersion,
                AWLocalizedNameService.SchemaVersion);
            pActor.data.name = display;
            AWLocalizedNameMigrationService.Enqueue("Unit", pActor.data.id,
                pActor.data);
        }

        private static void CommitActor(Actor pActor,
            ActorManualNameDraft pDraft, bool pMarkCustom)
        {
            ActorManualFamilyWritePlan familyWrite = ActorManualRenameRules
                .PlanIntegratedFamilyWrite(pDraft.FamilyOrClanName);
            pActor.data.set(LineageKeys.GIVEN_NAME, pDraft.GivenName);
            pActor.data.set(AWNameDataKeys.GivenName, pDraft.GivenName);
            ApplyFamilyIdentity(pActor, familyWrite);
            pActor.data.set("display_name", pDraft.DisplayName);
            pActor.data.set(AWNameDataKeys.NativeName, pDraft.DisplayName);
            pActor.data.set(AWNameDataKeys.ChineseName, pDraft.DisplayName);
            pActor.data.set(AWNameDataKeys.NamingSchemaVersion,
                AWLocalizedNameService.SchemaVersion);
            if (pMarkCustom) pActor.data.custom_name = true;
            pActor.setName(pDraft.DisplayName);
            LineageService.ArchiveActor(pActor, pAlive: pActor.isAlive());
            AWLocalizedNameMigrationService.Enqueue("Unit", pActor.data.id,
                pActor.data);
            try { pActor.clearGraphicsFully(); } catch { }
        }

        private static string ResolveFamily(Actor pActor,
            ActorManualNameMode pMode)
        {
            pActor.data.get(LineageKeys.CLAN_NAME, out string clan,
                string.Empty);
            pActor.data.get(LineageKeys.FAMILY_NAME, out string family,
                string.Empty);
            pActor.data.get(LineageKeys.CHINESE_FAMILY_NAME,
                out string chineseFamily, string.Empty);
            pActor.data.get(AWNameDataKeys.FamilyComponent,
                out string localizedFamily, string.Empty);
            return ActorManualRenameRules.ResolveFamilyIdentity(pMode, clan,
                family, chineseFamily, localizedFamily);
        }

        private static void ApplyFamilyIdentity(Actor pActor,
            ActorManualFamilyWritePlan pPlan)
        {
            pActor.data.set(LineageKeys.FAMILY_NAME, pPlan.FamilyName);
            pActor.data.set(LineageKeys.CHINESE_FAMILY_NAME,
                pPlan.ChineseFamilyName);
            pActor.data.set(LineageKeys.CLAN_NAME, pPlan.ClanName);
            pActor.data.set(AWNameDataKeys.FamilyComponent,
                pPlan.LocalizedFamilyComponent);
            pActor.data.set(LineageKeys.NAME_INTEGRATED,
                pPlan.NameIntegrated);
        }

        private static string ResolveGiven(Actor pActor, string pFamily,
            ActorManualNameMode pMode)
        {
            pActor.data.get(LineageKeys.GIVEN_NAME, out string given,
                string.Empty);
            if (string.IsNullOrWhiteSpace(given))
                pActor.data.get(AWNameDataKeys.GivenName, out given,
                    string.Empty);
            if (!string.IsNullOrWhiteSpace(given)) return given.Trim();
            string display = (pActor.data.name ?? string.Empty).Trim();
            if (pFamily.Length == 0) return display;
            if (pMode == ActorManualNameMode.Xia &&
                display.StartsWith(pFamily, StringComparison.Ordinal))
                return display.Substring(pFamily.Length).Trim();
            string suffix = " " + pFamily;
            if (display.EndsWith(suffix, StringComparison.Ordinal))
                return display.Substring(0, display.Length - suffix.Length)
                    .Trim();
            return display;
        }

        private static void RefreshVanillaReferences(Actor pActor)
        {
            try { pActor.city?.updateRulers(); } catch { }
            try { pActor.kingdom?.updateRulers(); } catch { }
            try { pActor.army?.updateCaptains(); } catch { }
        }
    }
}
