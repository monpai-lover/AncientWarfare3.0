using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal sealed class EraDecisionSnapshot
    {
        public string StateName = "";
        public string InitialEra = "";
        public int PoliticalPoints;
        public readonly HashSet<string> UsedNames =
            new HashSet<string>(StringComparer.Ordinal);
        public readonly List<string> AvailableHistoricalNames =
            new List<string>();
        public EraChangeBlockReason BlockReason;
    }

    internal static class YearNameService
    {
        public const int VoluntaryChangeCost = 30;

        private static string T(string pKey) =>
            HistoryLocalizationRules.Text(pKey);

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null &&
                                     LineageArchiveManager.Instance.InitializeSuccessful;

        public static EraChangeResult TryStartAccessionEra(Kingdom pKingdom,
            Actor pEmperor)
        {
            long reignId = ReignRecordWriter.FindOpenReignId(pKingdom?.id ?? -1L);
            string sourceEventId = reignId < 0 ? "" : "accession:" + reignId;
            return TryChangeEra(pKingdom, pEmperor, "", EraChangeKind.Accession,
                EraChangeReason.Accession, sourceEventId);
        }

        public static EraChangeResult TryStartRestoredMonarchyEra(
            Kingdom pKingdom, Actor pEmperor)
        {
            long reignId = ReignRecordWriter.FindOpenReignId(pKingdom?.id ?? -1L);
            string sourceEventId = reignId < 0
                ? ""
                : "monarchy_restored:" + reignId + ":" + Date.getCurrentYear();
            return TryChangeEra(pKingdom, pEmperor, "", EraChangeKind.Accession,
                EraChangeReason.Accession, sourceEventId);
        }

        public static EraDecisionSnapshot PrepareVoluntaryDecision(
            Kingdom pKingdom)
        {
            var snapshot = new EraDecisionSnapshot();
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                pKingdom.king?.data == null)
            {
                snapshot.BlockReason = EraChangeBlockReason.NotHereditaryEmperor;
                return snapshot;
            }

            Actor emperor = pKingdom.king;
            snapshot.StateName = StateNameService.GetBoundOrCurrentName(pKingdom);
            snapshot.PoliticalPoints = Math.Max(0, (int)Math.Floor(
                KingdomPolicyService.GetPoliticalPoints(pKingdom)));

            bool hereditary = !RepublicGovernmentService.IsRepublic(pKingdom) &&
                              !RepublicGovernmentService.IsRepublicLeader(emperor) &&
                              RepublicGovernmentService.HasEstablishedMonarchy(pKingdom) &&
                              (LineageService.IsXiaKingdom(pKingdom) ||
                               XiaizationService.UsesXiaizedInstitutionSystem(pKingdom));
            if (!hereditary)
            {
                snapshot.BlockReason = EraChangeBlockReason.NotHereditaryEmperor;
                return snapshot;
            }
            if (!KingdomTitleService.IsEmperor(pKingdom))
            {
                snapshot.BlockReason = EraChangeBlockReason.BelowEmpireRank;
                return snapshot;
            }
            if (VassalService.GetSuzerain(pKingdom) != null)
            {
                snapshot.BlockReason = EraChangeBlockReason.NotIndependent;
                return snapshot;
            }
            if (!Ready)
            {
                snapshot.BlockReason = EraChangeBlockReason.ArchiveUnavailable;
                return snapshot;
            }

            emperor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            if (shiId < 0)
            {
                snapshot.BlockReason = EraChangeBlockReason.MissingLineageIdentity;
                return snapshot;
            }
            if (ReignRecordWriter.FindOpenReignId(pKingdom.id) < 0)
            {
                snapshot.BlockReason = EraChangeBlockReason.MissingReign;
                return snapshot;
            }

            int yearsSinceChange;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText =
                    "SELECT TITLE_VALUE FROM " +
                    DynastyTitleRegistryTableItem.GetTableName() +
                    " WHERE SHI_ID=@shi AND TITLE_TYPE='era' AND CYCLE_NO=0;" +
                    "SELECT MAX(START_YEAR) FROM " +
                    EraPeriodTableItem.GetTableName() +
                    " WHERE KINGDOM_ID=@kingdom AND " +
                    "CHANGE_KIND IN ('voluntary','ai_major_event')";
                command.Parameters.AddWithValue("@shi", shiId);
                command.Parameters.AddWithValue("@kingdom", pKingdom.id);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string value = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    if (!string.IsNullOrEmpty(value)) snapshot.UsedNames.Add(value);
                }
                object lastYear = null;
                if (reader.NextResult() && reader.Read() && !reader.IsDBNull(0))
                    lastYear = reader.GetValue(0);
                yearsSinceChange = lastYear == null
                    ? int.MaxValue
                    : Math.Max(0, Date.getCurrentYear() - Convert.ToInt32(lastYear));
            }
            catch
            {
                snapshot.BlockReason = EraChangeBlockReason.ArchiveUnavailable;
                return snapshot;
            }

            foreach (string candidate in EraNameRules.HistoricalSlots)
            {
                if (!EraNameRules.IsValidCustom(candidate) ||
                    snapshot.UsedNames.Contains(candidate) ||
                    snapshot.AvailableHistoricalNames.Contains(candidate)) continue;
                snapshot.AvailableHistoricalNames.Add(candidate);
            }
            snapshot.InitialEra = snapshot.AvailableHistoricalNames.Count > 0
                ? snapshot.AvailableHistoricalNames[0]
                : EraNameRules.SelectAutomatic(shiId, emperor.data.id,
                    Date.getCurrentYear(), snapshot.UsedNames);

            snapshot.BlockReason = EraNameRules.ValidateVoluntaryChange(
                new EraChangeContext
                {
                    IsHereditaryEmperor = true,
                    IsEmpireRank = true,
                    IsIndependent = true,
                    AtWar = IsAtWar(pKingdom),
                    PoliticalPoints = snapshot.PoliticalPoints,
                    YearsSinceVoluntaryChange = yearsSinceChange,
                    Candidate = snapshot.InitialEra,
                    UsedNames = snapshot.UsedNames
                });
            return snapshot;
        }

        public static EraChangeResult TryChangeEra(Kingdom pKingdom,
            Actor pEmperor, string pRequestedName, EraChangeKind pKind,
            EraChangeReason pReason, string pSourceEventId = "")
        {
            if (!Ready)
                return Blocked(EraChangeBlockReason.ArchiveUnavailable);
            if (!IsSupportedHereditaryEmperor(pKingdom, pEmperor))
                return Blocked(EraChangeBlockReason.NotHereditaryEmperor);

            pEmperor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            if (shiId < 0)
                return Blocked(EraChangeBlockReason.MissingLineageIdentity);
            long reignId = ReignRecordWriter.FindOpenReignId(pKingdom.id);
            if (reignId < 0)
                return Blocked(EraChangeBlockReason.MissingReign);

            int currentYear = Date.getCurrentYear();
            int reignIndex = ReadReignIndex(reignId);
            HashSet<string> usedNames;
            try
            {
                usedNames = DynastyTitleRegistryService.ReadUsed(shiId, "era", 0);
            }
            catch
            {
                return Blocked(EraChangeBlockReason.ArchiveUnavailable);
            }

            string candidate = string.IsNullOrEmpty(pRequestedName)
                ? EraNameRules.SelectAutomatic(shiId, pEmperor.data.id,
                    reignIndex, usedNames)
                : pRequestedName;
            string sourceEventId = ResolveSourceEventId(pKind, pReason,
                reignId, currentYear, candidate, pSourceEventId);
            if (EraRecordWriter.TryReadEvent(reignId, pKind, sourceEventId,
                    out long existingEraId, out string existingName,
                    out double existingStart))
            {
                ProjectCommittedEra(pKingdom, existingName, existingStart);
                return new EraChangeResult(true, existingEraId, existingName,
                    EraChangeBlockReason.None);
            }

            var context = new EraChangeContext
            {
                IsHereditaryEmperor = true,
                IsEmpireRank = KingdomTitleService.IsEmperor(pKingdom),
                IsIndependent = VassalService.GetSuzerain(pKingdom) == null,
                AtWar = IsAtWar(pKingdom),
                PoliticalPoints = Math.Max(0, (int)Math.Floor(
                    KingdomPolicyService.GetPoliticalPoints(pKingdom))),
                YearsSinceVoluntaryChange = EraRecordWriter.YearsSinceLastActiveChange(
                    pKingdom.id, currentYear),
                Candidate = candidate,
                UsedNames = usedNames
            };
            EraChangeBlockReason block = EraNameRules.Validate(context, pKind);
            if (block != EraChangeBlockReason.None) return Blocked(block);

            long reservationId = -1;
            bool requiresPoints = pKind != EraChangeKind.Accession;
            if (requiresPoints && !PoliticalPointReservationService.TryReserve(
                    pKingdom.id, VoluntaryChangeCost, out reservationId))
                return Blocked(EraChangeBlockReason.InsufficientPoliticalPoints);

            double now = LineageService.CurTime();
            string stateName = StateNameService.GetBoundOrCurrentName(pKingdom, shiId);
            HistoryText history = BuildHistory(pKind, pReason, pEmperor,
                stateName, GetLocalEraName(pKingdom), candidate);
            var request = new EraAtomicCommitRequest
            {
                KingdomId = pKingdom.id,
                KingdomColor = HistoryColors.FromKingdom(pKingdom),
                ShiId = shiId,
                ActorId = pEmperor.data.id,
                ReignId = reignId,
                EraName = candidate,
                ChangeKind = EraRecordWriter.KindId(pKind),
                ChangeReason = ReasonId(pReason),
                SourceEventId = sourceEventId,
                DecidedTime = now,
                StartYear = currentYear,
                YearPrefix = HistoryWriter.BuildYearPrefix(now, pKingdom),
                YearPrefixRich = HistoryWriter.BuildYearPrefixRich(now, pKingdom),
                StateName = stateName,
                ActorName = pEmperor.getName() ?? "",
                HistoryContent = history.Plain,
                HistoryContentRich = history.Rich,
                BiographyCategory = ChronicleCategory.HONOR,
                BiographyRole = "king",
                BiographyRoleLabel = "皇帝",
                AgeAtEvent = SafeAge(pEmperor)
            };

            EraAtomicCommitResult committed = EraRecordWriter.TryCommit(request);
            if (!committed.Success)
            {
                if (requiresPoints) PoliticalPointReservationService.Release(reservationId);
                ModClass.LogWarning("Era transaction failed: " + committed.Error);
                return Blocked(EraChangeBlockReason.PersistenceFailed);
            }
            if (requiresPoints)
            {
                if (committed.AlreadyCommitted)
                    PoliticalPointReservationService.Release(reservationId);
                else if (!PoliticalPointReservationService.Commit(reservationId))
                    ModClass.LogWarning("Committed era could not consume its political-point reservation.");
            }

            double startTime = committed.AlreadyCommitted &&
                               EraRecordWriter.TryReadEvent(reignId, pKind,
                                   sourceEventId, out _, out _, out double recordedStart)
                ? recordedStart
                : now;
            ProjectCommittedEra(pKingdom, committed.EraName, startTime);
            return new EraChangeResult(true, committed.EraId,
                committed.EraName, EraChangeBlockReason.None);
        }

        public static EffectiveChronology GetEffectiveChronology(Kingdom pKingdom)
        {
            if (pKingdom?.data == null)
                return new EffectiveChronology(-1, "", "", false);
            bool empireRank = KingdomTitleService.GetTitle(pKingdom) >=
                              KingdomTitle.Emperor;
            Kingdom root = empireRank ? pKingdom : VassalService.GetRootSuzerain(pKingdom);
            EffectiveChronology rootChronology = root?.data != null && root != pKingdom
                ? ReadCachedChronology(root, true)
                : new EffectiveChronology(-1, "", "", false);
            ChronologySourceChoice source = EraNameRules.ResolveChronologySource(
                pKingdom.id, empireRank, root?.id ?? -1L,
                !string.IsNullOrEmpty(rootChronology.EraName));
            if (source.UsesSuzerain)
            {
                return rootChronology;
            }
            return ReadCachedChronology(pKingdom, false);
        }

        public static void EndMonarchicalChronology(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            EraRecordWriter.CloseOpenEra(pKingdom.id);
            pKingdom.data.set(LineageKeys.KINGDOM_YEAR_NAME, "");
            pKingdom.data.set(LineageKeys.KINGDOM_YEAR_START, -1f);
            EraChangeTriggerService.Clear(pKingdom);
            RulerAppellationService.RefreshLivingProjection(pKingdom);
        }

        public static string GetYearName(Kingdom pKingdom)
        {
            EffectiveChronology chronology = GetEffectiveChronology(pKingdom);
            return string.IsNullOrEmpty(chronology.EraName)
                ? ""
                : chronology.EraName + chronology.YearText;
        }

        public static bool RetryCommittedProjection(Kingdom pKingdom)
        {
            if (pKingdom?.data == null ||
                !EraRecordWriter.TryReadCurrent(pKingdom.id, out _,
                    out string eraName, out double startTime)) return false;
            return ProjectCommittedEra(pKingdom, eraName, startTime);
        }

        private static EffectiveChronology ReadCachedChronology(Kingdom pKingdom,
            bool pUsesSuzerain)
        {
            if (pKingdom?.data == null)
                return new EffectiveChronology(-1, "", "", pUsesSuzerain);
            bool hereditary = !RepublicGovernmentService.IsRepublic(pKingdom) &&
                              RepublicGovernmentService.HasEstablishedMonarchy(pKingdom);
            bool empireRank = KingdomTitleService.IsEmperor(pKingdom);
            if (!EraNameRules.CanExposeChronology(hereditary, empireRank))
                return new EffectiveChronology(pKingdom.id, "", "", pUsesSuzerain);
            pKingdom.data.get(LineageKeys.KINGDOM_YEAR_NAME, out string eraName, "");
            if (!EraNameRules.IsValidCustom(eraName))
                return new EffectiveChronology(pKingdom.id, "", "", pUsesSuzerain);
            pKingdom.data.get(LineageKeys.KINGDOM_YEAR_START, out float startTime, -1f);
            if (startTime < 0f)
                return new EffectiveChronology(pKingdom.id, "", "", pUsesSuzerain);
            int year = Math.Max(1, Date.getYearsSince(startTime) + 1);
            return new EffectiveChronology(pKingdom.id, eraName,
                EraNameRules.FormatYear(year), pUsesSuzerain);
        }

        private static bool ProjectCommittedEra(Kingdom pKingdom,
            string pEraName, double pStartTime)
        {
            if (pKingdom?.data == null || !EraNameRules.IsValidCustom(pEraName) ||
                pStartTime < 0) return false;
            try
            {
                pKingdom.data.set(LineageKeys.KINGDOM_YEAR_NAME, pEraName);
                pKingdom.data.set(LineageKeys.KINGDOM_YEAR_START, (float)pStartTime);
                RulerAppellationService.RefreshLivingProjection(pKingdom);
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Era projection failed: " + error.Message);
                return false;
            }
        }

        private static string ResolveSourceEventId(EraChangeKind pKind,
            EraChangeReason pReason, long pReignId, int pYear, string pCandidate,
            string pProvided)
        {
            if (!string.IsNullOrWhiteSpace(pProvided)) return pProvided.Trim();
            return pKind switch
            {
                EraChangeKind.Accession => "accession:" + pReignId,
                EraChangeKind.Voluntary => "player:" + pReignId + ":" + pYear +
                                           ":" + (pCandidate ?? ""),
                _ => ReasonId(pReason) + ":" + pReignId + ":" + pYear
            };
        }

        private static HistoryText BuildHistory(EraChangeKind pKind,
            EraChangeReason pReason, Actor pEmperor, string pStateName,
            string pOldEra, string pNewEra)
        {
            string imperialState = ImperialStateName(pStateName);
            string color = HistoryColors.FromActor(pEmperor);
            if (pKind == EraChangeKind.Accession)
            {
                string title = RulerAppellationRules.LivingEmperor(
                    pStateName, pNewEra);
                HistoryText actor = HistoryText.Actor(pEmperor);
                HistoryText era = HistoryText.Colored(pNewEra, color);
                HistoryText appellation = HistoryText.Colored(title, color);
                string template = T("aw_hist_title_accession_era");
                return new HistoryText(
                    string.Format(template, pEmperor?.getName() ?? "", pNewEra, title),
                    string.Format(template, actor.Rich, era.Rich, appellation.Rich),
                    actor.TargetType, actor.TargetId);
            }
            string oldTitle = imperialState + (pOldEra ?? "") + "皇帝";
            string reason = ReasonLabel(pReason);
            string voluntaryTemplate = T("aw_hist_title_voluntary_era");
            return new HistoryText(
                string.Format(voluntaryTemplate, oldTitle, reason, pNewEra),
                string.Format(voluntaryTemplate,
                    HistoryText.Colored(oldTitle, color).Rich,
                    HistoryColors.EscapeRich(reason),
                    HistoryText.Colored(pNewEra, color).Rich));
        }

        private static string ImperialStateName(string pStateName)
        {
            string state = pStateName ?? "";
            return state.StartsWith("大", StringComparison.Ordinal) ? state : "大" + state;
        }

        private static string ReasonLabel(EraChangeReason pReason)
        {
            return T("aw_title_reason_" + ReasonId(pReason));
        }

        private static string ReasonId(EraChangeReason pReason)
        {
            return pReason switch
            {
                EraChangeReason.Accession => "accession",
                EraChangeReason.RestoredMandate => "restored_mandate",
                EraChangeReason.AutonomousRestoration => "autonomous_restoration",
                EraChangeReason.MajorVictory => "major_victory",
                EraChangeReason.CapitalRecovered => "capital_recovered",
                EraChangeReason.LegalCoreRecovered => "legal_core_recovered",
                EraChangeReason.EnteredRevival => "entered_revival",
                EraChangeReason.CentralReform => "central_reform",
                EraChangeReason.CapitalRelocated => "capital_relocated",
                EraChangeReason.GrandSacrificeBlessing => "grand_sacrifice_blessing",
                EraChangeReason.PlayerRequested => "player_requested",
                _ => "none"
            };
        }

        private static bool IsSupportedHereditaryEmperor(Kingdom pKingdom,
            Actor pEmperor)
        {
            if (pKingdom?.data == null || pEmperor?.data == null ||
                pKingdom.king != pEmperor || pKingdom.isRekt() ||
                !KingdomTitleService.IsEmperor(pKingdom) ||
                RepublicGovernmentService.IsRepublic(pKingdom) ||
                RepublicGovernmentService.IsRepublicLeader(pEmperor) ||
                !RepublicGovernmentService.HasEstablishedMonarchy(pKingdom)) return false;
            return LineageService.IsXiaKingdom(pKingdom) ||
                   XiaizationService.UsesXiaizedInstitutionSystem(pKingdom);
        }

        private static bool IsAtWar(Kingdom pKingdom)
        {
            try { return pKingdom.hasEnemies(); }
            catch { return true; }
        }

        private static int ReadReignIndex(long pReignId)
        {
            if (!Ready || pReignId < 0) return 1;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT REIGN_INDEX FROM " +
                                      KingdomReignTableItem.GetTableName() +
                                      " WHERE REIGN_ID=@reign LIMIT 1";
                command.Parameters.AddWithValue("@reign", pReignId);
                object value = command.ExecuteScalar();
                return value == null || value == DBNull.Value
                    ? 1
                    : Math.Max(1, Convert.ToInt32(value));
            }
            catch
            {
                return 1;
            }
        }

        private static string GetLocalEraName(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            pKingdom.data.get(LineageKeys.KINGDOM_YEAR_NAME, out string eraName, "");
            return EraNameRules.IsValidCustom(eraName) ? eraName : "";
        }

        private static int SafeAge(Actor pActor)
        {
            try { return pActor?.getAge() ?? -1; }
            catch { return -1; }
        }

        private static EraChangeResult Blocked(EraChangeBlockReason pReason)
        {
            return new EraChangeResult(false, -1, "", pReason);
        }
    }
}
