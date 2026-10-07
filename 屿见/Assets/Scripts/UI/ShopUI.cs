using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Shop;

namespace Yujian.UI
{
    /// <summary>
    /// 原料商店面板。
    ///
    /// 职责：按「商品目录」为每种原料克隆一个 ShopItemUI 条目，显示金币与状态行，
    /// 并把条目的购买请求转交给 Shop.TryBuy()。
    /// 严格只读：显示的金币与持有数量都从 Shop 暴露的只读属性取，本类不修改任何数值。
    ///
    /// 条目的克隆、订阅与刷新都只有这一处：条目自己**不**订阅 OnInventoryChanged，
    /// 否则每次重建条目都要小心退订，很容易漏。这里订阅一次，收到通知后挨个推给条目。
    /// </summary>
    public class ShopUI : MonoBehaviour
    {
        /// <summary>
        /// 颜色 → 色块贴图。沿用原先 ColorButtonBinding 的写法：
        /// [Serializable] struct + 数组挂在组件上，Inspector 里直接调，不新建资产类型。
        /// 放在面板上而不是条目上：条目是克隆出来的，放在条目上会让每份克隆各存一份副本。
        /// </summary>
        [Serializable]
        private struct ColorSpriteBinding
        {
            [SerializeField] private MaterialColor color;
            [SerializeField] private Sprite sprite;

            public MaterialColor Color => color;
            public Sprite Sprite => sprite;
        }

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
        [Tooltip("状态行：成功购买 / 当前货币不足")]
        [SerializeField] private Text statusText;

        [Header("条目列表")]
        [Tooltip("克隆出来的条目的父物体")]
        [SerializeField] private Transform itemContainer;

        [Tooltip("条目模板。运行时按目录克隆，本物体应当保持隐藏")]
        [SerializeField] private RectTransform itemTemplate;

        [Header("颜色贴图")]
        [Tooltip("每个颜色配一张色块贴图。没配的颜色会退化成纯色块并给出警告")]
        [SerializeField] private ColorSpriteBinding[] colorSprites = new ColorSpriteBinding[0];

        [Header("文案")]
        [SerializeField] private string title = "原料商店";
        [SerializeField] private string successMessage = "成功购买";
        [SerializeField] private string insufficientMessage = "当前货币不足";
        [Tooltip("状态行自动清空的秒数。<=0 表示一直保留")]
        [SerializeField] private float statusClearDelay = 2f;

        [Header("配色")]
        [SerializeField] private Color successColor = new Color(0.24f, 0.65f, 0.32f);
        [SerializeField] private Color failureColor = new Color(0.80f, 0.22f, 0.20f);

        private readonly List<ShopItemUI> items = new List<ShopItemUI>();

        /// <summary>已经为哪些颜色提示过「没配贴图」。每种只提示一次，免得每次刷新都刷屏。</summary>
        private readonly HashSet<MaterialColor> warnedMissingSprites = new HashSet<MaterialColor>();

        private float statusClearTime = -1f;
        private bool warnedMissingMoneySource;
        private bool warnedMissingInventorySource;

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
                               "请重跑菜单「屿见/配置原料商店（阶段 2 重构）」。", this);
            }

            BuildItems();
            RefreshStaticText();
        }

        private void OnEnable()
        {
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
            RefreshItems();
        }

        private void OnDisable()
        {
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

        /// <summary>
        /// 某种原料某种颜色当前持有多少。给条目显示用——条目自己不认识 MaterialInventory。
        /// 面板不负责显示这个数字，但负责提供它。
        /// </summary>
        public int GetOwnedCount(MaterialData material, MaterialColor color)
        {
            if (shop == null || material == null)
            {
                return 0;
            }

            if (shop.Inventory == null)
            {
                if (!warnedMissingInventorySource)
                {
                    warnedMissingInventorySource = true;
                    Debug.LogWarning("[ShopUI] 拿不到 MaterialInventory，持有数量将一直显示 0。", this);
                }

                return 0;
            }

            return shop.Inventory.GetMaterialCount(material, color);
        }

        /// <summary>
        /// 取某种颜色对应的色块贴图，没配就返回 null，由条目自己退化成纯色块。
        /// 每缺一种颜色只提示一次（规则 5：不允许静默失败，但也不允许刷屏）。
        /// </summary>
        public Sprite GetColorSprite(MaterialColor color)
        {
            if (colorSprites != null)
            {
                for (int i = 0; i < colorSprites.Length; i++)
                {
                    ColorSpriteBinding entry = colorSprites[i];

                    if (entry.Color == color && entry.Sprite != null)
                    {
                        return entry.Sprite;
                    }
                }
            }

            if (warnedMissingSprites.Add(color))
            {
                Debug.LogWarning($"[ShopUI] 颜色「{color.ToChineseName()}」没有配色块贴图，已退化成纯色块显示。" +
                                 "跑一次菜单「屿见/配置原料商店（阶段 2 重构）」可以自动生成占位贴图。", this);
            }

            return null;
        }

        /// <summary>条目的唯一购买入口 —— 条目自己不认识 Shop。</summary>
        public void RequestBuy(MaterialData material, MaterialColor color)
        {
            if (material == null)
            {
                Debug.LogError("[ShopUI] 购买请求里没有原料数据，已忽略。", this);
                return;
            }

            if (shop == null)
            {
                Debug.LogError("[ShopUI] Shop 未配置，无法购买。", this);
                return;
            }

            bool success = shop.TryBuy(material, color);

            ShowStatus(success ? successMessage : insufficientMessage, success);
            RefreshItems();
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
                    Debug.LogWarning($"[ShopUI] 条目模板上没有 ShopItemUI 组件，已自动补上。" +
                                     "请重跑菜单「屿见/配置原料商店（阶段 2 重构）」把引用接好。", this);
                    item = clone.gameObject.AddComponent<ShopItemUI>();
                }

                item.Initialize(this, data);
                items.Add(item);
            }
        }

        /// <summary>把库存变化推给每个条目。条目自己不订阅事件，避免重建时漏退订。</summary>
        private void RefreshItems()
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null)
                {
                    items[i].RefreshOwned();
                }
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
                    Debug.LogWarning("[ShopUI] 拿不到 PlayerCurrency，金币显示将一直为 0。", this);
                }

                moneyText.text = "金币：?";
                return;
            }

            moneyText.text = $"金币：{shop.Currency.CurrentMoney}";
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
            RefreshItems();
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
