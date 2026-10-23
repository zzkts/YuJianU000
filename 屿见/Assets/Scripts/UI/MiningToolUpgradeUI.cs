using UnityEngine;
using UnityEngine.UI;
using Yujian.Mining;
using Yujian.Player;

namespace Yujian.UI
{
    /// <summary>
    /// 商店里的挖矿工具升级卡。
    /// 只负责显示与转发：等级、攻击力和花费从 MiningToolProgress 读取，
    /// 点击后仍由 MiningToolProgress.TryUpgrade() 扣钱并升级。
    /// </summary>
    [DisallowMultipleComponent]
    public class MiningToolUpgradeUI : MonoBehaviour
    {
        [Header("依赖")]
        [SerializeField] private MiningToolProgress tool;
        [SerializeField] private PlayerCurrency currency;

        [Header("显示")]
        [SerializeField] private Text titleText;
        [SerializeField] private Text statsText;
        [SerializeField] private Text costText;
        [SerializeField] private Button upgradeButton;
        [SerializeField] private Text upgradeButtonText;
        [SerializeField] private Text feedbackText;

        [Header("配色")]
        [SerializeField] private Color costColor = new Color(1f, 0.85f, 0.42f, 1f);
        [SerializeField] private Color successColor = new Color(0.35f, 0.9f, 0.45f, 1f);
        [SerializeField] private Color failureColor = new Color(1f, 0.4f, 0.4f, 1f);

        [Header("反馈")]
        [Tooltip("升级结果提示保留的秒数。<=0 表示一直保留")]
        [SerializeField] private float feedbackClearDelay = 2f;

        private float feedbackClearTime = -1f;
        private bool subscribed;
        private bool warnedMissingReferences;

        /// <summary>编辑器向导调用，把场景引用一次性接好。</summary>
        public void ConfigureForSetup(MiningToolProgress newTool, PlayerCurrency newCurrency,
            Text newTitleText, Text newStatsText, Text newCostText, Button newUpgradeButton,
            Text newUpgradeButtonText, Text newFeedbackText)
        {
            tool = newTool;
            currency = newCurrency;
            titleText = newTitleText;
            statsText = newStatsText;
            costText = newCostText;
            upgradeButton = newUpgradeButton;
            upgradeButtonText = newUpgradeButtonText;
            feedbackText = newFeedbackText;
        }

        private void Awake()
        {
            ApplyBuiltinFontToUnassignedTexts();
            ResolveDependencies();
            ValidateReferences();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            Subscribe();

            if (upgradeButton != null)
            {
                upgradeButton.onClick.RemoveListener(HandleUpgradeClicked);
                upgradeButton.onClick.AddListener(HandleUpgradeClicked);
            }

            ClearFeedback();
            RefreshBase();
        }

        private void OnDisable()
        {
            Unsubscribe();

            if (upgradeButton != null)
            {
                upgradeButton.onClick.RemoveListener(HandleUpgradeClicked);
            }
        }

        private void Start()
        {
            // 等所有 Awake 跑完再刷一次，确保读到工具的初始等级。
            RefreshBase();
        }

        private void Update()
        {
            if (feedbackClearTime < 0f || Time.unscaledTime < feedbackClearTime)
            {
                return;
            }

            ClearFeedback();
        }

        private void ResolveDependencies()
        {
            if (tool == null)
            {
                tool = FindFirstObjectByType<MiningToolProgress>(FindObjectsInactive.Include);
            }

            if (currency == null && tool != null)
            {
                currency = tool.Currency;
            }

            if (currency == null)
            {
                currency = FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            }
        }

        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            if (tool != null)
            {
                tool.OnToolChanged += HandleToolChanged;
            }

            if (currency != null)
            {
                currency.OnMoneyChanged += HandleMoneyChanged;
            }

            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            if (tool != null)
            {
                tool.OnToolChanged -= HandleToolChanged;
            }

            if (currency != null)
            {
                currency.OnMoneyChanged -= HandleMoneyChanged;
            }

            subscribed = false;
        }

