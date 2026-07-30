using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using UnityEngine;

namespace AncientWarfare3.core.presentation
{
    internal static class ArmyRtsAttackSpeechBubbleService
    {
        private const string AssetId = "aw_army_rts_attack_speech";

        private sealed class ActiveBubble
        {
            public Actor Captain;
            public long CaptainId;
            public double ExpiresAt;
            public string Text;
        }

        private static readonly System.Random Rng = new System.Random();
        private static readonly ArmyRtsAttackSpeechBubbleLedger Ledger =
            new ArmyRtsAttackSpeechBubbleLedger();
        private static readonly List<ActiveBubble> Active =
            new List<ActiveBubble>(
                ArmyRtsAttackSpeechBubbleRules.MaximumActiveBubbles);

        private static QuantumSpriteAsset _asset;
        private static double _activePlayTime;
        private static double _nextScanTime;
        private static int _visibleCaptainScanCursor;
        private static int _visibleSoldierScanCursor;
        private static ArmyRtsAttackSpeechLine _lastLine =
            ArmyRtsAttackSpeechLine.None;
        private static bool _drawFailed;

        public static void RegisterAsset()
        {
            QuantumSpriteLibrary library = AssetManager.quantum_sprites;
            if (library == null) return;
            QuantumSpriteAsset existing = library.getSimple(AssetId);
            if (existing != null)
            {
                _asset = existing;
                return;
            }

            _asset = library.add(new QuantumSpriteAsset
            {
                id = AssetId,
                id_prefab = "p_mapArmy",
                base_scale = 0.16f,
                add_camera_zoom_multiplier = false,
                render_gameplay = true,
                render_map = false,
                default_amount =
                    ArmyRtsAttackSpeechBubbleRules.MaximumActiveBubbles,
                draw_call = DrawBubbles,
                create_object = ConfigureBubble
            });
        }

        public static void ProcessFrame()
        {
            if (!Config.game_loaded || SmoothLoader.isLoading() ||
                World.world?.units?.visible_units_with_banner == null ||
                World.world.units.visible_units_alive == null)
            {
                ClearActive();
                return;
            }

            if (ArmyRtsAttackSpeechBubbleRules.ShouldSuppressPresentation(
                    MapBox.isRenderMiniMap()))
            {
                ClearActive();
                _asset?.group_system?.clearFull();
                return;
            }

            bool talkBubblesEnabled =
                PlayerConfig.optionBoolEnabled("talk_bubbles");
            if (!talkBubblesEnabled)
            {
                ClearActive();
                return;
            }

            bool paused = World.world.isPaused();
            _activePlayTime = ArmyRtsAttackSpeechBubbleRules.
                AdvanceActivePlayTime(_activePlayTime,
                    Time.unscaledDeltaTime, worldLoaded: true, paused);
            if (paused) return;

            double now = _activePlayTime;
            Prune(now);
            if (now < _nextScanTime || Active.Count >=
                ArmyRtsAttackSpeechBubbleRules.MaximumActiveBubbles) return;
            _nextScanTime = now +
                ArmyRtsAttackSpeechBubbleRules.ScanIntervalSeconds;

            ActorVisibleDataArray visibleCaptains =
                World.world.units.visible_units_with_banner;
            ScanCaptains(visibleCaptains, talkBubblesEnabled, now);
            if (Active.Count <
                ArmyRtsAttackSpeechBubbleRules.MaximumActiveBubbles)
                ScanSoldiers(World.world.units.visible_units_alive,
                    talkBubblesEnabled, now);
        }

        private static void ScanCaptains(ActorVisibleDataArray pVisible,
            bool pTalkBubblesEnabled, double pNow)
        {
            int visibleCount = pVisible.count;
            int count = Math.Min(visibleCount,
                ArmyRtsAttackSpeechBubbleRules.MaximumVisibleCaptainsScanned);
            Actor[] actors = pVisible.array;
            int visitedCount = 0;
            for (int offset = 0; offset < count && Active.Count <
                 ArmyRtsAttackSpeechBubbleRules.MaximumActiveBubbles; offset++)
            {
                int index = ArmyRtsAttackSpeechBubbleRules.VisibleScanIndex(
                    _visibleCaptainScanCursor, offset, visibleCount);
                if (index < 0 || index >= actors.Length) break;
                visitedCount++;
                TryObserveCaptain(actors[index], pTalkBubblesEnabled, pNow);
            }
            _visibleCaptainScanCursor = ArmyRtsAttackSpeechBubbleRules.
                AdvanceVisibleScanCursor(_visibleCaptainScanCursor,
                    visitedCount, visibleCount);
        }

