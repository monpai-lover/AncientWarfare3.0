using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using AncientWarfare3.attributes;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.utils;

public static class WarPeacePersistenceRoundTripTests
{
    public static int Main()
    {
        try
        {
            using var db = OpenMemoryDatabase();
            InstallLegacyDatabaseAndMigrate(db);
            LegacyProposalDefaultsAndIndexesAreMigrated(db);
            EntrySourcesAreIdempotentBoundedAndExitMarked(db);
            EntrySourceStartupFallbackQueuesAndFlushes(db);
            SettlementStoreRoundTripsScopeTermsAndParticipants(db);
            InterruptedSettlementWithoutOuterProposalIsRecovered(db);
            ExecutedCoalitionTermsUseOneAuthoritativeProposalAndRejectOverflow(
                db);
            ParticipantLimitsRejectOversizedState(db);
            ClosedConnectionsAndTransactionFailuresFailClosed(db);
            DeferredSourceClosuresRecoverAfterLock();
            DeferredWholeWarClosuresRecoverAfterLock();
            QueuedSourceDeparturePersistsClosedInterval(db);
            Console.WriteLine(
                "War peace persistence SQLite round-trip tests passed.");
            return 0;
        }
        catch (Exception error)
        {
            error = Unwrap(error);
            Console.Error.WriteLine(error);
            if (error is ReflectionTypeLoadException typeLoad)
                foreach (Exception loader in typeLoad.LoaderExceptions)
                    Console.Error.WriteLine(loader);
            return 1;
        }
        finally
        {
            LineageArchiveManager.Instance.OperatingDB = null;
        }
    }

    private static void InstallLegacyDatabaseAndMigrate(SQLiteConnection db)
    {
        Execute(db, "CREATE TABLE WarPeaceSettlementProposal (" +
            "PROPOSAL_ID INTEGER PRIMARY KEY,WAR_ID INTEGER," +
            "REQUESTER_KINGDOM_ID INTEGER,RESPONDER_KINGDOM_ID INTEGER," +
            "SIGNED_WAR_SCORE INTEGER,TOTAL_COST INTEGER," +
            "PLAYER_INITIATED INTEGER,STATUS TEXT,RESPONSE_REASON TEXT," +
            "RECOVERY_ATTEMPTS INTEGER,CREATED_YEAR INTEGER," +
            "RESPONSE_YEAR INTEGER,CREATED_TIME REAL," +
            "RESPONSE_TIME REAL,EXECUTED_TIME REAL)");
        Execute(db, "INSERT INTO WarPeaceSettlementProposal VALUES " +
            "(1,10,100,200,25,0,0,'pending','',0,5,-1,10,-1,-1)");
        Execute(db, "CREATE TABLE DiplomacyProposal (" +
            "PROPOSAL_ID INTEGER PRIMARY KEY,DETAIL_ID TEXT,STATUS TEXT)");

        MigrateReflectedTable(db,
            typeof(WarPeaceSettlementProposalTableItem));
        MigrateReflectedTable(db,
            typeof(WarPeaceSettlementTermTableItem));
        MigrateReflectedTable(db,
            typeof(WarPeaceSettlementParticipantTableItem));
        MigrateReflectedTable(db,
            typeof(WarParticipantEntrySourceTableItem));
        EnsureWarPeaceIndexes(db);
        LineageArchiveManager.Instance.OperatingDB = db;
    }

    private static void LegacyProposalDefaultsAndIndexesAreMigrated(
        SQLiteConnection db)
    {
        using (var command = new SQLiteCommand(
                   "SELECT SCOPE_KIND,EXIT_ROOT_KINGDOM_ID FROM " +
                   "WarPeaceSettlementProposal WHERE PROPOSAL_ID=1", db))
        using (SQLiteDataReader reader = command.ExecuteReader())
        {
            True(reader.Read(), "legacy proposal remains after migration");
            Equal("coalition", reader.GetString(0),
                "legacy proposal defaults to coalition scope");
            Equal(-1L, reader.GetInt64(1),
                "legacy proposal defaults to no exit root");
        }

        AssertIndex(db, "WarParticipantEntrySource",
            "idx_WarParticipantEntry_war_kingdom_active",
            unique: false, partial: false,
            "WAR_ID", "KINGDOM_ID", "ACTIVE", "CREATED_TIME", "ENTRY_ID");
        AssertIndex(db, "WarParticipantEntrySource",
            "uq_WarParticipantEntry_active_source",
            unique: true, partial: true,
            "WAR_ID", "KINGDOM_ID", "SOURCE_KIND", "SOURCE_KINGDOM_ID");
        AssertIndex(db, "WarPeaceSettlementParticipant",
            "idx_WarPeaceParticipant_proposal_included",
            unique: false, partial: false,
            "PROPOSAL_ID", "INCLUDED_IN_EXIT_GROUP", "KINGDOM_ID");
        AssertIndex(db, "WarPeaceSettlementParticipant",
            "uq_WarPeaceParticipant_proposal_kingdom",
            unique: true, partial: false,
            "PROPOSAL_ID", "KINGDOM_ID");
        AssertIndex(db, "WarPeaceSettlementTerm",
            "idx_WarPeaceTerm_proposal_position",
            unique: false, partial: false,
            "PROPOSAL_ID", "POSITION", "TERM_ID");

        string partialSql = ScalarString(db,
            "SELECT sql FROM sqlite_master WHERE type='index' AND " +
            "name='uq_WarParticipantEntry_active_source'");
        True(partialSql.IndexOf("WHERE ACTIVE=1",
                StringComparison.OrdinalIgnoreCase) >= 0,
            "active-source uniqueness is a partial index");
    }

