using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Yujian.UI
{
    /// <summary>
    /// 建筑面板的鼠标滚轮滚动。
    ///
    /// 2026-11-20 第二版：不再依赖 EventSystem 的 OnScroll 派发。
    /// 第一版把滚动交给 ScrollRect 自带的 IScrollHandler，但只有指针落在滚动区图上时
    /// 才会派发；指针停在进度行 / 底部按钮 / 面板空白处时滚轮无效，用户体感就是
    /// 「滑动功能没做」。现在改成每帧直接读 <see cref="Mouse"/> 的滚轮增量，
    /// 只要指针在面板矩形内就滚动——不依赖射线命中了哪张图。
    ///
    /// 副作用：ScrollRect 自带的滚轮灵敏度会被向导设为 0，避免同一次滚轮被处理两次；
    /// 拖动滚动条仍然由 ScrollRect / Scrollbar 负责。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ScrollWheelRelay : MonoBehaviour
    {
        [Tooltip("要控制的滚动视图")]
        [SerializeField] private ScrollRect target;

        [Tooltip("滚轮一格对应的归一化滚动量。0.12 ≈ 8~9 格滚完一整页")]
        [SerializeField, Min(0.01f)] private float normalizedStep = 0.12f;

        private RectTransform panelRect;
        private Canvas canvas;

        /// <summary>由编辑器向导接线的入口。</summary>
        public void Configure(ScrollRect scrollRect, float step = 0.12f)
        {
            target = scrollRect;
            normalizedStep = Mathf.Max(0.01f, step);
            CacheReferences();
        }

        private void Awake()
        {
            CacheReferences();
        }

        private void OnEnable()
        {
            CacheReferences();
        }

        private void Update()
        {
            if (target == null || !target.vertical)
            {
                return;
            }

            Mouse mouse = Mouse.current;

            if (mouse == null)
            {
                return;
            }

            float wheel = mouse.scroll.ReadValue().y;

            if (Mathf.Approximately(wheel, 0f))
            {
                return;
            }

            if (!IsPointerOverPanel(mouse.position.ReadValue()))
            {
                return;
            }

            // 不同平台一格滚轮可能是 ±1 或 ±120；这里只取方向，一次滚动一格
            target.verticalNormalizedPosition = Mathf.Clamp01(
                target.verticalNormalizedPosition + Mathf.Sign(wheel) * normalizedStep);
        }

        private bool IsPointerOverPanel(Vector2 screenPosition)
        {
            CacheReferences();

            if (panelRect == null)
            {
                return false;
            }

            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            return RectTransformUtility.RectangleContainsScreenPoint(panelRect, screenPosition, camera);
        }

        private void CacheReferences()
        {
            if (panelRect == null)
            {
                panelRect = transform as RectTransform;
            }

            if (canvas == null)
            {
                canvas = GetComponentInParent<Canvas>();
            }
        }
    }
}
