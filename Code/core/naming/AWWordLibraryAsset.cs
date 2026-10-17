using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.naming
{
    public sealed class AWWordLibraryAsset
    {
        private readonly string[] _words;

        public AWWordLibraryAsset(string pId, IEnumerable<string> pWords)
        {
            Id = (pId ?? string.Empty).Trim();
            _words = (pWords ?? Array.Empty<string>())
                .Select(NormalizeWord)
                .Where(pWord => pWord.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        public string Id { get; }

        public IReadOnlyList<string> Words => _words;

        private static string NormalizeWord(string pWord)
        {
            // Chinese Name's separator library is a single hair-space token.
            // Trimming that token removes the separator from every template.
            return pWord == "\u200a" ? pWord : (pWord ?? string.Empty).Trim();
        }

        internal bool TryPick(ref AWDeterministicNameRandom pRandom,
            out string pWord)
        {
            if (_words.Length == 0)
            {
                pWord = string.Empty;
                return false;
            }

            pWord = _words[pRandom.NextIndex(_words.Length)];
            return true;
        }
    }
}
