using UnityEngine;

namespace Yujian.UI
{
    /// <summary>
    /// 世界坐标 → Canvas 本地坐标的唯一实现。
    ///
    /// 建筑上方的升级按钮、迁移确认按钮都要跟着一座会移动的建筑，
    /// 两处各写一份投影，迟早会出现「一个跟着建筑、一个没跟上」的差异。
    ///
    /// 屏幕空间 Overlay 的 Canvas 传 null 相机；Camera / World Space 才传 worldCamera——
    /// 传错的表现是按钮整体偏移，而且不会报任何错。
    /// </summary>
    public static class WorldToCanvas
    {
        /// <summary>
        /// 把世界坐标投影成 Canvas 下的本地坐标。相机看不到（在背后）时返回 false。
        /// </summary>
        /// <param name="viewCamera">世界相机，为空返回 false。</param>
        /// <param name="canvas">目标 Canvas，为空返回 false。</param>
        /// <param name="worldPosition">要跟随的世界坐标。</param>
        /// <param name="pixelOffset">额外的屏幕像素偏移（例如往上抬一点避开建筑本体）。</param>
        /// <param name="localPosition">Canvas 本地坐标。</param>
        public static bool TryProject(
            Camera viewCamera, Canvas canvas, Vector3 worldPosition, Vector2 pixelOffset,
            out Vector2 localPosition)
        {
            localPosition = default;

            if (viewCamera == null || canvas == null || !(canvas.transform is RectTransform canvasRect))
            {
                return false;
            }

            Vector3 screenPosition = viewCamera.WorldToScreenPoint(worldPosition);

            if (screenPosition.z <= 0f)
            {
                return false;
            }

            screenPosition.x += pixelOffset.x;
            screenPosition.y += pixelOffset.y;

            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screenPosition, uiCamera, out localPosition);
        }

        /// <summary>把内容夹在 Canvas 范围内，避免建筑贴近屏幕边缘时按钮跑出屏幕点不到。</summary>
        public static Vector2 ClampToCanvas(Vector2 localPosition, RectTransform content, Canvas canvas)
        {
            if (content == null || canvas == null || !(canvas.transform is RectTransform canvasRect))
            {
                return localPosition;
            }

            Rect bounds = canvasRect.rect;
            Vector2 size = content.rect.size;
            float halfWidth = size.x * 0.5f;
            float halfHeight = size.y * 0.5f;
            float padding = 24f;

            float minX = bounds.xMin + halfWidth + padding;
            float maxX = bounds.xMax - halfWidth - padding;
            float minY = bounds.yMin + halfHeight + padding;
            float maxY = bounds.yMax - halfHeight - padding;

            if (minX <= maxX)
            {
                localPosition.x = Mathf.Clamp(localPosition.x, minX, maxX);
            }

            if (minY <= maxY)
            {
                localPosition.y = Mathf.Clamp(localPosition.y, minY, maxY);
            }

            return localPosition;
        }
    }
}