        private static void ScanSoldiers(ActorVisibleDataArray pVisible,
            bool pTalkBubblesEnabled, double pNow)
        {
            int visibleCount = pVisible.count;
            int count = Math.Min(visibleCount,
                ArmyRtsAttackSpeechBubbleRules.MaximumVisibleCaptainsScanned);
            Actor[] actors = pVisible.array;
            int visitedCount = 0;
            for (int offset = 0; offset < count && Active.Count <
                 ArmyRtsAttackSpeechBubbleRules.MaximumActiveBubbles; offset++)
            {
                int index = ArmyRtsAttackSpeechBubbleRules.VisibleScanIndex(
                    _visibleSoldierScanCursor, offset, visibleCount);
                if (index < 0 || index >= actors.Length) break;
                visitedCount++;
                TryObserveSoldier(actors[index], pTalkBubblesEnabled, pNow);
            }
            _visibleSoldierScanCursor = ArmyRtsAttackSpeechBubbleRules.
                AdvanceVisibleScanCursor(_visibleSoldierScanCursor,
                    visitedCount, visibleCount);
        }

        public static void ClearRuntime()
        {
            ClearActive();
            Ledger.Clear();
            _activePlayTime = 0d;
            _nextScanTime = 0d;
            _visibleCaptainScanCursor = 0;
            _visibleSoldierScanCursor = 0;
            _lastLine = ArmyRtsAttackSpeechLine.None;
            _drawFailed = false;
            _asset?.group_system?.clearFull();
        }

        public static void Shutdown()
        {
            ClearRuntime();
            _asset = null;
        }

        private static void TryObserveCaptain(Actor pCaptain,
            bool pTalkBubblesEnabled, double pNow)
        {
            try
            {
                if (pCaptain?.data == null || pCaptain.army?.data == null)
                    return;
                Army army = pCaptain.army;
                if (!ReferenceEquals(army.getCaptain(), pCaptain) ||
                    !ArmyRtsControllerService.TryGetProjection(army,
                        out ArmyRtsStrategicProjection projection) ||
                    !ArmyRtsControllerService.TryGetMission(army,
                        out ArmyRtsMission mission)) return;

                bool captainAlive = pCaptain.isAlive() &&
                                    !pCaptain.isRekt();
                bool captainInCombat =
                    StandingArmyPeacetimeService.IsInCombat(pCaptain);
                long captainId = pCaptain.data.id;
                if (!ArmyRtsAttackSpeechBubbleRules.IsEligible(
                        pTalkBubblesEnabled, captainAlive, captainId,
                        army.id, mission.WarId, mission.TargetCityId,
                        projection.State, mission.ProposalKind,
                        mission.Role, captainInCombat)) return;

                var eventKey = new ArmyRtsAttackSpeechEventKey(
                    army.id, mission.WarId, mission.TargetCityId,
                    mission.IssuedTime);
                ArmyRtsAttackSpeechContext context = ResolveContext(
                    pCaptain, army, projection.State, mission.ProposalKind,
                    mission.Role);
                if (!Ledger.TryReserve(eventKey, captainId, pNow,
                        Active.Count)) return;
                string text = SelectSpeechText(context,
                    out ArmyRtsAttackSpeechLine selectedLine);
                if (string.IsNullOrEmpty(text)) return;
                _lastLine = selectedLine;

                Active.Add(new ActiveBubble
                {
                    Captain = pCaptain,
                    CaptainId = captainId,
                    ExpiresAt = pNow +
                                ArmyRtsAttackSpeechBubbleRules.
                                    DisplayDurationSeconds,
                    Text = text
                });
            }
            catch
            {
                // A stale visible-unit slot must not disable the frame stage.
            }
        }

