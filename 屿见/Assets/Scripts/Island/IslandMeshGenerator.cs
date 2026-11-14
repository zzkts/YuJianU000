using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Yujian.Island
{
    /// <summary>
    /// 低模海岛的生成参数。纯数据，不依赖场景物体，
    /// 编辑器向导和后续运行时调试可以共用同一份配置。
    /// </summary>
    [System.Serializable]
    public class IslandSettings
    {
        [Tooltip("海岛根物体所在的世界坐标，也是可建台地的中心。")]
        public Vector3 center = new Vector3(10.21735f, 0f, -9.49205f);

        [Tooltip("平整台地的半宽（X 方向）。要覆盖现有 92.4×92.4 的 Plane，至少 46.19。")]
        public float halfSizeX = 52f;

        [Tooltip("平整台地的半深（Z 方向）。")]
        public float halfSizeZ = 52f;

        [Tooltip("台地圆角半径。")]
        public float cornerRadius = 14f;

        [Tooltip("从台地边缘到水面的基准沙滩宽度。")]
        public float beachWidth = 10f;

        [Tooltip("从台地边缘到海床截断处的总宽度，必须大于沙滩宽度。")]
        public float coastWidth = 18f;

        [Tooltip("岬角/海湾起伏强度，0 = 均匀沙滩。")]
        [Range(0f, 1f)] public float shoreVariation = 0.85f;

        [Tooltip("岬角数量（低频瓣数）。3~4 比较自然。")]
        public int headlandCount = 3;

        [Tooltip("湿沙带半高（世界单位），在水线上下各这么高。")]
        public float wetSandBand = 0.22f;

        [Tooltip("海平面高度，水面物体也放在这个高度。")]
        public float waterLevel = -1.4f;

        [Tooltip("水下海床的高度。")]
        public float seabedDepth = -4f;

        [Tooltip("网格步长（世界单位）。越小越细，2 左右适合低模。")]
        public float gridSize = 2f;

        [Tooltip("随机种子，保证海岸线可复现。")]
        public int seed = 20261017;

        [Tooltip("大尺度海岸线噪声的振幅。")]
        public float coastNoiseLarge = 4f;

        [Tooltip("小尺度海岸线噪声的振幅。")]
        public float coastNoiseSmall = 1.5f;

        [Tooltip("装饰山体在台地局部坐标里的中心。默认在台地北侧水岸外。")]
        public Vector2 hillCenter = new Vector2(0f, 61.5f);

        [Tooltip("装饰山体的影响半径。")]
        public float hillRadius = 10f;

        [Tooltip("装饰山体的高度。")]
        public float hillHeight = 14f;

        [Header("岛基岩石")]
        [Tooltip("沿岛基分布的岩石簇数量。0 = 不生成。")]
        public int rockClusterCount = 40;

        [Tooltip("每个岩石簇被保留的概率（0~1），用来制造疏密变化。")]
        [Range(0f, 1f)] public float rockDensity = 0.85f;

        [Tooltip("单块岩石的最小尺寸（世界单位）。")]
        public float rockSizeMin = 0.8f;

        [Tooltip("单块岩石的最大尺寸（世界单位），越大越靠近岛基。")]
        public float rockSizeMax = 3.2f;

        [Tooltip("一个岩石簇的散布半径。")]
        public float rockClusterSpread = 5f;

        [Tooltip("水下岩石允许露出水面的最大高度。")]
        public float rockMaxAboveWater = 0.9f;

        public float NoiseOffsetX => (seed % 997) * 0.731f;
        public float NoiseOffsetY => ((seed / 997) % 997) * 0.531f;
    }

    /// <summary>
    /// 纯静态的低模海岛网格生成器：圆角矩形台地 + 噪声海岸 + 沙滩/海床 + 装饰山体。
    /// 每个三角形使用独立顶点并直接写入面法线，得到硬边 low-poly 观感。
    /// 不创建任何 Unity 资产，也不引用 UnityEditor。
    /// </summary>
    public static class IslandMeshGenerator
    {
        public const int GrassSubmesh = 0;
        public const int SandSubmesh = 1;
        public const int RockSubmesh = 2;
        public const int WetSandSubmesh = 3;
        public const int SubmeshCount = 4;

        /// <summary>
        /// 生成海岛网格。返回的 Mesh 有 4 个子网格（草/沙/岩/湿沙），
        /// 顶点不共享，法线为逐面法线。
        /// </summary>
        public static Mesh Generate(IslandSettings settings)
        {
            if (settings == null)
            {
                throw new System.ArgumentNullException(nameof(settings));
            }

            float gridSize = Mathf.Max(0.1f, settings.gridSize);
            // 岬角/海湾会让海岸宽度变化，采样范围按最大宽度留够。
            float maxCoastWidth = Mathf.Max(
                settings.coastWidth * 1.35f,
                settings.beachWidth * 1.45f + 1f);

            // 采样范围要盖住“台地 + 海岸带 + 噪声扰动”，否则海岸线会被硬切。
            float extentX = settings.halfSizeX + maxCoastWidth
                + settings.coastNoiseLarge + settings.coastNoiseSmall + 2f;
            float extentZ = settings.halfSizeZ + maxCoastWidth
                + settings.coastNoiseLarge + settings.coastNoiseSmall + 2f;

            int cellsX = Mathf.Max(1, Mathf.CeilToInt(extentX * 2f / gridSize));
            int cellsZ = Mathf.Max(1, Mathf.CeilToInt(extentZ * 2f / gridSize));
            float stepX = extentX * 2f / cellsX;
            float stepZ = extentZ * 2f / cellsZ;

            int verticesX = cellsX + 1;
            int verticesZ = cellsZ + 1;
            Vector3[] gridPositions = new Vector3[verticesX * verticesZ];
            float[] gridDistances = new float[verticesX * verticesZ];

            for (int iz = 0; iz < verticesZ; iz++)
            {
                float z = -extentZ + iz * stepZ;
                for (int ix = 0; ix < verticesX; ix++)
                {
                    float x = -extentX + ix * stepX;
                    int index = iz * verticesX + ix;
                    gridPositions[index] = SamplePoint(settings, x, z, out float distance);
                    gridDistances[index] = distance;
                }
            }

            List<Vector3> vertices = new List<Vector3>(cellsX * cellsZ * 6);
            List<Vector3> normals = new List<Vector3>(cellsX * cellsZ * 6);
            List<Vector2> uvs = new List<Vector2>(cellsX * cellsZ * 6);
            List<int>[] indices = new List<int>[SubmeshCount];
            for (int i = 0; i < SubmeshCount; i++)
            {
                indices[i] = new List<int>(cellsX * cellsZ);
            }

            for (int iz = 0; iz < cellsZ; iz++)
            {
                for (int ix = 0; ix < cellsX; ix++)
                {
                    int i00 = iz * verticesX + ix;
                    int i10 = i00 + 1;
                    int i01 = i00 + verticesX;
                    int i11 = i01 + 1;

                    Vector3 p00 = gridPositions[i00];
                    Vector3 p10 = gridPositions[i10];
                    Vector3 p01 = gridPositions[i01];
                    Vector3 p11 = gridPositions[i11];

                    float d00 = gridDistances[i00];
                    float d10 = gridDistances[i10];
                    float d01 = gridDistances[i01];
                    float d11 = gridDistances[i11];

                    AddTriangle(settings, maxCoastWidth,p00, p01, p11, d00, d01, d11,
                        vertices, normals, uvs, indices);
                    AddTriangle(settings, maxCoastWidth,p00, p11, p10, d00, d11, d10,
                        vertices, normals, uvs, indices);
                }
            }

            AddBaseRocks(settings, vertices, normals, uvs, indices);

            Mesh mesh = new Mesh { name = "IslandTerrain" };
            // 顶点数可能超过 65535，直接用 UInt32 索引。
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = SubmeshCount;
            for (int i = 0; i < SubmeshCount; i++)
            {
                mesh.SetTriangles(indices[i], i);
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 世界坐标到海岸线的有符号距离：&lt;=0 表示在平整台地内。
        /// 编辑器向导用它校验矿石/玩家/建筑中心是否都落在可建区。
        /// </summary>
        public static float CoastDistance(IslandSettings settings, Vector3 worldPosition)
        {
            float x = worldPosition.x - settings.center.x;
            float z = worldPosition.z - settings.center.z;
            return ComputeCoastDistance(settings, x, z);
        }

        /// <summary>
        /// 单个采样点的高度。向导生成前可用它做台地平整性自检。
        /// </summary>
        public static float SampleHeight(IslandSettings settings, float localX, float localZ)
        {
            return SamplePoint(settings, localX, localZ, out _).y;
        }

        private static Vector3 SamplePoint(IslandSettings settings, float x, float z, out float distance)
        {
            distance = ComputeCoastDistance(settings, x, z);
            ShoreModulation(
                settings, x, z, out _, out float beachScale, out float coastScale);
            float height = HeightFromDistance(
                settings, distance, beachScale, coastScale);

            // 装饰山体：只在台地外隆起，门控保证可建区不被抬起。
            float hillX = x - settings.hillCenter.x;
            float hillZ = z - settings.hillCenter.y;
            float hillDistanceSqr = hillX * hillX + hillZ * hillZ;
            float hillRadiusSqr = Mathf.Max(0.01f, settings.hillRadius * settings.hillRadius);
            float hillMask = Mathf.Exp(-3f * hillDistanceSqr / hillRadiusSqr);
            float hillGate = SmoothStep01(Mathf.InverseLerp(2f, 6f, distance));
            height += settings.hillHeight * hillMask * hillGate;

            return new Vector3(x, height, z);
        }

        private static float ComputeCoastDistance(IslandSettings settings, float x, float z)
        {
            float distance = RoundedRectSdf(
                x, z, settings.halfSizeX, settings.halfSizeZ, settings.cornerRadius);

            // 噪声只在台地外淡入，保证可建区严格水平。
            float fade = SmoothStep01(Mathf.InverseLerp(2f, 8f, distance));
            float largeNoise = (Mathf.PerlinNoise(
                x * 0.025f + settings.NoiseOffsetX,
                z * 0.025f + settings.NoiseOffsetY) - 0.5f) * 2f;
            float smallNoise = (Mathf.PerlinNoise(
                x * 0.09f + settings.NoiseOffsetX * 1.7f,
                z * 0.09f + settings.NoiseOffsetY * 1.7f) - 0.5f) * 2f;

            return distance
                + (largeNoise * settings.coastNoiseLarge + smallNoise * settings.coastNoiseSmall) * fade;
        }

        /// <summary>
        /// 海岸调制：用低频正弦 + 噪声决定当前方向是“湾”（bayFactor≈1）
        /// 还是“岬角”（bayFactor≈0）。湾里沙滩更宽更缓，岬角沙滩收窄。
        /// </summary>
        private static void ShoreModulation(
            IslandSettings settings, float x, float z,
            out float bayFactor, out float beachScale, out float coastScale)
        {
            float angle = Mathf.Atan2(z, x);
            int lobes = Mathf.Max(2, settings.headlandCount);
            float harmonic = Mathf.Sin(angle * lobes + settings.NoiseOffsetX * 0.013f) * 0.55f
                + Mathf.Sin(angle * (lobes * 2 + 1) - settings.NoiseOffsetY * 0.017f + 1.7f) * 0.25f;
            float noise = (Mathf.PerlinNoise(
                x * 0.017f + settings.NoiseOffsetX,
                z * 0.017f + settings.NoiseOffsetY) - 0.5f) * 1.2f;
            float raw = (harmonic + noise) * settings.shoreVariation;

            bayFactor = Mathf.Clamp01(0.5f + raw * 0.7f);
            beachScale = Mathf.Lerp(0.45f, 1.45f, bayFactor);
            coastScale = Mathf.Lerp(1.1f, 1.35f, bayFactor);
        }

        private static float HeightFromDistance(
            IslandSettings settings, float distance,
            float beachScale, float coastScale)
        {
            float beachWidth = Mathf.Max(1f, settings.beachWidth * beachScale);
            float coastWidth = Mathf.Max(
                beachWidth + 1f, settings.coastWidth * coastScale);

            if (distance <= 0f)
            {
                return 0f;
            }

            if (distance < beachWidth)
            {
                return Mathf.Lerp(0f, settings.waterLevel,
                    SmootherStep01(distance / beachWidth));
            }

            if (distance < coastWidth)
            {
                float t = (distance - beachWidth) / (coastWidth - beachWidth);
                return Mathf.Lerp(settings.waterLevel, settings.seabedDepth, SmoothStep01(t));
            }

            return settings.seabedDepth;
        }

        // ------------------------------------------------------------------
        // 岛基岩石：水下礁石 + 岸边落石
        // 参考自然规律：岩石在岛基/坡脚成簇堆积，越靠近岛体越大，
        // 部分埋进海床，少量露出水面；形状用低模二十面体做不规则缩放。
        // ------------------------------------------------------------------

        private static readonly Vector3[] IcosahedronVertices =
        {
            new Vector3(-1f, 1.618f, 0f),
            new Vector3(1f, 1.618f, 0f),
            new Vector3(-1f, -1.618f, 0f),
            new Vector3(1f, -1.618f, 0f),
            new Vector3(0f, -1f, 1.618f),
            new Vector3(0f, 1f, 1.618f),
            new Vector3(0f, -1f, -1.618f),
            new Vector3(0f, 1f, -1.618f),
            new Vector3(1.618f, 0f, -1f),
            new Vector3(1.618f, 0f, 1f),
            new Vector3(-1.618f, 0f, -1f),
            new Vector3(-1.618f, 0f, 1f),
        };

        private static readonly int[] IcosahedronFaces =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };

        private static void AddBaseRocks(
            IslandSettings settings,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int>[] indices)
        {
            if (settings.rockClusterCount <= 0 || settings.rockDensity <= 0f)
            {
                return;
            }

            for (int cluster = 0; cluster < settings.rockClusterCount; cluster++)
            {
                float angle = Hash01(cluster, 1, settings.seed) * Mathf.PI * 2f;
                float offset = Mathf.Lerp(1.5f, 15f,
                    Hash01(cluster, 2, settings.seed));
                float centerRadius = FindWaterlineRadius(settings, angle) + offset;
                float clusterX = Mathf.Cos(angle) * centerRadius;
                float clusterZ = Mathf.Sin(angle) * centerRadius;
                Vector3 clusterGround = SamplePoint(
                    settings, clusterX, clusterZ, out _);
                if (clusterGround.y > settings.waterLevel - 0.25f)
                {
                    continue;
                }

                // 岬角多、湾内少：湾里概率降到约三成，岬角略高于基准。
                ShoreModulation(
                    settings, clusterX, clusterZ,
                    out float bayFactor, out _, out _);
                float clusterChance = settings.rockDensity
                    * Mathf.Lerp(1.15f, 0.3f, bayFactor);
                if (Hash01(cluster, 0, settings.seed) > clusterChance)
                {
                    continue;
                }

                float spread = settings.rockClusterSpread
                    * (0.6f + 0.8f * Hash01(cluster, 3, settings.seed));
                int boulderCount = 3 + Mathf.FloorToInt(
                    Hash01(cluster, 4, settings.seed) * 5f);

                for (int boulder = 0; boulder < boulderCount; boulder++)
                {
                    float localAngle = Hash01(cluster, 10 + boulder, settings.seed)
                        * Mathf.PI * 2f;
                    float localRadius = Mathf.Sqrt(
                        Hash01(cluster, 20 + boulder, settings.seed)) * spread;
                    float x = clusterX + Mathf.Cos(localAngle) * localRadius;
                    float z = clusterZ + Mathf.Sin(localAngle) * localRadius;

                    Vector3 ground = SamplePoint(settings, x, z, out float distance);
                    if (ground.y > settings.waterLevel - 0.35f || distance < 8f)
                    {
                        continue;
                    }

                    // 越靠近岛基越大，越远越小。
                    float depthT = Mathf.Clamp01((distance - 9f) / 14f);
                    float baseSize = Mathf.Lerp(
                        settings.rockSizeMax, settings.rockSizeMin, depthT);
                    float size = baseSize
                        * (0.55f + 0.9f * Hash01(cluster, 30 + boulder, settings.seed))
                        * Mathf.Lerp(1.25f, 0.75f, bayFactor);

                    float scaleX = size * (0.8f + 0.5f
                        * Hash01(cluster, 40 + boulder, settings.seed));
                    float scaleY = size * (0.45f + 0.5f
                        * Hash01(cluster, 50 + boulder, settings.seed));
                    float scaleZ = size * (0.8f + 0.5f
                        * Hash01(cluster, 60 + boulder, settings.seed));

                    // 约四分之一的岩石是扁平岩板，像被海浪磨平的海床基岩。
                    if (Hash01(cluster, 70 + boulder, settings.seed) < 0.25f)
                    {
                        scaleY *= 0.5f;
                        scaleX *= 1.4f;
                        scaleZ *= 1.4f;
                    }

                    // 取岩石覆盖范围内的最低地面，保证整块岩石埋进坡面，不会悬空。
                    float minGround = ground.y;
                    for (int corner = 0; corner < 4; corner++)
                    {
                        float cornerAngle = corner * Mathf.PI * 0.5f;
                        float sampleX = x + Mathf.Cos(cornerAngle) * scaleX * 0.7f;
                        float sampleZ = z + Mathf.Sin(cornerAngle) * scaleZ * 0.7f;
                        Vector3 sample = SamplePoint(settings, sampleX, sampleZ, out _);
                        minGround = Mathf.Min(minGround, sample.y);
                    }

                    float centerY = minGround + scaleY * 0.45f;
                    float maxCenterY = settings.waterLevel
                        + settings.rockMaxAboveWater - scaleY;
                    centerY = Mathf.Min(centerY, maxCenterY);
                    if (centerY + scaleY < minGround)
                    {
                        continue;
                    }

                    float yaw = Hash01(cluster, 80 + boulder, settings.seed) * 360f;
                    float tiltX = (Hash01(cluster, 90 + boulder, settings.seed) - 0.5f)
                        * 22f;
                    float tiltZ = (Hash01(cluster, 100 + boulder, settings.seed) - 0.5f)
                        * 22f;
                    Quaternion rotation = Quaternion.Euler(tiltX, yaw, tiltZ);
                    Vector3 boulderCenter = new Vector3(x, centerY, z);
                    Vector3 boulderScale = new Vector3(scaleX, scaleY, scaleZ);
                    AddBoulder(
                        settings,
                        vertices, normals, uvs, indices[RockSubmesh],
                        boulderCenter, boulderScale, rotation,
                        cluster * 131 + boulder);
                }
            }
        }

        private static float FindWaterlineRadius(IslandSettings settings, float angle)
        {
            float dirX = Mathf.Cos(angle);
            float dirZ = Mathf.Sin(angle);
            float start = Mathf.Max(settings.halfSizeX, settings.halfSizeZ)
                + settings.coastWidth + settings.coastNoiseLarge
                + settings.coastNoiseSmall + 20f;
            float step = 0.5f;
            float radius = start;
            while (radius > 1f)
            {
                float height = SamplePoint(
                    settings, dirX * radius, dirZ * radius, out _).y;
                if (height >= settings.waterLevel)
                {
                    return radius;
                }
                radius -= step;
            }

            return 20f;
        }

        private static void AddBoulder(
            IslandSettings settings,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> indices,
            Vector3 center,
            Vector3 scale,
            Quaternion rotation,
            int seed)
        {
            Vector3[] points = new Vector3[IcosahedronVertices.Length];
            for (int i = 0; i < IcosahedronVertices.Length; i++)
            {
                float jitter = 1f
                    + (Hash01(seed, i, settings.seed + 17) - 0.5f) * 0.35f;
                Vector3 point = IcosahedronVertices[i].normalized * jitter;
                point = Vector3.Scale(point, scale);
                points[i] = rotation * point + center;
            }

            for (int face = 0; face < IcosahedronFaces.Length; face += 3)
            {
                Vector3 a = points[IcosahedronFaces[face]];
                Vector3 b = points[IcosahedronFaces[face + 1]];
                Vector3 c = points[IcosahedronFaces[face + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 1e-8f)
                {
                    continue;
                }
                normal.Normalize();

                // 保证法线朝外；法线翻面时同时交换顶点顺序。
                if (Vector3.Dot(normal, (a + b + c) / 3f - center) < 0f)
                {
                    Vector3 swap = b;
                    b = c;
                    c = swap;
                    normal = -normal;
                }

                int start = vertices.Count;
                vertices.Add(a);
                normals.Add(normal);
                uvs.Add(new Vector2(a.x, a.z) * 0.05f);
                vertices.Add(b);
                normals.Add(normal);
                uvs.Add(new Vector2(b.x, b.z) * 0.05f);
                vertices.Add(c);
                normals.Add(normal);
                uvs.Add(new Vector2(c.x, c.z) * 0.05f);

                indices.Add(start);
                indices.Add(start + 1);
                indices.Add(start + 2);
            }
        }

        private static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0x00FFFFFFu) / 16777215f;
            }
        }

        private static void AddTriangle(
            IslandSettings settings,
            float coastWidth,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            float distanceA,
            float distanceB,
            float distanceC,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int>[] indices)
        {
            // 三个角都已经落在海床截断区外，这个三角形不需要生成。
            if (distanceA >= coastWidth && distanceB >= coastWidth && distanceC >= coastWidth)
            {
                return;
            }

            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-8f)
            {
                return;
            }
            normal.Normalize();

            float averageHeight = (a.y + b.y + c.y) / 3f;
            float averageDistance = (distanceA + distanceB + distanceC) / 3f;
            ShoreModulation(
                settings,
                (a.x + b.x + c.x) / 3f,
                (a.z + b.z + c.z) / 3f,
                out float bayFactor, out _, out _);
            int submesh = Classify(
                settings, averageHeight, averageDistance, normal, bayFactor);

            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            uvs.Add(new Vector2(a.x, a.z) * 0.05f);
            uvs.Add(new Vector2(b.x, b.z) * 0.05f);
            uvs.Add(new Vector2(c.x, c.z) * 0.05f);

            indices[submesh].Add(start);
            indices[submesh].Add(start + 1);
            indices[submesh].Add(start + 2);
        }

        private static int Classify(
            IslandSettings settings,
            float averageHeight,
            float averageDistance,
            Vector3 normal,
            float bayFactor)
        {
            // 台地（含噪声淡入前的肩部）恒为草地，保证可建区看起来是整片平地。
            if (averageDistance <= 2f)
            {
                return GrassSubmesh;
            }

            // 岬角：沙滩收窄，岩石从水线一直露到坡上；湾里不生成这圈岩石。
            if (bayFactor < 0.35f
                && averageHeight > settings.waterLevel - 0.8f
                && averageHeight < 2.5f)
            {
                return RockSubmesh;
            }

            // 湿沙带：水线上下各 wetSandBand 高，颜色比干沙深。
            if (averageHeight < settings.waterLevel + settings.wetSandBand
                && averageHeight > settings.waterLevel - settings.wetSandBand)
            {
                return WetSandSubmesh;
            }

            // 水下坡面并入沙滩基底，保持岛体/水线的颜色连续。
            if (averageHeight < settings.waterLevel + 0.15f)
            {
                return SandSubmesh;
            }

            if (averageHeight > 3f || 1f - normal.y > 0.45f)
            {
                return RockSubmesh;
            }

            if (averageHeight < 0.25f)
            {
                return SandSubmesh;
            }

            return GrassSubmesh;
        }

        private static float RoundedRectSdf(
            float x, float z, float halfX, float halfZ, float radius)
        {
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(halfX, halfZ));
            float qx = Mathf.Abs(x) - (halfX - radius);
            float qz = Mathf.Abs(z) - (halfZ - radius);
            float ax = Mathf.Max(qx, 0f);
            float az = Mathf.Max(qz, 0f);
            return Mathf.Sqrt(ax * ax + az * az)
                + Mathf.Min(Mathf.Max(qx, qz), 0f)
                - radius;
        }

        private static float SmoothStep01(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * (3f - 2f * t);
        }

        private static float SmootherStep01(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }
    }
}
