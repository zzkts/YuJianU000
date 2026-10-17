using System.Collections;
using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 受击闪白。和 Ore 自带的缩放 / 旋转叠加使用，不接管原有表现。
    /// 第一次受击时记住当前颜色（此时 OreTypeTag 已经染过色），闪光结束再还原。
    /// </summary>
    public class OreHitFlash : MonoBehaviour
    {
        [SerializeField] private Color flashColor = Color.white;
        [SerializeField] private float flashDuration = 0.08f;

        private Renderer[] renderers;
        private Color[] baseColors;
        private Coroutine routine;

        public void Play()
        {
            if (!isActiveAndEnabled) return;

            CacheBaseColors();
            if (renderers == null || renderers.Length == 0) return;

            if (routine != null)
            {
                StopCoroutine(routine);
                RestoreBaseColors();
            }

            routine = StartCoroutine(FlashRoutine());
        }

        /// <summary>矿石换种类后重新记录底色，避免闪光结束把颜色还原成旧种类。</summary>
        public void RefreshBaseColors()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            renderers = null;
            baseColors = null;
            CacheBaseColors();
        }

        private void CacheBaseColors()
        {
            if (renderers != null && renderers.Length > 0) return;

            renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                renderers = null;
                return;
            }

            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                baseColors[i] = GetColor(renderers[i]);
            }
        }

        private IEnumerator FlashRoutine()
        {
            float t = 0f;
            while (t < flashDuration)
            {
                t += Time.deltaTime;
                float k = 1f - Mathf.Clamp01(t / flashDuration);

                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null) continue;
                    SetColor(renderers[i], Color.Lerp(baseColors[i], flashColor, k));
                }

                yield return null;
            }

            RestoreBaseColors();
            routine = null;
        }

        private void RestoreBaseColors()
        {
            if (renderers == null || baseColors == null) return;

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                SetColor(renderers[i], baseColors[i]);
            }
        }

        private static Color GetColor(Renderer renderer)
        {
            Material material = renderer.material;
            return material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
        }

        private static void SetColor(Renderer renderer, Color color)
        {
            Material material = renderer.material;
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        }
    }
}
