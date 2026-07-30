using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal sealed class WarParticipantRosterEntry
    {
        public WarParticipantRosterEntry(Kingdom pKingdom,
            WarParticipantSideKind pSide, WarParticipantRoleKind pRole,
            long pFormalSuzerainId, long pVassalRelationId,
            WarParticipantEntrySourceKind pEntrySourceKind,
            string pEntrySourceFingerprint,
            IReadOnlyList<WarParticipantSourceFact> pSources,
            long pExitParentId, bool pIncludedInExitGroup)
        {
            Kingdom = pKingdom;
            KingdomId = pKingdom?.data?.id ?? -1L;
            Side = pSide;
            Role = pRole;
            FormalSuzerainId = pFormalSuzerainId;
            VassalRelationId = pVassalRelationId;
            EntrySourceKind = pEntrySourceKind;
            EntrySourceFingerprint = string.IsNullOrEmpty(
                pEntrySourceFingerprint)
                ? "unknown"
                : pEntrySourceFingerprint;
            Sources = pSources ?? Array.Empty<WarParticipantSourceFact>();
            ExitParentId = pExitParentId;
            IncludedInExitGroup = pIncludedInExitGroup;
        }

        public Kingdom Kingdom { get; }
        public long KingdomId { get; }
        public WarParticipantSideKind Side { get; }
        public WarParticipantRoleKind Role { get; }
        public long FormalSuzerainId { get; }
        public long VassalRelationId { get; }
        public WarParticipantEntrySourceKind EntrySourceKind { get; }
        public string EntrySourceFingerprint { get; }
        public IReadOnlyList<WarParticipantSourceFact> Sources { get; }
        public long ExitParentId { get; }
        public bool IncludedInExitGroup { get; }

        public WarParticipantSnapshotFacts ToFacts()
        {
            return new WarParticipantSnapshotFacts(KingdomId, Side, Role,
                FormalSuzerainId, VassalRelationId, ExitParentId,
                EntrySourceFingerprint, IncludedInExitGroup);
        }
    }

    internal sealed class WarParticipantRosterContext
    {
        private readonly Dictionary<long, WarParticipantRosterEntry> _byId;

        public WarParticipantRosterContext(War pWar,
            long pMainAttackerId, long pMainDefenderId,
            long pExitRootKingdomId,
            IReadOnlyList<WarParticipantRosterEntry> pParticipants)
        {
            War = pWar;
            WarId = pWar?.data?.id ?? -1L;
            MainAttackerId = pMainAttackerId;
            MainDefenderId = pMainDefenderId;
            ExitRootKingdomId = pExitRootKingdomId;
            Participants = pParticipants ??
                Array.Empty<WarParticipantRosterEntry>();
            var byId = new Dictionary<long, WarParticipantRosterEntry>();
            for (int i = 0; i < Participants.Count; i++)
                byId[Participants[i].KingdomId] = Participants[i];
            _byId = byId;
        }

        public War War { get; }
        public long WarId { get; }
        public long MainAttackerId { get; }
        public long MainDefenderId { get; }
        public long ExitRootKingdomId { get; }
        public IReadOnlyList<WarParticipantRosterEntry> Participants { get; }

        public bool TryGet(long pKingdomId,
            out WarParticipantRosterEntry pEntry)
        {
            return _byId.TryGetValue(pKingdomId, out pEntry);
        }

        public List<WarPeaceSettlementParticipantSnapshot>
            BuildParticipantSnapshots()
        {
            var result = new List<WarPeaceSettlementParticipantSnapshot>(
                Participants.Count);
            for (int i = 0; i < Participants.Count; i++)
            {
                WarParticipantRosterEntry entry = Participants[i];
                result.Add(new WarPeaceSettlementParticipantSnapshot
                {
                    KingdomId = entry.KingdomId,
                    SideKind = SideId(entry.Side),
                    ParticipantRole = RoleId(entry.Role),
                    ExitParentId = entry.ExitParentId,
                    VassalRelationId = entry.VassalRelationId,
                    EntrySourceKind = entry.EntrySourceKind,
                    EntrySourceFingerprint =
                        entry.EntrySourceFingerprint,
                    IncludedInExitGroup = entry.IncludedInExitGroup
                });
            }
            return result;
        }

        public bool ValidateParticipantSnapshots(
            IReadOnlyList<WarPeaceSettlementParticipantSnapshot> pExpected,
            out string pReason)
        {
            pReason = "";
            if (pExpected == null ||
                pExpected.Count != Participants.Count)
            {
                pReason = "participant_roster_changed";
                return false;
            }
            var seen = new HashSet<long>();
            for (int i = 0; i < pExpected.Count; i++)
            {
                WarPeaceSettlementParticipantSnapshot expected =
                    pExpected[i];
                if (expected == null || !seen.Add(expected.KingdomId) ||
                    !TryGet(expected.KingdomId,
                        out WarParticipantRosterEntry actual) ||
                    expected.EntrySourceKind != actual.EntrySourceKind)
                {
                    pReason = "participant_roster_changed";
                    return false;
                }
                var expectedFacts = new WarParticipantSnapshotFacts(
                    expected.KingdomId, ParseSide(expected.SideKind),
                    ParseRole(expected.ParticipantRole),
                    actual.FormalSuzerainId,
                    expected.VassalRelationId, expected.ExitParentId,
                    expected.EntrySourceFingerprint,
                    expected.IncludedInExitGroup);
                if (WarParticipantRosterRules.SnapshotMatches(expectedFacts,
                        actual.ToFacts())) continue;
                pReason = "participant_roster_changed";
                return false;
            }
            return true;
        }

        private static string SideId(WarParticipantSideKind pSide)
        {
            return pSide == WarParticipantSideKind.Attacker
                ? "attacker"
                : pSide == WarParticipantSideKind.Defender
                    ? "defender"
                    : "unknown";
        }

        private static WarParticipantSideKind ParseSide(string pSide)
        {
            return string.Equals(pSide, "attacker", StringComparison.Ordinal)
                ? WarParticipantSideKind.Attacker
                : string.Equals(pSide, "defender", StringComparison.Ordinal)
                    ? WarParticipantSideKind.Defender
                    : WarParticipantSideKind.Unknown;
        }

        private static string RoleId(WarParticipantRoleKind pRole)
        {
            return pRole switch
            {
                WarParticipantRoleKind.MainBelligerent =>
                    "main_belligerent",
                WarParticipantRoleKind.Independent => "independent",
                WarParticipantRoleKind.FormalVassal => "formal_vassal",
                WarParticipantRoleKind.Tributary => "tributary",
                _ => "unknown"
            };
        }

        private static WarParticipantRoleKind ParseRole(string pRole)
        {
            return pRole switch
            {
                "main_belligerent" =>
                    WarParticipantRoleKind.MainBelligerent,
                "independent" => WarParticipantRoleKind.Independent,
                "formal_vassal" => WarParticipantRoleKind.FormalVassal,
                "tributary" => WarParticipantRoleKind.Tributary,
                _ => WarParticipantRoleKind.Unknown
            };
        }
    }

    internal static class WarParticipantRosterService
    {
        private const int MaximumParticipants = 64;

        private sealed class EntryBuilder
        {
            public Kingdom Kingdom;
            public WarParticipantSideKind Side;
            public WarParticipantRoleKind Role;
            public long FormalSuzerainId = -1L;
            public long VassalRelationId = -1L;
            public WarParticipantEntrySourceKind EntrySourceKind;
            public string EntrySourceFingerprint = "unknown";
            public IReadOnlyList<WarParticipantSourceFact> Sources =
                Array.Empty<WarParticipantSourceFact>();
            public long ExitParentId = -1L;
            public bool IncludedInExitGroup;
        }

        public static bool TryBuild(War pWar, long pExitRootKingdomId,
            out WarParticipantRosterContext pContext,
            out string pReason)
        {
            return TryBuild(pWar, pExitRootKingdomId,
                pRepairMissingMainSource: true, out pContext, out pReason);
        }

        public static bool TryBuildReadOnly(War pWar,
            long pExitRootKingdomId,
            out WarParticipantRosterContext pContext,
            out string pReason)
        {
            return TryBuild(pWar, pExitRootKingdomId,
                pRepairMissingMainSource: false, out pContext, out pReason);
        }

        private static bool TryBuild(War pWar, long pExitRootKingdomId,
            bool pRepairMissingMainSource,
            out WarParticipantRosterContext pContext,
            out string pReason)
        {
            pContext = null;
            pReason = "";
            if (pWar?.data == null || pWar.hasEnded())
            {
                pReason = "war_unavailable";
                return false;
            }

            long mainAttackerId = pWar.getMainAttacker()?.data?.id ?? -1L;
            long mainDefenderId = pWar.getMainDefender()?.data?.id ?? -1L;
            var builders = new List<EntryBuilder>();
            var byId = new Dictionary<long, EntryBuilder>();
            if (!TryAddSide(pWar, pWar.getAttackers(),
                    WarParticipantSideKind.Attacker, mainAttackerId,
                    mainDefenderId, builders, byId,
                    pRepairMissingMainSource, out pReason) ||
                !TryAddSide(pWar, pWar.getDefenders(),
                    WarParticipantSideKind.Defender, mainAttackerId,
                    mainDefenderId, builders, byId,
                    pRepairMissingMainSource, out pReason))
                return false;

            if (pExitRootKingdomId >= 0)
            {
                if (!byId.TryGetValue(pExitRootKingdomId,
                        out EntryBuilder root))
                {
                    pReason = "exit_root_not_participant";
                    return false;
                }
                root.IncludedInExitGroup = true;
                var queue = new Queue<EntryBuilder>();
                queue.Enqueue(root);
                while (queue.Count > 0)
                {
                    EntryBuilder parent = queue.Dequeue();
                    for (int i = 0; i < builders.Count; i++)
                    {
                        EntryBuilder child = builders[i];
                        if (child.IncludedInExitGroup ||
                            child.Role !=
                            WarParticipantRoleKind.FormalVassal ||
                            child.FormalSuzerainId !=
                            parent.Kingdom.data.id) continue;
                        bool obligationOnly = WarParticipantRosterRules.
                            IsObligationOnlyFromParent(child.Sources,
                                parent.Kingdom.data.id);
                        bool include = WarPeaceSettlementScopeRules.
                            ShouldIncludeVassal(
                                new WarPeaceExitParticipantFacts(
                                    activeParticipant: true,
                                    sameSide: child.Side == parent.Side,
                                    parentIncluded: true,
                                    currentFormalVassal: true,
                                    suzerainMatchesParent: true,
                                    tributary: false,
                                    entrySource: child.EntrySourceKind,
                                    hasIndependentEntrySource:
                                        !obligationOnly));
                        if (!include) continue;
                        child.IncludedInExitGroup = true;
                        child.ExitParentId = parent.Kingdom.data.id;
                        queue.Enqueue(child);
                    }
                }
            }

            var entries = new List<WarParticipantRosterEntry>(
                builders.Count);
            for (int i = 0; i < builders.Count; i++)
            {
                EntryBuilder entry = builders[i];
                entries.Add(new WarParticipantRosterEntry(entry.Kingdom,
                    entry.Side, entry.Role, entry.FormalSuzerainId,
                    entry.VassalRelationId, entry.EntrySourceKind,
                    entry.EntrySourceFingerprint, entry.Sources,
                    entry.ExitParentId, entry.IncludedInExitGroup));
            }
            pContext = new WarParticipantRosterContext(pWar,
                mainAttackerId, mainDefenderId, pExitRootKingdomId,
                entries);
            return true;
        }

        public static bool ValidateParticipantSnapshots(War pWar,
            long pExitRootKingdomId,
            IReadOnlyList<WarPeaceSettlementParticipantSnapshot> pExpected,
            out WarParticipantRosterContext pContext,
            out string pReason)
        {
            if (!TryBuild(pWar, pExitRootKingdomId, out pContext,
                    out pReason)) return false;
            return pContext.ValidateParticipantSnapshots(pExpected,
                out pReason);
        }

        private static bool TryAddSide(War pWar,
            IEnumerable<Kingdom> pKingdoms, WarParticipantSideKind pSide,
            long pMainAttackerId, long pMainDefenderId,
            List<EntryBuilder> pBuilders,
            Dictionary<long, EntryBuilder> pById,
            bool pRepairMissingMainSource, out string pReason)
        {
            pReason = "";
            try
            {
                foreach (Kingdom kingdom in pKingdoms)
                {
                    long kingdomId = kingdom?.data?.id ?? -1L;
                    if (kingdomId < 0 || pById.ContainsKey(kingdomId) ||
                        pBuilders.Count >= MaximumParticipants)
                    {
                        pReason = pBuilders.Count >= MaximumParticipants
                            ? "participant_roster_overflow"
                            : "participant_roster_invalid";
                        return false;
                    }
                    if (!TryBuildEntry(pWar, kingdom, pSide,
                            kingdomId == pMainAttackerId ||
                            kingdomId == pMainDefenderId,
                            pRepairMissingMainSource,
                            out EntryBuilder entry, out pReason))
                        return false;
                    pBuilders.Add(entry);
                    pById[kingdomId] = entry;
                }
                return true;
            }
            catch
            {
                pReason = "participant_roster_unavailable";
                return false;
            }
        }

        private static bool TryBuildEntry(War pWar, Kingdom pKingdom,
            WarParticipantSideKind pSide, bool pMainBelligerent,
            bool pRepairMissingMainSource,
            out EntryBuilder pEntry, out string pReason)
        {
            pEntry = null;
            pReason = "";
            long warId = pWar?.data?.id ?? -1L;
            long kingdomId = pKingdom?.data?.id ?? -1L;
            if (!WarParticipantEntrySourceService.Instance.
                    TryReadActiveSources(warId, kingdomId,
                        out IReadOnlyList<WarParticipantEntrySourceRecord>
                            sourceRecords))
            {
                pReason = "participant_sources_unavailable";
                return false;
            }

            if (pMainBelligerent && sourceRecords.Count == 0)
            {
                if (!pRepairMissingMainSource)
                    sourceRecords = new[]
                    {
                        new WarParticipantEntrySourceRecord
                        {
                            WarId = warId,
                            KingdomId = kingdomId,
                            SourceKind = WarParticipantEntrySourceKind.
                                MainBelligerent,
                            SourceKindId = "main_belligerent",
                            SourceKingdomId = kingdomId,
                            Active = true
                        }
                    };
                else if (!WarParticipantEntrySourceService.Instance.
                             TryRecordSource(warId, kingdomId,
                                 WarParticipantEntrySourceKind.MainBelligerent,
                                 kingdomId, LineageService.CurTime()) ||
                         !WarParticipantEntrySourceService.Instance.
                             TryReadActiveSources(warId, kingdomId,
                                 out sourceRecords))
                {
                    pReason = "participant_sources_unavailable";
                    return false;
                }
            }

            var sources = new List<WarParticipantSourceFact>(
                sourceRecords.Count);
            for (int i = 0; i < sourceRecords.Count; i++)
                sources.Add(new WarParticipantSourceFact(
                    sourceRecords[i].SourceKind,
                    sourceRecords[i].SourceKingdomId));

            if (!VassalService.TryReadActiveRelationIdentity(kingdomId,
                    out ActiveVassalRelationIdentity relation,
                    out bool relationExists))
            {
                pReason = "vassal_relation_unavailable";
                return false;
            }

            long runtimeFormalSuzerain =
                VassalService.GetSuzerainId(pKingdom);
            long runtimeTributarySuzerain =
                VassalService.GetTributarySuzerainId(pKingdom);
            WarParticipantRoleKind role;
            long formalSuzerainId = -1L;
            long relationId = -1L;
            if (pMainBelligerent)
            {
                role = WarParticipantRoleKind.MainBelligerent;
                if (relationExists)
                {
                    relationId = relation.RelationId;
                    if (!relation.IsTributary)
                        formalSuzerainId = relation.SuzerainId;
                }
            }
            else if (!relationExists)
            {
                if (runtimeFormalSuzerain >= 0 ||
                    runtimeTributarySuzerain >= 0)
                {
                    pReason = "vassal_relation_inconsistent";
                    return false;
                }
                role = WarParticipantRoleKind.Independent;
            }
            else if (relation.Ambiguous)
            {
                role = WarParticipantRoleKind.Unknown;
                relationId = relation.RelationId;
            }
            else if (relation.IsTributary)
            {
                if (runtimeTributarySuzerain != relation.SuzerainId)
                {
                    pReason = "vassal_relation_inconsistent";
                    return false;
                }
                role = WarParticipantRoleKind.Tributary;
                relationId = relation.RelationId;
            }
            else
            {
                if (runtimeFormalSuzerain != relation.SuzerainId)
                {
                    pReason = "vassal_relation_inconsistent";
                    return false;
                }
                role = WarParticipantRoleKind.FormalVassal;
                relationId = relation.RelationId;
                formalSuzerainId = relation.SuzerainId;
            }

            pEntry = new EntryBuilder
            {
                Kingdom = pKingdom,
                Side = pSide,
                Role = role,
                FormalSuzerainId = formalSuzerainId,
                VassalRelationId = relationId,
                EntrySourceKind = sources.Count == 1
                    ? sources[0].Kind
                    : WarParticipantEntrySourceKind.Unknown,
                EntrySourceFingerprint = WarParticipantRosterRules.
                    BuildSourceFingerprint(sources),
                Sources = sources
            };
            return true;
        }
    }
}
