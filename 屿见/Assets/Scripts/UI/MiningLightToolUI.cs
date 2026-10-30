using UnityEngine;
using UnityEngine.UI;
using Yujian.Mining;
using Yujian.Player;

namespace Yujian.UI
{
    /// <summary>
    /// 商店里的照明工具购买卡（D17=A）。
    /// 只负责显示与转发：等级、暴击点上限、停留时长、价格从 MiningLightTool 读取，
    /// 点击后仍由 MiningLightTool.TryUpgrade() 扣钱并升级。
    /// </summary>
    [DisallowMultipleComponent]
    public class MiningLightToolUI : MonoBehaviour
    {
        [Header("依赖")]
        [SerializeField] private MiningLightTool tool;
        [SerializeField] private PlayerCurrency currency;

        [Header("显示")]
        [SerializeField] private Text titleText;
        [SerializeField] private Text statsText;
        [SerializeField] private Text costText;
        [SerializeField] private Button buyButton;
        [SerializeField] private Text buyButtonText;
        [SerializeField] private Text feedbackText;

        [Header("配色")]
        [SerializeField] private Color costColor = new Color(1f, 0.85f, 0.42f, 1f);
        [SerializeField] private Color successColor = new Color(0.35f, 0.9f, 0.45f, 1f);
        [SerializeField] private Color failureColor = new Color(1f, 0.4f, 0.4f, 1f);

        [Header("反馈")]
        [SerializeField] private float feedbackClearDelay = 2f;

        private float feedbackClearTime = -1f;
        private bool subscribed;
        private bool warnedMissingReferences;

        /// <summary>编辑器向导调用，把场景引用一次性接好。</summary>
        public void ConfigureForSetup(MiningLightTool newTool, PlayerCurrency newCurrency,
            Text newTitleText, Text newStatsText, Text newCostText, Button newBuyButton,
            Text newBuyButtonText, Text newFeedbackText)
        {
            tool = newTool;
            currency = newCurrency;
            titleText = newTitleText;
            statsText = newStatsText;
            costText = newCostText;
            buyButton = newBuyButton;
            buyButtonText = newBuyButtonText;
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

            if (buyButton != null)
            {
                buyButton.onClick.RemoveListener(HandleBuyClicked);
                buyButton.onClick.AddListener(HandleBuyClicked);
            }

            ClearFeedback();
            RefreshBase();
        }

        private void OnDisable()
        {
            Unsubscribe();

            if (buyButton != null)
            {
                buyButton.onClick.RemoveListener(HandleBuyClicked);
            }
        }

        private void Start()
        {
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
                tool = FindFirstObjectByType<MiningLightTool>(FindObjectsInactive.Include);
            }

            if (currency == null && tool != null)
            {
                // MiningLightTool 没有公开 Currency，退回到场景里的 PlayerCurrency
                currency = FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
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

        private void HandleBuyClicked()
        {
            if (tool == null)
            {
                ShowFeedback("照明工具未配置，无法购买。", false);
                return;
            }

            if (tool.IsMaxed)
            {
                ShowFeedback("已经买到最高等级了。", true);
                RefreshBase();
                return;
            }

            if (currency == null)
            {
                ShowFeedback("没有读到玩家金币，无法购买。", false);
                return;
            }

            int cost = tool.NextUpgradeCost;
            int moneyBefore = currency.CurrentMoney;
            string nextName = tool.NextLevelName;

            if (!tool.TryUpgrade())
            {
                int missing = Mathf.Max(0, cost - moneyBefore);
                ShowFeedback(missing > 0 ? $"金币不足，还差 {missing}。" : "购买失败，请检查 Console。", false);
                RefreshBase();
                return;
            }

            RefreshBase();
            ShowFeedback($"已获得「{nextName}」，暴击点上限 {tool.CritPointCapacity}。", true);
        }

        private void HandleToolChanged(MiningLightTool _)
        {
            RefreshBase();
        }

        private void HandleMoneyChanged(int _)
        {
            RefreshBase();
        }

        private void RefreshBase()
        {
            if (tool == null)
            {
                SetText(titleText, "照明工具");
                SetText(statsText, "未配置 MiningLightTool");
                SetText(costText, "请运行「屿见/挖矿/配置照明工具」");
                SetText(buyButtonText, "无法购买");
                SetCostColor(false);
                SetButtonInteractable(false);
                return;
            }

            SetText(titleText, $"照明工具 · {tool.LevelName}");

            if (tool.IsMaxed)
            {
                SetText(statsText, $"暴击点上限 {tool.CritPointCapacity}　停留 {tool.CritPointLifetime:0.#} 秒");
                SetText(costText, "已满级");
                SetText(buyButtonText, "已满级");
                SetCostColor(true);
                SetButtonInteractable(false);
                return;
            }

            SetText(statsText, $"暴击点上限 {tool.CritPointCapacity} → {tool.NextCritPointCapacity}　" +
                               $"停留 {tool.CritPointLifetime:0.#} → {tool.NextCritPointLifetime:0.#} 秒");

            if (currency == null)
            {
                SetText(costText, "没有读到玩家金币");
                SetText(buyButtonText, "无法购买");
                SetCostColor(false);
                SetButtonInteractable(false);
                return;
            }

            int cost = tool.NextUpgradeCost;
            int money = currency.CurrentMoney;
            bool canAfford = cost <= 0 || money >= cost;

            SetText(costText, canAfford
                ? $"购买「{tool.NextLevelName}」需要 {cost} 金币 · 当前 {money}"
                : $"需要 {cost} 金币 · 当前 {money}，还差 {cost - money}");
            SetText(buyButtonText, canAfford ? $"购买{tool.NextLevelName}" : "金币不足");
            SetCostColor(canAfford);
            SetButtonInteractable(canAfford);
        }

        private void SetButtonInteractable(bool value)
        {
            if (buyButton != null)
            {
                buyButton.interactable = value;
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
                           buyButton == null || buyButtonText == null || feedbackText == null;

            if (missing)
            {
                warnedMissingReferences = true;
                Debug.LogWarning("[MiningLightToolUI] 有显示引用未接好，购买卡会显示不全。" +
                                 "请重跑菜单「屿见/挖矿/配置照明工具」。", this);
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
