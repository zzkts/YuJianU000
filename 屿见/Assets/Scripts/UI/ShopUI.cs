using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Shop;

namespace Yujian.UI
{
    /// <summary>
    /// 轻量化原料商店面板（2026-11-05 商店策划案）。
    ///
    /// 职责：按「商品目录」为每种原料克隆一个 ShopItemUI 条目，维护当前选中的原料，
    /// 在右侧显示单价 / 总价，并把购买请求转交给 Shop.TryBuy()。
    /// 严格只读：金币从 Shop 暴露的只读属性取，本类不直接修改任何数值。
    ///
    /// 布局是 240×90 的固定小窗：
    ///   标题 / 金币（顶部）
    ///   ├─ 左侧：可滚轮浏览的原料列表（名称、样式、说明、颜色、数量）
    ///   └─ 右侧：单价、总价、购买
    ///   底部：成功 / 余量不足提示
    ///
    /// 颜色只展示不可选；购买时按原料默认色入库（颜色选择功能留给后续建筑系统）。
    /// </summary>
    public class ShopUI : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("购买入口。UI 只通过它下单")]
        // 必须写全限定名：类 Shop 与命名空间 Yujian.Shop 同名，在 Yujian.UI 里直接写 Shop
        // 会被解析成命名空间并报 CS0118。这不是笔误，请勿"简化"。
        [SerializeField] private Yujian.Shop.Shop shop;

        [Header("商品目录")]
        [Tooltip("面板会为这里的每一种原料克隆一个条目。加原料只需往数组里加一项，不用改代码")]
        [SerializeField] private MaterialData[] materials = new MaterialData[0];

        [Header("文本")]
        [SerializeField] private Text titleText;
        [SerializeField] private Text moneyText;

        [Tooltip("右侧价格：两行，单价与本次总价")]
        [SerializeField] private Text priceText;

        [Tooltip("底部状态行：成功购买 / 当前货币不足")]
        [SerializeField] private Text statusText;

        [Header("条目列表")]
        [Tooltip("克隆出来的条目的父物体（ScrollRect 的 Content）")]
        [SerializeField] private Transform itemContainer;

        [Tooltip("条目模板。运行时按目录克隆，本物体应当保持隐藏")]
        [SerializeField] private RectTransform itemTemplate;

        [Header("购买")]
        [SerializeField] private Button buyButton;

        [Header("文案")]
        [SerializeField] private string title = "原料商店";
        [SerializeField] private string successMessage = "成功购买";
        [SerializeField] private string insufficientMessage = "当前货币不足";
        [SerializeField] private string failureMessage = "购买失败";
        [Tooltip("状态行自动清空的秒数。<=0 表示一直保留")]
        [SerializeField] private float statusClearDelay = 2f;

        [Header("配色")]
        [SerializeField] private Color successColor = new Color(0.24f, 0.65f, 0.32f);
        [SerializeField] private Color failureColor = new Color(0.80f, 0.22f, 0.20f);

        private readonly List<ShopItemUI> items = new List<ShopItemUI>();

        /// <summary>当前选中的条目。目录为空时为 null。</summary>
        private ShopItemUI selected;

        private float statusClearTime = -1f;
        private bool warnedMissingMoneySource;

        /// <summary>当前选中的原料，可能为 null。</summary>
        public MaterialData SelectedMaterial => selected != null ? selected.Material : null;

        /// <summary>当前选中的单次购买数量。</summary>
        public int SelectedQuantity => selected != null ? selected.Quantity : 0;

        private void Awake()
        {
            ApplyBuiltinFontToUnassignedTexts();

            if (itemTemplate != null)
            {
                // 模板本身永远不参与显示，只用来克隆。
                // 它必须是隐藏的，否则会在列表里多出一个空条目。
                itemTemplate.gameObject.SetActive(false);
            }
            else
            {
                Debug.LogError("[ShopUI] 条目模板未配置，商店不会显示任何原料。" +
                               "请重跑菜单「屿见/商店/重建轻量化商店窗口」。", this);
            }

            BuildItems();
            RefreshStaticText();
            RefreshMoney();
            RefreshPrice();
        }

        private void OnEnable()
        {
            if (shop != null && shop.Currency != null)
            {
                shop.Currency.OnMoneyChanged += HandleMoneyChanged;
            }

            if (buyButton != null)
            {
                buyButton.onClick.AddListener(HandleBuyClicked);
            }

            ClearStatus();
            RefreshMoney();
            RefreshPrice();
        }

        private void OnDisable()
        {
            if (shop != null && shop.Currency != null)
            {
                shop.Currency.OnMoneyChanged -= HandleMoneyChanged;
            }

            if (buyButton != null)
            {
                buyButton.onClick.RemoveListener(HandleBuyClicked);
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

        /// <summary>选中某个原料条目。价格与购买都跟随当前选中项。</summary>
        public void SelectItem(ShopItemUI item)
        {
            if (item == null || item.Material == null)
            {
                return;
            }

            selected = item;

            for (int i = 0; i < items.Count; i++)
            {
                items[i].SetSelected(items[i] == item);
            }

            RefreshPrice();
        }

        /// <summary>条目数量变化时由条目调用：如果它是当前选中项，刷新右侧总价。</summary>
        public void NotifyQuantityChanged(ShopItemUI item)
        {
            if (item == selected)
            {
                RefreshPrice();
            }
        }

        /// <summary>按目录重建条目。幂等：先清掉上次克隆出来的，再按目录重新克隆。</summary>
        private void BuildItems()
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null)
                {
                    Destroy(items[i].gameObject);
                }
            }

            items.Clear();
            selected = null;

            if (itemContainer == null || itemTemplate == null)
            {
                return;
            }

            if (materials == null || materials.Length == 0)
            {
                Debug.LogWarning("[ShopUI] 商品目录是空的，商店里不会出现任何原料。", this);
                return;
            }

            for (int i = 0; i < materials.Length; i++)
            {
                MaterialData data = materials[i];

                if (data == null)
                {
                    Debug.LogWarning($"[ShopUI] 商品目录第 {i} 项是空的，已跳过。", this);
                    continue;
                }

                RectTransform clone = Instantiate(itemTemplate, itemContainer);
                clone.name = $"Item_{data.MaterialName}";
                clone.gameObject.SetActive(true);

                ShopItemUI item = clone.GetComponent<ShopItemUI>();

                if (item == null)
                {
                    Debug.LogWarning("[ShopUI] 条目模板上没有 ShopItemUI 组件，已自动补上。" +
                                     "请重跑菜单「屿见/商店/重建轻量化商店窗口」把引用接好。", this);
                    item = clone.gameObject.AddComponent<ShopItemUI>();
                }

                item.Initialize(this, data);
                items.Add(item);
            }

            if (items.Count > 0)
            {
                SelectItem(items[0]);
            }
        }

        private void RefreshStaticText()
        {
            if (titleText != null)
            {
                titleText.text = title;
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
                    Debug.LogWarning("[ShopUI] 拿不到 PlayerCurrency，金币显示将一直为 ?。", this);
                }

                moneyText.text = "金币 ?";
                return;
            }

            moneyText.text = $"金币 {shop.Currency.CurrentMoney}";
        }

        /// <summary>
        /// 刷新右侧价格：同时显示单价和「单价 × 当前数量」的总价。
        /// 没有选中项时禁用购买键，避免点了才知道没选。
        /// </summary>
        private void RefreshPrice()
        {
            bool hasSelection = selected != null && selected.Material != null;

            if (buyButton != null)
            {
                buyButton.interactable = hasSelection;
            }

            if (priceText == null)
            {
                return;
            }

            if (!hasSelection)
            {
                priceText.text = "单价 —\n总价 —";
                return;
            }

            int unitPrice = selected.Material.Price;
            int total = unitPrice * selected.Quantity;
            priceText.text = $"单价 {unitPrice}\n总价 {total}";
        }

        private void HandleBuyClicked()
        {
            if (selected == null || selected.Material == null)
            {
                ShowStatus("请先选择原料", false);
                return;
            }

            if (shop == null)
            {
                Debug.LogError("[ShopUI] Shop 未配置，无法购买。", this);
                ShowStatus(failureMessage, false);
                return;
            }

            int total = selected.Material.Price * selected.Quantity;

            if (shop.Currency != null && shop.Currency.CurrentMoney < total)
            {
                ShowStatus(insufficientMessage, false);
                return;
            }

            // 颜色选择已从商店移除：购买统一按原料默认色入库。
            bool success = shop.TryBuy(selected.Material, selected.Material.DefaultColor, selected.Quantity);

            ShowStatus(success ? successMessage : failureMessage, success);
            RefreshMoney();
            RefreshPrice();
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

        /// <summary>
        /// 给没指定字体的 Text 补上 Unity 内置的 LegacyRuntime.ttf。
        /// 项目规则要求不引入 TextMeshPro，而 legacy Text 不指定字体就不显示任何文字。
        /// 已经手动指定过字体的不动。
        ///
        /// 注意它带 includeInactive=true：条目模板是隐藏的，漏掉它的话克隆出来的条目会没有字体。
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
