using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Core;
using Yujian.Mining;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 阶段 11 一键配置：在岛上放一个可点击的矿洞入口。
    ///
    /// 做四件事：
    ///   1. 找不到就创建「矿洞入口」根物体，并生成一套占位矿山（美术可随时替换）；
    ///   2. 加 BoxCollider，让鼠标 / 手指能点到；
    ///   3. 给 GameAreaSystem 加 MiningEntranceRouter，接好点击动作与相机；
    ///   4. 保存场景。
    ///
    /// 不直接改 .unity / .prefab，由 Unity 自己写盘（项目规则 13）。
    /// 入口：菜单栏 → 屿见 → 挖矿 → 配置矿洞入口（阶段 11）
    /// </summary>
    public static class MiningEntranceSetupWizard
    {
        private const string RootName = "矿洞入口";
        private const string ModelName = "Model";
        private const string MaterialFolder = "Assets/Materials/Mining";
        private const string RockMaterialPath = "Assets/Materials/Island/Island_Rock.mat";
        private const string DarkMaterialPath = "Assets/Materials/Mining/MineEntrance_Dark.mat";
        private const string MarkerMaterialPath = "Assets/Materials/Mining/MineEntrance_Marker.mat";

        [MenuItem("屿见/挖矿/配置矿洞入口（阶段 11）", false, 340)]
        public static void Configure()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("矿洞入口", "请先退出 Play Mode，再运行本命令。", "知道了");
                return;
            }

            GameAreaController areaController =
                Object.FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);
            MiningInput miningInput =
                Object.FindFirstObjectByType<MiningInput>(FindObjectsInactive.Include);

            if (areaController == null || miningInput == null)
            {
                Debug.LogError("[矿洞入口] 场景里找不到 GameAreaController 或 MiningInput。" +
                               "请先打开 SampleScene，并确认阶段 6 的整合已经配置过。");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("配置矿洞入口");

            MiningEntrance entrance =
                Object.FindFirstObjectByType<MiningEntrance>(FindObjectsInactive.Include);

            GameObject root;
            bool created = false;

            if (entrance != null)
            {
                root = entrance.gameObject;
            }
            else
            {
                root = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(root, "创建矿洞入口");
                root.transform.position = ResolveDefaultPosition();
                entrance = Undo.AddComponent<MiningEntrance>(root);
                created = true;
            }

            // 旧版向导把入口放在矿石旁边。检测到这种位置就搬到岛上的独立位置，
            // 让「点矿山 → 进矿洞」在画面上说得通。
            if (!created && IsNearOre(root.transform.position))
            {
                root.transform.position = ResolveDefaultPosition();
                Debug.Log("[矿洞入口] 检测到入口仍在矿石旁，已移动到岛上的独立位置。", root);
            }

            if (root.GetComponent<BoxCollider>() == null)
            {
                BoxCollider box = Undo.AddComponent<BoxCollider>(root);
                box.center = new Vector3(0f, 1.9f, 1.2f);
                box.size = new Vector3(8.4f, 4.6f, 5.2f);
            }

            BuildPlaceholderModel(root.transform);
            entrance.ConfigureForSetup(areaController);
            EditorUtility.SetDirty(entrance);

            // 矿石只在矿洞里出现：观察模式下岛上只看得到矿山，点进去才看到矿石。
            Ore ore = Object.FindFirstObjectByType<Ore>(FindObjectsInactive.Include);

            if (ore != null)
            {
                SerializedObject oreSo = new SerializedObject(ore);
                SerializedProperty oreVisible = oreSo.FindProperty("onlyVisibleInMiningArea");

                if (oreVisible != null)
                {
                    oreVisible.boolValue = true;
                    oreSo.ApplyModifiedProperties();
                }

                EditorUtility.SetDirty(ore);
            }

            MiningEntranceRouter router = areaController.GetComponent<MiningEntranceRouter>();

            if (router == null)
            {
                router = Undo.AddComponent<MiningEntranceRouter>(areaController.gameObject);
            }

            // 相机与 MiningInput 保持一致（Main Camera）。点击动作由 Router 自己创建，
            // 不共用 MiningInput 的 action，避免离开矿区时被一起关掉。
            SerializedObject inputSo = new SerializedObject(miningInput);
            SerializedProperty cameraProp = inputSo.FindProperty("viewCamera");

            SerializedObject routerSo = new SerializedObject(router);
            SerializedProperty routerCamera = routerSo.FindProperty("viewCamera");
            SerializedProperty routerArea = routerSo.FindProperty("areaController");

            if (routerCamera != null && cameraProp != null)
            {
                routerCamera.objectReferenceValue = cameraProp.objectReferenceValue;
            }

            if (routerArea != null)
            {
                routerArea.objectReferenceValue = areaController;
            }

            routerSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(router);

            if (routerCamera != null && routerCamera.objectReferenceValue == null)
            {
                Debug.LogWarning("[矿洞入口] 没有读到 MiningInput 的 View Camera，请手动把 Main Camera " +
                                 "拖到 GameAreaSystem 的 MiningEntranceRouter 上。");
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveOpenScenes();
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log((created ? "[矿洞入口] 已创建「" : "[矿洞入口] 已更新「") + RootName +
                      "」，位置 " + root.transform.position +
                      "。观察模式下点击矿山即可进入挖矿区。" +
                      "美术换模型时，替换 Model 下的占位物体，然后右键 MiningEntrance 组件执行" +
                      "「按子物体重新适配碰撞框」。", root);
        }

        private static Vector3 ResolveDefaultPosition()
        {
            GameObject island = GameObject.Find("Island");
            Vector3 center = island != null ? island.transform.position : Vector3.zero;

            // 岛中心偏左前方：从观察机位（岛南侧上方）看过去在画面里，
            // 又远离矿石点（矿石在岛西侧），不会和挖矿视角混在一起。
            Vector3 candidate = center + new Vector3(-4f, 0f, -26f);
            return new Vector3(candidate.x, SampleGroundHeight(candidate, center.y), candidate.z);
        }

        /// <summary>从高处向下打一条射线，找岛面高度；找不到就退回岛屿原点高度。</summary>
        private static float SampleGroundHeight(Vector3 position, float fallbackY)
        {
            Vector3 origin = new Vector3(position.x, 200f, position.z);

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 400f, ~0,
                    QueryTriggerInteraction.Ignore))
            {
                return hit.point.y;
            }

            return fallbackY;
        }

        private static bool IsNearOre(Vector3 position)
        {
            Ore ore = Object.FindFirstObjectByType<Ore>(FindObjectsInactive.Include);

            if (ore == null)
            {
                return false;
            }

            return Vector3.Distance(position, ore.transform.position) < 8f;
        }

        private static void BuildPlaceholderModel(Transform root)
        {
            Transform model = root.Find(ModelName);

            if (model == null)
            {
                GameObject modelObject = new GameObject(ModelName);
                Undo.RegisterCreatedObjectUndo(modelObject, "创建矿洞占位模型");
                modelObject.transform.SetParent(root, false);
                model = modelObject.transform;
            }

            if (model.childCount > 0)
            {
                // 已经有模型了（占位或美术版），不覆盖
                return;
            }

            Material rock = LoadOrCreateMaterial(
                RockMaterialPath, "Universal Render Pipeline/Lit",
                new Color(0.48f, 0.44f, 0.40f, 1f), 0.05f);
            Material dark = LoadOrCreateMaterial(
                DarkMaterialPath, "Universal Render Pipeline/Lit",
                new Color(0.03f, 0.03f, 0.035f, 1f), 0f);
            Material marker = LoadOrCreateMaterial(
                MarkerMaterialPath, "Universal Render Pipeline/Unlit",
                new Color(1f, 0.78f, 0.25f, 1f), 0f);

            AddPrimitive(model, PrimitiveType.Sphere, "Mound_Left",
                new Vector3(-2.4f, 1.5f, 3.3f), new Vector3(4.4f, 3.2f, 3.4f),
                Quaternion.Euler(0f, 0f, 12f), rock);
            AddPrimitive(model, PrimitiveType.Sphere, "Mound_Right",
                new Vector3(2.4f, 1.4f, 3.2f), new Vector3(4.2f, 3.0f, 3.2f),
                Quaternion.Euler(0f, 0f, -14f), rock);
            AddPrimitive(model, PrimitiveType.Sphere, "Mound_Top",
                new Vector3(0f, 3.2f, 3.4f), new Vector3(4.8f, 3.2f, 3.2f),
                Quaternion.identity, rock);

            AddPrimitive(model, PrimitiveType.Cube, "Opening",
                new Vector3(0f, 1.55f, 1.35f), new Vector3(2.7f, 3.0f, 0.7f),
                Quaternion.identity, dark);
            AddPrimitive(model, PrimitiveType.Cube, "Pillar_Left",
                new Vector3(-1.75f, 1.6f, 0f), new Vector3(0.7f, 3.2f, 0.8f),
                Quaternion.identity, rock);
            AddPrimitive(model, PrimitiveType.Cube, "Pillar_Right",
                new Vector3(1.75f, 1.6f, 0f), new Vector3(0.7f, 3.2f, 0.8f),
                Quaternion.identity, rock);
            AddPrimitive(model, PrimitiveType.Cube, "Lintel",
                new Vector3(0f, 3.35f, 0f), new Vector3(4.3f, 0.8f, 0.9f),
                Quaternion.identity, rock);
            AddPrimitive(model, PrimitiveType.Cube, "Marker",
                new Vector3(0f, 4.9f, 0.4f), new Vector3(0.55f, 0.55f, 0.55f),
                Quaternion.Euler(45f, 45f, 0f), marker);
        }

        private static void AddPrimitive(Transform parent, PrimitiveType type, string objectName,
            Vector3 localPosition, Vector3 localScale, Quaternion localRotation, Material material)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = objectName;
            Undo.RegisterCreatedObjectUndo(primitive, "创建矿洞占位模型");
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = localPosition;
            primitive.transform.localRotation = localRotation;
            primitive.transform.localScale = localScale;

            Collider collider = primitive.GetComponent<Collider>();

            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            Renderer renderer = primitive.GetComponent<Renderer>();

            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static Material LoadOrCreateMaterial(string path, string shaderName, Color color, float smoothness)
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find(shaderName);

            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                return null;
            }

            EnsureFolder(MaterialFolder);

            Material material = new Material(shader);
            material.name = Path.GetFileNameWithoutExtension(path);

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            string folderName = Path.GetFileName(folder);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}