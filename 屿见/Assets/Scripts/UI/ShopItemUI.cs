using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Shop;

namespace Yujian.UI
{
    /// <summary>
    /// 轻量化商店面板里的一个原料条目（2026-11-05 商店策划案）。
    ///
    /// 结构（由 ShopSetupWizard 生成）：
    ///   原料名称 / 原料样式 / 原料说明 / 可选颜色色块（只展示不可选）
    ///   + 右侧「− 数量 +」单次购买数量调节
    ///
    /// 价格与购买键属于整个面板（右侧），不在条目里。点击条目把它设为当前选中项，
    /// 面板据此显示该原料的单价 / 总价并购买。
    ///
    /// 职责边界：本类只管理自己这一格的数量与显示，不认识 Shop / MaterialInventory，
    /// 购买入口只有 ShopUI 一处。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ShopItemUI : MonoBehaviour
    {
        [Header("文本")]
        [Tooltip("原料名称，例如「小砖」")]
        [SerializeField] private Text nameText;

        [Tooltip("原料样式，例如「样式：砖块」")]
        [SerializeField] private Text styleText;

        [Tooltip("原料说明 / 样式功能，例如「玩家伤害倍率 +0.1（建成后生效）」")]
        [SerializeField] private Text effectText;

        [Tooltip("当前单次购买数量，位于条目右侧")]
        [SerializeField] private Text quantityText;

        [Header("颜色（只展示，不可选择）")]
        [Tooltip("色块父物体")]
        [SerializeField] private Transform colorRow;

        [Tooltip("隐藏的小色块模板，运行时按可选颜色克隆")]
        [SerializeField] private Image colorDotTemplate;

        [Header("交互")]
        [Tooltip("点击整条原料选中它")]
        [SerializeField] private Button selectButton;

        [Tooltip("选中高亮。只切换 enabled，不改布局")]
        [SerializeField] private Image selectionHighlight;

        [SerializeField] private Button minusButton;
        [SerializeField] private Button plusButton;

        [Header("数量")]
        [Tooltip("单次购买数量下限")]
        [SerializeField] private int minQuantity = 1;

        [Tooltip("单次购买数量上限")]
        [SerializeField] private int maxQuantity = 99;

        private ShopUI panel;
        private MaterialData material;
        private int quantity = 1;

        /// <summary>运行时克隆出来的色块，重设时统一清掉。</summary>
        private readonly List<Image> colorDots = new List<Image>();

        /// <summary>本条目代表的原料，可能为 null（尚未初始化）。</summary>
        public MaterialData Material => material;

        /// <summary>单次购买数量。</summary>
        public int Quantity => quantity;

        /// <summary>由 ShopUI 在实例化并激活之后调用。</summary>
        public void Initialize(ShopUI owner, MaterialData materialData)
        {
            panel = owner;
            material = materialData;
            quantity = Mathf.Clamp(quantity, minQuantity, maxQuantity);

            ValidateReferences();
            RefreshStaticText();
            BuildColorDots();
            RefreshQuantity();
            SetSelected(false);
        }

        /// <summary>切换选中高亮。由 ShopUI 统一调用。</summary>
        public void SetSelected(bool selected)
        {
            if (selectionHighlight != null)
            {
                selectionHighlight.enabled = selected;
            }
        }

        /// <summary>调整数量。面板会据此刷新右侧总价。</summary>
        public void StepQuantity(int delta)
        {
            int next = Mathf.Clamp(quantity + delta, minQuantity, maxQuantity);

            if (next == quantity)
            {
                return;
            }

            quantity = next;
            RefreshQuantity();
            panel?.NotifyQuantityChanged(this);
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

            if (styleText != null)
            {
                styleText.text = $"样式：{material.MaterialType.ToChineseName()}";
            }

            if (effectText != null)
            {
                effectText.text = material.EffectDescription;
            }
        }

        private void RefreshQuantity()
        {
            if (quantityText != null)
            {
                quantityText.text = quantity.ToString();
            }
        }

        /// <summary>
        /// 只展示该原料允许的颜色，不生成任何可点击的颜色选择器。
        /// 去重原因同旧实现：AllowedColors 文档明确说「可能含重复值」。
        /// </summary>
        private void BuildColorDots()
        {
            for (int i = 0; i < colorDots.Count; i++)
            {
                if (colorDots[i] != null)
                {
                    Destroy(colorDots[i].gameObject);
                }
            }

            colorDots.Clear();

            if (material == null)
            {
                return;
            }

            if (colorRow == null || colorDotTemplate == null)
            {
                Debug.LogWarning("[ShopItemUI] 色块父物体或色块模板未配置，可选颜色不会显示。", this);
                return;
            }

            HashSet<MaterialColor> seen = new HashSet<MaterialColor>();
            IReadOnlyList<MaterialColor> allowed = material.AllowedColors;

            for (int i = 0; i < allowed.Count; i++)
            {
                if (!seen.Add(allowed[i]))
                {
                    continue;
                }

                Image dot = Instantiate(colorDotTemplate, colorRow);
                dot.gameObject.SetActive(true);
                dot.name = $"Color_{allowed[i]}";
                dot.sprite = null;
                dot.color = allowed[i].ToDisplayColor();
                dot.raycastTarget = false;
                colorDots.Add(dot);
            }
        }

        private void HandleSelectClicked()
        {
            panel?.SelectItem(this);
        }

        private void HandleMinusClicked()
        {
            panel?.SelectItem(this);
            StepQuantity(-1);
        }

        private void HandlePlusClicked()
        {
            panel?.SelectItem(this);
            StepQuantity(1);
        }

        private void OnEnable()
        {
            if (selectButton != null)
            {
                selectButton.onClick.AddListener(HandleSelectClicked);
            }

            if (minusButton != null)
            {
                minusButton.onClick.AddListener(HandleMinusClicked);
            }

            if (plusButton != null)
            {
                plusButton.onClick.AddListener(HandlePlusClicked);
            }
        }

        private void OnDisable()
        {
            if (selectButton != null)
            {
                selectButton.onClick.RemoveListener(HandleSelectClicked);
            }

            if (minusButton != null)
            {
                minusButton.onClick.RemoveListener(HandleMinusClicked);
            }

            if (plusButton != null)
            {
                plusButton.onClick.RemoveListener(HandlePlusClicked);
            }
        }

        /// <summary>引用没接全不报错、只显示一半是最难查的问题，所以这里直接说清楚。</summary>
        private void ValidateReferences()
        {
            bool missing = nameText == null || styleText == null || effectText == null ||
                           quantityText == null || colorRow == null || colorDotTemplate == null ||
                           selectButton == null || selectionHighlight == null ||
                           minusButton == null || plusButton == null;

            if (missing)
            {
                Debug.LogWarning("[ShopItemUI] 有引用没有接上，条目会显示不全。" +
                                 "请重跑菜单「屿见/商店/重建轻量化商店窗口」。", this);
            }
        }
    }
}