    private static void EntrySourcesAreIdempotentBoundedAndExitMarked(
        SQLiteConnection db)
    {
        var service = new WarParticipantEntrySourceService(db);
        True(service.TryHasSeparatePeaceExit(30, 301,
                out bool initiallyExited),
            "missing exit marker is a successful lookup");
        Equal(false, initiallyExited,
            "missing exit marker is reported as not exited");
        True(service.TryRecordSource(30, 301,
                WarParticipantEntrySourceKind.AllianceCall, 101, 12.5),
            "first alliance source is recorded");
        True(service.TryRecordSource(30, 301,
                WarParticipantEntrySourceKind.AllianceCall, 101, 13.5),
            "duplicate active source is idempotent");
        Equal(1L, ScalarLong(db,
            "SELECT COUNT(*) FROM WarParticipantEntrySource WHERE " +
            "WAR_ID=30 AND KINGDOM_ID=301 AND SOURCE_KIND='alliance_call'"),
            "duplicate record does not create a second active interval");

        True(service.TryRecordSource(30, 301,
                WarParticipantEntrySourceKind.ScriptedJoin, 102, 14.5),
            "second source kind is recorded independently");
        True(service.TryReadActiveSourceFingerprint(30, 301,
                out string sourceFingerprint),
            "active source set fingerprint can be read");
        Equal("alliance_call:101|scripted_join:102", sourceFingerprint,
            "active source fingerprint preserves every sorted source");
        True(service.TryCanJoinWar(30, 301, out bool canJoinBeforeExit),
            "same-war rejoin gate can query a missing marker");
        Equal(true, canJoinBeforeExit,
            "participant without an exit marker may join");
        IReadOnlyList<WarParticipantEntrySourceRecord> bounded =
            service.ReadActiveSources(30, 301, 1);
        Equal(1, bounded.Count, "active source reads honor the limit");

        True(service.TryEndSource(30, 301,
                WarParticipantEntrySourceKind.AllianceCall, 101, 20.5),
            "active source interval is ended");
        Equal(0L, ScalarLong(db,
            "SELECT ACTIVE FROM WarParticipantEntrySource WHERE " +
            "WAR_ID=30 AND KINGDOM_ID=301 AND SOURCE_KIND='alliance_call'"),
            "ended source becomes inactive");
        Equal(20.5, ScalarDouble(db,
            "SELECT ENDED_TIME FROM WarParticipantEntrySource WHERE " +
            "WAR_ID=30 AND KINGDOM_ID=301 AND SOURCE_KIND='alliance_call'"),
            "ended source records interval end time");

        True(service.TryMarkSeparatePeaceExit(30, 301, 21.5),
            "separate-peace exit marker is written");
        True(service.TryMarkSeparatePeaceExit(30, 301, 22.5),
            "separate-peace exit marker write is idempotent");
        True(service.HasSeparatePeaceExit(30, 301),
            "separate-peace exit marker is queryable");
        True(service.TryHasSeparatePeaceExit(30, 301, out bool exited),
            "separate-peace exit marker lookup succeeds");
        Equal(true, exited,
            "separate-peace exit marker lookup reports exited");
        True(service.TryCanJoinWar(30, 301, out bool canJoinAfterExit),
            "same-war rejoin gate reads an existing marker");
        Equal(false, canJoinAfterExit,
            "participant with an exit marker cannot rejoin the same war");
        True(service.TryReadSeparatePeaceExit(30, 301,
                out WarParticipantEntrySourceRecord exitMarker),
            "separate-peace exit marker can be read");
        Equal("separate_peace_exit", exitMarker.SourceKindId,
            "exit marker read preserves its durable source kind");
        Equal(WarParticipantEntrySourceKind.SeparatePeaceExit,
            exitMarker.SourceKind,
            "exit marker has explicit strong enum semantics");
        Equal(1L, ScalarLong(db,
            "SELECT COUNT(*) FROM WarParticipantEntrySource WHERE " +
            "WAR_ID=30 AND KINGDOM_ID=301 AND " +
            "SOURCE_KIND='separate_peace_exit'"),
            "exit marker has one durable row");
    }

    private static void EntrySourceStartupFallbackQueuesAndFlushes(
        SQLiteConnection db)
    {
        SQLiteConnection previous =
            LineageArchiveManager.Instance.OperatingDB;
        var service = new WarParticipantEntrySourceService();
        try
        {
            LineageArchiveManager.Instance.OperatingDB = null;
            True(service.TryRecordSource(31, 311,
                    WarParticipantEntrySourceKind.AllianceCall, 111, 20),
                "startup-unavailable source writes are queued in memory");
            Equal(false, service.TryCanJoinWar(31, 311,
                    out bool startupCanJoin),
                "startup-unavailable marker lookup cannot prove rejoin safety");
            Equal(false, startupCanJoin,
                "ordinary rejoin fails closed until persisted markers load");

            True(service.TryMarkSeparatePeaceExit(31, 312, 20),
                "a same-session startup exit marker is retained in memory");
            True(service.TryCanJoinWar(31, 312,
                    out bool knownExitCanJoin),
                "a known startup exit marker has an authoritative answer");
            Equal(false, knownExitCanJoin,
                "the known exit marker blocks same-war re-entry");

            LineageArchiveManager.Instance.OperatingDB = db;
            Equal(2, service.FlushPendingSources(8),
                "pending startup sources are flushed when SQLite is ready");
            Equal(1L, ScalarLong(db,
                    "SELECT COUNT(*) FROM WarParticipantEntrySource WHERE " +
                    "WAR_ID=31 AND KINGDOM_ID=311 AND " +
                    "SOURCE_KIND='alliance_call' AND ACTIVE=1"),
                "the startup source becomes durable after recovery");
            Equal(1L, ScalarLong(db,
                    "SELECT COUNT(*) FROM WarParticipantEntrySource WHERE " +
                    "WAR_ID=31 AND KINGDOM_ID=312 AND " +
                    "SOURCE_KIND='separate_peace_exit' AND ACTIVE=1"),
                "the known startup exit marker becomes durable after recovery");
        }
        finally
        {
            service.ClearRuntime();
            LineageArchiveManager.Instance.OperatingDB = previous;
        }
    }

