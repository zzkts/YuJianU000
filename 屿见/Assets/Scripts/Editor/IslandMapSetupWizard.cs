using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Yujian.Island;
using Yujian.Mining;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 生成低模海岛底图：中央平整台地 + 沙滩/海床 + 海面 + 装饰山体。
    /// 只新增场景物体和资产，不改任何玩法脚本；
    /// 原 Plane 的 Renderer 会被隐藏，但 MeshCollider 保留并挂到 Ground 层，
    /// 供后续方格放置系统做地面射线。
    /// </summary>
    public static class IslandMapSetupWizard
    {
        private const string MenuPath = "屿见/场景/生成低模海岛底图";
        private const string MeshFolder = "Assets/Data/Island";
        private const string MeshAssetPath = MeshFolder + "/IslandTerrain.asset";
        private const string SettingsAssetPath = MeshFolder + "/IslandSettings.asset";
        private const string MaterialFolder = "Assets/Materials/Island";
        private const string LitShaderName = "Universal Render Pipeline/Lit";
        private const string WaterShaderPath = "Assets/Shaders/IslandWater.shader";
        private const string WaterShaderName = "Yujian/Island Water";
        private const string GroundLayerName = "Ground";

        [MenuItem(MenuPath, false, 210)]
        public static void GenerateIsland()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Fail("请先退出 Play Mode 再生成海岛。");
                return;
            }

            Shader litShader = Shader.Find(LitShaderName);
            if (litShader == null)
            {
                Fail("找不到 URP Lit Shader，请确认当前项目使用 URP。");
                return;
            }

            Shader waterShader = AssetDatabase.LoadAssetAtPath<Shader>(WaterShaderPath);
            if (waterShader == null)
            {
                Fail("找不到 " + WaterShaderPath + "，请确认水面 Shader 已导入。");
                return;
            }
            if (ShaderUtil.ShaderHasError(waterShader))
            {
                Fail("水面 Shader 编译失败，请先看 Console 里的 Shader error 并修复 " + WaterShaderPath + "。");
                return;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                Fail("当前没有有效的活动场景。");
                return;
            }

            int planeCount = CountRoots(activeScene, "Plane");
            if (planeCount != 1)
            {
                Fail("场景里必须恰好有一个名为 Plane 的根物体，当前找到 " + planeCount + " 个。");
                return;
            }

            GameObject plane = FindUniqueRoot(activeScene, "Plane");
            MeshFilter planeFilter = plane.GetComponent<MeshFilter>();
            MeshRenderer planeRenderer = plane.GetComponent<MeshRenderer>();
            if (planeFilter == null || planeRenderer == null)
            {
                Fail("Plane 缺少 MeshFilter 或 MeshRenderer，无法作为地图基准。");
                return;
            }

            IslandSettingsAsset settingsAsset = EnsureSettingsAsset();
            IslandSettings settings = settingsAsset.settings;
            // Plane 目前是 y = 0；台地顶面也固定在 y = 0。
            settings.center = new Vector3(plane.transform.position.x, 0f, plane.transform.position.z);
            EditorUtility.SetDirty(settingsAsset);

            // 先做只读校验，全部通过后再改场景，避免留下半配置状态。
            List<string> problems = new List<string>();
            ValidateFlatArea(settings, planeRenderer, problems);
            if (problems.Count > 0)
            {
                Fail("台地校验失败，未生成海岛：\n" + string.Join("\n", problems));
                return;
            }

            int groundLayer = EnsureLayer(GroundLayerName);
            if (groundLayer < 0)
            {
                Fail("无法创建 Ground 层，请到 Project Settings > Tags and Layers 手动添加后重试。");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("生成低模海岛底图");

            EnsureFolder(MaterialFolder);

            Material grass = EnsureMaterial("Island_Grass", new Color(0.553f, 0.733f, 0.388f), 0.08f, litShader);
            Material sand = EnsureMaterial("Island_Sand", new Color(0.902f, 0.827f, 0.639f), 0.08f, litShader);
            Material rock = EnsureMaterial("Island_Rock", new Color(0.639f, 0.612f, 0.549f), 0.08f, litShader);
            Material seabed = EnsureMaterial("Island_Seabed", new Color(0.310f, 0.549f, 0.510f), 0.05f, litShader);
            int depthTextureChanges = EnsureWaterDepthTexture();
            Material water = EnsureWaterMaterial(waterShader);

            Mesh mesh = EnsureMesh(settings);
            GameObject islandObject = CreateOrUpdateIsland(
                activeScene, settings, mesh, grass, sand, rock, seabed, groundLayer);
            CreateOrUpdateOcean(activeScene, settings, water);
            PreparePlane(plane, groundLayer);

            EditorSceneManager.MarkSceneDirty(activeScene);
            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(undoGroup);

            Selection.activeGameObject = islandObject;
            EditorGUIUtility.PingObject(islandObject);

            Debug.Log(string.Format(
                "[屿见] 低模海岛已生成。Ground 层索引 {0}；网格 {1} 顶点 / {2} 三角形；"
                + "分区材质 4 个；海面 1 个（{3}，深度纹理{4}）；台地校验通过。"
                + "\n参数资产：{5}\n请按 Ctrl+S 保存场景。",
                groundLayer, mesh.vertexCount, CountTriangles(mesh), WaterShaderName,
                depthTextureChanges > 0 ? "已自动开启" : "已就绪",
                SettingsAssetPath));
        }

        private static void ValidateFlatArea(
            IslandSettings settings, MeshRenderer planeRenderer, List<string> problems)
        {
            Bounds bounds = planeRenderer.bounds;
            Vector3[] corners =
            {
                new Vector3(bounds.min.x, 0f, bounds.min.z),
                new Vector3(bounds.min.x, 0f, bounds.max.z),
                new Vector3(bounds.max.x, 0f, bounds.min.z),
                new Vector3(bounds.max.x, 0f, bounds.max.z),
            };

            foreach (Vector3 corner in corners)
            {
                CheckPoint(settings, corner, "Plane 角点", problems);
            }

            CheckPoint(settings, settings.center, "地图中心", problems);
            CheckNamedTransform(settings, "Player", problems);
            CheckNamedTransform(settings, "Shop", problems);

            Ore[] ores = UnityEngine.Object.FindObjectsByType<Ore>(FindObjectsSortMode.None);
            if (ores.Length == 0)
            {
                Debug.LogWarning("[屿见] 场景里没有找到矿石，跳过矿石的台地校验。");
            }

            foreach (Ore ore in ores)
            {
                CheckPoint(settings, ore.transform.position, "矿石 " + ore.name, problems);
            }
        }

        private static void CheckNamedTransform(
            IslandSettings settings, string objectName, List<string> problems)
        {
            GameObject target = GameObject.Find(objectName);
            if (target == null)
            {
                Debug.LogWarning("[屿见] 场景里没有找到 " + objectName + "，跳过它的台地校验。");
                return;
            }

            CheckPoint(settings, target.transform.position, objectName, problems);
        }

        private static void CheckPoint(
            IslandSettings settings, Vector3 worldPosition, string label, List<string> problems)
        {
            float distance = IslandMeshGenerator.CoastDistance(settings, worldPosition);
            if (distance > 0f)
            {
                problems.Add(string.Format(
                    "{0} {1} 落在台地外（d = {2:F2}），请调整台地尺寸或位置。",
                    label, worldPosition, distance));
            }
        }

        private static GameObject CreateOrUpdateIsland(
            Scene scene,
            IslandSettings settings,
            Mesh mesh,
            Material grass,
            Material sand,
            Material rock,
            Material seabed,
            int groundLayer)
        {
            GameObject islandObject = FindUniqueRoot(scene, "Island");
            if (islandObject == null)
            {
                islandObject = new GameObject("Island");
                Undo.RegisterCreatedObjectUndo(islandObject, "创建海岛");
            }

            Undo.RecordObject(islandObject.transform, "配置海岛");
            Undo.RecordObject(islandObject, "配置海岛");
            islandObject.layer = groundLayer;
            islandObject.transform.position = settings.center;
            islandObject.transform.rotation = Quaternion.identity;
            islandObject.transform.localScale = Vector3.one;

            MeshFilter filter = islandObject.GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = Undo.AddComponent<MeshFilter>(islandObject);
            }
            Undo.RecordObject(filter, "设置海岛网格");
            filter.sharedMesh = mesh;

            MeshRenderer renderer = islandObject.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = Undo.AddComponent<MeshRenderer>(islandObject);
            }
            Undo.RecordObject(renderer, "设置海岛材质");
            renderer.sharedMaterials = new[] { grass, sand, rock, seabed };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            // 第一版不加 Collider；如果旧版本留下过，移除它。
            MeshCollider existingCollider = islandObject.GetComponent<MeshCollider>();
            if (existingCollider != null)
            {
                Undo.DestroyObjectImmediate(existingCollider);
            }

            IslandDefinition definition = islandObject.GetComponent<IslandDefinition>();
            if (definition == null)
            {
                definition = Undo.AddComponent<IslandDefinition>(islandObject);
            }
            Undo.RecordObject(definition, "配置可建区");
            definition.Configure(
                settings.center,
                new Vector2(settings.halfSizeX, settings.halfSizeZ),
                settings.waterLevel);

            return islandObject;
        }

        private static void CreateOrUpdateOcean(
            Scene scene, IslandSettings settings, Material water)
        {
            GameObject oceanObject = FindUniqueRoot(scene, "Ocean");

            // 如果同名物体不是我们创建的平面（没有 MeshFilter），删掉重建。
            if (oceanObject != null && oceanObject.GetComponent<MeshFilter>() == null)
            {
                Undo.DestroyObjectImmediate(oceanObject);
                oceanObject = null;
            }

            if (oceanObject == null)
            {
                oceanObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
                oceanObject.name = "Ocean";
                Undo.RegisterCreatedObjectUndo(oceanObject, "创建海面");

                Collider primitiveCollider = oceanObject.GetComponent<Collider>();
                if (primitiveCollider != null)
                {
                    // 水面不参与任何物理查询。
                    Undo.DestroyObjectImmediate(primitiveCollider);
                }
            }

            int waterLayer = LayerMask.NameToLayer("Water");
            if (waterLayer < 0)
            {
                Debug.LogWarning("[屿见] 没有找到 Water 层，海面暂时放在 Default 层。");
                waterLayer = 0;
            }

            Undo.RecordObject(oceanObject.transform, "配置海面");
            Undo.RecordObject(oceanObject, "配置海面");
            oceanObject.layer = waterLayer;
            oceanObject.transform.position = new Vector3(
                settings.center.x, settings.waterLevel, settings.center.z);
            oceanObject.transform.rotation = Quaternion.identity;
            // 内置 Plane 是 10×10，scale 200 = 2000×2000，远超相机 far clip。
            oceanObject.transform.localScale = new Vector3(200f, 1f, 200f);

            MeshRenderer renderer = oceanObject.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = Undo.AddComponent<MeshRenderer>(oceanObject);
            }
            Undo.RecordObject(renderer, "设置海面材质");
            renderer.sharedMaterial = water;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        private static void PreparePlane(GameObject plane, int groundLayer)
        {
            MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Undo.RecordObject(renderer, "隐藏原地图平面");
                renderer.enabled = false;
            }

            MeshCollider collider = plane.GetComponent<MeshCollider>();
            if (collider == null)
            {
                collider = Undo.AddComponent<MeshCollider>(plane);
            }
            Undo.RecordObject(collider, "启用地图碰撞");
            collider.enabled = true;
            if (collider.sharedMesh == null)
            {
                MeshFilter filter = plane.GetComponent<MeshFilter>();
                if (filter != null)
                {
                    collider.sharedMesh = filter.sharedMesh;
                }
            }

            Undo.RecordObject(plane, "设置地图层");
            plane.layer = groundLayer;
        }

        /// <summary>
        /// 加载或创建参数资产。首次运行自动生成，之后在 Inspector 里调参再重跑菜单。
        /// </summary>
        private static IslandSettingsAsset EnsureSettingsAsset()
        {
            EnsureFolder(MeshFolder);

            IslandSettingsAsset asset =
                AssetDatabase.LoadAssetAtPath<IslandSettingsAsset>(SettingsAssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<IslandSettingsAsset>();
                AssetDatabase.CreateAsset(asset, SettingsAssetPath);
            }

            if (asset.settings == null)
            {
                asset.settings = new IslandSettings();
                EditorUtility.SetDirty(asset);
            }

            return asset;
        }

        private static Mesh EnsureMesh(IslandSettings settings)
        {
            Mesh generated = IslandMeshGenerator.Generate(settings);
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, MeshAssetPath);
                return generated;
            }

            // 原地覆盖，保住 GUID，场景里的引用不会丢。
            CopyMesh(generated, existing);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(existing);
            return existing;
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

        private static Material EnsureMaterial(
            string fileName, Color color, float smoothness, Shader litShader)
        {
            string path = MaterialFolder + "/" + fileName + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                if (existing.shader == null || existing.shader.name != LitShaderName)
                {
                    Debug.LogWarning("[屿见] 材质 " + path + " 已存在但不是 URP Lit，保留原样，请手动检查。");
                }
                return existing;
            }

            Material material = new Material(litShader) { name = fileName };
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// 加载或创建海面材质，并把它切换到本项目的透明水面 Shader。
        /// 只在首次创建或切换 Shader 时写入默认美术参数，之后保留手动调色。
        /// </summary>
        private static Material EnsureWaterMaterial(Shader waterShader)
        {
            EnsureFolder(MaterialFolder);
            string path = MaterialFolder + "/Ocean_Water.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = false;
            bool shaderChanged = false;

            // 旧材质是 URP Lit，切换前先记下原来的海面颜色，作为浅水色的种子。
            Color previousColor = new Color(0.180f, 0.561f, 0.639f);
            if (material != null && material.HasProperty("_BaseColor"))
            {
                previousColor = material.GetColor("_BaseColor");
            }

            if (material == null)
            {
                material = new Material(waterShader) { name = "Ocean_Water" };
                created = true;
            }
            else
            {
                Undo.RecordObject(material, "配置水面材质");
                if (material.shader != waterShader)
                {
                    material.shader = waterShader;
                    shaderChanged = true;
                }
            }

            if (created || shaderChanged)
            {
                material.SetColor("_ShallowColor", previousColor);
                material.SetColor("_DeepColor", new Color(
                    previousColor.r * 0.32f, previousColor.g * 0.46f, previousColor.b * 0.64f, 1f));
                material.SetColor("_FoamColor", new Color(0.94f, 0.98f, 1f, 1f));
                material.SetOverrideTag("RenderType", "Transparent");
            }

            // 透明队列固定为 3000，避免旧材质序列化下来的 Opaque 标签影响排序。
            if (material.renderQueue != (int)RenderQueue.Transparent)
            {
                material.renderQueue = (int)RenderQueue.Transparent;
            }

            EditorUtility.SetDirty(material);
            if (created)
            {
                AssetDatabase.CreateAsset(material, path);
                Debug.Log("[屿见] 已创建海面材质并切换到 " + WaterShaderName + "。");
            }
            else if (shaderChanged)
            {
                Debug.Log("[屿见] 海面材质已切换到 " + WaterShaderName + "。");
            }

            return material;
        }

        /// <summary>
        /// 水面泡沫依赖 URP 的 Scene Depth。把工程里所有 URP 资产的深度纹理打开；
        /// 已经打开的不重复改。找不到资产时只警告，不中止生成。
        /// </summary>
        private static int EnsureWaterDepthTexture()
        {
            string[] guids = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset");
            if (guids.Length == 0)
            {
                Debug.LogWarning(
                    "[屿见] 没找到 URP 资产，无法自动开启深度纹理；"
                    + "请到 URP Asset 里手动勾选 Depth Texture。");
                return 0;
            }

            int changed = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (asset == null)
                {
                    continue;
                }

                SerializedObject serialized = new SerializedObject(asset);
                SerializedProperty property = serialized.FindProperty("m_RequireDepthTexture");
                if (property == null || property.boolValue)
                {
                    continue;
                }

                Undo.RecordObject(asset, "开启水面深度纹理");
                property.boolValue = true;
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(asset);
                changed++;
                Debug.Log("[屿见] 已为 " + path + " 开启 Depth Texture（水面泡沫依赖）。");
            }

            if (changed > 0)
            {
                AssetDatabase.SaveAssets();
            }

            return changed;
        }

        /// <summary>
        /// 确保指定图层存在并返回索引；槽位全满时返回 -1。
        /// 当前工程 0~7 已占用，从 8 开始找空槽。
        /// </summary>
        private static int EnsureLayer(string layerName)
        {
            int existing = LayerMask.NameToLayer(layerName);
            if (existing >= 0)
            {
                return existing;
            }

            UnityEngine.Object[] assets =
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogError("[屿见] 读不到 TagManager.asset，无法创建 " + layerName + " 层。");
                return -1;
            }

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            if (layers == null)
            {
                Debug.LogError("[屿见] TagManager 中找不到 layers 属性。");
                return -1;
            }

            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(layer.stringValue))
                {
                    layer.stringValue = layerName;
                    tagManager.ApplyModifiedProperties();
                    AssetDatabase.SaveAssets();
                    Debug.Log(string.Format(
                        "[屿见] 已创建图层 {0}（索引 {1}）。", layerName, i));
                    return i;
                }
            }

            Debug.LogError("[屿见] 图层槽位已满，请手动添加 " + layerName + " 层。");
            return -1;
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(folderPath);
            if (string.IsNullOrEmpty(parent))
            {
                return;
            }

            parent = parent.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            string leaf = System.IO.Path.GetFileName(folderPath);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static int CountTriangles(Mesh mesh)
        {
            int triangles = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                triangles += (int)mesh.GetIndexCount(i) / 3;
            }
            return triangles;
        }

        private static int CountRoots(Scene scene, string objectName)
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == objectName)
                {
                    count++;
                }
            }
            return count;
        }

        private static GameObject FindUniqueRoot(Scene scene, string objectName)
        {
            GameObject found = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != objectName)
                {
                    continue;
                }

                if (found != null)
                {
                    Debug.LogWarning("[屿见] 场景里有多个名为 " + objectName
                        + " 的根物体，使用第一个。");
                    return found;
                }
                found = root;
            }
            return found;
        }

        private static void Fail(string message)
        {
            Debug.LogError("[屿见] " + message);
            EditorUtility.DisplayDialog("低模海岛底图", message, "确定");
        }
    }
}
