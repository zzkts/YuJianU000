using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Yujian.Core;
using Yujian.Mining;
using Yujian.Player;

namespace Yujian.UI
{
    /// <summary>
    /// 挖矿界面（飞书 2.1「挖矿系统界面设计」）。
    ///
    /// 为什么运行时搭 UI：这一版是原型，界面元素多、还在反复改，
    /// 运行时生成可以保证不碰场景文件，也不会因为少拖一个引用而整块不显示。
    /// 美术定稿后可以把这里的尺寸 / 颜色换成预制体，逻辑不用动。
    ///
    /// 包含：返回主岛、金币栏、当前照明 / 挖矿工具、当前矿石、两个可滑动升级列表、
    /// x1 / Max 切换（同时作用于两个列表）、光标右侧悬浮说明、金币缩写。
    /// </summary>
    [DisallowMultipleComponent]
    public class MiningHudUI : MonoBehaviour
    {
        [Header("依赖（留空会自动查找）")]
        [SerializeField] private GameAreaController areas;
        [SerializeField] private PlayerCurrency currency;
        [SerializeField] private MiningLightTool lightTool;
        [SerializeField] private MiningToolProgress miningTool;

        [Header("升级方式")]
        [Tooltip("true = Max（升到金币不足为止），false = x1（每次一级）")]
        [SerializeField] private bool useMaxMode;

        [Header("配色")]
        [SerializeField] private Color panelColor = new Color(0.07f, 0.09f, 0.12f, 0.86f);
        [SerializeField] private Color rowColor = new Color(1f, 1f, 1f, 0.11f);
        [SerializeField] private Color rowOwnedColor = new Color(0.24f, 0.45f, 0.34f, 0.55f);
        [SerializeField] private Color accentColor = new Color(1f, 0.84f, 0.42f, 1f);
        [SerializeField] private Color textColor = new Color(0.95f, 0.95f, 0.95f, 1f);
        [SerializeField] private Color dimTextColor = new Color(0.82f, 0.82f, 0.82f, 1f);

        private const float RowHeight = 86f;
        private const float RefWidth = 1920f;
        private const float RefHeight = 1080f;

        private class ToolRow
        {
            public int Level;
            public Image Background;
            public Text Icon;
            public Text Name;
            public Text Gain;
            public Text Cost;
            public Button Button;
            public Text ButtonText;
            public CanvasGroup Group;
        }

        private Canvas canvas;
        private GameObject canvasObject;
        private bool visible;
        private bool built;

        private Text coinText;
        private Text lightIconText;
        private Text lightNameText;
        private Text lightStatsText;
        private Text mineIconText;
        private Text mineNameText;
        private Text mineStatsText;
        private Text oreText;
        private Text modeText;
        private Text feedbackText;
        private RectTransform tooltipRect;
        private Text tooltipText;

        private readonly List<ToolRow> lightRows = new List<ToolRow>();
        private readonly List<ToolRow> mineRows = new List<ToolRow>();

        private float nextOreRefresh;
        private float feedbackClearTime = -1f;
        private bool tooltipVisible;

        public bool UseMaxMode => useMaxMode;

        /// <summary>编辑器向导调用，把场景引用接好。</summary>
        public void ConfigureForSetup(GameAreaController newAreas, PlayerCurrency newCurrency,
            MiningLightTool newLightTool, MiningToolProgress newMiningTool)
        {
            areas = newAreas;
            currency = newCurrency;
            lightTool = newLightTool;
            miningTool = newMiningTool;
        }

        private void Awake()
        {
            ResolveDependencies();
            Build();
        }

        private void OnEnable()
        {
            if (areas != null)
            {
                areas.OnAreaChanged += HandleAreaChanged;
            }
        }