    private static void ParticipantLimitsRejectOversizedState(
        SQLiteConnection db)
    {
        const int maximumParticipants = 64;
        IWarPeaceSettlementStore store = CreateSettlementStore();
        var oversizedDraft = new WarPeaceSettlementDraft
        {
            WarId = 42,
            RequesterKingdomId = 421,
            ResponderKingdomId = 422
        };
        for (int i = 0; i <= maximumParticipants; i++)
            oversizedDraft.Participants.Add(
                new WarPeaceSettlementParticipantSnapshot
                {
                    KingdomId = 4200 + i
                });

        Equal(false, store.TryCreate(oversizedDraft,
                Array.Empty<WarPeaceSettlementTerm>(), out _, out _),
            "settlement creation rejects participant limit plus one");
        Equal(0L, ScalarLong(db,
            "SELECT COUNT(*) FROM WarPeaceSettlementProposal WHERE WAR_ID=42"),
            "oversized draft creates no proposal row");

        Execute(db, "INSERT INTO WarPeaceSettlementProposal " +
            "(PROPOSAL_ID,WAR_ID,REQUESTER_KINGDOM_ID," +
            "RESPONDER_KINGDOM_ID,SCOPE_KIND,EXIT_ROOT_KINGDOM_ID," +
            "SIGNED_WAR_SCORE,TOTAL_COST,PLAYER_INITIATED,STATUS," +
            "RESPONSE_REASON,RECOVERY_ATTEMPTS,CREATED_YEAR," +
            "RESPONSE_YEAR,CREATED_TIME,RESPONSE_TIME,EXECUTED_TIME) " +
            "VALUES (900,43,431,432,'coalition',-1,0,0,0,'pending',''," +
            "0,0,-1,0,-1,-1)");
        for (int i = 0; i <= maximumParticipants; i++)
            Execute(db, "INSERT INTO WarPeaceSettlementParticipant " +
                "(PARTICIPANT_ID,PROPOSAL_ID,KINGDOM_ID,SIDE_KIND," +
                "PARTICIPANT_ROLE,EXIT_PARENT_ID,VASSAL_RELATION_ID," +
                "ENTRY_SOURCE_KIND,INCLUDED_IN_EXIT_GROUP) VALUES (" +
                (9000 + i) + ",900," + (4300 + i) + ",'attacker',''," +
                "-1,-1,'scripted_join',1)");

        Equal(false, store.TryRead(900,
                out WarPeaceSettlementProposal oversized),
            "participant limit plus one fails the entire proposal read");
        Equal(null, oversized,
            "oversized participant read returns no truncated proposal");
    }

