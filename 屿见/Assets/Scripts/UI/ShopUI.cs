using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Yujian.Shop;

namespace Yujian.UI
{
    /// <summary>
    /// 商店面板（最小实现）。
    /// 职责：把 MaterialData 与库存/金币显示出来，把按钮点击转交给 Shop.TryBuy()。
    /// 严格只读：显示的金币与库存都从 Shop 暴露的只读属性取，本类不修改任何数值。
    /// </summary>
    public class ShopUI : MonoBehaviour
    {
        /// <summary>一个颜色按钮的绑定。用数组而不是三个固定字段，新增颜色时只需在 Inspector 加一行。</summary>
        [Serializable]
        private struct ColorButtonBinding
        {
            [SerializeField] private MaterialColor color;
            [SerializeField] private Button button;

            public MaterialColor Color => color;
            public Button Button => button;
        }

        /// <summary>运行时把颜色、按钮、回调绑在一起，方便精确地订阅与退订。</summary>
        private sealed class BoundButton
        {
            public MaterialColor Color;
            public Button Button;
            public UnityAction Action;
        }

        [Header("依赖")]
        [Tooltip("购买入口。UI 只通过它下单")]
        // 必须写全限定名：类 Shop 与命名空间 Yujian.Shop 同名，在 Yujian.UI 里直接写 Shop
        // 会被解析成命名空间并报 CS0118。这不是笔误，请勿"简化"。
        [SerializeField] private Yujian.Shop.Shop shop;

        [Tooltip("本面板销售的原料。MVP 只卖一种，阶段 4 再扩展成列表")]
        [SerializeField] private MaterialData material;

        [Header("文本")]
        [SerializeField] private Text titleText;
        [Tooltip("显示原料名与类型，例如「小砖（砖块）」")]
        [SerializeField] private Text nameText;
        [SerializeField] private Text priceText;
        [SerializeField] private Text effectText;
        [SerializeField] private Text moneyText;
        [SerializeField] private Text inventoryText;
        [Tooltip("状态行：成功购买 / 当前货币不足")]
        [SerializeField] private Text statusText;

        [Header("颜色按钮")]
        [Tooltip("每个颜色一个按钮。新增颜色在这里加一行即可，不需要改代码")]
        [SerializeField] private ColorButtonBinding[] colorButtons = new ColorButtonBinding[0];

        [Header("文案")]
        [SerializeField] private string title = "原料商店";
        [SerializeField] private string successMessage = "成功购买";
        [SerializeField] private string insufficientMessage = "当前货币不足";
        [Tooltip("状态行自动清空的秒数。<=0 表示一直保留")]
        [SerializeField] private float statusClearDelay = 2f;

        [Header("配色")]
        [SerializeField] private Color successColor = new Color(0.24f, 0.65f, 0.32f);
        [SerializeField] private Color failureColor = new Color(0.80f, 0.22f, 0.20f);

        private readonly List<BoundButton> boundButtons = new List<BoundButton>();
        private float statusClearTime = -1f;
        private bool warnedMissingMoneySource;

        private void Awake()
        {
            ApplyBuiltinFontToUnassignedTexts();
            BuildButtonBindings();
            RefreshStaticText();
        }

        private void OnEnable()
        {
            for (int i = 0; i < boundButtons.Count; i++)
            {
                boundButtons[i].Button.onClick.AddListener(boundButtons[i].Action);
            }

            if (shop != null)
            {
                if (shop.Currency != null)
                {
                    shop.Currency.OnMoneyChanged += HandleMoneyChanged;
                }

                if (shop.Inventory != null)
                {
                    shop.Inventory.OnInventoryChanged += HandleInventoryChanged;
                }
            }

            ClearStatus();
            RefreshMoney();
            RefreshInventory();
        }

        private void OnDisable()
        {
            for (int i = 0; i < boundButtons.Count; i++)
            {
                boundButtons[i].Button.onClick.RemoveListener(boundButtons[i].Action);
            }

            if (shop != null)
            {
                if (shop.Currency != null)
                {
                    shop.Currency.OnMoneyChanged -= HandleMoneyChanged;
                }

                if (shop.Inventory != null)
                {
                    shop.Inventory.OnInventoryChanged -= HandleInventoryChanged;
                }
            }
        }

        private void Update()
        {
            if (statusClearTime < 0f || Time.unscaledTime < statusClearTime)
            {
                return;
            }

            statusClearTime = -1f;
            ClearStatus();
        }

