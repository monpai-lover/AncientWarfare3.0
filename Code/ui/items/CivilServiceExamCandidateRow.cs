using System;
using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;
using AncientWarfare3.ui.components;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class CivilServiceExamCandidateRow : MonoBehaviour
    {
        public const float Height = 58f;
        private const float PortraitSize = 42f;

        private Image _background;
        private Button _rowButton;
        private TipButton _tip;
        private GameObject _portraitRoot;
        private UiUnitAvatarElement _livePortrait;
        private Image _archivedPortrait;
        private Text _name;
        private Text _identity;
        private Text _scores;
        private Text _result;
        private Button _moveUp;
        private Button _moveDown;
        private long _actorId = -1L;
        private bool _portraitBound;

        public bool NeedsPortrait => gameObject.activeSelf && !_portraitBound &&
                                     _actorId >= 0L;

        public static CivilServiceExamCandidateRow Create(Transform pParent)
        {
            var root = new GameObject("CivilServiceExamCandidateRow",
                typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(TipButton));
            root.transform.SetParent(pParent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(480f, Height);
            var row = root.AddComponent<CivilServiceExamCandidateRow>();
            row.BuildUi();
            return row;
        }

        public void Bind(CivilServiceExamCandidateView pCandidate,
            int pDisplayPosition, float pWidth, string pVisibleStageResult,
            bool pCanMoveUp,
            bool pCanMoveDown, Action<long, int> pMove)
        {
            ResetBinding();
            if (pCandidate == null)
            {
                gameObject.SetActive(false);
                return;
            }
            gameObject.SetActive(true);
            _actorId = pCandidate.ActorId;
            SetWidth(pWidth);
            AW_UIStyle.ApplyListRow(_background,
                pCandidate.FinalRank > 0 ? .94f : .82f);

            string rank = pCandidate.FinalRank > 0
                ? "#" + pCandidate.FinalRank
                : "#" + Math.Max(1, pDisplayPosition);
            string title = string.IsNullOrEmpty(pCandidate.FinalTitle)
                ? ""
                : "  " + FinalTitle(pCandidate.FinalTitle);
            _name.text = rank + "  " + (pCandidate.ActorName ?? "") + title;
            _name.color = pCandidate.FinalRank > 0 && pCandidate.FinalRank <= 3
                ? new Color(1f, .82f, .38f, 1f)
                : Color.white;

            CourtSchoolDefinition school =
                CourtSchoolRegistry.Find(pCandidate.SchoolId);
            string schoolName = school == null
                ? AW_L10n.Text("aw_civil_service_school_none", "No school")
                : AW_L10n.Text(school.NameKey, school.Id);
            _identity.text = Origin(pCandidate.SocialOrigin) + "  |  " +
                             (pCandidate.HomeCityName ?? "") + "  |  " +
                             schoolName + "  |  " +
                             AW_L10n.Text("aw_civil_service_local_grade",
                                 "Local grade") + " " + pCandidate.LocalGrade;
            _scores.text =
                AW_L10n.Text("aw_civil_service_score_local", "Local") + " " +
                Score(pCandidate.LocalScore) + "    " +
                AW_L10n.Text("aw_civil_service_score_metropolitan", "Metropolitan") + " " +
                Score(pCandidate.MetropolitanScore) + "    " +
                AW_L10n.Text("aw_civil_service_score_palace", "Palace") + " " +
                Score(pCandidate.PalaceScore) + "    " +
                AW_L10n.Text("aw_civil_service_score_national", "National") + " " +
                Score(pCandidate.NationalScore);
            _result.text = Qualification(pCandidate.Qualification) + "\n" +
                           StageResult(pVisibleStageResult ??
                                       pCandidate.StageResult);

            _moveUp.gameObject.SetActive(pCanMoveUp || pCanMoveDown);
            _moveDown.gameObject.SetActive(pCanMoveUp || pCanMoveDown);
            _moveUp.interactable = pCanMoveUp;
            _moveDown.interactable = pCanMoveDown;
            long candidateId = pCandidate.CandidateId;
            if (pCanMoveUp)
                _moveUp.onClick.AddListener(() => pMove?.Invoke(candidateId, -1));
            if (pCanMoveDown)
                _moveDown.onClick.AddListener(() => pMove?.Invoke(candidateId, 1));

            _tip.enabled = true;
            string tipTitle = pCandidate.ActorName ?? "";
            string tipBody = _identity.text + "\n" + _scores.text + "\n" +
                             _result.text;
            _tip.hoverAction = () => Tooltip.show(gameObject,
                AW_RawTooltip.TYPE, new TooltipData
                {
                    tip_name = tipTitle,
                    tip_description = tipBody
                });
            long actorId = pCandidate.ActorId;
            _rowButton.interactable = IsLive(actorId);
            if (_rowButton.interactable)
                _rowButton.onClick.AddListener(() => OpenActor(actorId));
        }

        public bool TryEnsurePortrait()
        {
            if (_portraitBound || _actorId < 0L) return true;
            Actor actor = FindActor(_actorId);
            if (actor?.data != null && actor.isAlive() && !actor.isRekt())
            {
                UiUnitAvatarElement prefab = FamilyTreeNodeView.GetAvatarPrefab();
                if (prefab == null) return false;
                if (_livePortrait == null)
                {
                    _livePortrait = Instantiate(prefab, _portraitRoot.transform);
                    RectTransform liveRect =
                        _livePortrait.GetComponent<RectTransform>();
                    if (liveRect != null) Fill(liveRect);
                }
                _archivedPortrait.gameObject.SetActive(false);
                _livePortrait.gameObject.SetActive(true);
                _livePortrait.enabled = true;
                if (_livePortrait.avatarLoader != null)
                    _livePortrait.avatarLoader.enabled = true;
                _livePortrait.show(actor);
                _portraitBound = true;
                return true;
            }

            Sprite archived = FamilyTreeNodeView.BuildArchivedPortrait(_actorId);
            if (archived == null) return false;
            if (_livePortrait != null) _livePortrait.gameObject.SetActive(false);
            _archivedPortrait.sprite = archived;
            _archivedPortrait.color = new Color(.68f, .68f, .68f, 1f);
            _archivedPortrait.gameObject.SetActive(true);
            _portraitBound = true;
            return true;
        }

        public void Unbind()
        {
            ResetBinding();
            gameObject.SetActive(false);
        }

        private void BuildUi()
        {
            _background = GetComponent<Image>();
            _rowButton = GetComponent<Button>();
            _tip = GetComponent<TipButton>();
            _tip.type = AW_RawTooltip.TYPE;

            _portraitRoot = new GameObject("Portrait", typeof(RectTransform));
            _portraitRoot.transform.SetParent(transform, false);
            RectTransform portraitRect =
                _portraitRoot.GetComponent<RectTransform>();
            portraitRect.anchorMin = portraitRect.anchorMax =
                new Vector2(0f, 1f);
            portraitRect.pivot = new Vector2(0f, 1f);
            portraitRect.anchoredPosition = new Vector2(5f, -8f);
            portraitRect.sizeDelta = new Vector2(PortraitSize, PortraitSize);
            var archivedObject = new GameObject("ArchivedPortrait",
                typeof(RectTransform), typeof(Image));
            archivedObject.transform.SetParent(_portraitRoot.transform, false);
            _archivedPortrait = archivedObject.GetComponent<Image>();
            _archivedPortrait.preserveAspect = true;
            _archivedPortrait.raycastTarget = false;
            Fill(_archivedPortrait.rectTransform);
            archivedObject.SetActive(false);

            _name = CreateText("Name", 10, TextAnchor.UpperLeft);
            _identity = CreateText("Identity", 8, TextAnchor.UpperLeft);
            _identity.color = new Color(.78f, .74f, .64f, 1f);
            _scores = CreateText("Scores", 8, TextAnchor.UpperLeft);
            _scores.color = new Color(.72f, .83f, .88f, 1f);
            _result = CreateText("Result", 8, TextAnchor.MiddleRight);
            _result.color = new Color(.92f, .78f, .42f, 1f);
            _moveUp = CreateMoveButton("MoveUp", 90f);
            _moveDown = CreateMoveButton("MoveDown", -90f);
            SetWidth(480f);
        }

        private void SetWidth(float pWidth)
        {
            float width = Mathf.Max(260f, pWidth);
            GetComponent<RectTransform>().sizeDelta = new Vector2(width, Height);
            Place(_name.rectTransform, 52f, 3f, width - 158f, 16f);
            Place(_identity.rectTransform, 52f, 20f, width - 158f, 14f);
            Place(_scores.rectTransform, 52f, 35f, width - 158f, 18f);
            Place(_result.rectTransform, width - 102f, 5f, 72f, 43f);
            Place(_moveUp.GetComponent<RectTransform>(), width - 26f, 5f,
                22f, 22f);
            Place(_moveDown.GetComponent<RectTransform>(), width - 26f, 31f,
                22f, 22f);
        }

        private Button CreateMoveButton(string pName, float pRotation)
        {
            var buttonObject = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button), typeof(TipButton));
            buttonObject.transform.SetParent(transform, false);
            Image image = buttonObject.GetComponent<Image>();
            AW_UIStyle.ApplyButton(image, .96f);
            Sprite icon = SpriteTextureLoader.getSprite(
                "ui/icons/iconArrowMetaRight");
            if (icon != null) image.sprite = icon;
            image.preserveAspect = true;
            image.transform.localRotation = Quaternion.Euler(0f, 0f,
                pRotation);
            TipButton tip = buttonObject.GetComponent<TipButton>();
            tip.type = AW_RawTooltip.TYPE;
            tip.hoverAction = () => Tooltip.show(buttonObject,
                AW_RawTooltip.TYPE, new TooltipData
                {
                    tip_name = AW_L10n.Text(
                        pRotation > 0f
                            ? "aw_civil_service_rank_move_up"
                            : "aw_civil_service_rank_move_down",
                        pRotation > 0f ? "Move up" : "Move down"),
                    tip_description = AW_L10n.Text(
                        "aw_civil_service_rank_move_desc",
                        "Adjust the palace examination order.")
                });
            return buttonObject.GetComponent<Button>();
        }

        private void ResetBinding()
        {
            _actorId = -1L;
            _portraitBound = false;
            _rowButton.onClick.RemoveAllListeners();
            _moveUp.onClick.RemoveAllListeners();
            _moveDown.onClick.RemoveAllListeners();
            _tip.hoverAction = null;
            if (_livePortrait != null) _livePortrait.gameObject.SetActive(false);
            _archivedPortrait.gameObject.SetActive(false);
        }

        private Text CreateText(string pName, int pSize, TextAnchor pAnchor)
        {
            var textObject = new GameObject(pName, typeof(RectTransform),
                typeof(Text));
            textObject.transform.SetParent(transform, false);
            Text text = textObject.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = pSize;
            text.raycastTarget = false;
            return text;
        }

        private static void Place(RectTransform pRect, float pX, float pY,
            float pWidth, float pHeight)
        {
            pRect.anchorMin = pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, -pY);
            pRect.sizeDelta = new Vector2(Mathf.Max(1f, pWidth), pHeight);
        }

        private static void Fill(RectTransform pRect)
        {
            pRect.anchorMin = Vector2.zero;
            pRect.anchorMax = Vector2.one;
            pRect.offsetMin = pRect.offsetMax = Vector2.zero;
        }

        private static Actor FindActor(long pActorId)
        {
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static bool IsLive(long pActorId)
        {
            Actor actor = FindActor(pActorId);
            return actor?.data != null && actor.isAlive() && !actor.isRekt();
        }

        private static void OpenActor(long pActorId)
        {
            Actor actor = FindActor(pActorId);
            if (actor?.data != null && actor.isAlive() && !actor.isRekt())
                ActionLibrary.openUnitWindow(actor);
        }

        private static string Score(int pScore)
        {
            return pScore < 0 ? "-" : pScore.ToString();
        }

        private static string Origin(string pOrigin)
        {
            string key = pOrigin switch
            {
                "noble" => "aw_civil_service_origin_noble",
                "gentry" => "aw_civil_service_origin_gentry",
                "declined_noble" => "aw_civil_service_origin_declined",
                _ => "aw_civil_service_origin_commoner"
            };
            return AW_L10n.Text(key, pOrigin ?? "commoner");
        }

        private static string Qualification(string pQualification)
        {
            return AW_L10n.Text("aw_civil_service_qualification_" +
                               (pQualification ?? "none"),
                pQualification ?? "none");
        }

        private static string StageResult(string pResult)
        {
            return AW_L10n.Text("aw_civil_service_result_" +
                               (pResult ?? "pending"),
                pResult ?? "pending");
        }

        private static string FinalTitle(string pTitle)
        {
            return AW_L10n.Text("aw_civil_service_rank_" + pTitle,
                pTitle ?? "");
        }
    }
}
