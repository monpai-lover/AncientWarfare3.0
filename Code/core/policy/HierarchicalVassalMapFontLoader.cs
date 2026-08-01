using System;
using System.IO;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal static class HierarchicalVassalMapFontLoader
    {
        private const string BundledFontFolder = "ABPackages";
        private const string BundledFontFile = "aw3_fzbeiwei";
        private const string BundledFontAsset =
            "assets/aw3/fonts/fzbeiwei.ttf";
        private static bool _loadAttempted;
        private static bool _diagnosticWritten;
        private static AssetBundle _loadedBundle;
        private static Font _loadedFont;

        internal static Font TryLoad(int pSize)
        {
            if (_loadAttempted) return _loadedFont;
            _loadAttempted = true;

            string fontPath = string.Empty;
            try
            {
                if (ModClass.Instance?.GetDeclaration() == null)
                {
                    LogFailureOnce("mod declaration unavailable", fontPath,
                        null, pSize);
                    return null;
                }

                string modFolder =
                    ModClass.Instance.GetDeclaration().FolderPath;
                fontPath = Path.Combine(modFolder, BundledFontFolder,
                    BundledFontFile);
                if (!File.Exists(fontPath))
                {
                    LogFailureOnce("font file missing", fontPath, null,
                        pSize);
                    return null;
                }

                _loadedBundle = AssetBundle.LoadFromFile(fontPath);
                if (_loadedBundle == null)
                {
                    LogFailureOnce("AssetBundle.LoadFromFile returned null",
                        fontPath, null, pSize);
                    return null;
                }

                Font font = _loadedBundle.LoadAsset<Font>(BundledFontAsset);
                if (font == null)
                {
                    LogFailureOnce("Font asset is missing from AssetBundle",
                        fontPath, font, pSize);
                    return null;
                }
                if (!font.HasCharacter('\u56fd'))
                {
                    LogFailureOnce("font does not contain U+56FD",
                        fontPath, font, pSize);
                    return null;
                }

                int requestedSize = Math.Max(1, pSize);
                font.RequestCharactersInTexture("\u56fd", requestedSize,
                    FontStyle.Normal);
                CharacterInfo countryGlyph;
                if (!font.GetCharacterInfo('\u56fd', out countryGlyph,
                        requestedSize, FontStyle.Normal))
                {
                    LogFailureOnce("Unity could not materialize U+56FD",
                        fontPath, font, pSize);
                    return null;
                }
                if (font.material == null)
                {
                    LogFailureOnce("warmed font has no material", fontPath,
                        font, pSize);
                    return null;
                }

                _loadedFont = font;
                LogSuccessOnce(fontPath, font, pSize);
                return _loadedFont;
            }
            catch (Exception error)
            {
                LogFailureOnce(error.GetType().Name + ": " + error.Message,
                    fontPath, null, pSize);
                return null;
            }
        }

        private static void LogSuccessOnce(string pFontPath, Font pFont,
            int pRequestedSize)
        {
            if (_diagnosticWritten) return;
            _diagnosticWritten = true;
            ModClass.LogInfo("[AW3 hierarchical map font] loaded path=" +
                             pFontPath + " requested_size=" +
                             Math.Max(1, pRequestedSize) + " name=" +
                             ResolveFontName(pFont) + " families=" +
                             ResolveFontFamilies(pFont));
        }

        private static void LogFailureOnce(string pReason, string pFontPath,
            Font pFont, int pRequestedSize)
        {
            if (_diagnosticWritten) return;
            _diagnosticWritten = true;
            ModClass.LogWarning("[AW3 hierarchical map font] fallback path=" +
                                pFontPath + " requested_size=" +
                                Math.Max(1, pRequestedSize) + " reason=" +
                                pReason + " name=" +
                                ResolveFontName(pFont) + " families=" +
                                ResolveFontFamilies(pFont));
        }

        private static string ResolveFontName(Font pFont)
        {
            try
            {
                return pFont == null || string.IsNullOrWhiteSpace(pFont.name)
                    ? "<none>"
                    : pFont.name;
            }
            catch
            {
                return "<unavailable>";
            }
        }

        private static string ResolveFontFamilies(Font pFont)
        {
            try
            {
                string[] names = pFont?.fontNames;
                return names == null || names.Length == 0
                    ? "<none>"
                    : string.Join(",", names);
            }
            catch
            {
                return "<unavailable>";
            }
        }
    }
}
