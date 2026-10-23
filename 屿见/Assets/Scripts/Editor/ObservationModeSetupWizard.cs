using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using Yujian.Building;
using Yujian.Core;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 观察模式的一键接线工具：给 GameAreaSystem 挂 ObservationCameraController，
    /// 接好区域 / 建造导演 / 相机 / 四个输入动作，并把开局固定为观察模式。
    ///
    /// 为什么单独做一个向导：
    /// 阶段 3 的 BuildingSetupWizard 重跑时会覆盖建造机位、重建 BuildingPanel；
    /// 阶段 6 的 MvpSetupWizard 又会重建底部导航。本工具只做增量接线，
    /// 可以独立、安全地重复运行。
    ///
    /// 运行入口：菜单栏 → 屿见 → 场景 → 配置观察模式系统
    /// </summary>
    public static class ObservationModeSetupWizard
    {
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

        // 与 AreaCameraDirector 里的默认值保持一致：海岛中心 (10.21735, 0, -9.49205)，
        // 距离 130、俯仰 55°。只在字段还是零值时写入，避免覆盖用户记录过的机位。
        private static readonly Vector3 DefaultObservationPosition = new Vector3(10.21735f, 106.49f, -84.06f);
        private static readonly Vector3 DefaultObservationEulerAngles = new Vector3(55f, 0f, 0f);

        [MenuItem("屿见/场景/配置观察模式系统", false, 230)]
        public static void Configure()
        {
            // ---------- 前置检查：任何一项不满足就整体不执行，避免留下半配置状态 ----------

            GameAreaController areas = Object.FindFirstObjectByType<GameAreaController>();

            if (areas == null)
            {
                Fail("场景里找不到 GameAreaController。\n\n" +
                     "请先运行菜单「屿见 → 整合 → 配置 MVP 整合（阶段 6）」把区域系统建出来。");
                return;
            }

            AreaCameraDirector areaCamera = areas.GetComponent<AreaCameraDirector>();

            if (areaCamera == null)
            {
                Fail($"物体「{areas.name}」上没有 AreaCameraDirector。\n\n" +
                     "请先运行菜单「屿见 → 整合 → 配置 MVP 整合（阶段 6）」。");
                return;
            }

            BuildingUI buildingUI = Object.FindFirstObjectByType<BuildingUI>();

            if (buildingUI == null)
            {
                Fail("场景里找不到 BuildingUI，无法把「退出建造」接到观察模式。\n\n" +
                     "请先运行菜单「屿见 → 建筑 → 配置建筑系统（阶段 3）」。");
                return;
            }

            BuildingCameraDirector buildingCamera = Object.FindFirstObjectByType<BuildingCameraDirector>();

            if (buildingCamera == null)
            {
                Fail("场景里找不到 BuildingCameraDirector，观察相机无法在建造过渡期间让位。\n\n" +
                     "请先运行菜单「屿见 → 建筑 → 配置建筑系统（阶段 3）」。");
                return;
            }

            Camera mainCamera = Camera.main;

            if (mainCamera == null)
            {
                Fail("场景里找不到带 MainCamera 标签的相机，无法配置观察相机。");
                return;
            }

            // ---------- 1. 组件 ----------

            GameObject systemObject = areas.gameObject;
            ObservationCameraController observation =
                EnsureComponent<ObservationCameraController>(systemObject);

            // 已存在但被人为关掉的组件也重新启用，否则运行时不会订阅区域事件
            observation.enabled = true;

            // ---------- 2. 输入动作 ----------

            InputActionReference lookAction = FindActionReference("Player", "Look");
            InputActionReference rotateAction = FindActionReference("UI", "MiddleClick");
            InputActionReference panAction = FindActionReference("UI", "RightClick");
            InputActionReference scrollAction = FindActionReference("UI", "ScrollWheel");

            // ---------- 3. ObservationCameraController ----------

            SerializedObject observationSo = new SerializedObject(observation);
            SetObject(observationSo, "areas", areas);
            SetObject(observationSo, "buildingDirector", buildingCamera);
            SetObject(observationSo, "cameraTransform", mainCamera.transform);
            SetObjectIfEmpty(observationSo, "lookAction", lookAction);
            SetObjectIfEmpty(observationSo, "rotateAction", rotateAction);
            SetObjectIfEmpty(observationSo, "panAction", panAction);
            SetObjectIfEmpty(observationSo, "scrollAction", scrollAction);
            observationSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 4. AreaCameraDirector：观察机位与控制器引用 ----------

            SerializedObject areaCameraSo = new SerializedObject(areaCamera);
            SetObject(areaCameraSo, "observationCamera", observation);
            SetVector3IfZero(areaCameraSo, "observationPosition", DefaultObservationPosition);
            SetVector3IfZero(areaCameraSo, "observationEulerAngles", DefaultObservationEulerAngles);
            areaCameraSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 5. 开局处于观察模式（文档第一节「默认进入该系统」） ----------

            SerializedObject areasSo = new SerializedObject(areas);
            SetInt(areasSo, "startArea", (int)GameArea.Observation);
            areasSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 6. BuildingUI：退出建造 → 观察模式 ----------

            SerializedObject buildingUiSo = new SerializedObject(buildingUI);
            SetObjectIfEmpty(buildingUiSo, "areas", areas);
            buildingUiSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 7. 收尾 ----------

            EditorSceneManager.MarkSceneDirty(systemObject.scene);
            Selection.activeGameObject = systemObject;
            EditorGUIUtility.PingObject(systemObject);

            Debug.Log("[ObservationModeSetupWizard] 配置完成：\n" +
                      $"  · {systemObject.name}：ObservationCameraController（区域 / 建造导演 / 相机已接）\n" +
                      "  · 观察机位：默认海岛中心俯视（距离约 130）；已记录的机位不会被覆盖\n" +
                      "  · 输入：滚轮缩放 / 中键单轴旋转 / 右键平移\n" +
                      "  · 开局区域：观察（点底部任意系统按钮进入该系统；再点当前系统按钮或「退出建造」回到观察）\n" +
                      $"  · {buildingUI.name}：退出建造已接到 GameAreaController\n\n" +
                      "请按 Ctrl+S 保存场景，然后进 Play Mode。\n" +
                      "想换观察机位：选中 GameAreaSystem 上的 AreaCameraDirector，" +
                      "在 Scene 视图摆好镜头后右键组件 →「把相机当前位置记录为观察机位」。");
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

            Debug.LogWarning($"[ObservationModeSetupWizard] 在 {InputActionsPath} 中找不到 " +
                             $"{mapName}/{actionName} 的 InputActionReference，" +
                             "请手动把该动作拖到 ObservationCameraController 的对应字段。");
            return null;
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
                Debug.LogWarning($"[ObservationModeSetupWizard] {so.targetObject.GetType().Name} 上找不到字段 " +
                                 $"{fieldName}，该项跳过。");
            }
        }

        /// <summary>只在字段为空时写入，避免重复运行覆盖用户已经手动指定的引用。</summary>
        private static void SetObjectIfEmpty(SerializedObject so, string fieldName, Object value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogWarning($"[ObservationModeSetupWizard] {so.targetObject.GetType().Name} 上找不到字段 " +
                                 $"{fieldName}，该项跳过。");
                return;
            }

            if (property.objectReferenceValue == null && value != null)
            {
                property.objectReferenceValue = value;
            }
        }

        private static void SetVector3IfZero(SerializedObject so, string fieldName, Vector3 value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogWarning($"[ObservationModeSetupWizard] {so.targetObject.GetType().Name} 上找不到字段 " +
                                 $"{fieldName}，该项跳过。");
                return;
            }

            if (property.vector3Value.sqrMagnitude < 0.0001f)
            {
                property.vector3Value = value;
            }
        }

        private static void SetInt(SerializedObject so, string fieldName, int value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.intValue = value;
            }
            else
            {
                Debug.LogWarning($"[ObservationModeSetupWizard] {so.targetObject.GetType().Name} 上找不到字段 " +
                                 $"{fieldName}，该项跳过。");
            }
        }

        private static void Fail(string message)
        {
            EditorUtility.DisplayDialog("观察模式配置未执行", message, "知道了");
            Debug.LogError("[ObservationModeSetupWizard] " + message);
        }
    }
}
