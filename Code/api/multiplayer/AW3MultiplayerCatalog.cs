using System;
using System.Collections.Generic;

namespace AncientWarfare3.api.multiplayer
{
    public static class AW3MultiplayerCatalog
    {
        public const int PayloadSchemaVersion = 2;

        private static readonly IReadOnlyList<AW3WindowDescriptor>
            WindowDescriptors = Array.AsReadOnly(new[]
            {
                Window(AW3WindowKind.LineageOverview,
                    "aw_lineage_overview", "ui/icons/iconClan",
                    AW3WindowCategory.Records),
                Window(AW3WindowKind.ShiBranchList, "aw_shi_list",
                    "ui/icons/iconClanList", AW3WindowCategory.Records,
                    AW3WindowContextRequirement.AnySubject),
                Window(AW3WindowKind.FamilyTree, "aw_family_tree",
                    "ui/icons/iconFamily", AW3WindowCategory.Records,
                    AW3WindowContextRequirement.AnySubject),
                Window(AW3WindowKind.History, "aw_history",
                    "ui/Icons/iconWorldLog", AW3WindowCategory.Records,
                    AW3WindowContextRequirement.AnySubject),
                Window(AW3WindowKind.KingdomRoster, "aw_kingdom_roster",
                    "ui/icons/iconKingdomList", AW3WindowCategory.Records),
                Window(AW3WindowKind.PolicyTree, "aw_policy_tree",
                    "ui/icons/iconKnowledge", AW3WindowCategory.Domestic,
                    AW3WindowContextRequirement.Country),
                Window(AW3WindowKind.AncestryAnalysis,
                    "aw_ancestry_analysis", "ui/icons/iconFamily",
                    AW3WindowCategory.Records,
                    AW3WindowContextRequirement.Actor),
                Window(AW3WindowKind.MandateDynasty,
                    "aw_mandate_dynasty", "ui/Icons/traits/iconTianming",
                    AW3WindowCategory.Mandate),
                Window(AW3WindowKind.MandateCycle, "aw_mandate_cycle",
                    "ui/Icons/traits/iconTianming",
                    AW3WindowCategory.Mandate),
                Window(AW3WindowKind.MandateDecisions,
                    "aw_mandate_decisions", "ui/icons/iconPlotsList",
                    AW3WindowCategory.Mandate,
                    AW3WindowContextRequirement.Country),
                Window(AW3WindowKind.VassalRelations,
                    "aw_vassal_relations", "ui/wars/war_vassal",
                    AW3WindowCategory.Realm,
                    AW3WindowContextRequirement.Country),
                Window(AW3WindowKind.WarTargets, "aw_war_targets",
                    "ui/icons/iconWarList",
                    AW3WindowCategory.DiplomacyAndWar,
                    AW3WindowContextRequirement.Country),
                Window(AW3WindowKind.Court, "aw_court",
                    "ui/icons/iconDiplomacy", AW3WindowCategory.Domestic,
                    AW3WindowContextRequirement.Country),
                Window(AW3WindowKind.CourtAppointment,
                    "aw_court_appointment", "ui/icons/iconKings",
                    AW3WindowCategory.Domestic,
                    AW3WindowContextRequirement.Country |
                    AW3WindowContextRequirement.Office),
                Window(AW3WindowKind.CourtDisposition,
                    "aw_court_disposition", "ui/icons/iconDiplomacy",
                    AW3WindowCategory.Domestic,
                    AW3WindowContextRequirement.Country |
                    AW3WindowContextRequirement.TargetActor),
                Window(AW3WindowKind.CourtAuxiliaryLaws,
                    "aw_court_auxiliary_laws", "ui/icons/iconKnowledge",
                    AW3WindowCategory.Domestic,
                    AW3WindowContextRequirement.Country),
                Window(AW3WindowKind.InheritanceLaws,
                    "aw_inheritance_laws", "ui/Icons/iconKings",
                    AW3WindowCategory.Domestic,
                    AW3WindowContextRequirement.Country),
                Window(AW3WindowKind.School, "aw_school_browser",
                    "ui/Icons/traits/iconRujia",
                    AW3WindowCategory.Records),
                Window(AW3WindowKind.SchoolRoster, "aw_school_roster",
                    "ui/icons/iconClan", AW3WindowCategory.Records),
                Window(AW3WindowKind.NameDecision, "aw_name_decision",
                    "ui/icons/iconKnowledge", AW3WindowCategory.Mandate,
                    AW3WindowContextRequirement.Country),
                Window(AW3WindowKind.ConferredPosthumous,
                    "aw_conferred_posthumous", "ui/Icons/iconKings",
                    AW3WindowCategory.Records,
                    AW3WindowContextRequirement.Country |
                    AW3WindowContextRequirement.Actor),
                Window(AW3WindowKind.CentralPower, "aw_central_power",
                    "ui/icons/iconKingdomList", AW3WindowCategory.Realm,
                    AW3WindowContextRequirement.Country),
                Window(AW3WindowKind.Feudatories, "aw_feudatories",
                    "ui/wars/war_vassal", AW3WindowCategory.Realm,
                    AW3WindowContextRequirement.Country),
                Window(AW3WindowKind.DiplomacyConversations,
                    "aw_diplomacy_conversations",
                    "ui/icons/iconDiplomacy",
                    AW3WindowCategory.DiplomacyAndWar,
                    AW3WindowContextRequirement.Country),
                Window(AW3WindowKind.DiplomaticWarDeclaration,
                    "aw_diplomatic_war_declaration",
                    "ui/icons/iconWar",
                    AW3WindowCategory.DiplomacyAndWar,
                    AW3WindowContextRequirement.Country |
                    AW3WindowContextRequirement.TargetCountry),
                Window(AW3WindowKind.DiplomaticMarriage,
                    "aw_diplomatic_marriage", "ui/icons/iconFamily",
                    AW3WindowCategory.DiplomacyAndWar,
                    AW3WindowContextRequirement.Country |
                    AW3WindowContextRequirement.TargetCountry),
                Window(AW3WindowKind.CivilServiceExam,
                    "aw_civil_service_exam", "ui/icons/iconKnowledge",
                    AW3WindowCategory.Domestic,
                    AW3WindowContextRequirement.Country),
                Window(AW3WindowKind.RulerHousehold,
                    "aw_ruler_household", "ui/icons/iconFamily",
                    AW3WindowCategory.Domestic,
                    AW3WindowContextRequirement.Country),
                Window(AW3WindowKind.HouseholdOffer,
                    "aw_ruler_household_offer", "ui/icons/iconFamily",
                    AW3WindowCategory.DiplomacyAndWar,
                    AW3WindowContextRequirement.Country |
                    AW3WindowContextRequirement.TargetCountry),
                Window(AW3WindowKind.Supporters, "aw_supporters",
                    "ui/icons/iconKnowledge", AW3WindowCategory.Records),
                Window(AW3WindowKind.VirtualTitles, "aw_virtual_titles",
                    "ui/icons/iconKings", AW3WindowCategory.Domestic,
                    AW3WindowContextRequirement.Country),
                Window(AW3WindowKind.MilitaryGovernorate,
                    "aw_military_governorate_window",
                    "ui/wars/war_vassal", AW3WindowCategory.Realm,
                    AW3WindowContextRequirement.City),
                Window(AW3WindowKind.CustomCourtWorkflow,
                    "aw_custom_court_workflow", "ui/icons/iconDiplomacy",
                    AW3WindowCategory.Domestic,
                    AW3WindowContextRequirement.Country),
                Window(AW3WindowKind.BanditAmnestySettlement,
                    "aw_bandit_amnesty_settlement",
                    "ui/icons/iconDiplomacy",
                    AW3WindowCategory.Domestic,
                    AW3WindowContextRequirement.Country |
                    AW3WindowContextRequirement.TargetCountry),
                Window(AW3WindowKind.CourtStatistics,
                    "aw_court_statistics", "ui/icons/iconKingdomList",
                    AW3WindowCategory.Domestic,
                    AW3WindowContextRequirement.Country),
                Window(AW3WindowKind.DeJureRegionMerge,
                    "aw_de_jure_region_merge", "ui/icons/iconDiplomacy",
                    AW3WindowCategory.Domestic,
                    AW3WindowContextRequirement.Country)
            });

