using System;
using System.Collections.Generic;
using System.Threading;
using AncientWarfare3.content;

namespace AncientWarfare3.core.presentation
{
    /// <summary>
    ///     一个 Xia 高清贴图目录的不可变配置。路径由登记表规范化后保存。
    /// </summary>
    public sealed class XiaHighResolutionTextureProfile
    {
        public string Path { get; }
        public float ResolutionFactor { get; }
        public bool ScaleAvatar { get; }
        public bool ScaleFrameOffsets { get; }
        public bool Enabled { get; }

        public XiaHighResolutionTextureProfile(string pPath,
            float pResolutionFactor, bool pScaleAvatar,
            bool pScaleFrameOffsets, bool pEnabled)
        {
            if (string.IsNullOrWhiteSpace(pPath))
                throw new ArgumentException("高清贴图路径不能为空。", nameof(pPath));
            if (pResolutionFactor <= 0f ||
                float.IsNaN(pResolutionFactor) ||
                float.IsInfinity(pResolutionFactor))
                throw new ArgumentOutOfRangeException(nameof(pResolutionFactor),
                    "高清贴图倍率必须是大于零的有限数值。");

            Path = XiaHighResolutionTextureRegistry.NormalizePath(pPath);
            if (Path.Length == 0)
                throw new ArgumentException("高清贴图路径不能为空。", nameof(pPath));
            ResolutionFactor = pResolutionFactor;
            ScaleAvatar = pScaleAvatar;
            ScaleFrameOffsets = pScaleFrameOffsets;
            Enabled = pEnabled;
        }
    }

    /// <summary>
    ///     Xia 高清贴图的唯一登记入口。动画容器和头像面板都从这里读取同一 profile。
    /// </summary>
    public static class XiaHighResolutionTextureRegistry
    {
        private const string DefaultKingBodyPath =
            "actors/species/civs/Xia/king";
        private static readonly object RegistrationGate = new object();
        private static Dictionary<string, XiaHighResolutionTextureProfile>
            _profiles = new Dictionary<string, XiaHighResolutionTextureProfile>(
                StringComparer.Ordinal);

        static XiaHighResolutionTextureRegistry()
        {
            Register(DefaultKingBodyPath);
            Register("actors/species/civs/Xia/king_han");
        }

        public static void Register(string pPath, float pResolutionFactor = 4f,
            bool pScaleAvatar = true, bool pScaleFrameOffsets = true,
            bool pEnabled = true)
        {
            string path = NormalizePath(pPath);
            var profile = new XiaHighResolutionTextureProfile(path,
                pResolutionFactor, pScaleAvatar, pScaleFrameOffsets,
                pEnabled);
            lock (RegistrationGate)
            {
                var next = new Dictionary<string,
                    XiaHighResolutionTextureProfile>(_profiles,
                    StringComparer.Ordinal)
                {
                    [path] = profile
                };
                Volatile.Write(ref _profiles, next);
            }
        }

        public static bool SetEnabled(string pPath, bool pEnabled)
        {
            string path = NormalizePath(pPath);
            lock (RegistrationGate)
            {
                if (!_profiles.TryGetValue(path, out
                        XiaHighResolutionTextureProfile current))
                    return false;
                if (current.Enabled == pEnabled) return true;
                var next = new Dictionary<string,
                    XiaHighResolutionTextureProfile>(_profiles,
                    StringComparer.Ordinal)
                {
                    [path] = new XiaHighResolutionTextureProfile(path,
                        current.ResolutionFactor, current.ScaleAvatar,
                        current.ScaleFrameOffsets, pEnabled)
                };
                Volatile.Write(ref _profiles, next);
                return true;
            }
        }

        public static bool TryGet(string pPath,
            out XiaHighResolutionTextureProfile pProfile)
        {
            pProfile = null;
            string path = NormalizePath(pPath);
            if (path.Length == 0) return false;
            return Volatile.Read(ref _profiles).TryGetValue(path,
                out pProfile);
        }

        public static bool IsEnabled(string pPath)
        {
            return TryGet(pPath, out XiaHighResolutionTextureProfile profile) &&
                   profile.Enabled;
        }

        public static float ResolveFactor(string pPath, float pFallback)
        {
            return TryGet(pPath, out XiaHighResolutionTextureProfile profile) &&
                   profile.Enabled
                ? profile.ResolutionFactor
                : pFallback;
        }

        public static bool TryGetAvatarProfile(string pAssetId, bool pIsKing,
            bool pIsBaby, out XiaHighResolutionTextureProfile pProfile)
        {
            pProfile = null;
            if (!pIsKing || pIsBaby ||
                !string.Equals(pAssetId, XiaRace.ID,
                    StringComparison.Ordinal))
                return false;
            return TryGet(DefaultKingBodyPath,
                out pProfile) && pProfile.Enabled && pProfile.ScaleAvatar;
        }

        public static string NormalizePath(string pPath)
        {
            if (string.IsNullOrWhiteSpace(pPath)) return string.Empty;
            return pPath.Trim().Replace('\\', '/').Trim('/');
        }
    }
}
