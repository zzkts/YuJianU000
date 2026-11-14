using UnityEngine;

namespace Yujian.Island
{
    /// <summary>
    /// 低模海面的海岸遮罩。
    ///
    /// 用海岛高度函数烘焙一张俯视遮罩：
    /// · R = 浪高衰减（深水 1，水线 0，陆地 0）；
    /// · G = 陆地遮罩（水线以上 1，水下 0），Shader 用它丢弃盖在岛上的水面；
    /// · B/A 暂未使用。
    ///
    /// 贴图覆盖海岛网格的完整范围，世界 XZ 映射到 0..1；
    /// 海岛范围之外 Shader 会忽略这张贴图，保持原浪高。
    /// </summary>
    public static class LowPolyOceanShoreMask
    {
        public const int DefaultResolution = 256;
        public const float DefaultFadeDepth = 2.5f;

        /// <summary>
        /// 遮罩正方形覆盖范围的一半。公式与 IslandMeshGenerator.Generate 的采样范围一致，
        /// 取 X / Z 两个方向里更大的那个，保证整个岛（含噪声和装饰山体）都在遮罩里。
        /// </summary>
        public static float ComputeHalfExtent(IslandSettings settings)
        {
            if (settings == null)
            {
                return 100f;
            }

            float coastWidth = Mathf.Max(settings.beachWidth + 0.01f, settings.coastWidth);
            float extentX = settings.halfSizeX + coastWidth
                + settings.coastNoiseLarge + settings.coastNoiseSmall + 2f;
            float extentZ = settings.halfSizeZ + coastWidth
                + settings.coastNoiseLarge + settings.coastNoiseSmall + 2f;
            return Mathf.Max(extentX, extentZ);
        }

        /// <summary>
        /// 生成一份新的遮罩贴图（不落盘）。调用方负责销毁。
        /// </summary>
        public static Texture2D Generate(
            IslandSettings settings, int resolution, float fadeDepth)
        {
            int size = Mathf.Clamp(resolution, 16, 1024);
            Texture2D texture = new Texture2D(
                size, size, TextureFormat.RGBA32, false, true)
            {
                name = "OceanShoreMask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 1,
            };
            Fill(texture, settings, fadeDepth);
            return texture;
        }

        /// <summary>
        /// 就地重写贴图像素，保留 Texture2D 资产引用不变。
        /// </summary>
        public static void Fill(Texture2D texture, IslandSettings settings, float fadeDepth)
        {
            if (texture == null)
            {
                throw new System.ArgumentNullException(nameof(texture));
            }
            if (settings == null)
            {
                throw new System.ArgumentNullException(nameof(settings));
            }

            int width = texture.width;
            int height = texture.height;
            float halfExtent = ComputeHalfExtent(settings);
            float size = halfExtent * 2f;
            float[] heights = new float[width * height];
            for (int y = 0; y < height; y++)
            {
                float localZ = ((y + 0.5f) / height - 0.5f) * size;
                for (int x = 0; x < width; x++)
                {
                    float localX = ((x + 0.5f) / width - 0.5f) * size;
                    heights[y * width + x] = IslandMeshGenerator.SampleHeight(
                        settings, localX, localZ);
                }
            }

            FillFromHeights(texture, heights, settings.waterLevel, fadeDepth);
        }

        /// <summary>
        /// 用一张高度采样表写遮罩。高度数组按行优先排列；
        /// float.NegativeInfinity 表示该像素没有碰到任何地形（深水）。
        /// </summary>
        public static void FillFromHeights(
            Texture2D texture, float[] heights, float waterLevel, float fadeDepth)
        {
            if (texture == null)
            {
                throw new System.ArgumentNullException(nameof(texture));
            }
            if (heights == null)
            {
                throw new System.ArgumentNullException(nameof(heights));
            }

            int width = texture.width;
            int height = texture.height;
            int count = width * height;
            if (heights.Length < count)
            {
                throw new System.ArgumentException(
                    "高度采样数量少于贴图像素数。", nameof(heights));
            }

            float fade = Mathf.Max(fadeDepth, 0.01f);
            Color32[] pixels = new Color32[count];
            for (int i = 0; i < count; i++)
            {
                float terrainHeight = heights[i];
                if (float.IsNegativeInfinity(terrainHeight))
                {
                    // 没有地形 = 深水。
                    pixels[i] = new Color32(255, 0, 0, 255);
                    continue;
                }

                // 水深越浅浪越小；水线处为 0，深水为 1。
                float waterDepth = waterLevel - terrainHeight;
                float waveMask = Mathf.Clamp01(waterDepth / fade);
                waveMask = waveMask * waveMask * (3f - 2f * waveMask);

                // 1.0 世界单位的软过渡；0.5 正好是水线。
                // Shader 用 0.8 裁剪，让水面往沙滩里多盖约 0.3 高度，
                // 把低模沙滩的锯齿藏到半透明水下面。
                float landMask = Mathf.Clamp01(
                    (terrainHeight - waterLevel) / 1.0f + 0.5f);

                pixels[i] = new Color32(ToByte(waveMask), ToByte(landMask), 0, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
        }

        private static byte ToByte(float value)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
        }
    }
}
