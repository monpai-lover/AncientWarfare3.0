using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.ui;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    internal sealed class DiplomacyConversationEvent
    {
        public long EventId;
        public long SpeakerKingdomId;
        public string SpeakerName = "";
        public string TargetName = "";
        public string EventType = "";
        public string Detail = "";
        public int EventYear;
        public double EventTime = -1d;
        public string YearPrefix = "";
        public string SpeakerTitle = "";
        public bool IsProposalResponse;
        public DiplomacyProposal Proposal;
    }

    internal static class DiplomacyConversationService
    {
        private const int RetainedEventsPerPair = 128;

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null &&
                                     LineageArchiveManager.Instance
                                         .InitializeSuccessful;

        public static IReadOnlyList<DiplomacyConversationEvent> ReadEvents(
            long pKingdomA, long pKingdomB, int pLimit = 80)
        {
            var result = new List<DiplomacyConversationEvent>();
            int eventLimit = DiplomacyConversationRules.ClampEventLimit(pLimit);
            if (!Ready || !DiplomacyConversationRules.TryNormalizePair(
                    pKingdomA, pKingdomB, out DiplomacyKingdomPair pair))
                return result;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT EVENT_ID,SPEAKER_KINGDOM_ID," +
                    "SPEAKER_NAME,TARGET_NAME,EVENT_TYPE,DETAIL,EVENT_YEAR," +
                    "EVENT_TIME,YEAR_PREFIX,SPEAKER_TITLE " +
                    "FROM " + DiplomacyDialogueTableItem.GetTableName() +
                    " WHERE KINGDOM_A_ID=@a AND KINGDOM_B_ID=@b " +
                    "ORDER BY EVENT_TIME DESC,EVENT_ID DESC LIMIT @limit";
                command.Parameters.AddWithValue("@a", pair.FirstKingdomId);
                command.Parameters.AddWithValue("@b", pair.SecondKingdomId);
                command.Parameters.AddWithValue("@limit", eventLimit);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                    result.Add(new DiplomacyConversationEvent
                    {
                        EventId = reader.GetInt64(0),
                        SpeakerKingdomId = reader.GetInt64(1),
                        SpeakerName = ReadString(reader, 2),
                        TargetName = ReadString(reader, 3),
                        EventType = ReadString(reader, 4),
                        Detail = ReadString(reader, 5),
                        EventYear = reader.GetInt32(6),
                        EventTime = reader.GetDouble(7),
                        YearPrefix = ReadString(reader, 8),
                        SpeakerTitle = ReadString(reader, 9)
                    });
                result.Reverse();
                IReadOnlyList<DiplomacyProposal> proposals =
                    DiplomacyProposalService.ReadPair(pKingdomA, pKingdomB,
                        64);
                var byId = new Dictionary<long, DiplomacyProposal>();
                for (int i = 0; i < proposals.Count; i++)
                    byId[proposals[i].ProposalId] = proposals[i];
                var expanded = new List<DiplomacyConversationEvent>(
                    Math.Min(eventLimit * 2, 160));
                for (int i = 0; i < result.Count; i++)
                {
                    if (result[i].EventType == "proposal" &&
                        long.TryParse(result[i].Detail, out long proposalId) &&
                        byId.TryGetValue(proposalId,
                            out DiplomacyProposal proposal))
                    {
                        result[i].Proposal = proposal;
                        expanded.Add(result[i]);
                        if (proposal.Status != DiplomacyProposalStatus.Pending &&
                            proposal.ResponseTime >= 0d &&
                            !DiplomacyConversationRules.IsAutomaticWarSettlementTruce(
                                proposal.Type, proposal.ResponseReason))
                            expanded.Add(CreateProposalResponseEvent(
                                result[i], proposal));
                        continue;
                    }
                    expanded.Add(result[i]);
                }
                expanded.Sort(CompareEvents);
                result = expanded;
                TrimExpandedEvents(result, eventLimit);
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Diplomacy dialogue read failed: " +
                                    exception.Message);
            }
            return result;
        }

        private static DiplomacyConversationEvent CreateProposalResponseEvent(
            DiplomacyConversationEvent pRequest,
            DiplomacyProposal pProposal)
        {
            return new DiplomacyConversationEvent
            {
                EventId = pRequest.EventId,
                SpeakerKingdomId = pProposal.ResponderKingdomId,
                SpeakerName = pProposal.ResponderName,
                TargetName = pProposal.RequesterName,
                EventType = "proposal_response",
                Detail = pProposal.ProposalId.ToString(),
                EventYear = pProposal.ResponseYear,
                EventTime = pProposal.ResponseTime,
                YearPrefix = pProposal.ResponseYearPrefix,
                SpeakerTitle = pProposal.ResponderTitle,
                IsProposalResponse = true,
                Proposal = pProposal
            };
        }

        private static int CompareEvents(DiplomacyConversationEvent pLeft,
            DiplomacyConversationEvent pRight)
        {
            int time = pLeft.EventTime.CompareTo(pRight.EventTime);
            if (time != 0) return time;
            int id = pLeft.EventId.CompareTo(pRight.EventId);
            if (id != 0) return id;
            return pLeft.IsProposalResponse.CompareTo(
                pRight.IsProposalResponse);
        }

        private static void TrimExpandedEvents(
            List<DiplomacyConversationEvent> pEvents, int pLimit)
        {
            int remove = pEvents.Count - Math.Max(1, pLimit);
            if (remove > 0) pEvents.RemoveRange(0, remove);
        }

        public static void RecordWarStarted(War pWar)
        {
            Kingdom attacker = pWar?.getMainAttacker();
            Kingdom defender = pWar?.getMainDefender();
            Record(attacker, defender, attacker, "war_declared",
                SafeWarType(pWar));
        }

        public static void RecordWarNotice(Kingdom pAttacker,
            Kingdom pDefender, int pEarliestYear, int pForcedYear)
        {
            Record(pAttacker, pDefender, pAttacker, "war_notice",
                pEarliestYear + ":" + pForcedYear);
        }

        public static void RecordWarEnded(War pWar, WarWinner pWinner)
        {
            Kingdom attacker = pWar?.getMainAttacker();
            Kingdom defender = pWar?.getMainDefender();
            Kingdom speaker = pWinner == WarWinner.Attackers ? attacker :
                pWinner == WarWinner.Defenders ? defender : null;
            string detail = pWinner == WarWinner.Attackers
                ? RealmDisplayName(attacker)
                : pWinner == WarWinner.Defenders
                    ? RealmDisplayName(defender)
                    : "peace";
            Record(attacker, defender, speaker, "war_ended", detail);
        }

        public static void RecordVassalSet(Kingdom pVassal,
            Kingdom pSuzerain, bool pTributary)
        {
            Record(pVassal, pSuzerain, pSuzerain,
                pTributary ? "tributary_set" : "vassal_set", "");
        }

        public static void RecordVassalEnded(Kingdom pVassal,
            Kingdom pSuzerain)
        {
            Record(pVassal, pSuzerain, null, "vassal_ended", "");
        }

        public static void RecordAllianceFormed(Kingdom pFounder,
            Kingdom pPartner, string pAllianceName)
        {
            Record(pFounder, pPartner, pFounder, "alliance_formed",
                pAllianceName);
        }

        public static void RecordAllianceJoined(Kingdom pJoining,
            Kingdom pMember, string pAllianceName)
        {
            Record(pJoining, pMember, pJoining, "alliance_joined",
                pAllianceName);
        }

        public static void RecordAllianceLeft(Kingdom pLeaving,
            Kingdom pMember, string pAllianceName)
        {
            Record(pLeaving, pMember, pLeaving, "alliance_left",
                pAllianceName);
        }

        public static void RecordNonAggressionBroken(Kingdom pBreaking,
            Kingdom pOther, int pTruceUntilYear)
        {
            Record(pBreaking, pOther, pBreaking,
                "non_aggression_broken", pTruceUntilYear.ToString());
        }

        public static void RecordProposal(Kingdom pRequester,
            Kingdom pResponder, long pProposalId)
        {
            Record(pRequester, pResponder, pRequester, "proposal",
                pProposalId.ToString());
        }

        public static void RecordCovertResult(Kingdom pSource,
            Kingdom pTarget, string pOperationType, string pResult,
            bool pDiscovered)
        {
            string detail = (pOperationType ?? "") + "|" +
                            (pResult ?? "");
            Record(pSource, pTarget, pDiscovered ? pTarget : pSource,
                pDiscovered ? "covert_discovered" : "covert_result",
                detail);
        }

        public static string BuildText(DiplomacyConversationEvent pEvent)
        {
            if (pEvent == null) return "";
            string speaker = string.IsNullOrEmpty(pEvent.SpeakerName)
                ? AW_L10n.Text("aw_diplomacy_system", "System")
                : pEvent.SpeakerName;
            string target = pEvent.TargetName ?? "";
            string detail = pEvent.Detail ?? "";
            if (pEvent.EventType == "proposal" && pEvent.Proposal != null)
            {
                if (DiplomacyConversationRules.IsAutomaticWarSettlementTruce(
                        pEvent.Proposal.Type,
                        pEvent.Proposal.ResponseReason))
                    return BuildAutomaticWarSettlementTruce(
                        pEvent.Proposal);
                return BuildProposalRequest(pEvent.Proposal);
            }
            if (pEvent.EventType == "proposal_response" &&
                pEvent.Proposal != null)
                return BuildProposalReply(pEvent.Proposal);
            return pEvent.EventType switch
            {
                "war_notice" => BuildWarNoticeText(speaker, target, detail),
                "war_declared" =>
                    DiplomacyConversationRules.FormatWarDeclaration(
                        speaker, target,
                        AW_L10n.Text("aw_diplomacy_war_declared_mid",
                            " declared war on "),
                        AW_L10n.Text("aw_diplomacy_war_declared_suffix", ""),
                        Detail(detail)),
                "war_ended" => AW_L10n.Text(
                    "aw_diplomacy_war_ended", "War ended") +
                    (detail == "peace" || string.IsNullOrEmpty(detail)
                        ? AW_L10n.Text("aw_diplomacy_white_peace_suffix",
                            ": white peace")
                        : AW_L10n.Text("aw_diplomacy_winner_mid",
                            ": victor ") + detail),
                "vassal_set" => target +
                    AW_L10n.Text("aw_diplomacy_vassal_set_mid",
                        " became a vassal of ") + speaker,
                "tributary_set" => target +
                    AW_L10n.Text("aw_diplomacy_tributary_set_mid",
                        " became a tributary of ") + speaker,
                "vassal_ended" => speaker + " / " + target +
                    AW_L10n.Text("aw_diplomacy_vassal_ended_suffix",
                        " ended their vassal relationship"),
                "alliance_formed" => speaker +
                    AW_L10n.Text("aw_diplomacy_alliance_formed_mid",
                        " formed an alliance with ") + target + Detail(detail),
                "alliance_joined" => speaker +
                    AW_L10n.Text("aw_diplomacy_alliance_joined_mid",
                        " joined the alliance of ") + target + Detail(detail),
                "alliance_left" => speaker +
                    AW_L10n.Text("aw_diplomacy_alliance_left_mid",
                        " left the alliance shared with ") + target + Detail(detail),
                "non_aggression_broken" => string.Format(AW_L10n.Text(
                        "aw_diplomacy_non_aggression_broken_text",
                        "{0} notified {1} that the non-aggression pact was " +
                        "terminated; a truce remains until year {2}."),
                    speaker, target, detail),
                "covert_result" => BuildCovertResultText(detail,
                    discovered: false),
                "covert_discovered" => BuildCovertResultText(detail,
                    discovered: true),
                _ => speaker + " / " + target
            };
        }

        private static string BuildCovertResultText(string pDetail,
            bool discovered)
        {
            string[] parts = (pDetail ?? "").Split('|');
            string operation = parts.Length > 0 ? parts[0] : "";
            string result = parts.Length > 1 ? parts[1] : "";
            string operationName = operation == "spy_network"
                ? AW_L10n.Text("aw_diplomacy_action_spy_network",
                    "Establish spy network")
                : AW_L10n.Text("aw_diplomacy_action_forge_documents",
                    "Forge documents");
            string resultName = AW_L10n.Text(
                "aw_diplomacy_covert_result_" + result,
                result.Replace('_', ' '));
            return string.Format(AW_L10n.Text(discovered
                        ? "aw_diplomacy_covert_discovered_text"
                        : "aw_diplomacy_covert_result_text",
                    discovered ? "Discovered: {0} ({1})" : "{0}: {1}"),
                operationName, resultName);
        }

        public static string Timestamp(DiplomacyConversationEvent pEvent)
        {
            if (pEvent == null) return "";
            string prefix = pEvent.IsProposalResponse ? pEvent.Proposal?.ResponseYearPrefix :
                pEvent.Proposal?.RequestYearPrefix;
            string sender = pEvent.IsProposalResponse
                ? pEvent.Proposal?.ResponderTitle
                : pEvent.Proposal?.RequesterTitle;
            if (string.IsNullOrEmpty(prefix)) prefix = pEvent.YearPrefix;
            if (string.IsNullOrEmpty(sender)) sender = pEvent.SpeakerTitle;
            if (string.IsNullOrEmpty(prefix))
                prefix = pEvent.EventTime >= 0
                    ? HistoryWriter.FormatDate(pEvent.EventTime)
                    : string.Format(AW_L10n.Text("aw_diplomacy_event_time",
                        "Year {0}"), pEvent.EventYear);
            return string.IsNullOrEmpty(sender)
                ? prefix
                : prefix + " · " + AW_L10n.Text(
                    "aw_diplomacy_sender_title", "Sender") + ": " + sender;
        }

        private static string BuildProposalRequest(DiplomacyProposal pProposal)
        {
            DiplomacyLetterStyle style = pProposal.RequestStyle;
            DiplomacyLetterTone tone = pProposal.RequestTone;
            string addressee = string.IsNullOrEmpty(pProposal.ResponderName)
                ? AW_L10n.Text("aw_diplomacy_unknown_realm", "the other realm")
                : pProposal.ResponderName;
            string openKey = style switch
            {
                DiplomacyLetterStyle.Imperial =>
                    "aw_diplomacy_letter_imperial_open",
                DiplomacyLetterStyle.Suzerain =>
                    "aw_diplomacy_letter_suzerain_open",
                DiplomacyLetterStyle.Subject =>
                    "aw_diplomacy_letter_subject_open",
                _ => "aw_diplomacy_letter_peer_open"
            };
            string openFallback = style switch
            {
                DiplomacyLetterStyle.Imperial =>
                    "The Emperor sends greetings to the ruler of {0}:",
                DiplomacyLetterStyle.Suzerain =>
                    "To our subject realm of {0}:",
                DiplomacyLetterStyle.Subject =>
                    "With respect, to the honored realm of {0}:",
                _ => "To the esteemed ruler of {0}:"
            };
            string bodyKey = style switch
            {
                DiplomacyLetterStyle.Imperial =>
                    "aw_diplomacy_letter_imperial_body",
                DiplomacyLetterStyle.Suzerain =>
                    "aw_diplomacy_letter_suzerain_body",
                DiplomacyLetterStyle.Subject =>
                    "aw_diplomacy_letter_subject_body",
                _ => "aw_diplomacy_letter_peer_body"
            };
            string bodyFallback = style switch
            {
                DiplomacyLetterStyle.Imperial =>
                    "By imperial instruction, we call upon your realm to consider: {0}.",
                DiplomacyLetterStyle.Suzerain =>
                    "As your suzerain, we instruct your court to consider: {0}.",
                DiplomacyLetterStyle.Subject =>
                    "Our realm respectfully petitions to conclude: {0}.",
                _ => "Our realm proposes that our two states conclude: {0}."
            };
            var text = new System.Text.StringBuilder();
            text.AppendLine(string.Format(AW_L10n.Text(openKey,
                openFallback), addressee));
            text.AppendLine(string.Format(AW_L10n.Text(bodyKey,
                bodyFallback), ProposalSubject(pProposal)));
            text.AppendLine(AW_L10n.Text(
                "aw_diplomacy_letter_tone_" + tone.ToString().ToLowerInvariant(),
                tone switch
                {
                    DiplomacyLetterTone.Cordial =>
                        "May trust and friendship between our realms endure.",
                    DiplomacyLetterTone.Cold =>
                        "This matter concerns both realms; weigh it carefully.",
                    DiplomacyLetterTone.Hostile =>
                        "This is our final notice; do not disregard it.",
                    _ => "We ask your court to consider this proposal."
                }));
            text.Append(AW_L10n.Text(
                "aw_diplomacy_letter_await_reply",
                "We await your considered reply."));
            return text.ToString();
        }

        private static string BuildProposalReply(DiplomacyProposal pProposal)
        {
            string responder = string.IsNullOrEmpty(pProposal.ResponderName)
                ? AW_L10n.Text("aw_diplomacy_unknown_realm", "The other realm")
                : pProposal.ResponderName;
            if (pProposal.Status != DiplomacyProposalStatus.Accepted &&
                pProposal.Status != DiplomacyProposalStatus.Rejected)
                return string.Format(AW_L10n.Text(
                        "aw_diplomacy_reply_status", "{0}: {1}"),
                    responder, ProposalStatusName(pProposal.Status));
            bool accepted = pProposal.Status == DiplomacyProposalStatus.Accepted;
            string key;
            string fallback;
            DiplomacyLetterStyle responseStyle = pProposal.ResponseStyle;
            if (pProposal.Type == DiplomacyProposalType.HouseholdOffering)
            {
                bool request = RulerHouseholdRules.IsConsortRequestDetail(
                    pProposal.DetailId);
                bool principal = pProposal.DetailId == "principal_wife";
                key = request
                    ? accepted
                        ? "aw_household_reply_request_accept"
                        : "aw_household_reply_request_reject"
                    : principal
                        ? accepted
                            ? "aw_household_reply_principal_accept"
                            : "aw_household_reply_principal_reject"
                        : accepted
                            ? "aw_household_reply_consort_accept"
                            : "aw_household_reply_consort_reject";
                fallback = request
                    ? accepted
                        ? "{0} replies: We shall provide a consort."
                        : "{0} replies: We cannot provide a consort."
                    : accepted
                        ? principal
                            ? "{0} replies: We accept her as principal wife."
                            : "{0} replies: We accept her as consort."
                        : principal
                            ? "{0} replies: We decline this principal-wife offer."
                            : "{0} replies: We decline this consort offer.";
            }
            else if (responseStyle == DiplomacyLetterStyle.Imperial)
            {
                key = accepted ? "aw_diplomacy_reply_from_imperial_accept" :
                    "aw_diplomacy_reply_from_imperial_reject";
                fallback = accepted
                    ? "{0} decrees: The proposal is accepted."
                    : "{0} decrees: The proposal is rejected.";
            }
            else if (responseStyle == DiplomacyLetterStyle.Suzerain)
            {
                key = accepted ? "aw_diplomacy_reply_suzerain_accept" :
                    "aw_diplomacy_reply_suzerain_reject";
                fallback = accepted
                    ? "{0} replies: We grant this request."
                    : "{0} replies: This request is denied.";
            }
            else if (responseStyle == DiplomacyLetterStyle.Subject)
            {
                key = accepted ? "aw_diplomacy_reply_subject_accept" :
                    "aw_diplomacy_reply_subject_reject";
                fallback = accepted
                    ? "{0} replies with respect: We shall comply."
                    : "{0} replies with respect: We are unable to comply.";
            }
            else if (pProposal.RequestStyle == DiplomacyLetterStyle.Imperial)
            {
                key = accepted ? "aw_diplomacy_reply_imperial_accept" :
                    "aw_diplomacy_reply_imperial_reject";
                fallback = accepted
                    ? "{0} replies: We receive the imperial instruction and shall comply."
                    : "{0} replies: We cannot obey this instruction.";
            }
            else
            {
                key = accepted ? "aw_diplomacy_reply_accept" :
                    "aw_diplomacy_reply_reject";
                fallback = accepted
                    ? "{0} replies: We accept your proposal."
                    : "{0} replies: We must decline your proposal.";
            }
            string reply = string.Format(AW_L10n.Text(key, fallback),
                responder);
            string tone = AW_L10n.Text(
                "aw_diplomacy_reply_tone_" +
                DiplomacyConversationRules.LetterToneId(
                    pProposal.ResponseTone),
                pProposal.ResponseTone switch
                {
                    DiplomacyLetterTone.Cordial =>
                        "May amity between our realms endure.",
                    DiplomacyLetterTone.Cold =>
                        "Let this answer settle the matter.",
                    DiplomacyLetterTone.Hostile =>
                        "Do not press this matter further.",
                    _ => "This is our court's considered answer."
                });
            reply += "\n" + tone;
            return ShouldAppendProposalSubject(pProposal)
                ? reply + "\n" + ProposalSubject(pProposal)
                : reply;
        }

        private static string BuildWarNoticeText(string pSpeaker,
            string pTarget, string pDetail)
        {
            string[] years = (pDetail ?? "").Split(':');
            string earliest = years.Length > 0 ? years[0] : "?";
            string forced = years.Length > 1 ? years[1] : earliest;
            return pSpeaker + AW_L10n.Text(
                       "aw_diplomacy_war_notice_mid",
                       " delivered a declaration of war to ") + pTarget +
                   AW_L10n.Text("aw_diplomacy_war_notice_years_mid",
                       "; hostilities will begin between years ") +
                   earliest + " - " + forced;
        }

        public static string ProposalTypeName(DiplomacyProposalType pType)
        {
            return pType switch
            {
                DiplomacyProposalType.Alliance => AW_L10n.Text(
                    "aw_diplomacy_action_alliance", "Form alliance"),
                DiplomacyProposalType.Peace => AW_L10n.Text(
                    "aw_diplomacy_action_peace", "White peace"),
                DiplomacyProposalType.Surrender => AW_L10n.Text(
                    "aw_diplomacy_action_surrender", "Surrender"),
                DiplomacyProposalType.EnforceDemands => AW_L10n.Text(
                    "aw_diplomacy_action_enforce_demands",
                    "Enforce demands"),
                DiplomacyProposalType.NonAggression => AW_L10n.Text(
                    "aw_diplomacy_action_non_aggression", "Non-aggression pact"),
                DiplomacyProposalType.JoinWar => AW_L10n.Text(
                    "aw_diplomacy_action_join_war", "Request war support"),
                DiplomacyProposalType.Vassalize => AW_L10n.Text(
                    "aw_diplomacy_action_vassalize", "Demand vassalage"),
                DiplomacyProposalType.Tributary => AW_L10n.Text(
                    "aw_diplomacy_action_tributary", "Demand tribute"),
                DiplomacyProposalType.EndAlliance => AW_L10n.Text(
                    "aw_diplomacy_action_end_alliance", "End alliance"),
                DiplomacyProposalType.EndVassal => AW_L10n.Text(
                    "aw_diplomacy_action_end_vassal", "End vassalage"),
                DiplomacyProposalType.Truce => AW_L10n.Text(
                    "aw_diplomacy_action_truce", "Truce"),
                DiplomacyProposalType.BreakNonAggression => AW_L10n.Text(
                    "aw_diplomacy_action_break_non_aggression",
                    "Break non-aggression pact"),
                DiplomacyProposalType.Coalition => AW_L10n.Text(
                    "aw_diplomacy_action_coalition", "Coalition against a threat"),
                DiplomacyProposalType.RoyalMarriage => AW_L10n.Text(
                    "aw_diplomacy_action_royal_marriage", "Royal marriage"),
                DiplomacyProposalType.HouseholdOffering => AW_L10n.Text(
                    "aw_diplomacy_action_household_offering",
                    "Offer ruler household member"),
                _ => AW_L10n.Text("aw_diplomacy_action_unknown", "Diplomatic request")
            };
        }

        private static string ProposalSubject(DiplomacyProposal pProposal)
        {
            string typeName = ProposalTypeName(pProposal.Type);
            string directional = DirectionalProposalSubject(pProposal);
            if (!string.IsNullOrEmpty(directional)) return directional;
            if (pProposal.Type == DiplomacyProposalType.Coalition)
            {
                Kingdom target = FindKingdom(pProposal.TargetKingdomId);
                string targetName = target?.data == null
                    ? AW_L10n.Text("aw_diplomacy_unknown_realm",
                        "the other realm")
                    : RealmDisplayName(target);
                return string.Format(AW_L10n.Text(
                        "aw_diplomacy_coalition_target", "{0}: {1}"),
                    typeName, targetName);
            }
            if (DiplomacyProposalRules.IsPeaceProposal(pProposal.Type))
                return typeName + PeaceTermsSummary(pProposal.DetailId);
            if (pProposal.Type == DiplomacyProposalType.HouseholdOffering)
            {
                string offered = LineageQuery.GetActorDisplayName(
                    pProposal.RequesterActorId);
                string ruler = LineageQuery.GetActorDisplayName(
                    pProposal.ResponderActorId);
                string unknownPerson = AW_L10n.Text(
                    "aw_diplomacy_household_unknown_person",
                    "court member");
                if (string.IsNullOrEmpty(offered)) offered = unknownPerson;
                if (string.IsNullOrEmpty(ruler)) ruler = unknownPerson;
                if (RulerHouseholdRules.IsConsortRequestDetail(
                        pProposal.DetailId))
                {
                    string requestName = AW_L10n.Text(
                        "aw_diplomacy_action_consort_request",
                        "Request a consort");
                    return string.Format(AW_L10n.Text(
                            "aw_diplomacy_consort_request_subject",
                            "{0}: choose a noblewoman to serve {1} as consort"),
                        requestName, ruler);
                }
                string rank = pProposal.DetailId == "principal_wife"
                    ? AW_L10n.Text("aw_household_kind_principal_wife",
                        "principal wife")
                    : AW_L10n.Text("aw_household_kind_consort", "consort");
                return string.Format(AW_L10n.Text(
                        "aw_diplomacy_household_offer_subject",
                        "{0}: offer {1} to {2} as {3}"),
                    typeName, offered, ruler, rank);
            }
            if (pProposal.Type != DiplomacyProposalType.RoyalMarriage)
                return typeName;
            string requester = LineageQuery.GetActorDisplayName(pProposal.RequesterActorId);
            string responder = LineageQuery.GetActorDisplayName(pProposal.ResponderActorId);
            string unknown = AW_L10n.Text(
                "aw_diplomacy_marriage_unknown_candidate", "royal member");
            if (string.IsNullOrEmpty(requester)) requester = unknown;
            if (string.IsNullOrEmpty(responder)) responder = unknown;
            return string.Format(AW_L10n.Text(
                    "aw_diplomacy_royal_marriage_pair", "{0}: {1} and {2}"),
                typeName, requester, responder);
        }

        private static bool ShouldAppendProposalSubject(
            DiplomacyProposal pProposal)
        {
            return pProposal?.Type == DiplomacyProposalType.RoyalMarriage ||
                   pProposal?.Type ==
                   DiplomacyProposalType.HouseholdOffering ||
                   pProposal?.Type == DiplomacyProposalType.Vassalize ||
                   pProposal?.Type == DiplomacyProposalType.EndVassal;
        }

        private static string DirectionalProposalSubject(
            DiplomacyProposal pProposal)
        {
            if (pProposal == null) return "";
            string key = "";
            string fallback = "";
            if (pProposal.Type == DiplomacyProposalType.Vassalize)
            {
                if (pProposal.DetailId == DiplomacyProposalOpportunityRules
                        .VassalizeDemandDetail)
                {
                    key = "aw_diplomacy_detail_vassalize_demand";
                    fallback = "Demand that the other realm become our vassal";
                }
                else if (pProposal.DetailId ==
                         DiplomacyProposalOpportunityRules
                             .VassalizeSeekDetail)
                {
                    key = "aw_diplomacy_detail_vassalize_seek";
                    fallback = "Seek protection as the other realm's vassal";
                }
                else if (pProposal.DetailId ==
                         DiplomacyProposalOpportunityRules
                             .VassalizeInternalizeDetail)
                {
                    key = "aw_diplomacy_detail_vassalize_internalize";
                    fallback = "Enter the suzerain's formal subject system";
                }
            }
            else if (pProposal.Type == DiplomacyProposalType.EndVassal)
            {
                if (pProposal.DetailId == DiplomacyProposalOpportunityRules
                        .EndVassalReleaseDetail)
                {
                    key = "aw_diplomacy_detail_end_vassal_release";
                    fallback = "Release the subject from vassalage";
                }
                else if (pProposal.DetailId ==
                         DiplomacyProposalOpportunityRules
                             .EndVassalRequestDetail)
                {
                    key = "aw_diplomacy_detail_end_vassal_request";
                    fallback = "Request release from the suzerain";
                }
            }
            return string.IsNullOrEmpty(key)
                ? ""
                : AW_L10n.Text(key, fallback);
        }

        private static string BuildAutomaticWarSettlementTruce(
            DiplomacyProposal pProposal)
        {
            string requester = string.IsNullOrEmpty(pProposal.RequesterName)
                ? AW_L10n.Text("aw_diplomacy_unknown_realm",
                    "the other realm")
                : pProposal.RequesterName;
            string responder = string.IsNullOrEmpty(pProposal.ResponderName)
                ? AW_L10n.Text("aw_diplomacy_unknown_realm",
                    "the other realm")
                : pProposal.ResponderName;
            int duration = Math.Max(0,
                pProposal.TreatyUntilYear - pProposal.CreatedYear);
            string summary = string.Format(AW_L10n.Text(
                    "aw_diplomacy_truce_settlement_summary",
                    "{0} and {1} ended the war and concluded a {2}-year " +
                    "truce through year {3}"),
                requester, responder, duration,
                pProposal.TreatyUntilYear);
            IReadOnlyList<WarPeaceSettlementTerm> terms =
                WarPeaceSettlementService.Instance.ReadExecutedTerms(
                    pProposal.WarId);
            if (terms.Count == 0)
                return summary + "\n" + AW_L10n.Text(
                    "aw_diplomacy_truce_settlement_no_extra_terms",
                    "No additional territorial or material terms were " +
                    "included in this settlement.");
            return summary + "\n" + AW_L10n.Text(
                       "aw_diplomacy_truce_settlement_terms",
                       "Executed settlement terms:") +
                   PeaceTermsSummary(terms);
        }

        private static string PeaceTermsSummary(string pDetailId)
        {
            IReadOnlyList<WarPeaceSettlementTerm> terms =
                WarPeaceSettlementService.Instance.ReadTerms(pDetailId);
            return PeaceTermsSummary(terms);
        }

        private static string PeaceTermsSummary(
            IReadOnlyList<WarPeaceSettlementTerm> pTerms)
        {
            IReadOnlyList<WarPeaceSettlementTerm> terms = pTerms ??
                Array.Empty<WarPeaceSettlementTerm>();
            if (terms.Count == 0) return "";
            var lines = new List<string>(Math.Min(16, terms.Count));
            for (int i = 0; i < terms.Count && i < 16; i++)
            {
                WarPeaceSettlementTerm term = terms[i];
                string label = AW_L10n.Text("aw_war_peace_term_" +
                    term.Kind.ToString().ToLowerInvariant(),
                    term.Kind.ToString());
                string detail = "";
                if (term.CityId >= 0)
                {
                    City city = FindCity(term.CityId);
                    detail = city?.data?.name ?? term.CityId.ToString();
                }
                else if (term.Amount > 0)
                    detail = term.Amount + " " +
                             WarPeaceResourceNameService.Resolve(
                                 term.ResourceId);
                lines.Add("- " + label +
                          (string.IsNullOrEmpty(detail)
                              ? ""
                              : ": " + detail));
            }
            return "\n" + string.Join("\n", lines.ToArray());
        }

        public static string ProposalStatusName(DiplomacyProposalStatus pStatus)
        {
            return pStatus switch
            {
                DiplomacyProposalStatus.Accepted => AW_L10n.Text(
                    "aw_diplomacy_status_accepted", "Accepted"),
                DiplomacyProposalStatus.Rejected => AW_L10n.Text(
                    "aw_diplomacy_status_rejected", "Rejected"),
                DiplomacyProposalStatus.Expired => AW_L10n.Text(
                    "aw_diplomacy_status_expired", "Expired"),
                DiplomacyProposalStatus.Cancelled => AW_L10n.Text(
                    "aw_diplomacy_status_cancelled", "Cancelled"),
                DiplomacyProposalStatus.Processing => AW_L10n.Text(
                    "aw_diplomacy_status_processing", "Processing response"),
                _ => AW_L10n.Text("aw_diplomacy_status_pending", "Awaiting response")
            };
        }

        private static void Record(Kingdom pKingdomA, Kingdom pKingdomB,
            Kingdom pSpeaker, string pEventType, string pDetail)
        {
            if (!Ready || pKingdomA?.data == null ||
                pKingdomB?.data == null ||
                !DiplomacyConversationRules.TryNormalizePair(
                    pKingdomA.id, pKingdomB.id,
                    out DiplomacyKingdomPair pair)) return;
            try
            {
                double eventTime = LineageService.CurTime();
                string yearPrefix = HistoryWriter.BuildYearPrefix(eventTime,
                    pSpeaker ?? pKingdomA);
                string speakerTitle = DiplomaticSenderTitle(
                    pSpeaker ?? pKingdomA);
                long eventId = TableIdAllocator.Next(DB,
                    DiplomacyDialogueTableItem.GetTableName(), "EVENT_ID");
                DB.Insert(DiplomacyDialogueTableItem.GetTableName(),
                    ColumnVal.Create("EVENT_ID", eventId),
                    ColumnVal.Create("KINGDOM_A_ID", pair.FirstKingdomId),
                    ColumnVal.Create("KINGDOM_B_ID", pair.SecondKingdomId),
                    ColumnVal.Create("SPEAKER_KINGDOM_ID",
                        pSpeaker?.id ?? -1L),
                    ColumnVal.Create("SPEAKER_NAME",
                        RealmDisplayName(pSpeaker ?? pKingdomA)),
                    ColumnVal.Create("TARGET_NAME",
                        pSpeaker == pKingdomA
                            ? RealmDisplayName(pKingdomB)
                            : pSpeaker == pKingdomB
                                ? RealmDisplayName(pKingdomA)
                                : RealmDisplayName(pKingdomB)),
                    ColumnVal.Create("EVENT_TYPE", pEventType ?? ""),
                    ColumnVal.Create("DETAIL", pDetail ?? ""),
                    ColumnVal.Create("EVENT_YEAR", SafeYear()),
                    ColumnVal.Create("EVENT_TIME", eventTime),
                    ColumnVal.Create("YEAR_PREFIX", yearPrefix),
                    ColumnVal.Create("SPEAKER_TITLE", speakerTitle));
                TrimPair(pair);
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Diplomacy dialogue write failed: " +
                                    exception.Message);
            }
        }

        private static string RealmDisplayName(Kingdom pKingdom)
        {
            return pKingdom?.data == null
                ? ""
                : SuccessionDisputeService.GetDisplayName(pKingdom);
        }

        private static void TrimPair(DiplomacyKingdomPair pPair)
        {
            using var command = new SQLiteCommand(DB);
            command.CommandText = "DELETE FROM " +
                DiplomacyDialogueTableItem.GetTableName() +
                " WHERE KINGDOM_A_ID=@a AND KINGDOM_B_ID=@b AND EVENT_ID " +
                "NOT IN (SELECT EVENT_ID FROM " +
                DiplomacyDialogueTableItem.GetTableName() +
                " WHERE KINGDOM_A_ID=@a AND KINGDOM_B_ID=@b " +
                "ORDER BY EVENT_TIME DESC,EVENT_ID DESC LIMIT " +
                RetainedEventsPerPair + ")";
            command.Parameters.AddWithValue("@a", pPair.FirstKingdomId);
            command.Parameters.AddWithValue("@b", pPair.SecondKingdomId);
            command.ExecuteNonQuery();
        }

        private static string Detail(string pValue)
        {
            return string.IsNullOrEmpty(pValue)
                ? ""
                : " (" + pValue + ")";
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static string DiplomaticSenderTitle(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            string actorName = pKingdom.king?.getName() ?? "";
            string title = RulerAppellationService.GetFullLivingAppellation(
                pKingdom);
            if (string.IsNullOrEmpty(title))
                title = KingdomTitleService.GetTitleChar(
                    KingdomTitleService.GetTitle(pKingdom));
            if (string.IsNullOrEmpty(actorName)) return title ?? "";
            return string.IsNullOrEmpty(title) || actorName.Contains(title)
                ? actorName
                : actorName + " · " + title;
        }

        private static string SafeWarType(War pWar)
        {
            try { return pWar?.name ?? pWar?.getAsset()?.id ?? ""; }
            catch { return ""; }
        }

        private static int SafeYear()
        {
            try { return Date.getCurrentYear(); }
            catch { return 0; }
        }

        private static string ReadString(SQLiteDataReader pReader,
            int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? "" : pReader.GetString(pIndex);
        }
    }
}
