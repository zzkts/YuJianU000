using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 世界空间文字提示，专给“文化碎片”这类非数字信息用。
    /// 不依赖 Canvas、TextMeshPro，也不依赖 DamagePopup 预制体。
    /// 用法：FragmentPopup.Spawn(位置, "文化碎片 +1", 颜色);
    /// </summary>
    public class FragmentPopup : MonoBehaviour
    {
        private static Font cachedChineseFont;
        private static Font cachedFallbackFont;

        private TextMesh textMesh;
        private Vector3 startPosition;
        private Color baseColor;
        private float elapsed;

        private const float Duration = 1.8f;

        public static void Spawn(Vector3 worldPosition, string text, Color color)
        {
            Font font = GetFont();
            if (font == null) return;

            GameObject popupObject = new GameObject("CulturalFragmentPopup");
            popupObject.transform.position = worldPosition + Random.insideUnitSphere * 0.1f;

            TextMesh mesh = popupObject.AddComponent<TextMesh>();
            mesh.font = font;
            mesh.text = text;
            mesh.fontSize = 56;
            mesh.characterSize = 0.028f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;

            MeshRenderer renderer = popupObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            FragmentPopup popup = popupObject.AddComponent<FragmentPopup>();
            popup.textMesh = mesh;
            popup.startPosition = popupObject.transform.position;
            popup.baseColor = color;
        }

        private static Font GetFont()
        {
            if (cachedChineseFont != null) return cachedChineseFont;

            // 内置字体在部分系统上没有中文字形，优先找系统里的中文字体。
            try
            {
                cachedChineseFont = Font.CreateDynamicFontFromOSFont(
                    new string[] { "Microsoft YaHei", "微软雅黑", "SimHei", "PingFang SC", "Noto Sans CJK SC" },
                    32);
            }
            catch (System.Exception) { }

            if (cachedChineseFont != null) return cachedChineseFont;

            if (cachedFallbackFont == null)
            {
                try { cachedFallbackFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
                catch (System.Exception) { }
            }

            if (cachedFallbackFont == null)
            {
                try { cachedFallbackFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
                catch (System.Exception) { }
            }

            return cachedFallbackFont;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / Duration);

            // 先快后慢地往上飘
            transform.position = startPosition + Vector3.up * (1.1f * Mathf.Sqrt(progress));

            // 冒头时稍微放大
            float scale = progress < 0.12f ? Mathf.Lerp(1.3f, 1f, progress / 0.12f) : 1f;
            transform.localScale = Vector3.one * scale;

            // 最后 40% 淡出
            Color faded = baseColor;
            faded.a = 1f - Mathf.Clamp01((progress - 0.6f) / 0.4f);
            if (textMesh != null) textMesh.color = faded;

            if (Camera.main != null) transform.rotation = Camera.main.transform.rotation;

            if (progress >= 1f) Destroy(gameObject);
        }
    }
}