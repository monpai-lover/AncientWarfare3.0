using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.ui;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_ModConfigSelectPatch
    {
        private const string ModeId = "AW3_ARMY_RTS_WAR_RESOLUTION_MODE";
        private const int ModeCount = 3;

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
            if (pItem == null || !string.Equals(pItem.Id, ModeId,
                    StringComparison.Ordinal) ||
                pItem.Type != ConfigItemType.SELECT)
                return true;
            try
            {
                SetupSelect(__instance, pItem);
                return false;
            }
            catch (Exception pException)
            {
                Debug.LogWarning("AW3 SELECT setup failed: " +
                                 pException.Message);
                return true;
            }
        }

        private static void SetupSelect(object pInstance,
            ModConfigItem pItem)
        {
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
            if (selectArea == null) return;
            selectArea.SetActive(true);

            HorizontalLayoutGroup layout = selectArea.GetComponent<
                HorizontalLayoutGroup>();
            if (layout == null) layout = selectArea.AddComponent<
                HorizontalLayoutGroup>();
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 4f;

            Text title = FindOrCreateText(selectArea.transform, "Title",
                120f, 18f);
            title.text = SafeLocalized(pItem.Id);
            title.alignment = TextAnchor.MiddleLeft;

            Button previous = FindOrCreateButton(selectArea.transform,
                "Previous", "<", 24f);
            Text value = FindOrCreateText(selectArea.transform, "Value",
                90f, 18f);
            value.alignment = TextAnchor.MiddleCenter;
            Button next = FindOrCreateButton(selectArea.transform, "Next",
                ">", 24f);

            Action<int> setIndex = pIndex =>
            {
                int normalized = (int)core.lineage.
                    ArmyRtsWarDoctrineRules.Normalize(pIndex);
                MarkModified(pItem);
                pItem.SetValue(normalized, true);
                value.text = SafeLocalized(pItem.Id + " Option " +
                                            normalized);
            };
            previous.onClick.RemoveAllListeners();
            previous.onClick.AddListener(new UnityAction(() =>
                setIndex(pItem.IntVal <= 0 ? ModeCount - 1 :
                    pItem.IntVal - 1)));
            next.onClick.RemoveAllListeners();
            next.onClick.AddListener(new UnityAction(() =>
                setIndex((pItem.IntVal + 1) % ModeCount)));
            value.text = SafeLocalized(pItem.Id + " Option " +
                                        (int)core.lineage.
                                            ArmyRtsWarDoctrineRules.Normalize(
                                                pItem.IntVal));

            AddTooltip(selectArea.transform, pItem.Id);
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
                text.text = pText;
                text.alignment = TextAnchor.MiddleCenter;
            }
            return button;
        }

        private static Text FindOrCreateText(Transform pParent,
            string pName, float pWidth, float pHeight)
        {
            Transform child = pParent.Find(pName);
            Text text = child?.GetComponent<Text>();
            if (text != null) return text;
            GameObject item = new GameObject(pName,
                typeof(RectTransform), typeof(Text));
            item.transform.SetParent(pParent, false);
            item.GetComponent<RectTransform>().sizeDelta =
                new Vector2(pWidth, pHeight);
            text = item.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 1;
            return text;
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
            Component tip = pArea.GetComponentInChildren(
                AccessTools.TypeByName("NeoModLoader.General.UI.Prefabs.TooltipButton"));
            if (tip == null) return;
            AccessTools.Field(tip.GetType(), "textOnClick")?.SetValue(tip, pId);
            AccessTools.Field(tip.GetType(), "text_description_2")?.SetValue(
                tip, pId + " Description");
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
}
