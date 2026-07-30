using System.Collections.Generic;
using System.Data.SQLite;

public static class Date
{
    public static int getCurrentYear() => 0;
}

namespace AncientWarfare3
{
    internal static class ModClass
    {
        internal static void LogWarning(string message)
        {
        }
    }
}

namespace AncientWarfare3.core.lineage
{
    public static class WarPeaceSettlementValidationRules
    {
        public const string DetailPrefix = "war_peace_settlement:";

        public static string DetailId(long proposalId)
        {
            return DetailPrefix + proposalId;
        }

        public static bool TryResolveRecipientSide(
            long requesterKingdomId, long responderKingdomId,
            IReadOnlyList<WarPeaceSettlementParticipantSnapshot> participants,
            long recipientKingdomId, out bool recipientOnRequesterSide)
        {
            recipientOnRequesterSide =
                recipientKingdomId == requesterKingdomId;
            if (recipientOnRequesterSide ||
                recipientKingdomId == responderKingdomId) return true;
            return false;
        }
    }

    internal static class LineageService
    {
        internal static double CurTime() => 0;
    }
}

namespace AncientWarfare3.core.db
{
    public sealed class LineageArchiveManager
    {
        public static LineageArchiveManager Instance { get; } = new();
        public SQLiteConnection OperatingDB { get; set; }
        public bool IsOperational => OperatingDB != null;
    }

    public sealed class DiplomacyActionIndexSpec
    {
        public string Name;
        public string Table;
        public string Columns;
        public string Where;
        public bool Unique;
    }

    public static class DiplomacyActionIndexRules
    {
        public static IEnumerable<DiplomacyActionIndexSpec>
            GetRequiredIndexes() => new List<DiplomacyActionIndexSpec>();
    }

    public sealed class SuccessionDisputeIndexSpec
    {
        public string Name;
        public string Table;
        public string Columns;
        public string Where;
        public bool Unique;
    }

    public static class SuccessionDisputeIndexRules
    {
        public static IEnumerable<SuccessionDisputeIndexSpec>
            GetRequiredIndexes() => new List<SuccessionDisputeIndexSpec>();
    }

    public sealed class CourtDispositionIndexSpec
    {
        public string Name;
        public string Table;
        public string Columns;
    }

    public static class CourtDispositionIndexRules
    {
        public static IEnumerable<CourtDispositionIndexSpec>
            GetRequiredIndexes() => new List<CourtDispositionIndexSpec>();
    }

    public sealed class ActorArchiveTableItem { public static string GetTableName() => "ActorArchive"; }
    public sealed class CityBureauStateTableItem { public static string GetTableName() => "CityBureauState"; }
    public sealed class CityHistoryTableItem { public static string GetTableName() => "CityHistory"; }
    public sealed class CitySchoolLedgerTableItem { public static string GetTableName() => "CitySchoolLedger"; }
    public sealed class CivilServiceExamCandidateTableItem { public static string GetTableName() => "CivilServiceExamCandidate"; }
    public sealed class CivilServiceExamSessionTableItem { public static string GetTableName() => "CivilServiceExamSession"; }
    public sealed class CourtOfficerTableItem { public static string GetTableName() => "CourtOfficer"; }
    public sealed class DynastyPeriodTableItem { public static string GetTableName() => "DynastyPeriod"; }
    public sealed class DynastyTitleRegistryTableItem { public static string GetTableName() => "DynastyTitleRegistry"; }
    public sealed class EnfeoffmentTableItem { public static string GetTableName() => "Enfeoffment"; }
    public sealed class EraPeriodTableItem { public static string GetTableName() => "EraPeriod"; }
    public sealed class FamilyEdgeTableItem { public static string GetTableName() => "FamilyEdge"; }
    public sealed class FeudatoryCityTableItem { public static string GetTableName() => "FeudatoryCity"; }
    public sealed class FeudatoryTableItem { public static string GetTableName() => "Feudatory"; }
    public sealed class GeneralStateTableItem { public static string GetTableName() => "GeneralState"; }
    public sealed class HistoricalSchoolMasterTableItem { public static string GetTableName() => "HistoricalSchoolMaster"; }
    public sealed class KingdomArchiveTableItem { public static string GetTableName() => "KingdomArchive"; }
    public sealed class KingdomCoreTableItem { public static string GetTableName() => "KingdomCore"; }
    public sealed class KingdomCourtStateTableItem { public static string GetTableName() => "KingdomCourtState"; }
    public sealed class KingdomHistoryTableItem { public static string GetTableName() => "KingdomHistory"; }
    public sealed class KingdomReignTableItem { public static string GetTableName() => "KingdomReign"; }
    public sealed class LineageGroupTableItem { public static string GetTableName() => "LineageGroup"; }
    public sealed class MandateCoreCityTableItem { public static string GetTableName() => "MandateCoreCity"; }
    public sealed class MandateEventTableItem { public static string GetTableName() => "MandateEvent"; }
    public sealed class MandatePeriodTableItem { public static string GetTableName() => "MandatePeriod"; }
    public sealed class OfficialCareerStateTableItem { public static string GetTableName() => "OfficialCareerState"; }
    public sealed class PersonBiographyTableItem { public static string GetTableName() => "PersonBiography"; }
    public sealed class PosthumousTitleTableItem { public static string GetTableName() => "PosthumousTitle"; }
    public sealed class RestorationCampaignTableItem { public static string GetTableName() => "RestorationCampaign"; }
    public sealed class RoyalClaimTableItem { public static string GetTableName() => "RoyalClaim"; }
    public sealed class SchoolAffiliationTableItem { public static string GetTableName() => "SchoolAffiliation"; }
    public sealed class SchoolDebateTableItem { public static string GetTableName() => "SchoolDebate"; }
    public sealed class SchoolEventTableItem { public static string GetTableName() => "SchoolEvent"; }
    public sealed class SchoolInstitutionTableItem { public static string GetTableName() => "SchoolInstitution"; }
    public sealed class SchoolMembershipTableItem { public static string GetTableName() => "SchoolMembership"; }
    public sealed class SchoolWorkTableItem { public static string GetTableName() => "SchoolWork"; }
    public sealed class ShiBranchTableItem { public static string GetTableName() => "ShiBranch"; }
    public sealed class VassalRelationTableItem { public static string GetTableName() => "VassalRelation"; }
    public sealed class WarClaimTableItem { public static string GetTableName() => "WarClaim"; }
    public sealed class WarGoalTableItem { public static string GetTableName() => "WarGoal"; }
    public sealed class WarProjectTableItem { public static string GetTableName() => "WarProject"; }
    public sealed class WarRecordTableItem { public static string GetTableName() => "WarRecord"; }
}
