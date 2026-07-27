using System;
using System.Collections.Generic;
using AncientWarfare3.core.schools;

internal static class Program
{
    private static int Main()
    {
        try
        {
            var candidates = new List<HistoricalSchoolEliteCandidate>
            {
                Candidate(1, 50, HistoricalSchoolElitePriority.CentralOfficial),
                Candidate(1, 10, HistoricalSchoolElitePriority.CentralOfficial),
                Candidate(1, 10, HistoricalSchoolElitePriority.Ruler),
                Candidate(1, 20, HistoricalSchoolElitePriority.Heir),
                Candidate(1, 30, HistoricalSchoolElitePriority.FeudatoryPrince),
                Candidate(1, 40, HistoricalSchoolElitePriority.TitledNoble),
                Candidate(2, 60, HistoricalSchoolElitePriority.Ruler),
                Candidate(2, 70, HistoricalSchoolElitePriority.CentralOfficial)
            };
            IReadOnlyList<HistoricalSchoolEliteCandidate> selected =
                HistoricalSchoolEliteEnrollmentRules.SelectCandidates(
                    candidates, pYear: 120,
                    pPerRealmLimit: HistoricalSchoolEliteEnrollmentRules
                        .MaxSuccessfulJoinsPerRealmPerYear);
            Equal(7, selected.Count,
                "each realm has an independent relaxed enrollment budget");
            Equal(10L, selected[0].ActorId,
                "duplicate roles collapse to the actor's highest priority");
            Equal(20L, selected[1].ActorId,
                "heir follows ruler");
            Equal(30L, selected[2].ActorId,
                "feudatory prince follows heir");
            Equal(40L, selected[3].ActorId,
                "title holder fills the fourth annual slot");

            var rotating = new List<HistoricalSchoolEliteCandidate>
            {
                Candidate(3, 101, HistoricalSchoolElitePriority.CentralOfficial),
                Candidate(3, 102, HistoricalSchoolElitePriority.CentralOfficial),
                Candidate(3, 103, HistoricalSchoolElitePriority.CentralOfficial),
                Candidate(3, 104, HistoricalSchoolElitePriority.CentralOfficial)
            };
            IReadOnlyList<HistoricalSchoolEliteCandidate> yearZero =
                HistoricalSchoolEliteEnrollmentRules.SelectCandidates(
                    rotating, pYear: 0, pPerRealmLimit: 2);
            IReadOnlyList<HistoricalSchoolEliteCandidate> yearOne =
                HistoricalSchoolEliteEnrollmentRules.SelectCandidates(
                    rotating, pYear: 1, pPerRealmLimit: 2);
            Equal(false, yearZero[0].ActorId == yearOne[0].ActorId &&
                         yearZero[1].ActorId == yearOne[1].ActorId,
                "same-priority candidates rotate across years");

            Equal(1, HistoricalSchoolEliteEnrollmentRules
                    .FrameAttemptBudget(12),
                "one scheduler frame attempts at most one elite");
            Equal(0, HistoricalSchoolEliteEnrollmentRules
                    .FrameAttemptBudget(0),
                "an empty work list consumes no frame budget");
            Equal(1, HistoricalSchoolEliteEnrollmentRules
                    .RealmPreparationBudget(12),
                "one scheduler frame prepares at most one realm");
            Equal(0, HistoricalSchoolEliteEnrollmentRules
                    .RealmPreparationBudget(0),
                "an empty realm list consumes no preparation budget");
            Equal(true, HistoricalSchoolEliteEnrollmentRules
                    .NeedsEnrollment(isValid: true,
                        hasMembership: false, writePending: false),
                "a valid school-less elite is eligible");
            Equal(false, HistoricalSchoolEliteEnrollmentRules
                    .NeedsEnrollment(isValid: true,
                        hasMembership: true, writePending: false),
                "an existing member is skipped");
            Equal(false, HistoricalSchoolEliteEnrollmentRules
                    .NeedsEnrollment(isValid: true,
                        hasMembership: false, writePending: true),
                "an actor with a queued join is not duplicated");
            Equal(true, HistoricalSchoolEliteEnrollmentRules
                    .IsNobleCandidateEligible(valid: true, adult: true,
                        noble: true, domestic: true),
                "an adult domestic noble may seek education");
            Equal(false, HistoricalSchoolEliteEnrollmentRules
                    .IsNobleCandidateEligible(valid: true, adult: false,
                        noble: true, domestic: true),
                "an underage noble cannot start an education journey");
            Equal(true, HistoricalSchoolEliteEnrollmentRules.
                    CanReserveAdmission(currentReservations: 11,
                        annualLimit: 12),
                "the last annual admission seat may be reserved");
            Equal(false, HistoricalSchoolEliteEnrollmentRules.
                    CanReserveAdmission(currentReservations: 12,
                        annualLimit: 12),
                "arrivals cannot exceed the current year's realm cap");
            Equal(true, HistoricalSchoolEliteEnrollmentRules.
                    CanReserveTeacher(committedDisciples: 7,
                        pendingAdmissions: 0, directDiscipleCap: 8),
                "the last direct-disciple seat may be reserved");
            Equal(false, HistoricalSchoolEliteEnrollmentRules.
                    CanReserveTeacher(committedDisciples: 7,
                        pendingAdmissions: 1, directDiscipleCap: 8),
                "pending writes reserve teacher capacity");

            Equal(6, HistoricalSchoolEliteEnrollmentRules
                    .MaxSuccessfulJoinsPerRealmPerYear,
                "each realm has a six-student continuity floor");
            Equal(16, HistoricalSchoolEliteEnrollmentRules
                    .MaxSuccessfulJoinsPerRealmHardCap,
                "dynamic realm admissions retain a sixteen-student hard cap");
            Equal(24, HistoricalSchoolEliteEnrollmentRules
                    .MaxCandidateAttemptsPerRealmPerYear,
                "each realm may attempt twenty-four bounded candidates");
            Equal(24, HistoricalSchoolEliteEnrollmentRules
                    .MaxNobleArchiveRowsPerRealmYear,
                "noble archive recovery inspects twenty-four rows");
            Equal(24, HistoricalSchoolEliteEnrollmentRules
                    .MaxAcademyResidentsPerYear,
                "each academy inspects twenty-four residents");
            Equal(2, HistoricalSchoolEliteEnrollmentRules
                    .MaxCommonerAdmissionsPerAcademyYear,
                "each academy admits at most two commoners per year");
            Equal(6, HistoricalSchoolEliteEnrollmentRules
                    .RealmSuccessfulJoinLimit(0, 0),
                "a realm retains the six-student continuity floor");
            Equal(14, HistoricalSchoolEliteEnrollmentRules
                    .RealmSuccessfulJoinLimit(8, 2),
                "unchanged teacher and academy bonuses raise a realm to fourteen");
            Equal(14, HistoricalSchoolEliteEnrollmentRules
                    .RealmSuccessfulJoinLimit(100, 100),
                "bounded bonuses cannot exceed fourteen with the current formula");
            Equal(true, HistoricalSchoolEliteEnrollmentRules
                    .IsAcademyCommonerEligible(
                        valid: true, adult: true, localResident: true,
                        noble: false, slave: false, madness: false,
                        hasMembership: false, writePending: false,
                        available: true),
                "a talented local commoner may enter academy education");
            Equal(false, HistoricalSchoolEliteEnrollmentRules
                    .IsAcademyCommonerEligible(
                        valid: true, adult: true, localResident: true,
                        noble: true, slave: false, madness: false,
                        hasMembership: false, writePending: false,
                        available: true),
                "nobles use the noble education source, not academy quota");
            Equal(true, HistoricalSchoolEliteEnrollmentRules
                    .AcademyCandidateScore(10f, 2f, 2f) >
                HistoricalSchoolEliteEnrollmentRules
                    .AcademyCandidateScore(6f, 6f, 6f),
                "intelligence is the primary academy candidate factor");

            Equal(50, HistoricalSchoolLectureRules.StablePopulationTarget,
                "ordinary schools stabilize around fifty living members");
            Equal(true, HistoricalSchoolLectureRules.PopulationPriority(20) >
                        HistoricalSchoolLectureRules.PopulationPriority(60),
                "a twenty-member school receives recovery priority over a " +
                "sixty-member school");
            IReadOnlyList<int> recoveryOrder = HistoricalSchoolLectureRules
                .BuildPopulationPriorityOrder(new[] { 60, 20, 50 },
                    pStartIndex: 0);
            Equal(1, recoveryOrder[0],
                "teacher selection visits the weakest school first");
            Equal(0, recoveryOrder[1],
                "schools at or above the stable target retain fair rotation");
            Equal(2, recoveryOrder[2],
                "equal recovery priority preserves the rotated order");

            var broadNobility = new List<HistoricalSchoolEliteCandidate>
            {
                Candidate(7, 701,
                    HistoricalSchoolElitePriority.UntitledNoble),
                Candidate(7, 702,
                    HistoricalSchoolElitePriority.AcademyCommoner)
            };
            IReadOnlyList<HistoricalSchoolEliteCandidate> broadSelected =
                HistoricalSchoolEliteEnrollmentRules.SelectCandidates(
                    broadNobility, pYear: 5, pPerRealmLimit: 2);
            Equal(701L, broadSelected[0].ActorId,
                "untitled nobles retain priority over academy commoners");

            var crowdedRealm = new List<HistoricalSchoolEliteCandidate>();
            for (int actor = 0; actor < 22; actor++)
                crowdedRealm.Add(Candidate(8, 800 + actor,
                    HistoricalSchoolElitePriority.UntitledNoble));
            crowdedRealm.Add(Candidate(8, 900,
                HistoricalSchoolElitePriority.AcademyCommoner));
            crowdedRealm.Add(Candidate(8, 901,
                HistoricalSchoolElitePriority.AcademyCommoner));
            IReadOnlyList<HistoricalSchoolEliteCandidate> crowdedSelected =
                HistoricalSchoolEliteEnrollmentRules.SelectCandidates(
                    crowdedRealm, pYear: 8, pPerRealmLimit: 24);
            Equal(24, crowdedSelected.Count,
                "the relaxed candidate budget can select twenty-four actors");
            int academySelected = 0;
            for (int index = 0; index < crowdedSelected.Count; index++)
                if (crowdedSelected[index].Priority ==
                    HistoricalSchoolElitePriority.AcademyCommoner)
                    academySelected++;
            Equal(2, academySelected,
                "academy seats are not starved by a large noble backlog");

            Equal(true, HistoricalSchoolRuntimeMembershipRules.ShouldIndex(
                    hasActiveMembership: true, actorExists: true,
                    actorAlive: true, actorWrecked: false),
                "a living actor with an active membership occupies a school slot");
            Equal(false, HistoricalSchoolRuntimeMembershipRules.ShouldIndex(
                    hasActiveMembership: true, actorExists: false,
                    actorAlive: false, actorWrecked: false),
                "a missing actor cannot occupy a living school slot");
            Equal(false, HistoricalSchoolRuntimeMembershipRules.ShouldIndex(
                    hasActiveMembership: true, actorExists: true,
                    actorAlive: false, actorWrecked: false),
                "a dead actor cannot occupy a living school slot");
            Equal(false, HistoricalSchoolRuntimeMembershipRules.ShouldIndex(
                    hasActiveMembership: true, actorExists: true,
                    actorAlive: true, actorWrecked: true),
                "a wrecked actor cannot occupy a living school slot");
            Equal(false, HistoricalSchoolRuntimeMembershipRules.ShouldIndex(
                    hasActiveMembership: false, actorExists: true,
                    actorAlive: true, actorWrecked: false),
                "an actor without active membership cannot occupy a school slot");

            Console.WriteLine("Historical school elite enrollment rules passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static HistoricalSchoolEliteCandidate Candidate(long pKingdomId,
        long pActorId, HistoricalSchoolElitePriority pPriority)
    {
        return new HistoricalSchoolEliteCandidate(pKingdomId, pActorId,
            pPriority);
    }

    private static void Equal<T>(T pExpected, T pActual, string pName)
    {
        if (!Equals(pExpected, pActual))
            throw new InvalidOperationException(pName + ": expected " +
                                                pExpected + ", got " +
                                                pActual);
    }
}
