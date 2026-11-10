using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Yujian.Building;

namespace Yujian.Levels
{
    /// <summary>
    /// 关卡污染区的程序化占位视觉层（策划案 2.3.5）。
    ///
    /// 净化前：污染核心是一小团垃圾聚合体，缝隙里透出紫光；地面铺满垃圾，空中飘着绿色气体。
    /// 净化后：核心换成大树，绿色气体与地面垃圾消失，长出红黄花朵和绿草。
    ///
    /// 全部用基础几何体拼，不挂碰撞体，避免挡住入口点击；正式美术到位后整组件可删。
    /// 由 <see cref="LevelManager"/> 在运行时自动挂到每个关卡入口上，不需要跑向导。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LevelEnvironmentVisual : MonoBehaviour
    {
        private const float DefaultCellSize = 2f;

        private LevelEntrance entrance;
        private LevelManager manager;
        private BuildingGrid buildingGrid;

        private Transform pollutedRoot;
        private Transform clearedRoot;
        private bool built;

        private readonly List<Material> runtimeMaterials = new List<Material>();

        /// <summary>由 LevelManager 注入依赖。可重复调用。</summary>
        public void Configure(LevelEntrance newEntrance, LevelManager newManager, BuildingGrid newGrid)
        {
            entrance = newEntrance;
            manager = newManager;
            buildingGrid = newGrid;

            if (built)
            {
                Refresh();
            }
        }

        private void Start()
        {
            Refresh();
        }

        /// <summary>按当前通关状态切换「污染 / 净化」两套占位视觉。</summary>
        public void Refresh()
        {
            if (entrance == null)
            {
                return;
            }

            EnsureBuilt();

            bool cleared = manager != null && manager.IsLevelCleared(entrance);

            if (pollutedRoot != null)
            {
                pollutedRoot.gameObject.SetActive(!cleared);
            }

            if (clearedRoot != null)
            {
                clearedRoot.gameObject.SetActive(cleared);
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < runtimeMaterials.Count; i++)
            {
                DestroySafe(runtimeMaterials[i]);
            }

            runtimeMaterials.Clear();
        }

        private void EnsureBuilt()
        {
            if (built)
            {
                return;
            }

            built = true;

            ResolvePlacement(out Vector3 center, out Vector2 halfExtents, out Vector3 coreOffset);

            transform.position = center;
            transform.rotation = Quaternion.identity;

            int seed = entrance != null
                ? entrance.LevelNumber * 7919 + Mathf.RoundToInt(center.x * 13f + center.z * 29f)
                : 7919;

            pollutedRoot = CreateRoot("Polluted");
            clearedRoot = CreateRoot("Cleared");

            BuildPolluted(pollutedRoot, halfExtents, coreOffset, new System.Random(seed));
            BuildCleared(clearedRoot, halfExtents, coreOffset, new System.Random(seed + 17));

            clearedRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// 以污染核心所在格为中心，算出关卡占地的世界中心、半尺寸与核心相对偏移。
        /// 网格不可用时退化为入口位置 + 默认格边长。
        /// </summary>
        private void ResolvePlacement(
            out Vector3 center, out Vector2 halfExtents, out Vector3 coreOffset)
        {
            Vector2Int footprint = entrance != null ? entrance.FootprintCells : new Vector2Int(4, 4);
            float cellSize = buildingGrid != null ? buildingGrid.CellSize : DefaultCellSize;

            Vector3 coreCenter = entrance != null ? entrance.PollutionCore.position : transform.position;
            Vector2Int coreCell = default;
            bool hasGrid = buildingGrid != null &&
                           buildingGrid.WorldToCell(coreCenter, out coreCell);

            if (hasGrid)
            {
                coreCenter = buildingGrid.CellCenter(coreCell);
            }

            // 偶数格宽的矩形中心比核心格中心偏半格：例如 4×4 时核心格位于矩形内的右上角那一格
            float offsetX = (footprint.x % 2 == 0) ? -cellSize * 0.5f : 0f;
            float offsetZ = (footprint.y % 2 == 0) ? -cellSize * 0.5f : 0f;

            center = coreCenter + new Vector3(offsetX, 0f, offsetZ);
            halfExtents = new Vector2(
                footprint.x * cellSize * 0.5f,
                footprint.y * cellSize * 0.5f);
            coreOffset = coreCenter - center;
        }

        private void BuildPolluted(
            Transform root, Vector2 halfExtents, Vector3 coreOffset, System.Random random)
        {
            Material garbage = CreateLitMaterial("LevelGarbage", new Color(0.22f, 0.21f, 0.16f));
            Material garbageDark = CreateLitMaterial("LevelGarbageDark", new Color(0.15f, 0.14f, 0.12f));
            Material coreGarbage = CreateLitMaterial("LevelCoreGarbage", new Color(0.13f, 0.1f, 0.16f));
            Material purple = CreateUnlitMaterial("LevelPollutionGlow", new Color(0.82f, 0.35f, 1f));
            Material gas = CreateGasMaterial(new Color(0.35f, 0.9f, 0.4f, 0.16f));

            // 地面垃圾：压扁的方块，随机大小与朝向，铺满整块占地
            int garbageCount = 16;

            for (int i = 0; i < garbageCount; i++)
            {
                float x = RandomRange(random, -halfExtents.x + 0.55f, halfExtents.x - 0.55f);
                float z = RandomRange(random, -halfExtents.y + 0.55f, halfExtents.y - 0.55f);
                float width = RandomRange(random, 0.45f, 1.15f);
                float height = RandomRange(random, 0.16f, 0.45f);

                CreatePrimitive(PrimitiveType.Cube, $"Garbage_{i}", root,
                    new Vector3(x, height * 0.5f, z),
                    new Vector3(width, height, width * RandomRange(random, 0.7f, 1.3f)),
                    i % 4 == 0 ? garbageDark : garbage,
                    Quaternion.Euler(0f, RandomRange(random, 0f, 360f), 0f));
            }

            // 绿色气体：半透明球，悬浮在不同高度
            int gasCount = 6;

            for (int i = 0; i < gasCount; i++)
            {
                float x = RandomRange(random, -halfExtents.x * 0.75f, halfExtents.x * 0.75f);
                float z = RandomRange(random, -halfExtents.y * 0.75f, halfExtents.y * 0.75f);
                float y = RandomRange(random, 1.1f, 3.2f);
                float size = RandomRange(random, 1.6f, 2.8f);

                CreatePrimitive(PrimitiveType.Sphere, $"Gas_{i}", root,
                    new Vector3(x, y, z), Vector3.one * size, gas);
            }

            // 污染核心：一小团垃圾聚合体 + 紫色发光球 + 点光源
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f + RandomRange(random, -20f, 20f);
                float radius = RandomRange(random, 0.15f, 0.45f);
                Vector3 offset = new Vector3(
                    Mathf.Cos(angle * Mathf.Deg2Rad) * radius,
                    RandomRange(random, 0.5f, 1.1f),
                    Mathf.Sin(angle * Mathf.Deg2Rad) * radius);

                CreatePrimitive(PrimitiveType.Cube, $"CoreGarbage_{i}", root,
                    coreOffset + offset,
                    Vector3.one * RandomRange(random, 0.35f, 0.65f),
                    coreGarbage,
                    Quaternion.Euler(RandomRange(random, 0f, 45f), angle, RandomRange(random, 0f, 45f)));
            }

            CreatePrimitive(PrimitiveType.Sphere, "PollutionGlow", root,
                coreOffset + new Vector3(0f, 1.15f, 0f), Vector3.one * 0.75f, purple);

            GameObject lightObject = new GameObject("PollutionLight");
            lightObject.transform.SetParent(root, false);
            lightObject.transform.localPosition = coreOffset + new Vector3(0f, 1.6f, 0f);

            Light purpleLight = lightObject.AddComponent<Light>();
            purpleLight.type = LightType.Point;
            purpleLight.color = new Color(0.66f, 0.3f, 1f);
            purpleLight.range = 9f;
            purpleLight.intensity = 3f;
        }

        private void BuildCleared(
            Transform root, Vector2 halfExtents, Vector3 coreOffset, System.Random random)
        {
            Material trunk = CreateLitMaterial("LevelTreeTrunk", new Color(0.36f, 0.23f, 0.12f));
            Material leaf = CreateLitMaterial("LevelTreeLeaf", new Color(0.17f, 0.48f, 0.19f));
            Material leafLight = CreateLitMaterial("LevelTreeLeafLight", new Color(0.25f, 0.6f, 0.24f));
            Material grass = CreateLitMaterial("LevelGrass", new Color(0.24f, 0.62f, 0.25f));
            Material flowerRed = CreateLitMaterial("LevelFlowerRed", new Color(0.86f, 0.16f, 0.14f));
            Material flowerYellow = CreateLitMaterial("LevelFlowerYellow", new Color(0.96f, 0.79f, 0.15f));

            // 大树：圆柱树干 + 三团球形树冠，长在污染核心格上
            CreatePrimitive(PrimitiveType.Cylinder, "Trunk", root,
                coreOffset + new Vector3(0f, 2.2f, 0f),
                new Vector3(0.9f, 2.2f, 0.9f), trunk);

            CreatePrimitive(PrimitiveType.Sphere, "Canopy_Main", root,
                coreOffset + new Vector3(0f, 4.7f, 0f),
                Vector3.one * 3.4f, leaf);

            CreatePrimitive(PrimitiveType.Sphere, "Canopy_Left", root,
                coreOffset + new Vector3(-0.9f, 4.1f, 0.35f),
                Vector3.one * 2.3f, leafLight);

            CreatePrimitive(PrimitiveType.Sphere, "Canopy_Back", root,
                coreOffset + new Vector3(0.8f, 4.0f, -0.5f),
                Vector3.one * 2.5f, leaf);

            // 草地：细长方块，随机铺开
            for (int i = 0; i < 22; i++)
            {
                float x = RandomRange(random, -halfExtents.x + 0.4f, halfExtents.x - 0.4f);
                float z = RandomRange(random, -halfExtents.y + 0.4f, halfExtents.y - 0.4f);

                CreatePrimitive(PrimitiveType.Cube, $"Grass_{i}", root,
                    new Vector3(x, 0.16f, z),
                    new Vector3(0.1f, RandomRange(random, 0.25f, 0.45f), 0.1f),
                    grass,
                    Quaternion.Euler(0f, RandomRange(random, 0f, 360f), RandomRange(random, -12f, 12f)));
            }

            // 红黄花朵：小方块 + 细花茎，散在草地上
            for (int i = 0; i < 14; i++)
            {
                float x = RandomRange(random, -halfExtents.x + 0.5f, halfExtents.x - 0.5f);
                float z = RandomRange(random, -halfExtents.y + 0.5f, halfExtents.y - 0.5f);
                Material petal = i % 2 == 0 ? flowerRed : flowerYellow;

                CreatePrimitive(PrimitiveType.Cube, $"FlowerStem_{i}", root,
                    new Vector3(x, 0.18f, z), new Vector3(0.05f, 0.36f, 0.05f), grass);

                CreatePrimitive(PrimitiveType.Sphere, $"Flower_{i}", root,
                    new Vector3(x, 0.4f, z), Vector3.one * RandomRange(random, 0.18f, 0.3f), petal);
            }
        }

        private Transform CreateRoot(string rootName)
        {
            GameObject go = new GameObject(rootName);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        private GameObject CreatePrimitive(
            PrimitiveType type, string objectName, Transform parent,
            Vector3 localPosition, Vector3 localScale, Material material,
            Quaternion? rotation = null)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = objectName;

            // 占位视觉不挂碰撞体，否则会挡住入口的点击射线
            Collider collider = go.GetComponent<Collider>();

            if (collider != null)
            {
                DestroySafe(collider);
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = rotation ?? Quaternion.identity;
            go.transform.localScale = localScale;

            Renderer renderer = go.GetComponent<Renderer>();

            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
            }

            return go;
        }

        // ---------------- 材质 ----------------

        private Material CreateLitMaterial(string materialName, Color color)
        {
            Material material = CreateMaterial(materialName,
                "Universal Render Pipeline/Lit", "Standard", "Sprites/Default");

            SetColor(material, color);

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.08f);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", 0.08f);
            }

