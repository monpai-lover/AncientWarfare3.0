using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.naming
{
    internal static class AWLocalizedMottoService
    {
        private const string KingdomGenerator = "kingdom_mottos";
        private const string ClanGenerator = "clan_mottos";
        private const string AllianceGenerator = "alliance_mottos";

        internal static string ProjectKingdom(Kingdom pKingdom,
            string pObservedMotto = null)
        {
            if (pKingdom?.data == null) return pObservedMotto ?? string.Empty;
            long cultureId = pKingdom.culture?.getID() ??
                pKingdom.data.name_culture_id;
            string projected = Project(pKingdom.data, pObservedMotto,
                KingdomGenerator, pKingdom.getID(), cultureId,
                (generator, parameters) =>
                    AWNameParameterGetters.GetKingdomParameterGetter(
                        generator.ParameterGetter)?.Invoke(pKingdom,
                        parameters));
            pKingdom.data.motto = projected;
            return projected;
        }

        internal static string ProjectClan(Clan pClan,
            string pObservedMotto = null)
        {
            if (pClan?.data == null) return pObservedMotto ?? string.Empty;
            string projected = Project(pClan.data, pObservedMotto,
                ClanGenerator, pClan.getID(), pClan.data.culture_id,
                (generator, parameters) =>
                    AWNameParameterGetters.GetClanParameterGetter(
                        generator.ParameterGetter)?.Invoke(pClan, null,
                        parameters));
            pClan.data.motto = projected;
            return projected;
        }

        internal static string ProjectAlliance(Alliance pAlliance,
            string pObservedMotto = null)
        {
            if (pAlliance?.data == null)
                return pObservedMotto ?? string.Empty;
            Kingdom founder = pAlliance.kingdoms_hashset
                .Where(pKingdom => pKingdom?.data != null)
                .OrderBy(pKingdom => pKingdom.getID())
                .FirstOrDefault();
            long cultureId = founder?.culture?.getID() ??
                pAlliance.data.name_culture_id;
            string projected = Project(pAlliance.data, pObservedMotto,
                AllianceGenerator, pAlliance.getID(), cultureId,
                (generator, parameters) =>
                    AWNameParameterGetters.GetAllianceParameterGetter(
                        generator.ParameterGetter)?.Invoke(pAlliance,
                        parameters));
            pAlliance.data.motto = projected;
            return projected;
        }

        internal static void CopyIdentity(BaseSystemData pSource,
            BaseSystemData pTarget)
        {
            if (pSource == null || pTarget == null) return;
            CopyString(pSource, pTarget, AWNameDataKeys.NativeMotto);
            CopyString(pSource, pTarget, AWNameDataKeys.ChineseMotto);
        }

        internal static void CommitEdit(BaseSystemData pData,
            string pEditedMotto)
        {
            if (pData == null || pEditedMotto == null) return;
            pData.get(AWNameDataKeys.NativeMotto, out string nativeMotto,
                string.Empty);
            pData.get(AWNameDataKeys.ChineseMotto, out string chineseMotto,
                string.Empty);
            AWLocalizedMottoProjection projection =
                AWLocalizedMottoProjectionRules.ResolveEdit(
                    AWLocalizedNameService.CurrentLanguage(), pEditedMotto,
                    nativeMotto, chineseMotto);
            StoreString(pData, AWNameDataKeys.NativeMotto,
                projection.NativeMotto);
            StoreString(pData, AWNameDataKeys.ChineseMotto,
                projection.ChineseMotto);
        }

        private static string Project(BaseSystemData pData,
            string pObservedMotto, string pGeneratorId, long pObjectId,
            long pCultureId,
            Action<AWNameGeneratorAsset, Dictionary<string, string>> pFill)
        {
            pData.get(AWNameDataKeys.NativeMotto, out string nativeMotto,
                string.Empty);
            pData.get(AWNameDataKeys.ChineseMotto, out string chineseMotto,
                string.Empty);
            AWLocalizedMottoProjection projection =
                AWLocalizedMottoProjectionRules.Resolve(
                    AWLocalizedNameService.CurrentLanguage(),
                    pObservedMotto, nativeMotto, chineseMotto);

            nativeMotto = projection.NativeMotto;
            chineseMotto = projection.ChineseMotto;
            if (projection.NeedsChineseGeneration &&
                AWNameGeneratorLibrary.Get(pGeneratorId) != null)
            {
                chineseMotto = AWLocalizedNameService.GenerateValue(
                    pGeneratorId, pObjectId, pCultureId, pFill);
            }

            if (projection.NeedsChineseGeneration &&
                string.IsNullOrWhiteSpace(chineseMotto))
            {
                chineseMotto = AWLocalizedMottoCreationRules.ResolveFallback(
                    AWLocalizedNameService.CurrentLanguage());
            }

            // A newly created kingdom may have no vanilla motto at all. The
            // old path only generated a Chinese value, leaving English and
            // other presentations empty when the race-specific library was
            // unavailable. Persist one deterministic fallback in the active
            // language slot so creation is never blank.
            if (AWLocalizedMottoCreationRules.ShouldUseFallback(
                    AWLocalizedNameService.CurrentLanguage(), pObservedMotto,
                    nativeMotto, chineseMotto))
            {
                string fallback = AWLocalizedMottoCreationRules.ResolveFallback(
                    AWLocalizedNameService.CurrentLanguage());
                if (AWNamingLanguageRules.IsChinesePresentation(
                        AWLocalizedNameService.CurrentLanguage()))
                    chineseMotto = fallback;
                else
                    nativeMotto = fallback;
            }

            if (!string.IsNullOrWhiteSpace(nativeMotto))
                pData.set(AWNameDataKeys.NativeMotto, nativeMotto);
            if (!string.IsNullOrWhiteSpace(chineseMotto))
                pData.set(AWNameDataKeys.ChineseMotto, chineseMotto);

            string selected = AWLocalizedNameProjectionRules.Select(
                AWLocalizedNameService.CurrentLanguage(), nativeMotto,
                chineseMotto);
            return selected.Length > 0
                ? selected
                : (pObservedMotto ?? string.Empty);
        }

        private static void CopyString(BaseSystemData pSource,
            BaseSystemData pTarget, string pKey)
        {
            pSource.get(pKey, out string value, string.Empty);
            StoreString(pTarget, pKey, value);
        }

        private static void StoreString(BaseSystemData pData, string pKey,
            string pValue)
        {
            if (string.IsNullOrWhiteSpace(pValue)) pData.removeString(pKey);
            else pData.set(pKey, pValue.Trim());
        }
    }
}
