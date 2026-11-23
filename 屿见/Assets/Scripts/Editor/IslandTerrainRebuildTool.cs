using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yujian.Building;
using Yujian.Island;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 按 IslandSettings 重新生成海岛网格，并把场景里的布局中心（可建区 / 建筑网格 /
    /// 地面 / 海面）对齐到 Island 物体。只改位置和网格，不动玩家、建筑和玩法数据。
    ///
    /// 场景里 Island 物体曾经被单独挪动过，而可建区/网格/海面还停留在旧中心，
    /// 导致岛边和可建区错位；重新调小岛型后必须先对齐，否则会出现“能在水边放建筑”。
    /// </summary>
    public static class IslandTerrainRebuildTool
    {
        private const string MenuPath = "屿见/场景/重建海岛模型";
        private const string MeshAssetPath = "Assets/Data/Island/IslandTerrain.asset";
        private const string SettingsAssetPath = "Assets/Data/Island/IslandSettings.asset";
        private const string TerrainMaterialPath = "Assets/Materials/Island/Island_Terrain.mat";
        private const string TerrainShaderName = "Yujian/Island Terrain";
        private const string IslandName = "Island";
        private const string PlaneName = "Plane";
        private const string LegacyOceanName = "Ocean";
        private const string StylizedOceanName = "Ocean_StylizedWater2";

        [MenuItem(MenuPath, false, 212)]
        public static void Rebuild()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[屿见] 请先退出 Play Mode 再重建海岛模型。");
                return;
            }

            IslandSettingsAsset settingsAsset =
                AssetDatabase.LoadAssetAtPath<IslandSettingsAsset>(SettingsAssetPath);
            if (settingsAsset == null || settingsAsset.settings == null)
            {
                Debug.LogError("[屿见] 找不到海岛参数资产：" + SettingsAssetPath);
                return;
            }

            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath);
            if (mesh == null)
            {
                Debug.LogError("[屿见] 找不到海岛网格资产：" + MeshAssetPath);
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogError("[屿见] 当前没有有效的活动场景。");
                return;
            }

            GameObject island = FindRoot(scene, IslandName);
            if (island == null)
            {
                Debug.LogError("[屿见] 场景里找不到名为 " + IslandName + " 的根物体。");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("重建海岛模型");

            IslandSettings settings = settingsAsset.settings;
            // 以岛物体的 XZ 作为新中心；台地顶面固定在世界 y = 0。
            Vector3 center = new Vector3(
                island.transform.position.x, 0f, island.transform.position.z);

            Undo.RecordObject(settingsAsset, "配置海岛参数");
            settings.center = center;
            EditorUtility.SetDirty(settingsAsset);

            Undo.RecordObject(island.transform, "对齐海岛");
            island.transform.position = center;

            IslandDefinition definition = island.GetComponent<IslandDefinition>();
            if (definition != null)
            {
                Undo.RecordObject(definition, "更新可建区");
                definition.Configure(
                    center,
                    new Vector2(settings.halfSizeX, settings.halfSizeZ),
                    settings.waterLevel);
                EditorUtility.SetDirty(definition);
            }

            // 用一套按高度连续混色的地形材质替换原来的草/沙/岩/湿沙四块硬切材质。
            MeshRenderer islandRenderer = island.GetComponent<MeshRenderer>();
            Material terrainMaterial = EnsureTerrainMaterial(settings);
            if (islandRenderer != null && terrainMaterial != null)
            {
                Undo.RecordObject(islandRenderer, "设置平滑地形材质");
                // 0=草 1=沙 2=岩 3=湿沙。岩石保留独立材质，避免礁石被染成草绿色；
                // 草/沙/湿沙共用平滑地形材质，它们之间的分界由高度渐变接管。
                Material[] materials = islandRenderer.sharedMaterials;
                if (materials.Length < 4)
                {
                    Material[] expanded = new Material[4];
                    for (int i = 0; i < materials.Length; i++)
                    {
                        expanded[i] = materials[i];
                    }

                    materials = expanded;
                }

                materials[0] = terrainMaterial;
                materials[1] = terrainMaterial;
                materials[3] = terrainMaterial;
                islandRenderer.sharedMaterials = materials;
                EditorUtility.SetDirty(islandRenderer);
            }

            // 地面基准、旧海面、插件海面一起对齐到岛屿中心。
            MoveRoot(scene, PlaneName, center);
            Vector3 waterPosition = new Vector3(center.x, settings.waterLevel, center.z);
            MoveRoot(scene, LegacyOceanName, waterPosition);
            MoveRoot(scene, StylizedOceanName, waterPosition);

            // 建筑网格按更新后的 IslandDefinition 重新自适应（autoFit 为 true 时）。
            BuildingGrid grid = UnityEngine.Object.FindFirstObjectByType<BuildingGrid>(
                FindObjectsInactive.Include);
            if (grid != null)
            {
                Undo.RecordObject(grid, "对齐建筑网格");
                grid.Rebuild();
                EditorUtility.SetDirty(grid);
            }

            Mesh generated = IslandMeshGenerator.Generate(settings);
            CopyMesh(generated, mesh);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(mesh);

            WarnIfOutside(settings, scene, "Player");
            WarnIfOutside(settings, scene, "Shop");

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undoGroup);
            SceneView.RepaintAll();

            Debug.Log(string.Format(
                "[屿见] 海岛模型已重建并对齐到中心 ({0:F2}, {1:F2})。\n" +
                "网格：{2} 顶点 / {3} 三角形；可建台地 {4}×{5}。\n" +
                "请按 Ctrl+S 保存场景。",
                center.x, center.z,
                mesh.vertexCount, CountTriangles(mesh),
                settings.halfSizeX * 2f, settings.halfSizeZ * 2f));
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == name)
                {
                    return roots[i];
                }
            }

            return null;
        }

        private static void MoveRoot(Scene scene, string name, Vector3 position)
        {
            GameObject root = FindRoot(scene, name);
            if (root == null)
            {
                return;
            }

            Undo.RecordObject(root.transform, "对齐场景布局");
            root.transform.position = position;
        }

        /// <summary>
        /// 缩小台地后，提示玩家/商店等关键物体是否落到了台地外。
        /// 只警告，不自动挪动，避免破坏已有的玩法摆放。
        /// </summary>
        private static void WarnIfOutside(IslandSettings settings, Scene scene, string name)
        {
            GameObject target = FindRoot(scene, name);
            if (target == null)
            {
                return;
            }

            float distance = IslandMeshGenerator.CoastDistance(
                settings, target.transform.position);
            if (distance > 0f)
            {
                Debug.LogWarning(string.Format(
                    "[屿见] {0} 在新台地外约 {1:F2} 米，可能需要手动挪回岛上。",
                    name, distance));
            }
        }

        /// <summary>
        /// 加载或创建“平滑地形”材质。只有新建或 Shader 被替换时才写入推荐配色，
        /// 之后手动调过的颜色不会被重建覆盖。
        /// </summary>
        private static Material EnsureTerrainMaterial(IslandSettings settings)
        {
            Shader shader = Shader.Find(TerrainShaderName);
            if (shader == null)
            {
                Debug.LogWarning("[屿见] 找不到 " + TerrainShaderName
                    + "，保留原来的分区材质。");
                return null;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
            bool needDefaults = false;
            if (material == null)
            {
                material = new Material(shader) { name = "Island_Terrain" };
                AssetDatabase.CreateAsset(material, TerrainMaterialPath);
                needDefaults = true;
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                needDefaults = true;
            }

            if (needDefaults)
            {
                material.SetColor("_GrassColor", new Color(0.553f, 0.733f, 0.388f));
                material.SetColor("_SandColor", new Color(0.902f, 0.827f, 0.639f));
                material.SetColor("_WetSandColor", new Color(0.58f, 0.50f, 0.38f));
                material.SetColor("_SeabedColor", new Color(0.74f, 0.68f, 0.52f));
                material.SetColor("_RockColor", new Color(0.639f, 0.612f, 0.549f));
                material.SetFloat("_GrassStart", 0.6f);
                material.SetFloat("_GrassBlend", 0.7f);
                material.SetFloat("_WetBand", 0.3f);
                material.SetFloat("_WetStrength", 0.35f);
                material.SetFloat("_RockSlopeStart", 0.35f);
                material.SetFloat("_RockSlopeEnd", 0.7f);
                material.SetFloat("_NoiseScale", 0.3f);
                material.SetFloat("_NoiseStrength", 0.12f);
            }

            // 海平面每次重建都同步，避免水位调整后色带错位。
            material.SetFloat("_WaterLevel", settings.waterLevel);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CopyMesh(Mesh source, Mesh destination)
        {
            destination.Clear();
            destination.indexFormat = source.indexFormat;
            destination.SetVertices(source.vertices);
            destination.SetNormals(source.normals);
            destination.SetUVs(0, source.uv);
            destination.subMeshCount = source.subMeshCount;
            for (int i = 0; i < source.subMeshCount; i++)
            {
                destination.SetTriangles(source.GetTriangles(i), i);
            }

            destination.RecalculateBounds();
        }

        private static int CountTriangles(Mesh mesh)
        {
            int count = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                count += (int)(mesh.GetIndexCount(i) / 3);
            }

            return count;
        }
    }
}
