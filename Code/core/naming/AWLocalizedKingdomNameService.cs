using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.naming
{
    internal static class AWLocalizedKingdomNameService
    {
        [ThreadStatic] private static Kingdom _editingKingdom;
        [ThreadStatic] private static string _nativeBefore;
        [ThreadStatic] private static string _chineseBefore;
        [ThreadStatic] private static string _displayBefore;

        internal static void BeginEdit(Kingdom pKingdom)
        {
            EndEdit();
            if (pKingdom?.data == null) return;
            _editingKingdom = pKingdom;
            pKingdom.data.get(AWNameDataKeys.NativeName,
                out _nativeBefore, string.Empty);
            pKingdom.data.get(AWNameDataKeys.ChineseName,
                out _chineseBefore, string.Empty);
            _displayBefore = pKingdom.data.name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(_nativeBefore) &&
                !AWNamingLanguageRules.IsChinesePresentation(
                    AWLocalizedNameService.CurrentLanguage()))
                _nativeBefore = _displayBefore;
            if (string.IsNullOrWhiteSpace(_chineseBefore) &&
                AWNamingLanguageRules.IsChinesePresentation(
                    AWLocalizedNameService.CurrentLanguage()))
                _chineseBefore = _displayBefore;
        }

        internal static bool IsEditing(Kingdom pKingdom)
        {
            return pKingdom != null && pKingdom == _editingKingdom;
        }

        internal static void CommitEdit(Kingdom pKingdom,
            string pEditedName)
        {
            if (pKingdom?.data == null || pKingdom != _editingKingdom ||
                string.IsNullOrWhiteSpace(pEditedName)) return;
            Kingdom[] members =
                SuccessionDisputeService.GetSharedNameMembers(pKingdom);
            for (int i = 0; i < members.Length; i++)
            {
                Kingdom member = members[i];
                if (member?.data == null || member.isRekt()) continue;
                member.data.get(AWNameDataKeys.NativeName,
                    out string nativeName, string.Empty);
                member.data.get(AWNameDataKeys.ChineseName,
                    out string chineseName, string.Empty);
                if (member == pKingdom)
                {
                    nativeName = _nativeBefore;
                    chineseName = _chineseBefore;
                }
                AWLocalizedNameEditDecision edit =
                    AWLocalizedKingdomRenameRules.ResolveEdit(
                        AWLocalizedNameService.CurrentLanguage(),
                        pEditedName, nativeName, chineseName);
                member.data.set(AWNameDataKeys.NativeName,
                    edit.NativeName);
                member.data.set(AWNameDataKeys.ChineseName,
                    edit.ChineseName);
                member.data.set(AWNameDataKeys.NamingSchemaVersion,
                    AWLocalizedNameService.SchemaVersion);
                member.data.custom_name = true;
                AWLocalizedNameMigrationService.Enqueue("Kingdom",
                    member.getID(), member.data);
            }
            ProjectStored(pKingdom, _displayBefore);
        }

        internal static bool CommitCanonicalStateName(Kingdom pKingdom,
            string pStateName)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                string.IsNullOrWhiteSpace(pStateName)) return false;
            string canonical = pStateName.Trim();
            Kingdom[] members =
                SuccessionDisputeService.GetSharedNameMembers(pKingdom);
            var changedIds = new HashSet<long>();
            for (int i = 0; i < members.Length; i++)
            {
                Kingdom member = members[i];
                if (member?.data == null || member.isRekt()) continue;
                member.data.get(AWNameDataKeys.NativeName,
                    out string nativeName, string.Empty);
                member.data.get(AWNameDataKeys.ChineseName,
                    out string chineseName, string.Empty);
                member.data.get(AWNameDataKeys.NamingSchemaVersion,
                    out int schemaVersion, 0);
                AWLocalizedNameEditDecision identity =
                    AWLocalizedKingdomRenameRules.ResolveCanonicalStateName(
                        canonical, nativeName, chineseName);
                bool identityChanged =
                    !string.Equals(nativeName, identity.NativeName,
                        StringComparison.Ordinal) ||
                    !string.Equals(chineseName, identity.ChineseName,
                        StringComparison.Ordinal) ||
                    schemaVersion != AWLocalizedNameService.SchemaVersion ||
                    !member.data.custom_name;
                bool displayChanged = !string.Equals(member.data.name,
                    canonical, StringComparison.Ordinal);
                if (!identityChanged && !displayChanged) continue;
                member.data.set(AWNameDataKeys.NativeName,
                    identity.NativeName);
                member.data.set(AWNameDataKeys.ChineseName,
                    identity.ChineseName);
                member.data.set(AWNameDataKeys.NamingSchemaVersion,
                    AWLocalizedNameService.SchemaVersion);
                member.data.custom_name = true;
                AWLocalizedNameMigrationService.Enqueue("Kingdom",
                    member.getID(), member.data);
                changedIds.Add(member.getID());
            }

            string projected = ProjectStored(pKingdom, pRefresh: false);
            if (!string.Equals(projected, canonical,
                    StringComparison.Ordinal)) return false;
            for (int i = 0; i < members.Length; i++)
            {
                Kingdom member = members[i];
                if (member?.data == null || member.isRekt() ||
                    !changedIds.Contains(member.getID())) continue;
                KingdomRenameProjectionService.Refresh(member);
            }
            return true;
        }

        internal static string ProjectStored(Kingdom pKingdom,
            string pObservedNameBefore = null, bool pRefresh = true)
        {
            if (pKingdom?.data == null || pKingdom.isRekt())
                return string.Empty;
            Kingdom[] members =
                SuccessionDisputeService.GetSharedNameMembers(pKingdom);
            Kingdom authority =
                SuccessionDisputeService.GetSharedNameAuthority(pKingdom);
            if (authority?.data == null) authority = pKingdom;
            authority.data.get(AWNameDataKeys.NativeName,
                out string nativeName, string.Empty);
            authority.data.get(AWNameDataKeys.ChineseName,
                out string chineseName, string.Empty);
            string projected = AWLocalizedKingdomRenameRules.
                ResolveSharedProjection(
                    AWLocalizedNameService.CurrentLanguage(), nativeName,
                    chineseName,
                    SuccessionDisputeService.GetLegacySharedName(pKingdom));
            if (string.IsNullOrWhiteSpace(projected))
                projected = authority.data.name ?? string.Empty;

            var invalidatedIds = new HashSet<long>();
            for (int i = 0; i < members.Length; i++)
            {
                Kingdom member = members[i];
                if (member?.data == null || member.isRekt()) continue;
                string before = member == pKingdom &&
                                pObservedNameBefore != null
                    ? pObservedNameBefore
                    : member.data.name ?? string.Empty;
                member.data.name = projected;
                if (pRefresh &&
                    AWLocalizedNameProjectionChangeRules.TryMarkInvalidated(
                        invalidatedIds, member.getID(), before, projected))
                    KingdomRenameProjectionService.Refresh(member);
            }
            return projected;
        }

        internal static int SynchronizeSharedIdentity(Kingdom pAuthority,
            IReadOnlyList<Kingdom> pMembers)
        {
            if (pAuthority?.data == null || pMembers == null) return 0;
            AWLocalizedNameIdentitySnapshot authorityIdentity =
                AWLocalizedNamePersistence.Capture(pAuthority.data);
            var membersById = new Dictionary<long, Kingdom>();
            var memberIds = new List<long>(pMembers.Count);
            for (int i = 0; i < pMembers.Count; i++)
            {
                Kingdom member = pMembers[i];
                if (member?.data == null || member.isRekt()) continue;
                long id = member.getID();
                if (id < 0L || membersById.ContainsKey(id)) continue;
                membersById[id] = member;
                memberIds.Add(id);
            }
            int writes = AWLocalizedKingdomIdentitySyncAdapter.Synchronize(
                authorityIdentity, memberIds, (id, identity) =>
                {
                    Kingdom member = membersById[id];
                    AWLocalizedNamePersistence.Apply(member.data, identity);
                    member.data.custom_name = pAuthority.data.custom_name;
                    AWLocalizedNameMigrationService.Enqueue("Kingdom", id,
                        member.data);
                });
            ProjectStored(pAuthority, pRefresh: false);
            return writes;
        }

        internal static void EndEdit()
        {
            _editingKingdom = null;
            _nativeBefore = null;
            _chineseBefore = null;
            _displayBefore = null;
        }
    }
}
