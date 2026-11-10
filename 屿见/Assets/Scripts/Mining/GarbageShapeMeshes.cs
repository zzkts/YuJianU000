using System.Collections.Generic;
using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 垃圾聚合体的占位几何。
    ///
    /// 美术资源还没到位时，外壳块与垃圾精灵仍是 Unity 自带的 Cube。
    /// 这里在运行时生成低多边形的不规则块 / 团块，把 Cube 换掉：
    ///   · 外壳块：二十面体随机压扁、拉伸、抖动，得到一块块不规则的垃圾；
    ///   · 垃圾精灵：细分一次的球面加低频起伏，得到一个软趴趴的紫色团块。
    ///
    /// 只在预制体仍然使用 Unity 自带 Cube 时替换。美术换好预制体后，
    /// 网格名不再是 "Cube"，这里会直接跳过，不会覆盖美术资源。
    /// </summary>
    public static class GarbageShapeMeshes
    {
        private const int ChunkVariantCount = 6;
        private const string BuiltInCubeMeshName = "Cube";

        private static Mesh[] chunkMeshes;
        private static Mesh blobMesh;

        /// <summary>把目标物体（及其子物体）上的占位 Cube 换成一块不规则垃圾。</summary>
        /// <param name="target">预制体实例。</param>
        /// <param name="variant">期望使用的形状编号；负数也可以，会取模。</param>
        /// <returns>真正替换了网格时返回 true。</returns>
        public static bool TryApplyChunk(GameObject target, int variant)
        {
            MeshFilter filter = FindPlaceholderFilter(target);

            if (filter == null)
            {
                return false;
            }

            Mesh[] variants = ChunkMeshes;
            int index = ((variant % variants.Length) + variants.Length) % variants.Length;
            filter.sharedMesh = variants[index];
            return true;
        }

        /// <summary>把目标物体（及其子物体）上的占位 Cube 换成垃圾精灵团块。</summary>
        public static bool TryApplyBlob(GameObject target)
        {
            MeshFilter filter = FindPlaceholderFilter(target);

            if (filter == null)
            {
                return false;
            }

            filter.sharedMesh = BlobMesh;
            return true;
        }

        /// <summary>
        /// 找到仍在使用 Unity 自带 Cube 的 MeshFilter。
        /// 美术预制体通常有自己的网格名，因此这里会返回 null，调用方不会覆盖。
        /// </summary>
        private static MeshFilter FindPlaceholderFilter(GameObject target)
        {
            if (target == null)
            {
                return null;
            }

            MeshFilter filter = target.GetComponentInChildren<MeshFilter>(true);

            if (filter == null || filter.sharedMesh == null)
            {
                return null;
            }

            Mesh mesh = filter.sharedMesh;
            bool isBuiltInCube = mesh.name == BuiltInCubeMeshName && mesh.vertexCount == 24;
            return isBuiltInCube ? filter : null;
        }

        private static Mesh[] ChunkMeshes
        {
            get
            {
                // 关闭域重载时静态字段会跨 Play 保留，旧 Mesh 可能已被销毁，这里一并重建。
                if (chunkMeshes == null || chunkMeshes.Length != ChunkVariantCount ||
                    chunkMeshes[0] == null)
                {
                    chunkMeshes = new Mesh[ChunkVariantCount];

                    for (int i = 0; i < chunkMeshes.Length; i++)
                    {
                        chunkMeshes[i] = BuildChunk(i * 977 + 17);
                    }
                }

                return chunkMeshes;
            }
        }

        private static Mesh BlobMesh
        {
            get
            {
                if (blobMesh == null)
                {
                    blobMesh = BuildBlob();
                }

                return blobMesh;
            }
        }

        private static Mesh BuildChunk(int seed)
        {
            Random.State previousState = Random.state;
            Random.InitState(seed);

            Vector3[] baseVertices = IcosahedronVertices();
            Vector3[] shaped = new Vector3[baseVertices.Length];

            // 每个变体有自己的整体比例，避免几块垃圾长得完全一样。
            float scaleX = Random.Range(0.82f, 1.14f);
            float scaleY = Random.Range(0.76f, 1.10f);
            float scaleZ = Random.Range(0.82f, 1.14f);

            for (int i = 0; i < baseVertices.Length; i++)
            {
                Vector3 vertex = baseVertices[i] * Random.Range(0.72f, 1.14f);
                vertex.x *= scaleX;
                vertex.y *= scaleY;
                vertex.z *= scaleZ;
                vertex += new Vector3(
                    Random.Range(-0.045f, 0.045f),
                    Random.Range(-0.045f, 0.045f),
                    Random.Range(-0.045f, 0.045f));
                shaped[i] = vertex;
            }

            int[] faces = IcosahedronTriangles();

            // 逐面拆开顶点，得到硬朗的低多边形棱角。
            Vector3[] vertices = new Vector3[faces.Length];
            int[] triangles = new int[faces.Length];

            for (int i = 0; i < faces.Length; i++)
            {
                vertices[i] = shaped[faces[i]];
                triangles[i] = i;
            }

            Mesh mesh = new Mesh
            {
                name = $"GarbageChunk_{seed}",
            };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            EnsureOutward(mesh);
            mesh.RecalculateBounds();

            Random.state = previousState;
            return mesh;
        }

        private static Mesh BuildBlob()
        {
            List<Vector3> vertices = new List<Vector3>(IcosahedronVertices());
            List<int> triangles = new List<int>(IcosahedronTriangles());
            SubdivideOnce(vertices, triangles);

            // 低频起伏：团块表面有几个软软的鼓包，但不会出现尖刺。
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 normal = vertices[i].normalized;
                float lobes = Mathf.Sin(normal.x * 3.1f + 0.7f) *
                              Mathf.Sin(normal.y * 2.7f + 1.3f) *
                              Mathf.Sin(normal.z * 3.7f + 2.1f);
                float radius = 1f + 0.14f * lobes;
                vertices[i] = normal * radius;
                vertices[i] = new Vector3(vertices[i].x, vertices[i].y * 1.06f, vertices[i].z);
            }

            Mesh mesh = new Mesh
            {
                name = "GarbageSpriteBlob",
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            EnsureOutward(mesh);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>细分一次二十面体：20 面 → 80 面，共享顶点以得到平滑法线。</summary>
        private static void SubdivideOnce(List<Vector3> vertices, List<int> triangles)
        {
            Dictionary<long, int> midpoints = new Dictionary<long, int>();
            List<int> result = new List<int>(triangles.Count * 4);

            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                int ab = GetMidpoint(a, b, vertices, midpoints);
                int bc = GetMidpoint(b, c, vertices, midpoints);
                int ca = GetMidpoint(c, a, vertices, midpoints);

                result.Add(a);
                result.Add(ab);
                result.Add(ca);

                result.Add(b);
                result.Add(bc);
                result.Add(ab);

                result.Add(c);
                result.Add(ca);
                result.Add(bc);

                result.Add(ab);
                result.Add(bc);
                result.Add(ca);
            }

            triangles.Clear();
            triangles.AddRange(result);
        }

        private static int GetMidpoint(int a, int b, List<Vector3> vertices,
            Dictionary<long, int> midpoints)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

            if (midpoints.TryGetValue(key, out int existing))
            {
                return existing;
            }

            int index = vertices.Count;
            vertices.Add(((vertices[a] + vertices[b]) * 0.5f).normalized);
            midpoints.Add(key, index);
            return index;
        }

        /// <summary>
        /// Unity 以顺时针为正面。这里按「法线是否朝外」统一修正一次绕序，
        /// 避免网格从外面看是透明的。
        /// </summary>
        private static void EnsureOutward(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            float score = 0f;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                score += Vector3.Dot(normal, (a + b + c) / 3f);
            }

            if (score < 0f)
            {
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int swap = triangles[i + 1];
                    triangles[i + 1] = triangles[i + 2];
                    triangles[i + 2] = swap;
                }

                mesh.triangles = triangles;
            }

            mesh.RecalculateNormals();
        }

        private static Vector3[] IcosahedronVertices()
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;

            Vector3[] vertices =
            {
                new Vector3(-1f, t, 0f),
                new Vector3(1f, t, 0f),
                new Vector3(-1f, -t, 0f),
                new Vector3(1f, -t, 0f),
                new Vector3(0f, -1f, t),
                new Vector3(0f, 1f, t),
                new Vector3(0f, -1f, -t),
                new Vector3(0f, 1f, -t),
                new Vector3(t, 0f, -1f),
                new Vector3(t, 0f, 1f),
                new Vector3(-t, 0f, -1f),
                new Vector3(-t, 0f, 1f),
            };

            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = vertices[i].normalized;
            }

            return vertices;
        }

        private static int[] IcosahedronTriangles()
        {
            return new[]
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
        }
    }
}