        private void HandleUpgradeClicked()
        {
            if (tool == null || tool.Data == null)
            {
                ShowFeedback("工具数据未配置，无法升级。", false);
                return;
            }

            if (currency == null)
            {
                ShowFeedback("没有读到玩家金币，无法升级。", false);
                return;
            }

            int cost = tool.NextUpgradeCost;
            int moneyBefore = currency.CurrentMoney;

            if (!tool.TryUpgrade())
            {
                int missing = Mathf.Max(0, cost - moneyBefore);
                ShowFeedback(missing > 0 ? $"金币不足，还差 {missing}。" : "升级失败，请检查 Console。", false);
                RefreshBase();
                return;
            }

            RefreshBase();
            ShowFeedback($"升级成功，攻击力提升到 {tool.CurrentAttack}。", true);
        }

        private void HandleToolChanged(MiningToolProgress _)
        {
            RefreshBase();
        }

        private void HandleMoneyChanged(int _)
        {
            RefreshBase();
        }

        private void RefreshBase()
        {
            if (tool == null || tool.Data == null)
            {
                SetText(titleText, "挖矿工具");
                SetText(statsText, "工具数据尚未配置");
                SetText(costText, "请先运行「配置挖矿工具升级（阶段 9）」");
                SetText(upgradeButtonText, "无法升级");
                SetCostColor(false);
                SetButtonInteractable(false);
                return;
            }

            int level = tool.CurrentLevel;
            int attack = tool.CurrentAttack;
            int nextAttack = tool.Data.GetAttackAtLevel(level + 1);

            SetText(titleText, $"挖矿工具 · {tool.Data.DisplayName}（{tool.Data.ItemId}）");
            SetText(statsText, $"等级 {level}　攻击力 {attack} → {nextAttack}");

            if (currency == null)
            {
                SetText(costText, "没有读到玩家金币");
                SetText(upgradeButtonText, "无法升级");
                SetCostColor(false);
                SetButtonInteractable(false);
                return;
            }

            int cost = tool.NextUpgradeCost;
            int money = currency.CurrentMoney;
            bool canAfford = cost <= 0 || money >= cost;

            SetText(costText, canAfford
                ? $"升级花费 {cost} 金币 · 当前 {money}"
                : $"需要 {cost} 金币 · 当前 {money}，还差 {cost - money}");
            SetText(upgradeButtonText, canAfford ? "升级" : "金币不足");
            SetCostColor(canAfford);
            SetButtonInteractable(canAfford);
        }

        private void SetButtonInteractable(bool value)
        {
            if (upgradeButton != null)
            {
                upgradeButton.interactable = value;
            }
        }

        private void SetCostColor(bool normal)
        {
            if (costText != null)
            {
                costText.color = normal ? costColor : failureColor;
            }
        }

        private void ShowFeedback(string message, bool success)
        {
            if (feedbackText == null)
            {
                return;
            }

            feedbackText.text = message;
            feedbackText.color = success ? successColor : failureColor;
            feedbackClearTime = feedbackClearDelay > 0f
                ? Time.unscaledTime + feedbackClearDelay
                : -1f;
        }

        private void ClearFeedback()
        {
            feedbackClearTime = -1f;

            if (feedbackText != null)
            {
                feedbackText.text = string.Empty;
            }
        }

        private static void SetText(Text target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }

        private void ValidateReferences()
        {
            if (warnedMissingReferences)
            {
                return;
            }

            bool missing = titleText == null || statsText == null || costText == null ||
                           upgradeButton == null || upgradeButtonText == null || feedbackText == null;

            if (missing)
            {
                warnedMissingReferences = true;
                Debug.LogWarning("[MiningToolUpgradeUI] 有显示引用未接好，升级卡会显示不全。" +
                                 "请重跑菜单「屿见/配置挖矿工具升级按钮（阶段 9）」。", this);
            }
        }

        private void ApplyBuiltinFontToUnassignedTexts()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            if (font == null)
            {
                return;
            }

            Text[] texts = GetComponentsInChildren<Text>(true);

            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i].font == null)
                {
                    texts[i].font = font;
                }
            }
        }
    }
}
