using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yujian.Island;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 从指定地形根物体下的 Collider 向下投射，烘焙一张俯视高度/水深遮罩。
    /// 不依赖 IslandSettings，也不依赖海岛形状；换一个带 Collider 的地形也能复用。
    /// </summary>
    public static class ColliderShoreMaskBaker
    {
        public const int DefaultResolution = 256;
        public const float DefaultMargin = 20f;

        public static GameObject FindTerrainRoot()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return null;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root != null && root.GetComponent<IslandDefinition>() != null)
                {
                    return root;
                }
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root != null && root.name == "Island")
                {
                    return root;
                }
            }

            return null;
        }

        public static List<Collider> CollectColliders(GameObject terrainRoot)
        {
            List<Collider> colliders = new List<Collider>();
            if (terrainRoot == null)
            {
                return colliders;
            }

            foreach (Collider collider in terrainRoot.GetComponentsInChildren<Collider>(true))
            {
                if (collider != null && collider.enabled && !collider.isTrigger)
                {
                    colliders.Add(collider);
                }
            }

            return colliders;
        }

        /// <summary>
        /// 地形根物体没有 Collider 时，给它补一个非 Convex 的 MeshCollider。
        /// 这样不用重新生成海岛，也能立刻用来烘焙海面遮罩。
        /// </summary>
        public static void EnsureTerrainCollider(GameObject terrainRoot, bool recordUndo)
        {
            if (terrainRoot == null)
            {
                return;
            }

            // 已经有 MeshCollider 就不重复加；Box/Sphere 等 Collider 不算地形高度。
            if (terrainRoot.GetComponentInChildren<MeshCollider>(true) != null)
            {
                return;
            }

            MeshFilter filter = terrainRoot.GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = terrainRoot.GetComponentInChildren<MeshFilter>(true);
            }
            if (filter == null || filter.sharedMesh == null)
            {
                return;
            }

            GameObject target = filter.gameObject;
            MeshCollider collider = recordUndo
                ? Undo.AddComponent<MeshCollider>(target)
                : target.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = false;
            collider.enabled = true;
            Physics.SyncTransforms();
            EditorUtility.SetDirty(collider);
        }

        public static Bounds ComputeBounds(List<Collider> colliders)
        {
            if (colliders == null || colliders.Count == 0)
            {
                return new Bounds(Vector3.zero, Vector3.zero);
            }

            Bounds bounds = colliders[0].bounds;
            for (int i = 1; i < colliders.Count; i++)
            {
                bounds.Encapsulate(colliders[i].bounds);
            }

            return bounds;
        }

        public static float[] SampleHeights(
            List<Collider> colliders,
            int resolution,
            float waterLevel,
            float margin,
            out Vector2 center,
            out float size)
        {
            resolution = Mathf.Clamp(resolution, 16, 1024);
            if (colliders == null || colliders.Count == 0)
            {
                center = new Vector2(0f, 0f);
                size = 100f;
                float[] empty = new float[resolution * resolution];
                for (int i = 0; i < empty.Length; i++)
                {
                    empty[i] = float.NegativeInfinity;
                }
                return empty;
            }

            Bounds bounds = ComputeBounds(colliders);
            float halfSize = Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f
                + Mathf.Max(0f, margin);
            center = new Vector2(bounds.center.x, bounds.center.z);
            size = Mathf.Max(1f, halfSize * 2f);

            float startY = Mathf.Max(bounds.max.y, waterLevel) + 10f;
            float endY = Mathf.Min(bounds.min.y, waterLevel) - 10f;
            float distance = Mathf.Max(1f, startY - endY);

            HashSet<Collider> allowed = new HashSet<Collider>(colliders);
            int layerMask = 0;
            for (int i = 0; i < colliders.Count; i++)
            {
                layerMask |= 1 << colliders[i].gameObject.layer;
            }

            Physics.SyncTransforms();
            RaycastHit[] hits = new RaycastHit[16];
            float[] heights = new float[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                float worldZ = center.y + ((y + 0.5f) / resolution - 0.5f) * size;
                for (int x = 0; x < resolution; x++)
                {
                    float worldX = center.x + ((x + 0.5f) / resolution - 0.5f) * size;
                    int hitCount = Physics.RaycastNonAlloc(
                        new Vector3(worldX, startY, worldZ),
                        Vector3.down,
                        hits,
                        distance,
                        layerMask,
                        QueryTriggerInteraction.Ignore);

                    float bestHeight = float.NegativeInfinity;
                    for (int i = 0; i < hitCount; i++)
                    {
                        if (allowed.Contains(hits[i].collider)
                            && hits[i].point.y > bestHeight)
                        {
                            bestHeight = hits[i].point.y;
                        }
                    }

                    heights[y * resolution + x] = bestHeight;
                }
            }

            return heights;
        }

        public static Texture2D BakeMaskAsset(
            List<Collider> colliders,
            float waterLevel,
            float fadeDepth,
            int resolution,
            float margin,
            int blurRadius,
            out Vector2 center,
            out float size)
        {
            Texture2D texture = LowPolyOceanTool.GetOrCreateShoreMaskTexture(resolution);
            if (texture == null)
            {
                center = Vector2.zero;
                size = 0f;
                return null;
            }

            float[] heights = SampleHeights(
                colliders, resolution, waterLevel, margin, out center, out size);
            BlurHeights(
                heights, resolution, blurRadius, waterLevel - fadeDepth - 0.01f);
            LowPolyOceanShoreMask.FillFromHeights(
                texture, heights, waterLevel, fadeDepth);
            EditorUtility.SetDirty(texture);
            AssetDatabase.SaveAssets();
            return texture;
        }

        /// <summary>
        /// 对高度采样做可分离盒式模糊，抹掉低模 Collider 表面带来的水线台阶。
        /// 没碰到地形的像素先按“深水高度”参与模糊，避免把岸边拉出尖峰。
        /// </summary>
        public static void BlurHeights(
            float[] heights, int resolution, int radius, float deepHeight)
        {
            if (heights == null || radius <= 0 || resolution <= 0)
            {
                return;
            }

            int count = resolution * resolution;
            if (heights.Length < count)
            {
                return;
            }

            radius = Mathf.Clamp(radius, 1, 12);
            float[] source = new float[count];
            float[] temp = new float[count];

            for (int i = 0; i < count; i++)
            {
                source[i] = float.IsNegativeInfinity(heights[i])
                    ? deepHeight
                    : heights[i];
            }

            // 横向
            for (int y = 0; y < resolution; y++)
            {
                int row = y * resolution;
                for (int x = 0; x < resolution; x++)
                {
                    float sum = 0f;
                    int samples = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int xx = Mathf.Clamp(x + k, 0, resolution - 1);
                        sum += source[row + xx];
                        samples++;
                    }
                    temp[row + x] = sum / samples;
                }
            }

            // 纵向
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float sum = 0f;
                    int samples = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int yy = Mathf.Clamp(y + k, 0, resolution - 1);
                        sum += temp[yy * resolution + x];
                        samples++;
                    }
                    heights[y * resolution + x] = sum / samples;
                }
            }
        }
    }
}