        private static void TryObserveSoldier(Actor pSoldier,
            bool pTalkBubblesEnabled, double pNow)
        {
            try
            {
                if (pSoldier?.data == null) return;
                bool alive = pSoldier.isAlive() && !pSoldier.isRekt();
                bool military = pSoldier.isWarrior() || pSoldier.hasArmy();
                bool inCombat = StandingArmyPeacetimeService.IsInCombat(
                    pSoldier);
                if (!ArmyRtsAttackSpeechBubbleRules.
                        IsSoldierCombatEligible(pTalkBubblesEnabled, alive,
                            pSoldier.data.id, military,
                            pSoldier.is_army_captain, inCombat)) return;

                ArmyRtsAttackSpeechContext context = ResolveSoldierContext(
                    pSoldier);
                if (!Ledger.TryReserveCombatant(pSoldier.data.id, pNow,
                        Active.Count)) return;
                string text = SelectSpeechText(context,
                    out ArmyRtsAttackSpeechLine selectedLine);
                if (string.IsNullOrEmpty(text)) return;
                _lastLine = selectedLine;

                Active.Add(new ActiveBubble
                {
                    Captain = pSoldier,
                    CaptainId = pSoldier.data.id,
                    ExpiresAt = pNow + ArmyRtsAttackSpeechBubbleRules.
                        DisplayDurationSeconds,
                    Text = text
                });
            }
            catch
            {
                // Visible actor arrays may retain a stale slot for one frame.
            }
        }

        private static void DrawBubbles(QuantumSpriteAsset pAsset)
        {
            if (_drawFailed) return;
            try
            {
                DrawBubblesCore(pAsset);
            }
            catch (Exception error)
            {
                _drawFailed = true;
                ClearActive();
                ModClass.LogWarning(
                    "AW3 attack speech bubble draw failed and was disabled: " +
                    error);
            }
        }

        private static void DrawBubblesCore(QuantumSpriteAsset pAsset)
        {
            if (World.world == null || pAsset?.group_system == null) return;
            if (ArmyRtsAttackSpeechBubbleRules.ShouldSuppressPresentation(
                    MapBox.isRenderMiniMap()))
            {
                pAsset.group_system.clearFull();
                return;
            }
            if (Active.Count == 0 ||
                !PlayerConfig.optionBoolEnabled("talk_bubbles")) return;
            Sprite background = CommunicationLibrary.normal?.getSpriteBubble();
            if (background == null) return;

            for (int index = 0; index < Active.Count; index++)
            {
                ActiveBubble active = Active[index];
                Actor captain = active.Captain;
                if (!IsDrawable(captain, active.CaptainId) ||
                    string.IsNullOrEmpty(active.Text)) continue;

                QuantumSpriteWithText bubble =
                    pAsset.group_system.getNext() as QuantumSpriteWithText;
                if (bubble?.text == null) continue;
                RefreshTextFont(bubble);
                Vector3 position =
                    captain.getHeadOffsetPositionForFunRendering();
                position.y += 0.55f * captain.current_scale.y;
                position.z = -0.12f;
                float scale = ArmyRtsAttackSpeechBubbleRules.BubbleScaleFor(
                    captain.current_scale.y);
                bubble.set(ref position, scale);
                bubble.setSprite(background);
                bubble.text.gameObject.SetActive(true);
                bubble.text.text = active.Text;
                float textScale =
                    ArmyRtsAttackSpeechBubbleRules.TextScaleFor(active.Text);
                bubble.text.transform.localScale =
                    Vector3.one * textScale;
            }
        }

