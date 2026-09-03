using System;
using AncientWarfare3.core.naming;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     把个人的宗族身份同步到他所在的小家庭（原版 <c>Family</c>）。
    ///
    ///     见 <see cref="FamilyIdentitySyncRules"/> 里对「为什么小家庭 ≠ 宗族」
    ///     的说明。这里只负责取数与落名，判断逻辑都在规则层。
    /// </summary>
    internal static class FamilyIdentitySyncService
    {
        /// <summary>
        ///     按某个成员的氏/姓重命名小家庭。<paramref name="pAnchor"/> 一般是
        ///     建家者；取不到氏/姓就什么都不做，让原版随机名留着。
        /// </summary>
        internal static void SyncFamilyName(Family pFamily, Actor pAnchor)
        {
            if (pFamily?.data == null || pFamily.isRekt() ||
                pAnchor?.data == null) return;
            try
            {
                string identity = ResolveIdentity(pAnchor);
                if (!FamilyIdentitySyncRules.ShouldAdoptLineageName(
                        UsesLineageSystem(pAnchor), identity)) return;
                string desired =
                    FamilyIdentitySyncRules.ResolveFamilyName(identity);
                if (!FamilyIdentitySyncRules.ShouldRewriteName(
                        pFamily.data.name, desired)) return;
                pFamily.setName(desired);
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "[AW3] 小家庭名同步失败: " + error.Message);
            }
        }

        /// <summary>
        ///     把某人的氏同步到他所属的原版 <c>Clan</c>。
        ///
        ///     原版 Clan 语义上就是宗族，我们在归档写入、王室认定、继承、
        ///     颜色/旗帜多处读它，但它的名字走 <c>generateName(MetaType.Clan)</c>
        ///     随机生成，从不认我们的氏。
        ///
        ///     只有族长能改名 —— 普通族人改氏不该把整个宗族改名。
        /// </summary>
        internal static void SyncClanName(Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt()) return;
            try
            {
                Clan clan = pActor.clan;
                if (clan?.data == null || clan.isRekt()) return;
                bool isChief = clan.getChief() == pActor;
                string identity = ResolveIdentity(pActor);
                if (!FamilyIdentitySyncRules.ShouldAdoptClanName(
                        UsesLineageSystem(pActor), isChief, identity)) return;
                string desired =
                    FamilyIdentitySyncRules.ResolveFamilyName(identity);
                if (!FamilyIdentitySyncRules.ShouldRewriteName(
                        clan.data.name, desired)) return;
                clan.setName(desired);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("[AW3] 宗族名同步失败: " + error.Message);
            }
        }

        /// <summary>
        ///     小家庭已有成员里挑一个当命名锚点。优先建家者，其次家长，
        ///     最后退回任意在世成员 —— 建家者可能已经死了。
        /// </summary>
        internal static Actor ResolveAnchor(Family pFamily)
        {
            if (pFamily?.data == null || pFamily.isRekt()) return null;
            try
            {
                Actor founder = pFamily.getFounderFirst();
                if (IsUsableAnchor(founder)) return founder;
                founder = pFamily.getFounderSecond();
                if (IsUsableAnchor(founder)) return founder;
                Actor alpha = pFamily.getAlpha();
                if (IsUsableAnchor(alpha)) return alpha;
                foreach (Actor unit in pFamily.units)
                    if (IsUsableAnchor(unit)) return unit;
            }
            catch { }
            return null;
        }

        private static bool IsUsableAnchor(Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt()) return false;
            return !string.IsNullOrWhiteSpace(ResolveIdentity(pActor));
        }

        /// <summary>
        ///     这个人的「氏」。复用手动改名那条路的解析,两处口径一致。
        /// </summary>
        private static string ResolveIdentity(Actor pActor)
        {
            if (pActor?.data == null) return string.Empty;
            pActor.data.get(LineageKeys.CLAN_NAME, out string clan,
                string.Empty);
            pActor.data.get(LineageKeys.FAMILY_NAME, out string family,
                string.Empty);
            pActor.data.get(LineageKeys.CHINESE_FAMILY_NAME,
                out string chineseFamily, string.Empty);
            pActor.data.get(AWNameDataKeys.FamilyComponent,
                out string localizedFamily, string.Empty);
            ActorManualNameMode mode =
                ActorManualRenameService.ResolveMode(pActor);
            return ActorManualRenameRules.ResolveFamilyIdentity(mode, clan,
                family, chineseFamily, localizedFamily);
        }

        private static bool UsesLineageSystem(Actor pActor)
        {
            try
            {
                return LineageService.IsXia(pActor) ||
                       LineageService.UsesAwLineageSystem(pActor);
            }
            catch { return false; }
        }
    }
}
