using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.Core;
using Yujian.Player;
using Yujian.Shop;

namespace Yujian.UI
{
    /// <summary>
    /// 建筑栏位（策划案 3.7）。
    ///
    /// 玩家进入建筑区后，屏幕底部（底部导航上方）出现一条横向建筑栏：
    ///   · 建筑按钮横向排列，超出部分被裁剪；
    ///   · 鼠标滚轮横向滚动、按住空白处拖动、拖下方滑块跳转；
    ///   · 每个按钮按 BuildingUI.Evaluate 的结论显示 可建 / 选中 / 未选中 / 灰掉+原因；
    ///   · 原料刚好够时按钮边框微光；
    ///   · 普通 / 特殊 两组之间有一条竖分隔。
    ///
    /// 为什么整个界面是运行时用代码搭的：
    /// 这一条要改的结构太多（Viewport / Content / Scrollbar / 模板 / 分组），
    /// 手拖到场景里既容易漏接线，也没法在代码里一眼看出层级。
    /// 运行时搭出来，场景里只需要挂这一个组件。
    ///
    /// 本类不判定能不能建：判定只在 BuildingUI，它只负责把结论画出来。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class BuildingBarUI : MonoBehaviour
    {
        [Header("依赖（留空会在运行时自动从场景里找）")]
        [Tooltip("建筑入口。提供建筑清单与可建判定")]
        [SerializeField] private BuildingUI buildingUI;

        [Tooltip("建筑管理器。读取「当前选中哪座」与已建数量")]
        [SerializeField] private BuildingManager buildingManager;

        [Tooltip("原料库存。库存变化时刷新按钮状态")]
        [SerializeField] private MaterialInventory inventory;

        [Tooltip("玩家等级。等级变化时刷新按钮状态")]
        [SerializeField] private PlayerLevel playerLevel;

        [Tooltip("里程碑进度。用来把主要建筑归入「特殊」分组")]
        [SerializeField] private MilestoneProgress milestones;

        [Tooltip("区域状态。只在建筑区显示栏位")]
        [SerializeField] private GameAreaController areas;

        [Header("栏位尺寸（Canvas 像素，参考分辨率 1920×1080）")]
        [Tooltip("栏位整体宽高")]
        [SerializeField] private Vector2 barSize = new Vector2(1100f, 186f);

        [Tooltip("距屏幕底部的距离。底部导航高 130，默认再让出 16")]
        [SerializeField] private float bottomOffset = 146f;

        [Tooltip("单个建筑按钮的宽高")]
        [SerializeField] private Vector2 itemSize = new Vector2(168f, 132f);

        [Tooltip("按钮之间的间距")]
        [SerializeField] private float itemSpacing = 12f;

        [Tooltip("栏位四周的内边距")]
        [SerializeField] private float innerPadding = 14f;

        [Tooltip("横向滑块的高度")]
        [SerializeField] private float scrollbarHeight = 12f;

        [Header("文字")]
        [SerializeField] private int nameFontSize = 22;
        [SerializeField] private int statusFontSize = 15;
        [SerializeField] private int groupFontSize = 16;
        [SerializeField] private int emptyFontSize = 20;

        [Header("配色")]
        [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.72f);
        [SerializeField] private Color scrollbarTrackColor = new Color(1f, 1f, 1f, 0.12f);
        [SerializeField] private Color scrollbarHandleColor = new Color(1f, 1f, 1f, 0.45f);
        [SerializeField] private Color dividerColor = new Color(1f, 1f, 1f, 0.22f);
        [SerializeField] private Color mutedTextColor = new Color(0.62f, 0.62f, 0.62f, 1f);

        [Header("调试")]
        [Tooltip("是否在 Console 打印栏位初始化日志")]
        [SerializeField] private bool logFlow = true;

        private RectTransform visual;
        private RectTransform viewport;
        private RectTransform content;
        private ScrollRect scroll;
        private Scrollbar horizontalScrollbar;
        private RectTransform scrollbarRect;
        private Text emptyHint;

        private readonly List<BuildingBarEntry> entries = new List<BuildingBarEntry>();
        private readonly List<int> entryIndexes = new List<int>();
        private readonly List<BuildingData> entryBuildings = new List<BuildingData>();

        private bool built;
        private bool dirty = true;
        private BuildingData lastSelected;
        private bool lastPlacing;
        private bool lastFilling;

        private void Awake()
        {
            if (!(transform is RectTransform))
            {
                Debug.LogError("[BuildingBarUI] 本组件必须挂在带 RectTransform 的 UI 物体上。" +
                               "请用菜单「屿见/建筑/配置建筑栏位（3.7）」创建。", this);
                enabled = false;
                return;
            }

            ResolveDependencies();
            BuildHierarchy();
            ApplyLayout();
        }

        private void OnEnable()
        {
            Subscribe();
            SyncVisibility();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Start()
        {
            BuildEntries();
            MarkDirty();
        }

        private void Update()
        {
            bool placing = buildingManager != null && buildingManager.IsPlacing;
            bool filling = buildingManager != null && buildingManager.IsFilling;
            BuildingData selected = buildingManager != null ? buildingManager.SelectedBuilding : null;

            if (filling != lastFilling)
            {
                lastFilling = filling;
                SyncVisibility();
            }

            if (selected != lastSelected || placing != lastPlacing)
            {
                lastSelected = selected;
                lastPlacing = placing;
                dirty = true;
            }

            if (!dirty)
            {
                return;
            }

            dirty = false;
            Refresh();
        }

        // ---------------- 依赖与订阅 ----------------

        private void ResolveDependencies()
        {
            if (buildingUI == null)
            {
                buildingUI = Object.FindFirstObjectByType<BuildingUI>();
            }

            if (buildingManager == null)
            {
                buildingManager = Object.FindFirstObjectByType<BuildingManager>();
            }

            if (inventory == null)
            {
                inventory = Object.FindFirstObjectByType<MaterialInventory>();
            }

            if (playerLevel == null)
            {
                playerLevel = Object.FindFirstObjectByType<PlayerLevel>();
            }

            if (milestones == null)
            {
                milestones = Object.FindFirstObjectByType<MilestoneProgress>();
            }

            if (areas == null)
            {
                areas = Object.FindFirstObjectByType<GameAreaController>();
            }

            if (buildingUI != null)
            {
                buildingUI.EnsureAvailabilitySources(inventory, playerLevel);
            }

            if (buildingUI == null)
            {
                Debug.LogError("[BuildingBarUI] 场景里找不到 BuildingUI，栏位里不会有任何建筑。", this);
            }

            if (logFlow)
            {
                Debug.Log($"[BuildingBarUI] 已接线：建筑清单 {(buildingUI != null ? buildingUI.AvailableBuildingCount : 0)} 项，" +
                          $"库存 {(inventory != null ? "有" : "缺")}，等级 {(playerLevel != null ? "有" : "缺")}。", this);
            }
        }

        private void Subscribe()
        {
            if (inventory != null)
            {
                inventory.OnInventoryChanged += MarkDirty;
            }

            if (playerLevel != null)
            {
                playerLevel.OnLevelChanged += HandleLevelChanged;
            }

            if (buildingManager != null)
            {
                buildingManager.OnBlueprintClosed += HandleBlueprintClosed;
                buildingManager.OnBuildingRemoved += HandleBuildingRemoved;
            }

            if (milestones != null)
            {
                milestones.OnProgressChanged += MarkDirty;
            }

            if (areas != null)
            {
                areas.OnAreaChanged += HandleAreaChanged;
            }
        }

        private void Unsubscribe()
        {
            if (inventory != null)
            {
                inventory.OnInventoryChanged -= MarkDirty;
            }

            if (playerLevel != null)
            {
                playerLevel.OnLevelChanged -= HandleLevelChanged;
            }

            if (buildingManager != null)
            {
                buildingManager.OnBlueprintClosed -= HandleBlueprintClosed;
                buildingManager.OnBuildingRemoved -= HandleBuildingRemoved;
            }

            if (milestones != null)
            {
                milestones.OnProgressChanged -= MarkDirty;
            }

            if (areas != null)
            {
                areas.OnAreaChanged -= HandleAreaChanged;
            }
        }

        private void HandleLevelChanged(int level)
        {
            MarkDirty();
        }

        private void HandleBlueprintClosed(bool builtSuccessfully)
        {
            MarkDirty();
        }

        private void HandleBuildingRemoved(GameObject instance)
        {
            MarkDirty();
        }

        private void HandleAreaChanged(GameArea area)
        {
            SyncVisibility();

            if (area == GameArea.Building)
            {
                MarkDirty();
            }
        }

        private void MarkDirty()
        {
            dirty = true;
        }

        /// <summary>只在建筑区、且没有在填料时显示；没有配置区域系统时按区域视为一直在。</summary>
        private void SyncVisibility()
        {
            bool inBuildingArea = areas == null || areas.CurrentArea == GameArea.Building;

            // 蓝图已落位、材料面板滑出来的时候把栏位收起来：
            // 材料面板是底部居中的 380×560，和栏位（1100×186）在屏幕底部正中间完全重叠。
            // 这时候玩家在往蓝图里填料，本来也不需要再选建筑。
            bool filling = buildingManager != null && buildingManager.IsFilling;
            bool visible = inBuildingArea && !filling;

            if (visual != null && visual.gameObject.activeSelf != visible)
            {
                visual.gameObject.SetActive(visible);

                if (visible)
                {
                    MarkDirty();
                }
            }
        }

        // ---------------- 搭界面 ----------------

        private void BuildHierarchy()
        {
            if (built)
            {
                return;
            }

            RectTransform root = (RectTransform)transform;

            visual = CreateUIObject("BarVisual", root).GetComponent<RectTransform>();
            Stretch(visual);

            Image background = visual.gameObject.AddComponent<Image>();
            background.sprite = UISprite();
            background.type = Image.Type.Sliced;
            background.color = panelColor;
            background.raycastTarget = true;

            scroll = visual.gameObject.AddComponent<ScrollRect>();

            viewport = CreateUIObject("Viewport", visual).GetComponent<RectTransform>();

            RectMask2D mask = viewport.gameObject.AddComponent<RectMask2D>();
            mask.padding = Vector4.zero;

            content = CreateUIObject("Content", viewport).GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 0.5f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            HorizontalLayoutGroup layout = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = itemSpacing;
            layout.padding = new RectOffset(4, 4, 0, 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            BuildScrollbar();

            emptyHint = CreateText(visual, "EmptyHint", "暂无可用建筑", emptyFontSize,
                                   mutedTextColor, TextAnchor.MiddleCenter);
            Stretch((RectTransform)emptyHint.transform);
            emptyHint.raycastTarget = false;
            emptyHint.gameObject.SetActive(false);

            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.1f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            scroll.scrollSensitivity = 40f;
            scroll.horizontalScrollbar = horizontalScrollbar;
            scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.horizontalScrollbarSpacing = 0f;

            built = true;
        }

        private void BuildScrollbar()
        {
            scrollbarRect = CreateUIObject("Scrollbar", visual).GetComponent<RectTransform>();

            Image track = scrollbarRect.gameObject.AddComponent<Image>();
            track.color = scrollbarTrackColor;
            track.raycastTarget = true;

            RectTransform slidingArea = CreateUIObject("Sliding Area", scrollbarRect).GetComponent<RectTransform>();
            Stretch(slidingArea);

            RectTransform handle = CreateUIObject("Handle", slidingArea).GetComponent<RectTransform>();
            Stretch(handle);

            Image handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = scrollbarHandleColor;
            handleImage.raycastTarget = true;

            horizontalScrollbar = scrollbarRect.gameObject.AddComponent<Scrollbar>();
            horizontalScrollbar.handleRect = handle;
            horizontalScrollbar.targetGraphic = handleImage;
            horizontalScrollbar.direction = Scrollbar.Direction.LeftToRight;
            horizontalScrollbar.size = 1f;
            horizontalScrollbar.transition = Selectable.Transition.ColorTint;

            ColorBlock colors = horizontalScrollbar.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            horizontalScrollbar.colors = colors;
        }

        private void ApplyLayout()
        {
            RectTransform root = (RectTransform)transform;
            root.anchorMin = new Vector2(0.5f, 0f);
            root.anchorMax = new Vector2(0.5f, 0f);
            root.pivot = new Vector2(0.5f, 0f);
            root.anchoredPosition = new Vector2(0f, bottomOffset);
            root.sizeDelta = new Vector2(Mathf.Max(200f, barSize.x), Mathf.Max(80f, barSize.y));

            if (visual != null)
            {
                Stretch(visual);
            }

            if (viewport != null)
            {
                viewport.anchorMin = Vector2.zero;
                viewport.anchorMax = Vector2.one;
                viewport.pivot = new Vector2(0.5f, 0.5f);
                viewport.offsetMin = new Vector2(innerPadding, innerPadding + scrollbarHeight + 6f);
                viewport.offsetMax = new Vector2(-innerPadding, -innerPadding);
            }

            if (scrollbarRect != null)
            {
                scrollbarRect.anchorMin = new Vector2(0f, 0f);
                scrollbarRect.anchorMax = new Vector2(1f, 0f);
                scrollbarRect.pivot = new Vector2(0.5f, 0f);
                scrollbarRect.anchoredPosition = new Vector2(0f, innerPadding * 0.5f);
                scrollbarRect.sizeDelta = new Vector2(-innerPadding * 2f, scrollbarHeight);
            }
        }

        // ---------------- 条目 ----------------

        private void BuildEntries()
        {
            if (content == null)
            {
                return;
            }

            for (int i = content.childCount - 1; i >= 0; i--)
            {
                Destroy(content.GetChild(i).gameObject);
            }

            entries.Clear();
            entryIndexes.Clear();
            entryBuildings.Clear();

            if (buildingUI == null)
            {
                return;
            }

            IReadOnlyList<BuildingData> list = buildingUI.AvailableBuildings;
            int normalCount = 0;
            int specialCount = 0;

            for (int i = 0; i < list.Count; i++)
            {
                BuildingData data = list[i];

                if (data == null)
                {
                    continue;
                }

                if (IsSpecial(data))
                {
                    specialCount++;
                }
                else
                {
                    normalCount++;
                }
            }

            // -1 = 还没出现任何一组，0 = 普通，1 = 特殊。
            // 组别变了就插一个标题；第一组不带竖分隔线，后面的组带一条。
            int currentGroup = -1;

            for (int i = 0; i < list.Count; i++)
            {
                BuildingData data = list[i];

                if (data == null)
                {
                    Debug.LogWarning($"[BuildingBarUI] 建筑清单第 {i} 项是空的，栏位里跳过它。", this);
                    continue;
                }

                int group = IsSpecial(data) ? 1 : 0;

                if (group != currentGroup)
                {
                    CreateGroupHeader(group == 1 ? "特\n殊" : "普\n通", currentGroup >= 0);
                    currentGroup = group;
                }

                BuildingBarEntry entry = CreateEntry(i, data);

                if (entry != null)
                {
                    entries.Add(entry);
                    entryIndexes.Add(i);
                    entryBuildings.Add(data);
                }
            }

            if (logFlow)
            {
                Debug.Log($"[BuildingBarUI] 生成 {entries.Count} 个建筑按钮（普通 {normalCount} / 特殊 {specialCount}）。", this);
            }
        }

        private BuildingBarEntry CreateEntry(int index, BuildingData data)
        {
            GameObject go = CreateUIObject($"Item_{data.BuildingName}", content);

            Image background = go.AddComponent<Image>();
            background.sprite = UISprite();
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = true;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;

            LayoutElement layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = itemSize.x;
            layoutElement.minWidth = itemSize.x;
            layoutElement.preferredHeight = itemSize.y;
            layoutElement.minHeight = itemSize.y;

            Outline glow = go.AddComponent<Outline>();
            glow.effectColor = Color.white;
            glow.effectDistance = new Vector2(3f, 3f);
            glow.useGraphicAlpha = true;
            glow.enabled = false;

            CanvasGroup canvasGroup = go.AddComponent<CanvasGroup>();

            Text nameText = CreateText(go.transform, "NameText", data.BuildingName,
                                       nameFontSize, Color.white, TextAnchor.MiddleCenter);
            nameText.fontStyle = FontStyle.Bold;
            RectTransform nameRect = (RectTransform)nameText.transform;
            nameRect.anchorMin = new Vector2(0f, 0.42f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.offsetMin = new Vector2(8f, 0f);
            nameRect.offsetMax = new Vector2(-8f, -8f);

            Text statusText = CreateText(go.transform, "StatusText", string.Empty,
                                         statusFontSize, Color.white, TextAnchor.MiddleCenter);
            RectTransform statusRect = (RectTransform)statusText.transform;
            statusRect.anchorMin = new Vector2(0f, 0.04f);
            statusRect.anchorMax = new Vector2(1f, 0.44f);
            statusRect.pivot = new Vector2(0.5f, 0.5f);
            statusRect.offsetMin = new Vector2(8f, 0f);
            statusRect.offsetMax = new Vector2(-8f, 0f);

            BuildingBarEntry entry = go.AddComponent<BuildingBarEntry>();
            entry.Bind(background, glow, canvasGroup, nameText, statusText, button);

            int captured = index;
            button.onClick.AddListener(() => HandleEntryClicked(captured));

            return entry;
        }

        private void CreateGroupHeader(string label, bool withDivider)
        {
            GameObject go = CreateUIObject("GroupDivider", content);

            LayoutElement layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = 40f;
            layoutElement.minWidth = 40f;
            layoutElement.preferredHeight = itemSize.y;
            layoutElement.minHeight = itemSize.y;

            if (withDivider)
            {
                GameObject lineObject = CreateUIObject("Line", go.transform);
                RectTransform lineRect = (RectTransform)lineObject.transform;
                lineRect.anchorMin = new Vector2(1f, 0f);
                lineRect.anchorMax = new Vector2(1f, 1f);
                lineRect.pivot = new Vector2(1f, 0.5f);
                lineRect.anchoredPosition = new Vector2(-6f, 0f);
                lineRect.sizeDelta = new Vector2(2f, -12f);

                Image line = lineObject.AddComponent<Image>();
                line.color = dividerColor;
                line.raycastTarget = false;
            }

            Text text = CreateText(go.transform, "GroupLabel", label, groupFontSize,
                                   mutedTextColor, TextAnchor.MiddleCenter);
            Stretch((RectTransform)text.transform);
            text.raycastTarget = false;
        }

        private void HandleEntryClicked(int index)
        {
            if (buildingUI == null)
            {
                return;
            }

            buildingUI.SelectBuilding(index);
            MarkDirty();
        }

        // ---------------- 刷新 ----------------

        private void Refresh()
        {
            if (!built || buildingUI == null)
            {
                return;
            }

            int availableCount = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                BuildingData data = entryBuildings[i];
                BuildingAvailabilityInfo info = buildingUI.Evaluate(entryIndexes[i]);
                bool selected = buildingManager != null &&
                                buildingManager.IsPlacing &&
                                buildingManager.SelectedBuilding == data;

                entries[i].Apply(data, info, selected);

                if (info.IsAvailable)
                {
                    availableCount++;
                }
            }

            if (emptyHint != null)
            {
                emptyHint.gameObject.SetActive(entries.Count == 0);
            }

            RefreshScrollbar();
        }

        private void RefreshScrollbar()
        {
            if (content == null || viewport == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            bool scrollable = content.rect.width > viewport.rect.width + 1f;

            if (scrollbarRect != null && scrollbarRect.gameObject.activeSelf != scrollable)
            {
                scrollbarRect.gameObject.SetActive(scrollable);
            }
        }

        private bool IsSpecial(BuildingData data)
        {
            if (data == null)
            {
                return false;
            }

            if (data.IsSpecial)
            {
                return true;
            }

            return milestones != null && milestones.IsMilestone(data);
        }

        // ---------------- 小工具 ----------------

        private static Sprite cachedUISprite;
        private static bool triedUISprite;

        /// <summary>内置的九宫格底图。取不到就退化成纯色矩形，不影响功能。</summary>
        private static Sprite UISprite()
        {
            if (triedUISprite)
            {
                return cachedUISprite;
            }

            triedUISprite = true;
            cachedUISprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            return cachedUISprite;
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Text CreateText(Transform parent, string name, string content, int fontSize,
                                       Color color, TextAnchor alignment)
        {
            GameObject go = CreateUIObject(name, parent);

            Text text = go.AddComponent<Text>();
            text.font = BuiltinFont();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;

            return text;
        }

        private static Font BuiltinFont()
        {
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }
}