    private static void SettlementStoreRoundTripsScopeTermsAndParticipants(
        SQLiteConnection db)
    {
        IWarPeaceSettlementStore store = CreateSettlementStore();
        var sourceTerm = new WarPeaceSettlementTerm
        {
            Position = 0,
            Kind = WarPeaceTermKind.GoldPayment,
            Cost = 12,
            FromKingdomId = 402,
            ToKingdomId = 401,
            ResourceId = "gold",
            Amount = 120
        };
        var draft = new WarPeaceSettlementDraft
        {
            WarId = 40,
            RequesterKingdomId = 401,
            ResponderKingdomId = 402,
            Scope = WarPeaceSettlementScopeKind.SeparateParticipant,
            ExitRootKingdomId = 402,
            SignedWarScore = 20
        };
        draft.Participants.Add(new WarPeaceSettlementParticipantSnapshot
        {
            KingdomId = 402,
            SideKind = "defender",
            ParticipantRole = "exit_root",
            ExitParentId = -1,
            VassalRelationId = -1,
            EntrySourceKind = WarParticipantEntrySourceKind.AllianceCall,
            EntrySourceFingerprint =
                "alliance_call:401|scripted_join:499",
            IncludedInExitGroup = true
        });

        True(store.TryCreate(draft, new[] { sourceTerm },
                out WarPeaceSettlementProposal created, out string reason),
            "separate proposal persists: " + reason);
        Equal(-1L, sourceTerm.TermId,
            "store term id allocation does not mutate caller term");
        True(store.TryRead(created.ProposalId,
                out WarPeaceSettlementProposal restored),
            "persisted proposal reads back");
        Equal(WarPeaceSettlementScopeKind.SeparateParticipant,
            restored.Scope, "scope survives SQLite round-trip");
        Equal(0, restored.CreatedYear,
            "settlement creation year survives SQLite round-trip");
        Equal(-1, restored.ResponseYear,
            "pending settlement has no response year");
        Equal(402L, restored.ExitRootKingdomId,
            "exit root survives SQLite round-trip");
        Equal(1, restored.Terms.Count, "term survives SQLite round-trip");
        Equal("gold", restored.Terms[0].ResourceId,
            "term payload survives SQLite round-trip");
        Equal(1, restored.Participants.Count,
            "participant snapshot survives SQLite round-trip");
        Equal(true, restored.Participants[0].IncludedInExitGroup,
            "participant inclusion survives SQLite round-trip");
        Equal(WarParticipantEntrySourceKind.AllianceCall,
            restored.Participants[0].EntrySourceKind,
            "participant source survives SQLite round-trip");
        Equal("alliance_call:401|scripted_join:499",
            restored.Participants[0].EntrySourceFingerprint,
            "participant source-set fingerprint survives SQLite round-trip");

        var legacyDraft = new WarPeaceSettlementDraft
        {
            WarId = 142,
            RequesterKingdomId = 1421,
            ResponderKingdomId = 1422
        };
        True(store.TryCreate(legacyDraft,
                Array.Empty<WarPeaceSettlementTerm>(),
                out WarPeaceSettlementProposal legacy, out _),
            "legacy coalition proposal persists without participant rows");
        var backfill = new[]
        {
            new WarPeaceSettlementParticipantSnapshot
            {
                KingdomId = 1421,
                SideKind = "attacker",
                ParticipantRole = "main_belligerent"
            },
            new WarPeaceSettlementParticipantSnapshot
            {
                KingdomId = 1422,
                SideKind = "defender",
                ParticipantRole = "main_belligerent"
            }
        };
        True(store.TryBackfillParticipants(legacy.ProposalId, backfill),
            "authoritative participants backfill a legacy coalition proposal");
        True(store.TryBackfillParticipants(legacy.ProposalId, backfill),
            "legacy participant backfill is idempotent");
        True(store.TryRead(legacy.ProposalId,
                out WarPeaceSettlementProposal backfilled),
            "backfilled legacy proposal reads back");
        Equal(2, backfilled.Participants.Count,
            "legacy participant roster survives SQLite recovery");

        True(store.TrySetStatus(created.ProposalId,
                WarPeaceSettlementStatus.Pending,
                WarPeaceSettlementStatus.Accepted, "accepted"),
            "separate proposal records its acceptance year");
        True(store.TryRead(created.ProposalId,
                out WarPeaceSettlementProposal accepted),
            "accepted proposal reads back");
        Equal(0, accepted.ResponseYear,
            "settlement response year survives SQLite round-trip");
        True(store.TrySetStatus(created.ProposalId,
                WarPeaceSettlementStatus.Accepted,
                WarPeaceSettlementStatus.TermsApplied, ""),
            "separate proposal reaches terms-applied state");
        Equal(false, store.HasExecutedCoalitionSettlement(40),
            "separate proposal does not complete coalition settlement");
        Equal(0, store.ReadExecutedCoalitionTerms(40).Count,
            "separate terms do not appear in coalition result");

        var duplicateDraft = new WarPeaceSettlementDraft
        {
            WarId = 41,
            RequesterKingdomId = 411,
            ResponderKingdomId = 412
        };
        duplicateDraft.Participants.Add(new WarPeaceSettlementParticipantSnapshot
            { KingdomId = 412 });
        duplicateDraft.Participants.Add(new WarPeaceSettlementParticipantSnapshot
            { KingdomId = 412 });
        Equal(false, store.TryCreate(duplicateDraft,
                Array.Empty<WarPeaceSettlementTerm>(), out _, out _),
            "duplicate participant rolls back settlement transaction");
        Equal(0L, ScalarLong(db,
            "SELECT COUNT(*) FROM WarPeaceSettlementProposal WHERE WAR_ID=41"),
            "failed participant insert rolls back proposal row");
    }

    private static void InterruptedSettlementWithoutOuterProposalIsRecovered(
        SQLiteConnection db)
    {
        var store = (WarPeaceSettlementStore)
            CreateSettlementStore();
        var orphanDraft = new WarPeaceSettlementDraft
        {
            WarId = 46,
            RequesterKingdomId = 461,
            ResponderKingdomId = 462,
            Scope = WarPeaceSettlementScopeKind.SeparateParticipant,
            ExitRootKingdomId = 462
        };
        True(store.TryCreate(orphanDraft,
                Array.Empty<WarPeaceSettlementTerm>(),
                out WarPeaceSettlementProposal orphan, out _),
            "interrupted preparation leaves a durable inner settlement");

        var linkedDraft = new WarPeaceSettlementDraft
        {
            WarId = 47,
            RequesterKingdomId = 461,
            ResponderKingdomId = 463,
            Scope = WarPeaceSettlementScopeKind.SeparateParticipant,
            ExitRootKingdomId = 463
        };
        True(store.TryCreate(linkedDraft,
                Array.Empty<WarPeaceSettlementTerm>(),
                out WarPeaceSettlementProposal linked, out _),
            "normal preparation creates the inner settlement");
        Execute(db, "INSERT INTO DiplomacyProposal " +
            "(PROPOSAL_ID,DETAIL_ID,STATUS) VALUES (4700,'" +
            WarPeaceSettlementValidationRules.DetailId(linked.ProposalId) +
            "','pending')");

        True(store.TryCancelOneOrphanedPendingForKingdom(461,
                currentYear: 1, out long cancelledId),
            "recovery query succeeds after an interrupted outer insert");
        Equal(orphan.ProposalId, cancelledId,
            "recovery selects the unreferenced inner settlement");
        Equal("cancelled", ScalarString(db,
                "SELECT STATUS FROM WarPeaceSettlementProposal WHERE " +
                "PROPOSAL_ID=" + orphan.ProposalId),
            "orphan settlement is durably cancelled");
        Equal("pending", ScalarString(db,
                "SELECT STATUS FROM WarPeaceSettlementProposal WHERE " +
                "PROPOSAL_ID=" + linked.ProposalId),
            "a settlement referenced by an outer proposal remains pending");
    }

