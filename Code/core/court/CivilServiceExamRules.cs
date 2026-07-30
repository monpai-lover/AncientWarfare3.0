using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    public enum CivilServiceExamMode
    {
        Tribute = 0,
        Imperial = 1
    }

    public enum CivilServiceExamStage
    {
        Scheduled = 0,
        Local = 1,
        Prefectural = 2,
        Metropolitan = 3,
        Palace = 4,
        National = 5,
        Ranking = 6,
        Completed = 7,
        Cancelled = 8
    }

    public enum CivilServiceQualification
    {
        None = 0,
        Juren = 1,
        Gongshi = 2,
        Jinshi = 3
    }

    public sealed class CivilServiceRankingFacts
    {
        public CivilServiceRankingFacts(long pCandidateId, long pActorId,
            int pScore)
        {
            CandidateId = pCandidateId;
            ActorId = pActorId;
            Score = pScore;
        }

        public long CandidateId { get; }
        public long ActorId { get; }
        public int Score { get; }
    }

    public sealed class CivilServiceAiRankingFacts
    {
        public CivilServiceAiRankingFacts(long pCandidateId, long pActorId,
            int pRawScore, string pSchoolId)
        {
            CandidateId = pCandidateId;
            ActorId = pActorId;
            RawScore = Math.Max(0, Math.Min(100, pRawScore));
            SchoolId = pSchoolId ?? "";
        }

        public long CandidateId { get; }
        public long ActorId { get; }
        public int RawScore { get; }
        public string SchoolId { get; }
    }

    public sealed class CivilServiceExamCandidateFacts
    {
        public CivilServiceExamCandidateFacts(long pActorId,
            string pSocialOrigin, int pEducation, int pKnowledge,
            int pAgeFitness)
        {
            ActorId = pActorId;
            SocialOrigin = pSocialOrigin ?? "";
            Education = Math.Max(0, Math.Min(100, pEducation));
            Knowledge = Math.Max(0, Math.Min(100, pKnowledge));
            AgeFitness = Math.Max(0, Math.Min(100, pAgeFitness));
        }

        public long ActorId { get; }
        public string SocialOrigin { get; }
        public int Education { get; }
        public int Knowledge { get; }
        public int AgeFitness { get; }
    }

    public static class CivilServiceExamRules
    {
        public const int CandidateLimit = 96;
        public const int CandidateSourceLimit = CandidateLimit * 3;
        public const int AuthorityCandidateBudget = 8;
        public const int SuggestedCandidateTarget = 24;
        public const int CandidatePopulationDivisor = 40;
        public const int EmptyCandidateRetryDays = 30;
        public const int AnnualForeignInvitationLimit = 4;
        public const int ForeignInvitationSourceLimit = 64;
        public const int CityVacancyFillBudget = 32;
        public const int PassMark = 60;
        public const int MinimumWaitingReserve = 4;
        public const int MaximumWaitingReserve = 32;

        public const string NobleOrigin = "noble";
        public const string DeclinedNobleOrigin = "declined_noble";
        public const string CommonerOrigin = "commoner";

        private static readonly string[] GuaranteedOrigins =
        {
            NobleOrigin,
            DeclinedNobleOrigin,
            CommonerOrigin
        };

        public static CivilServiceExamMode ResolveMode(bool hasMandate,
            bool hasEmpireTitle)
        {
            return hasMandate || hasEmpireTitle
                ? CivilServiceExamMode.Imperial
                : CivilServiceExamMode.Tribute;
        }

        public static int FirstOpeningYear(int completionYear)
        {
            return completionYear == int.MaxValue
                ? int.MaxValue
                : completionYear + 1;
        }

        public static bool IsCycleYear(int year, int anchorYear)
        {
            return anchorYear >= 0 && year >= anchorYear &&
                   (year - anchorYear) % 3 == 0;
        }

        public static bool ShouldOpenCandidateRoll(int candidateCount)
        {
            return candidateCount > 0;
        }

        public static bool ShouldUseVacancyPromotion(bool officeVacant,
            bool strictEligible, bool hasFormalQualification)
        {
            return officeVacant && !strictEligible && hasFormalQualification;
        }

        public static int ReserveTarget(int establishedPosts)
        {
            int posts = Math.Max(0, establishedPosts);
            int halfRoundedUp = posts / 2 + posts % 2;
            return Math.Max(MinimumWaitingReserve,
                Math.Min(MaximumWaitingReserve, halfRoundedUp));
        }

        public static int FinalAdmissionQuota(int centralVacancies,
            int cityVacancies, int waitingCandidateCount, int reserveTarget,
            int candidateCapacity)
        {
            long vacancies = (long)Math.Max(0, centralVacancies) +
                             Math.Max(0, cityVacancies);
            long demand = vacancies + Math.Max(0, reserveTarget) -
                          Math.Max(0, waitingCandidateCount);
            long renewedDemand = Math.Max(1L, demand);
            return (int)Math.Min(Math.Max(0, candidateCapacity),
                renewedDemand);
        }

        public static int FinalAdmissionQuota(int centralVacancies,
            int cityVacancies, int candidateCapacity)
        {
            long vacancies = (long)Math.Max(0, centralVacancies) +
                             Math.Max(0, cityVacancies);
            long legacyReserve = Math.Max(1L, (vacancies + 3L) / 4L);
            return (int)Math.Min(Math.Max(0, candidateCapacity),
                Math.Min(int.MaxValue, vacancies + legacyReserve));
        }

        public static int CandidateTarget(int livingPopulation,
            int finalAdmissionQuota)
        {
            int population = Math.Max(0, livingPopulation);
            int populationTarget = population == 0
                ? 0
                : (population - 1) / CandidatePopulationDivisor + 1;
            long vacancyTarget = (long)Math.Max(0, finalAdmissionQuota) * 4L;
            long target = Math.Max(SuggestedCandidateTarget,
                Math.Max(populationTarget, vacancyTarget));
            return (int)Math.Min(CandidateLimit, target);
        }

        public static bool IsWaitingCandidate(bool alive, bool adult,
            bool male, bool slave, bool availableForOffice,
            bool activeOffice, bool domesticOrHostQualifiedResident)
        {
            return alive && adult && male && !slave && availableForOffice &&
                   !activeOffice && domesticOrHostQualifiedResident;
        }

        public static bool ShouldShowReserveSummary(
            int waitingCandidateCount, int reserveTarget)
        {
            return waitingCandidateCount >= 0 && reserveTarget >= 0;
        }

        public static int AdmissionQuotaForStage(
            CivilServiceExamStage stage, int finalAdmissionQuota,
            int stageCap)
        {
            int finalQuota = Math.Max(0, finalAdmissionQuota);
            int multiplier = stage switch
            {
                CivilServiceExamStage.Local or
                CivilServiceExamStage.Prefectural => 4,
                CivilServiceExamStage.Metropolitan => 2,
                CivilServiceExamStage.Palace or
                CivilServiceExamStage.National => 1,
                _ => 0
            };
            long requested = (long)finalQuota * multiplier;
            return (int)Math.Min(Math.Max(0, stageCap),
                Math.Min(int.MaxValue, requested));
        }

        public static bool IsStageParticipant(CivilServiceExamStage stage,
            int localScore, int metropolitanScore, int palaceScore,
            int nationalScore)
        {
            return stage switch
            {
                CivilServiceExamStage.Local or
                CivilServiceExamStage.Prefectural => localScore >= 0,
                CivilServiceExamStage.Metropolitan =>
                    metropolitanScore >= 0,
                CivilServiceExamStage.Palace => palaceScore >= 0,
                CivilServiceExamStage.National => nationalScore >= 0,
                _ => true
            };
        }

        public static string ResolveStageResult(CivilServiceExamStage stage,
            string localResult, string metropolitanResult,
            string palaceResult, string nationalResult,
            string currentResult)
        {
            string result = stage switch
            {
                CivilServiceExamStage.Local or
                CivilServiceExamStage.Prefectural => localResult,
                CivilServiceExamStage.Metropolitan => metropolitanResult,
                CivilServiceExamStage.Palace => palaceResult,
                CivilServiceExamStage.National => nationalResult,
                _ => currentResult
            };
            return string.IsNullOrEmpty(result) ? "pending" : result;
        }

        public static string RepairLegacyStageResult(
            CivilServiceExamStage stage, string storedResult,
            int localScore, int metropolitanScore, int palaceScore,
            int nationalScore, string qualification, string currentResult)
        {
            if (!string.IsNullOrEmpty(storedResult) &&
                !string.Equals(storedResult, "pending",
                    StringComparison.OrdinalIgnoreCase))
                return storedResult;
            string latest = string.IsNullOrEmpty(currentResult)
                ? "pending"
                : currentResult;
            bool juren = string.Equals(qualification, "juren",
                StringComparison.OrdinalIgnoreCase);
            bool gongshi = string.Equals(qualification, "gongshi",
                StringComparison.OrdinalIgnoreCase);
            bool jinshi = string.Equals(qualification, "jinshi",
                StringComparison.OrdinalIgnoreCase);
            return stage switch
            {
                CivilServiceExamStage.Local or
                CivilServiceExamStage.Prefectural => localScore < 0
                    ? "pending"
                    : metropolitanScore >= 0 || nationalScore >= 0 ||
                      palaceScore >= 0 || juren || gongshi || jinshi
                        ? "passed"
                        : latest,
                CivilServiceExamStage.Metropolitan => metropolitanScore < 0
                    ? "pending"
                    : palaceScore >= 0 || gongshi || jinshi
                        ? "passed"
                        : latest,
                CivilServiceExamStage.Palace => palaceScore < 0
                    ? "pending"
                    : jinshi ? "passed" : latest,
                CivilServiceExamStage.National => nationalScore < 0
                    ? "pending"
                    : gongshi || jinshi ? "passed" : latest,
                _ => latest
            };
        }

        public static int StagePercent(CivilServiceExamMode mode,
            CivilServiceExamStage stage)
        {
            if (stage == CivilServiceExamStage.Scheduled) return 5;
            if (stage == CivilServiceExamStage.Ranking ||
                stage == CivilServiceExamStage.Completed) return 95;
            if (mode == CivilServiceExamMode.Imperial)
            {
                if (stage == CivilServiceExamStage.Local) return 30;
                if (stage == CivilServiceExamStage.Metropolitan) return 55;
                if (stage == CivilServiceExamStage.Palace) return 80;
            }
            else
            {
                if (stage == CivilServiceExamStage.Prefectural) return 40;
                if (stage == CivilServiceExamStage.National) return 80;
            }
            return -1;
        }

        public static int KingdomOffsetDays(long kingdomId)
        {
            long value = kingdomId % 31L;
            if (value < 0L) value += 31L;
            return (int)value;
        }

        public static int LocalQuota(int cityCount)
        {
            return Math.Min(64, Math.Max(12, Math.Max(0, cityCount) * 4));
        }

        public static int MetropolitanQuota(int cityCount)
        {
            return Math.Min(32, Math.Max(6, Math.Max(0, cityCount) * 2));
        }

        public static int PrefecturalQuota(int cityCount)
        {
            return Math.Min(48, Math.Max(8, Math.Max(0, cityCount) * 3));
        }

        public static int NationalQuota(int cityCount)
        {
            return Math.Min(20, Math.Max(4, Math.Max(0, cityCount)));
        }

        public static int StageCapacity(CivilServiceExamMode mode,
            CivilServiceExamStage stage, int cityCount)
        {
            if (mode == CivilServiceExamMode.Imperial)
            {
                return stage switch
                {
                    CivilServiceExamStage.Local => LocalQuota(cityCount),
                    CivilServiceExamStage.Metropolitan or
                    CivilServiceExamStage.Palace =>
                        MetropolitanQuota(cityCount),
                    _ => 0
                };
            }

            return stage switch
            {
                CivilServiceExamStage.Prefectural =>
                    PrefecturalQuota(cityCount),
                CivilServiceExamStage.National => NationalQuota(cityCount),
                _ => 0
            };
        }

        public static bool Passes(int score)
        {
            return score >= PassMark;
        }

        public static CivilServiceQualification QualificationAfterPass(
            CivilServiceExamMode mode, CivilServiceExamStage stage)
        {
            if (mode == CivilServiceExamMode.Tribute)
                return stage == CivilServiceExamStage.National
                    ? CivilServiceQualification.Gongshi
                    : CivilServiceQualification.None;
            if (stage == CivilServiceExamStage.Local)
                return CivilServiceQualification.Juren;
            if (stage == CivilServiceExamStage.Metropolitan)
                return CivilServiceQualification.Gongshi;
            return stage == CivilServiceExamStage.Palace
                ? CivilServiceQualification.Jinshi
                : CivilServiceQualification.None;
        }

        public static bool IsFormalAppointmentQualification(
            CivilServiceQualification qualification)
        {
            return qualification == CivilServiceQualification.Gongshi ||
                   qualification == CivilServiceQualification.Jinshi;
        }

        public static bool IsFormalAppointmentQualification(string value)
        {
            return string.Equals(value, "gongshi",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "jinshi",
                       StringComparison.OrdinalIgnoreCase);
        }

        public static bool CanEnterMetropolitan(
            CivilServiceQualification qualification)
        {
            return qualification == CivilServiceQualification.Juren ||
                   qualification == CivilServiceQualification.Gongshi;
        }

        public static bool RequiresExamQualification(
            bool examinationEnabled)
        {
            return examinationEnabled;
        }

        public static bool HasEquivalentHostQualification(
            long hostKingdomId, long issuingKingdomId,
            CivilServiceQualification qualification,
            CivilServiceExamMode mode)
        {
            if (hostKingdomId < 0L || issuingKingdomId != hostKingdomId)
                return false;
            if (qualification == CivilServiceQualification.Jinshi)
                return true;
            return mode == CivilServiceExamMode.Tribute &&
                   qualification == CivilServiceQualification.Gongshi;
        }

        public static bool IsEligibleForeignExamCandidate(bool adult,
            bool alive, bool slave, bool king, bool heir, bool prince,
            bool civilOffice, bool militaryOffice, bool servingElsewhere,
            bool resident, bool residenceInHost, bool foreignHome,
            bool educated, bool equivalentQualification)
        {
            return adult && alive && !slave && !king && !heir && !prince &&
                   !civilOffice && !militaryOffice && !servingElsewhere &&
                   resident && residenceInHost && foreignHome && educated &&
                   !equivalentQualification;
        }

        public static long ResolveCandidateHomeCityId(bool foreignResident,
            long hometownCityId, long residenceCityId)
        {
            if (foreignResident && hometownCityId >= 0L)
                return hometownCityId;
            return residenceCityId;
        }

        public static bool IsPresentAtHostExamination(bool alive, bool rekt,
            bool currentKingdomIsHost, bool activeForeignResident,
            bool residenceInHost, bool foreignHome)
        {
            if (!alive || rekt) return false;
            if (currentKingdomIsHost) return true;
            return activeForeignResident && residenceInHost && foreignHome;
        }

        public static bool CanInviteForeignScholar(bool eligiblePerson,
            bool movableAffiliation, bool foreignHome, bool sourceAtWar,
            bool alreadyAtDestination)
        {
            return eligiblePerson && movableAffiliation && foreignHome &&
                   !sourceAtWar && !alreadyAtDestination;
        }

        public static bool CanEnterGuestCandidateIndex(
            bool centralOfficeSexEligible, bool hasExaminationSystem,
            bool educatedScholar, bool qualifiedTeacher,
            bool hostIssuedQualification)
        {
            return centralOfficeSexEligible && educatedScholar &&
                   (!hasExaminationSystem || qualifiedTeacher ||
                    hostIssuedQualification);
        }

        public static int ForeignInvitationCount(bool examinationEnabled,
            int targetCandidates, int eligibleCount, int annualInvitedCount,
            int availableForeignCount)
        {
            if (!examinationEnabled) return 0;
            int deficit = Math.Max(0,
                Math.Min(CandidateLimit, Math.Max(SuggestedCandidateTarget,
                    targetCandidates)) - Math.Max(0, eligibleCount));
            int annualRemaining = Math.Max(0,
                AnnualForeignInvitationLimit -
                Math.Max(0, annualInvitedCount));
            return Math.Min(Math.Max(0, availableForeignCount),
                Math.Min(deficit, annualRemaining));
        }

        public static int Score(int knowledge, int stageAbility,
            int education, int jitter)
        {
            int learned = ClampPercent(knowledge);
            int applied = ClampPercent(stageAbility);
            int schooling = ClampPercent(education);
            int boundedJitter = Math.Max(-5, Math.Min(5, jitter));
            int score = (learned * 5 + applied * 3 + schooling * 2) / 10 +
                        boundedJitter;
            return ClampPercent(score);
        }

        public static int NormalizeActorAbility(int rawAbility)
        {
            if (rawAbility <= 0) return 0;
            if (rawAbility >= 10) return 100;
            return rawAbility * 10;
        }

        public static float CandidateRowWidth(float windowWidth,
            float horizontalWindowMargin, float scrollbarAndGap)
        {
            return Math.Max(260f, windowWidth -
                Math.Max(0f, horizontalWindowMargin) -
                Math.Max(0f, scrollbarAndGap));
        }

        public static bool ShouldUseActingCentralFallback(
            bool allowActing, bool hasExaminationSystem,
            bool formalCandidateFound,
            bool educatedCandidateFound)
        {
            return allowActing && hasExaminationSystem &&
                   !formalCandidateFound &&
                   educatedCandidateFound;
        }

        public static bool ShouldAttemptCityVacancyFill(bool hasLeader,
            bool gettingCaptured, bool belongsToHost, int attemptsRemaining)
        {
            return !hasLeader && !gettingCaptured && belongsToHost &&
                   attemptsRemaining > 0;
        }

        public static bool ShouldUseCivilServiceGovernorPipeline(
            bool hasNineRankSystem)
        {
            return hasNineRankSystem;
        }

        public static bool ShouldUseIntercityGovernorCirculation(
            bool hasNineRankSystem, int liveCityCount)
        {
            return hasNineRankSystem && liveCityCount > 1;
        }

        public static bool CanEnterActingGovernorCandidatePool(
            bool directCandidateEligible, bool hasExistingCourtOffice)
        {
            return directCandidateEligible && !hasExistingCourtOffice;
        }

        public static bool ShouldExpireActingCentralOfficial(string layer,
            int actingSinceYear, int currentYear)
        {
            return string.Equals(layer, "central",
                       StringComparison.OrdinalIgnoreCase) &&
                   actingSinceYear >= 0 && currentYear > actingSinceYear;
        }

        public static int AgeFitness(int age)
        {
            int safeAge = Math.Max(0, Math.Min(150, age));
            return Math.Max(0, 100 - Math.Abs(safeAge - 30) * 2);
        }

        public static string ResolveSocialOrigin(string currentStatus,
            bool everNoble, long lineageId)
        {
            if (string.Equals(currentStatus, NobleOrigin,
                    StringComparison.OrdinalIgnoreCase))
                return NobleOrigin;
            return everNoble || lineageId >= 0L
                ? DeclinedNobleOrigin
                : CommonerOrigin;
        }

        public static IReadOnlyList<CivilServiceExamCandidateFacts>
            SelectCandidates(
                IReadOnlyList<CivilServiceExamCandidateFacts> pCandidates,
                int pLimit = CandidateLimit)
        {
            int limit = Math.Max(0, Math.Min(CandidateLimit, pLimit));
            if (pCandidates == null || pCandidates.Count == 0 || limit == 0)
                return Array.Empty<CivilServiceExamCandidateFacts>();

            CivilServiceExamCandidateFacts[] ordered = pCandidates
                .Where(p => p != null && p.ActorId >= 0L)
                .OrderByDescending(p => p.Education)
                .ThenByDescending(p => p.Knowledge)
                .ThenByDescending(p => p.AgeFitness)
                .ThenBy(p => p.ActorId)
                .ToArray();
            var unique = new List<CivilServiceExamCandidateFacts>(
                ordered.Length);
            var uniqueActorIds = new HashSet<long>();
            foreach (CivilServiceExamCandidateFacts candidate in ordered)
                if (uniqueActorIds.Add(candidate.ActorId))
                    unique.Add(candidate);
            CivilServiceExamCandidateFacts[] ranked = unique.ToArray();
            var selectedActorIds = new HashSet<long>();
            foreach (string origin in GuaranteedOrigins)
            {
                if (selectedActorIds.Count >= limit) break;
                CivilServiceExamCandidateFacts guaranteed = ranked.
                    FirstOrDefault(p => p.SocialOrigin == origin &&
                                        !selectedActorIds.Contains(p.ActorId));
                if (guaranteed != null)
                    selectedActorIds.Add(guaranteed.ActorId);
            }

            foreach (CivilServiceExamCandidateFacts candidate in ranked)
            {
                if (selectedActorIds.Count >= limit) break;
                selectedActorIds.Add(candidate.ActorId);
            }

            return ranked
                .Where(p => selectedActorIds.Contains(p.ActorId))
                .Take(limit)
                .ToArray();
        }

        public static IReadOnlyList<CivilServiceExamCandidateFacts>
            SelectCandidatesWithLocalPriority(
                IReadOnlyList<CivilServiceExamCandidateFacts> pLocalCandidates,
                IReadOnlyList<CivilServiceExamCandidateFacts> pForeignCandidates,
                int pLimit = CandidateLimit)
        {
            int limit = Math.Max(0, Math.Min(CandidateLimit, pLimit));
            if (limit == 0)
                return Array.Empty<CivilServiceExamCandidateFacts>();
            IReadOnlyList<CivilServiceExamCandidateFacts> foreign =
                SelectCandidates(pForeignCandidates, limit);
            bool reserveForeign = limit > 1 && foreign.Count > 0;
            IReadOnlyList<CivilServiceExamCandidateFacts> local =
                SelectCandidates(pLocalCandidates,
                    reserveForeign ? limit - 1 : limit);
            var selected = new List<CivilServiceExamCandidateFacts>(limit);
            selected.AddRange(local);
            var selectedActorIds = new HashSet<long>(
                selected.Select(p => p.ActorId));
            foreach (CivilServiceExamCandidateFacts candidate in foreign)
            {
                if (!selectedActorIds.Add(candidate.ActorId)) continue;
                selected.Add(candidate);
                if (selected.Count >= limit) break;
            }
            if (selected.Count >= limit) return selected;

            IReadOnlyList<CivilServiceExamCandidateFacts> remainingLocal =
                SelectCandidates(pLocalCandidates, limit);
            foreach (CivilServiceExamCandidateFacts candidate in remainingLocal)
            {
                if (!selectedActorIds.Add(candidate.ActorId)) continue;
                selected.Add(candidate);
                if (selected.Count >= limit) break;
            }
            return selected;
        }

        public static IReadOnlyList<long> InterleaveCandidateSources(
            IReadOnlyList<IReadOnlyList<long>> sources, int scanLimit)
        {
            int limit = Math.Max(0, scanLimit);
            if (sources == null || sources.Count == 0 || limit == 0)
                return Array.Empty<long>();

            var cursors = new int[sources.Count];
            var result = new List<long>(limit);
            var seen = new HashSet<long>();
            int inspected = 0;
            bool advanced;
            do
            {
                advanced = false;
                for (int sourceIndex = 0;
                     sourceIndex < sources.Count && inspected < limit;
                     sourceIndex++)
                {
                    IReadOnlyList<long> source = sources[sourceIndex];
                    if (source == null || cursors[sourceIndex] >= source.Count)
                        continue;
                    long actorId = source[cursors[sourceIndex]++];
                    inspected++;
                    advanced = true;
                    if (actorId >= 0L && seen.Add(actorId))
                        result.Add(actorId);
                }
            } while (advanced && inspected < limit);
            return result;
        }

        public static int DeterministicJitter(long sessionId, long actorId,
            CivilServiceExamStage stage)
        {
            unchecked
            {
                ulong value = (ulong)sessionId * 11400714819323198485UL;
                value ^= (ulong)actorId + 0x9E3779B97F4A7C15UL +
                         (value << 6) + (value >> 2);
                value ^= (ulong)((int)stage + 1) * 0xBF58476D1CE4E5B9UL;
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                return (int)(value % 11UL) - 5;
            }
        }

        public static bool TryBuildPlayerRanking(
            IReadOnlyList<CivilServiceRankingFacts> pFinalists,
            IReadOnlyList<long> pPreferredTopCandidateIds,
            out long[] pOrderedCandidateIds)
        {
            pOrderedCandidateIds = Array.Empty<long>();
            if (pFinalists == null || pFinalists.Count == 0 ||
                pPreferredTopCandidateIds == null) return false;

            var byCandidate = new Dictionary<long, CivilServiceRankingFacts>();
            foreach (CivilServiceRankingFacts finalist in pFinalists)
            {
                if (finalist == null || finalist.CandidateId < 0L ||
                    finalist.ActorId < 0L ||
                    byCandidate.ContainsKey(finalist.CandidateId))
                    return false;
                byCandidate.Add(finalist.CandidateId, finalist);
            }

            int requiredTopCount = Math.Min(3, byCandidate.Count);
            if (pPreferredTopCandidateIds.Count != requiredTopCount)
                return false;
            var chosen = new HashSet<long>();
            var ordered = new List<long>(byCandidate.Count);
            foreach (long candidateId in pPreferredTopCandidateIds)
            {
                if (!byCandidate.ContainsKey(candidateId) ||
                    !chosen.Add(candidateId)) return false;
                ordered.Add(candidateId);
            }

            ordered.AddRange(byCandidate.Values
                .Where(facts => !chosen.Contains(facts.CandidateId))
                .OrderByDescending(facts => facts.Score)
                .ThenBy(facts => facts.ActorId)
                .ThenBy(facts => facts.CandidateId)
                .Select(facts => facts.CandidateId));
            pOrderedCandidateIds = ordered.ToArray();
            return true;
        }

        public static int AiRankingAdjustment(int rawScore,
            string candidateSchool, string dominantSchool,
            int rulerAbility)
        {
            int ability = ClampPercent(rulerAbility);
            bool hasDominant = !string.IsNullOrEmpty(dominantSchool);
            bool aligned = hasDominant && string.Equals(candidateSchool,
                dominantSchool, StringComparison.Ordinal);
            int adjustment = 0;
            if (aligned)
                adjustment += ability >= 70 ? 1 : ability >= 40 ? 2 : 3;
            else if (hasDominant)
                adjustment--;

            if (ability >= 70)
            {
                if (rawScore >= 80) adjustment++;
                else if (rawScore < 65) adjustment--;
            }
            return Math.Max(-3, Math.Min(3, adjustment));
        }

        public static IReadOnlyList<long> BuildAiRanking(
            IReadOnlyList<CivilServiceAiRankingFacts> pFinalists,
            string pDominantSchool, int pRulerAbility)
        {
            if (pFinalists == null || pFinalists.Count == 0)
                return Array.Empty<long>();
            return pFinalists
                .Where(p => p != null && p.CandidateId >= 0L &&
                            p.ActorId >= 0L)
                .GroupBy(p => p.CandidateId)
                .Select(p => p.First())
                .OrderByDescending(p => p.RawScore + AiRankingAdjustment(
                    p.RawScore, p.SchoolId, pDominantSchool,
                    pRulerAbility))
                .ThenByDescending(p => p.RawScore)
                .ThenBy(p => p.ActorId)
                .Select(p => p.CandidateId)
                .ToArray();
        }

        private static int ClampPercent(int value)
        {
            return Math.Max(0, Math.Min(100, value));
        }
    }
}
