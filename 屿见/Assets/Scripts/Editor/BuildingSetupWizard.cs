using System.Collections.Generic;
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
        private const string BuildingFolder = "Assets/Data/Buildings";
        private const string SystemObjectName = "BuildingSystem";
        private const string PanelObjectName = "BuildingPanel";
        private const string BlueprintActionPanelName = "蓝图按钮";

        /// <summary>
        /// 建筑面板里的按钮顺序（也就是玩家看到的顺序）。
        /// **新增建筑时把文件名追加到这里**：面板、BuildingUI.availableBuildings 与按钮下标一次配齐。
        /// 风琴博物馆固定第一，三座主要建筑按剧情顺序排在后面。
        /// </summary>
        private static readonly string[] BuildingFileNames =
        {
            "Building_风琴博物馆.asset",
            "Building_八卦楼.asset",
            "Building_菽庄花园.asset",
            "Building_日光岩.asset",
        };

        /// <summary>建筑面板顶部的里程碑进度行。名字固定，里程碑 UI 会按它找文本。</summary>
        private const string ProgressTextName = "MilestoneProgressText";
        private const float ProgressTextHeight = 44f;

        // 以下坐标全部读自 SampleScene.unity 的真实值，不是估的
        private static readonly Vector3 MapCenter = new Vector3(10.21735f, 0f, -9.49205f);
        private static readonly Vector3 BuildingCameraPosition = new Vector3(10.21735f, 49.15f, -43.9f);
        private static readonly Vector3 BuildingCameraEuler = new Vector3(55f, 0f, 0f);

        private const float ButtonHeight = 80f;
        private const float ButtonSpacing = 10f;

        [MenuItem("屿见/建筑/配置建筑系统（阶段 3）", false, 510)]
        public static void Configure()
        {
            // ---------- 前置检查：任何一项不满足就整体不执行，避免留下半配置状态 ----------

            List<BuildingData> buildings = LoadBuildings();

            if (buildings.Count == 0)
            {
                Fail($"在 {BuildingFolder} 下找不到任何建筑数据资产，无法创建建筑按钮。\n\n" +
                     "请确认 BuildingFileNames 里列出的资产存在且已导入。");
                return;
            }

            BuildingData data = buildings[0];

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
                list.arraySize = buildings.Count;

                for (int i = 0; i < buildings.Count; i++)
                {
                    list.GetArrayElementAtIndex(i).objectReferenceValue = buildings[i];
                }
            }
            else
            {
                Debug.LogWarning("[BuildingSetupWizard] 找不到 BuildingUI.availableBuildings 字段，" +
                                 "建筑清单需要手动填写。");
            }

            uiSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 6. 按钮面板 ----------

            BuildButtonPanel(canvas, templateButton, ui, buildings);

            // ---------- 7. 收尾 ----------

            EditorSceneManager.MarkSceneDirty(systemObject.scene);
            Selection.activeGameObject = systemObject;
            EditorGUIUtility.PingObject(systemObject);

            Debug.Log($"[BuildingSetupWizard] 配置完成：\n" +
                      $"  · {SystemObjectName}：BuildingManager / BuildingPlacement / BuildingCameraDirector / BuildingUI\n" +
                      $"  · Canvas/{PanelObjectName}：{buildings.Count} 个建筑按钮 + 取消蓝图 / 退出建造，" +
                      $"顶部进度行「主要建筑 0/3」\n" +
                      "请按 Ctrl+S 保存场景。");
        }

        /// <summary>
        /// 在 Canvas 右下角建按钮面板。
        /// 位置选右下是因为商店面板占的是左下（anchoredPosition 280,400，尺寸 520×760）。
        /// 面板 = 顶部一行里程碑进度 + N 个建筑按钮 + 取消蓝图 / 退出建造。
        /// </summary>
        private static void BuildButtonPanel(
            Canvas canvas, Transform templateButton, BuildingUI ui, IReadOnlyList<BuildingData> buildings)
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

                RectTransform created = panel.GetComponent<RectTransform>();
                created.SetParent(canvas.transform, false);
                created.anchorMin = new Vector2(1f, 0f);
                created.anchorMax = new Vector2(1f, 0f);
                created.pivot = new Vector2(1f, 0f);
                created.anchoredPosition = new Vector2(-30f, 30f);
            }

            int buttonCount = buildings.Count + 2; // + 取消蓝图 / 退出建造

            // 尺寸每次重跑都重写：建筑数量变了面板高度要跟着变
            RectTransform panelRect = panel.GetComponent<RectTransform>();

            if (panelRect != null)
            {
                panelRect.sizeDelta = new Vector2(
                    320f,
                    ProgressTextHeight + ButtonSpacing +
                    buttonCount * ButtonHeight + (buttonCount - 1) * ButtonSpacing);
            }

            // 重复运行时先清掉旧按钮，否则每跑一次就多一批。
            // 进度行要留下：里程碑 UI 组件持有它的引用，重建会让引用变成 Missing。
            for (int i = panel.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = panel.transform.GetChild(i);

                if (child.name == ProgressTextName)
                {
                    continue;
                }

                Undo.DestroyObjectImmediate(child.gameObject);
            }

            // ---------- 顶部进度行 ----------

            bool createdProgressText = panel.transform.Find(ProgressTextName) == null;
            Text progressText = FindOrCreateTextChild(panel.transform, ProgressTextName);
            RectTransform progressRect = progressText.GetComponent<RectTransform>();
            progressRect.anchorMin = new Vector2(0f, 1f);
            progressRect.anchorMax = new Vector2(1f, 1f);
            progressRect.pivot = new Vector2(0.5f, 1f);
            progressRect.anchoredPosition = Vector2.zero;
            progressRect.sizeDelta = new Vector2(0f, ProgressTextHeight);

            progressText.font = BuiltinFont();
            progressText.fontSize = 26;
            progressText.alignment = TextAnchor.MiddleCenter;
            progressText.color = Color.white;
            progressText.raycastTarget = false;

            if (createdProgressText)
            {
                // 只是新建时的占位文案；真实数字由 MilestonePanelUI 写入。
                // 重跑本向导时不覆盖，避免把已刷新的进度打回 0/3。
                progressText.text = "主要建筑 0/3";
            }

            // ---------- 按钮 ----------

            float top = ProgressTextHeight + ButtonSpacing;

            for (int i = 0; i < buildings.Count; i++)
            {
                // 必须拷贝一份：for 循环变量在所有迭代里是同一个，闭包会全部抓到最后一个下标
                int index = i;

                CreateButton(panel.transform, templateButton, top, buildings[i].BuildingName,
                    button => UnityEventTools.AddIntPersistentListener(button.onClick, ui.SelectBuilding, index));

                top += ButtonHeight + ButtonSpacing;
            }

            CreateButton(panel.transform, templateButton, top, "取消蓝图",
                button => UnityEventTools.AddPersistentListener(button.onClick, ui.CancelSelection));
            top += ButtonHeight + ButtonSpacing;

            CreateButton(panel.transform, templateButton, top, "退出建造",
                button => UnityEventTools.AddPersistentListener(button.onClick, ui.ExitBuildMode));
        }

        private static void CreateButton(
            Transform panel, Transform templateButton, float top, string label, System.Action<Button> wire)
        {
            // 拿商店里已有的按钮当模板：字体、图片、配色都跟着复制过来，
            // 不必在代码里加载字体（那才是容易出错的地方）
            GameObject clone = Object.Instantiate(templateButton.gameObject, panel, false);
            clone.name = "Button_" + label;

            // 模板是商店面板里那个**隐藏**的 ButtonTemplate（m_IsActive: 0），克隆体会带着关闭状态。
            // 漏掉这一行的话：面板本身照样显示（它没有背景图），但里面一块空白、一个字都没有，
            // 而且 Console 一句报错都不给 —— 阶段 6 实测就是撞在这上面
            // （「建筑模块点开后没有蓝图选项」＝ 风琴博物馆 / 取消蓝图 / 退出建造 三个按钮全是关的）。
            // 本行是那个缺陷的源头：不补上，每重跑一次本命令就会重新把它造出来（踩坑 5）。
            clone.SetActive(true);

            RectTransform rect = clone.GetComponent<RectTransform>();

            if (rect != null)
            {
                // 顶部对齐、从上往下排
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -top);
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
                    (buttons[i].transform.parent.name == PanelObjectName ||
                     buttons[i].transform.parent.name == BlueprintActionPanelName))
                {
                    continue;
                }

                return buttons[i].transform;
            }

            return null;
        }

        /// <summary>按 BuildingFileNames 的顺序加载建筑资产；缺哪个就警告哪个，不中断。</summary>
        private static List<BuildingData> LoadBuildings()
        {
            List<BuildingData> result = new List<BuildingData>(BuildingFileNames.Length);

            for (int i = 0; i < BuildingFileNames.Length; i++)
            {
                string path = BuildingFolder + "/" + BuildingFileNames[i];
                BuildingData data = AssetDatabase.LoadAssetAtPath<BuildingData>(path);

                if (data == null)
                {
                    Debug.LogWarning($"[BuildingSetupWizard] 找不到建筑资产「{path}」，" +
                                     "这一项不会出现在建筑面板里。");
                    continue;
                }

                result.Add(data);
            }

            return result;
        }

        /// <summary>找到名字对应的子物体，没有就建一个（重跑向导时复用，不会越堆越多）。</summary>
        private static Text FindOrCreateTextChild(Transform parent, string childName)
        {
            Transform existing = parent.Find(childName);
            GameObject child;

            if (existing == null)
            {
                child = new GameObject(childName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(child, "创建 " + childName);
                child.transform.SetParent(parent, false);
            }
            else
            {
                child = existing.gameObject;
            }

            return EnsureComponent<Text>(child);
        }

        private static Font BuiltinFont()
        {
            // 项目规则：不引入 TextMeshPro，UI 文字用旧版 Text + 内置字体
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
