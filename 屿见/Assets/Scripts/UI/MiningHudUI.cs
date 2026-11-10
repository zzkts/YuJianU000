using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Yujian.Core;
using Yujian.Levels;
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
    /// 包含：退出关卡、垃圾栏、当前照明 / 挖矿工具、当前矿石、两个可滑动升级列表、
    /// x1 / Max 切换（同时作用于两个列表）、光标右侧悬浮说明、垃圾缩写。
    /// </summary>
    [DisallowMultipleComponent]
    public class MiningHudUI : MonoBehaviour
    {
        [Header("依赖（留空会自动查找）")]
        [SerializeField] private GameAreaController areas;
        [SerializeField] private PlayerCurrency currency;
        [SerializeField] private MiningLightTool lightTool;
        [SerializeField] private MiningToolProgress miningTool;

        [Tooltip("关卡管理器。退出按钮走它的黑场转场与暂停流程；留空会自动查找")]
        [SerializeField] private LevelManager levelManager;

        [Header("升级方式")]
        [Tooltip("true = Max（升到垃圾不足为止），false = x1（每次一级）")]
        [SerializeField] private bool useMaxMode;

        [Header("资源栏动画")]
        [Tooltip("一块垃圾入账时，资源栏数字 / 图标放大的倍数")]
        [SerializeField, Min(1f)] private float punchScale = 1.25f;

        [Tooltip("一块垃圾入账时，资源栏数字旋转的角度（度）")]
        [SerializeField] private float punchAngle = 12f;

        [Tooltip("一次弹跳的总时长（秒）")]
        [SerializeField, Min(0.05f)] private float punchDuration = 0.36f;

        [Header("资源栏图标")]
        [Tooltip("资源栏图标。留空时在运行时绘制一个垃圾桶图标")]
        [SerializeField] private Sprite resourceIconSprite;

        [Header("聚合体血条")]
        [Tooltip("血条自然色。剩余血量减少时立即缩短")]
        [SerializeField] private Color healthFillColor = new Color(0.86f, 0.24f, 0.22f, 1f);

        [Tooltip("延迟血条颜色。扣血后停留一下，再平滑追上当前血量")]
        [SerializeField] private Color healthHurtColor = new Color(0.98f, 0.75f, 0.3f, 1f);

        [Tooltip("扣血后延迟血条开始回追的停留时间（秒）")]
        [SerializeField, Min(0f)] private float hurtDelay = 0.12f;

        [Tooltip("延迟血条回追速度。越大越快追上当前血量")]
        [SerializeField, Min(0.1f)] private float hurtLerpSpeed = 9f;

        [Header("关卡状态")]
        [Tooltip("BOSS 倒计时剩余较多时的颜色（策划案 2.3.6：前 20 秒蓝色）")]
        [SerializeField] private Color countdownNormalColor = new Color(0.35f, 0.72f, 1f, 1f);

        [Tooltip("BOSS 倒计时进入最后阶段时的颜色（策划案 2.3.6：剩余 10 秒变红并抖动）")]
        [SerializeField] private Color countdownWarningColor = new Color(1f, 0.32f, 0.26f, 1f);

        [Tooltip("剩余多少秒开始变红并轻微抖动")]
        [SerializeField, Min(0f)] private float countdownWarningSeconds = 10f;

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
        private bool authoringMode;
        private Sprite authoringSprite;

        private Text coinText;
        private Image coinIconImage;
        private Sprite generatedIconSprite;
        private RectTransform coinBarRect;
        private Vector3 coinTextBaseScale = Vector3.one;
        private Vector3 coinIconBaseScale = Vector3.one;
        private Quaternion coinTextBaseRotation = Quaternion.identity;
        private Quaternion coinIconBaseRotation = Quaternion.identity;
        private Coroutine coinPunchRoutine;
        private Text lightIconText;
        private Text lightNameText;
        private Text lightStatsText;
        private Text mineIconText;
        private Text mineNameText;
        private Text mineStatsText;
        private Text oreText;
        private RectTransform oreHealthGroup;
        private Image oreHpFill;
        private Image oreHurtFill;
        private Text oreHealthText;
        private Sprite barFillSprite;
        private Ore healthOre;
        private float oreHurtHoldUntil;
        private float lastOreHealth = -1f;
        private float lastOreMaxHealth = -1f;

        // 关卡状态（2.3.7）：血条下方的剩余波次 / BOSS 倒计时
        private Text wavesText;
        private RectTransform countdownGroup;
        private Image countdownFill;
        private Text countdownText;
        private Vector2 countdownTextBasePosition;
        private Text modeText;
        private Text feedbackText;
        private RectTransform tooltipRect;
        private Text tooltipText;

        private readonly List<ToolRow> lightRows = new List<ToolRow>();
        private readonly List<ToolRow> mineRows = new List<ToolRow>();

        private float nextOreRefresh;
        private float feedbackClearTime = -1f;
        private bool tooltipVisible;

        /// <summary>岛上常驻的「垃圾：N」栏。清洁界面自己也有资源栏，进入清洁区时要收起来。</summary>
        private CoinHudUI standaloneResourceHud;
        private bool hidStandaloneResourceHud;

        public bool UseMaxMode => useMaxMode;

        /// <summary>挖矿界面当前是否显示。收集器用它决定还要不要做飞行动画。</summary>
        public bool IsVisible => visible;

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

            // 场景里已经有编辑器生成好的 UI 时直接绑定，避免重复生成一份。
            if (!TryBindAuthoredHierarchy())
            {
                Build();
            }
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

            SetStandaloneResourceHudVisible(true);
        }

        private void OnDestroy()
        {
            Unsubscribe();

            if (barFillSprite != null)
            {
                Destroy(barFillSprite);
            }

            if (generatedIconSprite != null)
            {
                Texture2D texture = generatedIconSprite.texture;
                Destroy(generatedIconSprite);

                if (texture != null)
                {
                    Destroy(texture);
                }
            }
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

            UpdateOreHealthBar();
            UpdateLevelStatus();

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

            if (standaloneResourceHud == null)
            {
                standaloneResourceHud = FindFirstObjectByType<CoinHudUI>(FindObjectsInactive.Include);
            }

            if (levelManager == null)
            {
                levelManager = FindFirstObjectByType<LevelManager>(FindObjectsInactive.Include);
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

            UnsubscribeHealthOre();
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

            // 清洁界面自带资源栏，进清洁区时把岛上那份「垃圾：N」收起来，避免叠两层
            SetStandaloneResourceHudVisible(!value);

            if (value)
            {
                RefreshAll();
            }
            else
            {
                HideTooltip();
            }
        }

        /// <summary>
        /// 离开清洁区时只恢复本组件关掉的那份资源栏，
        /// 场景里原本就隐藏的不动（有人可能主动停用了 CoinHud）。
        /// </summary>
        private void SetStandaloneResourceHudVisible(bool value)
        {
            if (standaloneResourceHud == null)
            {
                return;
            }

            GameObject target = standaloneResourceHud.gameObject;

            if (!value)
            {
                if (target.activeSelf)
                {
                    target.SetActive(false);
                    hidStandaloneResourceHud = true;
                }

                return;
            }

            if (hidStandaloneResourceHud)
            {
                target.SetActive(true);
                hidStandaloneResourceHud = false;
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

        private void HandleExitLevelClicked()
        {
            ExitLevelWithFade();
        }

        /// <summary>
        /// 退出关卡：有 LevelManager 就走它的黑场转场（同时暂停关卡内容），
        /// 没有就退回旧行为直接切观察模式，保证界面在还没跑关卡向导时也能用。
        /// </summary>
        private void ExitLevelWithFade()
        {
            if (levelManager == null)
            {
                levelManager = FindFirstObjectByType<LevelManager>(FindObjectsInactive.Include);
            }

            if (levelManager != null)
            {
                levelManager.ExitLevel();
                return;
            }

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
                ShowFeedback($"垃圾不足，还差 {cost - CurrentMoney()}", false);
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
                ShowFeedback($"垃圾不足，还差 {cost - CurrentMoney()}", false);
                RefreshAll();
                return;
            }

            if (miningTool.TryUpgrade(count))
            {
                ShowFeedback($"清洁工具升到 {miningTool.CurrentLevel} 级", true);
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
                coinText.text = $"垃圾 {FormatNumber(CurrentMoney())}";
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
                SetText(mineIconText, "刷");
                SetText(mineNameText, "未配置清洁工具");
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
                SetText(row.Cost, $"{FormatNumber(cost)} 垃圾");
                SetText(row.ButtonText, "未解锁");
                // 垃圾不足时只降低亮度，仍然可以点：点了会在右下提示还差多少。
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
            SetText(row.Cost, $"共 {FormatNumber(cost)} 垃圾");
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

            if (target != null && target != healthOre)
            {
                BindHealthOre(target);
                ApplyOreHealth(target.CurrentHealth, target.MaxHealth, false);
            }

            if (oreHealthGroup != null)
            {
                oreHealthGroup.gameObject.SetActive(target != null);
            }

            if (target == null)
            {
                SetText(oreText, "当前垃圾聚合体：等待刷新");
                return;
            }

            OreTypeTag tag = target.GetComponent<OreTypeTag>();
            string oreName = tag != null ? OreTypeInfo.DisplayName(tag.Type) : "垃圾";

            // 血量文字已经交给右侧大号数字，这里只保留种类与剩余块数（策划案 2.2.5）
            SetText(oreText, $"当前垃圾聚合体：{oreName}　剩余 {alive} 块");

            if (target == healthOre &&
                (!Mathf.Approximately(target.CurrentHealth, lastOreHealth) ||
                 !Mathf.Approximately(target.MaxHealth, lastOreMaxHealth)))
            {
                ApplyOreHealth(target.CurrentHealth, target.MaxHealth, true);
            }
        }

        // ---------------- 聚合体血条 ----------------

        /// <summary>
        /// 订阅当前聚合体的血量事件。刷新成新聚合体时 <see cref="Ore.OnDefinitionApplied"/>
        /// 会把血条重置为满血，这就是策划案 2.2.5 的「每次刷新使用新的血条」。
        /// </summary>
        private void BindHealthOre(Ore ore)
        {
            if (healthOre == ore)
            {
                return;
            }

            UnsubscribeHealthOre();
            healthOre = ore;

            if (ore == null)
            {
                return;
            }

            ore.OnHealthChanged += HandleOreHealthChanged;
            ore.OnDefinitionApplied += HandleOreDefinitionApplied;
        }

        private void UnsubscribeHealthOre()
        {
            if (healthOre == null)
            {
                return;
            }

            healthOre.OnHealthChanged -= HandleOreHealthChanged;
            healthOre.OnDefinitionApplied -= HandleOreDefinitionApplied;
            healthOre = null;
        }

        private void HandleOreHealthChanged(float currentHealth, float maxHealth)
        {
            if (healthOre == null)
            {
                return;
            }

            ApplyOreHealth(currentHealth, maxHealth, true);
        }

        private void HandleOreDefinitionApplied(Ore ore)
        {
            if (ore != healthOre)
            {
                return;
            }

            ResetOreHealthBar(ore.MaxHealth);
        }

        /// <summary>新聚合体出现：血条与数字都回到满血。</summary>
        private void ResetOreHealthBar(float maxHealth)
        {
            float safeMax = Mathf.Max(0.0001f, maxHealth);

            if (oreHpFill != null)
            {
                oreHpFill.fillAmount = 1f;
            }

            if (oreHurtFill != null)
            {
                oreHurtFill.fillAmount = 1f;
            }

            SetText(oreHealthText, FormatHealthValue(safeMax));
            lastOreHealth = safeMax;
            lastOreMaxHealth = safeMax;
            oreHurtHoldUntil = 0f;
        }

        private void ApplyOreHealth(float currentHealth, float maxHealth, bool animateHurt)
        {
            float safeMax = Mathf.Max(0.0001f, maxHealth);
            float safeCurrent = Mathf.Clamp(currentHealth, 0f, safeMax);
            float fraction = safeCurrent / safeMax;

            if (oreHpFill != null)
            {
                oreHpFill.fillAmount = fraction;
            }

            if (oreHurtFill != null)
            {
                if (animateHurt)
                {
                    if (fraction < oreHurtFill.fillAmount)
                    {
                        // 扣血：黄色延迟条先停住，等 Update 再追上来
                        oreHurtHoldUntil = Time.unscaledTime + Mathf.Max(0f, hurtDelay);
                    }
                    else
                    {
                        // 回血：延迟条直接抬到当前血量，不做「追赶」
                        oreHurtFill.fillAmount = fraction;
                    }
                }
                else
                {
                    oreHurtFill.fillAmount = fraction;
                }
            }

            SetText(oreHealthText, FormatHealthValue(safeCurrent));
            lastOreHealth = safeCurrent;
            lastOreMaxHealth = safeMax;
        }

        private void UpdateOreHealthBar()
        {
            if (oreHpFill == null || oreHurtFill == null)
            {
                return;
            }

            float target = oreHpFill.fillAmount;
            float hurt = oreHurtFill.fillAmount;

            if (hurt <= target)
            {
                if (hurt < target)
                {
                    oreHurtFill.fillAmount = target;
                }

                return;
            }

            if (Time.unscaledTime < oreHurtHoldUntil)
            {
                return;
            }

            float k = 1f - Mathf.Exp(-Mathf.Max(0.1f, hurtLerpSpeed) * Time.unscaledDeltaTime);
            hurt = Mathf.Lerp(hurt, target, k);

            if (hurt - target < 0.002f)
            {
                hurt = target;
            }

            oreHurtFill.fillAmount = hurt;
        }

        // ---------------- 关卡状态（2.3.7） ----------------

        /// <summary>
        /// 血条下方：小怪阶段显示剩余波次，BOSS 阶段换成倒计时进度条 + 秒数。
        /// 剩余 10 秒起变红并轻微抖动（策划案 2.3.6）。
        /// </summary>
        private void UpdateLevelStatus()
        {
            if (wavesText == null || countdownGroup == null)
            {
                return;
            }

            if (levelManager == null || (areas != null && areas.CurrentArea != GameArea.Mining))
            {
                SetLevelStatusVisible(false, false);
                return;
            }

            LevelEntrance entrance = levelManager.ActiveLevel;
            LevelProgress progress = entrance != null ? levelManager.GetProgress(entrance) : null;

            bool bossPhase = progress != null && progress.BossAppeared;
            int remainingWaves = levelManager.RemainingWaves;
            bool showWaves = progress != null && !bossPhase && remainingWaves > 0;
            bool showCountdown = levelManager.IsBossCountdownActive;

            SetLevelStatusVisible(showWaves, showCountdown);

            if (showWaves)
            {
                wavesText.text = $"剩余波次：{remainingWaves}";
            }

            if (!showCountdown)
            {
                return;
            }

            float remaining = levelManager.BossCountdownRemaining;
            float duration = Mathf.Max(0.0001f, levelManager.BossCountdownDuration);
            bool warning = remaining <= Mathf.Max(0f, countdownWarningSeconds);
            Color color = warning ? countdownWarningColor : countdownNormalColor;

            if (countdownFill != null)
            {
                countdownFill.fillAmount = Mathf.Clamp01(remaining / duration);
                countdownFill.color = color;
            }

            if (countdownText != null)
            {
                countdownText.color = color;
                countdownText.text = remaining.ToString("0.0",
                    System.Globalization.CultureInfo.InvariantCulture) + "s";

                // 最后 10 秒轻微左右 / 上下抖动，幅度很小，不遮挡血条
                if (warning)
                {
                    float time = Time.unscaledTime;
                    countdownText.rectTransform.anchoredPosition = countdownTextBasePosition +
                        new Vector2(Mathf.Sin(time * 42f) * 2.2f, Mathf.Sin(time * 31f) * 1.2f);
                }
                else
                {
                    countdownText.rectTransform.anchoredPosition = countdownTextBasePosition;
                }
            }
        }

        private void SetLevelStatusVisible(bool waves, bool countdown)
        {
            if (wavesText != null && wavesText.gameObject.activeSelf != waves)
            {
                wavesText.gameObject.SetActive(waves);
            }

            if (countdownGroup != null && countdownGroup.gameObject.activeSelf != countdown)
            {
                countdownGroup.gameObject.SetActive(countdown);
            }
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

        // ---------------- 资源栏动画 ----------------

        /// <summary>
        /// 资源栏在屏幕上的位置。垃圾收集器用它反算世界坐标里的飞行目标。
        /// 界面还没构建或不可见时返回 false。
        /// </summary>
        public bool TryGetResourceScreenPoint(out Vector2 screenPoint)
        {
            RectTransform target = coinBarRect != null
                ? coinBarRect
                : coinText != null ? coinText.rectTransform : null;

            if (target == null)
            {
                screenPoint = default;
                return false;
            }

            screenPoint = RectTransformUtility.WorldToScreenPoint(null, target.position);
            return true;
        }

        /// <summary>一块垃圾入账时调用：资源栏数字放大后缩回。</summary>
        public void PlayResourcePunch()
        {
            if (coinText == null)
            {
                return;
            }

            if (coinPunchRoutine != null)
            {
                StopCoroutine(coinPunchRoutine);
            }

            coinPunchRoutine = StartCoroutine(CoinPunchRoutine());
        }

        private IEnumerator CoinPunchRoutine()
        {
            float duration = Mathf.Max(0.05f, punchDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // 0 → 1 → 0：先旋转并放大，再转回来并缩小
                float phase = t < 0.45f
                    ? t / 0.45f
                    : 1f - (t - 0.45f) / 0.55f;
                float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phase));

                float scale = 1f + (Mathf.Max(1f, punchScale) - 1f) * eased;
                float angle = punchAngle * eased;

                if (coinText != null)
                {
                    RectTransform rect = coinText.rectTransform;
                    rect.localScale = coinTextBaseScale * scale;
                    rect.localRotation = coinTextBaseRotation * Quaternion.Euler(0f, 0f, angle);
                }

                if (coinIconImage != null)
                {
                    RectTransform rect = coinIconImage.rectTransform;
                    rect.localScale = coinIconBaseScale * scale;
                    rect.localRotation = coinIconBaseRotation;
                }

                yield return null;
            }

            ResetResourceTransform();
            coinPunchRoutine = null;
        }

        private void ResetResourceTransform()
        {
            if (coinText != null)
            {
                coinText.rectTransform.localScale = coinTextBaseScale;
                coinText.rectTransform.localRotation = coinTextBaseRotation;
            }

            if (coinIconImage != null)
            {
                coinIconImage.rectTransform.localScale = coinIconBaseScale;
                coinIconImage.rectTransform.localRotation = coinIconBaseRotation;
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
            authoringMode = false;
            BuildHierarchy();

            canvasObject.SetActive(false);
            Subscribe();
            RefreshAll();
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器菜单调用：把界面真实生成到场景里（而不是 Play 时生成），
        /// 之后就能在 Hierarchy 里直接拖控件调位置。运行时 Awake 会优先绑定这份 UI。
        /// </summary>
        public void BuildAuthoredHierarchyForEditor()
        {
            ResolveDependencies();
            authoringMode = true;
            built = false;

            // 重新生成前清掉上一次的 UI，避免场景里出现两份。
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);

                if (child.name == "MiningHUDRuntime" ||
                    child.GetComponent<MiningHudAuthoredRoot>() != null)
                {
                    DestroyImmediate(child.gameObject);
                }
            }

            BuildHierarchy();
            built = true;

            if (canvasObject != null)
            {
                // 编辑模式保持显示，方便直接摆位置。
                canvasObject.SetActive(true);
            }
        }
#endif

        private void BuildHierarchy()
        {
            canvasObject = new GameObject("MiningHUDRuntime",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.layer = 5;
            canvasObject.transform.SetParent(transform, false);

            if (authoringMode)
            {
                // 用组件标记，用户重命名根节点后运行时仍然找得到。
                canvasObject.AddComponent<MiningHudAuthoredRoot>();
            }

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

            BuildExitButton(root);
            BuildCoinBar(root);
            BuildEquippedPanel(root);
            BuildUpgradePanel(root);
            BuildOreBar(root);
            BuildLevelStatusRow(root);
            BuildTooltip(root);
        }

        /// <summary>
        /// 绑定场景里由编辑器生成的 UI。层级不完整时返回 false，交回运行时生成。
        /// </summary>
        private bool TryBindAuthoredHierarchy()
        {
            MiningHudAuthoredRoot marker =
                GetComponentInChildren<MiningHudAuthoredRoot>(true);
            Transform authored = marker != null
                ? marker.transform
                : FindDirectChild(transform, "MiningHUDRuntime");

            if (authored == null)
            {
                return false;
            }

            Canvas authoredCanvas = authored.GetComponent<Canvas>();

            if (authoredCanvas == null || !BindAuthoredReferences(authored))
            {
                Debug.LogWarning("[MiningHudUI] 场景里的可编辑 UI 不完整，改为运行时生成。", this);
                authored.gameObject.SetActive(false);
                DestroyObject(authored.gameObject);
                return false;
            }

            canvasObject = authored.gameObject;
            canvas = authoredCanvas;
            built = true;

            WireAuthoredEvents(authored);
            ApplyAuthoredRuntimeSprites();

            canvasObject.SetActive(false);
            Subscribe();
            RefreshAll();
            return true;
        }

        private bool BindAuthoredReferences(Transform canvasRoot)
        {
            Transform hud = canvasRoot.Find("Hud");

            if (hud == null)
            {
                return false;
            }

            coinBarRect = FindRect(hud, "CoinBar");
            coinText = FindText(hud, "CoinBar/CoinText");
            coinIconImage = FindImage(hud, "CoinBar/Icon/GarbageIcon");

            lightIconText = FindText(hud, "EquippedPanel/LightCard/Icon/Glyph");
            lightNameText = FindText(hud, "EquippedPanel/LightCard/Name");
            lightStatsText = FindText(hud, "EquippedPanel/LightCard/Stats");
            mineIconText = FindText(hud, "EquippedPanel/MineCard/Icon/Glyph");
            mineNameText = FindText(hud, "EquippedPanel/MineCard/Name");
            mineStatsText = FindText(hud, "EquippedPanel/MineCard/Stats");

            modeText = FindText(hud, "UpgradePanel/ModeButton/Label");
            feedbackText = FindText(hud, "UpgradePanel/Feedback");

            oreText = FindText(hud, "OreBar/OreText");
            oreHealthGroup = FindRect(hud, "OreBar/HealthGroup");
            oreHurtFill = FindImage(hud, "OreBar/HealthGroup/HpBar/HurtFill");
            oreHpFill = FindImage(hud, "OreBar/HealthGroup/HpBar/HpFill");
            oreHealthText = FindText(hud, "OreBar/HealthGroup/HpValue");

            // 血条只做展示，不允许拦截点击；旧版烘焙的场景也要在绑定时纠正一次
            if (oreText != null)
            {
                DisableRaycast(oreText.transform.parent);
            }

            wavesText = FindText(hud, "LevelStatusRow/WavesText");
            countdownGroup = FindRect(hud, "LevelStatusRow/CountdownGroup");
            countdownFill = FindImage(hud,
                "LevelStatusRow/CountdownGroup/CountdownBar/CountdownFill");
            countdownText = FindText(hud, "LevelStatusRow/CountdownGroup/CountdownText");

            tooltipRect = FindRect(hud, "Tooltip");
            tooltipText = FindText(hud, "Tooltip/Text");

            bool coreOk = coinBarRect != null && coinText != null && coinIconImage != null &&
                          lightIconText != null && lightNameText != null &&
                          lightStatsText != null && mineIconText != null &&
                          mineNameText != null && mineStatsText != null &&
                          modeText != null && feedbackText != null &&
                          oreText != null && oreHealthGroup != null &&
                          oreHurtFill != null && oreHpFill != null &&
                          oreHealthText != null && wavesText != null &&
                          countdownGroup != null && countdownFill != null &&
                          countdownText != null && tooltipRect != null &&
                          tooltipText != null;

            Transform lightContent = hud.Find("UpgradePanel/LightList/Content");
            Transform mineContent = hud.Find("UpgradePanel/MineList/Content");

            if (!coreOk || lightContent == null || mineContent == null)
            {
                return false;
            }

            coinTextBaseScale = coinText.rectTransform.localScale;
            coinTextBaseRotation = coinText.rectTransform.localRotation;
            coinIconBaseScale = coinIconImage.rectTransform.localScale;
            coinIconBaseRotation = coinIconImage.rectTransform.localRotation;
            countdownTextBasePosition = countdownText.rectTransform.anchoredPosition;

            BindAuthoredLightRows(lightContent);
            BindAuthoredMineRows(mineContent);
            return true;
        }

        private void BindAuthoredLightRows(Transform content)
        {
            lightRows.Clear();

            for (int i = 0; i < content.childCount; i++)
            {
                Transform child = content.GetChild(i);
                string prefix = "LightRow_";

                if (!child.name.StartsWith(prefix) ||
                    !int.TryParse(child.name.Substring(prefix.Length), out int level))
                {
                    continue;
                }

                ToolRow row = BindToolRow(child);

                if (row == null)
                {
                    continue;
                }

                row.Level = level;
                WireLightRow(row, level);
                lightRows.Add(row);
            }

            // 工具等级配置改过之后，场景里烘焙的行数可能对不上，运行时补建一份。
            if (lightTool != null && lightRows.Count != lightTool.MaxLevel)
            {
                RebuildLightRows((RectTransform)content);
            }
        }

        private void BindAuthoredMineRows(Transform content)
        {
            mineRows.Clear();

            for (int i = 0; i < content.childCount; i++)
            {
                Transform child = content.GetChild(i);

                if (child.name != "MineRow")
                {
                    continue;
                }

                ToolRow row = BindToolRow(child);

                if (row == null)
                {
                    continue;
                }

                WireMineRow(row);
                mineRows.Add(row);
                break;
            }

            if (mineRows.Count == 0)
            {
                RebuildMineRows((RectTransform)content);
            }
        }

        private static ToolRow BindToolRow(Transform rowTransform)
        {
            Image background = rowTransform.GetComponent<Image>();
            Text icon = FindText(rowTransform, "Icon/Glyph");
            Text name = FindText(rowTransform, "Name");
            Text gain = FindText(rowTransform, "Gain");
            Text cost = FindText(rowTransform, "Cost");
            Button button = FindButton(rowTransform, "Action");

            if (background == null || icon == null || name == null ||
                gain == null || cost == null || button == null)
            {
                return null;
            }

            return new ToolRow
            {
                Background = background,
                Icon = icon,
                Name = name,
                Gain = gain,
                Cost = cost,
                Button = button,
                ButtonText = button.GetComponentInChildren<Text>(),
                Group = rowTransform.GetComponent<CanvasGroup>(),
            };
        }

        private void WireAuthoredEvents(Transform canvasRoot)
        {
            Transform hud = canvasRoot.Find("Hud");

            if (hud == null)
            {
                return;
            }

            // 「返回主岛」已与「退出关卡」合并：旧版烘焙场景里的按钮直接隐藏
            Button back = FindButton(hud, "BackButton");

            if (back != null)
            {
                back.onClick.RemoveAllListeners();
                back.gameObject.SetActive(false);
            }

            Button exit = FindButton(hud, "LevelExitButton");

            if (exit != null)
            {
                exit.onClick.RemoveAllListeners();
                exit.onClick.AddListener(HandleExitLevelClicked);

                // 退出关卡按钮移到原「返回主岛」的左上角位置
                RectTransform exitRect = exit.transform as RectTransform;

                if (exitRect != null)
                {
                    exitRect.anchorMin = new Vector2(0f, 1f);
                    exitRect.anchorMax = new Vector2(0f, 1f);
                    exitRect.pivot = new Vector2(0f, 1f);
                    exitRect.anchoredPosition = new Vector2(32f, -26f);
                }
            }

            Button mode = FindButton(hud, "UpgradePanel/ModeButton");

            if (mode != null)
            {
                mode.onClick.RemoveAllListeners();
                mode.onClick.AddListener(HandleModeClicked);
                BindHoverTooltip(mode.gameObject, () => useMaxMode
                    ? "Max——升级到垃圾不足为止"
                    : "x1——每次升一级");
            }
        }

        private void WireLightRow(ToolRow row, int level)
        {
            row.Button.onClick.RemoveAllListeners();
            row.Button.onClick.AddListener(() => HandleLightRowClicked(level));
            BindHoverTooltip(row.Background.gameObject, () => DescribeLightTool(level));
        }

        private void WireMineRow(ToolRow row)
        {
            row.Button.onClick.RemoveAllListeners();
            row.Button.onClick.AddListener(HandleMineRowClicked);
            BindHoverTooltip(row.Background.gameObject, DescribeMiningTool);
        }

        private void ApplyAuthoredRuntimeSprites()
        {
            if (coinIconImage != null)
            {
                coinIconImage.sprite = ResolveResourceIconSprite();
            }

            Sprite fill = ResolveBarFillSprite();
            ApplyFillSprite(oreHurtFill, fill);
            ApplyFillSprite(oreHpFill, fill);
            ApplyFillSprite(countdownFill, fill);
        }

        private static void ApplyFillSprite(Image image, Sprite sprite)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = sprite;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
        }

        private static void DestroyObject(GameObject target)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(target);
                return;
            }
#endif
            Destroy(target);
        }

        /// <summary>
        /// 策划案 2.3.2：进入关卡后显示退出按钮。
        /// 与旧的「返回主岛」合并后放在左上角，原右上角改成只放垃圾资源栏。
        /// </summary>
        private void BuildExitButton(RectTransform root)
        {
            Button button = CreateButton(root, "LevelExitButton", "退出关卡", 18,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -26f),
                new Vector2(110f, 46f));

            button.onClick.AddListener(HandleExitLevelClicked);
        }

        private void BuildCoinBar(RectTransform root)
        {
            RectTransform panel = CreatePanel(root, "CoinBar",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-32f, -26f),
                new Vector2(240f, 46f));
            coinBarRect = panel;

            // 资源图标：默认运行时画一个垃圾桶；美术出图后拖到 resourceIconSprite 即可替换
            RectTransform iconBox = CreatePanel(panel, "Icon",
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f),
                new Vector2(34f, 34f), new Color(1f, 1f, 1f, 0.14f));

            RectTransform iconRect = CreateRect("GarbageIcon", iconBox);
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.offsetMin = new Vector2(2f, 2f);
            iconRect.offsetMax = new Vector2(-2f, -2f);

            coinIconImage = iconRect.gameObject.AddComponent<Image>();
            coinIconImage.sprite = authoringMode
                ? ResolveAuthoringSprite()
                : ResolveResourceIconSprite();
            coinIconImage.preserveAspect = true;
            coinIconImage.raycastTarget = false;
            coinIconBaseScale = iconRect.localScale;
            coinIconBaseRotation = iconRect.localRotation;

            coinText = CreateText(panel, "CoinText", 22,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(4f, 0f),
                new Vector2(190f, 40f), TextAnchor.MiddleRight, accentColor, true);
            coinText.text = "垃圾 0";
            coinTextBaseScale = coinText.rectTransform.localScale;
            coinTextBaseRotation = coinText.rectTransform.localRotation;
        }

        private Sprite ResolveResourceIconSprite()
        {
            if (resourceIconSprite != null)
            {
                return resourceIconSprite;
            }

            if (generatedIconSprite == null)
            {
                generatedIconSprite = GarbageIconFactory.Create();
            }

            return generatedIconSprite;
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

            CreateToolCard(panel, "MineCard", -225f, "刷",
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
            mineTitle.text = "清洁工具";

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
                ? "Max——升级到垃圾不足为止"
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

        private void RebuildLightRows(RectTransform content)
        {
            ClearChildren(content);
            lightRows.Clear();
            BuildLightRows(content);
        }

        private void RebuildMineRows(RectTransform content)
        {
            ClearChildren(content);
            mineRows.Clear();
            BuildMineRows(content);
        }

        private static void ClearChildren(RectTransform content)
        {
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject child = content.GetChild(i).gameObject;
                child.SetActive(false);
                DestroyObject(child);
            }
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
                ? "清洁工具未配置"
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

        /// <summary>
        /// 底部聚合体血条（策划案 2.2.5）：上方是种类与剩余块数，下方是「血条 + 大号剩余血量」。
        /// 血条分两层：红色当前血量即时缩短，黄色延迟条停留后追上，表现这一下掉了多少。
        /// </summary>
        private void BuildOreBar(RectTransform root)
        {
            RectTransform panel = CreatePanel(root, "OreBar",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 90f),
                new Vector2(580f, 84f));

            oreText = CreateText(panel, "OreText", 17,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f),
                new Vector2(552f, 22f), TextAnchor.MiddleCenter, dimTextColor, false);
            oreText.text = "当前垃圾聚合体";

            oreHealthGroup = CreateRect("HealthGroup", panel);
            oreHealthGroup.anchorMin = new Vector2(0.5f, 0f);
            oreHealthGroup.anchorMax = new Vector2(0.5f, 0f);
            oreHealthGroup.pivot = new Vector2(0.5f, 0f);
            oreHealthGroup.anchoredPosition = new Vector2(0f, 8f);
            oreHealthGroup.sizeDelta = new Vector2(552f, 48f);

            RectTransform bar = CreatePanel(oreHealthGroup, "HpBar",
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero,
                new Vector2(390f, 26f), new Color(0f, 0f, 0f, 0.55f));

            // 先建延迟条，再建当前血量条：后建的画在上层，红色挡住黄色
            oreHurtFill = CreateFillImage(bar, "HurtFill", healthHurtColor);
            oreHpFill = CreateFillImage(bar, "HpFill", healthFillColor);

            oreHealthText = CreateText(oreHealthGroup, "HpValue", 30,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero,
                new Vector2(150f, 44f), TextAnchor.MiddleRight, textColor, true);
            oreHealthText.text = "0";

            // 血条只做展示：整块关掉 Raycast Target，点击穿透到场景里
            DisableRaycast(panel);

            oreHealthGroup.gameObject.SetActive(false);
        }

        /// <summary>关闭整棵 UI 子树的点击拦截，让鼠标事件穿透到场景。</summary>
        private static void DisableRaycast(Transform root)
        {
            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
            }
        }

        /// <summary>
        /// 聚合体血条下方的一行关卡状态（策划案 2.3.7）：
        /// 小怪阶段显示「剩余波次」，BOSS 阶段切换成倒计时条 + 右侧秒数。
        /// </summary>
        private void BuildLevelStatusRow(RectTransform root)
        {
            RectTransform row = CreateRect("LevelStatusRow", root);
            row.anchorMin = new Vector2(0.5f, 0f);
            row.anchorMax = new Vector2(0.5f, 0f);
            row.pivot = new Vector2(0.5f, 0f);
            row.anchoredPosition = new Vector2(0f, 44f);
            row.sizeDelta = new Vector2(580f, 34f);

            wavesText = CreateText(row, "WavesText", 17,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(560f, 30f), TextAnchor.MiddleCenter, dimTextColor, false);
            wavesText.text = "剩余波次：0";

            countdownGroup = CreateRect("CountdownGroup", row);
            countdownGroup.anchorMin = new Vector2(0.5f, 0.5f);
            countdownGroup.anchorMax = new Vector2(0.5f, 0.5f);
            countdownGroup.pivot = new Vector2(0.5f, 0.5f);
            countdownGroup.anchoredPosition = Vector2.zero;
            countdownGroup.sizeDelta = new Vector2(580f, 34f);

            RectTransform bar = CreatePanel(countdownGroup, "CountdownBar",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-56f, 0f),
                new Vector2(390f, 16f), new Color(0f, 0f, 0f, 0.55f));

            countdownFill = CreateFillImage(bar, "CountdownFill", countdownNormalColor);

            countdownTextBasePosition = new Vector2(201f, 0f);
            countdownText = CreateText(countdownGroup, "CountdownText", 22,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), countdownTextBasePosition,
                new Vector2(100f, 30f), TextAnchor.MiddleLeft, countdownNormalColor, true);
            countdownText.text = "30.0s";

            countdownGroup.gameObject.SetActive(false);
            wavesText.gameObject.SetActive(false);
        }

        private Image CreateFillImage(Transform parent, string name, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(2f, 2f);
            rect.offsetMax = new Vector2(-2f, -2f);

            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = authoringMode ? ResolveAuthoringSprite() : ResolveBarFillSprite();
            image.color = color;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            image.fillAmount = 1f;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Image.Type.Filled 必须有 Sprite 才会按 fillAmount 裁切，这里用 1×1 白图。</summary>
        private Sprite ResolveBarFillSprite()
        {
            if (barFillSprite != null)
            {
                return barFillSprite;
            }

            Texture2D texture = Texture2D.whiteTexture;
            barFillSprite = Sprite.Create(
                texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            barFillSprite.name = "MiningHudBarFill";
            barFillSprite.hideFlags = HideFlags.HideAndDontSave;
            return barFillSprite;
        }

        /// <summary>
        /// 编辑器烘焙时用内置 UI Sprite 占位：它是工程里的真实资源引用，
        /// 能随场景序列化；运行时绑定会换回运行时生成的图标 / 白图。
        /// </summary>
        private Sprite ResolveAuthoringSprite()
        {
            if (authoringSprite != null)
            {
                return authoringSprite;
            }

#if UNITY_EDITOR
            authoringSprite = UnityEditor.AssetDatabase.GetBuiltinExtraResource<Sprite>(
                "UI/Skin/UISprite.psd");
#endif
            return authoringSprite;
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
            // 编辑器烘焙不挂这个组件：它定义在 MiningHudUI.cs 里没有独立脚本资源，
            // 序列化进场景会变成 Missing Script。运行时绑定阶段再补。
            if (authoringMode)
            {
                return;
            }

            BindHoverTooltip(target, content);
        }

        private void BindHoverTooltip(GameObject target, System.Func<string> content)
        {
            HoverTooltipTrigger trigger = target.GetComponent<HoverTooltipTrigger>();

            if (trigger == null)
            {
                trigger = target.AddComponent<HoverTooltipTrigger>();
            }

            trigger.Bind(this, content);
        }

        // ---------------- 小工具 ----------------

        private static string IconGlyph(string label)
        {
            return string.IsNullOrEmpty(label) ? "?" : label.Substring(0, 1);
        }

        /// <summary>
        /// 垃圾缩写：1000 → 1k，1,500,000 → 1.5m；
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

        /// <summary>
        /// 聚合体血量文字（策划案 2.2.5）：千位以上换 k / m / b / t，且只保留一位小数。
        /// 例：10918 → 10.9k，1234567 → 1.2m；999 以下不足整数的显示一位小数（39.5）。
        /// </summary>
        public static string FormatHealthValue(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            {
                return "0";
            }

            if (value < 1000f)
            {
                return value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            }

            string[] units = { string.Empty, "k", "m", "b", "t" };
            double scaled = value;
            int unit = 0;

            // 999.95 起就算 1000.0，提前进下一档，避免显示「1000.0k」
            while (scaled >= 999.95d && unit < units.Length - 1)
            {
                scaled /= 1000d;
                unit++;
            }

            string suffix = units[unit];

            if (scaled >= 999.95d)
            {
                int extra = 0;

                while (scaled >= 999.95d)
                {
                    scaled /= 1000d;
                    extra++;
                }

                suffix = SuffixFor(extra);
            }

            return scaled.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + suffix;
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

        private static Transform FindDirectChild(Transform parent, string childName)
        {
            if (parent == null)
            {
                return null;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);

                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        private static RectTransform FindRect(Transform root, string path)
        {
            Transform found = root != null ? root.Find(path) : null;
            return found as RectTransform;
        }

        private static Text FindText(Transform root, string path)
        {
            Transform found = root != null ? root.Find(path) : null;
            return found != null ? found.GetComponent<Text>() : null;
        }

        private static Image FindImage(Transform root, string path)
        {
            Transform found = root != null ? root.Find(path) : null;
            return found != null ? found.GetComponent<Image>() : null;
        }

        private static Button FindButton(Transform root, string path)
        {
            Transform found = root != null ? root.Find(path) : null;
            return found != null ? found.GetComponent<Button>() : null;
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