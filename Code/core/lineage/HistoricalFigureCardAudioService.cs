using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AncientWarfare3.content.figures;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    /// Presentation-only audio boundary for the card flow. MusicBox is the
    /// stable WorldBox mod API and applies the game's sound settings.
    /// </summary>
    public static class HistoricalFigureCardAudioService
    {
        // Kept as a compatibility marker for loaders that provide this manager.
        private const string CustomAudioManager = "CustomAudioManager";
        private const string Root = "historical_cards/";
        private static bool _initialized;
        private static bool _available = true;
        private static readonly IReadOnlyDictionary<string, string> RevealSounds =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["blue"] = "aw3_card_reveal_blue",
                ["purple"] = "aw3_card_reveal_purple",
                ["pink"] = "aw3_card_reveal_pink",
                ["red"] = "aw3_card_reveal_red",
                ["gold"] = "aw3_card_reveal_gold"
            };

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _available = true;
        }

        public static void PlayButtonPress() => Play("aw3_card_button_press");
        public static void PlayUnlock() => Play("aw3_card_unlock");
        public static void PlayImmediateUnlock() =>
            Play("aw3_card_unlock_immediate");
        public static void PlayScroll() => Play("aw3_card_scroll");
        public static void PlayItemHover() => Play("aw3_card_item_hover");

        public static void PlayReveal(HistoricalFigureCardRarity pRarity)
        {
            string id = pRarity?.Id ?? "blue";
            if (!RevealSounds.TryGetValue(id, out string sound))
                sound = RevealSounds["blue"];
            Play(sound);
        }

        private static void Play(string pName)
        {
            if (!_initialized) Initialize();
            if (!_available || string.IsNullOrWhiteSpace(pName)) return;
            try
            {
                string path = Root + pName;
                if (TryPlayViaCustomAudioManager(path)) return;
                MusicBox.playSound(path, 0f, 0f, false, false);
            }
            catch (Exception error)
            {
                _available = false;
                try
                {
                    ModClass.LogWarning("Historical card audio disabled: " +
                        error.Message);
                }
                catch { }
            }
        }

        private static bool TryPlayViaCustomAudioManager(string pPath)
        {
            try
            {
                Type manager = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(p => p.GetType(CustomAudioManager, false))
                    .FirstOrDefault(p => p != null);
                MethodInfo play = manager?.GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Static)
                    .FirstOrDefault(p =>
                    {
                        if (p.Name != "Play" && p.Name != "PlaySound" &&
                            p.Name != "playSound") return false;
                        ParameterInfo[] parameters = p.GetParameters();
                        return parameters.Length == 1 &&
                               parameters[0].ParameterType == typeof(string);
                    });
                if (play == null) return false;
                play.Invoke(null, new object[] { pPath });
                return true;
            }
            catch { return false; }
        }
    }
}
