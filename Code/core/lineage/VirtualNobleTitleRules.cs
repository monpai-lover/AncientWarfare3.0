using System;
using System.Text;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct VirtualNobleTitleCandidate
    {
        public VirtualNobleTitleCandidate(long pTitleId, string pTitleText,
            long pGrantOrder)
        {
            TitleId = pTitleId;
            TitleText = pTitleText ?? "";
            GrantOrder = pGrantOrder;
        }

        public long TitleId { get; }
        public string TitleText { get; }
        public long GrantOrder { get; }
    }

    internal static class VirtualNobleTitleRules
    {
        public const int MaximumTitleLength = 64;

        public static string NormalizeTitle(string pTitle)
        {
            if (string.IsNullOrWhiteSpace(pTitle)) return "";
            return pTitle.Trim();
        }

        public static string NormalizeTitleKey(string pTitle)
        {
            string normalized = NormalizeTitle(pTitle);
            if (normalized.Length == 0) return "";
            return normalized.Normalize(NormalizationForm.FormKC)
                .ToUpperInvariant();
        }

        public static bool IsValidTitle(string pTitle)
        {
            string normalized = NormalizeTitle(pTitle);
            return normalized.Length > 0 &&
                   normalized.Length <= MaximumTitleLength;
        }

        public static bool ShouldBePrimary(bool pHasFormalHereditaryTitle,
            bool pHasActiveVirtualTitle)
        {
            return !pHasFormalHereditaryTitle && pHasActiveVirtualTitle;
        }

        public static bool ShouldExposeInRoster(bool formalTitle,
            bool virtualTitle)
        {
            return formalTitle || virtualTitle;
        }

        public static long SelectPrimaryId(
            System.Collections.Generic.IReadOnlyList<
                VirtualNobleTitleCandidate> pCandidates)
        {
            if (pCandidates == null || pCandidates.Count == 0) return -1L;
            long selectedId = -1L;
            long selectedOrder = long.MaxValue;
            for (int i = 0; i < pCandidates.Count; i++)
            {
                VirtualNobleTitleCandidate candidate = pCandidates[i];
                if (candidate.TitleId < 0) continue;
                if (selectedId < 0 || candidate.GrantOrder < selectedOrder ||
                    candidate.GrantOrder == selectedOrder &&
                    candidate.TitleId < selectedId)
                {
                    selectedId = candidate.TitleId;
                    selectedOrder = candidate.GrantOrder;
                }
            }
            return selectedId;
        }
    }
}
