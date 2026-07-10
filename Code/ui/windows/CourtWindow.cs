using System.Collections.Generic;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal class CourtWindow : AbstractWindow<CourtWindow>
    {
        private const float ROW_X = 8f;
        private const float ROW_H = 18f;
        private const float HEADER_H = 22f;
        private const float CONTENT_BOTTOM = 18f;

        private static long _kingdomId = -1;

        public static void Open(long pKingdomId)
        {
            _kingdomId = pKingdomId;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.COURT);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.COURT,
                () => { if (Instance != null) Instance.Refresh(); });
        }

        protected override void Init()
        {
            ScrollWindow sw = GetComponent<ScrollWindow>();
            if (sw?.titleText != null)
                sw.titleText.text = AW_L10n.Text("aw_court_title", "Court of the Hundred Schools");

            EnsureContentMask();
        }

        // 给内容视口挂遮罩，把正文裁剪在窗体框内，避免文字溢出到左侧游戏工具栏上。
        private void EnsureContentMask()
        {
            Transform viewport = ContentTransform != null ? ContentTransform.parent : null;
            if (viewport == null) return;
            if (viewport.GetComponent<RectMask2D>() == null)
                viewport.gameObject.AddComponent<RectMask2D>();
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        private void Refresh()
        {
            long benchmark = UpdateAgeBenchmark.Begin();
            try
            {
                ClearContent();
                Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
                if (kingdom?.data == null || kingdom.isRekt())
                {
                    AddText("Missing", AW_L10n.Text("aw_policy_no_kingdom", "Kingdom missing"),
                        0f, HEADER_H, 12, Color.white);
                    SetContentHeight(HEADER_H + CONTENT_BOTTOM);
                    return;
                }

                CourtSnapshot snapshot = CourtService.GetSnapshot(kingdom);
                bool policyEnabled = KingdomPolicyService.IsPolicyEnabledForKingdom(kingdom);
                bool official = policyEnabled && CourtService.HasOfficialCourt(kingdom);
                string tier = CourtService.ResolveTier(kingdom);
                string title = !policyEnabled
                    ? AW_L10n.Text("aw_court_button_locked", "Court Locked")
                    : official
                    ? TierName(tier)
                    : AW_L10n.Text("aw_court_button_primitive", "Primitive Council");

                Color kColor = KingdomColor(kingdom);
                float y = 0f;
                AddText("Header", kingdom.name + " - " + title, y, HEADER_H, 12, kColor);
                y += HEADER_H + 4f;

                AddText("Mode", AW_L10n.Text("aw_policy_status", "Status") + ": " + title,
                    y, ROW_H, 10, Color.white);
                y += ROW_H;

                if (!policyEnabled)
                {
                    AddText("LockedDesc", AW_L10n.Text("aw_policy_disabled", "Policy system disabled") + "\n" +
                                          AW_L10n.Text("aw_court_tooltip_cached",
                                              "Court data uses cached refresh and does not scan every actor when opened."),
                        y, ROW_H * 3f, 10, new Color(0.78f, 0.82f, 0.88f, 1f));
                    SetContentHeight(y + ROW_H * 3f + CONTENT_BOTTOM);
                    return;
                }

                AddText("Dominant", AW_L10n.Text("aw_court_dominant_school", "Dominant School") + ": " +
                                    SchoolName(snapshot.dominant_school),
                    y, ROW_H, 10, Color.white);
                y += ROW_H;
                AddText("Efficiency", AW_L10n.Text("aw_court_efficiency", "Court Efficiency") + ": " +
                                     Mathf.FloorToInt(snapshot.efficiency),
                    y, ROW_H, 10, Color.white);
                y += ROW_H + 6f;

                AddText("FactionHeader", AW_L10n.Text("aw_court_faction_composition", "Faction Composition"),
                    y, HEADER_H, 11, new Color(1f, 0.88f, 0.55f, 1f));
                y += HEADER_H;

                List<string> factionRows = BuildFactionRows(snapshot.faction_cache);
                if (factionRows.Count == 0)
                {
                    AddText("NoFaction", AW_L10n.Text("aw_court_no_officer", "Vacant"),
                        y, ROW_H, 10, new Color(0.78f, 0.78f, 0.78f, 1f));
                    y += ROW_H;
                }
                else
                {
                    for (int i = 0; i < factionRows.Count; i++)
                    {
                        AddText("Faction" + i, factionRows[i], y, ROW_H, 10, Color.white);
                        y += ROW_H;
                    }
                }

                if (official)
                {
                    y += 6f;
                    y = AddOfficerSection(kingdom, y);
                    y = AddBureauSection(kingdom, y);
                }

                y += 6f;
                AddText("CacheNote", AW_L10n.Text("aw_court_tooltip_cached",
                        "Court data uses cached refresh and does not scan every actor when opened."),
                    y, ROW_H * 2f, 9, new Color(0.78f, 0.82f, 0.88f, 1f));
                SetContentHeight(y + ROW_H * 2f + CONTENT_BOTTOM);
            }
            finally
            {
                UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomCourtUiBuildIndex, benchmark);
            }
        }

        private void ClearContent()
        {
            if (ContentTransform == null) return;
            for (int i = ContentTransform.childCount - 1; i >= 0; i--)
            {
                Transform child = ContentTransform.GetChild(i);
                if (child != null) Destroy(child.gameObject);
            }
        }

        private void AddText(string pName, string pText, float pY, float pHeight, int pSize, Color pColor)
        {
            if (ContentTransform == null) return;
            GameObject obj = new GameObject(pName, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(ContentTransform, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(ROW_X, -pY - pHeight);
            rect.offsetMax = new Vector2(-ROW_X, -pY);

            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = TextAnchor.UpperLeft;
            text.color = pColor;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = pText ?? "";
        }

        private void SetContentHeight(float pHeight)
        {
            RectTransform rect = ContentTransform != null ? ContentTransform.GetComponent<RectTransform>() : null;
            if (rect == null) return;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, Mathf.Max(pHeight, 80f));
        }

        private float AddOfficerSection(Kingdom pKingdom, float pY)
        {
            AddText("OfficerHeader", AW_L10n.Text("aw_court_central_officers", "Central Officers"),
                pY, HEADER_H, 11, new Color(1f, 0.88f, 0.55f, 1f));
            pY += HEADER_H;

            List<CourtOfficerView> officers = CourtService.GetActiveOfficers(pKingdom, 16);
            if (officers.Count == 0)
            {
                AddText("NoOfficer", AW_L10n.Text("aw_court_no_officer", "Vacant"),
                    pY, ROW_H, 10, new Color(0.78f, 0.78f, 0.78f, 1f));
                return pY + ROW_H;
            }

            for (int i = 0; i < officers.Count; i++)
            {
                CourtOfficerView o = officers[i];
                string line = OfficeName(o.office_id) + " - " + o.actor_name +
                              " (" + SchoolName(o.school_id) + ")";
                AddClickableText("Officer" + i, line, pY, ROW_H, 10, Color.white, o.actor_id);
                pY += ROW_H;
            }
            return pY;
        }

        private float AddBureauSection(Kingdom pKingdom, float pY)
        {
            List<CityBureauView> bureaus = CourtService.GetCityBureaus(pKingdom, 12);
            if (bureaus.Count == 0) return pY;

            pY += 6f;
            AddText("BureauHeader", AW_L10n.Text("aw_court_local_bureaus", "Local Bureaus"),
                pY, HEADER_H, 11, new Color(1f, 0.88f, 0.55f, 1f));
            pY += HEADER_H;

            for (int i = 0; i < bureaus.Count; i++)
            {
                CityBureauView b = bureaus[i];
                string line = b.city_name + " - " +
                              AW_L10n.Text("aw_court_office_slots", "Slots") + " " + b.office_slots + ", " +
                              SchoolName(b.local_school) + ", " +
                              AW_L10n.Text("aw_court_efficiency", "Court Efficiency") + " " + Mathf.FloorToInt(b.efficiency);
                AddText("Bureau" + i, line, pY, ROW_H, 10, Color.white);
                pY += ROW_H;
            }
            return pY;
        }

        private static string OfficeName(string pOfficeId)
        {
            return AW_L10n.Text("aw_court_office_" + (pOfficeId ?? ""), pOfficeId ?? "");
        }

        private static string TierName(string pTier)
        {
            switch (pTier)
            {
                case CourtTier.SanShengLiuBu:
                    return AW_L10n.Text("aw_court_tier_sanshengliubu", "Three Departments and Six Ministries");
                case CourtTier.SanGongJiuQing:
                    return AW_L10n.Text("aw_court_tier_sangongjiuqing", "Three Excellencies and Nine Ministers");
                default:
                    return AW_L10n.Text("aw_court_button_primitive", "Primitive Council");
            }
        }

        private static Color KingdomColor(Kingdom pKingdom)
        {
            string hex = HistoryColors.FromKingdom(pKingdom);
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color c))
                return new Color(c.r, c.g, c.b, 1f);
            return Color.white;
        }

        // 与 AddText 相同,但让该行可点击打开对应人物窗口(仿 EmpireCraft 的可点官员)。
        private void AddClickableText(string pName, string pText, float pY, float pHeight, int pSize,
            Color pColor, long pActorId)
        {
            AddText(pName, pText, pY, pHeight, pSize, pColor);
            if (pActorId < 0 || ContentTransform == null) return;
            Transform t = ContentTransform.Find(pName);
            if (t == null) return;
            Text txt = t.GetComponent<Text>();
            if (txt != null) txt.raycastTarget = true;
            Button btn = t.gameObject.GetComponent<Button>() ?? t.gameObject.AddComponent<Button>();
            btn.onClick.RemoveAllListeners();
            long id = pActorId;
            btn.onClick.AddListener(() =>
            {
                Actor actor = World.world?.units?.get(id);
                if (actor != null && !actor.isRekt()) ActionLibrary.openUnitWindow(actor);
            });
        }

        private static List<string> BuildFactionRows(string pEncoded)
        {
            Dictionary<string, float> values = CourtStateCodec.DecodeFactionCache(pEncoded);
            var rows = new List<string>();
            foreach (KeyValuePair<string, float> pair in values)
                rows.Add(SchoolName(pair.Key) + ": " + pair.Value.ToString("0.0"));
            rows.Sort();
            return rows;
        }

        private static string SchoolName(string pSchool)
        {
            switch (pSchool)
            {
                case CourtSchoolId.Ru: return AW_L10n.Text("aw_court_school_ru", "Ru School");
                case CourtSchoolId.Legalist: return AW_L10n.Text("aw_court_school_fa", "Legalist School");
                case CourtSchoolId.Dao: return AW_L10n.Text("aw_court_school_dao", "Daoist School");
                case CourtSchoolId.Mohist: return AW_L10n.Text("aw_court_school_mo", "Mohist School");
                case CourtSchoolId.Military: return AW_L10n.Text("aw_court_school_bing", "Military School");
                case CourtSchoolId.Diplomat: return AW_L10n.Text("aw_court_school_zongheng", "Diplomat School");
                case CourtSchoolId.Agrarian: return AW_L10n.Text("aw_court_school_nong", "Agrarian School");
                case CourtSchoolId.YinYang: return AW_L10n.Text("aw_court_school_yinyang", "Yin-Yang School");
                case CourtSchoolId.Logician: return AW_L10n.Text("aw_court_school_ming", "Logician School");
                case CourtSchoolId.PrimitiveMinister: return AW_L10n.Text("aw_court_primitive_title", "Primitive Council");
                default: return AW_L10n.Text("aw_policy_idle", "Idle");
            }
        }
    }
}
