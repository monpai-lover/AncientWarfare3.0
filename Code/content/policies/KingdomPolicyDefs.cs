using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.content.policies
{
    internal enum PolicyNodeKind
    {
        Social,
        Tech,
        Decision
    }

    internal sealed class KingdomPolicyDef
    {
        public string Id;
        public PolicyNodeKind Kind;
        public string NameKey;
        public string DescKey;
        public string FallbackName;
        public string FallbackDesc;
        public string IconPath;
        public float Cost;
        public string ClassAfter;
        public string ArmyStateAfter;
        public string NameStateAfter;
        public string EnfeoffmentStateAfter;
        public string GovernmentStateAfter;
        public string[] RequiredPolicies = Array.Empty<string>();
        public string[] RequiredTechs = Array.Empty<string>();
        public KingdomPolicyProfileId[] ProfileIds =
            { KingdomPolicyProfileId.Xia };
        public bool Repeatable;
        public int Column;
        public int Row;
    }

    internal static class KingdomPolicyDefs
    {
        private static readonly KingdomPolicyProfileId[] CommonProfiles =
            { KingdomPolicyProfileId.Common };
        private static readonly KingdomPolicyProfileId[] WesternProfiles =
            { KingdomPolicyProfileId.WesternGeneral };

        public const string ClassDefault = "default";
        public const string ClassSlaveOwner = "slaveowner";
        public const string ClassHalfAristocrat = "halfaristocrat";
        public const string ClassAristocrat = "aristocrat";
        public const string ClassReform = "reform";
        public const string ClassRepublic = "republic";
        public const string ClassRebel = "peasant_rebel";
        public const string ClassBandit = "peasant_bandit";
        public const string ArmyDefault = "default";
        public const string ArmySlaveSoldier = "slave_soldier";
        public const string NameDefault = "default";
        public const string NameIntegration = "name_integration";
        public const string EnfeoffmentDefault = "default";
        public const string EnfeoffmentBase = "enfeoffment_base";
        public const string EnfeoffmentLimit = "enfeoffment_limit";
        public const string EnfeoffmentUnlimit = "enfeoffment_unlimit";

        public static readonly string[] ClassStates =
        {
            ClassDefault,
            ClassSlaveOwner,
            ClassHalfAristocrat,
            ClassAristocrat,
            ClassReform,
            ClassRepublic,
            ClassRebel,
            ClassBandit
        };

        private static readonly KingdomPolicyDef[] _all =
        {
            new KingdomPolicyDef
            {
                Id = "aw_tech_writing",
                Kind = PolicyNodeKind.Tech,
                ProfileIds = CommonProfiles,
                NameKey = "aw_tech_writing",
                DescKey = "aw_tech_writing_desc",
                FallbackName = "\u6587\u5B57\u8BB0\u4E8B",
                FallbackDesc = "\u5EFA\u7ACB\u7A33\u5B9A\u7684\u5B98\u5E9C\u8BB0\u5F55\uFF0C\u89E3\u9501\u66F4\u590D\u6742\u7684\u56FD\u7B56\u3002",
                IconPath = "ui/icons/iconKnowledge",
                Cost = 40f,
                Column = 0,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_tech_bronze_casting",
                Kind = PolicyNodeKind.Tech,
                ProfileIds = CommonProfiles,
                NameKey = "aw_tech_bronze_casting",
                DescKey = "aw_tech_bronze_casting_desc",
                FallbackName = "\u9752\u94DC\u94F8\u9020",
                FallbackDesc = "\u6539\u826F\u5175\u5668\u4E0E\u793C\u5668\uFF0C\u4E3A\u66F4\u6210\u719F\u7684\u56FD\u5BB6\u5236\u5EA6\u6253\u57FA\u7840\u3002",
                IconPath = "ui/icons/icon_ore",
                Cost = 55f,
                RequiredTechs = new[] { "aw_tech_pottery_casting" },
                Column = 2,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_tech_pottery_casting",
                Kind = PolicyNodeKind.Tech,
                ProfileIds = CommonProfiles,
                NameKey = "aw_tech_pottery_casting",
                DescKey = "aw_tech_pottery_casting_desc",
                FallbackName = "\u9676\u8303\u94F8\u9020",
                FallbackDesc = "\u7528\u9676\u8303\u7A33\u5B9A\u5668\u7269\u4E0E\u5175\u5668\u94F8\u9020\uFF0C\u4E3A\u9752\u94DC\u6280\u827A\u6253\u57FA\u7840\u3002",
                IconPath = "ui/icons/icon_ore",
                Cost = 42f,
                RequiredTechs = new[] { "aw_tech_writing" },
                Column = 1,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_tech_iron_plow",
                Kind = PolicyNodeKind.Tech,
                NameKey = "aw_tech_iron_plow",
                DescKey = "aw_tech_iron_plow_desc",
                FallbackName = "\u94C1\u94F8\u7281",
                FallbackDesc = "\u63A8\u52A8\u519C\u4E1A\u5DE5\u5177\u6539\u826F\uFF0C\u662F\u5E9F\u5974\u8F6C\u578B\u7684\u79D1\u6280\u524D\u63D0\u3002",
                IconPath = "ui/icons/iconFood",
                Cost = 70f,
                RequiredTechs = new[] { "aw_tech_bronze_casting" },
                Column = 3,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_tech_well_field_survey",
                Kind = PolicyNodeKind.Tech,
                ProfileIds = CommonProfiles,
                NameKey = "aw_tech_well_field_survey",
                DescKey = "aw_tech_well_field_survey_desc",
                FallbackName = "\u4E95\u7530\u6D4B\u7ED8",
                FallbackDesc = "\u6574\u7406\u7530\u754C\u4E0E\u805A\u843D\u8BB0\u5F55\uFF0C\u63D0\u4F9B\u5F81\u53D1\u548C\u5206\u5C01\u7684\u5730\u7C4D\u57FA\u7840\u3002",
                IconPath = "ui/icons/iconDiplomacy",
                Cost = 48f,
                RequiredTechs = new[] { "aw_tech_writing" },
                Column = 1,
                Row = 1
            },
            new KingdomPolicyDef
            {
                Id = "aw_tech_granary_accounting",
                Kind = PolicyNodeKind.Tech,
                ProfileIds = CommonProfiles,
                NameKey = "aw_tech_granary_accounting",
                DescKey = "aw_tech_granary_accounting_desc",
                FallbackName = "\u4ED3\u50A8\u8BB0\u8D26",
                FallbackDesc = "\u7528\u7B26\u7C4D\u7BA1\u7406\u4ED3\u5EEA\u548C\u8D21\u8D4B\uFF0C\u4F7F\u5927\u56FD\u52A8\u5458\u66F4\u7A33\u5B9A\u3002",
                IconPath = "ui/icons/iconKnowledge",
                Cost = 68f,
                RequiredTechs = new[] { "aw_tech_well_field_survey" },
                Column = 4,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_tech_chariot_training",
                Kind = PolicyNodeKind.Tech,
                NameKey = "aw_tech_chariot_training",
                DescKey = "aw_tech_chariot_training_desc",
                FallbackName = "\u8F66\u6218\u8BAD\u7EC3",
                FallbackDesc = "\u5EFA\u7ACB\u8F66\u58EB\u8BAD\u7EC3\u548C\u6218\u9635\u534F\u540C\uFF0C\u63D0\u5347\u519B\u529F\u5236\u5EA6\u7684\u53EF\u884C\u6027\u3002",
                IconPath = "ui/icons/iconDamage",
                Cost = 66f,
                RequiredTechs = new[] { "aw_tech_bronze_casting" },
                Column = 3,
                Row = 1
            },
            new KingdomPolicyDef
            {
                Id = "aw_tech_city_defense",
                Kind = PolicyNodeKind.Tech,
                ProfileIds = CommonProfiles,
                NameKey = "aw_tech_city_defense",
                DescKey = "aw_tech_city_defense_desc",
                FallbackName = "\u57CE\u9632\u8425\u9020",
                FallbackDesc = "\u6539\u826F\u57CE\u5899\u3001\u58D5\u6C9F\u4E0E\u5B88\u5907\u8BBE\u65BD\uFF0C\u652F\u6301\u8FB9\u9091\u5C01\u5EFA\u3002",
                IconPath = "ui/icons/iconArmor",
                Cost = 74f,
                RequiredTechs = new[] { "aw_tech_bronze_casting", "aw_tech_granary_accounting" },
                Column = 4,
                Row = 1
            },
            new KingdomPolicyDef
            {
                Id = "aw_tech_rites_music",
                Kind = PolicyNodeKind.Tech,
                NameKey = "aw_tech_rites_music",
                DescKey = "aw_tech_rites_music_desc",
                FallbackName = "\u793C\u4E50\u5236\u5EA6",
                FallbackDesc = "\u628A\u796D\u7940\u3001\u5BB4\u4F1A\u548C\u8D35\u65CF\u7B49\u7EA7\u7EB3\u5165\u56FA\u5B9A\u4EEA\u5236\u3002",
                IconPath = "ui/icons/iconCulture",
                Cost = 72f,
                RequiredTechs = new[] { "aw_tech_writing", "aw_tech_bronze_casting" },
                Column = 5,
                Row = 1
            },
            new KingdomPolicyDef
            {
                Id = "aw_tech_official_court",
                Kind = PolicyNodeKind.Tech,
                NameKey = "aw_tech_official_court",
                DescKey = "aw_tech_official_court_desc",
                FallbackName = "\u5B98\u573A\u5236\u5EA6",
                FallbackDesc = "\u5C06\u539F\u59CB\u671D\u4F1A\u5347\u7EA7\u4E3A\u767E\u5BB6\u5B98\u573A\uFF0C\u5141\u8BB8\u8D24\u4EBA\u5165\u4ED5\u5E76\u5F71\u54CD\u56FD\u5BB6\u8DEF\u7EBF\u3002",
                IconPath = "ui/icons/iconDiplomacy",
                Cost = 55f,
                RequiredTechs = new[] { "aw_tech_writing" },
                Column = 1,
                Row = 2
            },
            new KingdomPolicyDef
            {
                Id = "aw_tech_nine_rank_system",
                Kind = PolicyNodeKind.Tech,
                NameKey = "aw_tech_nine_rank_system",
                DescKey = "aw_tech_nine_rank_system_desc",
                FallbackName = "\u4E5D\u54C1\u4E2D\u6B63",
                FallbackDesc = "\u6BCF\u516D\u5E74\u590D\u8BC4\u4E61\u54C1\uFF0C\u95E8\u7B2C\u660E\u663E\u52A0\u5206\uFF0C\u5BD2\u95E8\u4ECD\u53EF\u51ED\u80FD\u529B\u3001\u5B66\u6D3E\u8D44\u5386\u3001\u8003\u8BFE\u548C\u529F\u7EE9\u8FDB\u5165\u4E0A\u54C1\u3002",
                IconPath = "ui/icons/iconKnowledge",
                Cost = 80f,
                RequiredTechs = new[] { "aw_tech_official_court" },
                Column = 2,
                Row = 3
            },
            new KingdomPolicyDef
            {
                Id = "aw_tech_civil_service_examination",
                Kind = PolicyNodeKind.Tech,
                NameKey = "aw_tech_civil_service_examination",
                DescKey = "aw_tech_civil_service_examination_desc",
                FallbackName = "\u8D21\u4E3E\u5236\u5EA6",
                FallbackDesc = "\u4EE5\u8003\u8BD5\u9009\u62D4\u53D7\u6559\u80B2\u7684\u58EB\u4EBA\uFF0C\u4F7F\u8D35\u65CF\u3001\u5BD2\u95E8\u4E0E\u5E73\u6C11\u51ED\u624D\u5B66\u5165\u4ED5\u3002",
                IconPath = "ui/icons/iconKnowledge",
                Cost = 90f,
                RequiredTechs = new[] { "aw_tech_nine_rank_system" },
                Column = 3,
                Row = 3
            },
            new KingdomPolicyDef
            {
                Id = "aw_tech_three_departments",
                Kind = PolicyNodeKind.Tech,
                NameKey = "aw_tech_three_departments",
                DescKey = "aw_tech_three_departments_desc",
                FallbackName = "三省六部制",
                FallbackDesc = "将三公九卿升级为三省六部，中枢分职集权，官场更高效。",
                IconPath = "ui/icons/iconDiplomacy",
                Cost = 95f,
                RequiredTechs = new[] { "aw_tech_nine_rank_system" },
                Column = 2,
                Row = 2
            },
            new KingdomPolicyDef
            {
                Id = "aw_tech_song_court",
                Kind = PolicyNodeKind.Tech,
                NameKey = "aw_tech_song_court",
                DescKey = "aw_tech_song_court_desc",
                FallbackName = "宋制官政",
                FallbackDesc = "在三省六部基础上以中书门下和枢密财政诸司分掌文武财计。",
                IconPath = "ui/icons/iconDiplomacy",
                Cost = 125f,
                RequiredTechs = new[] { "aw_tech_three_departments" },
                Column = 3,
                Row = 2
            },
            new KingdomPolicyDef
            {
                Id = "aw_tech_enfeoffment_study",
                Kind = PolicyNodeKind.Tech,
                NameKey = "aw_tech_enfeoffment_study",
                DescKey = "aw_tech_enfeoffment_study_desc",
                FallbackName = "\u5206\u5C01\u8003",
                FallbackDesc = "\u6574\u7406\u5B97\u6CD5\u4E0E\u5C01\u5EFA\u79E9\u5E8F\uFF0C\u89E3\u9501\u57FA\u7840\u5206\u5C01\u56FD\u7B56\u3002",
                IconPath = "ui/icons/iconDiplomacy",
                Cost = 65f,
                RequiredTechs = new[] { "aw_tech_writing", "aw_tech_well_field_survey" },
                Column = 2,
                Row = 1
            },
            new KingdomPolicyDef
            {
                Id = "aw_west_tech_iron_casting",
                Kind = PolicyNodeKind.Tech,
                ProfileIds = WesternProfiles,
                NameKey = "aw_west_tech_iron_casting",
                DescKey = "aw_west_tech_iron_casting_desc",
                FallbackName = "\u94C1\u5668\u94F8\u9020",
                FallbackDesc = "\u6539\u826F\u70BC\u94C1\u4E0E\u94F8\u9020\u5DE5\u827A\uFF0C\u4E3A\u519C\u5177\u548C\u6B66\u5668\u63D0\u4F9B\u66F4\u7A33\u5B9A\u7684\u6750\u6599\u3002",
                IconPath = "ui/icons/icon_ore",
                Cost = 78f,
                RequiredTechs = new[] { "aw_tech_bronze_casting" },
                Column = 3,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_west_tech_coin_minting",
                Kind = PolicyNodeKind.Tech,
                ProfileIds = WesternProfiles,
                NameKey = "aw_west_tech_coin_minting",
                DescKey = "aw_west_tech_coin_minting_desc",
                FallbackName = "\u94F8\u9020\u91D1\u5E01",
                FallbackDesc = "\u4EE5\u7EDF\u4E00\u5E01\u5236\u652F\u6491\u7A0E\u6536\u3001\u5E02\u573A\u4E0E\u5B98\u5E9C\u652F\u4ED8\u3002",
                IconPath = "ui/icons/iconGold",
                Cost = 72f,
                RequiredTechs = new[] { "aw_tech_bronze_casting" },
                Column = 3,
                Row = 1
            },
            new KingdomPolicyDef
            {
                Id = "aw_west_tech_irrigation",
                Kind = PolicyNodeKind.Tech,
                ProfileIds = WesternProfiles,
                NameKey = "aw_west_tech_irrigation",
                DescKey = "aw_west_tech_irrigation_desc",
                FallbackName = "\u704C\u6E89\u6E20",
                FallbackDesc = "\u5F00\u51FF\u6C9F\u6E20\u5E76\u6574\u7406\u519C\u7530\uFF0C\u7A33\u5B9A\u57CE\u9091\u7CAE\u98DF\u751F\u4EA7\u3002",
                IconPath = "ui/icons/iconFood",
                Cost = 70f,
                RequiredTechs = new[] { "aw_tech_well_field_survey" },
                Column = 2,
                Row = 2
            },
            new KingdomPolicyDef
            {
                Id = "aw_west_tech_enfeoffment_study",
                Kind = PolicyNodeKind.Tech,
                ProfileIds = WesternProfiles,
                NameKey = "aw_west_tech_enfeoffment_study",
                DescKey = "aw_west_tech_enfeoffment_study_desc",
                FallbackName = "\u5206\u5C01\u8003",
                FallbackDesc = "\u4EE5\u5C01\u571F\u3001\u4E49\u52A1\u548C\u7EE7\u627F\u89C4\u5219\u7EC4\u7EC7\u5C01\u5EFA\u9886\u5730\u3002",
                IconPath = "ui/icons/iconDiplomacy",
                Cost = 82f,
                RequiredTechs = new[] { "aw_west_tech_irrigation" },
                Column = 3,
                Row = 2
            },
            new KingdomPolicyDef
            {
                Id = "aw_west_tech_tax_office",
                Kind = PolicyNodeKind.Tech,
                ProfileIds = WesternProfiles,
                NameKey = "aw_west_tech_tax_office",
                DescKey = "aw_west_tech_tax_office_desc",
                FallbackName = "\u7A0E\u52A1\u5B98",
                FallbackDesc = "\u5EFA\u7ACB\u4E13\u804C\u7A0E\u52A1\u4E66\u540F\uFF0C\u767B\u8BB0\u571F\u5730\u3001\u8D27\u5E01\u4E0E\u8D21\u8D4B\u3002",
                IconPath = "ui/icons/iconDiplomacy",
                Cost = 68f,
                RequiredTechs = new[] { "aw_tech_writing" },
                Column = 1,
                Row = 3
            },
            new KingdomPolicyDef
            {
                Id = "aw_west_tech_landlord_tax",
                Kind = PolicyNodeKind.Tech,
                ProfileIds = WesternProfiles,
                NameKey = "aw_west_tech_landlord_tax",
                DescKey = "aw_west_tech_landlord_tax_desc",
                FallbackName = "\u5730\u4E3B\u7A0E",
                FallbackDesc = "\u628A\u571F\u5730\u6301\u6709\u4E0E\u8D22\u653F\u8D23\u4EFB\u7ED3\u5408\uFF0C\u63D0\u9AD8\u7A33\u5B9A\u7A0E\u6E90\u3002",
                IconPath = "ui/icons/iconGold",
                Cost = 88f,
                RequiredTechs = new[]
                {
                    "aw_west_tech_tax_office",
                    "aw_west_tech_coin_minting"
                },
                Column = 4,
                Row = 3
            },
            new KingdomPolicyDef
            {
                Id = "aw_west_tech_office_system",
                Kind = PolicyNodeKind.Tech,
                ProfileIds = WesternProfiles,
                NameKey = "aw_west_tech_office_system",
                DescKey = "aw_west_tech_office_system_desc",
                FallbackName = "\u5B98\u804C\u4F53\u7CFB",
                FallbackDesc = "\u5C06\u5BAB\u5EF7\u804C\u8D23\u5212\u5206\u4E3A\u7A33\u5B9A\u5B98\u804C\uFF0C\u5F62\u6210\u53EF\u7EE7\u7EED\u7684\u884C\u653F\u79E9\u5E8F\u3002",
                IconPath = "ui/icons/iconDiplomacy",
                Cost = 76f,
                RequiredTechs = new[] { "aw_tech_writing" },
                Column = 1,
                Row = 4
            },
            new KingdomPolicyDef
            {
                Id = "aw_west_tech_elective_offices",
                Kind = PolicyNodeKind.Tech,
                ProfileIds = WesternProfiles,
                NameKey = "aw_west_tech_elective_offices",
                DescKey = "aw_west_tech_elective_offices_desc",
                FallbackName = "\u9009\u4E3E\u5236",
                FallbackDesc = "\u7531\u8D35\u65CF\u4E0E\u5BAB\u5EF7\u96C6\u4F1A\u63A8\u4E3E\u4E3B\u8981\u5B98\u5458\uFF0C\u4EE5\u4EFB\u671F\u7EA6\u675F\u6743\u529B\u3002",
                IconPath = "ui/icons/iconDiplomacy",
                Cost = 104f,
                RequiredTechs = new[]
                {
                    "aw_west_tech_office_system",
                    "aw_tech_pottery_casting",
                    "aw_west_tech_landlord_tax"
                },
                Column = 5,
                Row = 4
            },
            new KingdomPolicyDef
            {
                Id = "aw_west_tech_ritual_order",
                Kind = PolicyNodeKind.Tech,
                ProfileIds = WesternProfiles,
                NameKey = "aw_west_tech_ritual_order",
                DescKey = "aw_west_tech_ritual_order_desc",
                FallbackName = "\u793C\u4E50\u5236\u5EA6",
                FallbackDesc = "\u4EE5\u5BAB\u5EF7\u4EEA\u5F0F\u3001\u795E\u804C\u4E0E\u7235\u4F4D\u79E9\u5E8F\u5F3A\u5316\u541B\u4E3B\u5408\u6CD5\u6027\u3002",
                IconPath = "ui/icons/iconCulture",
                Cost = 112f,
                RequiredTechs = new[] { "aw_west_tech_elective_offices" },
                Column = 6,
                Row = 4
            },
            new KingdomPolicyDef
            {
                Id = "aw_west_tech_feudal_retainers",
                Kind = PolicyNodeKind.Tech,
                ProfileIds = WesternProfiles,
                NameKey = "aw_west_tech_feudal_retainers",
                DescKey = "aw_west_tech_feudal_retainers_desc",
                FallbackName = "\u5C01\u5EFA\u5BB6\u81E3",
                FallbackDesc = "\u4EE5\u5BB6\u81E3\u3001\u5C01\u81E3\u548C\u5730\u65B9\u8D35\u65CF\u627F\u62C5\u519B\u4E8B\u4E0E\u884C\u653F\u4E49\u52A1\u3002",
                IconPath = "ui/icons/iconDiplomacy",
                Cost = 120f,
                RequiredTechs = new[]
                {
                    "aw_west_tech_ritual_order",
                    "aw_west_tech_enfeoffment_study"
                },
                Column = 7,
                Row = 4
            },
            new KingdomPolicyDef
            {
                Id = "aw_west_tech_royal_domain",
                Kind = PolicyNodeKind.Tech,
                ProfileIds = WesternProfiles,
                NameKey = "aw_west_tech_royal_domain",
                DescKey = "aw_west_tech_royal_domain_desc",
                FallbackName = "\u56FD\u738B\u76F4\u8F96",
                FallbackDesc = "\u6269\u5927\u738B\u5BA4\u76F4\u5C5E\u9886\u5730\u4E0E\u5B98\u50DA\uFF0C\u964D\u4F4E\u5BF9\u5C01\u81E3\u7684\u4F9D\u8D56\u3002",
                IconPath = "ui/icons/iconKingdom",
                Cost = 132f,
                RequiredTechs = new[] { "aw_west_tech_feudal_retainers" },
                Column = 8,
                Row = 4
            },
            new KingdomPolicyDef
            {
                Id = "aw_policy_household_registry",
                Kind = PolicyNodeKind.Social,
                ProfileIds = CommonProfiles,
                NameKey = "aw_policy_household_registry",
                DescKey = "aw_policy_household_registry_desc",
                FallbackName = "\u6237\u7C4D\u7F16\u6237",
                FallbackDesc = "\u767B\u8BB0\u6237\u53E3\u3001\u57CE\u9091\u548C\u8D21\u8D4B\u5BF9\u8C61\uFF0C\u662F\u5F79\u4F7F\u4E0E\u56FD\u7B56\u6267\u884C\u7684\u57FA\u7840\u3002",
                IconPath = "ui/icons/iconKnowledge",
                Cost = 38f,
                RequiredTechs = new[] { "aw_tech_writing" },
                Column = 0,
                Row = 1
            },
            new KingdomPolicyDef
            {
                Id = "aw_policy_start_slavery",
                Kind = PolicyNodeKind.Social,
                ProfileIds = CommonProfiles,
                NameKey = "aw_policy_start_slavery",
                DescKey = "aw_policy_start_slavery_desc",
                FallbackName = "\u5F00\u542F\u5974\u96B6\u5236",
                FallbackDesc = "\u56FD\u5BB6\u6B63\u5F0F\u627F\u8BA4\u5974\u96B6\u8EAB\u4EFD\u4E0E\u5974\u96B6\u52B3\u5F79\u3002",
                IconPath = "ui/policy/start_slaves",
                Cost = 45f,
                RequiredPolicies = new[] { "aw_policy_household_registry" },
                ClassAfter = ClassSlaveOwner,
                Column = 0,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_policy_corvee_labor",
                Kind = PolicyNodeKind.Social,
                ProfileIds = CommonProfiles,
                NameKey = "aw_policy_corvee_labor",
                DescKey = "aw_policy_corvee_labor_desc",
                FallbackName = "\u5FAD\u5F79\u5F81\u53D1",
                FallbackDesc = "\u4EE5\u57CE\u9091\u548C\u6237\u7C4D\u4E3A\u5355\u4F4D\u5206\u6D3E\u52B3\u5F79\uFF0C\u652F\u6491\u57CE\u9632\u3001\u4ED3\u50A8\u548C\u5BAB\u5BA4\u5EFA\u8BBE\u3002",
                IconPath = "ui/icons/iconWork",
                Cost = 48f,
                RequiredPolicies = new[] { "aw_policy_household_registry" },
                RequiredTechs = new[] { "aw_tech_well_field_survey" },
                Column = 1,
                Row = 1
            },
            new KingdomPolicyDef
            {
                Id = "aw_policy_control_slaves",
                Kind = PolicyNodeKind.Social,
                ProfileIds = CommonProfiles,
                NameKey = "aw_policy_control_slaves",
                DescKey = "aw_policy_control_slaves_desc",
                FallbackName = "\u5F3A\u5316\u5974\u96B6\u63A7\u5236",
                FallbackDesc = "\u5B8C\u5584\u5974\u96B6\u767B\u8BB0\u548C\u5F79\u4F7F\u89C4\u5219\uFF0C\u5974\u96B6\u519B\u4E0E\u6355\u5974\u66F4\u7A33\u5B9A\u3002",
                IconPath = "ui/icons/iconDamage",
                Cost = 50f,
                RequiredPolicies = new[] { "aw_policy_start_slavery", "aw_policy_corvee_labor" },
                ClassAfter = ClassSlaveOwner,
                Column = 1,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_policy_slave_army",
                Kind = PolicyNodeKind.Social,
                ProfileIds = CommonProfiles,
                NameKey = "aw_policy_slave_army",
                DescKey = "aw_policy_slave_army_desc",
                FallbackName = "\u5974\u96B6\u519B",
                FallbackDesc = "\u5141\u8BB8\u57CE\u9091\u5C06\u5974\u96B6\u7F16\u5165\u519B\u961F\uFF0C\u4F46\u5C06\u9886\u5FC5\u987B\u4E3A\u975E\u5974\u96B6\u3002",
                IconPath = "ui/icons/iconDamage",
                Cost = 58f,
                RequiredPolicies = new[] { "aw_policy_start_slavery" },
                ClassAfter = ClassSlaveOwner,
                ArmyStateAfter = ArmySlaveSoldier,
                Column = 2,
                Row = 2
            },
            new KingdomPolicyDef
            {
                Id = "aw_policy_start_halfaristocrat",
                Kind = PolicyNodeKind.Social,
                NameKey = "aw_policy_start_halfaristocrat",
                DescKey = "aw_policy_start_halfaristocrat_desc",
                FallbackName = "\u534A\u8D35\u65CF\u8FC7\u6E21",
                FallbackDesc = "\u4ECE\u5355\u7EAF\u5974\u96B6\u5236\u8F6C\u5411\u8D35\u65CF\u5206\u5C42\u79E9\u5E8F\u3002",
                IconPath = "ui/policy/start_halfaristocrat",
                Cost = 70f,
                RequiredPolicies = new[] { "aw_policy_control_slaves" },
                ClassAfter = ClassHalfAristocrat,
                Column = 2,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_policy_noble_council",
                Kind = PolicyNodeKind.Social,
                NameKey = "aw_policy_noble_council",
                DescKey = "aw_policy_noble_council_desc",
                FallbackName = "\u8D35\u65CF\u8BAE\u653F",
                FallbackDesc = "\u8BA9\u6709\u52BF\u6C0F\u652F\u53C2\u4E0E\u5E99\u5802\u51B3\u7B56\uFF0C\u7A33\u5B9A\u534A\u8D35\u65CF\u5236\u7684\u653F\u6CBB\u79E9\u5E8F\u3002",
                IconPath = "ui/icons/iconDiplomacy",
                Cost = 64f,
                RequiredPolicies = new[] { "aw_policy_start_halfaristocrat" },
                RequiredTechs = new[] { "aw_tech_rites_music" },
                ClassAfter = ClassHalfAristocrat,
                Column = 2,
                Row = 1
            },
            new KingdomPolicyDef
            {
                Id = "aw_policy_ancestral_rites",
                Kind = PolicyNodeKind.Social,
                NameKey = "aw_policy_ancestral_rites",
                DescKey = "aw_policy_ancestral_rites_desc",
                FallbackName = "\u5B97\u5E99\u796D\u7940",
                FallbackDesc = "\u4EE5\u5B97\u5E99\u548C\u8C31\u7CFB\u786E\u8BA4\u8D35\u65CF\u6B63\u7EDF\uFF0C\u4E3A\u59D3\u6C0F\u5408\u6D41\u63D0\u4F9B\u793C\u5236\u4F9D\u636E\u3002",
                IconPath = "ui/icons/iconCulture",
                Cost = 72f,
                RequiredPolicies = new[] { "aw_policy_noble_council" },
                RequiredTechs = new[] { "aw_tech_rites_music" },
                ClassAfter = ClassHalfAristocrat,
                Column = 3,
                Row = 1
            },
            new KingdomPolicyDef
            {
                Id = "aw_policy_name_integration",
                Kind = PolicyNodeKind.Social,
                NameKey = "aw_policy_name_integration",
                DescKey = "aw_policy_name_integration_desc",
                FallbackName = "\u59D3\u6C0F\u5408\u6D41",
                FallbackDesc = "\u7EDF\u4E00\u59D3\u4E0E\u6C0F\u7684\u79F0\u540D\u89C4\u5219\uFF0C\u89E3\u5F00\u540E\u7EED\u5386\u53F2\u4EBA\u7269\u5408\u6D41\u95E8\u3002",
                IconPath = "ui/icons/iconDiplomacy",
                Cost = 80f,
                RequiredPolicies = new[] { "aw_policy_start_halfaristocrat", "aw_policy_ancestral_rites" },
                RequiredTechs = new[] { "aw_tech_writing" },
                ClassAfter = ClassHalfAristocrat,
                NameStateAfter = NameIntegration,
                Column = 3,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_policy_military_merit",
                Kind = PolicyNodeKind.Social,
                NameKey = "aw_policy_military_merit",
                DescKey = "aw_policy_military_merit_desc",
                FallbackName = "\u519B\u529F\u6388\u7235",
                FallbackDesc = "\u7528\u6218\u529F\u8BB0\u5F55\u5956\u8D4F\u519B\u58EB\u548C\u5C06\u9886\uFF0C\u8BA9\u519B\u961F\u4E0E\u8D35\u65CF\u79E9\u5E8F\u4EA7\u751F\u8054\u52A8\u3002",
                IconPath = "ui/icons/iconDamage",
                Cost = 76f,
                RequiredPolicies = new[] { "aw_policy_control_slaves" },
                RequiredTechs = new[] { "aw_tech_chariot_training" },
                Column = 3,
                Row = 2
            },
            new KingdomPolicyDef
            {
                Id = "aw_policy_base_enfeoffment",
                Kind = PolicyNodeKind.Social,
                NameKey = "aw_policy_base_enfeoffment",
                DescKey = "aw_policy_base_enfeoffment_desc",
                FallbackName = "\u57FA\u7840\u5206\u5C01",
                FallbackDesc = "\u627F\u8BA4\u8D35\u65CF\u5916\u51FA\u5C01\u57CE\u4E0E\u5EFA\u7ACB\u652F\u65CF\u7684\u5236\u5EA6\u57FA\u7840\u3002",
                IconPath = "ui/policy/base_enfeoffment",
                Cost = 75f,
                RequiredPolicies = new[] { "aw_policy_name_integration" },
                RequiredTechs = new[] { "aw_tech_enfeoffment_study" },
                ClassAfter = ClassAristocrat,
                EnfeoffmentStateAfter = EnfeoffmentBase,
                Column = 4,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_policy_border_enfeoffment",
                Kind = PolicyNodeKind.Social,
                NameKey = "aw_policy_border_enfeoffment",
                DescKey = "aw_policy_border_enfeoffment_desc",
                FallbackName = "\u8FB9\u9091\u5C01\u5EFA",
                FallbackDesc = "\u628A\u65B0\u57CE\u3001\u8FB9\u9091\u548C\u519B\u9547\u7EB3\u5165\u5206\u5C01\u79E9\u5E8F\uFF0C\u9F13\u52B1\u8D35\u65CF\u5916\u51FA\u5EFA\u652F\u3002",
                IconPath = "ui/policy/base_enfeoffment",
                Cost = 86f,
                RequiredPolicies = new[] { "aw_policy_base_enfeoffment" },
                RequiredTechs = new[] { "aw_tech_city_defense" },
                ClassAfter = ClassAristocrat,
                EnfeoffmentStateAfter = EnfeoffmentBase,
                Column = 4,
                Row = 1
            },
            new KingdomPolicyDef
            {
                Id = "aw_policy_continuous_enfeoffment",
                Kind = PolicyNodeKind.Social,
                NameKey = "aw_policy_continuous_enfeoffment",
                DescKey = "aw_policy_continuous_enfeoffment_desc",
                FallbackName = "\u957F\u671F\u5206\u5C01",
                FallbackDesc = "\u627F\u8BA4\u66F4\u6301\u7EED\u7684\u5916\u51FA\u5C01\u57CE\u548C\u5EFA\u652F\uFF0C\u4F9B\u5206\u5C01\u7CFB\u7EDF\u533A\u5206\u8DEF\u7EBF\u3002",
                IconPath = "ui/policy/base_enfeoffment",
                Cost = 88f,
                RequiredPolicies = new[] { "aw_policy_base_enfeoffment" },
                RequiredTechs = new[] { "aw_tech_city_defense" },
                ClassAfter = ClassAristocrat,
                EnfeoffmentStateAfter = EnfeoffmentUnlimit,
                Column = 5,
                Row = 3
            },
            new KingdomPolicyDef
            {
                Id = "aw_policy_early_law",
                Kind = PolicyNodeKind.Social,
                NameKey = "aw_policy_early_law",
                DescKey = "aw_policy_early_law_desc",
                FallbackName = "\u5F8B\u4EE4\u96CF\u5F62",
                FallbackDesc = "\u628A\u5F81\u53D1\u3001\u8D4B\u7A0E\u3001\u8EAB\u4EFD\u548C\u5211\u7F5A\u5199\u5165\u56FA\u5B9A\u6761\u4F8B\uFF0C\u4E3A\u5E9F\u5974\u548C\u96C6\u6743\u5316\u505A\u51C6\u5907\u3002",
                IconPath = "ui/icons/iconKnowledge",
                Cost = 92f,
                RequiredPolicies = new[] { "aw_policy_name_integration" },
                RequiredTechs = new[] { "aw_tech_granary_accounting" },
                Column = 5,
                Row = 1
            },
            new KingdomPolicyDef
            {
                Id = "aw_policy_mandate_rites",
                Kind = PolicyNodeKind.Social,
                NameKey = "aw_policy_mandate_rites",
                DescKey = "aw_policy_mandate_rites_desc",
                FallbackName = "\u5929\u547D\u793C\u5236",
                FallbackDesc = "\u628A\u53D7\u547D\u3001\u796D\u5929\u548C\u6539\u5143\u7EB3\u5165\u56FD\u5BB6\u793C\u5236\uFF0C\u4E3A\u5929\u547D\u738B\u671D\u63D0\u4F9B\u5408\u6CD5\u6027\u6846\u67B6\u3002",
                IconPath = "ui/Icons/traits/iconTianming",
                Cost = MandatePolicyDefinitionRules.MandateRitesCost,
                RequiredPolicies = new[] { MandatePolicyDefinitionRules.RequiredPolicy },
                RequiredTechs = new[] { MandatePolicyDefinitionRules.RequiredTech },
                ClassAfter = ClassAristocrat,
                Column = MandatePolicyDefinitionRules.MandateRitesColumn,
                Row = MandatePolicyDefinitionRules.MandateRitesRow
            },
            new KingdomPolicyDef
            {
                Id = "aw_policy_adopt_xia_rites",
                Kind = PolicyNodeKind.Social,
                ProfileIds = WesternProfiles,
                NameKey = "aw_policy_adopt_xia_rites",
                DescKey = "aw_policy_adopt_xia_rites_desc",
                FallbackName = "\u91C7\u590F\u793C",
                FallbackDesc = "\u5165\u636E\u590F\u5730\u7684\u5916\u65CF\u738B\u56FD\u91C7\u7528\u590F\u5730\u793C\u5236\u3001\u671D\u4EEA\u548C\u6B63\u7EDF\u53D9\u4E8B\uFF0C\u964D\u4F4E\u6C11\u6028\u5E76\u63A5\u5165\u5929\u547D\u4F53\u7CFB\u3002",
                IconPath = "ui/icons/iconCulture",
                Cost = 70f,
                Column = 6,
                Row = 3
            },
            new KingdomPolicyDef
            {
                Id = "aw_policy_xia_law_institutions",
                Kind = PolicyNodeKind.Social,
                ProfileIds = WesternProfiles,
                NameKey = "aw_policy_xia_law_institutions",
                DescKey = "aw_policy_xia_law_institutions_desc",
                FallbackName = "\u884C\u590F\u5236",
                FallbackDesc = "\u4FDD\u7559\u5916\u65CF\u8840\u7EDF\u4E0E\u519B\u4E8B\u4F18\u52BF\uFF0C\u4F46\u5728\u5F8B\u4EE4\u3001\u7EAA\u5E74\u3001\u7235\u4F4D\u548C\u57CE\u9091\u7BA1\u7406\u4E0A\u6539\u884C\u590F\u5236\u3002",
                IconPath = "ui/icons/iconKnowledge",
                Cost = 96f,
                RequiredPolicies = new[] { "aw_policy_adopt_xia_rites" },
                RequiredTechs = new[] { "aw_tech_writing" },
                Column = 7,
                Row = 3
            },
            new KingdomPolicyDef
            {
                Id = "aw_west_policy_landlord_taxation",
                Kind = PolicyNodeKind.Social,
                ProfileIds = WesternProfiles,
                NameKey = "aw_west_policy_landlord_taxation",
                DescKey = "aw_west_policy_landlord_taxation_desc",
                FallbackName = "\u5730\u4E3B\u7A0E\u5236",
                FallbackDesc = "\u4EE5\u767B\u8BB0\u571F\u5730\u4E0E\u94F8\u5E01\u4F53\u7CFB\u5411\u5927\u5730\u4E3B\u5F81\u7A0E\uFF0C\u5EFA\u7ACB\u7A33\u5B9A\u8D22\u653F\u3002",
                IconPath = "ui/icons/iconGold",
                Cost = 74f,
                RequiredPolicies = new[] { "aw_policy_household_registry" },
                RequiredTechs = new[] { "aw_west_tech_landlord_tax" },
                Column = 4,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_west_policy_noble_council",
                Kind = PolicyNodeKind.Social,
                ProfileIds = WesternProfiles,
                NameKey = "aw_west_policy_noble_council",
                DescKey = "aw_west_policy_noble_council_desc",
                FallbackName = "\u8D35\u65CF\u8BAE\u653F",
                FallbackDesc = "\u8BA9\u5927\u8D35\u65CF\u548C\u5B97\u6559\u9886\u8896\u53C2\u4E0E\u5BAB\u5EF7\u51B3\u7B56\uFF0C\u4EE5\u59A5\u534F\u6362\u53D6\u79E9\u5E8F\u3002",
                IconPath = "ui/icons/iconDiplomacy",
                Cost = 92f,
                RequiredPolicies = new[]
                    { "aw_west_policy_landlord_taxation" },
                RequiredTechs = new[] { "aw_west_tech_ritual_order" },
                GovernmentStateAfter = "western_noble_council",
                Column = 6,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_west_policy_elective_offices",
                Kind = PolicyNodeKind.Social,
                ProfileIds = WesternProfiles,
                NameKey = "aw_west_policy_elective_offices",
                DescKey = "aw_west_policy_elective_offices_desc",
                FallbackName = "\u9009\u4E3E\u5B98\u5236",
                FallbackDesc = "\u5C06\u4E3B\u8981\u5B98\u804C\u7EB3\u5165\u516D\u5E74\u4EFB\u671F\u4E0E\u5BAB\u5EF7\u9009\u4E3E\uFF0C\u51CF\u5C11\u957F\u671F\u5784\u65AD\u3002",
                IconPath = "ui/icons/iconDiplomacy",
                Cost = 108f,
                RequiredPolicies = new[]
                    { "aw_west_policy_noble_council" },
                RequiredTechs = new[] { "aw_west_tech_elective_offices" },
                GovernmentStateAfter = "western_elective",
                Column = 7,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_west_policy_feudal_retainers",
                Kind = PolicyNodeKind.Social,
                ProfileIds = WesternProfiles,
                NameKey = "aw_west_policy_feudal_retainers",
                DescKey = "aw_west_policy_feudal_retainers_desc",
                FallbackName = "\u5C01\u5EFA\u5BB6\u81E3\u5236",
                FallbackDesc = "\u7531\u5730\u65B9\u8D35\u65CF\u4E0E\u5BB6\u81E3\u5206\u62C5\u7EDF\u6CBB\uFF0C\u4EE5\u5C01\u571F\u6362\u53D6\u5FE0\u8BDA\u548C\u5175\u5F79\u3002",
                IconPath = "ui/icons/iconDiplomacy",
                Cost = 116f,
                RequiredPolicies = new[]
                    { "aw_west_policy_noble_council" },
                RequiredTechs = new[] { "aw_west_tech_feudal_retainers" },
                GovernmentStateAfter = "western_feudal",
                Column = 7,
                Row = 1
            },
            new KingdomPolicyDef
            {
                Id = "aw_west_policy_royal_direct_rule",
                Kind = PolicyNodeKind.Social,
                ProfileIds = WesternProfiles,
                NameKey = "aw_west_policy_royal_direct_rule",
                DescKey = "aw_west_policy_royal_direct_rule_desc",
                FallbackName = "\u738B\u5BA4\u76F4\u8F96\u5236",
                FallbackDesc = "\u5C06\u8D22\u653F\u3001\u519B\u4E8B\u4E0E\u5730\u65B9\u5B98\u5458\u91CD\u65B0\u6536\u5F52\u738B\u5BA4\uFF0C\u5EFA\u7ACB\u96C6\u4E2D\u7EDF\u6CBB\u3002",
                IconPath = "ui/icons/iconKingdom",
                Cost = 138f,
                RequiredPolicies = new[]
                    { "aw_west_policy_feudal_retainers" },
                RequiredTechs = new[] { "aw_west_tech_royal_domain" },
                GovernmentStateAfter = "western_royal_direct",
                Column = 8,
                Row = 1
            },
            new KingdomPolicyDef
            {
                Id = "aw_policy_imperial_court",
                Kind = PolicyNodeKind.Social,
                NameKey = "aw_policy_imperial_court",
                DescKey = "aw_policy_imperial_court_desc",
                FallbackName = "\u738B\u671D\u671D\u5EF7",
                FallbackDesc = "\u5EFA\u7ACB\u9762\u5411\u8BF8\u4FAF\u548C\u9644\u5EB8\u7684\u671D\u5EF7\u79E9\u5E8F\uFF0C\u63D0\u9AD8\u5929\u547D\u56FD\u7684\u7687\u6743\u4E0E\u9644\u5EB8\u7BA1\u63A7\u80FD\u529B\u3002",
                IconPath = "ui/icons/iconDiplomacy",
                Cost = 125f,
                RequiredPolicies = new[] { "aw_policy_mandate_rites" },
                RequiredTechs = new[] { "aw_tech_rites_music" },
                ClassAfter = ClassAristocrat,
                Column = 7,
                Row = 1
            },
            new KingdomPolicyDef
            {
                Id = "aw_policy_abolish_slavery",
                Kind = PolicyNodeKind.Social,
                NameKey = "aw_policy_abolish_slavery",
                DescKey = "aw_policy_abolish_slavery_desc",
                FallbackName = "\u5E9F\u5974\u5236",
                FallbackDesc = "\u5173\u95ED\u56FD\u5BB6\u5974\u96B6\u5236\u5F00\u5173\uFF0C\u505C\u6B62\u65B0\u7684\u5974\u96B6\u5236\u6269\u5F20\u3002",
                IconPath = "ui/icons/iconPeace",
                Cost = 100f,
                RequiredPolicies = new[] { "aw_policy_early_law" },
                RequiredTechs = new[] { "aw_tech_iron_plow" },
                ClassAfter = ClassReform,
                Column = 5,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_decision_claim_mandate",
                Kind = PolicyNodeKind.Decision,
                NameKey = "aw_decision_claim_mandate",
                DescKey = "aw_decision_claim_mandate_desc",
                FallbackName = "\u53D7\u547D\u79F0\u5E1D",
                FallbackDesc = "\u5F53\u56FD\u529B\u3001\u7235\u4F4D\u6216\u5386\u53F2\u4EBA\u7269\u6761\u4EF6\u8FBE\u6807\u65F6\uFF0C\u5BA3\u544A\u5EFA\u7ACB\u5929\u547D\u738B\u671D\u3002",
                IconPath = "ui/Icons/traits/iconTianming",
                Cost = 120f,
                RequiredPolicies = new[] { "aw_policy_mandate_rites" },
                Repeatable = true,
                Column = 0,
                Row = 1
            },
            new KingdomPolicyDef
            {
                Id = "aw_decision_year_name",
                Kind = PolicyNodeKind.Decision,
                NameKey = "aw_decision_year_name",
                DescKey = "aw_decision_year_name_desc",
                FallbackName = "\u6539\u5143",
                FallbackDesc = "\u91CD\u65B0\u9881\u5E03\u5E74\u53F7\uFF0C\u8BB0\u5165\u56FD\u53F2\u548C\u5F53\u4EE3\u7EAA\u5E74\u3002",
                IconPath = "ui/policy/change_name",
                Cost = YearNameService.VoluntaryChangeCost,
                Repeatable = true,
                Column = 0,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_decision_title_upgrade",
                Kind = PolicyNodeKind.Decision,
                ProfileIds = CommonProfiles,
                NameKey = "aw_decision_title_upgrade",
                DescKey = "aw_decision_title_upgrade_desc",
                FallbackName = "\u4E0A\u8868\u8BF7\u5C01",
                FallbackDesc = "\u72EC\u7ACB\u56FD\u5BB6\u5728\u9886\u571F\u6269\u5C55\u8FBE\u6807\u540E\u63D0\u5347\u7235\u4F4D\uFF0C\u9644\u5EB8\u5BA1\u6279\u7559\u7ED9\u540E\u7EED\u7CFB\u7EDF\u63A5\u5165\u3002",
                IconPath = "ui/policy/change_name",
                Cost = 80f,
                Repeatable = true,
                Column = 1,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_decision_royal_expansion",
                Kind = PolicyNodeKind.Decision,
                ProfileIds = CommonProfiles,
                NameKey = "aw_decision_royal_expansion",
                DescKey = "aw_decision_royal_expansion_desc",
                FallbackName = "\u6D3E\u5B50\u5F00\u7586",
                FallbackDesc = "\u5728\u9886\u5730\u672A\u8FBE\u638C\u63A7\u4E0A\u9650\u65F6\uFF0C\u6D3E\u9063\u5408\u683C\u738B\u5BA4\u5B50\u55E3\u5EFA\u7ACB\u65B0\u57CE\u3002",
                IconPath = "ui/icons/iconCity",
                Cost = 65f,
                Repeatable = true,
                Column = 2,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_decision_change_capital",
                Kind = PolicyNodeKind.Decision,
                ProfileIds = CommonProfiles,
                NameKey = "aw_decision_change_capital",
                DescKey = "aw_decision_change_capital_desc",
                FallbackName = "\u8FC1\u90FD",
                FallbackDesc = "\u5728\u975E\u6218\u65F6\u5C06\u9996\u90FD\u8FC1\u5F80\u66F4\u9002\u5408\u7EDF\u6CBB\u7684\u57CE\u5E02\u3002",
                IconPath = "ui/policy/move_capital",
                Cost = 70f,
                RequiredTechs = new[] { "aw_tech_well_field_survey" },
                Repeatable = true,
                Column = 3,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_decision_control_slaves",
                Kind = PolicyNodeKind.Decision,
                ProfileIds = CommonProfiles,
                NameKey = "aw_decision_control_slaves",
                DescKey = "aw_decision_control_slaves_desc",
                FallbackName = "\u6574\u996C\u5974\u96B6",
                FallbackDesc = "\u91CD\u7533\u5974\u96B6\u7BA1\u63A7\u4E0E\u6355\u5974\u804C\u8D23\uFF0C\u7528\u4E8E\u7EF4\u6301\u5974\u96B6\u5236\u56FD\u5BB6\u7684\u79E9\u5E8F\u3002",
                IconPath = "ui/icons/iconDamage",
                Cost = 45f,
                RequiredPolicies = new[] { "aw_policy_start_slavery" },
                Repeatable = true,
                Column = 4,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_decision_clean_corruption",
                Kind = PolicyNodeKind.Decision,
                ProfileIds = CommonProfiles,
                NameKey = "aw_decision_clean_corruption",
                DescKey = "aw_decision_clean_corruption_desc",
                FallbackName = "清理腐败",
                FallbackDesc = "整饬官场与地方官府，降低国家和地方腐败。",
                IconPath = "ui/icons/iconPeace",
                Cost = 50f,
                Repeatable = true,
                Column = 5,
                Row = 1
            },
            new KingdomPolicyDef
            {
                Id = "aw_decision_appease_foreign_cities",
                Kind = PolicyNodeKind.Decision,
                ProfileIds = CommonProfiles,
                NameKey = "aw_decision_appease_foreign_cities",
                DescKey = "aw_decision_appease_foreign_cities_desc",
                FallbackName = "\u5B89\u629A\u5F02\u65CF",
                FallbackDesc = "\u5B89\u629A\u672C\u56FD\u63A7\u5236\u7684\u5F02\u6587\u5316\u57CE\u9091\uFF0C\u964D\u4F4E\u5360\u9886\u6C11\u6028\u4E0E\u53DB\u4E71\u98CE\u9669\u3002",
                IconPath = "ui/icons/iconPeace",
                Cost = 45f,
                Repeatable = true,
                Column = 5,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_west_decision_consolidate_royal_authority",
                Kind = PolicyNodeKind.Decision,
                ProfileIds = WesternProfiles,
                NameKey = "aw_west_decision_consolidate_royal_authority",
                DescKey =
                    "aw_west_decision_consolidate_royal_authority_desc",
                FallbackName = "\u5DE9\u56FA\u738B\u6743",
                FallbackDesc = "\u501F\u52A9\u738B\u5BA4\u76F4\u8F96\u5236\u6574\u5408\u5BAB\u5EF7\u4E0E\u9886\u5730\uFF0C\u964D\u4F4E\u7EE7\u627F\u4E89\u8BAE\u5E76\u63D0\u9AD8\u541B\u4E3B\u5408\u6CD5\u6027\u3002",
                IconPath = "ui/icons/iconKingdom",
                Cost = 75f,
                RequiredPolicies = new[]
                    { "aw_west_policy_royal_direct_rule" },
                RequiredTechs = new[] { "aw_west_tech_royal_domain" },
                Repeatable = true,
                Column = 8,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_decision_fabricate_core",
                Kind = PolicyNodeKind.Decision,
                ProfileIds = CommonProfiles,
                NameKey = "aw_decision_fabricate_core",
                DescKey = "aw_decision_fabricate_core_desc",
                FallbackName = "\u5236\u9020\u6838\u5fc3",
                FallbackDesc = "\u5728\u672c\u56fd\u63a7\u5236\u7684\u975e\u6838\u5fc3\u57ce\u5e02\u5236\u9020\u6838\u5fc3\u3002",
                IconPath = "ui/icons/iconMap",
                Cost = 80f,
                Repeatable = true,
                Column = 6,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_decision_seek_suzerain",
                Kind = PolicyNodeKind.Decision,
                ProfileIds = CommonProfiles,
                NameKey = "aw_decision_seek_suzerain",
                DescKey = "aw_decision_seek_suzerain_desc",
                FallbackName = "\u8bf7\u6c42\u81e3\u5c5e",
                FallbackDesc = "\u5c0f\u56fd\u5728\u5a01\u80c1\u4e0b\u4e3b\u52a8\u5411\u5f3a\u56fd\u81e3\u5c5e\u3002",
                IconPath = "ui/wars/war_vassal",
                Cost = 35f,
                Repeatable = true,
                Column = 9,
                Row = 0
            },
            new KingdomPolicyDef
            {
                Id = "aw_decision_absorb_vassal",
                Kind = PolicyNodeKind.Decision,
                ProfileIds = CommonProfiles,
                NameKey = "aw_decision_absorb_vassal",
                DescKey = "aw_decision_absorb_vassal_desc",
                FallbackName = "\u541E\u5E76\u9644\u5EB8",
                FallbackDesc = "\u5148\u5728\u76F4\u5C5E\u9644\u5EB8\u5185\u6210\u529F\u5EFA\u7ACB\u95F4\u8C0D\u7F51\uFF0C\u518D\u8C0B\u5212\u5C06\u5176\u5E76\u5165\u672C\u56FD\uFF1B\u53CC\u65B9\u4EA4\u6218\u65F6\u4E0D\u53EF\u6267\u884C\u3002",
                IconPath = "ui/wars/war_vassal",
                Cost = 120f,
                Repeatable = true,
                Column = 10,
                Row = 0
            }
        };

        static KingdomPolicyDefs()
        {
            KingdomPolicyCatalogRules.Validate(_all);
        }

        public static IReadOnlyList<KingdomPolicyDef> GetNodes(
            KingdomPolicyProfileId pProfile, PolicyNodeKind pKind)
        {
            if (!KingdomPolicyProfileRules.IsResolvableKingdomProfile(
                    pProfile)) return Array.Empty<KingdomPolicyDef>();
            return _all.Where(pDefinition =>
                    pDefinition.Kind == pKind &&
                    KingdomPolicyCatalogRules.BelongsTo(pDefinition,
                        pProfile))
                .ToArray();
        }

        public static IReadOnlyList<KingdomPolicyDef> GetResearchNodes(
            KingdomPolicyProfileId pProfile)
        {
            if (!KingdomPolicyProfileRules.IsResolvableKingdomProfile(
                    pProfile)) return Array.Empty<KingdomPolicyDef>();
            return _all.Where(pDefinition =>
                    pDefinition.Kind != PolicyNodeKind.Decision &&
                    KingdomPolicyCatalogRules.BelongsTo(pDefinition,
                        pProfile))
                .ToArray();
        }

        public static KingdomPolicyDef Get(KingdomPolicyProfileId pProfile,
            string pId)
        {
            KingdomPolicyDef definition = GetAny(pId);
            return KingdomPolicyCatalogRules.BelongsTo(definition, pProfile)
                ? definition
                : null;
        }

        public static bool Contains(KingdomPolicyProfileId pProfile,
            string pId)
        {
            return Get(pProfile, pId) != null;
        }

        internal static KingdomPolicyDef GetAny(string pId)
        {
            if (string.IsNullOrEmpty(pId)) return null;
            return _all.FirstOrDefault(p => p.Id == pId);
        }
    }
}
