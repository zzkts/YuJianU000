using UnityEngine;
using UnityEngine.UI;

namespace Yujian.UI
{
    /// <summary>
    /// 轻量化商店浮窗的尺寸与自适应布局（2026-11-06）。
    ///
    /// 背景：浮窗原来把 240×90 硬编码在编辑器向导里，想改大小只能改代码，
    /// 而且子元素全是固定尺寸，外框放大后只会多出空白。
    ///
    /// 现在：
    ///   · 本组件挂在 Canvas/ShopPanel 上，Inspector 里直接调宽高、内边距、右侧列宽、字号；
    ///   · ApplyLayout() 按锚点重排，列表区自动拉伸占满左侧剩余空间；
    ///   · 编辑器里改字段会通过 OnValidate 立即重排，场景视图实时可见；
    ///   · 进 Play 时在 Awake 再应用一次兜底；
    ///   · 编辑器向导重跑时保留本组件的配置，不再把尺寸写死。
    ///
    /// 本类只负责布局，不碰购买、不碰显隐。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class ShopWindowLayout : MonoBehaviour
    {
        [Header("窗口尺寸")]
        [Tooltip("浮窗宽高（Canvas 像素）。默认 240×90 与旧版一致")]
        [SerializeField] private Vector2 windowSize = new Vector2(240f, 90f);

        [Header("内边距与分区")]
        [Tooltip("四周内边距")]
        [Min(0f)]
        [SerializeField] private float padding = 4f;

        [Tooltip("顶部「原料商店 / 金币」这一行的高度")]
        [Min(1f)]
        [SerializeField] private float topRowHeight = 12f;

        [Tooltip("底部状态提示（成功购买 / 货币不足）这一行的高度")]
        [Min(1f)]
        [SerializeField] private float bottomRowHeight = 12f;

        [Tooltip("右侧价格 / 购买按钮那一列的宽度")]
        [Min(1f)]
        [SerializeField] private float rightColumnWidth = 84f;

        [Header("右侧列高")]
        [Tooltip("价格文本（单价 / 总价 两行）的高度")]
        [Min(1f)]
        [SerializeField] private float priceTextHeight = 30f;

        [Tooltip("购买按钮的高度")]
        [Min(1f)]
        [SerializeField] private float buyButtonHeight = 24f;

        [Header("商品条目")]
        [Tooltip("列表里单个商品条目的高度。窗口变高后可以调大，让条目更好点")]
        [Min(1f)]
        [SerializeField] private float itemHeight = 52f;

        [Header("文字")]
        [Tooltip("整窗文字缩放。1 = 向导建出来的原始字号；窗口放大后可调到 1.3~1.6")]
        [Min(0.1f)]
        [SerializeField] private float fontScale = 1f;

        [Header("应用时机")]
        [Tooltip("进 Play 时再应用一次，保证 Inspector 里改的数值生效")]
        [SerializeField] private bool applyOnAwake = true;

        /// <summary>当前配置的窗口尺寸，供向导读取。</summary>
        public Vector2 WindowSize => windowSize;

        /// <summary>
        /// 应用整套布局：窗口尺寸 → 各子元素锚点 → 条目高度 → 字号。
        /// 幂等，可以反复调用。
        /// </summary>
        public void ApplyLayout()
        {
            ApplyWindowSize();
            ApplyAreas();
            ApplyItemHeight();
            ApplyFontScale();
        }

        private void Awake()
        {
            if (applyOnAwake)
            {
                ApplyLayout();
            }
        }

        // OnValidate 在编辑器里改 Inspector 时触发，用来实时预览；
        // 运行时不会触发，所以不用额外的 #if UNITY_EDITOR。
        private void OnValidate()
        {
            ApplyLayout();
        }

        /// <summary>设外框大小；锚点与 pivot 保持向导配置的「右下角」不变。</summary>
        private void ApplyWindowSize()
        {
            RectTransform self = (RectTransform)transform;
            self.sizeDelta = new Vector2(
                Mathf.Max(1f, windowSize.x),
                Mathf.Max(1f, windowSize.y));
        }

        private void ApplyAreas()
        {
            float w = Mathf.Max(1f, windowSize.x);
            float h = Mathf.Max(1f, windowSize.y);
            float p = Mathf.Max(0f, padding);
            float topH = Mathf.Max(1f, topRowHeight);
            float bottomH = Mathf.Max(1f, bottomRowHeight);
            float rightW = Mathf.Max(1f, rightColumnWidth);

            // 左侧可用宽度：去掉左右内边距与右侧列。
            float leftWidth = Mathf.Max(0f, w - rightW - p * 3f);

            // 标题：左上
            SetAnchored("TitleText", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                        new Vector2(p, -p), new Vector2(leftWidth, topH));

            // 金币：右上
            SetAnchored("MoneyText", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                        new Vector2(-p, -p), new Vector2(rightW, topH));

            // 状态提示：左下
            SetAnchored("StatusText", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                        new Vector2(p, p), new Vector2(leftWidth, bottomH));

            // 商品列表：拉伸占满左侧剩余区域
            RectTransform scroll = FindRect("ItemScroll");

            if (scroll != null)
            {
                scroll.anchorMin = Vector2.zero;
                scroll.anchorMax = Vector2.one;
                scroll.pivot = new Vector2(0.5f, 0.5f);
                scroll.offsetMin = new Vector2(p, bottomH + p * 2f);
                scroll.offsetMax = new Vector2(-(rightW + p * 2f), -(topH + p * 2f));
            }

            // 价格：右下，贴在购买按钮上方
            SetAnchored("PriceText", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                        new Vector2(-p, buyButtonHeight + p * 2f),
                        new Vector2(rightW, Mathf.Max(1f, priceTextHeight)));

            // 购买按钮：右下角
            SetAnchored("BuyButton", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                        new Vector2(-p, p), new Vector2(rightW, Mathf.Max(1f, buyButtonHeight)));
        }

        /// <summary>把指定名字的子物体按锚点放到固定位置。</summary>
        private void SetAnchored(string childName, Vector2 anchorMin, Vector2 anchorMax,
                                 Vector2 pivot, Vector2 position, Vector2 size)
        {
            RectTransform rect = FindRect(childName);

            if (rect == null)
            {
                return;
            }

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        /// <summary>条目高度可配；列表的 VerticalLayoutGroup 会按 LayoutElement 排。</summary>
        private void ApplyItemHeight()
        {
            RectTransform item = FindRect("ItemTemplate");

            if (item == null)
            {
                return;
            }

            LayoutElement element = item.GetComponent<LayoutElement>();

            if (element == null)
            {
                element = item.gameObject.AddComponent<LayoutElement>();
            }

            element.preferredHeight = itemHeight;
            element.minHeight = itemHeight;
        }

        /// <summary>
        /// 用「基准字号 × fontScale」直接赋值，幂等：反复调用不会累积放大。
        /// 基准值必须与 ShopSetupWizard.CreateText 里写的一致。
        /// </summary>
        private void ApplyFontScale()
        {
            Text[] texts = GetComponentsInChildren<Text>(true);

            for (int i = 0; i < texts.Length; i++)
            {
                int baseSize = GetBaseFontSize(texts[i]);

                if (baseSize <= 0)
                {
                    continue;
                }

                texts[i].fontSize = Mathf.Max(1, Mathf.RoundToInt(baseSize * fontScale));
            }
        }

        private static int GetBaseFontSize(Text text)
        {
            Transform t = text.transform;

            switch (t.name)
            {
                case "TitleText": return 10;
                case "MoneyText": return 8;
                case "StatusText": return 8;
                case "PriceText": return 9;
                case "NameText": return 9;
                case "StyleText": return 7;
                case "EffectText": return 7;
                case "QuantityLabel": return 7;
                case "QuantityText": return 8;

                // 按钮文字统一叫 "Text"，按父物体区分。
                case "Text":
                    if (t.parent == null)
                    {
                        return 0;
                    }

                    switch (t.parent.name)
                    {
                        case "BuyButton": return 10;
                        case "MinusButton":
                        case "PlusButton": return 11;
                        case "ButtonTemplate": return 12;
                        default: return 0;
                    }
            }

            return 0;
        }

        private RectTransform FindRect(string childName)
        {
            return FindDeep(transform, childName) as RectTransform;
        }

        private static Transform FindDeep(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);

                if (child.name == childName)
                {
                    return child;
                }

                Transform found = FindDeep(child, childName);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
