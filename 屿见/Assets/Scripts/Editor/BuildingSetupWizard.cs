using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.Mining;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 阶段 3 建筑系统的一键配置工具。
    ///
    /// 为什么用编辑器脚本而不是直接改 .unity 文件：
    /// Unity 打开着场景时，从外部改动磁盘上的 .unity 文件会让编辑器整场重载，
    /// 编辑器里尚未保存的改动会一起丢失。让 Unity 自己写就没有这个问题。
    ///
    /// 本工具可重复运行：已存在的物体会被复用，按钮会先清空再重建，不会越堆越多。
    /// 运行入口：菜单栏 → 屿见 → 配置建筑系统（阶段 3）
    /// </summary>
    public static class BuildingSetupWizard
    {
        private const string BuildingDataPath = "Assets/Data/Buildings/Building_风琴博物馆.asset";
        private const string SystemObjectName = "BuildingSystem";
        private const string PanelObjectName = "BuildingPanel";

        // 以下坐标全部读自 SampleScene.unity 的真实值，不是估的
        private static readonly Vector3 MapCenter = new Vector3(10.21735f, 0f, -9.49205f);
        private static readonly Vector3 BuildingCameraPosition = new Vector3(10.21735f, 49.15f, -43.9f);
        private static readonly Vector3 BuildingCameraEuler = new Vector3(55f, 0f, 0f);

        private const float ButtonHeight = 80f;
        private const float ButtonSpacing = 10f;

        [MenuItem("屿见/配置建筑系统（阶段 3）")]
        public static void Configure()
        {
            // ---------- 前置检查：任何一项不满足就整体不执行，避免留下半配置状态 ----------

            BuildingData data = AssetDatabase.LoadAssetAtPath<BuildingData>(BuildingDataPath);

            if (data == null)
            {
                Fail($"找不到建筑数据资产：\n{BuildingDataPath}\n\n请确认该文件存在且已导入。");
                return;
            }

            int buildingLayer = LayerMask.NameToLayer("Building");

            if (buildingLayer < 0)
            {
                Fail("工程里没有名为 Building 的图层。\n\n" +
                     "请先到 Project Settings → Tags and Layers 添加一个 Building 层，再运行本命令。");
                return;
            }

            Camera mainCamera = Camera.main;

            if (mainCamera == null)
            {
                Fail("场景里找不到带 MainCamera 标签的相机，无法配置视角与射线。");
                return;
            }

            Canvas canvas = Object.FindFirstObjectByType<Canvas>();

            if (canvas == null)
            {
                Fail("场景里找不到 Canvas，无法创建建筑按钮。");
                return;
            }

            Transform templateButton = FindTemplateButton(canvas);

            if (templateButton == null)
            {
                Fail("Canvas 里没有任何现成的 Button 可以当模板复制。\n\n" +
                     "请先在 Canvas 下手动建一个 Button，再运行本命令。");
                return;
            }

            // ---------- 1. BuildingSystem 物体与组件 ----------

            BuildingManager manager = Object.FindFirstObjectByType<BuildingManager>();
            GameObject systemObject;

            if (manager == null)
            {
                systemObject = new GameObject(SystemObjectName);
                Undo.RegisterCreatedObjectUndo(systemObject, "创建 " + SystemObjectName);
                manager = systemObject.AddComponent<BuildingManager>();
            }
            else
            {
                systemObject = manager.gameObject;
            }

            Transform buildingRoot = FindOrCreateChild(systemObject.transform, "Building Root");
            Transform blueprintRoot = FindOrCreateChild(systemObject.transform, "Blueprint Root");
            BuildingPlacement placement = EnsureComponent<BuildingPlacement>(systemObject);
            BuildingCameraDirector director = EnsureComponent<BuildingCameraDirector>(systemObject);
            BuildingUI ui = EnsureComponent<BuildingUI>(systemObject);

            MiningInput miningInput = Object.FindFirstObjectByType<MiningInput>();

            // ---------- 2. BuildingManager ----------

            SerializedObject managerSo = new SerializedObject(manager);
            SetObject(managerSo, "miningInput", miningInput);
            SetInt(managerSo, "buildingLayers", 1 << buildingLayer);
            SetVector3(managerSo, "defaultSpawnPosition", MapCenter);
            SetObject(managerSo, "buildingRoot", buildingRoot);
            SetObject(managerSo, "blueprintRoot", blueprintRoot);
            SetObject(managerSo, "testBuilding", data);
            managerSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 3. BuildingPlacement ----------

            SerializedObject placementSo = new SerializedObject(placement);
            SetObject(placementSo, "buildingManager", manager);
            SetObject(placementSo, "viewCamera", mainCamera);
            SetFloat(placementSo, "groundHeight", 0f);
            SetFloat(placementSo, "overlapBoxHeight", 4f);
            SetBool(placementSo, "blockClicksOverUI", true);

            if (miningInput != null)
            {
                // MiningInput 的 clickAction 就是 Player/Attack，直接复用它的引用，
                // 省得手工去拖 .inputactions 资产、再在弹出的动作列表里翻
                SerializedProperty clickAction = new SerializedObject(miningInput).FindProperty("clickAction");

                if (clickAction != null && clickAction.objectReferenceValue != null)
                {
                    SetObject(placementSo, "confirmAction", clickAction.objectReferenceValue);
                }
                else
                {
                    Debug.LogWarning("[BuildingSetupWizard] MiningInput 的 Click Action 是空的，" +
                                     "Confirm Action 需要你手动指定 Player/Attack。");
                }
            }
            else
            {
                Debug.LogWarning("[BuildingSetupWizard] 场景里找不到 MiningInput，" +
                                 "BuildingPlacement 的 Confirm Action 需要手动指定 Player/Attack。");
            }

            placementSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 4. BuildingCameraDirector ----------

            SerializedObject directorSo = new SerializedObject(director);
            SetObject(directorSo, "cameraTransform", mainCamera.transform);
            SetObject(directorSo, "buildingManager", manager);
            SetBool(directorSo, "enterAutomatically", true);
            SetVector3(directorSo, "buildingModePosition", BuildingCameraPosition);
            SetVector3(directorSo, "buildingModeEulerAngles", BuildingCameraEuler);
            SetFloat(directorSo, "transitionDuration", 0.6f);
            directorSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 5. BuildingUI 的引用与建筑清单 ----------

            SerializedObject uiSo = new SerializedObject(ui);
            SetObject(uiSo, "buildingManager", manager);
            SetObject(uiSo, "cameraDirector", director);

            SerializedProperty list = uiSo.FindProperty("availableBuildings");

            if (list != null)
            {
                list.arraySize = 1;
                list.GetArrayElementAtIndex(0).objectReferenceValue = data;
            }
            else
            {
                Debug.LogWarning("[BuildingSetupWizard] 找不到 BuildingUI.availableBuildings 字段，" +
                                 "建筑清单需要手动填写。");
            }

            uiSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 6. 按钮面板 ----------

            BuildButtonPanel(canvas, templateButton, ui, data);

            // ---------- 7. 收尾 ----------

            EditorSceneManager.MarkSceneDirty(systemObject.scene);
            Selection.activeGameObject = systemObject;
            EditorGUIUtility.PingObject(systemObject);

            Debug.Log($"[BuildingSetupWizard] 配置完成：\n" +
                      $"  · {SystemObjectName}：BuildingManager / BuildingPlacement / BuildingCameraDirector / BuildingUI\n" +
                      $"  · Canvas/{PanelObjectName}：3 个按钮（{data.BuildingName} / 取消蓝图 / 退出建造）\n" +
                      "请按 Ctrl+S 保存场景。");
        }

        /// <summary>
        /// 在 Canvas 右下角建按钮面板。
        /// 位置选右下是因为商店面板占的是左下（anchoredPosition 280,400，尺寸 520×760）。
        /// </summary>
        private static void BuildButtonPanel(Canvas canvas, Transform templateButton, BuildingUI ui, BuildingData data)
        {
            Transform existing = canvas.transform.Find(PanelObjectName);
            GameObject panel;

            if (existing != null)
            {
                panel = existing.gameObject;
            }
            else
            {
                panel = new GameObject(PanelObjectName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(panel, "创建 " + PanelObjectName);

                RectTransform rect = panel.GetComponent<RectTransform>();
                rect.SetParent(canvas.transform, false);
                rect.anchorMin = new Vector2(1f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(1f, 0f);
                rect.anchoredPosition = new Vector2(-30f, 30f);
                rect.sizeDelta = new Vector2(320f, 3f * ButtonHeight + 2f * ButtonSpacing);
            }

            // 重复运行时先清掉旧按钮，否则每跑一次就多三个
            for (int i = panel.transform.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(panel.transform.GetChild(i).gameObject);
            }

            CreateButton(panel.transform, templateButton, 0, data.BuildingName,
                button => UnityEventTools.AddIntPersistentListener(button.onClick, ui.SelectBuilding, 0));

            CreateButton(panel.transform, templateButton, 1, "取消蓝图",
                button => UnityEventTools.AddPersistentListener(button.onClick, ui.CancelSelection));

            CreateButton(panel.transform, templateButton, 2, "退出建造",
                button => UnityEventTools.AddPersistentListener(button.onClick, ui.ExitBuildMode));
        }

        private static void CreateButton(
            Transform panel, Transform templateButton, int index, string label, System.Action<Button> wire)
        {
            // 拿商店里已有的按钮当模板：字体、图片、配色都跟着复制过来，
            // 不必在代码里加载字体（那才是容易出错的地方）
            GameObject clone = Object.Instantiate(templateButton.gameObject, panel, false);
            clone.name = "Button_" + label;

            RectTransform rect = clone.GetComponent<RectTransform>();

            if (rect != null)
            {
                // 顶部对齐、从上往下排
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -index * (ButtonHeight + ButtonSpacing));
                rect.sizeDelta = new Vector2(0f, ButtonHeight);
            }

            Text text = clone.GetComponentInChildren<Text>(true);

            if (text != null)
            {
                text.text = label;
            }

            Button button = clone.GetComponent<Button>();

            if (button == null)
            {
                Debug.LogWarning($"[BuildingSetupWizard] 复制的按钮模板上没有 Button 组件，「{label}」按钮无法接线。");
                return;
            }

            // 模板上可能带着别的持久监听，先清干净再接自己的
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                UnityEventTools.RemovePersistentListener(button.onClick, i);
            }

            wire(button);
        }

        /// <summary>在 Canvas 里找一个可以当模板的按钮。跳过自建面板里的按钮，避免重复运行时复制自己。</summary>
        private static Transform FindTemplateButton(Canvas canvas)
        {
            Button[] buttons = canvas.GetComponentsInChildren<Button>(true);

            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null)
                {
                    continue;
                }

                if (buttons[i].transform.parent != null &&
                    buttons[i].transform.parent.name == PanelObjectName)
                {
                    continue;
                }

                return buttons[i].transform;
            }

            return null;
        }

        private static Transform FindOrCreateChild(Transform parent, string childName)
        {
            Transform existing = parent.Find(childName);

            if (existing != null)
            {
                return existing;
            }

            // 普通 Transform，不是 RectTransform：这两个是挂世界空间 3D 物体的容器
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
        // 这些字段都是 private [SerializeField]，只能走 SerializedObject 改。
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
                Debug.LogWarning($"[BuildingSetupWizard] {so.targetObject.GetType().Name} 上找不到字段 " +
                                 $"{fieldName}，该项跳过。");
            }
        }

        private static void SetInt(SerializedObject so, string fieldName, int value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.intValue = value;
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

        private static void Fail(string message)
        {
            EditorUtility.DisplayDialog("建筑系统配置未执行", message, "知道了");
            Debug.LogError("[BuildingSetupWizard] " + message);
        }
    }
}
