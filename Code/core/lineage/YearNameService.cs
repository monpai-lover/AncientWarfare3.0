using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal static class YearNameService
    {
        public const int VoluntaryChangeCost = 30;

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
            string history = BuildHistory(pKind, pReason, pEmperor,
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
                HistoryContent = history,
                HistoryContentRich = HistoryColors.EscapeRich(history),
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

        private static string BuildHistory(EraChangeKind pKind,
            EraChangeReason pReason, Actor pEmperor, string pStateName,
            string pOldEra, string pNewEra)
        {
            string imperialState = ImperialStateName(pStateName);
            if (pKind == EraChangeKind.Accession)
                return (pEmperor?.getName() ?? "") + "践祚，建元" + pNewEra +
                       "，称" + imperialState + pNewEra + "皇帝。";
            string oldTitle = imperialState + (pOldEra ?? "") + "皇帝";
            return oldTitle + "以" + ReasonLabel(pReason) + "，改元" + pNewEra + "。";
        }

        private static string ImperialStateName(string pStateName)
        {
            string state = pStateName ?? "";
            return state.StartsWith("大", StringComparison.Ordinal) ? state : "大" + state;
        }

        private static string ReasonLabel(EraChangeReason pReason)
        {
            return pReason switch
            {
                EraChangeReason.RestoredMandate => "重受天命",
                EraChangeReason.AutonomousRestoration => "自主复国",
                EraChangeReason.MajorVictory => "大胜",
                EraChangeReason.CapitalRecovered => "收复国都",
                EraChangeReason.LegalCoreRecovered => "恢复法理故土",
                EraChangeReason.EnteredRevival => "中兴",
                EraChangeReason.CentralReform => "整饬中枢",
                EraChangeReason.CapitalRelocated => "迁都",
                EraChangeReason.GrandSacrificeBlessing => "大祭获吉",
                EraChangeReason.PlayerRequested => "诏令",
                _ => "即位"
            };
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