            return material;
        }

        private Material CreateUnlitMaterial(string materialName, Color color)
        {
            Material material = CreateMaterial(materialName,
                "Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default");

            SetColor(material, color);
            return material;
        }

        /// <summary>半透明气体材质。URP Unlit 需要手动把 Surface 改成 Transparent。</summary>
        private Material CreateGasMaterial(Color color)
        {
            Material material = CreateMaterial("LevelGas",
                "Universal Render Pipeline/Unlit", "Sprites/Default");

            SetColor(material, color);

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }

            return material;
        }

        private Material CreateMaterial(string materialName, params string[] shaderNames)
        {
            Shader shader = null;

            for (int i = 0; i < shaderNames.Length && shader == null; i++)
            {
                shader = Shader.Find(shaderNames[i]);
            }

            if (shader == null)
            {
                Debug.LogWarning($"[LevelEnvironmentVisual] 找不到可用 Shader，{materialName} 会显示为粉色。", this);
                shader = Shader.Find("Hidden/InternalErrorShader");
            }

            Material material = new Material(shader)
            {
                name = materialName,
            };

            runtimeMaterials.Add(material);
            return material;
        }

        private static void SetColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private static float RandomRange(System.Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        /// <summary>运行时 Destroy，编辑器（向导 / 批处理验收）用 DestroyImmediate。</summary>
        private static void DestroySafe(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
