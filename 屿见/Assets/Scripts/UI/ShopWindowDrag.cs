using UnityEngine;
using UnityEngine.EventSystems;

namespace Yujian.UI
{
    /// <summary>
    /// 轻量化商店浮窗的拖动（2026-11-05 商店策划案：「拖动商店 UI 的边框以移动商店页面」）。
    ///
    /// 窗口大小固定、不缩放。拖动窗口空白处或边框即可移动；原料列表的上下拖动
    /// 由列表自己的 ScrollRect 消费，不会误移动窗口。
    ///
    /// 默认把窗口限制在 Canvas 范围内，避免拖出屏幕后找不回来。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class ShopWindowDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IPointerDownHandler
    {
        [Header("依赖")]
        [Tooltip("限制窗口移动范围的 Canvas。留空则自动取父级 Canvas")]
        [SerializeField] private Canvas canvas;

        [Header("行为")]
        [Tooltip("勾选后窗口不能被拖出 Canvas 范围")]
        [SerializeField] private bool clampToCanvas = true;

        private RectTransform rect;
        private RectTransform canvasRect;
        private Vector2 grabOffset;

        /// <summary>编辑器向导调用，指定限制范围的 Canvas。</summary>
        public void ConfigureForSetup(Canvas targetCanvas)
        {
            canvas = targetCanvas;
        }

        private void Awake()
        {
            rect = (RectTransform)transform;

            if (canvas == null)
            {
                canvas = GetComponentInParent<Canvas>();
            }

            if (canvas != null)
            {
                canvasRect = (RectTransform)canvas.transform;
            }
            else if (clampToCanvas)
            {
                Debug.LogWarning("[ShopWindowDrag] 找不到 Canvas，窗口不会被限制在屏幕内。", this);
            }
        }

        /// <summary>开始拖动：记下鼠标与窗口锚点的偏移，避免窗口跳到鼠标中心。</summary>
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!TryGetLocalPoint(eventData, out Vector2 localPoint))
            {
                return;
            }

            grabOffset = localPoint - (Vector2)rect.localPosition;
        }

        /// <summary>拖动中：按偏移移动窗口，并按需夹在 Canvas 内。</summary>
        public void OnDrag(PointerEventData eventData)
        {
            if (!TryGetLocalPoint(eventData, out Vector2 localPoint))
            {
                return;
            }

            Vector2 target = localPoint - grabOffset;
            rect.localPosition = clampToCanvas ? ClampInsideCanvas(target) : (Vector3)target;
        }

        /// <summary>点中窗口时提到最前，方便在其它 UI 之上操作。</summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            transform.SetAsLastSibling();
        }

        private bool TryGetLocalPoint(PointerEventData eventData, out Vector2 localPoint)
        {
            localPoint = Vector2.zero;

            if (canvasRect == null)
            {
                return false;
            }

            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, eventData.position, eventData.pressEventCamera, out localPoint);
        }

        /// <summary>
        /// 把窗口锚点夹到「窗口四边都不超出 Canvas」的范围内。
        /// rect.localPosition 表示 pivot 的位置，所以要按 pivot 换算左右/上下边距。
        /// </summary>
        private Vector2 ClampInsideCanvas(Vector2 position)
        {
            if (canvasRect == null)
            {
                return position;
            }

            Vector2 size = rect.rect.size;
            Vector2 pivot = rect.pivot;
            Rect canvasBounds = canvasRect.rect;

            float minX = canvasBounds.xMin + size.x * pivot.x;
            float maxX = canvasBounds.xMax - size.x * (1f - pivot.x);
            float minY = canvasBounds.yMin + size.y * pivot.y;
            float maxY = canvasBounds.yMax - size.y * (1f - pivot.y);

            position.x = Mathf.Clamp(position.x, minX, maxX);
            position.y = Mathf.Clamp(position.y, minY, maxY);
            return position;
        }
    }
}
