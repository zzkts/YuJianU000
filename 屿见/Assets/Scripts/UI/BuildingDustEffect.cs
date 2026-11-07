using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Yujian.UI
{
    /// <summary>
    /// 拆除建筑时的尘埃（策划案 2026-11-06 美术需求）：黄褐色、类似体积云的团状粒子。
    ///
    /// 不依赖任何美术资源：运行时生成柔边圆形贴图，用屏幕空间 UI Image 叠加；
    /// 每帧把世界坐标投影回画布，镜头移动时尘埃仍贴在原建筑位置。
    /// 由 BuildingProgressTracker 订阅 BuildingRemoved 后调用，场景里不需要手动摆物体。
    /// </summary>
    public class BuildingDustEffect : MonoBehaviour
    {
        private const int PuffCount = 16;

        private static BuildingDustEffect instance;

        private RectTransform overlayRoot;
        private Sprite softCircleSprite;

        /// <summary>在指定世界坐标生成一团尘埃。</summary>
        public static void Play(Vector3 worldPosition)
        {
            if (instance == null)
            {
                GameObject host = new GameObject("BuildingDustEffect");
                instance = host.AddComponent<BuildingDustEffect>();
            }

            instance.Emit(worldPosition);
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            BuildOverlay();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private void BuildOverlay()
        {
            GameObject canvasObject = new GameObject(
                "BuildingDustCanvas",
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 盖在 3D 之上、压在其他 HUD 下面
            canvas.sortingOrder = -10;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            overlayRoot = canvasObject.GetComponent<RectTransform>();
            overlayRoot.anchorMin = Vector2.zero;
            overlayRoot.anchorMax = Vector2.one;
            overlayRoot.offsetMin = Vector2.zero;
            overlayRoot.offsetMax = Vector2.zero;

            softCircleSprite = CreateSoftCircleSprite();
        }

        private void Emit(Vector3 worldPosition)
        {
            if (overlayRoot == null)
            {
                return;
            }

            Camera camera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();

            if (camera == null)
            {
                return;
            }

            Vector3 anchor = worldPosition + Vector3.up * 0.35f;

            for (int i = 0; i < PuffCount; i++)
            {
                Vector3 drift = new Vector3(
                    Random.Range(-2.2f, 2.2f),
                    Random.Range(0.6f, 2f),
                    Random.Range(-2.2f, 2.2f));

                SpawnPuff(
                    camera,
                    anchor,
                    drift,
                    Random.Range(46f, 96f),
                    Random.Range(1.1f, 1.7f),
                    Random.Range(0f, 0.12f));
            }
        }

        private void SpawnPuff(
            Camera camera, Vector3 anchor, Vector3 drift, float size, float lifetime, float delay)
        {
            GameObject puff = new GameObject("DustPuff", typeof(RectTransform), typeof(Image));
            puff.transform.SetParent(overlayRoot, false);

            RectTransform rect = (RectTransform)puff.transform;
            rect.sizeDelta = new Vector2(size, size);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            Image image = puff.GetComponent<Image>();
            image.sprite = softCircleSprite;
            image.raycastTarget = false;
            image.color = new Color(
                Random.Range(0.55f, 0.68f),
                Random.Range(0.44f, 0.56f),
                Random.Range(0.3f, 0.4f),
                Random.Range(0.34f, 0.52f));

            StartCoroutine(AnimatePuff(rect, image, camera, anchor, drift, lifetime, delay));
        }

        private IEnumerator AnimatePuff(
            RectTransform rect, Image image, Camera camera,
            Vector3 anchor, Vector3 drift, float lifetime, float delay)
        {
            Color baseColor = image.color;
            float elapsed = -delay;

            while (elapsed < lifetime && rect != null && image != null)
            {
                elapsed += Time.unscaledDeltaTime;

                if (elapsed < 0f)
                {
                    SetAlpha(image, baseColor, 0f);
                    yield return null;
                    continue;
                }

                float t = Mathf.Clamp01(elapsed / lifetime);
                Vector3 world = anchor + drift * Mathf.SmoothStep(0f, 1f, t);
                Vector3 screenPoint = camera.WorldToScreenPoint(world);

                if (screenPoint.z < 0f)
                {
                    SetAlpha(image, baseColor, 0f);
                    yield return null;
                    continue;
                }

                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    overlayRoot, screenPoint, null, out Vector2 localPoint);
                rect.anchoredPosition = localPoint;

                float reveal = Mathf.Clamp01(t / 0.15f);
                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, t));
                SetAlpha(image, baseColor, baseColor.a * reveal * fade);

                float scale = Mathf.Lerp(0.55f, 2.1f, t);
                rect.localScale = new Vector3(scale, scale, 1f);

                yield return null;
            }

            if (rect != null)
            {
                Destroy(rect.gameObject);
            }
        }

        private static void SetAlpha(Image image, Color baseColor, float alpha)
        {
            Color color = baseColor;
            color.a = alpha;
            image.color = color;
        }

        private static Sprite CreateSoftCircleSprite()
        {
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "BuildingDustSoftCircle",
                wrapMode = TextureWrapMode.Clamp,
            };

            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center) / radius;
                    float alpha = Mathf.Clamp01(1f - distance);
                    alpha = alpha * alpha * (3f - 2f * alpha);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
