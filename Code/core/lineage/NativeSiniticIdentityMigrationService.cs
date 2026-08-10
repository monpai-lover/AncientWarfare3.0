using System;
using AncientWarfare3.core.db;
using AncientWarfare3.core.naming;

namespace AncientWarfare3.core.lineage
{
    internal static class NativeSiniticIdentityMigrationService
    {
        private const string MigrationVersionKey =
            "aw_native_sinitic_identity_migration_version";
        private const int CurrentMigrationVersion = 1;
        [ThreadStatic] private static bool _repairing;

        internal static bool TryRepair(Actor pActor)
        {
            if (_repairing || pActor?.data == null ||
                !AWNativeSiniticSpeciesRules.IsNativeSiniticSpecies(
                    pActor.asset?.id)) return false;

            pActor.data.get(LineageKeys.FAMILY_NAME, out string family,
                string.Empty);
            pActor.data.get(LineageKeys.GIVEN_NAME, out string given,
                string.Empty);
            bool complete = !string.IsNullOrWhiteSpace(family) &&
                            !string.IsNullOrWhiteSpace(given);
            pActor.data.get(MigrationVersionKey, out int migrationVersion, 0);
            if (pActor.data.custom_name ||
                (complete && migrationVersion >= CurrentMigrationVersion))
                return false;
            pActor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            pActor.data.get(LineageKeys.NAMING_PROFILE,
                out string actorProfile, string.Empty);
            ShiBranchInfo branch = shiId >= 0L
                ? LineageQuery.GetShiBranchInfo(shiId)
                : null;
            bool legacyBranch = branch != null &&
                (string.Equals(branch.naming_profile, "western",
                     StringComparison.Ordinal) ||
                 string.Equals(branch.naming_profile, "orc_nomadic",
                     StringComparison.Ordinal));
            bool legacyIdentity = legacyBranch ||
                string.Equals(actorProfile, "western",
                    StringComparison.Ordinal) ||
                string.Equals(actorProfile, "orc_nomadic",
                    StringComparison.Ordinal);
            bool nativeBranch = branch != null && string.Equals(
                branch.naming_profile, "native_sinitic",
                StringComparison.Ordinal);
            bool branchMismatch = nativeBranch &&
                !string.IsNullOrWhiteSpace(branch.clan_name) &&
                !string.Equals(branch.clan_name.Trim(), family?.Trim(),
                    StringComparison.Ordinal);
            NativeSiniticIdentityMigrationAction action =
                NativeSiniticIdentityMigrationRules.Decide(
                    targetProfile: true,
                    protectedName: false,
                    completeIdentity: complete,
                    legacyWesternBranch: legacyIdentity,
                    branchFamilyMismatch: branchMismatch);
            if (action == NativeSiniticIdentityMigrationAction.Reuse)
            {
                pActor.data.set(LineageKeys.NAMING_PROFILE,
                    "native_sinitic");
                pActor.data.set(MigrationVersionKey,
                    CurrentMigrationVersion);
                return false;
            }
            if (action != NativeSiniticIdentityMigrationAction.Repair)
                return false;

            _repairing = true;
            try
            {
                string inheritedFamily = nativeBranch
                    ? branch.clan_name?.Trim()
                    : (!legacyIdentity && !string.IsNullOrWhiteSpace(family)
                        ? family.Trim()
                        : string.Empty);
                NativeSiniticNameParts generated =
                    AWLocalizedNameService.GenerateNativeSiniticIdentity(
                        pActor, inheritedFamily);
                if (!generated.Valid) return false;

                string repairedFamily = inheritedFamily.Length > 0
                    ? inheritedFamily
                    : generated.FamilyName;
                if (legacyBranch)
                {
                    NativeSiniticIdentityMigrationCommitResult committed =
                        NativeSiniticIdentityMigrationPersistence.TryCommit(
                            LineageArchiveManager.Instance?.OperatingDB,
                            shiId, generated.FamilyName);
                    if (!committed.Success) return false;
                    repairedFamily = committed.FamilyName;
                }
                string repairedGiven = string.IsNullOrWhiteSpace(given)
                    ? generated.GivenName
                    : given.Trim();
                ApplyIdentity(pActor, repairedFamily, repairedGiven);
                return true;
            }
            finally
            {
                _repairing = false;
            }
        }

        private static void ApplyIdentity(Actor pActor, string pFamily,
            string pGiven)
        {
            string display = pFamily + pGiven;
            pActor.data.set(LineageKeys.FAMILY_NAME, pFamily);
            pActor.data.set(LineageKeys.CHINESE_FAMILY_NAME, pFamily);
            pActor.data.set(LineageKeys.CLAN_NAME, pFamily);
            pActor.data.set(LineageKeys.GIVEN_NAME, pGiven);
            pActor.data.set(LineageKeys.NAMING_PROFILE, "native_sinitic");
            pActor.data.set(LineageKeys.NAME_INTEGRATED, 1);
            pActor.data.set(MigrationVersionKey, CurrentMigrationVersion);
            pActor.data.set(AWNameDataKeys.FamilyComponent, pFamily);
            pActor.data.set(AWNameDataKeys.GivenName, pGiven);
            pActor.data.set(AWNameDataKeys.ChineseName, display);
            pActor.data.set("display_name", display);
        }
    }
}