    private static void
        ExecutedCoalitionTermsUseOneAuthoritativeProposalAndRejectOverflow(
            SQLiteConnection db)
    {
        IWarPeaceSettlementStore store = CreateSettlementStore();
        var firstDraft = new WarPeaceSettlementDraft
        {
            WarId = 44,
            RequesterKingdomId = 441,
            ResponderKingdomId = 442
        };
        True(store.TryCreate(firstDraft, new[]
            {
                new WarPeaceSettlementTerm
                {
                    Position = 0,
                    Kind = WarPeaceTermKind.GoldPayment,
                    FromKingdomId = 442,
                    ToKingdomId = 441,
                    ResourceId = "first"
                }
            }, out WarPeaceSettlementProposal first, out string firstReason),
            "first coalition proposal persists: " + firstReason);
        True(store.TrySetStatus(first.ProposalId,
                WarPeaceSettlementStatus.Pending,
                WarPeaceSettlementStatus.TermsApplied, ""),
            "first coalition proposal reaches terms-applied state");

        var secondDraft = new WarPeaceSettlementDraft
        {
            WarId = 44,
            RequesterKingdomId = 441,
            ResponderKingdomId = 442
        };
        True(store.TryCreate(secondDraft, new[]
            {
                new WarPeaceSettlementTerm
                {
                    Position = 0,
                    Kind = WarPeaceTermKind.GoldPayment,
                    FromKingdomId = 442,
                    ToKingdomId = 441,
                    ResourceId = "second"
                }
            }, out WarPeaceSettlementProposal second, out string secondReason),
            "second coalition proposal persists: " + secondReason);
        True(store.TrySetStatus(second.ProposalId,
                WarPeaceSettlementStatus.Pending,
                WarPeaceSettlementStatus.TermsApplied, ""),
            "second coalition proposal reaches terms-applied state");

        True(store.TryReadExecutedCoalitionTerms(44,
                out IReadOnlyList<WarPeaceSettlementTerm> authoritative),
            "authoritative coalition terms can be read");
        Equal(1, authoritative.Count,
            "executed coalition terms never mix multiple proposals");
        Equal("second", authoritative[0].ResourceId,
            "latest completed coalition proposal is authoritative");

        Execute(db, "INSERT INTO WarPeaceSettlementProposal " +
            "(PROPOSAL_ID,WAR_ID,REQUESTER_KINGDOM_ID," +
            "RESPONDER_KINGDOM_ID,SCOPE_KIND,EXIT_ROOT_KINGDOM_ID," +
            "SIGNED_WAR_SCORE,TOTAL_COST,PLAYER_INITIATED,STATUS," +
            "RESPONSE_REASON,RECOVERY_ATTEMPTS,CREATED_YEAR," +
            "RESPONSE_YEAR,CREATED_TIME,RESPONSE_TIME,EXECUTED_TIME) " +
            "VALUES (950,45,451,452,'coalition',-1,100,0,0," +
            "'terms_applied','',0,0,-1,0,-1,-1)");
        for (int i = 0; i <= 256; i++)
            Execute(db, "INSERT INTO WarPeaceSettlementTerm " +
                "(TERM_ID,PROPOSAL_ID,POSITION,TERM_KIND,COST," +
                "FROM_KINGDOM_ID,TO_KINGDOM_ID,RESOURCE_ID,AMOUNT," +
                "DURATION_YEARS,CITY_ID,CAPTIVE_ACTOR_ID,CLAIM_ID," +
                "WAR_GOAL_ID,FROZEN_OCCUPATION,CORE_OR_CLAIM_BASIS," +
                "APPLY_STATUS,APPLY_REASON,APPLIED_TIME," +
                "BASELINE_CAPTURED,SOURCE_AMOUNT_BEFORE," +
                "TARGET_AMOUNT_BEFORE,SOURCE_CITY_ID,TARGET_CITY_ID) " +
                "VALUES (" + (95000 + i) + ",950," + i +
                ",'GoldPayment',0,452,451,'gold',0,0,-1,-1,-1,-1," +
                "0,0,'applied','',0,1,0,0,-1,-1)");

        Equal(false, store.TryReadExecutedCoalitionTerms(45,
                out IReadOnlyList<WarPeaceSettlementTerm> overflow),
            "executed coalition term overflow fails the entire read");
        Equal(0, overflow.Count,
            "overflow read never exposes a truncated partial treaty");
    }

