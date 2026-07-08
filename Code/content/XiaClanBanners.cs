using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AncientWarfare3.core.lineage;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.content
{
    internal static class XiaClanBanners
    {
        private const string BACKGROUND_PREFIX = "actors/species/civs/Xia/clans/backgrounds/clan_background_";
        private const string ICON_PREFIX = "actors/species/civs/Xia/clans/icons/clan_icon_";
        private const string FRAME_PATH = "actors/species/civs/Xia/clans/clan_frame";
        private const int BACKGROUND_COUNT = 17;
        private const int ICON_COUNT = 22;

        private static readonly List<int> _backgroundIndices = new List<int>();
        private static readonly List<int> _iconIndices = new List<int>();
        private static readonly ConditionalWeakTable<ClanBanner, FrameState> _frameStates =
            new ConditionalWeakTable<ClanBanner, FrameState>();

        private static Sprite _frameSprite;
        private static FrameState _sharedDefaultFrame;

        public static void Init()
        {
            BannerAsset main = AssetManager.clan_banners_library?.main;
            if (main == null) return;

            _backgroundIndices.Clear();
            _iconIndices.Clear();
            AppendRange(main.backgrounds, BACKGROUND_PREFIX, BACKGROUND_COUNT, _backgroundIndices);
            AppendRange(main.icons, ICON_PREFIX, ICON_COUNT, _iconIndices);
        }

        public static void ApplyToClan(Clan pClan, Actor pFounder)
        {
            if (pClan?.data == null || pFounder?.data == null) return;
            if (!LineageService.IsXia(pFounder)) return;

            if (_backgroundIndices.Count == 0 || _iconIndices.Count == 0) Init();
            if (_backgroundIndices.Count > 0)
                pClan.data.banner_background_id = Pick(_backgroundIndices);
            if (_iconIndices.Count > 0)
                pClan.data.banner_icon_id = Pick(_iconIndices);
        }

        public static void ApplyFrameToBanner(ClanBanner pBanner)
        {
            if (pBanner == null) return;
            Image frameImage = pBanner.part_frame ??
                               pBanner.transform.FindRecursive("Frame")?.GetComponent<Image>();
            if (frameImage == null) return;
            Sprite frame = GetFrameSprite();
            FrameState state = _frameStates.GetOrCreateValue(pBanner);
            CaptureDefaultFrame(state, frameImage, frame);
            Clan clan = pBanner.GetNanoObject() as Clan;
            bool isXiaClan = IsXiaClan(clan);

            if (ClanBannerFrameRules.ShouldApplyXiaFrame(isXiaClan, frame != null))
            {
                frameImage.sprite = frame;
                frameImage.enabled = true;
                frameImage.color = Color.white;
                return;
            }

            if (ClanBannerFrameRules.ShouldRestoreDefaultFrame(isXiaClan, state.HasDefault))
                RestoreDefaultFrame(frameImage, state);
        }

        private static bool IsXiaClan(Clan pClan)
        {
            if (pClan?.data == null) return false;
            return pClan.data.original_actor_asset == LineageService.XIA_ASSET_ID ||
                   pClan.data.creator_species_id == LineageService.XIA_ASSET_ID;
        }

        private static Sprite GetFrameSprite()
        {
            if (_frameSprite == null)
                _frameSprite = SpriteTextureLoader.getSprite(FRAME_PATH);
            return _frameSprite;
        }

        private static void CaptureDefaultFrame(FrameState pState, Image pFrameImage, Sprite pXiaFrame)
        {
            if (pState == null || pFrameImage == null) return;
            Sprite current = pFrameImage.sprite;
            bool currentIsXiaFrame = pXiaFrame != null && current == pXiaFrame;
            if (!ClanBannerFrameRules.ShouldCacheDefaultFrame(current != null, currentIsXiaFrame, pState.HasDefault))
            {
                if (!pState.HasDefault && currentIsXiaFrame && _sharedDefaultFrame != null)
                    pState.CopyFrom(_sharedDefaultFrame);
                return;
            }

            pState.Capture(pFrameImage);
            if (_sharedDefaultFrame == null)
            {
                _sharedDefaultFrame = new FrameState();
                _sharedDefaultFrame.CopyFrom(pState);
            }
        }

        private static void RestoreDefaultFrame(Image pFrameImage, FrameState pState)
        {
            if (pFrameImage == null || pState == null || !pState.HasDefault) return;
            pFrameImage.sprite = pState.DefaultSprite;
            pFrameImage.enabled = pState.DefaultEnabled;
            pFrameImage.color = pState.DefaultColor;
        }

        private static void AppendRange(List<string> pTarget, string pPrefix, int pCount, List<int> pIndices)
        {
            if (pTarget == null) return;
            for (int i = 0; i < pCount; i++)
            {
                string path = pPrefix + i;
                int index = pTarget.IndexOf(path);
                if (index < 0)
                {
                    pTarget.Add(path);
                    index = pTarget.Count - 1;
                }
                pIndices.Add(index);
            }
        }

        private static int Pick(List<int> pIndices)
        {
            return pIndices[LineageNamePool.Rng.Next(pIndices.Count)];
        }

        private sealed class FrameState
        {
            public Sprite DefaultSprite;
            public Color DefaultColor = Color.white;
            public bool DefaultEnabled = true;
            public bool HasDefault;

            public void Capture(Image pFrameImage)
            {
                DefaultSprite = pFrameImage.sprite;
                DefaultColor = pFrameImage.color;
                DefaultEnabled = pFrameImage.enabled;
                HasDefault = DefaultSprite != null;
            }

            public void CopyFrom(FrameState pOther)
            {
                if (pOther == null || !pOther.HasDefault) return;
                DefaultSprite = pOther.DefaultSprite;
                DefaultColor = pOther.DefaultColor;
                DefaultEnabled = pOther.DefaultEnabled;
                HasDefault = true;
            }
        }
    }
}
