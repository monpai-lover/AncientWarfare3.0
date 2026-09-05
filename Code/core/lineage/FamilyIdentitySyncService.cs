using System;
using System.Collections.Generic;
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
        private static readonly object Gate = new object();
        private static int _lastWorldIdentity;
        private static bool _completed;

        /// <summary>
        ///     读档后一次性把全世界现有的小家庭和宗族名对齐到谱系。
        ///
        ///     新建事件（<see cref="AncientWarfare3.patch.naming.AW_FamilyIdentityPatch"/>）
        ///     只能覆盖读档之后新产生的对象，存量的旧随机名需要这里做一趟修复。
        ///     遍历 FamilyManager 和 ClanManager 各自的列表，全内存，无查询，
        ///     与 <see cref="ClanMembershipSyncService.RepairAfterWorldLoaded"/> 共用
        ///     同一套防重入设计。
        /// </summary>
        internal static void RepairAfterWorldLoaded()
        {
            if (World.world == null) return;
            int worldIdentity = World.world.GetHashCode();
            lock (Gate)
            {
                if (_completed && _lastWorldIdentity == worldIdentity) return;
            }

            int families = 0;
            int clans = 0;
            try
            {
                families = RepairFamilies();
                clans    = RepairClans();
                lock (Gate)
                {
                    _completed = true;
                    _lastWorldIdentity = worldIdentity;
                }
                if (families > 0 || clans > 0)
                    ModClass.LogInfo("[AW3] 读档小家庭/宗族名修复: 小家庭 " +
                        families + " 个, 宗族 " + clans + " 个");
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "[AW3] 读档小家庭/宗族名修复失败: " + error.Message);
            }
        }

        internal static void ClearRuntime()
        {
            lock (Gate)
            {
                _completed = false;
                _lastWorldIdentity = 0;
            }
        }

        private static int RepairFamilies()
        {
            FamilyManager mgr;
            try { mgr = World.world?.families; }
            catch { return 0; }
            if (mgr == null) return 0;

            var snapshot = new List<Family>();
            try { foreach (Family f in mgr) if (f != null) snapshot.Add(f); }
            catch { return 0; }

            int changed = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                Family family = snapshot[i];
                if (family?.data == null || family.isRekt()) continue;
                try
                {
                    Actor anchor = ResolveAnchor(family);
                    if (anchor == null) continue;
                    string before = family.data.name;
                    SyncFamilyName(family, anchor);
                    if (!string.Equals(family.data.name, before,
                            StringComparison.Ordinal))
                        changed++;
                }
                catch { }
            }
            return changed;
        }

        private static int RepairClans()
        {
            ClanManager mgr;
            try { mgr = World.world?.clans; }
            catch { return 0; }
            if (mgr == null) return 0;

            var snapshot = new List<Clan>();
            try { foreach (Clan c in mgr) if (c != null) snapshot.Add(c); }
            catch { return 0; }

            int changed = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                Clan clan = snapshot[i];
                if (clan?.data == null || clan.isRekt()) continue;
                try
                {
                    // 族长优先;没有族长就退回建族者。原版建族路径不写
                    // chief_id(setChief 只出现在每帧 checkMembersForNewChief
                    // 和 tryForgetChief 里),存量存档里大量宗族直到某帧才有
                    // 族长 —— 只认族长会让这趟修复对它们全部跳过。
                    Actor anchor;
                    try { anchor = clan.getChief(); }
                    catch { anchor = null; }
                    if (anchor?.data == null || anchor.isRekt())
                        anchor = ResolveClanFounder(clan);
                    if (anchor?.data == null) continue;
                    string before = clan.data.name;
                    SyncClanName(anchor);
                    if (!string.Equals(clan.data.name, before,
                            StringComparison.Ordinal))
                        changed++;
                }
                catch { }
            }
            return changed;
        }

        /// <summary>
        ///     宗族的建族者。原版把他记在 <c>founder_actor_id</c> 上,
        ///     这是没有族长时唯一可靠的命名锚点。他可能已经死了 ——
        ///     那就退回族里任意一个有氏的在世成员。
        /// </summary>
        private static Actor ResolveClanFounder(Clan pClan)
        {
            if (pClan?.data == null) return null;
            try
            {
                Actor founder = World.world?.units?.get(
                    pClan.data.founder_actor_id);
                if (IsUsableAnchor(founder)) return founder;
                foreach (Actor unit in pClan.units)
                    if (IsUsableAnchor(unit)) return unit;
            }
            catch { }
            return null;
        }
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
        /// <summary>
        ///     把某人的氏同步到他所属的原版 <c>Clan</c>。
        ///
        ///     <para>
        ///     **委托给 <see cref="LineageService.RenameClanByLeader"/>**,
        ///     不自己拼名字。宗族的显示名是「本源城名 + 氏 + 氏」
        ///     (如「乐安国宣氏」),由 <c>ShiBranchRules.BuildDisplayName</c>
        ///     从 <c>ShiBranchInfo.origin_city_name</c> 拼出;而本类的
        ///     <c>ResolveFamilyName</c> 只给裸氏(「蓟」),那是**小家庭**的规格。
        ///     两者混用会让宗族名退化成裸氏。
        ///     </para>
        ///
        ///     <para>
        ///     历史上这里确实自己拼过名,但因为族长守卫恒为假(原版建族路径
        ///     从不写 <c>chief_id</c>)而从未真正执行,一直是
        ///     <c>RenameClanByLeader</c> 在独占这条路。放宽族长守卫后覆盖才
        ///     显形 —— 玩家看到宗族名从「乐安国宣氏」退化成「蓟」。
        ///     </para>
        ///
        ///     <para>
        ///     夏化文化守卫仍在:<c>RenameClanByLeader</c> 内部走
        ///     <c>ForeignPseudoLineageRules.ShouldRenameInstitutionalClan</c>,
        ///     要求领袖是夏人或王国行夏化制度,其余种族的 clan 保持原版随机名。
        ///     </para>
        /// </summary>
        internal static void SyncClanName(Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt()) return;
            try
            {
                Clan clan = pActor.clan;
                if (clan?.data == null || clan.isRekt()) return;
                LineageService.RenameClanByLeader(clan, pActor);
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
