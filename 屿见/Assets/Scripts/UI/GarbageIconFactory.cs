using UnityEngine;

namespace Yujian.UI
{
    /// <summary>
    /// 资源栏的垃圾图标：运行时画一个简洁的垃圾桶，替代原来的「垃」字占位。
    ///
    /// 不依赖任何美术资源，进 Play 就能显示；美术出图后把 Sprite 拖到
    /// MiningHudUI 的「资源栏图标」字段即可整体替换，这里的代码不会再生效。
    ///
    /// 画法：在 96×96 上做 4×4 超采样，先画桶身（带描边和竖向压痕），
    /// 再画桶盖与提手，最后按透明度做预乘平均，得到干净的抗锯齿边缘。
    /// </summary>
    public static class GarbageIconFactory
    {
        private const int DefaultResolution = 96;
        private const int DefaultSamplesPerAxis = 4;

        private static readonly Color OutlineColor = new Color(0.15f, 0.17f, 0.21f, 1f);
        private static readonly Color BodyBottomColor = new Color(0.68f, 0.70f, 0.75f, 1f);
        private static readonly Color BodyTopColor = new Color(0.90f, 0.92f, 0.95f, 1f);
        private static readonly Color LidColor = new Color(0.78f, 0.80f, 0.85f, 1f);
        private static readonly Color Clear = new Color(0f, 0f, 0f, 0f);
        private static readonly float[] RibCenters = { 0.425f, 0.575f };

        /// <summary>生成一个可直接用于 UI Image 的图标 Sprite。</summary>
        public static Sprite Create(int resolution = DefaultResolution,
            int samplesPerAxis = DefaultSamplesPerAxis)
        {
            resolution = Mathf.Max(16, resolution);
            samplesPerAxis = Mathf.Clamp(samplesPerAxis, 1, 8);

            Color[] pixels = new Color[resolution * resolution];
            int sampleCount = samplesPerAxis * samplesPerAxis;

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float r = 0f;
                    float g = 0f;
                    float b = 0f;
                    float a = 0f;

                    for (int sy = 0; sy < samplesPerAxis; sy++)
                    {
                        for (int sx = 0; sx < samplesPerAxis; sx++)
                        {
                            float u = (x + (sx + 0.5f) / samplesPerAxis) / resolution;
                            float v = 1f - (y + (sy + 0.5f) / samplesPerAxis) / resolution;
                            Color sample = Sample(u, v);

                            // 预乘 alpha 再平均，避免边缘混进黑边
                            r += sample.r * sample.a;
                            g += sample.g * sample.a;
                            b += sample.b * sample.a;
                            a += sample.a;
                        }
                    }

                    a /= sampleCount;

                    if (a <= 0.0001f)
                    {
                        pixels[y * resolution + x] = Clear;
                        continue;
                    }

                    pixels[y * resolution + x] = new Color(
                        r / (a * sampleCount),
                        g / (a * sampleCount),
                        b / (a * sampleCount),
                        a);
                }
            }

            Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = "GarbageIcon_Runtime",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, resolution, resolution),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            sprite.name = "GarbageIcon_Runtime";
            return sprite;
        }

        private static Color Sample(float x, float y)
        {
            // 提手
            if (RoundedRect(x, y, 0.415f, 0.585f, 0.775f, 0.875f, 0.028f))
            {
                if (RoundedRect(x, y, 0.445f, 0.555f, 0.805f, 0.875f, 0.018f))
                {
                    return LidColor;
                }

                if (!RoundedRect(x, y, 0.428f, 0.572f, 0.788f, 0.862f, 0.015f))
                {
                    return OutlineColor;
                }

                return LidColor;
            }

            // 桶盖
            if (RoundedRect(x, y, 0.135f, 0.865f, 0.695f, 0.800f, 0.035f))
            {
                if (!RoundedRect(x, y, 0.155f, 0.845f, 0.712f, 0.783f, 0.024f))
                {
                    return OutlineColor;
                }

                return y > 0.762f ? Color.Lerp(LidColor, Color.white, 0.30f) : LidColor;
            }

            // 桶身
            if (y >= 0.13f && y <= 0.70f)
            {
                float half = BodyHalfWidth(y);
                float offset = Mathf.Abs(x - 0.5f);

                if (offset <= half)
                {
                    float edge = Mathf.Min(half - offset, y - 0.13f);

                    if (y > 0.68f)
                    {
                        edge = Mathf.Min(edge, 0.70f - y);
                    }

                    if (edge < 0.020f)
                    {
                        return OutlineColor;
                    }

                    float t = Mathf.Clamp01((y - 0.13f) / 0.57f);
                    Color color = Color.Lerp(BodyBottomColor, BodyTopColor, t);

                    float highlightX = (x - 0.36f) / 0.035f;
                    float highlightY = (y - 0.42f) / 0.16f;
                    float highlight = highlightX * highlightX + highlightY * highlightY;

                    if (highlight < 1f)
                    {
                        color = Color.Lerp(color, Color.white, 0.38f * (1f - highlight));
                    }

                    for (int i = 0; i < RibCenters.Length; i++)
                    {
                        float ribX = (x - RibCenters[i]) / 0.012f;

                        if (ribX * ribX < 1f && y > 0.20f && y < 0.62f)
                        {
                            float fade = Mathf.Min(1f, Mathf.Min((y - 0.20f) / 0.06f,
                                (0.62f - y) / 0.06f));
                            color = Color.Lerp(color, OutlineColor, 0.22f * Mathf.Max(0f, fade));
                        }
                    }

                    return color;
                }
            }

            return Clear;
        }

        private static float BodyHalfWidth(float y)
        {
            float t = Mathf.Clamp01((0.70f - y) / 0.60f);
            float half = 0.355f - 0.065f * t;

            // 底部圆角：超级椭圆，收得比正圆更平一些
            if (y < 0.16f)
            {
                float d = (0.16f - y) / 0.06f;
                float d2 = d * d;
                half *= Mathf.Sqrt(Mathf.Max(0f, 1f - d2 * d2));
            }

            return half;
        }

        private static bool RoundedRect(float x, float y,
            float x0, float x1, float y0, float y1, float radius)
        {
            if (x < x0 || x > x1 || y < y0 || y > y1)
            {
                return false;
            }

            float cx = Mathf.Clamp(x, x0 + radius, x1 - radius);
            float cy = Mathf.Clamp(y, y0 + radius, y1 - radius);
            float dx = x - cx;
            float dy = y - cy;
            return dx * dx + dy * dy <= radius * radius;
        }
    }
}
