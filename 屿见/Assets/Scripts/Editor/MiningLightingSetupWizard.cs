using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Core;
using Yujian.Lighting;
using Yujian.Mining;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 阶段 12 一键配置：矿洞照明。
    ///
    /// 做四件事：
    ///   1. 在矿石旁生成一盏矿灯（灯柱 + 发光灯泡 + 主光）；
    ///   2. 再加补光与轮廓光，让矿石在黑暗里仍有层次；
    ///   3. 给「矿洞灯光」根物体加 MiningLightingController，接好区域、太阳和相机；
    ///   4. 保存场景。
    ///
    /// 不直接改 .unity，由 Unity 自己写盘（项目规则 13）。
    /// 入口：菜单栏 → 屿见 → 挖矿 → 配置矿洞照明（阶段 12）
    /// </summary>
    public static class MiningLightingSetupWizard
    {
        private const string RootName = "矿洞灯光";
        private const string MetalMaterialPath = "Assets/Materials/Mining/MineLantern_Metal.mat";
        private const string GlowMaterialPath = "Assets/Materials/Mining/MineLantern_Glow.mat";
        private const string CaveRockMaterialPath = "Assets/Materials/Mining/MineCave_Rock.mat";

        [MenuItem("屿见/挖矿/配置矿洞照明（阶段 12）", false, 350)]
        public static void Configure()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("矿洞照明", "请先退出 Play Mode，再运行本命令。", "知道了");
                return;
            }

            GameAreaController areaController =
                Object.FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);
            MiningInput miningInput =
                Object.FindFirstObjectByType<MiningInput>(FindObjectsInactive.Include);
            Ore ore = Object.FindFirstObjectByType<Ore>(FindObjectsInactive.Include);

            if (areaController == null)
            {
                Debug.LogError("[矿洞照明] 场景里找不到 GameAreaController，" +
                               "请先打开 SampleScene 并确认阶段 6 已经配置过。");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("配置矿洞照明");

            MiningLightingController controller =
                Object.FindFirstObjectByType<MiningLightingController>(FindObjectsInactive.Include);

            GameObject root;
            bool created = false;

            if (controller != null)
            {
                root = controller.gameObject;
            }
            else
            {
                Vector3 position = ore != null
                    ? new Vector3(ore.transform.position.x, ore.transform.position.y - 0.6f,
                        ore.transform.position.z)
                    : Vector3.zero;

                root = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(root, "创建矿洞灯光");
                root.transform.position = position;

                controller = Undo.AddComponent<MiningLightingController>(root);
                created = true;
            }

            EnsureLanternRig(root.transform);
            EnsureCaveShell(root.transform);

            Camera viewCamera = null;

            if (miningInput != null)
            {
                SerializedObject inputSo = new SerializedObject(miningInput);
                SerializedProperty cameraProp = inputSo.FindProperty("viewCamera");

                if (cameraProp != null)
                {
                    viewCamera = cameraProp.objectReferenceValue as Camera;
                }
            }

            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }

            Light sunLight = FindSunLight();

            SerializedObject controllerSo = new SerializedObject(controller);
            SerializedProperty areaProp = controllerSo.FindProperty("areaController");
            SerializedProperty sunProp = controllerSo.FindProperty("sunLight");
            SerializedProperty cameraRefProp = controllerSo.FindProperty("viewCamera");

            if (areaProp != null)
            {
                areaProp.objectReferenceValue = areaController;
            }

            if (sunProp != null)
            {
                sunProp.objectReferenceValue = sunLight;
            }

            if (cameraRefProp != null)
            {
                cameraRefProp.objectReferenceValue = viewCamera;
            }

            controllerSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(controller);

            if (sunLight == null)
            {
                Debug.LogWarning("[矿洞照明] 场景里没找到平行光（Directional Light），" +
                                 "进矿洞后太阳不会变暗，但矿灯仍会亮。");
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveOpenScenes();
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log((created ? "[矿洞照明] 已创建「" : "[矿洞照明] 已更新「") + RootName +
                      "」，位置 " + root.transform.position +
                      "。进入挖矿区后太阳变暗、背景变黑、矿灯亮起；离开后恢复白天。");
        }

        private static void BuildLightRig(Transform root)
        {
            Material metal = LoadOrCreateMaterial(MetalMaterialPath,
                new Color(0.16f, 0.15f, 0.14f, 1f), 0.45f, 0.75f, false);
            Material glow = LoadOrCreateMaterial(GlowMaterialPath,
                new Color(1f, 0.82f, 0.45f, 1f), 0f, 0f, true);

            // 灯笼：立在矿石与相机之间，主光就放在灯泡位置
            GameObject lantern = new GameObject("Lantern");
            Undo.RegisterCreatedObjectUndo(lantern, "创建矿灯");
            lantern.transform.SetParent(root, false);
            // 放在矿石右后方，避免灯柱挡在相机与矿石之间
            lantern.transform.localPosition = new Vector3(1.8f, 0f, 0.6f);

            AddPrimitive(lantern.transform, PrimitiveType.Cylinder, "Post",
                new Vector3(0f, 1.1f, 0f), new Vector3(0.12f, 1.1f, 0.12f),
                Quaternion.identity, metal);
            AddPrimitive(lantern.transform, PrimitiveType.Cube, "Cap",
                new Vector3(0f, 2.5f, 0f), new Vector3(0.5f, 0.1f, 0.5f),
                Quaternion.identity, metal);
            GameObject glowObject = AddPrimitive(lantern.transform, PrimitiveType.Cube, "Glow",
                new Vector3(0f, 2.3f, 0f), new Vector3(0.38f, 0.38f, 0.38f),
                Quaternion.Euler(0f, 45f, 0f), glow);

            // 灯泡自己不投影，否则会把主光挡在灯罩里
            Renderer glowRenderer = glowObject.GetComponent<Renderer>();

            if (glowRenderer != null)
            {
                glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                glowRenderer.receiveShadows = false;
            }

            // 主光放在灯泡上方，避免被灯柱 / 灯罩包住
            CreatePointLight(lantern.transform, "Key Light", new Vector3(0f, 3.2f, 0f),
                new Color(1f, 0.78f, 0.5f, 1f), 5f, 10f, LightShadows.Soft);
            CreatePointLight(root, "Fill Light", new Vector3(-3.5f, 1.8f, 1.0f),
                new Color(0.45f, 0.6f, 0.9f, 1f), 1.5f, 10f, LightShadows.None);
            CreatePointLight(root, "Rim Light", new Vector3(3.0f, 2.2f, 2.2f),
                new Color(0.95f, 0.55f, 0.3f, 1f), 2.5f, 8f, LightShadows.None);
        }

        /// <summary>
        /// 灯架已存在时做增量修正：旧版本的灯柱可能挡镜头、主光可能被灯泡包住。
        /// 已经正确摆放的不动，避免覆盖手动调整。
        /// </summary>
        private static void EnsureLanternRig(Transform root)
        {
            Transform lantern = root.Find("Lantern");

            if (lantern == null)
            {
                BuildLightRig(root);
                return;
            }

            if (lantern.localPosition.z < -1f)
            {
                lantern.localPosition = new Vector3(1.8f, 0f, 0.6f);
            }

            Transform keyLight = lantern.Find("Key Light");

            if (keyLight != null && keyLight.localPosition.y < 3f)
            {
                keyLight.localPosition = new Vector3(0f, 3.2f, 0f);
            }

            Transform glow = lantern.Find("Glow");

            if (glow != null)
            {
                Renderer glowRenderer = glow.GetComponent<Renderer>();

                if (glowRenderer != null)
                {
                    glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    glowRenderer.receiveShadows = false;
                }
            }
        }

        /// <summary>
        /// 围住挖矿相机的矿洞盒子：后墙、左右墙、顶板，再放几块碎石。
        /// 没有底板——岛面本身就是矿洞地面，避免地面和地形互相穿插。
        /// </summary>
        private static void EnsureCaveShell(Transform root)
        {
            if (root.Find("Cave") != null)
            {
                return;
            }

            Material rock = LoadOrCreateMaterial(CaveRockMaterialPath,
                new Color(0.30f, 0.27f, 0.24f, 1f), 0.05f, 0f, false);

            GameObject cave = new GameObject("Cave");
            Undo.RegisterCreatedObjectUndo(cave, "创建矿洞墙体");
            cave.transform.SetParent(root, false);

            AddPrimitive(cave.transform, PrimitiveType.Cube, "BackWall",
                new Vector3(0f, 3.2f, 5.8f), new Vector3(20f, 9f, 0.6f),
                Quaternion.identity, rock);
            AddPrimitive(cave.transform, PrimitiveType.Cube, "LeftWall",
                new Vector3(-8.5f, 3.2f, 1.5f), new Vector3(0.6f, 9f, 9.4f),
                Quaternion.identity, rock);
            AddPrimitive(cave.transform, PrimitiveType.Cube, "RightWall",
                new Vector3(8.5f, 3.2f, 1.5f), new Vector3(0.6f, 9f, 9.4f),
                Quaternion.identity, rock);
            AddPrimitive(cave.transform, PrimitiveType.Cube, "Ceiling",
                new Vector3(0f, 6.0f, 1.5f), new Vector3(17.6f, 0.6f, 9.4f),
                Quaternion.identity, rock);

            // 墙脚碎石，让盒子看起来不那么像方盒子
            AddPrimitive(cave.transform, PrimitiveType.Sphere, "Rock_A",
                new Vector3(-5.2f, 0.7f, 5.1f), new Vector3(3.0f, 2.0f, 1.8f),
                Quaternion.Euler(0f, 15f, 8f), rock);
            AddPrimitive(cave.transform, PrimitiveType.Sphere, "Rock_B",
                new Vector3(-1.8f, 0.5f, 5.2f), new Vector3(2.4f, 1.5f, 1.6f),
                Quaternion.Euler(0f, -20f, -6f), rock);
            AddPrimitive(cave.transform, PrimitiveType.Sphere, "Rock_C",
                new Vector3(4.6f, 0.6f, 5.0f), new Vector3(2.8f, 1.8f, 1.7f),
                Quaternion.Euler(0f, 30f, 10f), rock);
            AddPrimitive(cave.transform, PrimitiveType.Sphere, "Rock_D",
                new Vector3(7.2f, 0.8f, 2.0f), new Vector3(2.2f, 2.4f, 2.0f),
                Quaternion.Euler(0f, 0f, -12f), rock);
        }

        private static Light CreatePointLight(Transform parent, string objectName, Vector3 localPosition,
            Color color, float intensity, float range, LightShadows shadows)
        {
            GameObject lightObject = new GameObject(objectName);
            Undo.RegisterCreatedObjectUndo(lightObject, "创建矿灯");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.localPosition = localPosition;

            Light light = Undo.AddComponent<Light>(lightObject);
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = shadows;
            light.shadowStrength = 0.8f;
            return light;
        }

        private static GameObject AddPrimitive(Transform parent, PrimitiveType type, string objectName,
            Vector3 localPosition, Vector3 localScale, Quaternion localRotation, Material material)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = objectName;
            Undo.RegisterCreatedObjectUndo(primitive, "创建矿灯模型");
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

            return primitive;
        }

        private static Material LoadOrCreateMaterial(string path, Color color,
            float smoothness, float metallic, bool unlit)
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find(unlit
                ? "Universal Render Pipeline/Unlit"
                : "Universal Render Pipeline/Lit");

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

            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

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

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
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

        private static Light FindSunLight()
        {
            Light[] lights = Object.FindObjectsByType<Light>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].type == LightType.Directional)
                {
                    return lights[i];
                }
            }

            return null;
        }
    }
}