        private static readonly IReadOnlyList<AW3CommandDescriptor>
            CommandDescriptors = Array.AsReadOnly(new[]
            {
                Command(AW3CommandKind.ConfigurePolicy,
                    AW3WindowCategory.Domestic, Country()),
                Command(AW3CommandKind.SetPolicyClass,
                    AW3WindowCategory.Domestic, Country()),
                Command(AW3CommandKind.StartPolicyNode,
                    AW3WindowCategory.Domestic, Country()),
                Command(AW3CommandKind.TogglePolicyNodeLock,
                    AW3WindowCategory.Domestic, Country()),
                Command(AW3CommandKind.StartCoreFabrication,
                    AW3WindowCategory.Domestic, Country() |
                    AW3WindowContextRequirement.City),
                Command(AW3CommandKind.StartTargetedDecision,
                    AW3WindowCategory.Realm, Country() |
                    AW3WindowContextRequirement.TargetCountry),
                Command(AW3CommandKind.StartMandateDecision,
                    AW3WindowCategory.Mandate, Country()),
                Command(AW3CommandKind.MergeDeJureRegions,
                    AW3WindowCategory.Domestic, Country()),
                Command(AW3CommandKind.RenameCounty,
                    AW3WindowCategory.Domestic, Country()),
                Command(AW3CommandKind.AppointCourtOfficer,
                    AW3WindowCategory.Domestic, Country() |
                    AW3WindowContextRequirement.Actor |
                    AW3WindowContextRequirement.Office),
                Command(AW3CommandKind.FillCentralCourtVacancies,
                    AW3WindowCategory.Domestic, Country()),
                Command(AW3CommandKind.SetCourtDisposition,
                    AW3WindowCategory.Domestic, Country() |
                    AW3WindowContextRequirement.Actor),
                Command(AW3CommandKind.ChangeCourtAuxiliaryLaw,
                    AW3WindowCategory.Domestic, Country()),
                Command(AW3CommandKind.ChangeInheritanceLaw,
                    AW3WindowCategory.Domestic, Country()),
                Command(AW3CommandKind.RelocateFeudatory,
                    AW3WindowCategory.Realm, Country()),
                Command(AW3CommandKind.ReclaimFeudatoryCity,
                    AW3WindowCategory.Realm, Country() |
                    AW3WindowContextRequirement.City),
                Command(AW3CommandKind.AbolishFeudatory,
                    AW3WindowCategory.Realm, Country()),
                Command(AW3CommandKind.CreateDiplomacyProposal,
                    AW3WindowCategory.DiplomacyAndWar, Country() |
                    AW3WindowContextRequirement.TargetCountry),
                Command(AW3CommandKind.RespondDiplomacyProposal,
                    AW3WindowCategory.DiplomacyAndWar, Country() |
                    AW3WindowContextRequirement.TargetCountry),
                Command(AW3CommandKind.StartSpyNetwork,
                    AW3WindowCategory.DiplomacyAndWar, Country() |
                    AW3WindowContextRequirement.TargetCountry),
                Command(AW3CommandKind.StartForgeDocuments,
                    AW3WindowCategory.DiplomacyAndWar, Country() |
                    AW3WindowContextRequirement.TargetCountry),
                Command(AW3CommandKind.DeclareWar,
                    AW3WindowCategory.DiplomacyAndWar, Country() |
                    AW3WindowContextRequirement.TargetCountry),
                Command(AW3CommandKind.ConferPosthumousTitle,
                    AW3WindowCategory.Records, Country() |
                    AW3WindowContextRequirement.Actor),
                Command(AW3CommandKind.RenameClan,
                    AW3WindowCategory.Records, Country() |
                    AW3WindowContextRequirement.Shi),
                Command(AW3CommandKind.RenameSurname,
                    AW3WindowCategory.Records, Country() |
                    AW3WindowContextRequirement.Actor),
                Command(AW3CommandKind.ChangeEra,
                    AW3WindowCategory.Mandate, Country()),
                Command(AW3CommandKind.SetArmyRallyPoint,
                    AW3WindowCategory.DiplomacyAndWar, Country() |
                    AW3WindowContextRequirement.City),
                Command(AW3CommandKind.SetArmyTargetCity,
                    AW3WindowCategory.DiplomacyAndWar, Country() |
                    AW3WindowContextRequirement.City),
                Command(AW3CommandKind.SetArmyPosture,
                    AW3WindowCategory.DiplomacyAndWar, Country()),
                Command(AW3CommandKind.CancelArmyOrder,
                    AW3WindowCategory.DiplomacyAndWar, Country()),
                Command(AW3CommandKind.SubmitCivilServiceRanking,
                    AW3WindowCategory.Domestic, Country()),
                Command(AW3CommandKind.GrantVirtualNobleTitle,
                    AW3WindowCategory.Domestic, Country() |
                    AW3WindowContextRequirement.Actor),
                Command(AW3CommandKind.EditVirtualNobleTitle,
                    AW3WindowCategory.Records, Country()),
                Command(AW3CommandKind.DeleteVirtualNobleTitle,
                    AW3WindowCategory.Records, Country()),
                Command(AW3CommandKind.CreateMilitaryGovernorate,
                    AW3WindowCategory.Realm, Country() |
                    AW3WindowContextRequirement.City |
                    AW3WindowContextRequirement.Actor),
                Command(AW3CommandKind.DesignateMilitaryGovernorateSuccessor,
                    AW3WindowCategory.Realm, Country() |
                    AW3WindowContextRequirement.TargetCountry |
                    AW3WindowContextRequirement.Actor),
                Command(AW3CommandKind.ReplaceMilitaryGovernorateGovernor,
                    AW3WindowCategory.Realm, Country() |
                    AW3WindowContextRequirement.TargetCountry |
                    AW3WindowContextRequirement.Actor),
                Command(AW3CommandKind.ApplyCustomCourtTemplate,
                    AW3WindowCategory.Domestic, Country()),
                Command(AW3CommandKind.GrantBanditAmnesty,
                    AW3WindowCategory.Domestic, Country() |
                    AW3WindowContextRequirement.TargetCountry)
                ,
                Command(AW3CommandKind.CommitDomesticHousehold,
                    AW3WindowCategory.Domestic, Country() |
                    AW3WindowContextRequirement.Actor)
            });

