using System;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     小家庭（原版 <c>Family</c>）与宗族身份的同步规则。
    ///
    ///     原版 <c>Family.generateName</c>（Family.cs:337）走 <c>NameGenerator</c>
    ///     按物种随机生成家族名，完全不认我们的氏/姓 —— 于是「姬发」成家之后，
    ///     他的小家庭会顶着一个随机洋名，家谱树、城市面板、编年史里两套名字对不上。
    ///
    ///     原版 Family 是**核心家庭**语义：每对夫妻新建一个、有 <c>family_limit</c>
    ///     上限、<c>original_family_1/2</c> 只记一层父辈。所以它对应的是宗族下的
    ///     「房支」，而不是宗族本身 —— 宗族仍由 <see cref="LineageQuery"/> 的
    ///     SQLite 谱系承载。这里只做**显示身份的同步**，不改变原版的分家行为。
    ///
    ///     同步方向是单向的：个人的氏/姓 → 小家庭名。反向不成立，改小家庭名
    ///     不应该动一个人的宗族归属。
    /// </summary>
    internal static class FamilyIdentitySyncRules
    {
        /// <summary>
        ///     小家庭该不该跟随宗族命名。只有在创建者确实有氏/姓时才接管，
        ///     取不到就放行原版随机名 —— 顶着随机名也好过顶着空名。
        /// </summary>
        internal static bool ShouldAdoptLineageName(bool pUsesLineageSystem,
            string pFamilyIdentity)
        {
            return pUsesLineageSystem &&
                   !string.IsNullOrWhiteSpace(pFamilyIdentity);
        }

        /// <summary>
        ///     小家庭的显示名。汉式取「氏」本身（「姬」），不加后缀 ——
        ///     原版在各处显示时会自己按语境拼「家族」「氏」这类词，
        ///     存进去的应当是裸名。
        /// </summary>
        internal static string ResolveFamilyName(string pFamilyIdentity)
        {
            return (pFamilyIdentity ?? string.Empty).Trim();
        }

        /// <summary>
        ///     改名是否需要落库。相同就跳过：<c>setName</c> 会触发脏标记与
        ///     一系列投影刷新，每年重算一次的路径上不能白写。
        /// </summary>
        internal static bool ShouldRewriteName(string pCurrentName,
            string pDesiredName)
        {
            string desired = (pDesiredName ?? string.Empty).Trim();
            if (desired.Length == 0) return false;
            return !string.Equals((pCurrentName ?? string.Empty).Trim(),
                desired, StringComparison.Ordinal);
        }

        /// <summary>
        ///     原版 <c>Clan</c> 该不该跟随我们的氏。
        ///
        ///     <para>
        ///     **已无调用者，保留仅为其单测。** 宗族命名统一走
        ///     <c>LineageService.RenameClanByLeader</c> —— 它拼的是
        ///     「本源城名 + 氏 + 氏」（「乐安国宣氏」），而本类的
        ///     <see cref="ResolveFamilyName"/> 只给裸氏，那是小家庭的规格。
        ///     两条链路都挂在建族路径上（<c>Clan.newClan</c> 与外层
        ///     <c>ClanManager.newClan</c>），后者一旦生效就会把前者的正确
        ///     名字覆写成裸氏。
        ///     </para>
        ///
        ///     <para>
        ///     这里的族长条件曾经恒为假 —— 原版建族路径从不写
        ///     <c>chief_id</c>（<c>setChief</c> 只出现在每帧
        ///     <c>checkMembersForNewChief</c> 和 <c>tryForgetChief</c> 里），
        ///     所以覆盖从未发生，一直是 <c>RenameClanByLeader</c> 独占。
        ///     放宽族长条件反而让覆盖显形，这是它被弃用的原因。
        ///     </para>
        /// </summary>
        internal static bool ShouldAdoptClanName(bool pUsesLineageSystem,
            bool pIsChief, string pClanIdentity)
        {
            return pUsesLineageSystem && pIsChief &&
                   !string.IsNullOrWhiteSpace(pClanIdentity);
        }
    }
}
