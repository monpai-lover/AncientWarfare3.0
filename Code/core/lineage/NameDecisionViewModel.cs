using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public sealed class NameDecisionViewModel
    {
        private const int RequiredPoliticalPoints = 30;
        private readonly HashSet<string> _usedNames =
            new HashSet<string>(StringComparer.Ordinal);
        private int _politicalPoints;

        public string StateName { get; private set; } = "";
        public string Input { get; private set; } = "";
        public string Preview { get; private set; } = "";
        public string ErrorKey { get; private set; } = "";
        public bool CanConfirm { get; private set; }

        public static NameDecisionViewModel ForEra(string pStateName,
            string pInitialEra, int politicalPoints)
        {
            var model = new NameDecisionViewModel
            {
                StateName = (pStateName ?? "").Trim(),
                Input = pInitialEra ?? "",
                _politicalPoints = Math.Max(0, politicalPoints)
            };
            model.Evaluate();
            return model;
        }

        public void SetInput(string pValue)
        {
            Input = pValue ?? "";
            Evaluate();
        }

        public void SetUsedNames(IEnumerable<string> pUsedNames)
        {
            _usedNames.Clear();
            if (pUsedNames != null)
                foreach (string name in pUsedNames)
                    if (!string.IsNullOrEmpty(name)) _usedNames.Add(name);
            Evaluate();
        }

        public void SetPoliticalPoints(int pPoliticalPoints)
        {
            _politicalPoints = Math.Max(0, pPoliticalPoints);
            Evaluate();
        }

        public void ApplyBlockReason(EraChangeBlockReason pReason)
        {
            if (pReason == EraChangeBlockReason.None)
            {
                Evaluate();
                return;
            }
            ErrorKey = ErrorKeyFor(pReason);
            CanConfirm = false;
        }

        public static string ErrorKeyFor(EraChangeBlockReason pReason)
        {
            return pReason switch
            {
                EraChangeBlockReason.NotHereditaryEmperor =>
                    "aw_title_error_not_hereditary_emperor",
                EraChangeBlockReason.BelowEmpireRank =>
                    "aw_title_error_below_empire_rank",
                EraChangeBlockReason.NotIndependent =>
                    "aw_title_error_not_independent",
                EraChangeBlockReason.AtWar => "aw_title_error_at_war",
                EraChangeBlockReason.Cooldown => "aw_title_error_cooldown",
                EraChangeBlockReason.InsufficientPoliticalPoints =>
                    "aw_title_error_insufficient_points",
                EraChangeBlockReason.InvalidName => "aw_title_error_invalid_name",
                EraChangeBlockReason.DuplicateName => "aw_title_error_duplicate_name",
                EraChangeBlockReason.ArchiveUnavailable =>
                    "aw_title_error_archive_unavailable",
                EraChangeBlockReason.MissingLineageIdentity =>
                    "aw_title_error_missing_shi",
                EraChangeBlockReason.MissingReign => "aw_title_error_missing_reign",
                EraChangeBlockReason.PersistenceFailed =>
                    "aw_title_error_persistence_failed",
                _ => "aw_title_error_unknown"
            };
        }

        private void Evaluate()
        {
            bool valid = EraNameRules.IsValidCustom(Input);
            Preview = RulerAppellationRules.LivingEmperor(
                StateName, valid ? Input : "");
            if (!valid)
            {
                ErrorKey = "aw_title_error_invalid_name";
                CanConfirm = false;
                return;
            }
            if (_usedNames.Contains(Input))
            {
                ErrorKey = "aw_title_error_duplicate_name";
                CanConfirm = false;
                return;
            }
            if (_politicalPoints < RequiredPoliticalPoints)
            {
                ErrorKey = "aw_title_error_insufficient_points";
                CanConfirm = false;
                return;
            }
            ErrorKey = "";
            CanConfirm = true;
        }
    }
}
