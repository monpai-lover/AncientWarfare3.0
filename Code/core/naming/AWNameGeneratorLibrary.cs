using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.naming
{
    public static class AWNameGeneratorLibrary
    {
        private static readonly object Gate = new object();
        private static Dictionary<string, AWNameGeneratorAsset> _generators =
            new Dictionary<string, AWNameGeneratorAsset>(StringComparer.Ordinal);

        public static int Count
        {
            get
            {
                lock (Gate) return _generators.Count;
            }
        }

        public static void Initialize(string pDirectory,
            Action<string> pWarning = null)
        {
            IReadOnlyList<AWNameGeneratorAsset> loaded =
                AWNamingResourceLoader.LoadGenerators(pDirectory, pWarning);
            var replacement = new Dictionary<string, AWNameGeneratorAsset>(
                StringComparer.Ordinal);
            foreach (AWNameGeneratorAsset asset in loaded)
            {
                if (asset == null || string.IsNullOrEmpty(asset.Id)) continue;
                replacement[asset.Id] = asset;
            }
            lock (Gate) _generators = replacement;
            foreach (AWNameGeneratorAsset asset in replacement.Values)
                RegisterVanillaStub(asset.Id);
        }

        public static void SubmitDirectoryToLoad(string pDirectory,
            Action<string> pWarning = null)
        {
            foreach (AWNameGeneratorAsset asset in
                     AWNamingResourceLoader.LoadGenerators(pDirectory, pWarning))
                Submit(asset);
        }

        public static void Submit(AWNameGeneratorAsset pAsset)
        {
            if (pAsset == null || string.IsNullOrEmpty(pAsset.Id)) return;
            lock (Gate) _generators[pAsset.Id] = pAsset;
            RegisterVanillaStub(pAsset.Id);
        }

        public static AWNameGeneratorAsset Get(string pId)
        {
            if (string.IsNullOrEmpty(pId)) return null;
            lock (Gate)
                return _generators.TryGetValue(pId,
                    out AWNameGeneratorAsset asset)
                    ? asset
                    : null;
        }

        private static void RegisterVanillaStub(string pId)
        {
            if (AssetManager.name_generator == null ||
                AssetManager.name_generator.dict.ContainsKey(pId))
                return;
            AssetManager.name_generator.add(new NameGeneratorAsset { id = pId });
        }
    }
}
