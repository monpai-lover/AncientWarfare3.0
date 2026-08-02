namespace AncientWarfare3.core.lineage
{
    internal sealed class RulerHouseholdRecord
    {
        public long RelationshipId = -1L;
        public long RulerActorId = -1L;
        public long PartnerActorId = -1L;
        public long SourceKingdomId = -1L;
        public long RecipientKingdomId = -1L;
        public RulerHouseholdKind Kind;
        public string RankCode = "";
        public int StartYear = -1;
        public double StartTime = -1d;
        public double EndTime = -1d;
        public int Status;
        public long SourceProposalId = -1L;

        public bool Active => Status == 0 && EndTime < 0d;
    }

    internal sealed class RulerHouseholdDisplayRow
    {
        public long RelationshipId = -1L;
        public long ActorId = -1L;
        public string ActorName = "";
        public string TitleKey = "";
        public string OriginRealmName = "";
        public string LineageLabel = "";
        public int Age = -1;
        public int EntryYear = -1;
        public int LivingChildren;
        public bool Alive;
        public RulerHouseholdKind Kind;
    }

    internal sealed class RulerHouseholdSnapshot
    {
        public bool Available;
        public string Reason = "household_not_ready";
        public long KingdomId = -1L;
        public long RulerActorId = -1L;
        public string RulerName = "";
        public string RulerTitle = "";
        public string RealmName = "";
        public bool RulerIsFemale;
        public int ConsortCapacity;
        public RulerHouseholdDisplayRow PrincipalWife;
        public readonly System.Collections.Generic.List<
            RulerHouseholdDisplayRow> Consorts = new();
    }

    internal sealed class RulerHouseholdOfferPreview
    {
        public bool Available;
        public string Reason = "invalid";
        public RulerHouseholdKind Kind;
        public long CandidateActorId = -1L;
        public long RulerActorId = -1L;
        public int ActiveConsorts;
        public int ConsortCapacity;
        public bool HasPrincipalWife;
    }

    internal sealed class RulerHouseholdOfferCandidate
    {
        public long ActorId = -1L;
        public Actor Actor;
        public string ActorName = "";
        public string LineageLabel = "";
        public int Age = -1;
        public bool MemberOfRulingLineage;
        public bool DirectChildOfRuler;
    }

    internal sealed class RulerHouseholdConsortRequestPreview
    {
        public bool Available;
        public string Reason = "invalid_consort_request";
        public long RulerActorId = -1L;
        public long SuggestedCandidateActorId = -1L;
        public int ActiveConsorts;
        public int ConsortCapacity;
    }

    internal sealed class RulerHouseholdOfferCandidatePool
    {
        public string Reason = "no_household_candidate";
        public long RulerActorId = -1L;
        public string RulerName = "";
        public string RulerTitle = "";
        public int ActiveConsorts;
        public int ConsortCapacity;
        public readonly System.Collections.Generic.List<
            RulerHouseholdOfferCandidate> Candidates = new();
    }
}
