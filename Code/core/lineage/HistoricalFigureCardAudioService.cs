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
        // NeoModLoader's wav playback manager. MusicBox.playSound only plays
        // FMOD events (event:/...), so custom .wav files must go through
        // CustomAudioManager.LoadCustomSound instead.
        private const string CustomAudioManagerFullName =
            "NeoModLoader.utils.CustomAudioManager";
        private const string Root = "sounds/historical_cards/";
        private static readonly IReadOnlyDictionary<string, string> SoundPaths =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["aw3_card_button_press"] = "aw3_card_button_press",
                ["aw3_card_item_hover"] = "aw3_card_item_hover",
                ["aw3_card_unlock"] = "aw3_card_unlock",
                ["aw3_card_unlock_immediate"] = "aw3_card_unlock_immediate",
                ["aw3_card_scroll"] = "aw3_card_scroll",
                ["aw3_card_reveal_blue"] = "aw3_card_reveal_blue",
                ["aw3_card_reveal_purple"] = "aw3_card_reveal_purple",
                ["aw3_card_reveal_pink"] = "aw3_card_reveal_pink",
                ["aw3_card_reveal_red"] = "aw3_card_reveal_red",
                ["aw3_card_reveal_gold"] = "aw3_card_reveal_gold"
            };
        private static bool _initialized;
        private static bool _available = true;
        private static bool _enabled = true;
        private static Type _audioManager;
        private static MethodInfo _loadCustomSound;
        private static MethodInfo _libraryContains;
        private static MethodInfo _libraryAdd;
        private static object _library;
        private static Type _wavContainerType;
        private static object _stereo3D;
        private static object _soundType;
        private static string _wavDirectory;
        private static int _loggedPlaybackFailures;
        private static MethodInfo _modifyWavData;
        private static object _basicMode;
        private static object _uiSoundType;
        private static readonly HashSet<string> _uiModeApplied =
            new HashSet<string>(StringComparer.Ordinal);
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

        public static bool Enabled => _enabled;

        public static void SetEnabled(bool pEnabled)
        {
            _enabled = pEnabled;
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
            if (!_enabled || !_available || string.IsNullOrWhiteSpace(pName)) return;
            if (TryPlayViaCustomAudioManager(pName)) return;
            try
            {
                // CustomAudioManager is unavailable: fall back to MusicBox.
                // This only plays FMOD events, so it is a silent no-op for a
                // .wav path, but it keeps the flow safe on loaders without
                // the NeoModLoader audio manager.
                MusicBox.playSound(Root + pName, 0f, 0f, false, false);
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

        private static bool TryPlayViaCustomAudioManager(string pName)
        {
            try
            {
                if (_audioManager == null)
                    ResolveCustomAudioManager();
                if (_audioManager == null || _loadCustomSound == null ||
                    _libraryContains == null || _libraryAdd == null) return false;

                string key = Root + pName;
                // NeoModLoader's ResourcesPatch already walks the mod's
                // GameResources folder at startup and registers every .wav
                // under the key "<relative path without extension>", i.e.
                // exactly "sounds/historical_cards/aw3_card_*". So the normal
                // path is: the key is already there and we just play it.
                // Registering our own container is only a fallback for the
                // case where that scan did not happen; a wrong absolute path
                // would poison the library for the rest of the session, so
                // skip it entirely when the mod folder cannot be resolved.
                if (!(bool)_libraryContains.Invoke(_library,
                        new object[] { key }))
                {
                    if (string.IsNullOrEmpty(_wavDirectory)) return false;
                    RegisterWav(key, _wavDirectory + key + ".wav");
                }
                ForceUiPlaybackMode(key);

                _loadCustomSound.Invoke(null,
                    new object[] { key, 0f, 0f, null });
                return true;
            }
            catch (Exception error)
            {
                // Surface the reason once instead of failing silently: a
                // reflection or FMOD problem here is the difference between
                // "sound plays" and "nothing happens at all".
                if (_loggedPlaybackFailures < 3)
                {
                    _loggedPlaybackFailures++;
                    try
                    {
                        ModClass.LogWarning("Historical card audio '" + pName +
                            "' failed: " + error.Message);
                    }
                    catch { }
                }
                return false;
            }
        }

        /// <summary>
        ///     把这条音效强制成 2D UI 音。
        ///
        ///     <para>
        ///     真凶在 NeoModLoader 的 <c>ResourcesPatch.LoadWavFile</c>:找不到
        ///     同名 .json 时它兜底成
        ///     <c>new WavContainer(abspath, SoundMode.Stereo3D, 50f)</c> ——
        ///     **3D 定位音**。而 <c>LoadCustomSound(key, 0f, 0f, null)</c> 把它
        ///     摆在世界坐标 (0,0)(地图左下角),摄像机在别处时几乎听不见,
        ///     表现就是「音效不对/没声」。开箱界面的音必须是 2D。
        ///     </para>
        ///
        ///     <para>
        ///     wav 旁边的 .json 已经写了 Mode=Basic / Type=UI,这里再用
        ///     <c>ModifyWavData</c> 兜一道:不依赖 json 是否被扫到,也覆盖
        ///     RegisterWav 那条兜底路径。每个 key 只改一次。
        ///     </para>
        /// </summary>
        private static void ForceUiPlaybackMode(string pKey)
        {
            if (_modifyWavData == null || _basicMode == null ||
                _uiSoundType == null || !_uiModeApplied.Add(pKey)) return;
            try
            {
                _modifyWavData.Invoke(null, new object[]
                {
                    pKey, 70f, _basicMode, 0, false, _uiSoundType
                });
            }
            catch { }
        }

        private static void ResolveCustomAudioManager()
        {
            try
            {
                _audioManager = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(p => p.GetType(CustomAudioManagerFullName, false))
                    .FirstOrDefault(p => p != null);
                if (_audioManager == null) return;
                _loadCustomSound = _audioManager.GetMethod("LoadCustomSound",
                    BindingFlags.Public | BindingFlags.Static);
                object library = _audioManager.GetField("AudioWavLibrary",
                    BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
                if (library == null) return;
                _library = library;
                Type libraryType = library.GetType();
                _libraryContains = libraryType.GetMethod("ContainsKey");
                _libraryAdd = libraryType.GetMethod("Add");
                _wavContainerType = _audioManager.Assembly.GetType(
                    "NeoModLoader.utils.WavContainer", false);
                _modifyWavData = _audioManager.GetMethod("ModifyWavData",
                    BindingFlags.Public | BindingFlags.Static);
                Type soundMode = _audioManager.Assembly.GetType(
                    "NeoModLoader.utils.SoundMode", false);
                if (soundMode != null)
                {
                    // Basic = 2D。开箱界面的音不该有空间定位。
                    _basicMode = Enum.Parse(soundMode, "Basic");
                    _stereo3D = _basicMode;
                }
                Type soundType = _audioManager.Assembly.GetType(
                    "NeoModLoader.utils.SoundType", false);
                if (soundType != null)
                {
                    _uiSoundType = Enum.Parse(soundType, "UI");
                    _soundType = _uiSoundType;
                }
                _wavDirectory = ResolveWavDirectory();
            }
            catch { _audioManager = null; }
        }

        private static void RegisterWav(string pKey, string pAbsolutePath)
        {
            if (_wavContainerType == null || _stereo3D == null ||
                _soundType == null) return;
            object container = Activator.CreateInstance(_wavContainerType,
                pAbsolutePath, _stereo3D, 50f, 0, false, _soundType);
            _libraryAdd.Invoke(_library, new[] { pKey, container });
        }

        private static string ResolveWavDirectory()
        {
            try
            {
                // Mod root is exposed by the NeoModLoader mod base class.
                // The library key already carries the "sounds/historical_cards/"
                // segment, so this must stop at GameResources or the segment
                // ends up duplicated and FMOD cannot open the file.
                string folder = ModClass.Instance.GetDeclaration().FolderPath;
                if (string.IsNullOrEmpty(folder)) return "";
                return folder.TrimEnd('\\', '/') + "/GameResources/";
            }
            catch { return ""; }
        }
    }
}
