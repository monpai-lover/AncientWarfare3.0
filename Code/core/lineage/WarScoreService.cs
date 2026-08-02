using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    public sealed class WarScoreOccupiedCitySnapshot
    {
        public WarScoreOccupiedCitySnapshot(long pWarId, long pCityId,
            long pHomeKingdomId, long pControllerKingdomId,
            WarScoreSide pHomeSide, WarScoreSide pControllerSide,
            int pContribution, bool pMatchesActiveWarGoal)
        {
            WarId = pWarId;
            CityId = pCityId;
            HomeKingdomId = pHomeKingdomId;
            ControllerKingdomId = pControllerKingdomId;
            HomeSide = pHomeSide;
            ControllerSide = pControllerSide;
            Contribution = pContribution;
            MatchesActiveWarGoal = pMatchesActiveWarGoal;
            ControlKey = pWarId + ":city:" + pCityId;
        }

        public long WarId { get; }
        public string ControlKey { get; }
        public long CityId { get; }
        public long HomeKingdomId { get; }
        public long ControllerKingdomId { get; }
        public WarScoreSide HomeSide { get; }
        public WarScoreSide ControllerSide { get; }
        public int Contribution { get; }
        public bool MatchesActiveWarGoal { get; }
    }

    public sealed class WarScoreSnapshot
    {
        public long WarId { get; internal set; } = -1;
        public long AttackerKingdomId { get; internal set; } = -1;
        public long DefenderKingdomId { get; internal set; } = -1;
        public WarScoreSide Perspective { get; internal set; }
        public int Score { get; internal set; }
        public int CityScore { get; internal set; }
        public int BattleScore { get; internal set; }
        public int GoalScore { get; internal set; }
        public int LossScore { get; internal set; }
        internal int DecisiveScore { get; set; }
        public int AttackerLosses { get; internal set; }
        public int DefenderLosses { get; internal set; }
        public int AttackerMobilizationBaseline { get; internal set; }
        public int DefenderMobilizationBaseline { get; internal set; }
        public int DurationYears { get; internal set; }
        public int LastCalibratedYear { get; internal set; } = int.MinValue;
        public int AttackerExhaustionRelief { get; internal set; }
        public int DefenderExhaustionRelief { get; internal set; }
        public int AttackerReserveExhaustion { get; internal set; }
        public int DefenderReserveExhaustion { get; internal set; }
        public int AttackerExhaustion { get; internal set; }
        public int DefenderExhaustion { get; internal set; }
        public bool Active { get; internal set; }
        public string Winner { get; internal set; } = "";
        public double StartedTime { get; internal set; } = -1d;
        public double UpdatedTime { get; internal set; } = -1d;
        public double EndedTime { get; internal set; } = -1d;
        public long Revision { get; internal set; }

        internal WarScoreSnapshot CloneCanonical()
        {
            return (WarScoreSnapshot)MemberwiseClone();
        }

        internal WarScoreSnapshot ForPerspective(WarScoreSide pSide)
        {
            WarScoreSnapshot result = CloneCanonical();
            result.Perspective = pSide;
            result.Score = WarScoreRules.ForSide(Score, pSide);
            result.CityScore = WarScoreRules.ForSide(CityScore, pSide);
            result.BattleScore = WarScoreRules.ForSide(BattleScore, pSide);
            result.GoalScore = WarScoreRules.ForSide(GoalScore, pSide);
            result.LossScore = WarScoreRules.ForSide(LossScore, pSide);
            result.DecisiveScore = WarScoreRules.ForSide(DecisiveScore,
                pSide);
            return result;
        }
    }

    public sealed partial class WarScoreService
    {
        private readonly object _gate = new object();
        private readonly Dictionary<long, WarScoreSnapshot> _active =
            new Dictionary<long, WarScoreSnapshot>();
        private readonly Dictionary<long, WarScoreSnapshot> _history =
            new Dictionary<long, WarScoreSnapshot>();
        private readonly Dictionary<long, Dictionary<string, WarScoreControlState>>
            _controls = new Dictionary<long, Dictionary<string, WarScoreControlState>>();
        private readonly Dictionary<long, HashSet<long>> _cityControlWars =
            new Dictionary<long, HashSet<long>>();
        private readonly Dictionary<long, int> _rawCityScores =
            new Dictionary<long, int>();
        private readonly Dictionary<long, int> _rawGoalScores =
            new Dictionary<long, int>();
        private readonly Dictionary<long, int> _cityScoreBudgets =
            new Dictionary<long, int>();
        private readonly WarScorePersistence _persistence;

        public WarScoreService(SQLiteConnection pDatabase)
        {
            _persistence = new WarScorePersistence(pDatabase);
            IReadOnlyList<WarScoreSnapshot> active = _persistence.LoadActive();
            for (int i = 0; i < active.Count; i++)
            {
                WarScoreSnapshot snapshot = active[i];
                _active[snapshot.WarId] = snapshot;
                var controls = new Dictionary<string, WarScoreControlState>(
                    StringComparer.Ordinal);
                int rawCityScore = 0;
                int rawGoalScore = 0;
                IReadOnlyList<WarScoreControlState> restored =
                    _persistence.LoadControls(snapshot.WarId);
                for (int j = 0; j < restored.Count; j++)
                {
                    controls[restored[j].Key] = restored[j];
                    if (restored[j].Kind == "city" &&
                        long.TryParse(restored[j].SubjectId, out long cityId))
                        AddCityControlIndex(cityId, snapshot.WarId);
                    if (restored[j].Kind == "city")
                        rawCityScore = AddSaturating(rawCityScore,
                            restored[j].Contribution);
                    else if (restored[j].Kind == "goal")
                        rawGoalScore = AddSaturating(rawGoalScore,
                            restored[j].Contribution);
                }
                _controls[snapshot.WarId] = controls;
                _rawCityScores[snapshot.WarId] = rawCityScore;
                _rawGoalScores[snapshot.WarId] = rawGoalScore;
                _cityScoreBudgets[snapshot.WarId] =
                    WarScoreRules.DefaultCityScoreBudget;
            }
        }

        public bool StartWar(long pWarId, long pAttackerKingdomId,
            long pDefenderKingdomId, double pWorldTime)
        {
            return StartWar(pWarId, pAttackerKingdomId, pDefenderKingdomId,
                pWorldTime, WarScoreRules.DefaultCityScoreBudget, 1, 1);
        }

        public bool StartWar(long pWarId, long pAttackerKingdomId,
            long pDefenderKingdomId, double pWorldTime,
            int pCityScoreBudget)
        {
            return StartWar(pWarId, pAttackerKingdomId, pDefenderKingdomId,
                pWorldTime, pCityScoreBudget, 1, 1);
        }

        public bool StartWar(long pWarId, long pAttackerKingdomId,
            long pDefenderKingdomId, double pWorldTime,
            int pCityScoreBudget, int pAttackerMobilizationBaseline,
            int pDefenderMobilizationBaseline)
        {
            if (pWarId < 0 || pAttackerKingdomId < 0 ||
                pAttackerKingdomId == pDefenderKingdomId) return false;
            lock (_gate)
            {
                int cityScoreBudget = WarScoreRules.NormalizeCityScoreBudget(
                    pCityScoreBudget);
                if (_active.ContainsKey(pWarId))
                {
                    _cityScoreBudgets[pWarId] = cityScoreBudget;
                    WarScoreSnapshot current = _active[pWarId];
                    int attackerBaseline = Math.Max(
                        current.AttackerMobilizationBaseline,
                        WarParticipantMobilizationBaselineRules.
                            NormalizePotential(pAttackerMobilizationBaseline));
                    int defenderBaseline = Math.Max(
                        current.DefenderMobilizationBaseline,
                        WarParticipantMobilizationBaselineRules.
                            NormalizePotential(pDefenderMobilizationBaseline));
                    if (attackerBaseline !=
                            current.AttackerMobilizationBaseline ||
                        defenderBaseline !=
                            current.DefenderMobilizationBaseline)
                    {
                        WarScoreSnapshot repaired = current.CloneCanonical();
                        repaired.AttackerMobilizationBaseline =
                            attackerBaseline;
                        repaired.DefenderMobilizationBaseline =
                            defenderBaseline;
                        RecalculateLossesAndExhaustion(repaired);
                        RecalculateTotal(repaired);
                        Touch(repaired, pWorldTime);
                        _persistence.Save(repaired);
                        _active[pWarId] = repaired;
                    }
                    return false;
                }
                if (_history.ContainsKey(pWarId) ||
                    _persistence.Read(pWarId) != null) return false;
                var snapshot = new WarScoreSnapshot
                {
                    WarId = pWarId,
                    AttackerKingdomId = pAttackerKingdomId,
                    DefenderKingdomId = pDefenderKingdomId,
                    AttackerMobilizationBaseline =
                        WarParticipantMobilizationBaselineRules.
                            NormalizePotential(
                                pAttackerMobilizationBaseline),
                    DefenderMobilizationBaseline =
                        WarParticipantMobilizationBaselineRules.
                            NormalizePotential(
                                pDefenderMobilizationBaseline),
                    Perspective = WarScoreSide.Attackers,
                    Active = true,
                    StartedTime = pWorldTime,
                    UpdatedTime = pWorldTime,
                    EndedTime = -1d,
                    Revision = 1
                };
                _persistence.Save(snapshot);
                _active[pWarId] = snapshot;
                _controls[pWarId] = new Dictionary<string, WarScoreControlState>(
                    StringComparer.Ordinal);
                _rawCityScores[pWarId] = 0;
                _rawGoalScores[pWarId] = 0;
                _cityScoreBudgets[pWarId] = cityScoreBudget;
                return true;
            }
        }

        public bool RecordCityControlChanged(long pWarId,
            WarScoreCityFacts pFacts, WarScoreSide pHomeSide,
            WarScoreSide pControllerSide, double pWorldTime)
        {
            return RecordCityControlChanged(pWarId, pFacts, pHomeSide,
                pControllerSide, -1, -1, pWorldTime);
        }

        public bool RecordCityControlChanged(long pWarId,
            WarScoreCityFacts pFacts, WarScoreSide pHomeSide,
            WarScoreSide pControllerSide, long pHomeKingdomId,
            long pControllerKingdomId, double pWorldTime)
        {
            if (pFacts.CityId < 0 ||
                !WarScoreRules.IsParticipantSide(pHomeSide) ||
                (pControllerSide != WarScoreSide.None &&
                 !WarScoreRules.IsParticipantSide(pControllerSide))) return false;
            lock (_gate)
            {
                if (!_active.TryGetValue(pWarId, out WarScoreSnapshot current))
                    return false;
                string key = BuildControlKey(pWarId, "city",
                    pFacts.CityId.ToString());
                int cityScoreBudget = CityScoreBudgetForWar(pWarId);
                int value = WarScoreRules.CityControlValue(pFacts,
                    cityScoreBudget);
                int contribution = WarScoreRules.CityControlContribution(
                    pFacts, pHomeSide, pControllerSide, cityScoreBudget);
                Dictionary<string, WarScoreControlState> controls = _controls[pWarId];
                bool hasOld = controls.TryGetValue(key,
                    out WarScoreControlState old);
                bool controllerChanged = hasOld &&
                    (old.ControllerSide != pControllerSide ||
                     old.ControllerKingdomId != pControllerKingdomId);
                if (hasOld &&
                    old.HomeSide == pHomeSide &&
                    old.ControllerSide == pControllerSide &&
                    old.HomeKingdomId == pHomeKingdomId &&
                    old.ControllerKingdomId == pControllerKingdomId &&
                    old.Value == value && old.Contribution == contribution &&
                    old.VerifiedGoal == pFacts.MatchesActiveWarGoal &&
                    old.HomeCityCount == pFacts.InitialOwnerCityCount &&
                    old.Decisive == (pFacts.IsOnlyLiveCity &&
                        pControllerSide != WarScoreSide.None &&
                        pHomeSide != pControllerSide)) return false;

                var control = new WarScoreControlState
                {
                    Key = key,
                    WarId = pWarId,
                    Kind = "city",
                    SubjectId = pFacts.CityId.ToString(),
                    HomeKingdomId = pHomeKingdomId,
                    ControllerKingdomId = pControllerKingdomId,
                    HomeSide = pHomeSide,
                    ControllerSide = pControllerSide,
                    Value = value,
                    Contribution = contribution,
                    VerifiedGoal = pFacts.MatchesActiveWarGoal,
                    Occurrence = ResolveControlOccurrence(hasOld, old,
                        pHomeSide, pControllerSide, controllerChanged),
                    HomeCityCount = pFacts.InitialOwnerCityCount,
                    Decisive = pFacts.IsOnlyLiveCity &&
                               pControllerSide != WarScoreSide.None &&
                               pHomeSide != pControllerSide,
                    StartedTime = hasOld &&
                                  old.HomeSide == pHomeSide &&
                                  old.ControllerSide == pControllerSide &&
                                  old.HomeKingdomId == pHomeKingdomId &&
                                  old.ControllerKingdomId ==
                                  pControllerKingdomId &&
                                  old.StartedTime >= 0d
                        ? old.StartedTime
                        : pWorldTime,
                    UpdatedTime = pWorldTime
                };
                WarScoreSnapshot next = current.CloneCanonical();
                next.DecisiveScore = ResolveDecisiveScore(next, controls,
                    key, control);
                RecalculateLossesAndExhaustion(next);
                RawScoreTotals raw = CalculateRawTotals(next, controls, key,
                    control);
                Touch(next, pWorldTime);
                WarScoreSnapshot committed = next;
                if (ShouldAwardOccupationRelief(hasOld, old, pHomeSide,
                        pControllerSide, controllerChanged))
                {
                    WarScoreSnapshot rewarded = next.CloneCanonical();
                    int requested = WarVictoryExhaustionRules.
                        OccupationRelief(value);
                    int applied = ApplyReliefAward(rewarded,
                        pControllerSide, requested);
                    RecalculateLossesAndExhaustion(rewarded);
                    var reliefEvent = new WarScoreReliefEventState
                    {
                        Key = "occupation:" + pWarId + ":" +
                              pFacts.CityId + ":" + control.Occurrence,
                        WarId = pWarId,
                        Kind = "occupation",
                        SubjectId = pFacts.CityId + ":" +
                                    control.Occurrence,
                        BeneficiarySide = pControllerSide,
                        Amount = applied,
                        WorldTime = pWorldTime
                    };
                    bool inserted = _persistence.SaveControlWithReliefEvent(
                        next, rewarded, control, reliefEvent);
                    committed = inserted ? rewarded : next;
                }
                else
                {
                    _persistence.Save(next, control);
                }
                PublishRawTotals(pWarId, raw);
                controls[key] = control;
                AddCityControlIndex(pFacts.CityId, pWarId);
                _active[pWarId] = committed;
                return true;
            }
        }

        public bool ClearCityControl(long pWarId, long pCityId,
            double pWorldTime)
        {
            if (pCityId < 0) return false;
            lock (_gate)
            {
                if (!_active.TryGetValue(pWarId, out WarScoreSnapshot current) ||
                    !_controls.TryGetValue(pWarId,
                        out Dictionary<string, WarScoreControlState> controls))
                    return false;
                string key = BuildControlKey(pWarId, "city",
                    pCityId.ToString());
                if (!controls.TryGetValue(key, out WarScoreControlState state) ||
                    state.Kind != "city") return false;
                WarScoreSnapshot next = current.CloneCanonical();
                next.DecisiveScore = ResolveDecisiveScore(next, controls,
                    key, pReplacement: null);
                RecalculateLossesAndExhaustion(next);
                RawScoreTotals raw = CalculateRawTotalsWithout(next,
                    controls, key);
                Touch(next, pWorldTime);
                _persistence.DeleteControl(next, key);
                PublishRawTotals(pWarId, raw);
                controls.Remove(key);
                RemoveCityControlIndex(pCityId, pWarId);
                _active[pWarId] = next;
                return true;
            }
        }

        public bool RecordGoalControlChanged(long pWarId, string pGoalId,
            WarScoreSide pBeneficiarySide, bool pControlled, int pValue,
            bool pMatchesActiveWarGoal, double pWorldTime)
        {
            if (string.IsNullOrWhiteSpace(pGoalId) ||
                !WarScoreRules.IsParticipantSide(pBeneficiarySide) ||
                !pMatchesActiveWarGoal) return false;
            lock (_gate)
            {
                if (!_active.TryGetValue(pWarId, out WarScoreSnapshot current))
                    return false;
                string key = BuildControlKey(pWarId, "goal", pGoalId);
                int value = WarScoreRules.NormalizeGoalValue(pValue);
                int contribution = pControlled
                    ? (pBeneficiarySide == WarScoreSide.Attackers ? value : -value)
                    : 0;
                WarScoreSide controller = pControlled
                    ? pBeneficiarySide
                    : WarScoreSide.None;
                Dictionary<string, WarScoreControlState> controls = _controls[pWarId];
                bool hasOld = controls.TryGetValue(key,
                    out WarScoreControlState old);
                if (hasOld &&
                    old.HomeSide == pBeneficiarySide &&
                    old.ControllerSide == controller &&
                    old.Value == value && old.Contribution == contribution &&
                    old.VerifiedGoal) return false;
                var control = new WarScoreControlState
                {
                    Key = key,
                    WarId = pWarId,
                    Kind = "goal",
                    SubjectId = pGoalId,
                    HomeKingdomId = -1,
                    ControllerKingdomId = -1,
                    HomeSide = pBeneficiarySide,
                    ControllerSide = controller,
                    Value = value,
                    Contribution = contribution,
                    VerifiedGoal = true,
                    StartedTime = hasOld &&
                                  old.ControllerSide == controller &&
                                  old.StartedTime >= 0d
                        ? old.StartedTime
                        : pWorldTime,
                    UpdatedTime = pWorldTime
                };
                WarScoreSnapshot next = current.CloneCanonical();
                RawScoreTotals raw = CalculateRawTotals(next, controls, key,
                    control);
                Touch(next, pWorldTime);
                _persistence.Save(next, control);
                PublishRawTotals(pWarId, raw);
                controls[key] = control;
                _active[pWarId] = next;
                return true;
            }
        }

        public bool RecordBattleResult(long pWarId, WarScoreSide pWinnerSide,
            int pIntensity, double pWorldTime)
        {
            int delta = WarScoreRules.BattleDelta(pWinnerSide, pIntensity);
            if (delta == 0) return false;
            lock (_gate)
            {
                if (!_active.TryGetValue(pWarId, out WarScoreSnapshot current))
                    return false;
                WarScoreSnapshot next = current.CloneCanonical();
                next.BattleScore = WarScoreRules.ClampBattleScore(
                    next.BattleScore + delta);
                RecalculateTotal(next);
                Touch(next, pWorldTime);
                _persistence.Save(next);
                _active[pWarId] = next;
                return true;
            }
        }

        public bool RecordBattleVictoryRelief(long pWarId,
            string pEpisodeId, WarScoreSide pWinnerSide, int pIntensity,
            double pWorldTime)
        {
            if (string.IsNullOrWhiteSpace(pEpisodeId) ||
                !WarScoreRules.IsParticipantSide(pWinnerSide) ||
                pIntensity <= 0 || double.IsNaN(pWorldTime) ||
                double.IsInfinity(pWorldTime)) return false;
            lock (_gate)
            {
                if (!_active.TryGetValue(pWarId, out WarScoreSnapshot current))
                    return false;
                WarScoreSnapshot rewarded = current.CloneCanonical();
                int battleDelta = WarScoreRules.BattleDelta(pWinnerSide,
                    pIntensity);
                rewarded.BattleScore = WarScoreRules.ClampBattleScore(
                    rewarded.BattleScore + battleDelta);
                int requested = WarVictoryExhaustionRules.BattleRelief(
                    pIntensity);
                int applied = ApplyReliefAward(rewarded, pWinnerSide,
                    requested);
                RecalculateLossesAndExhaustion(rewarded);
                RecalculateTotal(rewarded);
                Touch(rewarded, pWorldTime);
                var reliefEvent = new WarScoreReliefEventState
                {
                    Key = "battle:" + pWarId + ":" + pEpisodeId,
                    WarId = pWarId,
                    Kind = "battle",
                    SubjectId = pEpisodeId,
                    BeneficiarySide = pWinnerSide,
                    Amount = applied,
                    WorldTime = pWorldTime
                };
                bool inserted = _persistence.SaveWithReliefEvent(current,
                    rewarded, reliefEvent);
                if (!inserted) return false;
                _active[pWarId] = rewarded;
                return true;
            }
        }

        public bool RecordDeath(long pWarId, WarScoreSide pSideOfDead,
            double pWorldTime)
        {
            if (!WarScoreRules.IsParticipantSide(pSideOfDead)) return false;
            lock (_gate)
            {
                if (!_active.TryGetValue(pWarId, out WarScoreSnapshot current))
                    return false;
                WarScoreSnapshot next = current.CloneCanonical();
                if (pSideOfDead == WarScoreSide.Attackers)
                    next.AttackerLosses = IncrementBounded(next.AttackerLosses);
                else
                    next.DefenderLosses = IncrementBounded(next.DefenderLosses);
                RecalculateLossesAndExhaustion(next);
                RecalculateTotal(next);
                Touch(next, pWorldTime);
                _persistence.Save(next);
                _active[pWarId] = next;
                return true;
            }
        }

        internal bool TryApplyReserveExhaustion(long pWarId,
            WarScoreSide pSide, double pWorldTime)
        {
            if (!WarScoreRules.IsParticipantSide(pSide)) return false;
            lock (_gate)
            {
                if (!_active.TryGetValue(pWarId,
                        out WarScoreSnapshot current) || !current.Active)
                    return false;
                int existing = pSide == WarScoreSide.Attackers
                    ? current.AttackerReserveExhaustion
                    : current.DefenderReserveExhaustion;
                int contribution = CityReservePoolRules.
                    ApplyReserveExhaustionContribution(existing);
                if (contribution == existing) return false;
                WarScoreSnapshot next = current.CloneCanonical();
                if (pSide == WarScoreSide.Attackers)
                    next.AttackerReserveExhaustion = contribution;
                else
                    next.DefenderReserveExhaustion = contribution;
                RecalculateLossesAndExhaustion(next);
                RecalculateTotal(next);
                Touch(next, pWorldTime);
                _persistence.Save(next);
                _active[pWarId] = next;
                return true;
            }
        }

        public bool SynchronizeDeaths(long pWarId, int pAttackerLosses,
            int pDefenderLosses, double pWorldTime)
        {
            if (pAttackerLosses < 0 || pDefenderLosses < 0) return false;
            lock (_gate)
            {
                if (!_active.TryGetValue(pWarId, out WarScoreSnapshot current))
                    return false;
                int attackers = Math.Max(current.AttackerLosses,
                    pAttackerLosses);
                int defenders = Math.Max(current.DefenderLosses,
                    pDefenderLosses);
                if (attackers == current.AttackerLosses &&
                    defenders == current.DefenderLosses) return false;
                WarScoreSnapshot next = current.CloneCanonical();
                next.AttackerLosses = attackers;
                next.DefenderLosses = defenders;
                RecalculateLossesAndExhaustion(next);
                RecalculateTotal(next);
                Touch(next, pWorldTime);
                _persistence.Save(next);
                _active[pWarId] = next;
                return true;
            }
        }

        public bool CalibrateYear(long pWarId, int pDurationYears,
            double pWorldTime)
        {
            return CalibrateYear(pWarId, pDurationYears, pDurationYears,
                0, 0, pWorldTime);
        }

        public bool CalibrateYear(long pWarId, int pDurationYears,
            int pCalibrationYear, double pWorldTime)
        {
            return CalibrateYear(pWarId, pDurationYears, pCalibrationYear,
                0, 0, pWorldTime);
        }

        public bool CalibrateYear(long pWarId, int pDurationYears,
            int pCalibrationYear, int pAttackerMobilizationBaseline,
            int pDefenderMobilizationBaseline, double pWorldTime)
        {
            lock (_gate)
            {
                if (!_active.TryGetValue(pWarId, out WarScoreSnapshot current))
                    return false;
                if (pCalibrationYear <= current.LastCalibratedYear)
                    return false;
                int duration = Math.Max(current.DurationYears,
                    Math.Max(0, pDurationYears));
                WarScoreSnapshot next = current.CloneCanonical();
                int elapsedYears = ElapsedCalibrationYears(
                    current.LastCalibratedYear, pCalibrationYear);
                next.DurationYears = duration;
                next.LastCalibratedYear = pCalibrationYear;
                next.AttackerMobilizationBaseline = Math.Max(
                    current.AttackerMobilizationBaseline,
                    Math.Max(0, pAttackerMobilizationBaseline));
                next.DefenderMobilizationBaseline = Math.Max(
                    current.DefenderMobilizationBaseline,
                    Math.Max(0, pDefenderMobilizationBaseline));
                next.AttackerExhaustionRelief = WarVictoryExhaustionRules.
                    DecayRelief(next.AttackerExhaustionRelief,
                        elapsedYears);
                next.DefenderExhaustionRelief = WarVictoryExhaustionRules.
                    DecayRelief(next.DefenderExhaustionRelief,
                        elapsedYears);
                RecalculateLossesAndExhaustion(next);
                RecalculateTotal(next);
                Touch(next, pWorldTime);
                _persistence.Save(next);
                _active[pWarId] = next;
                return true;
            }
        }

        public bool EndWar(long pWarId, string pWinner, double pWorldTime)
        {
            lock (_gate)
            {
                if (!_active.TryGetValue(pWarId, out WarScoreSnapshot current))
                    return false;
                WarScoreSnapshot ended = current.CloneCanonical();
                ended.Active = false;
                ended.Winner = pWinner ?? "";
                ended.EndedTime = pWorldTime;
                Touch(ended, pWorldTime);
                _persistence.End(ended);
                _active.Remove(pWarId);
                _controls.Remove(pWarId);
                _rawCityScores.Remove(pWarId);
                _rawGoalScores.Remove(pWarId);
                _cityScoreBudgets.Remove(pWarId);
                RemoveCityControlIndexes(pWarId);
                _history[pWarId] = ended;
                return true;
            }
        }

        public bool TryGetSnapshot(long pWarId, WarScoreSide pPerspective,
            out WarScoreSnapshot pSnapshot)
        {
            pSnapshot = null;
            if (!WarScoreRules.IsParticipantSide(pPerspective)) return false;
            lock (_gate)
            {
                if (!_active.TryGetValue(pWarId, out WarScoreSnapshot value) &&
                    !_history.TryGetValue(pWarId, out value))
                {
                    value = _persistence.Read(pWarId);
                    if (value == null) return false;
                    if (value.Active) _active[pWarId] = value;
                    else _history[pWarId] = value;
                }
                pSnapshot = value.ForPerspective(pPerspective);
                return true;
            }
        }

        public IReadOnlyList<WarScoreSnapshot> ReadHistory(long pKingdomId,
            int pLimit = 64)
        {
            lock (_gate)
            {
                IReadOnlyList<WarScoreSnapshot> rows =
                    _persistence.ReadHistory(pKingdomId, pLimit);
                var result = new List<WarScoreSnapshot>(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                {
                    WarScoreSnapshot row = rows[i];
                    WarScoreSide side = row.DefenderKingdomId == pKingdomId
                        ? WarScoreSide.Defenders
                        : WarScoreSide.Attackers;
                    result.Add(row.ForPerspective(side));
                    _history[row.WarId] = row;
                }
                return result;
            }
        }

        public IReadOnlyList<WarScoreOccupiedCitySnapshot> ReadOccupiedCities(
            long pWarId, long pControllerKingdomId, int pLimit = 64)
        {
            if (pWarId < 0 || pControllerKingdomId < 0)
                return Array.Empty<WarScoreOccupiedCitySnapshot>();
            lock (_gate)
                return _persistence.ReadOccupiedCities(pWarId,
                    pControllerKingdomId, pLimit);
        }

        public IReadOnlyList<WarScoreOccupiedCitySnapshot>
            ReadOccupiedCitiesForWar(long pWarId, int pLimit = 128)
        {
            if (pWarId < 0)
                return Array.Empty<WarScoreOccupiedCitySnapshot>();
            lock (_gate)
                return _persistence.ReadOccupiedCitiesForWar(pWarId,
                    pLimit);
        }

        internal IReadOnlyList<WarScoreOccupiedCitySnapshot>
            ReadOccupiedCitiesByHomeKingdom(long pWarId,
                long pHomeKingdomId, string pAfterControlKey,
                int pLimit = 32)
        {
            if (pWarId < 0 || pHomeKingdomId < 0)
                return Array.Empty<WarScoreOccupiedCitySnapshot>();
            lock (_gate)
                return _persistence.ReadOccupiedCitiesByHomeKingdom(
                    pWarId, pHomeKingdomId, pAfterControlKey, pLimit);
        }

        public IReadOnlyList<WarScoreOccupiedCitySnapshot>
            ReadAllOccupiedCitiesForWarCleanup(long pWarId)
        {
            if (pWarId < 0)
                return Array.Empty<WarScoreOccupiedCitySnapshot>();
            lock (_gate)
                return _persistence.ReadAllOccupiedCitiesForWarCleanup(
                    pWarId);
        }

        internal bool TryReadFrozenOccupation(long pWarId, long pCityId,
            out long pControllerKingdomId)
        {
            pControllerKingdomId = -1;
            if (pWarId < 0 || pCityId < 0) return false;
            lock (_gate)
                return _persistence.TryReadFrozenOccupation(pWarId, pCityId,
                    out pControllerKingdomId);
        }

        public bool DeleteHistory(long pWarId)
        {
            lock (_gate)
            {
                if (_active.ContainsKey(pWarId)) return false;
                bool deleted = _persistence.DeleteHistory(pWarId);
                if (deleted) _history.Remove(pWarId);
                return deleted;
            }
        }

        private readonly struct RawScoreTotals
        {
            public RawScoreTotals(int pCity, int pGoal)
            {
                City = pCity;
                Goal = pGoal;
            }

            public int City { get; }
            public int Goal { get; }
        }

        private RawScoreTotals CalculateRawTotals(WarScoreSnapshot pSnapshot,
            Dictionary<string, WarScoreControlState> pControls,
            string pReplacementKey, WarScoreControlState pReplacement)
        {
            int city = _rawCityScores.TryGetValue(pSnapshot.WarId,
                out int rawCity) ? rawCity : 0;
            int goal = _rawGoalScores.TryGetValue(pSnapshot.WarId,
                out int rawGoal) ? rawGoal : 0;
            if (pControls.TryGetValue(pReplacementKey,
                    out WarScoreControlState previous))
            {
                if (previous.Kind == "city")
                    city = AddSaturating(city, -previous.Contribution);
                else if (previous.Kind == "goal")
                    goal = AddSaturating(goal, -previous.Contribution);
            }
            if (pReplacement.Kind == "city")
                city = AddSaturating(city, pReplacement.Contribution);
            else if (pReplacement.Kind == "goal")
                goal = AddSaturating(goal, pReplacement.Contribution);
            pSnapshot.CityScore = WarScoreRules.ClampCityScore(city,
                CityScoreBudgetForWar(pSnapshot.WarId));
            pSnapshot.GoalScore = WarScoreRules.ClampGoalScore(goal);
            RecalculateTotal(pSnapshot);
            return new RawScoreTotals(city, goal);
        }

        private RawScoreTotals CalculateRawTotalsWithout(
            WarScoreSnapshot pSnapshot,
            Dictionary<string, WarScoreControlState> pControls,
            string pRemovedKey)
        {
            int city = _rawCityScores.TryGetValue(pSnapshot.WarId,
                out int rawCity) ? rawCity : 0;
            int goal = _rawGoalScores.TryGetValue(pSnapshot.WarId,
                out int rawGoal) ? rawGoal : 0;
            if (pControls.TryGetValue(pRemovedKey,
                    out WarScoreControlState removed))
            {
                if (removed.Kind == "city")
                    city = AddSaturating(city, -removed.Contribution);
                else if (removed.Kind == "goal")
                    goal = AddSaturating(goal, -removed.Contribution);
            }
            pSnapshot.CityScore = WarScoreRules.ClampCityScore(city,
                CityScoreBudgetForWar(pSnapshot.WarId));
            pSnapshot.GoalScore = WarScoreRules.ClampGoalScore(goal);
            RecalculateTotal(pSnapshot);
            return new RawScoreTotals(city, goal);
        }

        private void PublishRawTotals(long pWarId, RawScoreTotals pTotals)
        {
            _rawCityScores[pWarId] = pTotals.City;
            _rawGoalScores[pWarId] = pTotals.Goal;
        }

        private int CityScoreBudgetForWar(long pWarId)
        {
            return _cityScoreBudgets.TryGetValue(pWarId, out int budget)
                ? WarScoreRules.NormalizeCityScoreBudget(budget)
                : WarScoreRules.DefaultCityScoreBudget;
        }

        private static int AddSaturating(int pLeft, int pRight)
        {
            long value = (long)pLeft + pRight;
            if (value > int.MaxValue) return int.MaxValue;
            return value < int.MinValue ? int.MinValue : (int)value;
        }

        internal bool TryGetFrozenCityControl(long pWarId, long pCityId,
            out WarScoreControlState pState)
        {
            pState = null;
            if (pWarId < 0 || pCityId < 0) return false;
            lock (_gate)
            {
                if (!_cityControlWars.TryGetValue(pCityId,
                        out HashSet<long> warIds) ||
                    !warIds.Contains(pWarId) ||
                    !_controls.TryGetValue(pWarId,
                        out Dictionary<string, WarScoreControlState> controls) ||
                    !controls.TryGetValue(BuildControlKey(pWarId, "city",
                        pCityId.ToString()), out WarScoreControlState state))
                    return false;
                pState = state.Clone();
                return true;
            }
        }

        internal IReadOnlyList<long> ReadFrozenCityControlWarIds(
            long pCityId)
        {
            if (pCityId < 0) return Array.Empty<long>();
            lock (_gate)
            {
                if (!_cityControlWars.TryGetValue(pCityId,
                        out HashSet<long> warIds) || warIds.Count == 0)
                    return Array.Empty<long>();
                var result = new List<long>(warIds);
                result.Sort();
                return result;
            }
        }

        private void RemoveCityControlIndexes(long pWarId)
        {
            var remove = new List<long>();
            foreach (KeyValuePair<long, HashSet<long>> pair in
                     _cityControlWars)
            {
                pair.Value.Remove(pWarId);
                if (pair.Value.Count == 0) remove.Add(pair.Key);
            }
            for (int i = 0; i < remove.Count; i++)
                _cityControlWars.Remove(remove[i]);
        }

        private void AddCityControlIndex(long pCityId, long pWarId)
        {
            if (!_cityControlWars.TryGetValue(pCityId,
                    out HashSet<long> warIds))
            {
                warIds = new HashSet<long>();
                _cityControlWars[pCityId] = warIds;
            }
            warIds.Add(pWarId);
        }

        private void RemoveCityControlIndex(long pCityId, long pWarId)
        {
            if (!_cityControlWars.TryGetValue(pCityId,
                    out HashSet<long> warIds)) return;
            warIds.Remove(pWarId);
            if (warIds.Count == 0) _cityControlWars.Remove(pCityId);
        }

        private static void RecalculateLossesAndExhaustion(
            WarScoreSnapshot pSnapshot)
        {
            pSnapshot.LossScore = WarScoreRules.LossScore(
                pSnapshot.AttackerLosses, pSnapshot.DefenderLosses);
            pSnapshot.AttackerExhaustion = WarVictoryExhaustionRules.
                ApplyRelief(CityReservePoolRules.ComposeExhaustion(
                        WarScoreRules.WarExhaustion(
                            pSnapshot.DurationYears,
                            pSnapshot.AttackerLosses,
                            pSnapshot.AttackerMobilizationBaseline),
                        pSnapshot.AttackerReserveExhaustion),
                    pSnapshot.AttackerExhaustionRelief);
            pSnapshot.DefenderExhaustion = WarVictoryExhaustionRules.
                ApplyRelief(CityReservePoolRules.ComposeExhaustion(
                        WarScoreRules.WarExhaustion(
                            pSnapshot.DurationYears,
                            pSnapshot.DefenderLosses,
                            pSnapshot.DefenderMobilizationBaseline),
                        pSnapshot.DefenderReserveExhaustion),
                    pSnapshot.DefenderExhaustionRelief);
            if (pSnapshot.DecisiveScore > 0)
                pSnapshot.DefenderExhaustion = 100;
            else if (pSnapshot.DecisiveScore < 0)
                pSnapshot.AttackerExhaustion = 100;
        }

        private static void RecalculateTotal(WarScoreSnapshot pSnapshot)
        {
            int composed = WarScoreRules.ComposeSignedScore(
                pSnapshot.CityScore, pSnapshot.BattleScore,
                pSnapshot.GoalScore, pSnapshot.LossScore);
            pSnapshot.Score = pSnapshot.DecisiveScore == 0
                ? composed
                : pSnapshot.DecisiveScore;
        }

        private static int ResolveDecisiveScore(WarScoreSnapshot pSnapshot,
            IReadOnlyDictionary<string, WarScoreControlState> pControls,
            string pReplacementKey, WarScoreControlState pReplacement)
        {
            int score = 0;
            var occupiedCounts = new Dictionary<long, int>();
            var remainingCounts = new Dictionary<long, int>();
            var homeSides = new Dictionary<long, WarScoreSide>();
            if (pControls != null)
                foreach (KeyValuePair<string, WarScoreControlState> pair in
                         pControls)
                {
                    if (pair.Key == pReplacementKey) continue;
                    WarScoreControlState state = pair.Value;
                    score = AccumulateDecisiveControl(pSnapshot, state,
                        occupiedCounts, remainingCounts, homeSides, score);
                    if (score == int.MinValue) return 0;
                }
            score = AccumulateDecisiveControl(pSnapshot, pReplacement,
                occupiedCounts, remainingCounts, homeSides, score);
            if (score == int.MinValue) return 0;
            foreach (KeyValuePair<long, int> pair in occupiedCounts)
            {
                if (!remainingCounts.TryGetValue(pair.Key,
                        out int remainingCount)) continue;
                int candidate = WarScoreRules.
                    ResolveRealmOccupationDecisiveScore(
                        homeSides[pair.Key], pair.Value,
                        remainingCount);
                if (candidate == 0) continue;
                if (score == 0) score = candidate;
                else if (score != candidate) return 0;
            }
            return score;
        }

        private static int AccumulateDecisiveControl(
            WarScoreSnapshot pSnapshot, WarScoreControlState pState,
            Dictionary<long, int> pOccupiedCounts,
            Dictionary<long, int> pRemainingCounts,
            Dictionary<long, WarScoreSide> pHomeSides, int pLegacyScore)
        {
            if (pState == null) return pLegacyScore;
            if (pState.Decisive && pState.HomeCityCount <= 0)
            {
                int legacy = pState.ControllerSide ==
                             WarScoreSide.Attackers ? 100 : -100;
                if (pLegacyScore != 0 && pLegacyScore != legacy)
                    return int.MinValue;
                pLegacyScore = legacy;
            }
            if (pState.Kind != "city" ||
                !WarScoreRules.IsParticipantSide(pState.HomeSide) ||
                !WarScoreRules.IsParticipantSide(pState.ControllerSide) ||
                pState.HomeSide == pState.ControllerSide)
                return pLegacyScore;

            long realmId = pState.HomeKingdomId;
            if (realmId < 0)
                realmId = pState.HomeSide == WarScoreSide.Attackers
                    ? pSnapshot.AttackerKingdomId
                    : pSnapshot.DefenderKingdomId;
            long mainRealmId = pState.HomeSide == WarScoreSide.Attackers
                ? pSnapshot.AttackerKingdomId
                : pSnapshot.DefenderKingdomId;
            if (realmId != mainRealmId) return pLegacyScore;

            pOccupiedCounts.TryGetValue(realmId, out int occupied);
            pOccupiedCounts[realmId] = occupied + 1;
            if (pState.HomeCityCount > 0)
            {
                pRemainingCounts.TryGetValue(realmId, out int remaining);
                pRemainingCounts[realmId] = Math.Max(remaining,
                    pState.HomeCityCount);
            }
            pHomeSides[realmId] = pState.HomeSide;
            return pLegacyScore;
        }

        private static void Touch(WarScoreSnapshot pSnapshot,
            double pWorldTime)
        {
            pSnapshot.UpdatedTime = pWorldTime;
            pSnapshot.Revision = pSnapshot.Revision == long.MaxValue
                ? long.MaxValue
                : pSnapshot.Revision + 1;
        }

        private static int IncrementBounded(int pValue)
        {
            return pValue == int.MaxValue ? int.MaxValue : pValue + 1;
        }

        private static int ResolveControlOccurrence(bool pHasOld,
            WarScoreControlState pOld, WarScoreSide pHomeSide,
            WarScoreSide pControllerSide, bool pControllerChanged)
        {
            if (!pHasOld)
                return WarScoreRules.IsParticipantSide(pControllerSide) &&
                       pControllerSide != pHomeSide ? 1 : 0;
            return pControllerChanged
                ? IncrementBounded(Math.Max(0, pOld.Occurrence))
                : Math.Max(0, pOld.Occurrence);
        }

        private static bool ShouldAwardOccupationRelief(bool pHasOld,
            WarScoreControlState pOld, WarScoreSide pHomeSide,
            WarScoreSide pControllerSide, bool pControllerChanged)
        {
            if (!WarScoreRules.IsParticipantSide(pControllerSide))
                return false;
            if (!pHasOld) return pControllerSide != pHomeSide;
            if (!pControllerChanged) return false;
            if (pOld.ControllerSide == WarScoreSide.None)
                return pControllerSide != pHomeSide;
            return WarScoreRules.IsParticipantSide(pOld.ControllerSide) &&
                   pOld.ControllerSide != pControllerSide;
        }

        private static int ApplyReliefAward(WarScoreSnapshot pSnapshot,
            WarScoreSide pSide, int pRequested)
        {
            if (pSide == WarScoreSide.Attackers)
            {
                int previous = pSnapshot.AttackerExhaustionRelief;
                pSnapshot.AttackerExhaustionRelief =
                    WarVictoryExhaustionRules.AddRelief(previous,
                        pRequested);
                return pSnapshot.AttackerExhaustionRelief - previous;
            }
            int defenderPrevious = pSnapshot.DefenderExhaustionRelief;
            pSnapshot.DefenderExhaustionRelief =
                WarVictoryExhaustionRules.AddRelief(defenderPrevious,
                    pRequested);
            return pSnapshot.DefenderExhaustionRelief - defenderPrevious;
        }

        private static int ElapsedCalibrationYears(int pPrevious,
            int pCurrent)
        {
            if (pPrevious == int.MinValue) return 0;
            long elapsed = (long)pCurrent - pPrevious;
            return elapsed <= 0 ? 0 :
                (int)Math.Min(int.MaxValue, elapsed);
        }

        private static string BuildControlKey(long pWarId, string pKind,
            string pSubjectId)
        {
            return pWarId + ":" + (pKind ?? "") + ":" +
                   (pSubjectId ?? "");
        }
    }
}
