using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class MilitaryGovernorateColorService
    {
        public const int MaximumSynchronizedChildren = 128;

        [ThreadStatic]
        private static int _copyDepth;

        public static void OnSuzerainColorChanged(Kingdom pSuzerain)
        {
            if (_copyDepth > 0 || pSuzerain?.data == null ||
                pSuzerain.isRekt()) return;
            List<MilitaryGovernorateSnapshot> children =
                MilitaryGovernorateStore.GetDirectActive(pSuzerain,
                    MaximumSynchronizedChildren);
            for (int i = 0; i < children.Count; i++)
            {
                MilitaryGovernorateSnapshot snapshot = children[i];
                Kingdom subject = FindKingdom(
                    snapshot?.SubjectKingdomId ?? -1L);
                if (!IsDirectActive(subject, pSuzerain)) continue;
                CopyFromSuzerain(subject, pSuzerain);
            }
        }

        public static bool CopyFromSuzerain(Kingdom pSubject,
            Kingdom pSuzerain)
        {
            if (pSubject?.data == null || pSuzerain?.data == null ||
                pSubject == pSuzerain) return false;
            try
            {
                _copyDepth++;
                return pSubject.updateColor(pSuzerain.getColor());
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Military governorate color copy failed: " +
                    error.Message);
                return false;
            }
            finally
            {
                _copyDepth--;
            }
        }

        private static bool IsDirectActive(Kingdom pSubject,
            Kingdom pSuzerain)
        {
            return MilitaryGovernorateRules.ShouldSynchronizeColor(
                VassalService.GetSuzerain(pSubject) == pSuzerain,
                pSubject?.data != null,
                VassalService.GetSubjectKind(pSubject));
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            if (pKingdomId < 0) return null;
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }
    }
}
