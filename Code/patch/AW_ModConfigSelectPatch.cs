using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.ui;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.performance;
using AncientWarfare3.ui.components;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_ModConfigSelectPatch
    {
        private const string ModeId = "AW3_ARMY_RTS_WAR_RESOLUTION_MODE";
        private const string FramePrioritySchedulerId =
            "AW3_ENABLE_FRAME_PRIORITY_SCHEDULER";
        private const int ModeCount = 3;
        private const float SelectAreaWidth = 170f;
        private const float SelectAreaHeight = 46f;
        private const float InfoHeight = 18f;
        private const float OptionsWidth = 170f;
        private const float OptionsHeight = 22f;
        private const float OptionButtonWidth = 24f;
        private const float OptionValueWidth = 114f;
        private const float OptionSpacing = 4f;

        private static MethodBase TargetMethod()
        {
            Type windowType = AccessTools.TypeByName(
                "NeoModLoader.ui.ModConfigureWindow");
            Type itemType = windowType?.GetNestedType(
                "ModConfigListItem", BindingFlags.NonPublic);
            return itemType == null
                ? null
                : AccessTools.Method(itemType, "Setup",
                    new[] { typeof(ModConfigItem) });
        }

        [HarmonyPrefix]
        private static bool Prefix(object __instance, ModConfigItem pItem)
        {
            if (pItem == null || pItem.Type != ConfigItemType.SELECT)
                return true;
            bool isMode = string.Equals(pItem.Id, ModeId,
                StringComparison.Ordinal);
            bool isFont = string.Equals(pItem.Id,
                HierarchicalVassalMapFontSettings.OptionId,
                StringComparison.Ordinal);
            if (!isMode && !isFont) return true;
            try
            {
                if (isFont) SetupFontSelect(__instance, pItem);
                else SetupSelect(__instance, pItem);
                return false;
            }
            catch (Exception pException)
            {
                Debug.LogWarning("AW3 SELECT setup failed: " +
                                 pException.Message);
                return true;
            }
        }

        [HarmonyPostfix]
        private static void Postfix(object __instance, ModConfigItem pItem)
        {
            if (pItem == null || pItem.Type != ConfigItemType.SWITCH ||
                !string.Equals(pItem.Id, FramePrioritySchedulerId,
                    StringComparison.Ordinal)) return;
            try { WireProtectedSchedulerSwitch(__instance, pItem); }
            catch (Exception exception)
            {
                Debug.LogWarning("AW3 protected scheduler switch setup failed: " +
                                 exception.Message);
            }
        }

        private static void WireProtectedSchedulerSwitch(object pInstance,
            ModConfigItem pItem)
        {
            if (pInstance == null || pItem == null) return;
            GameObject switchArea = GetArea(pInstance.GetType(), pInstance,
                "switch_area");
            NeoModLoader.General.UI.Prefabs.SwitchButton switchButton =
                switchArea?.transform.Find("Button")?.GetComponent<
                    NeoModLoader.General.UI.Prefabs.SwitchButton>();
            if (switchButton == null) return;
            RefreshProtectedSchedulerSwitch(switchButton, pItem);
        }

        private static void RefreshProtectedSchedulerSwitch(
            NeoModLoader.General.UI.Prefabs.SwitchButton pSwitch,
            ModConfigItem pItem)
        {
            if (pSwitch == null || pItem == null) return;
            bool current = pItem.BoolVal;
            pSwitch.icon.sprite = SpriteTextureLoader.getSprite(current
                ? "ui/icons/iconOn"
                : "ui/icons/iconOff");
            pSwitch.text.text = current ? LM.Get("short_on") : LM.Get("short_off");
            pSwitch.button.onClick.RemoveAllListeners();
            pSwitch.button.onClick.AddListener(new UnityAction(() =>
            {
                bool requested = !pItem.BoolVal;
                if (!FramePrioritySchedulerConfirmationRules.RequiresConfirmation(
                        pItem.BoolVal, requested))
                {
                    ApplyProtectedSwitchValue(pItem, requested);
                    RefreshProtectedSchedulerSwitch(pSwitch, pItem);
                    return;
                }

                Transform parent = ModConfigureWindow.Instance?.transform;
                FramePrioritySchedulerConfirmDialog.Show(parent, () =>
                {
                    ApplyProtectedSwitchValue(pItem, true);
                    if (pSwitch != null)
                        RefreshProtectedSchedulerSwitch(pSwitch, pItem);
                });
            }));
        }

        private static void ApplyProtectedSwitchValue(ModConfigItem pItem,
            bool pValue)
        {
            if (pItem == null || pItem.BoolVal == pValue) return;
            MarkModified(pItem);
            pItem.SetValue(pValue, true);
        }

        private static void SetupSelect(object pInstance,
            ModConfigItem pItem)
        {
            SetupIndexedSelect(pInstance, pItem, ModeCount,
                pIndex => (int)core.lineage.ArmyRtsWarDoctrineRules.
                    Normalize(pIndex),
                pIndex =>
                {
                    MarkModified(pItem);
                    pItem.SetValue(pIndex, true);
                });
        }

        private static void SetupFontSelect(object pInstance,
            ModConfigItem pItem)
        {
            int fontCount = Math.Max(1,
                HierarchicalVassalMapFontSettings.FontCount);
            int selected = HierarchicalVassalMapFontRules.ClampIndex(
                pItem.IntVal, fontCount);
            HierarchicalVassalMapFontSettings.SelectFont(selected);
            Transform selectTransform = PrepareSelectArea(pInstance, pItem);
            if (selectTransform == null) return;
            HideLegacyFontCycleControls(selectTransform);
            AWFontDropdown dropdown = AWFontDropdown.Create(
                selectTransform, "FontDropdown", OptionsWidth, OptionsHeight,
                pIndex =>
                {
                    MarkModified(pItem);
                    pItem.SetValue(pIndex, true);
            }, pPopupOffsetX: 80f);
            dropdown?.Refresh();
            AddTooltip(selectTransform, pItem.Id);
            RebuildSelectLayout(selectTransform);
        }

        private static void SetupIndexedSelect(object pInstance,
            ModConfigItem pItem, int pItemCount, Func<int, int> pNormalize,
            Action<int> pApply)
        {
            Transform selectTransform = PrepareSelectArea(pInstance, pItem);
            if (selectTransform == null) return;

            Button previous = FindOrCreateButton(selectTransform,
                "Previous", "<", OptionButtonWidth);
            Text value = FindOrCreateText(selectTransform, "Value",
                OptionValueWidth, OptionsHeight);
            value.alignment = TextAnchor.MiddleCenter;
            Button next = FindOrCreateButton(selectTransform, "Next",
                ">", OptionButtonWidth);

            Action<int> setIndex = pIndex =>
            {
                int normalized = pNormalize(pIndex);
                pApply(normalized);
                value.text = SafeLocalized(pItem.Id + " Option " +
                                            normalized);
            };
            previous.onClick.RemoveAllListeners();
            previous.onClick.AddListener(new UnityAction(() =>
                setIndex(pItem.IntVal <= 0 ? pItemCount - 1 :
                    pItem.IntVal - 1)));
            next.onClick.RemoveAllListeners();
            next.onClick.AddListener(new UnityAction(() =>
                setIndex((pItem.IntVal + 1) % pItemCount)));
            value.text = SafeLocalized(pItem.Id + " Option " +
                                        pNormalize(pItem.IntVal));

            AddTooltip(selectTransform, pItem.Id);
            RebuildSelectLayout(selectTransform);
        }

        private static Transform PrepareSelectArea(object pInstance,
            ModConfigItem pItem)
        {
            if (pInstance == null || pItem == null) return null;
            Type itemType = pInstance.GetType();
            GameObject switchArea = GetArea(itemType, pInstance,
                "switch_area");
            GameObject sliderArea = GetArea(itemType, pInstance,
                "slider_area");
            GameObject textArea = GetArea(itemType, pInstance,
                "text_area");
            GameObject selectArea = GetArea(itemType, pInstance,
                "select_area");
            if (switchArea != null) switchArea.SetActive(false);
            if (sliderArea != null) sliderArea.SetActive(false);
            if (textArea != null) textArea.SetActive(false);
            if (selectArea == null) return null;
            selectArea.SetActive(true);

            Transform selectTransform = selectArea.transform;
            DisableLayoutComponents(selectTransform);
            ApplyLayoutSize(selectTransform, SelectAreaWidth,
                SelectAreaHeight);

            VerticalLayoutGroup areaLayout = selectTransform.GetComponent<
                VerticalLayoutGroup>();
            if (areaLayout == null) areaLayout = selectTransform.gameObject.
                AddComponent<VerticalLayoutGroup>();
            areaLayout.enabled = true;
            areaLayout.childControlWidth = false;
            areaLayout.childControlHeight = false;
            areaLayout.childForceExpandWidth = false;
            areaLayout.childForceExpandHeight = false;
            areaLayout.childAlignment = TextAnchor.UpperCenter;
            areaLayout.spacing = OptionSpacing;
            areaLayout.padding = new RectOffset(0, 0, 0, 0);

            Transform infoTransform = selectTransform.Find("Info");
            Text title = selectTransform.Find("Info/Text")?.GetComponent<Text>();
            Transform options = selectTransform.Find("Options");
            ClearChildrenExcept(selectTransform, infoTransform, options);

            if (infoTransform == null)
            {
                GameObject infoObject = new GameObject("Info",
                    typeof(RectTransform), typeof(HorizontalLayoutGroup),
                    typeof(LayoutElement));
                infoObject.transform.SetParent(selectTransform, false);
                infoTransform = infoObject.transform;
            }
            infoTransform.SetSiblingIndex(0);
            DisableLayoutComponents(infoTransform);
            ApplyLayoutSize(infoTransform, OptionsWidth, InfoHeight);
            HorizontalLayoutGroup infoLayout = infoTransform.GetComponent<
                HorizontalLayoutGroup>();
            if (infoLayout == null) infoLayout = infoTransform.gameObject.
                AddComponent<HorizontalLayoutGroup>();
            infoLayout.enabled = true;
            infoLayout.childControlWidth = false;
            infoLayout.childControlHeight = false;
            infoLayout.childForceExpandWidth = false;
            infoLayout.childForceExpandHeight = false;
            infoLayout.childAlignment = TextAnchor.MiddleLeft;
            infoLayout.spacing = 0f;

            Transform icon = infoTransform.Find("Icon");
            if (icon == null)
            {
                GameObject iconObject = new GameObject("Icon",
                    typeof(RectTransform), typeof(Image));
                iconObject.transform.SetParent(infoTransform, false);
                icon = iconObject.transform;
            }
            ApplyLayoutSize(icon, 16f, 16f);
            icon.gameObject.SetActive(false);

            if (title == null)
                title = FindOrCreateText(infoTransform, "Text",
                    OptionsWidth, InfoHeight);
            ApplyLayoutSize(title.transform, OptionsWidth, InfoHeight);
            title.gameObject.SetActive(true);
            title.text = SafeLocalized(pItem.Id);
            title.alignment = TextAnchor.MiddleLeft;
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.verticalOverflow = VerticalWrapMode.Truncate;
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 1;
            title.resizeTextMaxSize = 10;

            if (options == null)
            {
                GameObject optionsObject = new GameObject("Options",
                    typeof(RectTransform), typeof(HorizontalLayoutGroup),
                    typeof(LayoutElement));
                optionsObject.transform.SetParent(selectTransform, false);
                options = optionsObject.transform;
            }
            options.SetSiblingIndex(1);
            DisableLayoutComponents(options);
            ApplyLayoutSize(options, OptionsWidth, OptionsHeight);
            ClearChildren(options);
            HorizontalLayoutGroup optionsLayout = options.GetComponent<
                HorizontalLayoutGroup>();
            if (optionsLayout == null) optionsLayout = options.gameObject.
                AddComponent<HorizontalLayoutGroup>();
            optionsLayout.enabled = true;
            optionsLayout.childControlWidth = false;
            optionsLayout.childControlHeight = false;
            optionsLayout.childForceExpandWidth = false;
            optionsLayout.childForceExpandHeight = false;
            optionsLayout.childAlignment = TextAnchor.MiddleCenter;
            optionsLayout.spacing = OptionSpacing;
            return options;
        }

        private static void ClearChildrenExcept(Transform pParent,
            Transform pKeepA, Transform pKeepB)
        {
            if (pParent == null) return;
            for (int index = pParent.childCount - 1; index >= 0; index--)
            {
                Transform child = pParent.GetChild(index);
                if (child == pKeepA || child == pKeepB) continue;
                child.SetParent(null, false);
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        private static void DisableLayoutComponents(Transform pArea)
        {
            if (pArea == null) return;
            LayoutGroup[] groups = pArea.GetComponents<LayoutGroup>();
            for (int index = 0; index < groups.Length; index++)
                groups[index].enabled = false;
            ContentSizeFitter fitter = pArea.GetComponent<
                ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
        }

        private static void ApplyLayoutSize(Transform pTransform,
            float pWidth, float pHeight)
        {
            if (pTransform == null) return;
            RectTransform rect = pTransform.GetComponent<RectTransform>();
            if (rect == null) return;
            rect.localScale = Vector3.one;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            float width = Mathf.Max(1f, pWidth);
            float height = Mathf.Max(1f, pHeight);
            rect.sizeDelta = new Vector2(width, height);

            LayoutElement layout = pTransform.GetComponent<LayoutElement>();
            if (layout == null)
                layout = pTransform.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = false;
            layout.minWidth = width;
            layout.preferredWidth = width;
            layout.flexibleWidth = 0f;
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleHeight = 0f;
        }

        private static void RebuildSelectLayout(Transform pOptions)
        {
            RectTransform options = pOptions?.GetComponent<RectTransform>();
            RectTransform area = options?.parent as RectTransform;
            if (area == null) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(area);
            RectTransform item = area.parent as RectTransform;
            if (item != null) LayoutRebuilder.MarkLayoutForRebuild(item);
        }

        private static void HideLegacyFontCycleControls(Transform pArea)
        {
            if (pArea == null) return;
            string[] names = { "Previous", "Next", "Value" };
            for (int index = 0; index < names.Length; index++)
            {
                Transform child = pArea.Find(names[index]);
                if (child != null) child.gameObject.SetActive(false);
            }
        }

        private static void ClearChildren(Transform pParent)
        {
            if (pParent == null) return;
            for (int index = pParent.childCount - 1; index >= 0; index--)
            {
                Transform child = pParent.GetChild(index);
                child.SetParent(null, false);
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        private static GameObject GetArea(Type pItemType, object pInstance,
            string pName)
        {
            FieldInfo field = pItemType.GetField(pName,
                BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
            return field?.GetValue(pInstance) as GameObject;
        }

        private static Button FindOrCreateButton(Transform pParent,
            string pName, string pText, float pWidth)
        {
            Transform child = pParent.Find(pName);
            Button button = child?.GetComponent<Button>();
            if (button == null)
            {
                GameObject item = new GameObject(pName,
                    typeof(RectTransform), typeof(Image), typeof(Button));
                item.transform.SetParent(pParent, false);
                button = item.GetComponent<Button>();
                item.GetComponent<RectTransform>().sizeDelta =
                    new Vector2(pWidth, 22f);
                Image image = item.GetComponent<Image>();
                image.sprite = SpriteTextureLoader.getSprite(
                    "ui/special/button");
                button.targetGraphic = image;
                Text text = FindOrCreateText(item.transform, "Text",
                    pWidth, 22f);
            }
            ApplyLayoutSize(button.transform, pWidth, OptionsHeight);
            Text label = button.transform.Find("Text")?.GetComponent<Text>();
            if (label != null)
            {
                label.text = pText;
                label.alignment = TextAnchor.MiddleCenter;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.verticalOverflow = VerticalWrapMode.Truncate;
            }
            button.gameObject.SetActive(true);
            return button;
        }

        private static Text FindOrCreateText(Transform pParent,
            string pName, float pWidth, float pHeight)
        {
            Transform child = pParent.Find(pName);
            Text text = child?.GetComponent<Text>();
            if (text != null)
            {
                if (text.font == null) text.font = ResolveFont(text);
                ApplyLayoutSize(text.transform, pWidth, pHeight);
                return text;
            }
            GameObject item = new GameObject(pName,
                typeof(RectTransform), typeof(Text));
            item.transform.SetParent(pParent, false);
            ApplyLayoutSize(item.transform, pWidth, pHeight);
            text = item.GetComponent<Text>();
            text.font = ResolveFont(text);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 1;
            text.resizeTextMaxSize = 10;
            text.raycastTarget = false;
            return text;
        }

        private static Font ResolveFont(Text pReference)
        {
            if (pReference?.font != null) return pReference.font;
            try
            {
                Font current = LocalizedTextManager.current_font;
                if (current != null) return current;
            }
            catch { }
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        private static void MarkModified(ModConfigItem pItem)
        {
            PropertyInfo instanceProperty = typeof(ModConfigureWindow).
                GetProperty("Instance", BindingFlags.Public |
                            BindingFlags.Static);
            object window = instanceProperty?.GetValue(null, null);
            if (window == null) return;
            FieldInfo field = typeof(ModConfigureWindow).GetField(
                "_modifiedItems", BindingFlags.Instance |
                BindingFlags.NonPublic);
            IDictionary modified = field?.GetValue(window) as IDictionary;
            if (modified != null && !modified.Contains(pItem))
                modified.Add(pItem, pItem.GetValue());
        }

        private static void AddTooltip(Transform pArea, string pId)
        {
            Type tooltipType = AccessTools.TypeByName(
                "NeoModLoader.General.UI.Prefabs.TooltipButton");
            if (tooltipType == null) return;
            Transform cursor = pArea;
            while (cursor != null)
            {
                Component tip = cursor.GetComponentInChildren(tooltipType,
                    true);
                if (tip != null)
                {
                    AccessTools.Field(tip.GetType(), "textOnClick")?.SetValue(
                        tip, pId);
                    AccessTools.Field(tip.GetType(), "text_description_2")?.
                        SetValue(tip, pId + " Description");
                    return;
                }
                cursor = cursor.parent;
            }
        }

        private static string SafeLocalized(string pKey)
        {
            try
            {
                string value = LM.Get(pKey);
                return string.IsNullOrWhiteSpace(value) || value == pKey
                    ? pKey
                    : value;
            }
            catch { return pKey; }
        }
    }

    [HarmonyPatch(typeof(ModConfigureWindow),
        nameof(ModConfigureWindow.ShowWindow))]
    internal static class AW_ModConfigWindowTitlePatch
    {
        private const string WarningKey =
            "aw_settings_experimental_warning";
        private const string WarningFallback =
            "Experimental; may freeze the game. Avoid if concerned.";
        private static bool _defaultsCaptured;
        private static Vector2 _defaultSize;
        private static int _defaultFontSize;
        private static bool _defaultResizeTextForBestFit;
        private static int _defaultResizeTextMinSize;
        private static int _defaultResizeTextMaxSize;
        private static HorizontalWrapMode _defaultHorizontalOverflow;
        private static VerticalWrapMode _defaultVerticalOverflow;
        private static bool _defaultSupportRichText;

        [HarmonyPostfix]
        private static void Postfix(ModConfig pConfig)
        {
            ModConfigureWindow configureWindow = ModConfigureWindow.Instance;
            ScrollWindow scrollWindow =
                configureWindow?.GetComponent<ScrollWindow>();
            Text title = scrollWindow?.titleText;
            if (title == null) return;

            CaptureDefaults(title);
            RestoreDefaults(title);
            title.text = SafeLocalized("ModConfigure Title",
                "Mod Configuration");

            ModConfig aw3Config = ModClass.Instance?.GetConfig();
            if (!ReferenceEquals(pConfig, aw3Config)) return;

            RectTransform titleRect = title.GetComponent<RectTransform>();
            if (titleRect != null)
                titleRect.sizeDelta = new Vector2(
                    Mathf.Max(_defaultSize.x, 250f),
                    Mathf.Max(_defaultSize.y, 34f));
            title.supportRichText = true;
            title.resizeTextForBestFit = false;
            title.horizontalOverflow = HorizontalWrapMode.Wrap;
            title.verticalOverflow = VerticalWrapMode.Overflow;
            title.text = AncientWarfare3.ui.AW_L10n.Text(
                             "aw_settings_btn", "AW3 Settings") +
                         "\n<size=6><color=#C94B3C>" +
                         AncientWarfare3.ui.AW_L10n.Text(WarningKey,
                             WarningFallback) + "</color></size>";
        }

        private static void CaptureDefaults(Text pTitle)
        {
            if (_defaultsCaptured) return;
            RectTransform titleRect = pTitle.GetComponent<RectTransform>();
            _defaultSize = titleRect == null
                ? Vector2.zero
                : titleRect.sizeDelta;
            _defaultFontSize = pTitle.fontSize;
            _defaultResizeTextForBestFit = pTitle.resizeTextForBestFit;
            _defaultResizeTextMinSize = pTitle.resizeTextMinSize;
            _defaultResizeTextMaxSize = pTitle.resizeTextMaxSize;
            _defaultHorizontalOverflow = pTitle.horizontalOverflow;
            _defaultVerticalOverflow = pTitle.verticalOverflow;
            _defaultSupportRichText = pTitle.supportRichText;
            _defaultsCaptured = true;
        }

        private static void RestoreDefaults(Text pTitle)
        {
            RectTransform titleRect = pTitle.GetComponent<RectTransform>();
            if (titleRect != null) titleRect.sizeDelta = _defaultSize;
            pTitle.fontSize = _defaultFontSize;
            pTitle.resizeTextForBestFit = _defaultResizeTextForBestFit;
            pTitle.resizeTextMinSize = _defaultResizeTextMinSize;
            pTitle.resizeTextMaxSize = _defaultResizeTextMaxSize;
            pTitle.horizontalOverflow = _defaultHorizontalOverflow;
            pTitle.verticalOverflow = _defaultVerticalOverflow;
            pTitle.supportRichText = _defaultSupportRichText;
        }

        private static string SafeLocalized(string pKey, string pFallback)
        {
            try
            {
                string value = LM.Get(pKey);
                return string.IsNullOrWhiteSpace(value) || value == pKey
                    ? pFallback
                    : value;
            }
            catch
            {
                return pFallback;
            }
        }
    }
}
