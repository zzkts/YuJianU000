using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Yujian.UI
{
    /// <summary>
    /// 屏幕中央的短提示（策划案 2.3.2「若玩家点击未解锁的关卡时，提示『当前等级不足』」）。
    ///
    /// 运行时自建一块最上层 Canvas，不依赖场景资源。同一时间只显示一条：
    /// 新提示会顶掉上一条，避免玩家连点未解锁关卡时叠出一摞文字。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ToastUI : MonoBehaviour
    {
        private const int SortingOrder = 30010;
        private const float FadeInDuration = 0.12f;
        private const float FadeOutDuration = 0.25f;

        private static ToastUI instance;

        private Canvas canvas;
        private CanvasGroup group;
        private Text label;
        private RectTransform panel;
        private Coroutine routine;

        /// <summary>全局唯一实例。不存在时自动创建。</summary>
        public static ToastUI Instance
        {
            get
            {
                if (instance == null)
                {
                    new GameObject("Toast", typeof(RectTransform), typeof(Canvas),
                        typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(ToastUI));
                }

                // 编辑器（批处理验收 / 向导）里 Awake 不会跑，instance 仍为空；
                // 这时候调用方应当安静跳过，而不是抛空引用
                return instance;
            }
        }

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

        /// <summary>弹一条提示。duration 是保持在全亮状态的时间（秒，不受时间缩放影响）。</summary>
        public static void Show(string message, float duration = 1.2f)
        {
            ToastUI toast = Instance;

            if (toast == null)
            {
                return;
            }

            toast.ShowInternal(message, duration);
        }

        private void ShowInternal(string message, float duration)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (routine != null)
            {
                StopCoroutine(routine);
            }

            routine = StartCoroutine(Play(message, Mathf.Max(0.1f, duration)));
        }

        private IEnumerator Play(string message, float duration)
        {
            panel.gameObject.SetActive(true);
            label.text = message;
            group.alpha = 0f;

            float elapsed = 0f;

            while (elapsed < FadeInDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.Clamp01(elapsed / FadeInDuration);
                yield return null;
            }

            group.alpha = 1f;
            yield return new WaitForSecondsRealtime(duration);

            elapsed = 0f;

            while (elapsed < FadeOutDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = 1f - Mathf.Clamp01(elapsed / FadeOutDuration);
                yield return null;
            }

            group.alpha = 0f;
            panel.gameObject.SetActive(false);
            routine = null;
        }

        private void Build()
        {
            if (panel != null)
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

            RectTransform root = transform as RectTransform;

            if (root == null)
            {
                root = gameObject.AddComponent<RectTransform>();
            }

            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.pivot = new Vector2(0.5f, 0.5f);
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            panel = CreateRect("Panel", root);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = new Vector2(0f, -40f);
            panel.sizeDelta = new Vector2(420f, 86f);

            Image background = panel.gameObject.AddComponent<Image>();
            background.color = new Color(0.05f, 0.06f, 0.08f, 0.82f);
            background.raycastTarget = false;

            group = panel.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            label = CreateText("Label", panel);
            label.text = string.Empty;

            panel.gameObject.SetActive(false);
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static Text CreateText(string name, Transform parent)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(12f, 8f);
            rect.offsetMax = new Vector2(-12f, -8f);

            Text text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 26;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.96f, 0.96f, 0.96f, 1f);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
