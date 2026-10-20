using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using Yujian.Building;
using Yujian.Island;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 阶段 7 的一键配置工具：建筑网格、建造相机控制、网格可视化。
    ///
    /// 为什么不扩 <see cref="BuildingSetupWizard"/>：
    /// 阶段 3 的向导重跑时会把 buildingModePosition / buildingModeEulerAngles 覆盖成硬编码默认值
    /// （场景里已是调好的机位），还会重建 BuildingPanel 的子物体。
    /// 本工具只做增量接线，可独立、安全地重复运行。
    ///
    /// 运行入口：菜单栏 → 屿见 → 配置建筑网格与建造相机（阶段 7）
    /// </summary>
    public static class BuildingGridSetupWizard
    {
        private const string IslandSettingsPath = "Assets/Data/Island/IslandSettings.asset";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

        private const string ValidMaterialPath = "Assets/Materials/Valid.mat";
        private const string InvalidMaterialPath = "Assets/Materials/InValid.mat";
        private const string GridLineMaterialPath = "Assets/Materials/GridLine.mat";

        private const float CellSize = 2f;
        private const float BuildableInset = 0f;

        private static readonly Color GridLineColor = new Color(0.35f, 0.40f, 0.35f, 0.25f);

        [MenuItem("屿见/配置建筑网格与建造相机（阶段 7）")]
        public static void Configure()
        {
            // ---------- 前置检查：任何一项不满足就整体不执行，避免留下半配置状态 ----------

            BuildingManager manager = Object.FindFirstObjectByType<BuildingManager>();

            if (manager == null)
            {
                Fail("场景里找不到 BuildingManager。\n\n" +
                     "请先运行菜单「屿见 → 配置建筑系统（阶段 3）」，再运行本命令。");
                return;
            }

            GameObject systemObject = manager.gameObject;
            BuildingPlacement placement = systemObject.GetComponent<BuildingPlacement>();
            BuildingCameraDirector director = systemObject.GetComponent<BuildingCameraDirector>();

            if (placement == null || director == null)
            {
                Fail($"物体「{systemObject.name}」上缺少 BuildingPlacement 或 BuildingCameraDirector。\n\n" +
                     "请先运行菜单「屿见 → 配置建筑系统（阶段 3）」，再运行本命令。");
                return;
            }

            IslandDefinition island = Object.FindFirstObjectByType<IslandDefinition>();

            if (island == null)
            {
                Fail("场景里找不到 IslandDefinition（海岛根物体）。\n\n" +
                     "没有它就无法确定可建台地与网格范围。请先运行海岛生成向导。");
                return;
            }

            Camera mainCamera = Camera.main;

            if (mainCamera == null)
            {
                Fail("场景里找不到带 MainCamera 标签的相机，无法配置建造相机控制。");
                return;
            }

            IslandSettingsAsset islandSettings =
                AssetDatabase.LoadAssetAtPath<IslandSettingsAsset>(IslandSettingsPath);

            if (islandSettings == null)
            {
                Debug.LogWarning($"[BuildingGridSetupWizard] 找不到 {IslandSettingsPath}，" +
                                 "可建区域会退化为 IslandDefinition 的矩形判断，" +
                                 "圆角台地的四角可能允许放到沙滩或水边。");
            }

            // ---------- 1. 组件 ----------

            BuildingGrid grid = EnsureComponent<BuildingGrid>(systemObject);
            BuildingCameraController cameraController = EnsureComponent<BuildingCameraController>(systemObject);
            BuildingGridVisualizer visualizer = EnsureComponent<BuildingGridVisualizer>(systemObject);

            // ---------- 2. BuildingGrid ----------

            Vector2 halfExtents = island.BuildableHalfExtents;
            Vector2Int counts = new Vector2Int(
                Mathf.Max(1, Mathf.FloorToInt(halfExtents.x * 2f / CellSize + 0.0001f)),
                Mathf.Max(1, Mathf.FloorToInt(halfExtents.y * 2f / CellSize + 0.0001f)));

            SerializedObject gridSo = new SerializedObject(grid);
            SetObject(gridSo, "islandDefinition", island);
            SetObject(gridSo, "islandSettings", islandSettings);
            SetVector3(gridSo, "gridCenter", island.BuildableCenter);
            SetFloat(gridSo, "cellSize", CellSize);
            SetVector2Int(gridSo, "cellCounts", counts);
            SetBool(gridSo, "autoFitToIsland", true);
            SetFloat(gridSo, "buildableInset", BuildableInset);
            gridSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 3. 蓝图放置 / 建筑管理器的网格引用 ----------

            SerializedObject placementSo = new SerializedObject(placement);
            SetObject(placementSo, "grid", grid);
            SetObject(placementSo, "gridVisualizer", visualizer);
            placementSo.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject managerSo = new SerializedObject(manager);
            SetObject(managerSo, "grid", grid);
            managerSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 4. 网格可视化：材质与渲染对象 ----------

            Material validMaterial = AssetDatabase.LoadAssetAtPath<Material>(ValidMaterialPath);
            Material invalidMaterial = AssetDatabase.LoadAssetAtPath<Material>(InvalidMaterialPath);
            Material lineMaterial = LoadOrCreateGridLineMaterial(validMaterial);

            if (validMaterial == null)
            {
                Debug.LogWarning($"[BuildingGridSetupWizard] 找不到 {ValidMaterialPath}，" +
                                 "合法占地高亮需要你手动指定材质。");
            }

            if (invalidMaterial == null)
            {
                Debug.LogWarning($"[BuildingGridSetupWizard] 找不到 {InvalidMaterialPath}，" +
                                 "非法占地高亮需要你手动指定材质。");
            }

            Transform gridMeshTransform = FindOrCreateChild(systemObject.transform, "Grid Mesh");
            MeshFilter gridMeshFilter = EnsureComponent<MeshFilter>(gridMeshTransform.gameObject);
            MeshRenderer gridMeshRenderer = EnsureComponent<MeshRenderer>(gridMeshTransform.gameObject);

            Transform highlightTransform = FindOrCreateChild(systemObject.transform, "Grid Highlight");
            MeshFilter highlightMeshFilter = EnsureComponent<MeshFilter>(highlightTransform.gameObject);
            MeshRenderer highlightMeshRenderer = EnsureComponent<MeshRenderer>(highlightTransform.gameObject);

            // 空 Mesh 在运行时才生成；编辑模式下先关掉，避免场景视图里出现粉红色占位
            gridMeshRenderer.enabled = false;
            highlightMeshRenderer.enabled = false;

            if (lineMaterial != null && gridMeshRenderer.sharedMaterial == null)
            {
                gridMeshRenderer.sharedMaterial = lineMaterial;
            }

            if (validMaterial != null && highlightMeshRenderer.sharedMaterial == null)
            {
                highlightMeshRenderer.sharedMaterial = validMaterial;
            }

            SerializedObject visualizerSo = new SerializedObject(visualizer);
            SetObject(visualizerSo, "grid", grid);
            SetObject(visualizerSo, "buildingManager", manager);
            SetObjectIfEmpty(visualizerSo, "lineMaterial", lineMaterial);
            SetObjectIfEmpty(visualizerSo, "validMaterial", validMaterial);
            SetObjectIfEmpty(visualizerSo, "invalidMaterial", invalidMaterial);
            SetObject(visualizerSo, "gridMeshFilter", gridMeshFilter);
            SetObject(visualizerSo, "gridMeshRenderer", gridMeshRenderer);
            SetObject(visualizerSo, "highlightMeshFilter", highlightMeshFilter);
            SetObject(visualizerSo, "highlightMeshRenderer", highlightMeshRenderer);
            visualizerSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 5. 建造相机控制 ----------

            InputActionReference lookAction = FindActionReference("Player", "Look");
            InputActionReference panAction = FindActionReference("UI", "RightClick");
            InputActionReference scrollAction = FindActionReference("UI", "ScrollWheel");

            SerializedObject controllerSo = new SerializedObject(cameraController);
            SetObject(controllerSo, "cameraDirector", director);
            SetObject(controllerSo, "grid", grid);
            SetObject(controllerSo, "cameraTransform", mainCamera.transform);
            SetObjectIfEmpty(controllerSo, "lookAction", lookAction);
            SetObjectIfEmpty(controllerSo, "panAction", panAction);
            SetObjectIfEmpty(controllerSo, "scrollAction", scrollAction);
            controllerSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 6. 收尾 ----------

            EditorSceneManager.MarkSceneDirty(systemObject.scene);
            Selection.activeGameObject = systemObject;
            EditorGUIUtility.PingObject(systemObject);

            Debug.Log($"[BuildingGridSetupWizard] 配置完成：\n" +
                      $"  · {systemObject.name}：BuildingGrid（{counts.x}×{counts.y} 格，单格 {CellSize}）/ " +
                      $"BuildingCameraController / BuildingGridVisualizer\n" +
                      $"  · 网格中心 {island.BuildableCenter}，可建判定 " +
                      $"{(islandSettings != null ? "严格圆角台地" : "矩形回退")}\n" +
                      "  · 建造模式内：右键拖动平移，滚轮缩放\n" +
                      "请按 Ctrl+S 保存场景，然后再进入 Play Mode。");
        }

        /// <summary>
        /// 加载格线材质。不存在时复制 Valid.mat 得到一份半透明绿色，并只改这一份新资产的颜色；
        /// 已存在则直接复用，不覆盖用户调过的颜色。
        /// </summary>
        private static Material LoadOrCreateGridLineMaterial(Material source)
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(GridLineMaterialPath);

            if (existing != null)
            {
                return existing;
            }

            if (source == null)
            {
                Debug.LogWarning($"[BuildingGridSetupWizard] 无法创建 {GridLineMaterialPath}：" +
                                 $"源材质 {ValidMaterialPath} 不存在。请手动指定格线材质。");
                return null;
            }

            if (!AssetDatabase.CopyAsset(ValidMaterialPath, GridLineMaterialPath))
            {
                Debug.LogWarning($"[BuildingGridSetupWizard] 复制 {ValidMaterialPath} 到 " +
                                 $"{GridLineMaterialPath} 失败。请手动创建格线材质。");
                return null;
            }

            AssetDatabase.ImportAsset(GridLineMaterialPath, ImportAssetOptions.ForceUpdate);

            Material created = AssetDatabase.LoadAssetAtPath<Material>(GridLineMaterialPath);

            if (created == null)
            {
                Debug.LogWarning($"[BuildingGridSetupWizard] {GridLineMaterialPath} 复制成功但加载失败，" +
                                 "请手动指定格线材质。");
                return null;
            }

            if (created.HasProperty("_BaseColor"))
            {
                created.SetColor("_BaseColor", GridLineColor);
            }

            if (created.HasProperty("_Color"))
            {
                created.SetColor("_Color", GridLineColor);
            }

            EditorUtility.SetDirty(created);
            AssetDatabase.SaveAssetIfDirty(created);

            return created;
        }

        /// <summary>
        /// 在 .inputactions 资产的子资产里按 动作表/动作 名查找 InputActionReference。
        /// 找不到只警告并提示手动拖入，不修改输入资产本身。
        /// </summary>
        private static InputActionReference FindActionReference(string mapName, string actionName)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(InputActionsPath);

            for (int i = 0; i < assets.Length; i++)
            {
                InputActionReference reference = assets[i] as InputActionReference;

                if (reference == null)
                {
                    continue;
                }

                InputAction action = reference.action;

                if (action == null || action.actionMap == null)
                {
                    continue;
                }

                if (action.actionMap.name == mapName && action.name == actionName)
                {
                    return reference;
                }
            }

            Debug.LogWarning($"[BuildingGridSetupWizard] 在 {InputActionsPath} 中找不到 " +
                             $"{mapName}/{actionName} 的 InputActionReference，" +
                             "请手动把该动作拖到 BuildingCameraController 的对应字段。");
            return null;
        }

        private static Transform FindOrCreateChild(Transform parent, string childName)
        {
            Transform existing = parent.Find(childName);

            if (existing != null)
            {
                return existing;
            }

            // 普通 Transform，不是 RectTransform：这是挂世界空间 3D 网格的容器
            GameObject child = new GameObject(childName);
            child.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(child, "创建 " + childName);
            return child.transform;
        }

        private static T EnsureComponent<T>(GameObject target) where T : Component
        {
            T existing = target.GetComponent<T>();
            return existing != null ? existing : target.AddComponent<T>();
        }

        // ---- SerializedProperty 写入助手 ----
        // 目标字段都是 private [SerializeField]，只能走 SerializedObject 改。
        // 每个都判空：字段被改名时只跳过，不抛异常，免得整个配置中断在半路。

        private static void SetObject(SerializedObject so, string fieldName, Object value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.objectReferenceValue = value;
            }
            else
            {
                Debug.LogWarning($"[BuildingGridSetupWizard] {so.targetObject.GetType().Name} 上找不到字段 " +
                                 $"{fieldName}，该项跳过。");
            }
        }

        /// <summary>只在字段为空时写入，避免重复运行覆盖用户已经手动指定的引用。</summary>
        private static void SetObjectIfEmpty(SerializedObject so, string fieldName, Object value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogWarning($"[BuildingGridSetupWizard] {so.targetObject.GetType().Name} 上找不到字段 " +
                                 $"{fieldName}，该项跳过。");
                return;
            }

            if (property.objectReferenceValue == null && value != null)
            {
                property.objectReferenceValue = value;
            }
        }

        private static void SetFloat(SerializedObject so, string fieldName, float value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.floatValue = value;
            }
        }

        private static void SetBool(SerializedObject so, string fieldName, bool value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static void SetVector3(SerializedObject so, string fieldName, Vector3 value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.vector3Value = value;
            }
        }

        private static void SetVector2Int(SerializedObject so, string fieldName, Vector2Int value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.vector2IntValue = value;
            }
        }

        private static void Fail(string message)
        {
            EditorUtility.DisplayDialog("建筑网格配置未执行", message, "知道了");
            Debug.LogError("[BuildingGridSetupWizard] " + message);
        }
    }
}