    private static void ClosedConnectionsAndTransactionFailuresFailClosed(
        SQLiteConnection db)
    {
        IWarPeaceSettlementStore store = CreateSettlementStore();
        var draft = new WarPeaceSettlementDraft
        {
            WarId = 50,
            RequesterKingdomId = 501,
            ResponderKingdomId = 502
        };

        string databasePath = Path.Combine(Path.GetTempPath(),
            "aw3-war-peace-lock-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using var lockOwner = OpenFileDatabase(databasePath);
            InstallLegacyDatabaseAndMigrate(lockOwner);
            using var contender = OpenFileDatabase(databasePath);
            Execute(contender, "PRAGMA busy_timeout=1");
            LineageArchiveManager.Instance.OperatingDB = contender;
            var mainSourceService =
                new WarParticipantEntrySourceService(contender);

            Execute(lockOwner, "BEGIN EXCLUSIVE");
            try
            {
                Equal(false, store.TryCreate(draft,
                        Array.Empty<WarPeaceSettlementTerm>(), out _, out _),
                    "settlement transaction begin failure is contained");
                Equal(false, new WarParticipantEntrySourceService(contender)
                        .TryRecordSource(50, 501,
                            WarParticipantEntrySourceKind.ScriptedJoin,
                            502, 30),
                    "entry-source transaction begin failure is contained");
                Equal(true, mainSourceService.TryRecordSource(50, 503,
                        WarParticipantEntrySourceKind.MainBelligerent,
                        503, 30),
                    "a transiently locked main-belligerent write is retained for retry");
                Equal(0, mainSourceService.FlushPendingSources(8),
                    "the main-belligerent retry remains queued while locked");
                Equal(false, new WarParticipantEntrySourceService(contender)
                        .TryHasSeparatePeaceExit(50, 501,
                            out bool lockExited),
                    "locked marker lookup reports query failure");
                Equal(false, lockExited,
                    "try-style locked marker lookup exposes no invented exit");
                True(new WarParticipantEntrySourceService(contender)
                        .HasSeparatePeaceExit(50, 501),
                    "legacy locked marker lookup blocks same-war re-entry");
                Equal(false, new WarParticipantEntrySourceService(contender)
                        .TryCanJoinWar(50, 501, out bool lockCanJoin),
                    "locked rejoin-gate lookup reports failure");
                Equal(false, lockCanJoin,
                    "locked rejoin gate fails closed");
            }
            finally
            {
                Execute(lockOwner, "ROLLBACK");
            }
            Equal(1, mainSourceService.FlushPendingSources(8),
                "the main-belligerent retry flushes after unlock");
            Equal(1L, ScalarLong(contender,
                    "SELECT COUNT(*) FROM WarParticipantEntrySource WHERE " +
                    "WAR_ID=50 AND KINGDOM_ID=503 AND " +
                    "SOURCE_KIND='main_belligerent' AND ACTIVE=1"),
                "the recovered main-belligerent source is durable");
        }
        finally
        {
            LineageArchiveManager.Instance.OperatingDB = db;
            SQLiteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }

        using SQLiteConnection closed = OpenMemoryDatabase();
        closed.Close();
        LineageArchiveManager.Instance.OperatingDB = closed;
        try
        {
            Equal(false, store.TryHasExecutedCoalitionSettlement(50,
                    out bool executed),
                "closed connection coalition lookup reports failure");
            Equal(false, executed,
                "closed connection coalition lookup is fail-closed");
            Equal(false, store.TryReadExecutedCoalitionTerms(50,
                    out IReadOnlyList<WarPeaceSettlementTerm> terms),
                "closed connection coalition term read reports failure");
            Equal(0, terms.Count,
                "closed connection term read returns no partial rows");
            Equal(false, store.TryCreate(draft,
                    Array.Empty<WarPeaceSettlementTerm>(), out _, out _),
                "closed connection settlement creation is contained");
            Equal(false, new WarParticipantEntrySourceService(closed)
                    .TryRecordSource(50, 501,
                        WarParticipantEntrySourceKind.ScriptedJoin,
                        502, 30),
                "closed connection entry-source write is contained");
            Equal(false, new WarParticipantEntrySourceService(closed)
                    .TryHasSeparatePeaceExit(50, 501,
                        out bool closedExited),
                "closed connection marker lookup reports query failure");
            Equal(false, closedExited,
                "try-style closed marker lookup exposes no invented exit");
            True(new WarParticipantEntrySourceService(closed)
                    .HasSeparatePeaceExit(50, 501),
                "legacy closed marker lookup blocks same-war re-entry");
            Equal(false, new WarParticipantEntrySourceService(closed)
                    .TryCanJoinWar(50, 501, out bool closedCanJoin),
                "closed rejoin-gate lookup reports failure");
            Equal(false, closedCanJoin,
                "closed rejoin gate fails closed");
        }
        finally
        {
            LineageArchiveManager.Instance.OperatingDB = db;
        }

        using SQLiteConnection missingSchema = OpenMemoryDatabase();
        var missingSchemaService =
            new WarParticipantEntrySourceService(missingSchema);
        Equal(false, missingSchemaService.TryRecordSource(50, 504,
                WarParticipantEntrySourceKind.MainBelligerent, 504, 30),
            "a permanent schema error is not accepted as queued work");
        Equal(0, missingSchemaService.FlushPendingSources(8),
            "a permanent schema error creates no hidden retry");
    }