        /// <summary>把颜色按钮整理成"颜色 → 按钮 → 回调"的列表，顺便揪出配错的地方。</summary>
        private void BuildButtonBindings()
        {
            boundButtons.Clear();

            if (colorButtons == null || colorButtons.Length == 0)
            {
                Debug.LogError("[ShopUI] 没有配置任何颜色按钮，商店将无法购买。", this);
                return;
            }

            if (material == null)
            {
                Debug.LogError("[ShopUI] MaterialData 未配置，商店面板不知道在卖什么。", this);
                return;
            }

            for (int i = 0; i < colorButtons.Length; i++)
            {
                ColorButtonBinding entry = colorButtons[i];

                if (entry.Button == null)
                {
                    Debug.LogError($"[ShopUI] 颜色 {entry.Color} 没有绑定按钮。", this);
                    continue;
                }

                if (!material.SupportsColor(entry.Color))
                {
                    // 与其在运行时给出误导性的"当前货币不足"，不如现在就禁用并说清原因
                    Debug.LogWarning($"[ShopUI] 原料「{material.MaterialName}」不支持颜色 {entry.Color}，" +
                                     "该按钮已禁用。请检查 MaterialData 的允许颜色，或改掉这个按钮绑定的颜色。", this);
                    entry.Button.interactable = false;
                    continue;
                }

                // 闭包捕获：必须复制到局部变量，否则所有回调都会用最后一次迭代的颜色
                MaterialColor captured = entry.Color;

                boundButtons.Add(new BoundButton
                {
                    Color = captured,
                    Button = entry.Button,
                    Action = () => HandlePurchaseClicked(captured),
                });
            }
        }

        private void HandlePurchaseClicked(MaterialColor color)
        {
            if (shop == null)
            {
                Debug.LogError("[ShopUI] Shop 未配置，无法购买。", this);
                return;
            }

            if (material == null)
            {
                Debug.LogError("[ShopUI] MaterialData 未配置，无法购买。", this);
                return;
            }

            bool success = shop.TryBuy(material, color);

            ShowStatus(success ? successMessage : insufficientMessage, success);
        }

        private void RefreshStaticText()
        {
            if (titleText != null)
            {
                titleText.text = title;
            }

            if (material == null)
            {
                return;
            }

            if (nameText != null)
            {
                nameText.text = $"{material.MaterialName}（{material.MaterialType.ToChineseName()}）";
            }

            if (priceText != null)
            {
                priceText.text = $"价格：{material.Price}";
            }

            if (effectText != null)
            {
                effectText.text = $"效果：{material.EffectDescription}";
            }
        }

        private void RefreshMoney()
        {
            if (moneyText == null)
            {
                return;
            }

            if (shop == null || shop.Currency == null)
            {
                if (!warnedMissingMoneySource)
                {
                    warnedMissingMoneySource = true;
                    Debug.LogWarning("[ShopUI] 拿不到 PlayerCurrency，金币显示将一直为 0。", this);
                }

                moneyText.text = "金币：?";
                return;
            }

            moneyText.text = $"金币：{shop.Currency.CurrentMoney}";
        }

        private void RefreshInventory()
        {
            if (inventoryText == null)
            {
                return;
            }

            if (shop == null || shop.Inventory == null)
            {
                inventoryText.text = "库存：?";
                return;
            }

            IReadOnlyList<MaterialStack> stacks = shop.Inventory.Stacks;
            StringBuilder builder = new StringBuilder("库存：");
            bool first = true;

            for (int i = 0; i < stacks.Count; i++)
            {
                MaterialStack stack = stacks[i];

                if (stack.Material == null || stack.Count <= 0)
                {
                    continue;
                }

                if (!first)
                {
                    builder.Append("  |  ");
                }

                builder.Append($"{stack.Color.ToChineseName()}{stack.Material.MaterialName} ×{stack.Count}");
                first = false;
            }

            if (first)
            {
                builder.Append("空");
            }

            inventoryText.text = builder.ToString();
        }

        private void ShowStatus(string message, bool success)
        {
            if (statusText == null)
            {
                return;
            }

            statusText.text = message;
            statusText.color = success ? successColor : failureColor;
            statusClearTime = statusClearDelay > 0f ? Time.unscaledTime + statusClearDelay : -1f;
        }

        private void ClearStatus()
        {
            statusClearTime = -1f;

            if (statusText != null)
            {
                statusText.text = string.Empty;
            }
        }

        private void HandleMoneyChanged(int _)
        {
            RefreshMoney();
        }

        private void HandleInventoryChanged()
        {
            RefreshInventory();
        }

        /// <summary>
        /// 给没指定字体的 Text 补上 Unity 内置的 LegacyRuntime.ttf。
        /// 项目规则要求不引入 TextMeshPro，而 legacy Text 不指定字体就不显示任何文字。
        /// 已经手动指定过字体的不动。
        /// </summary>
        private void ApplyBuiltinFontToUnassignedTexts()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            if (font == null)
            {
                Debug.LogWarning("[ShopUI] 找不到内置字体 LegacyRuntime.ttf，未指定字体的 Text 将不显示文字。", this);
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
