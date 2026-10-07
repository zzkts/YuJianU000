using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Yujian.Shop;

namespace Yujian.UI
{
    /// <summary>
    /// 材料面板里的一个可拖拽条目，例如「红色小砖 ×2」。
    ///
    /// 职责：按下时问一次「这块料现在能放吗」，不能放就只弹提示、**不生成拖拽残影**
    /// （规格第三条要求）；能放就生成一个跟着指针走的残影，松手时把屏幕坐标交给面板。
    ///
    /// 不碰库存、不碰蓝图——那些是 BuildingConstruction 的事。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class MaterialDragItem : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerDownHandler
    {
        [Header("显示（留空会自动在本物体及子物体里找）")]
        [Tooltip("底图。拖拽时残影会照抄它的 sprite")]
        [SerializeField] private Image background;

        [Tooltip("文字，显示「红色小砖 ×2」")]
        [SerializeField] private Text label;

        [Header("外观")]
        [Tooltip("拖拽残影的边长（像素）")]
        [SerializeField] private float ghostSize = 90f;

        [Header("文案")]
        [Tooltip("没拖动、只是点了一下条目时的提示。" +
                 "条目是靠拖拽填料的，不说清楚玩家只会以为界面坏了")]
        [SerializeField] private string clickHint = "请把材料拖到蓝图上";

        private BuildingMaterialPanel panel;
        private MaterialData material;
        private MaterialColor color;

        private RectTransform ghost;
        private bool dragAllowed;

        /// <summary>
        /// 这一次按下有没有真的拖起来过。用来压掉拖拽结束后的那次「点击」——
        /// 本类同时实现了拖拽与点击，而「拖完松手会不会再补一次 OnPointerClick」
        /// 取决于 EventSystem 输入模块内部对 eligibleForClick 的处理（见 OnPointerClick 注释），
        /// 我不在本地实证，所以干脆自己记一笔，两种情况都不会误报。
        /// </summary>
        private bool draggedThisPress;

        /// <summary>本条目代表的原料。</summary>
        public MaterialData Material => material;

        /// <summary>本条目代表的颜色。</summary>
        public MaterialColor Color => color;

        private void Awake()
        {
            ResolveReferences();
        }

        /// <summary>
        /// 解析 Background / Label 引用。
        ///
        /// ⚠ Initialize 里也必须调一次，不能只靠 Awake：
        /// 条目是 BuildingMaterialPanel 在 ItemContainer 还**隐藏着**的时候克隆出来的
        /// （面板要等蓝图落位才显示），那一刻条目不在激活层级里，
        /// Unity 不会立刻调用 Awake——要等面板显示的瞬间才补上。
        /// 而 Initialize 在那之前就执行了。少了这一次解析，background / label 都是 null，
        /// 颜色和数量就刷不上去，条目会永远保持模板原样：
        /// 白底 + 模板上那句占位文字「Button」，玩家根本认不出这是哪块料。
        /// </summary>
        private void ResolveReferences()
        {
            if (background == null)
            {
                background = GetComponent<Image>();
            }

            if (background == null)
            {
                // 工程的按钮把底图放在子物体 Visual 上，不在按钮自己身上。
                // 拖拽事件会沿着父级往上冒泡，所以处理器挂在父物体上照样能收到
                background = GetComponentInChildren<Image>(true);
            }

            if (label == null)
            {
                label = GetComponentInChildren<Text>(true);
            }
        }

        /// <summary>由 BuildingMaterialPanel 在实例化后调用。</summary>
        public void Initialize(BuildingMaterialPanel owner, MaterialData materialData, MaterialColor materialColor)
        {
            panel = owner;
            material = materialData;
            color = materialColor;

            // 条目克隆出来的时候还不在激活层级里，Awake 此刻尚未执行，
            // 引用必须在用之前再解析一次（原因见 ResolveReferences 的注释）
            ResolveReferences();

            if (background != null)
            {
                background.color = color.ToDisplayColor();
            }

            if (label != null)
            {
                // 条目底图是饱和的材料色，沿用按钮模板原本的字色多半读不清，统一改成白字。
                // 必须写全 UnityEngine.Color：本类自己有一个名为 Color 的属性，
                // 类成员在简单名查找里优先于 using 进来的类型，只写 Color 会解析成
                // MaterialColor 那个枚举（枚举没有 white）。
                label.color = UnityEngine.Color.white;
            }

            Refresh(0);
        }

        /// <summary>刷新条目上的数量文字。</summary>
        public void Refresh(int count)
        {
            if (label == null)
            {
                return;
            }

            if (material == null)
            {
                label.text = "—";
                return;
            }

            label.text = $"{color.ToChineseName()}{material.MaterialName} ×{count}";
        }

        /// <summary>
        /// 库存为 0 时把条目压暗，给玩家一个「这条现在拖不动」的视觉提示。
        /// 注意仍然保留 raycastTarget：玩家点下去要能收到「原料不足，请购买」的提示，
        /// 直接禁用条目会变成静默失败。
        /// </summary>
        public void SetDimmed(bool dimmed)
        {
            if (background != null)
            {
                UnityEngine.Color baseColor = color.ToDisplayColor();
                background.color = dimmed
                    ? new UnityEngine.Color(baseColor.r * 0.45f, baseColor.g * 0.45f, baseColor.b * 0.45f, 0.85f)
                    : baseColor;
            }

            if (label != null)
            {
                label.color = dimmed
                    ? new UnityEngine.Color(label.color.r, label.color.g, label.color.b, 0.55f)
                    : new UnityEngine.Color(label.color.r, label.color.g, label.color.b, 1f);
            }
        }

        /// <summary>每次按下都清掉上一次的拖拽标记，否则上一次的拖拽会吃掉这一次的点击提示。</summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            draggedThisPress = false;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            dragAllowed = false;
            draggedThisPress = true;

            if (panel == null)
            {
                return;
            }

            if (!panel.CanDrag(material, color, out string reason))
            {
                // 规格第三条：库存没有对应材料 → 不能生成拖拽物，直接给提示
                panel.ShowStatus(reason, false);
                return;
            }

            dragAllowed = true;
            CreateGhost(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            MoveGhost(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            DestroyGhost();

            if (!dragAllowed || panel == null)
            {
                dragAllowed = false;
                return;
            }

            dragAllowed = false;
            panel.DropAt(eventData.position, material, color);
        }

        /// <summary>
        /// 只点了一下、没有拖动的情况。
        /// 条目本身没有点击功能（填料靠拖拽），但一声不吭会让玩家以为界面坏了，
        /// 所以这里把「现在为什么拖不动」或「该怎么操作」写在面板的状态行上。
        /// 复用 CanDrag 的判定，提示与实际拖拽时的提示完全一致，不会出现两套说法。
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (draggedThisPress)
            {
                // 这次按下拖过——刚才的提示（放入成功 / 请把材料拖到蓝图上）已经给过了，不要覆盖
                draggedThisPress = false;
                return;
            }

            if (panel == null)
            {
                return;
            }

            bool canDrag = panel.CanDrag(material, color, out string reason);
            panel.ShowStatus(canDrag ? clickHint : reason, false);
        }

        private void CreateGhost(PointerEventData eventData)
        {
            RectTransform parent = panel != null ? panel.GhostParent : null;

            if (parent == null)
            {
                return;
            }

            GameObject ghostObject = new GameObject("材料拖拽残影", typeof(RectTransform), typeof(Image));
            ghost = ghostObject.GetComponent<RectTransform>();
            ghost.SetParent(parent, false);
            ghost.sizeDelta = new Vector2(ghostSize, ghostSize);

            Image image = ghostObject.GetComponent<Image>();
            image.raycastTarget = false;

            UnityEngine.Color ghostColor = color.ToDisplayColor();
            ghostColor.a = 0.85f;
            image.color = ghostColor;

            if (background != null && background.sprite != null)
            {
                image.sprite = background.sprite;
                image.type = Image.Type.Simple;
            }

            MoveGhost(eventData);
        }

        private void MoveGhost(PointerEventData eventData)
        {
            if (ghost == null || panel == null)
            {
                return;
            }

            RectTransform parent = panel.GhostParent;

            if (parent == null)
            {
                return;
            }

            // 用 RectTransformUtility 而不是直接赋值 screenPosition：
            // 这样 Screen Space Overlay 与 Camera 两种 Canvas 模式都对
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
            {
                ghost.anchoredPosition = localPoint;
            }
        }

        private void DestroyGhost()
        {
            if (ghost == null)
            {
                return;
            }

            Destroy(ghost.gameObject);
            ghost = null;
        }

        private void OnDisable()
        {
            // 拖到一半面板被关掉（例如退出建造模式）时，残影不能留在屏幕上
            DestroyGhost();
            dragAllowed = false;
        }
    }
}