    private static void DeferredSourceClosuresRecoverAfterLock()
    {
        string databasePath = Path.Combine(Path.GetTempPath(),
            "aw3-war-source-close-" + Guid.NewGuid().ToString("N") +
            ".db");
        try
        {
            using var lockOwner = OpenFileDatabase(databasePath);
            InstallLegacyDatabaseAndMigrate(lockOwner);
            using var contender = OpenFileDatabase(databasePath);
            Execute(contender, "PRAGMA busy_timeout=1");
            var service = new WarParticipantEntrySourceService(contender);
            True(service.TryRecordSource(51, 511,
                    WarParticipantEntrySourceKind.AllianceCall, 501, 30),
                "the source exists before the deferred close");

            Execute(lockOwner, "BEGIN EXCLUSIVE");
            try
            {
                True(service.TryEndAllActiveSources(51, 511, 40),
                    "a locked source close is retained for bounded retry");
            }
            finally
            {
                Execute(lockOwner, "ROLLBACK");
            }

            Equal(1, service.FlushPendingSources(8),
                "the deferred close flushes after the lock clears");
            Equal(0L, ScalarLong(contender,
                    "SELECT COUNT(*) FROM WarParticipantEntrySource " +
                    "WHERE WAR_ID=51 AND KINGDOM_ID=511 AND ACTIVE=1 " +
                    "AND SOURCE_KIND<>'separate_peace_exit'"),
                "the old active source is closed after recovery");

            True(service.TryRecordSource(51, 511,
                    WarParticipantEntrySourceKind.ScriptedJoin, 502, 50),
                "a later rejoin source can be recorded");
            True(service.TryEndAllActiveSources(51, 511, 40),
                "replaying the old close remains idempotent");
            Equal(1L, ScalarLong(contender,
                    "SELECT COUNT(*) FROM WarParticipantEntrySource " +
                    "WHERE WAR_ID=51 AND KINGDOM_ID=511 AND ACTIVE=1 " +
                    "AND SOURCE_KIND='scripted_join'"),
                "an old deferred close cannot erase a later rejoin source");
        }
        finally
        {
            SQLiteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private static void DeferredWholeWarClosuresRecoverAfterLock()
    {
        string databasePath = Path.Combine(Path.GetTempPath(),
            "aw3-war-source-close-all-" + Guid.NewGuid().ToString("N") +
            ".db");
        try
        {
            using var lockOwner = OpenFileDatabase(databasePath);
            InstallLegacyDatabaseAndMigrate(lockOwner);
            using var contender = OpenFileDatabase(databasePath);
            Execute(contender, "PRAGMA busy_timeout=1");
            var service = new WarParticipantEntrySourceService(contender);
            True(service.TryRecordSource(53, 531,
                    WarParticipantEntrySourceKind.MainBelligerent, 531, 30),
                "the first participant source exists before whole-war close");
            True(service.TryRecordSource(53, 532,
                    WarParticipantEntrySourceKind.AllianceCall, 531, 31),
                "the second participant source exists before whole-war close");
            True(service.TryMarkSeparatePeaceExit(53, 533, 32),
                "a separate-peace exit marker exists before whole-war close");

            Execute(lockOwner, "BEGIN EXCLUSIVE");
            try
            {
                True(service.TryEndAllActiveSourcesForWar(53, 40),
                    "a locked whole-war close is retained for retry");
            }
            finally
            {
                Execute(lockOwner, "ROLLBACK");
            }

            True(service.TryRecordSource(53, 534,
                    WarParticipantEntrySourceKind.ScriptedJoin, 531, 50),
                "a participant may join a later reconstructed war interval");
            Equal(1, service.FlushPendingSources(8),
                "the deferred whole-war close flushes after the lock clears");
            Equal(0L, ScalarLong(contender,
                    "SELECT COUNT(*) FROM WarParticipantEntrySource WHERE " +
                    "WAR_ID=53 AND ACTIVE=1 AND " +
                    "SOURCE_KIND<>'separate_peace_exit' AND CREATED_TIME<=40"),
                "whole-war recovery closes every source active at war end");
            Equal(1L, ScalarLong(contender,
                    "SELECT COUNT(*) FROM WarParticipantEntrySource WHERE " +
                    "WAR_ID=53 AND KINGDOM_ID=534 AND ACTIVE=1 AND " +
                    "SOURCE_KIND='scripted_join'"),
                "an old whole-war close cannot erase a later source");
            Equal(1L, ScalarLong(contender,
                    "SELECT COUNT(*) FROM WarParticipantEntrySource WHERE " +
                    "WAR_ID=53 AND KINGDOM_ID=533 AND ACTIVE=1 AND " +
                    "SOURCE_KIND='separate_peace_exit'"),
                "whole-war close preserves separate-peace exit markers");
        }
        finally
        {
            SQLiteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private static void QueuedSourceDeparturePersistsClosedInterval(
        SQLiteConnection db)
    {
        SQLiteConnection previous =
            LineageArchiveManager.Instance.OperatingDB;
        var service = new WarParticipantEntrySourceService();
        try
        {
            LineageArchiveManager.Instance.OperatingDB = null;
            True(service.TryRecordSource(52, 521,
                    WarParticipantEntrySourceKind.AllianceCall, 501, 30),
                "a startup source is queued before SQLite is ready");
            True(service.TryEndAllActiveSources(52, 521, 40),
                "departure preserves the queued source interval");

            LineageArchiveManager.Instance.OperatingDB = db;
            Equal(2, service.FlushPendingSources(8),
                "the deferred close and closed source interval both flush");
            Equal(1L, ScalarLong(db,
                    "SELECT COUNT(*) FROM WarParticipantEntrySource WHERE " +
                    "WAR_ID=52 AND KINGDOM_ID=521 AND " +
                    "SOURCE_KIND='alliance_call' AND ACTIVE=0 AND " +
                    "CREATED_TIME=30 AND ENDED_TIME=40"),
                "queued join and leave survive as one closed durable interval");
        }
        finally
        {
            service.ClearRuntime();
            LineageArchiveManager.Instance.OperatingDB = previous;
        }
    }

    private static IWarPeaceSettlementStore CreateSettlementStore()
    {
        return new WarPeaceSettlementStore();
    }

    private static void AssertIndex(SQLiteConnection db, string table,
        string index, bool unique, bool partial, params string[] columns)
    {
        bool found = false;
        using (var command = new SQLiteCommand(
                   "PRAGMA index_list(" + table + ")", db))
        using (SQLiteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                if (!string.Equals(reader.GetString(1), index,
                        StringComparison.Ordinal)) continue;
                found = true;
                Equal(unique, reader.GetInt32(2) != 0,
                    index + " unique flag");
                Equal(partial, reader.GetInt32(4) != 0,
                    index + " partial flag");
            }
        }
        True(found, index + " exists");

        var actual = new List<string>();
        using (var command = new SQLiteCommand(
                   "PRAGMA index_info(" + index + ")", db))
        using (SQLiteDataReader reader = command.ExecuteReader())
            while (reader.Read()) actual.Add(reader.GetString(2));
        Equal(string.Join(",", columns), string.Join(",", actual),
            index + " column order");
    }

    private static void MigrateReflectedTable(SQLiteConnection db,
        Type tableType)
    {
        TableDefAttribute table =
            tableType.GetCustomAttribute<TableDefAttribute>();
        List<SQLiteHelper.ColumnDef> columns = tableType.GetFields()
            .Select(field =>
            {
                TableItemDefAttribute attribute =
                    field.GetCustomAttribute<TableItemDefAttribute>() ??
                    new TableItemDefAttribute();
                SQLiteHelper.ColumnType type = field.FieldType.Name.ToLower()
                    switch
                    {
                        "string" => SQLiteHelper.ColumnType.TEXT,
                        "boolean" => SQLiteHelper.ColumnType.INTEGER,
                        "byte" => SQLiteHelper.ColumnType.INTEGER,
                        "sbyte" => SQLiteHelper.ColumnType.INTEGER,
                        "int16" => SQLiteHelper.ColumnType.INTEGER,
                        "uint16" => SQLiteHelper.ColumnType.INTEGER,
                        "int32" => SQLiteHelper.ColumnType.INTEGER,
                        "uint32" => SQLiteHelper.ColumnType.INTEGER,
                        "int64" => SQLiteHelper.ColumnType.INTEGER,
                        "uint64" => SQLiteHelper.ColumnType.INTEGER,
                        "single" => SQLiteHelper.ColumnType.REAL,
                        "double" => SQLiteHelper.ColumnType.REAL,
                        _ => SQLiteHelper.ColumnType.BLOB
                    };
                string name = string.IsNullOrEmpty(attribute.Name)
                    ? field.Name.ToUpper()
                    : attribute.Name;
                return new SQLiteHelper.ColumnDef(name, type,
                    attribute.IsPrimary, attribute.IsUnique,
                    attribute.IsNotNull, attribute.DefaultValue,
                    attribute.Check);
            }).ToList();
        if (db.TableExists(table.Name))
        {
            SQLiteHelper.RegisterTable(table.Name, columns);
            db.AddMissingColumns(table.Name, columns);
        }
        else
        {
            db.CreateTable(table.Name, columns);
        }
    }

    private static void EnsureWarPeaceIndexes(SQLiteConnection db)
    {
        foreach (LineageArchiveIndexSpec spec in
                 LineageArchiveIndexRules.GetRequiredIndexes())
        {
            if (spec.table !=
                    WarParticipantEntrySourceTableItem.GetTableName() &&
                spec.table !=
                    WarPeaceSettlementParticipantTableItem.GetTableName() &&
                spec.table !=
                    WarPeaceSettlementTermTableItem.GetTableName())
                continue;
            Execute(db, spec.BuildSql());
        }
    }

    private static SQLiteConnection OpenMemoryDatabase()
    {
        var db = new SQLiteConnection(
            "Data Source=:memory:;Version=3;New=True;Pooling=False;");
        db.Open();
        return db;
    }

    private static SQLiteConnection OpenFileDatabase(string databasePath)
    {
        var db = new SQLiteConnection("Data Source=" + databasePath +
            ";Version=3;Pooling=False;Default Timeout=1;");
        db.Open();
        return db;
    }

    private static void Execute(SQLiteConnection db, string sql)
    {
        using var command = new SQLiteCommand(sql, db);
        command.ExecuteNonQuery();
    }

    private static long ScalarLong(SQLiteConnection db, string sql)
    {
        using var command = new SQLiteCommand(sql, db);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static double ScalarDouble(SQLiteConnection db, string sql)
    {
        using var command = new SQLiteCommand(sql, db);
        return Convert.ToDouble(command.ExecuteScalar());
    }

    private static string ScalarString(SQLiteConnection db, string sql)
    {
        using var command = new SQLiteCommand(sql, db);
        return Convert.ToString(command.ExecuteScalar());
    }

    private static Exception Unwrap(Exception error)
    {
        while (error is TargetInvocationException invocation &&
               invocation.InnerException != null)
            error = invocation.InnerException;
        return error;
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException(name + ": expected " +
                expected + ", got " + actual);
    }
}
