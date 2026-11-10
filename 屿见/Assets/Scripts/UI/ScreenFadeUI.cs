using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Yujian.UI
{
    /// <summary>
    /// 全屏黑场（策划案 2.3.2 / 2.3.3 的「屏幕黑色渐入渐出转场」）。
    ///
    /// 运行时装一块最上层的全屏黑色 Image：
    ///   · FadeToBlack：透明 → 全黑，转场前半段，同时挡住鼠标，避免连点；
    ///   · FadeFromBlack：全黑 → 透明，转场后半段，结束后取消挡点击。
    ///
    /// 用 unscaledDeltaTime，关卡暂停 / 时间缩放都不会让转场卡住。
    /// 不依赖场景里的任何资源，第一次访问 Instance 时自动创建。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScreenFadeUI : MonoBehaviour
    {
        private const int SortingOrder = 30000;

        private static ScreenFadeUI instance;

        private Canvas canvas;
        private Image overlay;

        /// <summary>全局唯一实例。不存在时自动创建。</summary>
        public static ScreenFadeUI Instance
        {
            get
            {
                if (instance == null)
                {
                    // Awake 会把 instance 填上
                    new GameObject("ScreenFade", typeof(RectTransform), typeof(Canvas),
                        typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(ScreenFadeUI));
                }

                return instance;
            }
        }

        /// <summary>当前是否完全遮黑。</summary>
        public bool IsOpaque => overlay != null && overlay.color.a >= 0.999f;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            Build();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        /// <summary>渐隐到全黑。</summary>
        public IEnumerator FadeToBlack(float duration)
        {
            return Fade(0f, 1f, duration);
        }

        /// <summary>从全黑渐显。</summary>
        public IEnumerator FadeFromBlack(float duration)
        {
            return Fade(1f, 0f, duration);
        }

        /// <summary>立即设置黑场透明度（0 = 完全透明）。</summary>
        public void SetAlpha(float alpha)
        {
            Build();

            if (overlay == null)
            {
                return;
            }

            Color color = overlay.color;
            color.a = Mathf.Clamp01(alpha);
            overlay.color = color;

            // 完全透明时不挡点击，半透明 / 全黑时挡住，避免转场途中连点
            overlay.raycastTarget = color.a > 0.001f;
        }

        private IEnumerator Fade(float from, float to, float duration)
        {
            Build();

            if (overlay == null)
            {
                yield break;
            }

            SetAlpha(from);

            if (duration <= 0f)
            {
                SetAlpha(to);
                yield break;
            }

            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }

            SetAlpha(to);
        }

        private void Build()
        {
            if (overlay != null)
            {
                return;
            }

            gameObject.layer = 5;
            transform.SetParent(null, false);

            canvas = GetComponent<Canvas>();

            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            CanvasScaler scaler = GetComponent<CanvasScaler>();

            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            RectTransform rect = transform as RectTransform;

            if (rect == null)
            {
                rect = gameObject.AddComponent<RectTransform>();
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            overlay = GetComponent<Image>();

            if (overlay == null)
            {
                overlay = gameObject.AddComponent<Image>();
            }

            overlay.color = new Color(0f, 0f, 0f, 0f);
            overlay.raycastTarget = false;
        }
    }
}
