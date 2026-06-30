using System.Collections.Generic;

namespace AncientWarfare3.content
{
    /// <summary>
    ///     特质注册(移植自 AW2 TraitLibrary,蓝图 1.2)。
    ///     9 个特质,分属 aw2 / aw_social_identity 两组。
    ///
    ///     ⚠️ 属性 key 适配新版 base_stats_library(AW2 旧 key 部分已删):
    ///        mod_health → health(直接加血)、knockback_reduction → knockback、fertility → birth_rate。
    ///     ⚠️ special_effect 留桩:天命/first/formerking/rebel/slave 的主动效果依赖
    ///        天命(批F)/政策(批G)/Plot 篡位/奴隶(批I)系统,等对应批次回填(见 TODO)。
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
            // ===== aw2 组:属性/身份特质 =====

            // figure 特殊人物:血+15、政务+10
            var figure = NewTrait("figure", "ui/Icons/traits/iconfigure", XiaTraitGroups.AW2);
            figure.base_stats["health"] = 15f;      // 旧 mod_health +15
            figure.base_stats["stewardship"] = 10f;

            // 天命:政务+150、外交+15、战争+14、智力+14。special_effect=天命状态检定(批F)
            var tianming = NewTrait("天命", "ui/Icons/traits/iconTianming", XiaTraitGroups.AW2);
            tianming.base_stats["stewardship"] = 150f;
            tianming.base_stats["diplomacy"] = 15f;
            tianming.base_stats["warfare"] = 14f;
            tianming.base_stats["intelligence"] = 14f;
            // TODO[批F-天命]: action_special_effect = Actionlib.checkP(天命兴衰状态检定)

            // first 天命之子:外交+15、战争+14、智力+14。special_effect=自动篡位驱动天命继承(批F)
            var first = NewTrait("first", "ui/Icons/traits/iconfirst", XiaTraitGroups.AW2);
            first.base_stats["diplomacy"] = 15f;
            first.base_stats["warfare"] = 14f;
            first.base_stats["intelligence"] = 14f;
            // TODO[批F-天命]: action_special_effect = tianmingP(>17岁非国王尝试篡位,全种族只留1个first,10年冷却)

            // formerking 亡国之君。special_effect=残部逻辑(批F)
            var formerking = NewTrait("formerking", "ui/Icons/traits/iconformerking", XiaTraitGroups.AW2);
            // TODO[批F-天命]: action_special_effect = Actionlib.former

            // 禁卫军:体型+0.03、血+2、伤害+25、速度+15、抗击退
            var jinwei = NewTrait("禁卫军", "ui/Icons/traits/iconjinwei", XiaTraitGroups.AW2);
            jinwei.base_stats["scale"] = 0.03f;
            jinwei.base_stats["health"] = 2f;       // 旧 mod_health +2
            jinwei.base_stats["damage"] = 25f;
            jinwei.base_stats["speed"] = 15f;
            jinwei.base_stats["knockback"] = -1f;   // 旧 knockback_reduction +100:抗击退

            // rebel 反抗者:血+2、外交+35、政务+35、战争+4、同特质聚集。special_effect=义军抱团(批F)
            var rebel = NewTrait("rebel", "ui/Icons/traits/iconrebel", XiaTraitGroups.AW2);
            rebel.same_trait_mod = 20;
            rebel.base_stats["health"] = 2f;        // 旧 mod_health +2
            rebel.base_stats["diplomacy"] = 35f;
            rebel.base_stats["stewardship"] = 35f;
            rebel.base_stats["warfare"] = 4f;
            // TODO[批F-天命]: action_special_effect = Actionlib.rebelkingdom(义军抱团)

            // ===== aw_social_identity 组:社会身份(互斥) =====

            // zhuhou 诸侯:血+5、政务+5
            var zhuhou = NewSocialIdentity("zhuhou", "ui/Icons/traits/iconzhuhou");
            zhuhou.base_stats["health"] = 5f;       // 旧 mod_health +5
            zhuhou.base_stats["stewardship"] = 5f;

            // guizu 贵族:多子(birth_rate +2)。
            // ⚠ 新版 birth_rate 是**整数额外子女尝试次数**(BabyMaker.cs:123 用 (int)stats["birth_rate"]),
            //   base_stats 经 mergeStats **累加**到 stats。AW2 旧值 0.35(fertility+35%)在新版整数语义下
            //   累加后被 (int) 取整丢弃 → 贵族 birth_rate 实际 0 → **贵族不生育**(已修:种族 genome 补 birth_rate=4,
            //   贵族再 +2 = 6,取整 6 → 贵族多子)。小数一律不能用于 birth_rate。
            var guizu = NewSocialIdentity("guizu", "ui/Icons/traits/iconguizu");
            guizu.base_stats["birth_rate"] = 2f;

            // slave 奴隶:不出生(rate_birth=0)、世袭(rate_inherit=100)、周期强制职业=Slave
            var slave = NewSocialIdentity("slave", "ui/policy/start_slaves");
            slave.rate_birth = 0;
            slave.rate_inherit = 100;
            slave.special_effect_interval = 3;
            // TODO[批I-奴隶]: action_special_effect = 周期性强制 setProfession(Slave)
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
    }
}