        private static void ConfigureBubble(QuantumSpriteAsset pAsset,
            QuantumSprite pSprite)
        {
            var bubble = pSprite as QuantumSpriteWithText;
            if (bubble == null) return;
            bubble.initText();
            if (LibraryMaterials.instance?.mat_socialize != null)
                bubble.setSharedMat(LibraryMaterials.instance.mat_socialize);
            if (bubble.sprite_renderer != null)
            {
                bubble.sprite_renderer.drawMode = SpriteDrawMode.Simple;
                bubble.sprite_renderer.sortingOrder = 20;
            }
            if (bubble.text == null) return;

            bubble.text.anchor = TextAnchor.MiddleCenter;
            bubble.text.alignment = TextAlignment.Center;
            bubble.text.characterSize = 0.72f;
            bubble.text.fontSize = 18;
            bubble.text.lineSpacing = 0.72f;
            bubble.text.richText = false;
            bubble.text.color = new Color(0.16f, 0.09f, 0.04f, 1f);
            bubble.text.transform.localPosition = new Vector3(
                ArmyRtsAttackSpeechBubbleRules.TextLocalX,
                ArmyRtsAttackSpeechBubbleRules.TextLocalY, -0.02f);
            bubble.text.transform.localScale = Vector3.one * 1.1f;
            RefreshTextFont(bubble);
            Renderer renderer = bubble.text.GetComponent<Renderer>();
            if (renderer == null) return;
            if (bubble.sprite_renderer != null)
            {
                renderer.sortingLayerID =
                    bubble.sprite_renderer.sortingLayerID;
                renderer.sortingOrder =
                    bubble.sprite_renderer.sortingOrder + 1;
            }
        }

        private static void RefreshTextFont(QuantumSpriteWithText pBubble)
        {
            if (pBubble?.text == null ||
                LocalizedTextManager.current_font == null) return;
            pBubble.text.font = LocalizedTextManager.current_font;
            Renderer renderer = pBubble.text.GetComponent<Renderer>();
            if (renderer != null && pBubble.text.font.material != null)
                renderer.sharedMaterial = pBubble.text.font.material;
        }

        private static bool IsDrawable(Actor pCaptain, long pCaptainId)
        {
            try
            {
                return pCaptain?.data != null &&
                       pCaptain.data.id == pCaptainId &&
                       pCaptain.isAlive() && !pCaptain.isRekt() &&
                       !pCaptain.isInMagnet() &&
                       pCaptain.current_zone?.visible == true;
            }
            catch
            {
                return false;
            }
        }

        private static ArmyRtsAttackSpeechContext ResolveSoldierContext(
            Actor pSoldier)
        {
            try
            {
                Army army = pSoldier?.army;
                if (army?.data != null &&
                    ArmyRtsControllerService.TryGetProjection(army,
                        out ArmyRtsStrategicProjection projection) &&
                    ArmyRtsControllerService.TryGetMission(army,
                        out ArmyRtsMission mission))
                    return ResolveContext(pSoldier, army, projection.State,
                        mission.ProposalKind, mission.Role);
            }
            catch
            {
                // A combatant may lose its army between visible-array scans.
            }
            return ArmyRtsAttackSpeechContext.GeneralCombat;
        }

        private static ArmyRtsAttackSpeechContext ResolveContext(Actor pActor,
            Army pArmy, ArmyRtsState pState,
            ArmyRtsProposalKind pProposalKind, ArmyRtsRole pRole)
        {
            bool activeVoyage = pArmy != null &&
                                ArmyRtsTransportService.HasActiveVoyage(pArmy);
            bool embarked = pActor?.is_inside_boat == true ||
                            pArmy != null &&
                            ArmyRtsTransportService.HasEmbarkedMembers(pArmy);
            return ArmyRtsAttackSpeechBubbleRules.ClassifyContext(pState,
                pProposalKind, pRole, activeVoyage, embarked);
        }

        private static string SelectSpeechText(
            ArmyRtsAttackSpeechContext pContext,
            out ArmyRtsAttackSpeechLine pSelectedLine)
        {
            pSelectedLine =
                ArmyRtsAttackSpeechBubbleRules.SelectLine(pContext,
                    Rng.Next(), _lastLine);
            string key = ArmyRtsAttackSpeechBubbleRules.LocalizationKeyFor(
                pSelectedLine);
            if (string.IsNullOrEmpty(key)) return string.Empty;

            string text = string.Empty;
            try
            {
                text = LocalizedTextManager.getText(key);
                if (text == key) text = string.Empty;
            }
            catch
            {
                text = string.Empty;
            }
            if (string.IsNullOrWhiteSpace(text))
                text = EnglishFallbackFor(pSelectedLine);
            text = ArmyRtsAttackSpeechBubbleRules.FormatText(text);
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text;
        }

