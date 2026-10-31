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
    /// 阶段 13 一键配置：矿洞第一人称手持提灯与挖矿工具。
    ///
    /// 做五件事：
    ///   1. 在主相机下创建「矿洞手持道具」，左手提灯、右手镐；
    ///   2. 给提灯加一盏暖色点光，并接好区域控制器与相机；
    ///   3. 删除旧的落地灯柱（阶段 12 生成在「矿洞灯光/Lantern」）；
    ///   4. 把补光 / 轮廓光调低，只做环境补充；
    ///   5. 保存场景。
    ///
    /// 不直接改 .unity，由 Unity 自己写盘（项目规则 13）。
    /// 入口：菜单栏 → 屿见 → 挖矿 → 配置手持提灯与挖矿工具（阶段 13）
    /// </summary>
    public static class MiningHandheldSetupWizard
    {
        private const string RootName = "矿洞手持道具";
        private const int IgnoreRaycastLayer = 2;
        private const string MetalMaterialPath = "Assets/Materials/Mining/HandLantern_Metal.mat";
        private const string GlowMaterialPath = "Assets/Materials/Mining/HandLantern_Glow.mat";
        private const string CoreMaterialPath = "Assets/Materials/Mining/HandLantern_Core.mat";
        private const string WoodMaterialPath = "Assets/Materials/Mining/HandTool_Wood.mat";
        private const string ToolMetalMaterialPath = "Assets/Materials/Mining/HandTool_Metal.mat";
        private const string GloveMaterialPath = "Assets/Materials/Mining/Hand_Glove.mat";
        private const string SleeveMaterialPath = "Assets/Materials/Mining/Hand_Sleeve.mat";

        [MenuItem("屿见/挖矿/配置手持提灯与挖矿工具（阶段 13）", false, 351)]
        public static void Configure()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("手持提灯", "请先退出 Play Mode，再运行本命令。", "知道了");
                return;
            }

            GameAreaController areaController =
                Object.FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);
            MiningInput miningInput =
                Object.FindFirstObjectByType<MiningInput>(FindObjectsInactive.Include);

            if (areaController == null)
            {
                Debug.LogError("[手持提灯] 场景里找不到 GameAreaController，" +
                               "请先打开 SampleScene 并确认阶段 6 已经配置过。");
                return;
            }

            Camera viewCamera = ResolveCamera(miningInput);

            if (viewCamera == null)
            {
                Debug.LogError("[手持提灯] 找不到主相机（Main Camera），无法把道具挂到镜头下。");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("配置手持提灯与挖矿工具");

            GameObject root = FindOrCreateRoot(viewCamera);
            MiningHandheldRig rig = root.GetComponent<MiningHandheldRig>();

            if (rig == null)
            {
                rig = Undo.AddComponent<MiningHandheldRig>(root);
            }

            EnsureLantern(root.transform);
            EnsureTool(root.transform);
            RemoveGroundLantern();

            Transform lanternPivot = root.transform.Find("LanternPivot");
            Transform toolPivot = root.transform.Find("ToolPivot");
            Light handLight = lanternPivot != null
                ? lanternPivot.Find("Lantern Light")?.GetComponent<Light>()
                : null;
            Transform lanternGlow = lanternPivot != null ? lanternPivot.Find("Glow") : null;

            if (handLight == null)
            {
                Debug.LogError("[手持提灯] 提灯点光没有创建成功，请检查 Console 是否有材质或几何体报错。");
            }

            rig.ConfigureForSetup(areaController, viewCamera, lanternPivot, toolPivot,
                handLight, lanternGlow);

            SerializedObject rigSo = new SerializedObject(rig);
            SetObjectReference(rigSo, "areaController", areaController);
            SetObjectReference(rigSo, "viewCamera", viewCamera);
            SetObjectReference(rigSo, "lanternPivot", lanternPivot);
            SetObjectReference(rigSo, "toolPivot", toolPivot);
            SetObjectReference(rigSo, "handLight", handLight);
            SetObjectReference(rigSo, "lanternGlow", lanternGlow);
            rigSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rig);

            SetLayerRecursively(root, IgnoreRaycastLayer);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveOpenScenes();
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log("[手持提灯] 已配置「" + RootName + "」：挂在 " + viewCamera.name +
                      " 下，进入挖矿区后左手提灯、右手镐出现；旧的落地灯柱已移除，" +
                      "补光与轮廓光已调低。");
        }

        private static Camera ResolveCamera(MiningInput miningInput)
        {
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

            return viewCamera;
        }

        private static GameObject FindOrCreateRoot(Camera viewCamera)
        {
            MiningHandheldRig existing =
                Object.FindFirstObjectByType<MiningHandheldRig>(FindObjectsInactive.Include);

            if (existing != null)
            {
                GameObject existingRoot = existing.gameObject;

                if (existingRoot.transform.parent != viewCamera.transform)
                {
                    Undo.SetTransformParent(existingRoot.transform, viewCamera.transform,
                        "把手持道具挂到主相机");
                    existingRoot.transform.localPosition = Vector3.zero;
                    existingRoot.transform.localRotation = Quaternion.identity;
                }

                return existingRoot;
            }

            GameObject root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "创建矿洞手持道具");
            root.transform.SetParent(viewCamera.transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            return root;
        }

        private static void EnsureLantern(Transform root)
        {
            Transform existing = root.Find("LanternPivot");

            if (existing != null && existing.Find("Lantern Light") != null &&
                existing.Find("Glow") != null)
            {
                return;
            }

            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            Material metal = LoadOrCreateMaterial(MetalMaterialPath,
                new Color(0.20f, 0.17f, 0.12f, 1f), 0.5f, 0.8f, false);
            Material glow = LoadOrCreateMaterial(GlowMaterialPath,
                new Color(1f, 0.74f, 0.36f, 1f), 0f, 0f, true);
            Material core = LoadOrCreateMaterial(CoreMaterialPath,
                new Color(1f, 0.93f, 0.68f, 1f), 0f, 0f, true);
            Material glove = LoadOrCreateMaterial(GloveMaterialPath,
                new Color(0.42f, 0.29f, 0.20f, 1f), 0.2f, 0f, false);
            Material sleeve = LoadOrCreateMaterial(SleeveMaterialPath,
                new Color(0.22f, 0.36f, 0.38f, 1f), 0.15f, 0f, false);

            GameObject pivot = new GameObject("LanternPivot");
            Undo.RegisterCreatedObjectUndo(pivot, "创建手提灯");
            pivot.transform.SetParent(root, false);
            pivot.transform.localPosition = DefaultAnchor(new Vector2(-0.56f, -0.92f), 0.92f);
            pivot.transform.localRotation = Quaternion.Euler(0f, 18f, -8f);

            AddPrimitive(pivot.transform, PrimitiveType.Cylinder, "Glass",
                Vector3.zero, new Vector3(0.085f, 0.10f, 0.085f),
                Quaternion.identity, glow);
            GameObject glowObject = AddPrimitive(pivot.transform, PrimitiveType.Sphere, "Glow",
                Vector3.zero, new Vector3(0.045f, 0.065f, 0.045f),
                Quaternion.identity, core);
            AddPrimitive(pivot.transform, PrimitiveType.Cylinder, "TopCap",
                new Vector3(0f, 0.108f, 0f), new Vector3(0.108f, 0.014f, 0.108f),
                Quaternion.identity, metal);
            AddPrimitive(pivot.transform, PrimitiveType.Cylinder, "BaseCap",
                new Vector3(0f, -0.108f, 0f), new Vector3(0.112f, 0.016f, 0.112f),
                Quaternion.identity, metal);

            AddPrimitive(pivot.transform, PrimitiveType.Cube, "Frame_A",
                new Vector3(0.086f, 0f, 0f), new Vector3(0.012f, 0.21f, 0.012f),
                Quaternion.identity, metal);
            AddPrimitive(pivot.transform, PrimitiveType.Cube, "Frame_B",
                new Vector3(-0.086f, 0f, 0f), new Vector3(0.012f, 0.21f, 0.012f),
                Quaternion.identity, metal);
            AddPrimitive(pivot.transform, PrimitiveType.Cube, "Frame_C",
                new Vector3(0f, 0f, 0.086f), new Vector3(0.012f, 0.21f, 0.012f),
                Quaternion.identity, metal);
            AddPrimitive(pivot.transform, PrimitiveType.Cube, "Frame_D",
                new Vector3(0f, 0f, -0.086f), new Vector3(0.012f, 0.21f, 0.012f),
                Quaternion.identity, metal);

            AddPrimitive(pivot.transform, PrimitiveType.Cube, "Handle_Left",
                new Vector3(-0.072f, 0.245f, 0f), new Vector3(0.013f, 0.085f, 0.013f),
                Quaternion.identity, metal);
            AddPrimitive(pivot.transform, PrimitiveType.Cube, "Handle_Right",
                new Vector3(0.072f, 0.245f, 0f), new Vector3(0.013f, 0.085f, 0.013f),
                Quaternion.identity, metal);
            AddPrimitive(pivot.transform, PrimitiveType.Cube, "Handle_Top",
                new Vector3(0f, 0.292f, 0f), new Vector3(0.158f, 0.014f, 0.014f),
                Quaternion.identity, metal);

            AddPrimitive(pivot.transform, PrimitiveType.Sphere, "LeftHand",
                new Vector3(0f, 0.292f, -0.012f), new Vector3(0.082f, 0.07f, 0.082f),
                Quaternion.identity, glove);
            AddPrimitive(pivot.transform, PrimitiveType.Capsule, "LeftForearm",
                new Vector3(-0.11f, 0.08f, -0.005f), new Vector3(0.075f, 0.19f, 0.075f),
                Quaternion.Euler(0f, 0f, -34f), sleeve);

            GameObject lightObject = new GameObject("Lantern Light");
            Undo.RegisterCreatedObjectUndo(lightObject, "创建手提灯光");
            lightObject.transform.SetParent(pivot.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.02f, -0.05f);

            Light light = Undo.AddComponent<Light>(lightObject);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.76f, 0.45f, 1f);
            light.intensity = 3.8f;
            light.range = 12f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.7f;

            Renderer glowRenderer = glowObject.GetComponent<Renderer>();

            if (glowRenderer != null)
            {
                glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                glowRenderer.receiveShadows = false;
            }
        }

        private static void EnsureTool(Transform root)
        {
            Transform existing = root.Find("ToolPivot");

            if (existing != null && existing.Find("Shaft") != null)
            {
                return;
            }

            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            Material wood = LoadOrCreateMaterial(WoodMaterialPath,
                new Color(0.34f, 0.22f, 0.13f, 1f), 0.2f, 0f, false);
            Material metal = LoadOrCreateMaterial(ToolMetalMaterialPath,
                new Color(0.52f, 0.55f, 0.60f, 1f), 0.7f, 0.9f, false);
            Material glove = LoadOrCreateMaterial(GloveMaterialPath,
                new Color(0.42f, 0.29f, 0.20f, 1f), 0.2f, 0f, false);
            Material sleeve = LoadOrCreateMaterial(SleeveMaterialPath,
                new Color(0.22f, 0.36f, 0.38f, 1f), 0.15f, 0f, false);

            GameObject pivot = new GameObject("ToolPivot");
            Undo.RegisterCreatedObjectUndo(pivot, "创建挖矿工具");
            pivot.transform.SetParent(root, false);
            pivot.transform.localPosition = DefaultAnchor(new Vector2(0.60f, -0.96f), 0.94f);
            pivot.transform.localRotation = Quaternion.Euler(0f, -14f, -8f);

            AddPrimitive(pivot.transform, PrimitiveType.Cylinder, "Shaft",
                Vector3.zero, new Vector3(0.028f, 0.32f, 0.028f),
                Quaternion.identity, wood);
            AddPrimitive(pivot.transform, PrimitiveType.Cylinder, "Grip",
                new Vector3(0f, -0.16f, 0f), new Vector3(0.033f, 0.055f, 0.033f),
                Quaternion.identity, glove);
            AddPrimitive(pivot.transform, PrimitiveType.Cube, "Head",
                new Vector3(0f, 0.305f, 0f), new Vector3(0.23f, 0.055f, 0.06f),
                Quaternion.identity, metal);
            AddPrimitive(pivot.transform, PrimitiveType.Cube, "Pick_Left",
                new Vector3(-0.145f, 0.295f, 0f), new Vector3(0.11f, 0.032f, 0.045f),
                Quaternion.Euler(0f, 0f, -18f), metal);
            AddPrimitive(pivot.transform, PrimitiveType.Cube, "Pick_Right",
                new Vector3(0.145f, 0.295f, 0f), new Vector3(0.11f, 0.032f, 0.045f),
                Quaternion.Euler(0f, 0f, 18f), metal);
            AddPrimitive(pivot.transform, PrimitiveType.Cylinder, "HeadBand",
                new Vector3(0f, 0.275f, 0f), new Vector3(0.038f, 0.018f, 0.038f),
                Quaternion.identity, metal);

            AddPrimitive(pivot.transform, PrimitiveType.Sphere, "RightHand",
                new Vector3(0f, -0.155f, -0.012f), new Vector3(0.082f, 0.075f, 0.082f),
                Quaternion.identity, glove);
            AddPrimitive(pivot.transform, PrimitiveType.Capsule, "RightForearm",
                new Vector3(0.075f, -0.255f, -0.005f), new Vector3(0.075f, 0.165f, 0.075f),
                Quaternion.Euler(0f, 0f, 35f), sleeve);
        }

        private static Vector3 DefaultAnchor(Vector2 anchor, float distance)
        {
            float halfHeight = Mathf.Tan(30f * Mathf.Deg2Rad) * distance;
            float halfWidth = halfHeight * (16f / 9f);
            return new Vector3(anchor.x * halfWidth, anchor.y * halfHeight, distance);
        }

        private static void RemoveGroundLantern()
        {
            MiningLightingController lighting =
                Object.FindFirstObjectByType<MiningLightingController>(FindObjectsInactive.Include);

            if (lighting == null)
            {
                return;
            }

            Transform oldLantern = lighting.transform.Find("Lantern");

            if (oldLantern != null)
            {
                Undo.DestroyObjectImmediate(oldLantern.gameObject);
            }

            SetLightIntensity(lighting.transform.Find("Fill Light"), 0.8f);
            SetLightIntensity(lighting.transform.Find("Rim Light"), 1.0f);
        }

        private static void SetLightIntensity(Transform target, float intensity)
        {
            if (target == null)
            {
                return;
            }

            Light light = target.GetComponent<Light>();

            if (light != null)
            {
                light.intensity = intensity;
                EditorUtility.SetDirty(light);
            }
        }

        private static void SetObjectReference(SerializedObject serializedObject, string fieldName,
            Object value)
        {
            SerializedProperty property = serializedObject.FindProperty(fieldName);

            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }

        private static void SetLayerRecursively(GameObject target, int layer)
        {
            target.layer = layer;

            for (int i = 0; i < target.transform.childCount; i++)
            {
                SetLayerRecursively(target.transform.GetChild(i).gameObject, layer);
            }
        }

        private static GameObject AddPrimitive(Transform parent, PrimitiveType type, string objectName,
            Vector3 localPosition, Vector3 localScale, Quaternion localRotation, Material material)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = objectName;
            Undo.RegisterCreatedObjectUndo(primitive, "创建手持道具模型");
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = localPosition;
            primitive.transform.localRotation = localRotation;
            primitive.transform.localScale = localScale;
            primitive.layer = IgnoreRaycastLayer;

            Collider collider = primitive.GetComponent<Collider>();

            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            Renderer renderer = primitive.GetComponent<Renderer>();

            if (renderer != null)
            {
                if (material != null)
                {
                    renderer.sharedMaterial = material;
                }

                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
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
    }
}