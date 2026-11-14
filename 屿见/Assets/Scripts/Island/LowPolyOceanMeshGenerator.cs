using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Yujian.Island
{
    public enum LowPolyOceanTopology
    {
        Radial = 0,
        RefinedGrid = 1,
        Delaunay = 2,
        ShaderSmooth = 3,
    }

    /// <summary>
    /// 低模海面网格参数。只描述拓扑，不涉及材质；
    /// 起伏、分面明暗等外观参数在 Shader / 材质上。
    /// </summary>
    [Serializable]
    public struct LowPolyOceanMeshSettings
    {
        [Tooltip("海岸附近使用的拓扑方案。")]
        public LowPolyOceanTopology topology;

        [Tooltip("海面覆盖边长（世界单位），以海岛中心为中心。")]
        public float size;

        [Tooltip("粗网格三角格边长（世界单位）。")]
        public float cellSize;

        [Tooltip("内部顶点抖动比例（相对格边长，0 = 规则网格）。")]
        public float jitter;

        [Tooltip("抖动随机种子，保证同参数生成结果一致。")]
        public int seed;

        [Tooltip("顶点起伏高度，只用于计算网格包围盒留白。")]
        public float waveHeight;

        [Tooltip("海岸遮罩的浪高淡出深度（世界单位）：水深小于它时浪高逐渐衰减到 0。")]
        public float shoreFadeDepth;

        [Tooltip("海面物体位置相对海岛中心的偏移；编辑器应用时自动计算。")]
        public Vector2 islandOffset;

        [Tooltip("方案 A：环向分段数。越多海岸线越细。")]
        public int radialSegments;

        [Tooltip("方案 A：岸边第一圈的环距（世界单位）。")]
        public float shoreRingSpacing;

        [Tooltip("方案 A：水面边界向陆地内缩的距离，避免和沙滩出现缝隙。")]
        public float shoreInset;

        [Tooltip("方案 B/C：海岸细网格边长。")]
        public float refineCellSize;

        [Tooltip("方案 B/C：海岸细网格覆盖半径。")]
        public float refineHalfSize;

        [Tooltip("方案 B/C：离开细网格区后，粗格边长每圈放大的倍率。")]
        public float coarseGrowth;

        [Tooltip("方案 C：Delaunay 散点的目标边长。")]
        public float delaunayCellSize;

        [Tooltip("方案 C：远海散点的最大边长，越大点数越少、Delaunay 越快。")]
        public float delaunayCoarseCellSize;

        public static LowPolyOceanMeshSettings Default
        {
            get
            {
                return new LowPolyOceanMeshSettings
                {
                    // 默认用整块全水面 + Shader 遮罩，不在岛中心挖洞。
                    topology = LowPolyOceanTopology.ShaderSmooth,
                    // 与原海面 Plane（200 × 10）一致，覆盖半径 1000。
                    size = 2000f,
                    cellSize = 10f,
                    jitter = 0.32f,
                    seed = 20261114,
                    waveHeight = 2f,
                    shoreFadeDepth = LowPolyOceanShoreMask.DefaultFadeDepth,
                    islandOffset = Vector2.zero,
                    radialSegments = 256,
                    shoreRingSpacing = 2.5f,
                    shoreInset = 1.5f,
                    refineCellSize = 2.5f,
                    refineHalfSize = 110f,
                    coarseGrowth = 1.28f,
                    // Delaunay 是 O(n²)，默认点集控制在 4k 左右，避免编辑器卡太久。
                    delaunayCellSize = 5f,
                    delaunayCoarseCellSize = 100f,
                };
            }
        }
    }

    /// <summary>
    /// 生成低模海面三角网格，支持四种海岸拓扑：
    /// A 径向海岸网格 / B 矩形网格局部细分 / C Delaunay 不规则三角化 / D 仅 Shader 柔化。
    ///
    /// 关键约定：每个三角形使用 3 个独立顶点（不共享），
    /// 这样每个面可以有独立的面随机值，Shader 再用屏幕导数得到面法线。
    /// 顶点位置相同、位移又是世界 XZ 的函数，所以拆点不会产生裂缝。
    ///
    /// UV2（C# 的 mesh.uv2，Shader 的 TEXCOORD1）保存重心坐标：
    /// 第 1 个顶点 (1,0)、第 2 个 (0,1)、第 3 个 (0,0)，z 由 Shader 补；
    /// 顶点色 R 通道保存该三角形的面随机值，用于每面色差。
    /// </summary>
    public static class LowPolyOceanMeshGenerator
    {
        public const int MaxTriangles = 1000000;

        /// <summary>
        /// 均匀矩形网格的三角形数量。
        /// </summary>
        public static long CountTriangles(float size, float cellSize)
        {
            long columns = ResolveColumns(size, cellSize);
            return columns * columns * 2L;
        }

        /// <summary>
        /// 当前方案预计的三角形数量。Delaunay 是估算值（约 2 × 点数）。
        /// </summary>
        public static long CountTriangles(LowPolyOceanMeshSettings settings)
        {
            Sanitize(ref settings);
            switch (settings.topology)
            {
                case LowPolyOceanTopology.Radial:
                {
                    IslandSettings island = new IslandSettings();
                    float inner = Mathf.Max(1f,
                        LowPolyOceanShoreMask.ComputeHalfExtent(island) - settings.shoreInset);
                    float outer = settings.size * 0.5f;
                    int rings = CountRingOffsets(inner, outer,
                        settings.shoreRingSpacing, 1.28f);
                    return (long)settings.radialSegments * Mathf.Max(0, rings - 1) * 2L;
                }
                case LowPolyOceanTopology.RefinedGrid:
                {
                    List<float> offsets = BuildGradedOffsets(
                        settings.size * 0.5f, settings.refineHalfSize,
                        settings.refineCellSize, settings.cellSize, settings.coarseGrowth);
                    long cells = (long)(offsets.Count - 1) * (offsets.Count - 1);
                    return cells * 2L;
                }
                case LowPolyOceanTopology.Delaunay:
                {
                    List<float> offsets = BuildDelaunayOffsets(settings);
                    return (long)offsets.Count * offsets.Count * 2L;
                }
                default:
                    return CountTriangles(settings.size, settings.cellSize);
            }
        }

        /// <summary>
        /// 生成一份新的网格（不落盘）。调用方负责销毁。
        /// </summary>
        public static Mesh Generate(LowPolyOceanMeshSettings settings)
        {
            return Generate(settings, new IslandSettings());
        }

        public static Mesh Generate(LowPolyOceanMeshSettings settings, IslandSettings island)
        {
            Mesh mesh = new Mesh { name = "OceanLowPoly" };
            Fill(mesh, settings, island);
            return mesh;
        }

        /// <summary>
        /// 就地重建网格内容，保留 Mesh 资产引用不变。
        /// </summary>
        public static void Fill(Mesh mesh, LowPolyOceanMeshSettings settings)
        {
            Fill(mesh, settings, new IslandSettings());
        }

        public static void Fill(
            Mesh mesh, LowPolyOceanMeshSettings settings, IslandSettings island)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            Sanitize(ref settings);
            if (island == null)
            {
                island = new IslandSettings();
            }

            switch (settings.topology)
            {
                case LowPolyOceanTopology.Radial:
                    BuildRadial(mesh, settings, island);
                    break;
                case LowPolyOceanTopology.RefinedGrid:
                    BuildRefinedGrid(mesh, settings);
                    break;
                case LowPolyOceanTopology.Delaunay:
                    BuildDelaunay(mesh, settings);
                    break;
                default:
                    BuildUniformGrid(mesh, settings);
                    break;
            }
        }

        // ------------------------------------------------------------------
        // 方案 D：均匀矩形网格（原实现）
        // ------------------------------------------------------------------

        private static void BuildUniformGrid(Mesh mesh, LowPolyOceanMeshSettings settings)
        {
            int columns = ResolveColumns(settings.size, settings.cellSize);
            int rows = columns;
            long triangleCountLong = (long)columns * rows * 2L;
            if (triangleCountLong > MaxTriangles)
            {
                throw new ArgumentException(
                    "三角面数量 " + triangleCountLong + " 超过上限 " + MaxTriangles
                    + "，请增大三角面大小或减小覆盖范围。");
            }

            int triangleCount = (int)triangleCountLong;
            Vector2 center = -settings.islandOffset;
            float cellX = settings.size / columns;
            float cellZ = settings.size / rows;
            int gridWidth = columns + 1;

            Vector3[] grid = new Vector3[gridWidth * (rows + 1)];
            for (int j = 0; j <= rows; j++)
            {
                for (int i = 0; i <= columns; i++)
                {
                    float x = center.x - settings.size * 0.5f + i * cellX;
                    float z = center.y - settings.size * 0.5f + j * cellZ;

                    // 只抖动内部顶点，边界保持直线，海面外缘不会出现缺口。
                    if (i > 0 && i < columns && j > 0 && j < rows)
                    {
                        x += (Hash01(i, j, settings.seed) - 0.5f)
                            * 2f * settings.jitter * cellX;
                        z += (Hash01(i, j, settings.seed + 7919) - 0.5f)
                            * 2f * settings.jitter * cellZ;
                    }

                    grid[j * gridWidth + i] = new Vector3(x, 0f, z);
                }
            }

            int vertexCount = triangleCount * 3;
            Vector3[] positions = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Color[] colors = new Color[vertexCount];
            Vector2[] uv = new Vector2[vertexCount];
            Vector2[] uv2 = new Vector2[vertexCount];
            int[] indices = new int[vertexCount];

            int write = 0;
            for (int j = 0; j < rows; j++)
            {
                for (int i = 0; i < columns; i++)
                {
                    int v00 = j * gridWidth + i;
                    int v10 = v00 + 1;
                    int v01 = v00 + gridWidth;
                    int v11 = v01 + 1;

                    // 随机对角线，避免整片海面出现同一方向的长条纹理。
                    bool flip = Hash01(i, j, settings.seed + 3571) > 0.5f;
                    if (flip)
                    {
                        write = AddTriangle(grid, positions, normals, colors, uv, uv2, indices,
                            write, v00, v10, v11, settings.size, settings.seed);
                        write = AddTriangle(grid, positions, normals, colors, uv, uv2, indices,
                            write, v00, v11, v01, settings.size, settings.seed);
                    }
                    else
                    {
                        write = AddTriangle(grid, positions, normals, colors, uv, uv2, indices,
                            write, v00, v10, v01, settings.size, settings.seed);
                        write = AddTriangle(grid, positions, normals, colors, uv, uv2, indices,
                            write, v10, v11, v01, settings.size, settings.seed);
                    }
                }
            }

            mesh.Clear();
            mesh.indexFormat = vertexCount > 65535
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, uv2);
            mesh.SetTriangles(indices, 0, false);
            mesh.bounds = BuildBounds(settings, center);
        }

        // ------------------------------------------------------------------
        // 方案 B：矩形网格 + 海岸局部细分 + 外圈渐变
        // ------------------------------------------------------------------

        private static void BuildRefinedGrid(Mesh mesh, LowPolyOceanMeshSettings settings)
        {
            float halfSize = settings.size * 0.5f;
            Vector2 center = -settings.islandOffset;

            List<float> xs = BuildGradedOffsets(
                halfSize, settings.refineHalfSize,
                settings.refineCellSize, settings.cellSize, settings.coarseGrowth);
            List<float> zs = BuildGradedOffsets(
                halfSize, settings.refineHalfSize,
                settings.refineCellSize, settings.cellSize, settings.coarseGrowth);

            int nx = xs.Count;
            int nz = zs.Count;
            if ((long)(nx - 1) * (nz - 1) * 2L > MaxTriangles)
            {
                throw new ArgumentException("方案 B 的三角形数量超过上限，请增大网格边长。");
            }

            Vector3[] grid = new Vector3[nx * nz];
            for (int j = 0; j < nz; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    float x = center.x + xs[i];
                    float z = center.y + zs[j];
                    bool boundary = i == 0 || i == nx - 1 || j == 0 || j == nz - 1;
                    if (!boundary)
                    {
                        float localCellX = (xs[Mathf.Min(i + 1, nx - 1)]
                            - xs[Mathf.Max(i - 1, 0)]) * 0.5f;
                        float localCellZ = (zs[Mathf.Min(j + 1, nz - 1)]
                            - zs[Mathf.Max(j - 1, 0)]) * 0.5f;
                        x += (Hash01(i, j, settings.seed) - 0.5f)
                            * 2f * settings.jitter * localCellX;
                        z += (Hash01(i, j, settings.seed + 7919) - 0.5f)
                            * 2f * settings.jitter * localCellZ;
                    }

                    grid[j * nx + i] = new Vector3(x, 0f, z);
                }
            }

            MeshBuilder builder = new MeshBuilder(settings.size, settings.seed,
                (nx - 1) * (nz - 1) * 2);
            for (int j = 0; j < nz - 1; j++)
            {
                for (int i = 0; i < nx - 1; i++)
                {
                    int v00 = j * nx + i;
                    int v10 = v00 + 1;
                    int v01 = v00 + nx;
                    int v11 = v01 + 1;

                    bool flip = Hash01(i, j, settings.seed + 3571) > 0.5f;
                    if (flip)
                    {
                        builder.AddTriangle(grid[v00], grid[v10], grid[v11]);
                        builder.AddTriangle(grid[v00], grid[v11], grid[v01]);
                    }
                    else
                    {
                        builder.AddTriangle(grid[v00], grid[v10], grid[v01]);
                        builder.AddTriangle(grid[v10], grid[v11], grid[v01]);
                    }
                }
            }

            builder.Build(mesh, BuildBounds(settings, center));
        }

        // ------------------------------------------------------------------
        // 方案 A：以海岛为中心的径向海岸网格
        // ------------------------------------------------------------------

        private static void BuildRadial(
            Mesh mesh, LowPolyOceanMeshSettings settings, IslandSettings island)
        {
            Vector2 center = -settings.islandOffset;
            int segments = settings.radialSegments;
            float outerRadius = settings.size * 0.5f;

            float[] rawCoast = new float[segments];
            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                rawCoast[i] = RaycastCoastRadius(island, angle);
            }

            // 轻微圆滑，避免水线因为高度噪声出现单点尖刺。
            float[] coast = new float[segments];
            for (int i = 0; i < segments; i++)
            {
                int prev = (i - 1 + segments) % segments;
                int next = (i + 1) % segments;
                coast[i] = Mathf.Max(1f,
                    (rawCoast[prev] + rawCoast[i] * 2f + rawCoast[next]) * 0.25f
                    - settings.shoreInset);
            }

            float minInner = outerRadius;
            for (int i = 0; i < segments; i++)
            {
                minInner = Mathf.Min(minInner, coast[i]);
            }

            float offsetMax = Mathf.Max(1f, outerRadius - minInner);
            List<float> offsets = BuildRingOffsets(offsetMax, settings.shoreRingSpacing, 1.28f);
            if ((long)segments * Mathf.Max(0, offsets.Count - 1) * 2L > MaxTriangles)
            {
                throw new ArgumentException("方案 A 的三角形数量超过上限，请减少环向分段。");
            }

            float angularStep = Mathf.PI * 2f / segments;
            MeshBuilder builder = new MeshBuilder(settings.size, settings.seed,
                segments * offsets.Count * 2);

            Vector3[][] rings = new Vector3[offsets.Count][];
            for (int ring = 0; ring < offsets.Count; ring++)
            {
                float t = offsets[ring] / offsetMax;
                bool boundaryRing = ring == offsets.Count - 1;
                rings[ring] = new Vector3[segments];

                for (int i = 0; i < segments; i++)
                {
                    float baseAngle = i * angularStep;
                    float radius = Mathf.Lerp(
                        coast[i] + offsets[ring], outerRadius, t);

                    if (ring > 0 && !boundaryRing)
                    {
                        float angleJitter = (Hash01(i, ring, settings.seed) - 0.5f)
                            * 2f * settings.jitter * angularStep * 0.5f;
                        float radiusJitter = (Hash01(i, ring, settings.seed + 7919) - 0.5f)
                            * 2f * settings.jitter * settings.shoreRingSpacing;
                        baseAngle += angleJitter;
                        radius += radiusJitter;
                    }

                    rings[ring][i] = new Vector3(
                        center.x + Mathf.Cos(baseAngle) * radius,
                        0f,
                        center.y + Mathf.Sin(baseAngle) * radius);
                }
            }

            for (int ring = 0; ring < offsets.Count - 1; ring++)
            {
                for (int i = 0; i < segments; i++)
                {
                    int next = (i + 1) % segments;
                    Vector3 a = rings[ring][i];
                    Vector3 b = rings[ring][next];
                    Vector3 c = rings[ring + 1][i];
                    Vector3 d = rings[ring + 1][next];

                    bool flip = Hash01(i, ring, settings.seed + 3571) > 0.5f;
                    if (flip)
                    {
                        builder.AddTriangle(a, b, d);
                        builder.AddTriangle(a, d, c);
                    }
                    else
                    {
                        builder.AddTriangle(a, b, c);
                        builder.AddTriangle(b, d, c);
                    }
                }
            }

            builder.Build(mesh, BuildBounds(settings, center));
        }

        /// <summary>
        /// 从岛中心向一个方向由外向内找最外层水线；返回海岛局部坐标下的半径。
        /// </summary>
        private static float RaycastCoastRadius(IslandSettings island, float angle)
        {
            float dirX = Mathf.Cos(angle);
            float dirZ = Mathf.Sin(angle);
            float start = Mathf.Max(20f,
                LowPolyOceanShoreMask.ComputeHalfExtent(island) * 1.25f);
            float step = 0.5f;
            float radius = start;

            while (radius > 0.5f)
            {
                float height = IslandMeshGenerator.SampleHeight(
                    island, dirX * radius, dirZ * radius);
                if (height >= island.waterLevel)
                {
                    return radius;
                }

                radius -= step;
            }

            return 10f;
        }

        // ------------------------------------------------------------------
        // 方案 C：Delaunay 不规则三角化
        // ------------------------------------------------------------------

        private static void BuildDelaunay(Mesh mesh, LowPolyOceanMeshSettings settings)
        {
            float halfSize = settings.size * 0.5f;
            Vector2 center = -settings.islandOffset;

            List<float> xs = BuildDelaunayOffsets(settings);
            List<float> zs = BuildDelaunayOffsets(settings);

            List<Vector2> points = new List<Vector2>(xs.Count * zs.Count);
            HashSet<long> seen = new HashSet<long>();
            for (int j = 0; j < zs.Count; j++)
            {
                for (int i = 0; i < xs.Count; i++)
                {
                    float x = center.x + xs[i];
                    float z = center.y + zs[j];
                    bool boundary = i == 0 || i == xs.Count - 1
                        || j == 0 || j == zs.Count - 1;
                    if (!boundary)
                    {
                        float jitter = settings.delaunayCellSize * 0.35f;
                        x += (Hash01(i, j, settings.seed) - 0.5f) * 2f * jitter;
                        z += (Hash01(i, j, settings.seed + 7919) - 0.5f) * 2f * jitter;
                    }

                    long key = PackPointKey(x, z);
                    if (seen.Add(key))
                    {
                        points.Add(new Vector2(x, z));
                    }
                }
            }

            List<int> triangles = LowPolyOceanDelaunay.Triangulate(points);
            if (triangles.Count / 3 > MaxTriangles)
            {
                throw new ArgumentException("方案 C 的三角形数量超过上限，请增大散点边长。");
            }

            MeshBuilder builder = new MeshBuilder(settings.size, settings.seed,
                triangles.Count / 3);
            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                Vector2 a = points[triangles[i]];
                Vector2 b = points[triangles[i + 1]];
                Vector2 c = points[triangles[i + 2]];
                if (Mathf.Abs((b.x - a.x) * (c.y - a.y)
                    - (b.y - a.y) * (c.x - a.x)) < 0.0001f)
                {
                    continue;
                }

                builder.AddTriangle(
                    new Vector3(a.x, 0f, a.y),
                    new Vector3(b.x, 0f, b.y),
                    new Vector3(c.x, 0f, c.y));
            }

            builder.Build(mesh, BuildBounds(settings, center));
        }

        // ------------------------------------------------------------------
        // 公共辅助
        // ------------------------------------------------------------------

        private static List<float> BuildDelaunayOffsets(LowPolyOceanMeshSettings settings)
        {
            return BuildGradedOffsets(
                settings.size * 0.5f,
                settings.refineHalfSize,
                settings.delaunayCellSize,
                settings.delaunayCoarseCellSize,
                settings.coarseGrowth);
        }

        private static List<float> BuildGradedOffsets(
            float halfSize, float fineHalfSize, float fineStep,
            float coarseStep, float growth)
        {
            halfSize = Mathf.Max(0.1f, halfSize);
            fineHalfSize = Mathf.Clamp(fineHalfSize, 0f, halfSize);
            fineStep = Mathf.Max(0.1f, fineStep);
            coarseStep = Mathf.Max(fineStep, coarseStep);
            growth = Mathf.Max(1.02f, growth);

            List<float> positive = new List<float>();
            float x = 0f;
            float step = fineStep;
            while (x < halfSize - 0.0001f && positive.Count < 8192)
            {
                if (x >= fineHalfSize)
                {
                    step = Mathf.Min(step * growth, coarseStep);
                }

                x += step;
                if (x > halfSize)
                {
                    x = halfSize;
                }

                positive.Add(x);
            }

            List<float> offsets = new List<float>(positive.Count * 2 + 1);
            for (int i = positive.Count - 1; i >= 0; i--)
            {
                offsets.Add(-positive[i]);
            }
            offsets.Add(0f);
            offsets.AddRange(positive);
            return offsets;
        }

        private static int CountRingOffsets(
            float innerRadius, float outerRadius, float firstStep, float growth)
        {
            float maxOffset = Mathf.Max(1f, outerRadius - innerRadius);
            List<float> offsets = BuildRingOffsets(maxOffset, firstStep, growth);
            return offsets.Count;
        }

        private static List<float> BuildRingOffsets(
            float maxOffset, float firstStep, float growth)
        {
            float step = Mathf.Max(0.5f, firstStep);
            growth = Mathf.Max(1.02f, growth);
            List<float> offsets = new List<float> { 0f };
            float offset = 0f;
            while (offset < maxOffset && offsets.Count < 512)
            {
                offset += step;
                if (offset > maxOffset)
                {
                    offset = maxOffset;
                }

                offsets.Add(offset);
                step = Mathf.Min(step * growth, 120f);
            }

            return offsets;
        }

        private static Bounds BuildBounds(
            LowPolyOceanMeshSettings settings, Vector2 center)
        {
            float halfY = Mathf.Max(settings.waveHeight + 2f, 2f);
            float halfXZ = settings.size * 0.5f + 8f;
            return new Bounds(
                new Vector3(center.x, 0f, center.y),
                new Vector3(halfXZ * 2f, halfY * 2f, halfXZ * 2f));
        }

        private static long PackPointKey(float x, float z)
        {
            long ix = Mathf.RoundToInt(x * 1000f);
            long iz = Mathf.RoundToInt(z * 1000f);
            return (ix << 32) ^ (iz & 0xffffffffL);
        }

        private static int ResolveColumns(float size, float cellSize)
        {
            float safeSize = Mathf.Max(1f, size);
            float safeCell = Mathf.Clamp(cellSize, 0.1f, safeSize);
            return Mathf.Max(1, Mathf.CeilToInt(safeSize / safeCell));
        }

        private static void Sanitize(ref LowPolyOceanMeshSettings settings)
        {
            settings.size = Mathf.Clamp(settings.size, 100f, 6000f);
            settings.cellSize = Mathf.Clamp(settings.cellSize, 0.5f, settings.size);
            settings.jitter = Mathf.Clamp(settings.jitter, 0f, 0.45f);
            settings.waveHeight = Mathf.Max(0f, settings.waveHeight);
            settings.shoreFadeDepth = settings.shoreFadeDepth <= 0f
                ? LowPolyOceanShoreMask.DefaultFadeDepth
                : Mathf.Clamp(settings.shoreFadeDepth, 0.01f, 50f);
            settings.radialSegments = Mathf.Clamp(settings.radialSegments, 32, 1024);
            settings.shoreRingSpacing = Mathf.Clamp(settings.shoreRingSpacing, 0.5f, 20f);
            settings.shoreInset = Mathf.Clamp(settings.shoreInset, 0f, 10f);
            settings.refineCellSize = Mathf.Clamp(settings.refineCellSize, 0.5f, 20f);
            settings.refineHalfSize = Mathf.Clamp(
                settings.refineHalfSize, 10f, settings.size * 0.5f);
            settings.coarseGrowth = Mathf.Clamp(settings.coarseGrowth, 1.02f, 2f);
            settings.delaunayCellSize = Mathf.Clamp(settings.delaunayCellSize, 1f, 20f);
            settings.delaunayCoarseCellSize = Mathf.Clamp(
                settings.delaunayCoarseCellSize, settings.delaunayCellSize, 200f);
            if (settings.topology < LowPolyOceanTopology.Radial
                || settings.topology > LowPolyOceanTopology.ShaderSmooth)
            {
                settings.topology = LowPolyOceanTopology.Radial;
            }
        }

        private static int AddTriangle(
            Vector3[] grid,
            Vector3[] positions,
            Vector3[] normals,
            Color[] colors,
            Vector2[] uv,
            Vector2[] uv2,
            int[] indices,
            int write,
            int indexA,
            int indexB,
            int indexC,
            float size,
            int seed)
        {
            // 面随机值：同一三角形的 3 个顶点相同，Shader 用它做每面色差。
            float faceRandom = Hash01(write / 3, 3571, seed);
            Color faceColor = new Color(faceRandom, faceRandom, faceRandom, 1f);

            AddVertex(grid[indexA], 1f, 0f, faceColor, positions, normals, colors, uv, uv2,
                indices, write, size);
            AddVertex(grid[indexB], 0f, 1f, faceColor, positions, normals, colors, uv, uv2,
                indices, write + 1, size);
            AddVertex(grid[indexC], 0f, 0f, faceColor, positions, normals, colors, uv, uv2,
                indices, write + 2, size);

            return write + 3;
        }

        private static void AddVertex(
            Vector3 position,
            float baryX,
            float baryY,
            Color faceColor,
            Vector3[] positions,
            Vector3[] normals,
            Color[] colors,
            Vector2[] uv,
            Vector2[] uv2,
            int[] indices,
            int write,
            float size)
        {
            positions[write] = position;
            normals[write] = Vector3.up;
            colors[write] = faceColor;
            uv[write] = new Vector2(
                position.x / Mathf.Max(size, 0.001f) + 0.5f,
                position.z / Mathf.Max(size, 0.001f) + 0.5f);
            uv2[write] = new Vector2(baryX, baryY);
            indices[write] = write;
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

        /// <summary>
        /// 所有方案共用的网格写入器：每面拆 3 个顶点，写重心坐标和面随机色。
        /// </summary>
        private sealed class MeshBuilder
        {
            private readonly List<Vector3> positions;
            private readonly List<Vector3> normals;
            private readonly List<Color> colors;
            private readonly List<Vector2> uv;
            private readonly List<Vector2> uv2;
            private readonly List<int> indices;
            private readonly float size;
            private readonly int seed;

            public MeshBuilder(float size, int seed, int triangleCapacity)
            {
                this.size = size;
                this.seed = seed;
                int vertexCapacity = Mathf.Max(0, triangleCapacity) * 3;
                positions = new List<Vector3>(vertexCapacity);
                normals = new List<Vector3>(vertexCapacity);
                colors = new List<Color>(vertexCapacity);
                uv = new List<Vector2>(vertexCapacity);
                uv2 = new List<Vector2>(vertexCapacity);
                indices = new List<int>(vertexCapacity);
            }

            public void AddTriangle(Vector3 a, Vector3 b, Vector3 c)
            {
                float faceRandom = Hash01(indices.Count / 3, 3571, seed);
                Color faceColor = new Color(faceRandom, faceRandom, faceRandom, 1f);
                AddVertex(a, 1f, 0f, faceColor);
                AddVertex(b, 0f, 1f, faceColor);
                AddVertex(c, 0f, 0f, faceColor);
            }

            public void Build(Mesh mesh, Bounds bounds)
            {
                int vertexCount = positions.Count;
                mesh.Clear();
                mesh.indexFormat = vertexCount > 65535
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16;
                mesh.SetVertices(positions);
                mesh.SetNormals(normals);
                mesh.SetColors(colors);
                mesh.SetUVs(0, uv);
                mesh.SetUVs(1, uv2);
                mesh.SetTriangles(indices, 0, false);
                mesh.bounds = bounds;
            }

            private void AddVertex(Vector3 position, float baryX, float baryY, Color color)
            {
                positions.Add(position);
                normals.Add(Vector3.up);
                colors.Add(color);
                uv.Add(new Vector2(
                    position.x / Mathf.Max(size, 0.001f) + 0.5f,
                    position.z / Mathf.Max(size, 0.001f) + 0.5f));
                uv2.Add(new Vector2(baryX, baryY));
                indices.Add(indices.Count);
            }
        }
    }
}
