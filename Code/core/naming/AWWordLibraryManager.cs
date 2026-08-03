using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.naming
{
    public sealed class AWWordLibraryManager
    {
        private readonly object _gate = new object();
        private Dictionary<string, AWWordLibraryAsset> _libraries =
            new Dictionary<string, AWWordLibraryAsset>(StringComparer.Ordinal);

        public static AWWordLibraryManager Instance { get; } =
            new AWWordLibraryManager();

        public int Count
        {
            get
            {
                lock (_gate) return _libraries.Count;
            }
        }

        public void Submit(string pId, IEnumerable<string> pWords)
        {
            Submit(new AWWordLibraryAsset(pId, pWords));
        }

        public void Submit(AWWordLibraryAsset pAsset)
        {
            if (pAsset == null || string.IsNullOrEmpty(pAsset.Id)) return;
            lock (_gate) _libraries[pAsset.Id] = pAsset;
        }

        public void Append(string pId, IEnumerable<string> pWords)
        {
            if (string.IsNullOrWhiteSpace(pId)) return;
            lock (_gate)
            {
                IEnumerable<string> existing = _libraries.TryGetValue(pId,
                    out AWWordLibraryAsset asset)
                    ? asset.Words
                    : Array.Empty<string>();
                _libraries[pId] = new AWWordLibraryAsset(pId,
                    existing.Concat(pWords ?? Array.Empty<string>()));
            }
        }

        public IReadOnlyList<string> GetWords(string pId)
        {
            if (string.IsNullOrWhiteSpace(pId)) return Array.Empty<string>();
            lock (_gate)
            {
                if (!_libraries.TryGetValue(pId, out AWWordLibraryAsset asset) ||
                    asset == null)
                    return Array.Empty<string>();
                return asset.Words.ToArray();
            }
        }

        public void InstallChineseNameLegacyAliases(Action<string> pWarning = null)
        {
            InstallMergedAlias("阿拉伯名字", "阿拉伯男名", "阿拉伯女名");
            InstallMergedAlias("罗斯名字", "罗斯男名", "罗斯女名");
            InstallMergedAlias("犹太人名", "犹太男名", "犹太女名");
            if (!InstallAlias("日本名字", "日本名字下"))
                pWarning?.Invoke(
                    "AW3 naming compatibility source is missing: 日本名字下.");
            if (!InstallCyclicPairAlias("天干地支", "天干", "地支"))
                pWarning?.Invoke(
                    "AW3 naming compatibility sources are missing: 天干/地支.");
        }

        private bool InstallAlias(string pAlias, string pSource)
        {
            lock (_gate)
            {
                if (_libraries.ContainsKey(pAlias)) return true;
                IEnumerable<string> words = _libraries.TryGetValue(pSource,
                    out AWWordLibraryAsset source)
                    ? source.Words
                    : Array.Empty<string>();
                var alias = new AWWordLibraryAsset(pAlias, words);
                _libraries[pAlias] = alias;
                return alias.Words.Count > 0;
            }
        }

        private bool InstallCyclicPairAlias(string pAlias, string pFirst,
            string pSecond)
        {
            lock (_gate)
            {
                if (_libraries.ContainsKey(pAlias)) return true;
                string[] first = _libraries.TryGetValue(pFirst,
                    out AWWordLibraryAsset firstAsset)
                    ? firstAsset.Words.ToArray()
                    : Array.Empty<string>();
                string[] second = _libraries.TryGetValue(pSecond,
                    out AWWordLibraryAsset secondAsset)
                    ? secondAsset.Words.ToArray()
                    : Array.Empty<string>();
                if (first.Length == 0 || second.Length == 0)
                {
                    _libraries[pAlias] = new AWWordLibraryAsset(pAlias,
                        Array.Empty<string>());
                    return false;
                }

                int count = LeastCommonMultiple(first.Length, second.Length);
                _libraries[pAlias] = new AWWordLibraryAsset(pAlias,
                    Enumerable.Range(0, count)
                        .Select(pIndex => first[pIndex % first.Length] +
                                          second[pIndex % second.Length]));
                return true;
            }
        }

        private static int LeastCommonMultiple(int pFirst, int pSecond)
        {
            int first = pFirst;
            int second = pSecond;
            while (second != 0)
            {
                int remainder = first % second;
                first = second;
                second = remainder;
            }
            return checked(pFirst / first * pSecond);
        }

        private void InstallMergedAlias(string pAlias, string pFirst,
            string pSecond)
        {
            lock (_gate)
            {
                if (_libraries.ContainsKey(pAlias)) return;
                IEnumerable<string> first = _libraries.TryGetValue(pFirst,
                    out AWWordLibraryAsset firstAsset)
                    ? firstAsset.Words
                    : Array.Empty<string>();
                IEnumerable<string> second = _libraries.TryGetValue(pSecond,
                    out AWWordLibraryAsset secondAsset)
                    ? secondAsset.Words
                    : Array.Empty<string>();
                _libraries[pAlias] = new AWWordLibraryAsset(pAlias,
                    first.Concat(second));
            }
        }

        internal bool TryPick(string pId,
            ref AWDeterministicNameRandom pRandom, out string pWord)
        {
            AWWordLibraryAsset asset;
            lock (_gate) _libraries.TryGetValue(pId ?? string.Empty, out asset);
            if (asset != null) return asset.TryPick(ref pRandom, out pWord);
            pWord = string.Empty;
            return false;
        }

        internal void ReplaceAll(IEnumerable<AWWordLibraryAsset> pAssets)
        {
            var replacement = new Dictionary<string, AWWordLibraryAsset>(
                StringComparer.Ordinal);
            foreach (AWWordLibraryAsset asset in pAssets ??
                     Array.Empty<AWWordLibraryAsset>())
            {
                if (asset == null || string.IsNullOrEmpty(asset.Id)) continue;
                replacement[asset.Id] = asset;
            }
            lock (_gate) _libraries = replacement;
        }
    }
}