        private void OnDisable()
        {
            if (areas != null)
            {
                areas.OnAreaChanged -= HandleAreaChanged;
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Start()
        {
            if (areas == null)
            {
                Debug.LogError("[MiningHudUI] 找不到 GameAreaController，界面不会按区域显示。" +
                               "请运行菜单「屿见/挖矿/配置挖矿界面」。", this);
            }

            SetVisible(areas == null || areas.CurrentArea == GameArea.Mining);
            RefreshAll();
        }

        private void Update()
        {
            if (!visible)
            {
                return;
            }

            if (Time.unscaledTime >= nextOreRefresh)
            {
                nextOreRefresh = Time.unscaledTime + 0.3f;
                RefreshOre();
            }

            if (feedbackClearTime > 0f && Time.unscaledTime >= feedbackClearTime)
            {
                feedbackClearTime = -1f;
                SetFeedback(string.Empty);
            }

            UpdateTooltipPosition();
        }

        private void ResolveDependencies()
        {
            if (areas == null)
            {
                areas = FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);
            }

            if (currency == null)
            {
                currency = FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            }

            if (lightTool == null)
            {
                lightTool = FindFirstObjectByType<MiningLightTool>(FindObjectsInactive.Include);
            }

            if (miningTool == null)
            {
                miningTool = FindFirstObjectByType<MiningToolProgress>(FindObjectsInactive.Include);
            }
        }

        private void Subscribe()
        {
            if (currency != null)
            {
                currency.OnMoneyChanged -= HandleMoneyChanged;
                currency.OnMoneyChanged += HandleMoneyChanged;
            }

            if (lightTool != null)
            {
                lightTool.OnToolChanged -= HandleLightChanged;
                lightTool.OnToolChanged += HandleLightChanged;
            }

            if (miningTool != null)
            {
                miningTool.OnToolChanged -= HandleMiningChanged;
                miningTool.OnToolChanged += HandleMiningChanged;
            }
        }

        private void Unsubscribe()
        {
            if (currency != null)
            {
                currency.OnMoneyChanged -= HandleMoneyChanged;
            }

            if (lightTool != null)
            {
                lightTool.OnToolChanged -= HandleLightChanged;
            }

            if (miningTool != null)
            {
                miningTool.OnToolChanged -= HandleMiningChanged;
            }
        }
        // ---------------- 区域显隐 ----------------

        private void HandleAreaChanged(GameArea area)
        {
            SetVisible(area == GameArea.Mining);

            if (visible)
            {
                RefreshAll();
            }
        }

        private void SetVisible(bool value)
        {
            visible = value;

            if (canvasObject != null)
            {
                canvasObject.SetActive(value);
            }

            if (value)
            {
                RefreshAll();
            }
            else
            {
                HideTooltip();
            }
        }

        // ---------------- 按钮与事件 ----------------

        private void HandleMoneyChanged(int _)
        {
            RefreshAll();
        }

        private void HandleLightChanged(MiningLightTool _)
        {
            RefreshAll();
        }

        private void HandleMiningChanged(MiningToolProgress _)
        {
            RefreshAll();
        }

        private void HandleBackClicked()
        {
            if (areas != null)
            {
                areas.EnterObservationMode();
            }
        }

        private void HandleModeClicked()
        {
            useMaxMode = !useMaxMode;
            RefreshAll();
        }

        private void HandleLightRowClicked(int level)
        {
            if (lightTool == null)
            {
                return;
            }

            int current = lightTool.Level;

            if (level <= current)
            {
                if (level != current)
                {
                    lightTool.SetLevel(level);
                    ShowFeedback($"已装备「{lightTool.GetLevelName(level)}」", true);
                }

                RefreshAll();
                return;
            }

            if (level > current + 1)
            {
                ShowFeedback("请先解锁上一件照明工具", false);
                return;
            }

            int count = ResolveLightUpgradeCount(level);
            int cost = lightTool.GetUpgradeCostForLevels(count);

            if (!CanAfford(cost))
            {
                ShowFeedback($"金币不足，还差 {cost - CurrentMoney()}", false);
                RefreshAll();
                return;
            }

            if (lightTool.TryUpgrade(count))
            {
                ShowFeedback($"升级成功，已装备「{lightTool.LevelName}」", true);
            }

            RefreshAll();
        }

        private void HandleMineRowClicked()
        {
            if (miningTool == null)
            {
                return;
            }

            int count = ResolveMineUpgradeCount();
            int cost = miningTool.GetUpgradeCostForLevels(count);

            if (!CanAfford(cost))
            {
                ShowFeedback($"金币不足，还差 {cost - CurrentMoney()}", false);
                RefreshAll();
                return;
            }

            if (miningTool.TryUpgrade(count))
            {
                ShowFeedback($"挖矿工具升到 {miningTool.CurrentLevel} 级", true);
            }

            RefreshAll();
        }

        private int ResolveLightUpgradeCount(int targetLevel)
        {
            int available = lightTool != null
                ? Mathf.Max(1, lightTool.MaxLevel - LightLevel())
                : Mathf.Max(1, targetLevel - LightLevel());

            if (!useMaxMode)
            {
                return 1;
            }

            int affordable = lightTool != null
                ? lightTool.GetAffordableUpgradeCount(CurrentMoney())
                : 0;

            return Mathf.Clamp(affordable, 1, available);
        }

        private int ResolveMineUpgradeCount()
        {
            if (!useMaxMode || miningTool == null)
            {
                return 1;
            }

            int affordable = miningTool.GetAffordableUpgradeCount(CurrentMoney());
            return Mathf.Max(1, affordable);
        }

        private int LightLevel()
        {
            return lightTool != null ? lightTool.Level : 0;
        }

        private int CurrentMoney()
        {
            return currency != null ? currency.CurrentMoney : 0;
        }

        private bool CanAfford(int cost)
        {
            return cost <= 0 || CurrentMoney() >= cost;
        }

        // ---------------- 刷新 ----------------

        private void RefreshAll()
        {
            if (!built)
            {
                return;
            }

            RefreshCoin();
            RefreshEquipped();
            RefreshLightRows();
            RefreshMineRows();
            RefreshMode();
            RefreshOre();
        }

        private void RefreshCoin()
        {
            if (coinText != null)
            {
                coinText.text = $"金币 {FormatNumber(CurrentMoney())}";
            }
        }

        private void RefreshEquipped()
        {
            if (lightTool != null && lightTool.Level > 0)
            {
                SetText(lightIconText, IconGlyph(lightTool.LevelName));
                SetText(lightNameText, lightTool.LevelName);
                SetText(lightStatsText,
                    $"冷却 {lightTool.RefreshCooldown:0.#} 秒\n" +
                    $"暴击点 {lightTool.CritPointCapacity} 个\n" +
                    $"暴击伤害 {lightTool.CritPointDamageMultiplier * 100f:0}%");
            }
            else if (lightTool != null)
            {
                SetText(lightIconText, "灯");
                SetText(lightNameText, "未装备照明工具");
                SetText(lightStatsText, "先从右侧解锁火把，\n才会开始刷新暴击点。");
            }
            else
            {
                SetText(lightIconText, "灯");
                SetText(lightNameText, "未配置照明工具");
                SetText(lightStatsText, "请运行配置菜单");
            }

            if (miningTool != null)
            {
                SetText(mineIconText, IconGlyph(miningTool.ToolName));
                SetText(mineNameText, miningTool.ToolName);
                SetText(mineStatsText,
                    $"等级 {miningTool.CurrentLevel}\n" +
                    $"攻击力 {miningTool.CurrentAttack:0.##}");
            }
            else
            {
                SetText(mineIconText, "镐");
                SetText(mineNameText, "未配置挖矿工具");
                SetText(mineStatsText, "请运行配置菜单");
            }
        }

        private void RefreshLightRows()
        {
            if (lightTool == null || lightRows.Count == 0)
            {
                return;
            }

            int current = lightTool.Level;
            int money = CurrentMoney();

            for (int i = 0; i < lightRows.Count; i++)
            {
                ToolRow row = lightRows[i];
                int level = row.Level;
                bool owned = level <= current;
                bool equipped = level == current;

                SetText(row.Icon, IconGlyph(lightTool.GetLevelName(level)));
                SetText(row.Name, lightTool.GetLevelName(level));

                if (equipped)
                {
                    row.Background.color = rowOwnedColor;
                    SetText(row.Gain, "当前装备");
                    SetText(row.Cost, "—");
                    SetText(row.ButtonText, "已装备");
                    SetButtonInteractable(row.Button, false);
                    SetRowDim(row.Group, false);
                    continue;
                }

                if (owned)
                {
                    row.Background.color = rowOwnedColor;
                    SetText(row.Gain, "已拥有");
                    SetText(row.Cost, "—");
                    SetText(row.ButtonText, "装备");
                    SetButtonInteractable(row.Button, true);
                    SetRowDim(row.Group, false);
                    continue;
                }

                row.Background.color = rowColor;

                if (level > current + 1)
                {
                    SetText(row.Gain, "未解锁");
                    SetText(row.Cost, "需先解锁上一件");
                    SetText(row.ButtonText, "未解锁");
                    SetButtonInteractable(row.Button, false);
                    SetRowDim(row.Group, true);
                    continue;
                }

                int count = ResolveLightUpgradeCount(level);
                int cost = lightTool.GetUpgradeCostForLevels(count);
                bool affordable = money >= cost;

                SetText(row.Gain, $"升级 x{count}");
                SetText(row.Cost, $"{FormatNumber(cost)} 金币");
                SetText(row.ButtonText, "未解锁");
                // 金币不足时只降低亮度，仍然可以点：点了会在右下提示还差多少。
                SetButtonInteractable(row.Button, true);
                SetRowDim(row.Group, !affordable);
            }
        }

        private void RefreshMineRows()
        {
            if (miningTool == null || mineRows.Count == 0)
            {
                return;
            }

            ToolRow row = mineRows[0];
            int count = ResolveMineUpgradeCount();
            int cost = miningTool.GetUpgradeCostForLevels(count);
            bool affordable = CurrentMoney() >= cost;

            SetText(row.Icon, IconGlyph(miningTool.ToolName));
            SetText(row.Name, $"{miningTool.ToolName}　Lv.{miningTool.CurrentLevel}");
            SetText(row.Gain, $"升级 x{count}");
            SetText(row.Cost, $"共 {FormatNumber(cost)} 金币");
            SetText(row.ButtonText, "升级");
            row.Background.color = rowColor;
            SetButtonInteractable(row.Button, true);
            SetRowDim(row.Group, !affordable);
        }

        private void RefreshMode()
        {
            SetText(modeText, useMaxMode ? "Max" : "x1");
        }

        private void RefreshOre()
        {
            Ore[] ores = FindObjectsByType<Ore>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Ore target = null;
            int alive = 0;

            for (int i = 0; i < ores.Length; i++)
            {
                if (ores[i] != null && ores[i].IsAlive)
                {
                    alive++;

                    if (target == null)
                    {
                        target = ores[i];
                    }
                }
            }

            if (target == null)
            {
                SetText(oreText, "当前矿石：等待刷新");
                return;
            }

            OreTypeTag tag = target.GetComponent<OreTypeTag>();
            string oreName = tag != null ? OreTypeInfo.DisplayName(tag.Type) : "矿石";

            SetText(oreText,
                $"当前矿石：{oreName}　生命 {target.CurrentHealth:0.#} / {target.MaxHealth:0.#}　" +
                $"剩余 {alive} 块");
        }
        private static void SetText(Text target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }

        private static void SetButtonInteractable(Button button, bool value)
        {
            if (button != null)
            {
                button.interactable = value;
            }
        }

        private static void SetRowDim(CanvasGroup group, bool dim)
        {
            if (group != null)
            {
                group.alpha = dim ? 0.45f : 1f;
            }
        }

        private void ShowFeedback(string message, bool success)
        {
            if (feedbackText == null)
            {
                return;
            }

            feedbackText.text = message;
            feedbackText.color = success
                ? new Color(0.45f, 0.95f, 0.55f, 1f)
                : new Color(1f, 0.5f, 0.45f, 1f);
            feedbackClearTime = Time.unscaledTime + 2.2f;
        }

        private void SetFeedback(string message)
        {
            if (feedbackText != null)
            {
                feedbackText.text = message;
            }
        }

        // ---------------- 悬浮说明 ----------------

        /// <summary>HoverTooltipTrigger 的入口。</summary>
        public void ShowTooltipFor(string message)
        {
            if (tooltipRect == null || tooltipText == null)
            {
                return;
            }

            tooltipText.text = message;
            tooltipRect.gameObject.SetActive(true);
            tooltipVisible = true;
            UpdateTooltipPosition();
        }

        /// <summary>HoverTooltipTrigger 的入口。</summary>
        public void HideTooltipFor()
        {
            HideTooltip();
        }

        private void HideTooltip()
        {
            tooltipVisible = false;

            if (tooltipRect != null)
            {
                tooltipRect.gameObject.SetActive(false);
            }
        }

        private void UpdateTooltipPosition()
        {
            if (!tooltipVisible || tooltipRect == null || canvas == null)
            {
                return;
            }

            Vector2 screen = Pointer.current != null
                ? Pointer.current.position.ReadValue()
                : Vector2.zero;

            RectTransform canvasRect = canvas.transform as RectTransform;

            if (canvasRect == null)
            {
                return;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screen, canvas.worldCamera, out Vector2 local);

            tooltipRect.anchoredPosition = local + new Vector2(24f, -20f);
        }

        // ---------------- 构建界面 ----------------

        private void Build()
        {
            if (built)
            {
                return;
            }

            built = true;

            canvasObject = new GameObject("MiningHUDRuntime",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.layer = 5;
            canvasObject.transform.SetParent(transform, false);

            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform root = CreateRect("Hud", canvasObject.transform);
            Stretch(root);
            root.gameObject.AddComponent<CanvasGroup>();

            BuildBackButton(root);
            BuildCoinBar(root);
            BuildEquippedPanel(root);
            BuildUpgradePanel(root);
            BuildOreBar(root);
            BuildTooltip(root);

            canvasObject.SetActive(false);
            Subscribe();
            RefreshAll();
        }

        private void BuildBackButton(RectTransform root)
        {
            Button button = CreateButton(root, "BackButton", "返回主岛", 18,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -26f),
                new Vector2(170f, 46f));

            button.onClick.AddListener(HandleBackClicked);
        }

        private void BuildCoinBar(RectTransform root)
        {
            RectTransform panel = CreatePanel(root, "CoinBar",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-32f, -26f),
                new Vector2(240f, 46f));

            coinText = CreateText(panel, "CoinText", 22,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(220f, 40f), TextAnchor.MiddleRight, accentColor, true);
            coinText.text = "金币 0";
        }

