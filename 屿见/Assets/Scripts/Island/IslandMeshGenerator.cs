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

        [Tooltip("从台地边缘到水面的沙滩宽度。")]
        public float beachWidth = 9f;

        [Tooltip("从台地边缘到海床截断处的总宽度，必须大于沙滩宽度。")]
        public float coastWidth = 18f;

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
        public const int SeabedSubmesh = 3;
        public const int SubmeshCount = 4;

        /// <summary>
        /// 生成海岛网格。返回的 Mesh 有 4 个子网格（草/沙/岩/海床），
        /// 顶点不共享，法线为逐面法线。
        /// </summary>
        public static Mesh Generate(IslandSettings settings)
        {
            if (settings == null)
            {
                throw new System.ArgumentNullException(nameof(settings));
            }

            float gridSize = Mathf.Max(0.1f, settings.gridSize);
            float coastWidth = Mathf.Max(settings.beachWidth + 0.01f, settings.coastWidth);

            // 采样范围要盖住“台地 + 海岸带 + 噪声扰动”，否则海岸线会被硬切。
            float extentX = settings.halfSizeX + coastWidth
                + settings.coastNoiseLarge + settings.coastNoiseSmall + 2f;
            float extentZ = settings.halfSizeZ + coastWidth
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

                    AddTriangle(settings, coastWidth, p00, p01, p11, d00, d01, d11,
                        vertices, normals, uvs, indices);
                    AddTriangle(settings, coastWidth, p00, p11, p10, d00, d11, d10,
                        vertices, normals, uvs, indices);
                }
            }

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
            float height = HeightFromDistance(settings, distance);

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

        private static float HeightFromDistance(IslandSettings settings, float distance)
        {
            if (distance <= 0f)
            {
                return 0f;
            }

            if (distance < settings.beachWidth)
            {
                return Mathf.Lerp(0f, settings.waterLevel,
                    SmootherStep01(distance / settings.beachWidth));
            }

            if (distance < settings.coastWidth)
            {
                float t = (distance - settings.beachWidth)
                    / (settings.coastWidth - settings.beachWidth);
                return Mathf.Lerp(settings.waterLevel, settings.seabedDepth, SmoothStep01(t));
            }

            return settings.seabedDepth;
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
            int submesh = Classify(settings, averageHeight, averageDistance, normal);

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
            Vector3 normal)
        {
            // 台地（含噪声淡入前的肩部）恒为草地，保证可建区看起来是整片平地。
            if (averageDistance <= 2f)
            {
                return GrassSubmesh;
            }

            if (averageHeight < settings.waterLevel + 0.15f)
            {
                return SeabedSubmesh;
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
