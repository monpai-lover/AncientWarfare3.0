using System.Collections.Generic;
using System.IO;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     古姓 / 氏 名池。直接读 mod 自带的 name_generators/lib/姓.txt 与 氏.txt,
    ///     不依赖"一米_中文名" mod 的 WordLibraryManager —— 后端血缘逻辑独立可用。
    ///     若文件缺失则退化为内置兜底池,保证 LineageService 不会因取不到姓氏而崩。
    ///
    ///     ⚠ 取样**必须用 mod 私有 System.Random**,不能用 UnityEngine.Random:
    ///     世界生成期 MapBox 会 `Random.InitState(Random.Range(...))` 用引擎默认态重新播种全局 RNG,
    ///     每次冷启动新档得到**同一种子→同一随机序列**→ RandomShi 每档恒取同一下标(实测恒为"慕容")。
    ///     私有 System.Random(默认时钟种子)不受全局 InitState 影响,跨档真随机。
    /// </summary>
    internal static class LineageNamePool
    {
        /// <summary>mod 私有 RNG:用时钟种子,不受 MapBox 的 Random.InitState 固定播种影响。</summary>
        public static readonly System.Random Rng = new System.Random();

        private static List<string> _surnames; // 姓(上古血统姓)
        private static List<string> _shiNames; // 氏(后天族名池)

        // 文件缺失时的兜底(先秦古姓 + 常见氏),保证最小可用。
        private static readonly string[] FALLBACK_SURNAMES =
            { "姬", "姜", "姒", "嬴", "妫", "姚", "妘", "姞", "妊", "风" };

        private static readonly string[] FALLBACK_SHI =
            { "夏后", "有扈", "斟鄩", "彤城", "褒", "费", "杞", "缯", "辛", "冥" };

        private static List<string> Surnames => _surnames ??= LoadOrFallback("姓.txt", FALLBACK_SURNAMES);
        private static List<string> ShiNames => _shiNames ??= LoadOrFallback("氏.txt", FALLBACK_SHI);

        public static string RandomSurname()
        {
            var list = Surnames;
            return list[Rng.Next(list.Count)];
        }

        public static string RandomShi()
        {
            var list = ShiNames;
            return list[Rng.Next(list.Count)];
        }

        private static List<string> LoadOrFallback(string pFileName, string[] pFallback)
        {
            try
            {
                string modPath = ModClass.Instance.GetDeclaration().FolderPath;
                string path = Path.Combine(modPath, "name_generators", "lib", pFileName);
                if (File.Exists(path))
                {
                    var list = new List<string>();
                    foreach (var line in File.ReadAllLines(path))
                    {
                        var w = line.Trim();
                        if (!string.IsNullOrEmpty(w)) list.Add(w);
                    }

                    if (list.Count > 0) return list;
                }
            }
            catch
            {
                // 读文件失败则用兜底池
            }

            return new List<string>(pFallback);
        }
    }
}
