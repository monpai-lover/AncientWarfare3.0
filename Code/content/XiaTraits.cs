using System.Collections.Generic;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.content
{
    /// <summary>
    ///     特质注册(移植自 AW2 TraitLibrary,蓝图 1.2)。
    ///     9 个特质,分属 aw2 / aw_social_identity 两组。
    ///
    ///     ⚠️ 属性 key 适配新版 base_stats_library(AW2 旧 key 部分已删):
    ///        mod_health → health(直接加血)、knockback_reduction → knockback、fertility → birth_rate。
    ///     天命、旧君、义军与奴隶行为由各自的年度/事务服务管理，特质仅保存身份和属性，
    ///     不再挂逐 actor 的 special_effect，避免重复扫描。
    ///
    ///     新版 API:AssetManager.traits(ActorTraitLibrary),add(new ActorTrait{...}),
    ///        base_stats["key"]=val,opposite_list/addOpposite,action_special_effect(WorldAction)。
    /// </summary>
    public static class XiaTraits
    {
        /// <summary>已注册的社会身份特质(组内互斥,注册时两两设为 opposite)。</summary>
        private static readonly List<ActorTrait> _socialIdentityTraits = new();

        public static void Init()
        {
            foreach (string error in CourtSchoolRegistry.Validate())
                ModClass.LogWarning("Court school registry: " + error);

            // ===== aw2 组:属性/身份特质 =====

            // figure 特殊人物:血+15、政务+10
            var figure = NewTrait("figure", TraitIconUsageRules.IconForTrait("figure"), XiaTraitGroups.AW2);
            figure.base_stats["health"] = 15f;      // 旧 mod_health +15
            figure.base_stats["stewardship"] = 10f;

            // 天命:政务+150、外交+15、战争+14、智力+14；状态检定由 MandateService 统一执行。
            var tianming = NewTrait("aw_tianming", "ui/Icons/traits/iconTianming", XiaTraitGroups.AW2);
            tianming.base_stats["stewardship"] = 150f;
            tianming.base_stats["diplomacy"] = 15f;
            tianming.base_stats["warfare"] = 14f;
            tianming.base_stats["intelligence"] = 14f;

            // first 天命之子:外交+15、战争+14、智力+14；不再通过特质自动篡位。
            var first = NewTrait("first", "ui/Icons/traits/iconfirst", XiaTraitGroups.AW2);
            first.base_stats["diplomacy"] = 15f;
            first.base_stats["warfare"] = 14f;
            first.base_stats["intelligence"] = 14f;

            // formerking 亡国之君；身份与复国宣称由亡国/复国服务管理。
            var formerking = NewTrait(LineageKeys.TRAIT_FORMER_KING,
                TraitIconUsageRules.IconForTrait(LineageKeys.TRAIT_FORMER_KING), XiaTraitGroups.AW2);

            var general = NewTrait(LineageKeys.TRAIT_GENERAL,
                TraitIconUsageRules.IconForTrait(LineageKeys.TRAIT_GENERAL), XiaTraitGroups.AW2);
            general.base_stats["warfare"] = 2f;
            general.base_stats["stewardship"] = 1f;

            var armyCommander = NewTrait(LineageKeys.TRAIT_ARMY_COMMANDER,
                TraitIconUsageRules.IconForTrait(LineageKeys.TRAIT_ARMY_COMMANDER), XiaTraitGroups.AW2);
            armyCommander.base_stats["warfare"] = 1f;

            // 禁卫军:小幅精锐加成，避免 AW2 版本的超模伤害/速度。
            var jinwei = NewTrait(LineageKeys.TRAIT_GUARD, "ui/Icons/traits/iconjinwei", XiaTraitGroups.AW2);
            jinwei.base_stats["scale"] = 0.02f;
            jinwei.base_stats["health"] = 8f;
            jinwei.base_stats["damage"] = 4f;
            jinwei.base_stats["warfare"] = 2f;
            jinwei.base_stats["speed"] = 1f;
            jinwei.base_stats["knockback"] = -0.1f;

            var fiefSoldier = NewTrait("aw_fief_soldier", "ui/Icons/traits/iconjinwei", XiaTraitGroups.AW2);
            fiefSoldier.base_stats["health"] = 3f;
            fiefSoldier.base_stats["damage"] = 1f;
            fiefSoldier.base_stats["warfare"] = 1f;

            // 求嗣:无男嗣的国王临时挂此特质,大幅提高 birth_rate(疯狂生育直到有儿子),有子后由周期检查撤销。
            var heirUrge = NewTrait(LineageKeys.TRAIT_HEIR_URGE, "ui/Icons/traits/iconguizu", XiaTraitGroups.AW2);
            heirUrge.needs_to_be_explored = false;
            heirUrge.unlocked_with_achievement = false;
            heirUrge.base_stats["birth_rate"] = 0f;

            // rebel 反抗者:血+2、外交+35、政务+35、战争+4、同特质聚集；义军由叛乱服务管理。
            var rebel = NewTrait("rebel", "ui/Icons/traits/iconrebel", XiaTraitGroups.AW2);
            rebel.same_trait_mod = 20;
            rebel.base_stats["health"] = 2f;        // 旧 mod_health +2
            rebel.base_stats["diplomacy"] = 35f;
            rebel.base_stats["stewardship"] = 35f;
            rebel.base_stats["warfare"] = 4f;

            // ===== aw_social_identity 组:社会身份(互斥) =====

            // zhuhou 诸侯:血+5、政务+5
            var zhuhou = NewSocialIdentity(LineageKeys.TRAIT_ZHUHOU,
                TraitIconUsageRules.IconForTrait(LineageKeys.TRAIT_ZHUHOU));
            zhuhou.base_stats["health"] = 5f;       // 旧 mod_health +5
            zhuhou.base_stats["stewardship"] = 5f;

            // guizu 贵族:多子(birth_rate +2)。
            // ⚠ 新版 birth_rate 是**整数额外子女尝试次数**(BabyMaker.cs:123 用 (int)stats["birth_rate"]),
            //   base_stats 经 mergeStats **累加**到 stats。AW2 旧值 0.35(fertility+35%)在新版整数语义下
            //   累加后被 (int) 取整丢弃 → 贵族 birth_rate 实际 0 → **贵族不生育**(已修:种族 genome 补 birth_rate=4,
            //   贵族再 +2 = 6,取整 6 → 贵族多子)。小数一律不能用于 birth_rate。
            var guizu = NewSocialIdentity("guizu", "ui/Icons/traits/iconguizu");
            guizu.base_stats["birth_rate"] = 2f;
            guizu.base_stats["health"] = 500f;

            NewSocialIdentity(LineageKeys.TRAIT_SHIDAFU,
                TraitIconUsageRules.IconForTrait(LineageKeys.TRAIT_SHIDAFU));

            // slave 奴隶:不出生(rate_birth=0)、世袭(rate_inherit=100)、周期强制职业=Slave
            var slave = NewSocialIdentity("slave", "ui/policy/start_slaves");
            slave.rate_birth = 0;
            slave.rate_inherit = 100;
            slave.special_effect_interval = 3;
            RegisterCourtSchoolTrait(CourtTraitId.Ru, "ui/Icons/traits/iconRujia",
                stewardship: 2f, diplomacy: 1f, warfare: 0f, intelligence: 1f);
            RegisterCourtSchoolTrait(CourtTraitId.Legalist, "ui/Icons/traits/iconfajia",
                stewardship: 2f, diplomacy: 0f, warfare: 1f, intelligence: 1f);
            RegisterCourtSchoolTrait(CourtTraitId.Dao, "ui/Icons/traits/icontao",
                stewardship: 1f, diplomacy: 1f, warfare: -1f, intelligence: 2f);
            RegisterCourtSchoolTrait(CourtTraitId.Mohist, "ui/Icons/traits/iconmo",
                stewardship: 1f, diplomacy: 0f, warfare: 1f, intelligence: 2f);
            RegisterCourtSchoolTrait(CourtTraitId.Military, "ui/Icons/traits/iconbinfa",
                stewardship: 0f, diplomacy: 0f, warfare: 3f, intelligence: 1f);
            RegisterCourtSchoolTrait(CourtTraitId.Diplomat, "ui/Icons/traits/iconzonheng",
                stewardship: 0f, diplomacy: 3f, warfare: 0f, intelligence: 1f);
            RegisterCourtSchoolTrait(CourtTraitId.Agrarian, "ui/Icons/traits/iconnong",
                stewardship: 2f, diplomacy: 0f, warfare: 0f, intelligence: 1f);
            RegisterCourtSchoolTrait(CourtTraitId.YinYang, "ui/Icons/traits/iconyingyang",
                stewardship: 1f, diplomacy: 1f, warfare: 0f, intelligence: 2f);
            RegisterCourtSchoolTrait(CourtTraitId.Logician, "ui/Icons/traits/iconmingjia",
                stewardship: 0f, diplomacy: 2f, warfare: 0f, intelligence: 2f);
            RegisterCourtSchoolTrait(CourtTraitId.Medical, "ui/Icons/traits/iconoisha",
                stewardship: 2f, diplomacy: 0f, warfare: 0f, intelligence: 2f);
            RegisterCourtSchoolTrait(CourtTraitId.Syncretist, "ui/Icons/traits/iconzajia",
                stewardship: 1f, diplomacy: 1f, warfare: 1f, intelligence: 1f);
            RegisterCourtSchoolTrait(CourtTraitId.Merchant, "ui/Icons/traits/iconshangjia",
                stewardship: 2f, diplomacy: 2f, warfare: 0f, intelligence: 1f);
            RegisterCourtSchoolTrait(CourtTraitId.Craftsman, "ui/Icons/traits/icongongjia",
                stewardship: 2f, diplomacy: 0f, warfare: 1f, intelligence: 2f);
            RegisterCourtSchoolTrait(CourtTraitId.Historian, "ui/Icons/traits/iconshijia",
                stewardship: 1f, diplomacy: 2f, warfare: 0f, intelligence: 2f);
        }

        /// <summary>建普通特质(默认 birth/inherit=0,即不随机生成/不遗传,只能主动赋予)。</summary>
        private static ActorTrait NewTrait(string pId, string pIcon, string pGroup)
        {
            var trait = new ActorTrait
            {
                id = pId,
                path_icon = pIcon,
                rate_birth = 0,
                rate_inherit = 0,
                group_id = pGroup
            };
            AssetManager.traits.add(trait);
            trait.unlock();
            return trait;
        }

        /// <summary>建社会身份特质,并与已注册的同组特质两两互斥。</summary>
        private static ActorTrait NewSocialIdentity(string pId, string pIcon)
        {
            var trait = new ActorTrait
            {
                id = pId,
                path_icon = pIcon,
                rate_birth = 0,
                rate_inherit = 0,
                needs_to_be_explored = false,
                unlocked_with_achievement = false,
                group_id = XiaTraitGroups.SOCIAL_IDENTITY
            };
            AssetManager.traits.add(trait);
            trait.unlock();

            // 与已有社会身份两两互斥
            foreach (var other in _socialIdentityTraits)
            {
                trait.addOpposite(other.id);
                other.addOpposite(trait.id);
            }
            _socialIdentityTraits.Add(trait);
            return trait;
        }

        private static ActorTrait RegisterCourtSchoolTrait(string pId, string pIcon, float stewardship,
            float diplomacy, float warfare, float intelligence)
        {
            var trait = NewTrait(pId, pIcon, XiaTraitGroups.AW2);
            trait.needs_to_be_explored = false;
            trait.unlocked_with_achievement = false;
            trait.base_stats["stewardship"] = stewardship;
            trait.base_stats["diplomacy"] = diplomacy;
            trait.base_stats["warfare"] = warfare;
            trait.base_stats["intelligence"] = intelligence;
            return trait;
        }
    }
}
