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
    public class MaterialDragItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("显示（留空会自动在本物体及子物体里找）")]
        [Tooltip("底图。拖拽时残影会照抄它的 sprite")]
        [SerializeField] private Image background;

        [Tooltip("文字，显示「红色小砖 ×2」")]
        [SerializeField] private Text label;

        [Header("外观")]
        [Tooltip("拖拽残影的边长（像素）")]
        [SerializeField] private float ghostSize = 90f;

        private BuildingMaterialPanel panel;
        private MaterialData material;
        private MaterialColor color;

        private RectTransform ghost;
        private bool dragAllowed;

        /// <summary>本条目代表的原料。</summary>
        public MaterialData Material => material;

        /// <summary>本条目代表的颜色。</summary>
        public MaterialColor Color => color;

        private void Awake()
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

        public void OnBeginDrag(PointerEventData eventData)
        {
            dragAllowed = false;

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