        private static string EnglishFallbackFor(
            ArmyRtsAttackSpeechLine pLine)
        {
            switch (pLine)
            {
                case ArmyRtsAttackSpeechLine.LastStand:
                    return "Fight to the last moment; accept death before surrender.";
                case ArmyRtsAttackSpeechLine.GreatAxe:
                    return "My great axe has long thirsted for battle.";
                case ArmyRtsAttackSpeechLine.CalmWisdom:
                    return "Do not be angry; anger dulls my wisdom.";
                case ArmyRtsAttackSpeechLine.Impossible:
                    return "Impossible. Absolutely impossible.";
                case ArmyRtsAttackSpeechLine.CrossingRiver:
                    return "If he crosses the river; so will I.";
                case ArmyRtsAttackSpeechLine.ProudArmy:
                    return "Victorious troops grow proud; proud troops are defeated.";
                case ArmyRtsAttackSpeechLine.NoCourtesy:
                    return "I will show no courtesy.";
                case ArmyRtsAttackSpeechLine.BrotherInvincible:
                    return "My second brother is invincible.";
                case ArmyRtsAttackSpeechLine.XuzhouPass:
                    return "Xuzhou truly is the mightiest pass of the central plains.";
                case ArmyRtsAttackSpeechLine.ThrowOut:
                    return "Throw him out.";
                case ArmyRtsAttackSpeechLine.LookAtYourHead:
                    return "I have my eye on your head.";
                case ArmyRtsAttackSpeechLine.CaoTraitor:
                    return "Cao traitor! Knave! Villain! Rebel!";
                case ArmyRtsAttackSpeechLine.ThreeSurnameSlave:
                    return "How did mighty Lu Bu become a slave of three surnames?";
                case ArmyRtsAttackSpeechLine.FourFathers:
                    return "Lu Bu has four fathers.";
                case ArmyRtsAttackSpeechLine.DongZhuoTraitor:
                    return "The traitor Dong Zhuo.";
                case ArmyRtsAttackSpeechLine.DeathUnclear:
                    return "His fate is unknown; that means he is dead.";
                case ArmyRtsAttackSpeechLine.CommandTable:
                    return "You cannot bear to leave that command table.";
                case ArmyRtsAttackSpeechLine.FiveHundredYears:
                    return "This battle will decide the next five hundred years.";
                case ArmyRtsAttackSpeechLine.NoSecondAmbush:
                    return "There was one ambush; there cannot possibly be another.";
                case ArmyRtsAttackSpeechLine.TenMyriadTroops:
                    return "He will conjure one hundred thousand elite troops at once.";
                case ArmyRtsAttackSpeechLine.XuHuangHeroes:
                    return "Xu Huang is my Han Xin; Bai Qi; and Zhou Yafu.";
                case ArmyRtsAttackSpeechLine.LiuSandao:
                    return "My fierce general Liu Sandao will slay Lu Bu within three blows.";
                case ArmyRtsAttackSpeechLine.PanFeng:
                    return "I have General Pan Feng; he can slay Hua Xiong.";
                case ArmyRtsAttackSpeechLine.DragonTiger:
                    return "Wind follows the tiger; clouds follow the dragon; heroes tower over all.";
                default:
                    return string.Empty;
            }
        }

        private static void Prune(double pNow)
        {
            for (int index = Active.Count - 1; index >= 0; index--)
            {
                ActiveBubble active = Active[index];
                if (ArmyRtsAttackSpeechBubbleRules.IsExpired(
                        active.ExpiresAt, pNow) ||
                    !IsDrawable(active.Captain, active.CaptainId))
                    Active.RemoveAt(index);
            }
        }

        private static void ClearActive()
        {
            Active.Clear();
        }
    }
}
