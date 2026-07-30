using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public enum WarParticipantSideKind
    {
        Unknown = 0,
        Attacker = 1,
        Defender = 2
    }

    public enum WarParticipantRoleKind
    {
        Unknown = 0,
        MainBelligerent = 1,
        Independent = 2,
        FormalVassal = 3,
        Tributary = 4
    }

    public readonly struct WarParticipantSourceFact
    {
        public WarParticipantSourceFact(
            WarParticipantEntrySourceKind pKind,
            long pSourceKingdomId)
        {
            Kind = pKind;
            SourceKingdomId = pSourceKingdomId;
        }

        public WarParticipantEntrySourceKind Kind { get; }
        public long SourceKingdomId { get; }
    }

    public readonly struct WarParticipantSnapshotFacts
    {
        public WarParticipantSnapshotFacts(long kingdomId,
            WarParticipantSideKind side, WarParticipantRoleKind role,
            long formalSuzerainId, long vassalRelationId,
            long exitParentId, string entrySourceFingerprint,
            bool includedInExitGroup)
        {
            KingdomId = kingdomId;
            Side = side;
            Role = role;
            FormalSuzerainId = formalSuzerainId;
            VassalRelationId = vassalRelationId;
            ExitParentId = exitParentId;
            EntrySourceFingerprint = string.IsNullOrEmpty(
                entrySourceFingerprint)
                ? "unknown"
                : entrySourceFingerprint;
            IncludedInExitGroup = includedInExitGroup;
        }

        public long KingdomId { get; }
        public WarParticipantSideKind Side { get; }
        public WarParticipantRoleKind Role { get; }
        public long FormalSuzerainId { get; }
        public long VassalRelationId { get; }
        public long ExitParentId { get; }
        public string EntrySourceFingerprint { get; }
        public bool IncludedInExitGroup { get; }
    }

    public static class WarParticipantRosterRules
    {
        public static bool IsObligationOnlyFromParent(
            IReadOnlyList<WarParticipantSourceFact> pSources,
            long pExpectedParentKingdomId)
        {
            return pExpectedParentKingdomId >= 0 && pSources != null &&
                   pSources.Count == 1 &&
                   pSources[0].Kind ==
                   WarParticipantEntrySourceKind.FormalVassalObligation &&
                   pSources[0].SourceKingdomId == pExpectedParentKingdomId;
        }

        public static string BuildSourceFingerprint(
            IReadOnlyList<WarParticipantSourceFact> pSources)
        {
            if (pSources == null || pSources.Count == 0) return "unknown";
            var rows = new List<string>(pSources.Count);
            for (int i = 0; i < pSources.Count; i++)
            {
                WarParticipantSourceFact source = pSources[i];
                rows.Add(SourceId(source.Kind) + ":" +
                         source.SourceKingdomId);
            }
            rows.Sort(StringComparer.Ordinal);
            return string.Join("|", rows);
        }

        public static bool SnapshotMatches(
            WarParticipantSnapshotFacts pExpected,
            WarParticipantSnapshotFacts pActual)
        {
            return pExpected.KingdomId == pActual.KingdomId &&
                   pExpected.Side == pActual.Side &&
                   pExpected.Role == pActual.Role &&
                   pExpected.FormalSuzerainId ==
                   pActual.FormalSuzerainId &&
                   pExpected.VassalRelationId ==
                   pActual.VassalRelationId &&
                   pExpected.ExitParentId == pActual.ExitParentId &&
                   string.Equals(pExpected.EntrySourceFingerprint,
                       pActual.EntrySourceFingerprint,
                       StringComparison.Ordinal) &&
                   pExpected.IncludedInExitGroup ==
                   pActual.IncludedInExitGroup;
        }

        public static string SourceId(
            WarParticipantEntrySourceKind pSource)
        {
            return pSource switch
            {
                WarParticipantEntrySourceKind.MainBelligerent =>
                    "main_belligerent",
                WarParticipantEntrySourceKind.AllianceCall =>
                    "alliance_call",
                WarParticipantEntrySourceKind.FormalVassalObligation =>
                    "formal_vassal_obligation",
                WarParticipantEntrySourceKind.IndependentDeclaration =>
                    "independent_declaration",
                WarParticipantEntrySourceKind.ScriptedJoin =>
                    "scripted_join",
                WarParticipantEntrySourceKind.SeparatePeaceExit =>
                    "separate_peace_exit",
                _ => "unknown"
            };
        }
    }
}