        private void BuildEquippedPanel(RectTransform root)
        {
            RectTransform panel = CreatePanel(root, "EquippedPanel",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -90f),
                new Vector2(300f, 390f));

            Text title = CreateText(panel, "Title", 19,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -12f),
                new Vector2(260f, 28f), TextAnchor.MiddleLeft, textColor, true);
            title.text = "当前装备";

            CreateToolCard(panel, "LightCard", -48f, "灯",
                out lightIconText, out lightNameText, out lightStatsText);

            CreateToolCard(panel, "MineCard", -225f, "镐",
                out mineIconText, out mineNameText, out mineStatsText);
        }

        private void CreateToolCard(RectTransform parent, string name, float y, string icon,
            out Text iconText, out Text titleText, out Text statsText)
        {
            RectTransform card = CreatePanel(parent, name,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, y),
                new Vector2(272f, 165f), rowColor);

            RectTransform iconBox = CreatePanel(card, "Icon",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -12f),
                new Vector2(76f, 76f), new Color(0.95f, 0.78f, 0.36f, 0.22f));

            iconText = CreateText(iconBox, "Glyph", 34,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(76f, 76f), TextAnchor.MiddleCenter, accentColor, true);
            iconText.text = icon;

            titleText = CreateText(card, "Name", 19,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(98f, -16f),
                new Vector2(162f, 26f), TextAnchor.MiddleLeft, textColor, true);

            statsText = CreateText(card, "Stats", 16,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(98f, -48f),
                new Vector2(166f, 106f), TextAnchor.UpperLeft, dimTextColor, false);
        }
        private void BuildUpgradePanel(RectTransform root)
        {
            RectTransform panel = CreatePanel(root, "UpgradePanel",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-32f, -90f),
                new Vector2(430f, 640f));

            Text lightTitle = CreateText(panel, "LightTitle", 19,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -12f),
                new Vector2(300f, 26f), TextAnchor.MiddleLeft, textColor, true);
            lightTitle.text = "照明工具";

            CreateScrollView(panel, "LightList",
                new Vector2(16f, -44f), new Vector2(398f, 250f), out RectTransform lightContent);

            Text mineTitle = CreateText(panel, "MineTitle", 19,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -306f),
                new Vector2(300f, 26f), TextAnchor.MiddleLeft, textColor, true);
            mineTitle.text = "挖矿工具";

            CreateScrollView(panel, "MineList",
                new Vector2(16f, -338f), new Vector2(398f, 112f), out RectTransform mineContent);

            BuildLightRows(lightContent);
            BuildMineRows(mineContent);

            Button modeButton = CreateButton(panel, "ModeButton", "x1", 18,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -470f),
                new Vector2(150f, 44f));
            modeButton.onClick.AddListener(HandleModeClicked);
            modeText = modeButton.GetComponentInChildren<Text>();

            AddHoverTooltip(modeButton.gameObject, () => useMaxMode
                ? "Max——升级到金币不足为止"
                : "x1——每次升一级");

            feedbackText = CreateText(panel, "Feedback", 16,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(180f, -470f),
                new Vector2(234f, 44f), TextAnchor.MiddleLeft, dimTextColor, false);
        }

        private void BuildLightRows(RectTransform content)
        {
            int maxLevel = lightTool != null ? lightTool.MaxLevel : 0;

            for (int level = 1; level <= maxLevel; level++)
            {
                int captured = level;
                ToolRow row = CreateToolRow(content, $"LightRow_{level}");
                row.Level = level;

                row.Button.onClick.AddListener(() => HandleLightRowClicked(captured));
                AddHoverTooltip(row.Background.gameObject, () => DescribeLightTool(captured));

                lightRows.Add(row);
            }
        }

        private void BuildMineRows(RectTransform content)
        {
            ToolRow row = CreateToolRow(content, "MineRow");
            row.Button.onClick.AddListener(HandleMineRowClicked);
            AddHoverTooltip(row.Background.gameObject, DescribeMiningTool);
            mineRows.Add(row);
        }

        private string DescribeLightTool(int level)
        {
            if (lightTool == null)
            {
                return "照明工具未配置";
            }

            string name = lightTool.GetLevelName(level);
            float cooldown = lightTool.GetRefreshCooldownAt(level);
            int capacity = lightTool.GetCritPointCapacityAt(level);
            float crit = lightTool.GetCritPointMultiplierAt(level);

            return $"{name}\n冷却 {cooldown:0.#} 秒　暴击点 {capacity} 个\n" +
                   $"暴击伤害 {crit * 100f:0}%";
        }

        private string DescribeMiningTool()
        {
            return miningTool == null
                ? "挖矿工具未配置"
                : $"{miningTool.ToolName}\n等级 {miningTool.CurrentLevel}　攻击力 {miningTool.CurrentAttack:0.##}";
        }

        private ToolRow CreateToolRow(RectTransform content, string name)
        {
            RectTransform rowRect = CreateRect(name, content);
            rowRect.sizeDelta = new Vector2(0f, RowHeight);

            LayoutElement layout = rowRect.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = RowHeight;

            Image background = rowRect.gameObject.AddComponent<Image>();
            background.color = rowColor;

            CanvasGroup group = rowRect.gameObject.AddComponent<CanvasGroup>();

            RectTransform iconBox = CreatePanel(rowRect, "Icon",
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f),
                new Vector2(58f, 58f), new Color(1f, 1f, 1f, 0.1f));

            Text icon = CreateText(iconBox, "Glyph", 26,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(58f, 58f), TextAnchor.MiddleCenter, accentColor, true);

            Text title = CreateText(rowRect, "Name", 18,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(78f, -8f),
                new Vector2(186f, 24f), TextAnchor.MiddleLeft, textColor, true);

            Text gain = CreateText(rowRect, "Gain", 16,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(78f, -34f),
                new Vector2(150f, 20f), TextAnchor.MiddleLeft, dimTextColor, false);

            Text cost = CreateText(rowRect, "Cost", 16,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(78f, -56f),
                new Vector2(186f, 20f), TextAnchor.MiddleLeft, accentColor, false);

            Button button = CreateButton(rowRect, "Action", "未解锁", 17,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f),
                new Vector2(104f, 42f));

            Text buttonText = button.GetComponentInChildren<Text>();

            return new ToolRow
            {
                Background = background,
                Icon = icon,
                Name = title,
                Gain = gain,
                Cost = cost,
                Button = button,
                ButtonText = buttonText,
                Group = group,
            };
        }

        private void BuildOreBar(RectTransform root)
        {
            RectTransform panel = CreatePanel(root, "OreBar",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 100f),
                new Vector2(430f, 54f));

            oreText = CreateText(panel, "OreText", 18,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(410f, 44f), TextAnchor.MiddleCenter, textColor, false);
            oreText.text = "当前矿石";
        }

        private void BuildTooltip(RectTransform root)
        {
            tooltipRect = CreatePanel(root, "Tooltip",
                new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), Vector2.zero,
                new Vector2(270f, 82f), new Color(0.05f, 0.06f, 0.08f, 0.96f));

            tooltipText = CreateText(tooltipRect, "Text", 16,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(250f, 70f), TextAnchor.MiddleLeft, textColor, false);

            tooltipRect.gameObject.SetActive(false);
        }

        private void AddHoverTooltip(GameObject target, System.Func<string> content)
        {
            HoverTooltipTrigger trigger = target.AddComponent<HoverTooltipTrigger>();
            trigger.Bind(this, content);
        }

        // ---------------- 小工具 ----------------

        private static string IconGlyph(string label)
        {
            return string.IsNullOrEmpty(label) ? "?" : label.Substring(0, 1);
        }

        /// <summary>
        /// 金币缩写：1000 → 1k，1,500,000 → 1.5m；
        /// 超过常规单位（k / m / b / t）后按 aa、bb、cc…… 递增。
        /// </summary>
        public static string FormatNumber(long value)
        {
            if (value < 0)
            {
                return "-" + FormatNumber(-value);
            }

            if (value < 1000)
            {
                return value.ToString();
            }

            string[] common = { string.Empty, "k", "m", "b", "t" };
            double scaled = value;
            int unit = 0;

            while (scaled >= 1000d && unit < common.Length - 1)
            {
                scaled /= 1000d;
                unit++;
            }

            string suffix;

            if (scaled >= 1000d)
            {
                int extra = 0;

                while (scaled >= 1000d)
                {
                    scaled /= 1000d;
                    extra++;
                }

                suffix = SuffixFor(extra);
            }
            else
            {
                suffix = common[unit];
            }

            string number = scaled >= 100d ? scaled.ToString("0")
                : scaled >= 10d ? scaled.ToString("0.#")
                : scaled.ToString("0.##");

            return number + suffix;
        }

        private static string SuffixFor(int index)
        {
            // 1 → aa，2 → bb，3 → cc …… 26 → zz，27 → aaa
            int safe = Mathf.Max(1, index) - 1;
            int letter = safe % 26;
            int repeat = safe / 26 + 2;
            char c = (char)('a' + letter);
            return new string(c, repeat);
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private RectTransform CreatePanel(Transform parent, string name,
            Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            return CreatePanel(parent, name, anchor, pivot, position, size, panelColor);
        }

        private static RectTransform CreatePanel(Transform parent, string name,
            Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return rect;
        }

        private static RectTransform CreateScrollView(Transform parent, string name,
            Vector2 position, Vector2 size, out RectTransform content)
        {
            RectTransform viewport = CreateRect(name, parent);
            viewport.anchorMin = new Vector2(0f, 1f);
            viewport.anchorMax = new Vector2(0f, 1f);
            viewport.pivot = new Vector2(0f, 1f);
            viewport.anchoredPosition = position;
            viewport.sizeDelta = size;

            Image background = viewport.gameObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.18f);
            viewport.gameObject.AddComponent<RectMask2D>();

            content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            return viewport;
        }

        private static Text CreateText(Transform parent, string name, int fontSize,
            Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size,
            TextAnchor alignment, Color color, bool bold)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Text text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Button CreateButton(Transform parent, string name, string label, int fontSize,
            Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            RectTransform rect = CreatePanel(parent, name, anchor, pivot, position, size,
                new Color(0.9f, 0.75f, 0.38f, 0.95f));

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.9f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            button.colors = colors;

            Text text = CreateText(rect, "Label", fontSize,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(size.x - 8f, size.y), TextAnchor.MiddleCenter,
                new Color(0.12f, 0.12f, 0.12f, 1f), true);
            text.text = label;

            return button;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }
    }

    /// <summary>鼠标移入 / 移出时通知挖矿界面显示光标右侧说明。</summary>
    public class HoverTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private MiningHudUI hud;
        private System.Func<string> content;

        public void Bind(MiningHudUI owner, System.Func<string> contentProvider)
        {
            hud = owner;
            content = contentProvider;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (hud != null && content != null)
            {
                hud.ShowTooltipFor(content());
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (hud != null)
            {
                hud.HideTooltipFor();
            }
        }
    }
}