using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    /// 功绩批量读取 —— 建将领候选表时用。
    ///
    /// <see cref="GeneralService.GetMerit"/> 取的是「actor 上的字段」与
    /// 「GENERAL_STATE 表里那一行」的较大值,后者是一条 <c>WHERE ACTOR_ID=?</c>
    /// 的 SQL。建表要问全国几千人,而资格判定和评分各问一次,于是几千人
    /// 就是几千次往返 —— 按已测的每事务约 1ms 计,这一项本身就够解释
    /// <c>general_refresh</c> 的 191ms。
    ///
    /// 这里改成一条查询把整个王国的功绩读进字典。查询本身也有界:
    /// 只取 <c>MERIT_SCORE &gt; 0</c> 的行 —— 为 0 的人在字典里查不到,
    /// 回退到 actor 字段即可,答案不变。
    ///
    /// 只在建表那一刻用。之后功绩靠 <c>AwardMerit</c> 换位维护,不再全量读。
    /// </summary>
    internal static class GeneralMeritIndex
    {
        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        private static bool Ready => DB != null &&
            LineageArchiveManager.Instance.InitializeSuccessful;

        /// <summary>
        /// 读这个王国所有有功绩记录的人。查不到的人功绩按 actor 字段算。
        /// 出错返回空字典 —— 那就退回逐人读,慢但正确。
        /// </summary>
        internal static Dictionary<long, int> LoadForKingdom(long pKingdomId)
        {
            var merits = new Dictionary<long, int>();
            if (!Ready || pKingdomId < 0L) return merits;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText =
                    "SELECT ACTOR_ID, MERIT_SCORE FROM " +
                    GeneralStateTableItem.GetTableName() +
                    " WHERE KINGDOM_ID=@k AND MERIT_SCORE>0";
                cmd.Parameters.AddWithValue("@k", pKingdomId);
                using SQLiteDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;
                    long actorId = Convert.ToInt64(reader.GetValue(0));
                    int merit = Convert.ToInt32(reader.GetValue(1));
                    if (actorId < 0L || merit <= 0) continue;
                    merits[actorId] = merit;
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("GeneralMeritIndex.LoadForKingdom: " +
                                    error.Message);
                merits.Clear();
            }

            return merits;
        }

        /// <summary>
        /// 批量结果 + actor 字段,取较大值 —— 与 <c>GetMerit</c> 同一口径。
        /// <paramref name="pMerits"/> 为空(读失败)时退回逐人读。
        /// </summary>
        internal static int Merit(Dictionary<long, int> pMerits, Actor pActor)
        {
            if (pActor?.data == null) return 0;
            if (pMerits == null) return GeneralService.GetMerit(pActor);
            pActor.data.get(LineageKeys.GENERAL_MERIT, out int stored, 0);
            return pMerits.TryGetValue(pActor.data.id, out int indexed) &&
                   indexed > stored
                ? indexed
                : stored;
        }
    }
}
