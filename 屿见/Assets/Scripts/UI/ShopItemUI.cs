using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Shop;

namespace Yujian.UI
{
    /// <summary>
    /// 商店面板里的一个原料模块，对应一种 MaterialData，例如「小砖」这一格。
    ///
    /// 结构（由编辑器脚本 ShopSetupWizard 生成）：
    ///   原料名称 → 原料类型 → 原料说明块（上半：颜色选择器 [上一个|色块|下一个]，下半：效果 + 持有数量）
    ///   → 价格 → 购买
    ///
    /// 职责边界：本类只显示自己这一格，并把「购买」转发给 ShopUI。
    /// 它**不认识** Shop、也不认识 MaterialInventory——购买入口与库存查询都只有 ShopUI 一处，
    /// 对应关系同 MaterialDragItem ↔ BuildingMaterialPanel。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ShopItemUI : MonoBehaviour
    {
        [Header("文本")]
        [Tooltip("原料名称，例如「小砖」。名称与类型分两行，不再合并成一行")]
        [SerializeField] private Text nameText;

        [Tooltip("原料类型，例如「类型：砖块」")]
        [SerializeField] private Text typeText;

        [Tooltip("当前选中的颜色名，例如「红色」")]
        [SerializeField] private Text colorNameText;

        [Tooltip("效果说明，例如「效果：伤害倍率 +0.1」。原料说明块的下半部分")]
        [SerializeField] private Text effectText;

        [Tooltip("当前颜色下的持有数量。原料说明块的下半部分")]
        [SerializeField] private Text ownedText;

        [Tooltip("价格，显示在购买键正上方")]
        [SerializeField] private Text priceText;

        [Header("颜色选择")]
        [Tooltip("颜色色块。sprite 由 ShopUI 提供，取不到时退化成纯色块")]
        [SerializeField] private Image swatchImage;

        [SerializeField] private Button previousButton;
        [SerializeField] private Button nextButton;

        [Header("购买")]
        [SerializeField] private Button buyButton;

        private ShopUI panel;
        private MaterialData material;

        /// <summary>可选颜色，已去重并按 MaterialData 里的顺序排列。</summary>
        private readonly List<MaterialColor> colorOptions = new List<MaterialColor>();

        private int colorIndex;

        /// <summary>本条目代表的原料，可能为 null（尚未初始化）。</summary>
        public MaterialData Material => material;

        /// <summary>当前选中的颜色。颜色列表为空时退回原料的默认色。</summary>
        public MaterialColor SelectedColor
        {
            get
            {
                if (colorOptions.Count == 0)
                {
                    return material != null ? material.DefaultColor : MaterialColor.Red;
                }

                return colorOptions[colorIndex];
            }
        }

        /// <summary>由 ShopUI 在实例化并激活之后调用。</summary>
        public void Initialize(ShopUI owner, MaterialData materialData)
        {
            panel = owner;
            material = materialData;

            ValidateReferences();
            BuildColorOptions();
            RefreshStaticText();
            RefreshColorDisplay();
            RefreshOwned();
        }

        /// <summary>刷新「持有数量」那一行。库存变化时由 ShopUI 统一调用。</summary>
        public void RefreshOwned()
        {
            if (ownedText == null)
            {
                return;
            }

            if (material == null)
            {
                ownedText.text = "持有：—";
                return;
            }

            int count = panel != null ? panel.GetOwnedCount(material, SelectedColor) : 0;
            ownedText.text = $"持有：{SelectedColor.ToChineseName()}{material.MaterialName} ×{count}";
        }

        /// <summary>
        /// 可选颜色来自 MaterialData.AllowedColors，而不是硬编码枚举。
        /// 理由：AllowedColors 才是「这种原料能买成哪些颜色」的真相源，Shop.TryBuy 也只认它；
        /// 直接遍历枚举会列出必然购买失败的颜色，玩家只会看到误导性的「当前货币不足」。
        ///
        /// AllowedColors 的属性文档明确说「可能含重复值」，所以这里用 HashSet 去重并保持原顺序。
        /// </summary>
        private void BuildColorOptions()
        {
            colorOptions.Clear();

            if (material == null)
            {
                return;
            }

            HashSet<MaterialColor> seen = new HashSet<MaterialColor>();
            IReadOnlyList<MaterialColor> allowed = material.AllowedColors;

            for (int i = 0; i < allowed.Count; i++)
            {
                if (seen.Add(allowed[i]))
                {
                    colorOptions.Add(allowed[i]);
                }
            }

            // 起始颜色优先用资产里配的默认色，找不到（例如默认色不在允许列表里）才退回第一个
            colorIndex = colorOptions.IndexOf(material.DefaultColor);

            if (colorIndex < 0)
            {
                colorIndex = 0;
            }

            // 只有一种颜色时上/下没有意义。禁用而不是隐藏：隐藏会让整行布局跳动，
            // 而禁用能让玩家一眼看出「这种原料就只有这一个颜色」。
            bool multiple = colorOptions.Count > 1;

            if (previousButton != null)
            {
                previousButton.interactable = multiple;
            }

            if (nextButton != null)
            {
                nextButton.interactable = multiple;
            }
        }

        private void RefreshStaticText()
        {
            if (material == null)
            {
                return;
            }

            if (nameText != null)
            {
                nameText.text = material.MaterialName;
            }

            if (typeText != null)
            {
                typeText.text = $"类型：{material.MaterialType.ToChineseName()}";
            }

            if (effectText != null)
            {
                effectText.text = $"效果：{material.EffectDescription}";
            }

            if (priceText != null)
            {
                priceText.text = $"价格：{material.Price}";
            }
        }

        /// <summary>刷新色块与颜色名。色块优先用 ShopUI 给的 sprite，取不到就退化成纯色块。</summary>
        private void RefreshColorDisplay()
        {
            MaterialColor color = SelectedColor;

            if (colorNameText != null)
            {
                colorNameText.text = color.ToChineseName();
            }

            if (swatchImage == null)
            {
                return;
            }

            Sprite sprite = panel != null ? panel.GetColorSprite(color) : null;

            if (sprite != null)
            {
                swatchImage.sprite = sprite;
                swatchImage.color = Color.white;
            }
            else
            {
                // 不允许静默留白：没配贴图时退化成纯色块，至少让玩家看出当前是什么颜色。
                // 缺哪张图由 ShopUI 负责提示一次，这里不重复刷屏。
                swatchImage.sprite = null;
                swatchImage.color = color.ToDisplayColor();
            }
        }

        /// <summary>循环切换颜色：走到头绕回另一端。</summary>
        private void StepColor(int delta)
        {
            if (colorOptions.Count <= 1)
            {
                return;
            }

            int count = colorOptions.Count;
            colorIndex = ((colorIndex + delta) % count + count) % count;

            RefreshColorDisplay();
            RefreshOwned();
        }

        private void HandlePreviousClicked()
        {
            StepColor(-1);
        }

        private void HandleNextClicked()
        {
            StepColor(1);
        }

        private void HandleBuyClicked()
        {
            if (panel == null)
            {
                Debug.LogError("[ShopItemUI] 面板引用为空，无法购买。请重跑菜单「屿见/配置原料商店（阶段 2 重构）」。", this);
                return;
            }

            panel.RequestBuy(material, SelectedColor);
        }

        private void OnEnable()
        {
            if (previousButton != null)
            {
                previousButton.onClick.AddListener(HandlePreviousClicked);
            }

            if (nextButton != null)
            {
                nextButton.onClick.AddListener(HandleNextClicked);
            }

            if (buyButton != null)
            {
                buyButton.onClick.AddListener(HandleBuyClicked);
            }
        }

        private void OnDisable()
        {
            if (previousButton != null)
            {
                previousButton.onClick.RemoveListener(HandlePreviousClicked);
            }

            if (nextButton != null)
            {
                nextButton.onClick.RemoveListener(HandleNextClicked);
            }

            if (buyButton != null)
            {
                buyButton.onClick.RemoveListener(HandleBuyClicked);
            }
        }

        /// <summary>引用没接全不报错、只显示一半是最难查的问题，所以这里直接说清楚（规则 5）。</summary>
        private void ValidateReferences()
        {
            bool missing = nameText == null || typeText == null || colorNameText == null ||
                           effectText == null || ownedText == null || priceText == null ||
                           swatchImage == null || previousButton == null || nextButton == null ||
                           buyButton == null;

            if (missing)
            {
                Debug.LogWarning("[ShopItemUI] 有引用没有接上，条目会显示不全。" +
                                 "请重跑菜单「屿见/配置原料商店（阶段 2 重构）」。", this);
            }
        }
    }
}