        public static IReadOnlyList<AW3WindowDescriptor> Windows =>
            WindowDescriptors;

        public static IReadOnlyList<AW3CommandDescriptor> Commands =>
            CommandDescriptors;

        public static AW3WindowDescriptor GetWindow(AW3WindowKind kind)
        {
            int index = (int)kind;
            if (index < 0 || index >= WindowDescriptors.Count ||
                WindowDescriptors[index].Kind != kind)
                throw new ArgumentOutOfRangeException(nameof(kind));
            return WindowDescriptors[index];
        }

        public static AW3CommandDescriptor GetCommand(AW3CommandKind kind)
        {
            for (int index = 0; index < CommandDescriptors.Count; index++)
            {
                AW3CommandDescriptor descriptor = CommandDescriptors[index];
                if (descriptor.Kind == kind) return descriptor;
            }
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        private static AW3WindowDescriptor Window(AW3WindowKind kind,
            string id, string icon, AW3WindowCategory category,
            AW3WindowContextRequirement requirements =
                AW3WindowContextRequirement.None) =>
            new AW3WindowDescriptor(kind, id, id + " Title", icon,
                category, requirements, countryLauncher: true,
                PayloadSchemaVersion);

        private static AW3CommandDescriptor Command(AW3CommandKind kind,
            AW3WindowCategory category,
            AW3WindowContextRequirement requirements) =>
            new AW3CommandDescriptor(kind, category, requirements,
                PayloadSchemaVersion);

        private static AW3WindowContextRequirement Country() =>
            AW3WindowContextRequirement.Country;
    }
}
