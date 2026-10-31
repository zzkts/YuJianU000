using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 蓝图旁 × / √ / 撤回 按钮的一键配置工具（2026-10-26 按建筑系统策划案改造）。
    ///
    /// 运行入口：菜单栏 → 屿见 → 建筑 → 配置蓝图按钮（×/√）
    ///
    /// 依赖阶段 4 已经建好的材料面板：本命令会把材料的「确认建造 / 撤回」开关切到蓝图按钮上，
    /// 并隐藏材料面板里的旧按钮。可重复运行，每次都会清空「蓝图按钮/Content」再重建。
    ///
    /// 和工程里其他向导一样，不直接改 .unity：Unity 开着场景时外部改磁盘文件会整场重载。
    /// </summary>
    public static class BuildingBlueprintActionSetupWizard
    {
        private const string ContainerName = "蓝图按钮";
        private const string ContentName = "Content";

        // 找按钮模板时要跳过的自建面板，避免重复运行时复制自己
        private const string ExistingPanelName = "BuildingPanel";
        private const string MaterialPanelName = "材料面板";

        private const float RootWidth = 200f;
        private const float RootHeight = 64f;
        private const float EdgeInset = 8f;

        [MenuItem("屿见/建筑/配置蓝图按钮（×/√）", false, 530)]
        public static void Configure()
        {
            Camera mainCamera = Camera.main;

            if (mainCamera == null)
            {
                Fail("场景里找不到带 MainCamera 标签的相机，无法把蓝图位置投影到屏幕。");
                return;
            }

            BuildingManager manager = Object.FindFirstObjectByType<BuildingManager>();

            if (manager == null)
            {
                Fail("场景里找不到 BuildingManager，请先运行菜单「屿见/建筑/配置建筑系统（阶段 3）」。");
                return;
            }

            BuildingPlacement placement = manager.GetComponent<BuildingPlacement>();

            if (placement == null)
            {
                placement = Object.FindFirstObjectByType<BuildingPlacement>();
            }

            if (placement == null)
            {
                Fail("场景里找不到 BuildingPlacement，请先运行菜单「屿见/建筑/配置建筑系统（阶段 3）」。");
                return;
            }

            BuildingConstruction construction = manager.GetComponent<BuildingConstruction>();

            if (construction == null)
            {
                construction = Object.FindFirstObjectByType<BuildingConstruction>();
            }

            if (construction == null)
            {
                Fail("场景里找不到 BuildingConstruction，请先运行菜单「屿见/建筑/配置建造系统（阶段 4）」。");
                return;
            }

            BuildingMaterialPanel materialPanel =
                Object.FindFirstObjectByType<BuildingMaterialPanel>(FindObjectsInactive.Include);

            if (materialPanel == null)
            {
                Fail("场景里找不到 BuildingMaterialPanel，请先运行菜单「屿见/建筑/配置建造系统（阶段 4）」。");
                return;
            }

            Canvas canvas = materialPanel.GetComponentInParent<Canvas>();

            if (canvas == null)
            {
                canvas = Object.FindFirstObjectByType<Canvas>();
            }

            if (canvas == null)
            {
                Fail("场景里找不到 Canvas，无法创建蓝图按钮。");
                return;
            }

            Transform templateButton = FindTemplateButton(canvas);

            if (templateButton == null)
            {
                Fail("Canvas 里找不到可以当模板的按钮（已有的按钮都在自建面板里）。\n\n" +
                     "请先在商店面板里保留一个按钮，再运行本命令。");
                return;
            }

            // ---------- 1. 容器与内容根 ----------

            Transform existing = canvas.transform.Find(ContainerName);
            GameObject container;

            if (existing != null)
            {
                container = existing.gameObject;
                container.SetActive(true);
            }
            else
            {
                container = new GameObject(ContainerName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(container, "创建 " + ContainerName);
                container.GetComponent<RectTransform>().SetParent(canvas.transform, false);
            }

            // 容器铺满 Canvas，只当「组件宿主」；真正定位的是 Content。
            // 铺满而不是给个小矩形，是为了让 Content 的 anchoredPosition 直接等于 Canvas 本地坐标。
            RectTransform containerRect = container.GetComponent<RectTransform>();

            if (containerRect == null)
            {
                containerRect = container.AddComponent<RectTransform>();
            }

            containerRect.anchorMin = Vector2.zero;
            containerRect.anchorMax = Vector2.one;
            containerRect.pivot = new Vector2(0.5f, 0.5f);
            containerRect.offsetMin = Vector2.zero;
            containerRect.offsetMax = Vector2.zero;

            // 每次运行都清空重建，避免按钮越堆越多
            for (int i = container.transform.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(container.transform.GetChild(i).gameObject);
            }

            GameObject content = new GameObject(ContentName, typeof(RectTransform));
            content.transform.SetParent(container.transform, false);

            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0.5f, 0.5f);
            contentRect.anchorMax = new Vector2(0.5f, 0.5f);
            contentRect.pivot = new Vector2(0.5f, 0.5f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(RootWidth, RootHeight);

            BuildingBlueprintActionUI actionUI = EnsureComponent<BuildingBlueprintActionUI>(container);

            // ---------- 2. 三个按钮 ----------

            Button cancelButton = CreateActionButton(
                content.transform, templateButton, "Button_取消", "×",
                new Vector2(56f, 56f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(EdgeInset, 0f), 30);

            Button undoButton = CreateActionButton(
                content.transform, templateButton, "Button_撤回", "撤回",
                new Vector2(72f, 56f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(EdgeInset, 0f), 20);

            Button confirmButton = CreateActionButton(
                content.transform, templateButton, "Button_确认", "√",
                new Vector2(56f, 56f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-EdgeInset, 0f), 30);

            if (cancelButton == null || confirmButton == null || undoButton == null)
            {
                Fail("按钮模板不完整（缺 Button 组件），蓝图按钮没有配置完成。\n\n" +
                     "请换一个带 Button 的场景按钮当模板后重跑本命令。");
                return;
            }

            // 落位后 × / √ 消失、撤回出现；初始给一个合理的组合，运行时每帧会再刷新
            cancelButton.gameObject.SetActive(true);
            undoButton.gameObject.SetActive(false);
            confirmButton.gameObject.SetActive(false);

            // ---------- 3. 接线 ----------

            SerializedObject so = new SerializedObject(actionUI);
            SetObject(so, "buildingManager", manager);
            SetObject(so, "construction", construction);
            SetObject(so, "placement", placement);
            SetObject(so, "viewCamera", mainCamera);
            SetObject(so, "canvas", canvas);
            SetObject(so, "actionRoot", contentRect);
            SetObject(so, "cancelButton", cancelButton);
            SetObject(so, "confirmButton", confirmButton);
            SetObject(so, "undoButton", undoButton);
            SetVector2(so, "screenOffset", new Vector2(0f, 48f));
            SetVector2(so, "edgePadding", new Vector2(80f, 64f));
            so.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 4. 材料面板的旧按钮退场 ----------

            SerializedObject panelSo = new SerializedObject(materialPanel);
            SetBool(panelSo, "useBlueprintActions", true);
            panelSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(materialPanel);

            // 内容根平时关闭；本组件挂在常开的容器上，运行时由它打开
            content.SetActive(false);

            // 容器放到最后，免得被后创建的面板盖住
            container.transform.SetAsLastSibling();

            // ---------- 5. 收尾 ----------

            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            Selection.activeGameObject = container;
            EditorGUIUtility.PingObject(container);

            Debug.Log("[BuildingBlueprintActionSetupWizard] 配置完成：\n" +
                      $"  · Canvas/{ContainerName}/{ContentName}：左侧 × / 撤回，右侧 √\n" +
                      "  · BuildingBlueprintActionUI：引用已接好，Content 平时关闭\n" +
                      "  · 材料面板：Use Blueprint Actions 已勾上，旧的「撤回 / 确认建造」运行时隐藏\n" +
                      "请按 Ctrl+S 保存场景，然后进 Play Mode 验证：\n" +
                      "  1. 选中建筑 → 蓝图左侧 ×、右侧 √；不可放置时 √ 变灰不可点\n" +
                      "  2. 点 √ → 蓝图落位，× 变成撤回\n" +
                      "  3. 往蓝图拖材料，凑齐后右侧 √ 重新出现\n" +
                      "  4. 点 √ 建成；落位后点撤回先退材料，没材料时撤回蓝图");
        }

        private static Button CreateActionButton(
            Transform parent,
            Transform templateButton,
            string name,
            string label,
            Vector2 size,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 anchoredPosition,
            int fontSize)
        {
            GameObject clone = Object.Instantiate(templateButton.gameObject, parent, false);
            clone.name = name;

            // 模板多半是隐藏的样品（商店的 ButtonTemplate），Instantiate 复制的是 activeSelf，
            // 不显式打开就会得到一个「矩形完全正确、但永远看不见也点不到」的按钮
            clone.SetActive(true);

            RectTransform rect = clone.GetComponent<RectTransform>();

            if (rect == null)
            {
                Debug.LogWarning($"[BuildingBlueprintActionSetupWizard] 按钮模板上没有 RectTransform，" +
                                 $"「{label}」无法定位。");
            }
            else
            {
                rect.anchorMin = anchor;
                rect.anchorMax = anchor;
                rect.pivot = pivot;
                rect.anchoredPosition = anchoredPosition;
                rect.sizeDelta = size;
            }

            Text text = clone.GetComponentInChildren<Text>(true);

            if (text != null)
            {
                text.text = label;
                text.fontSize = fontSize;
                text.alignment = TextAnchor.MiddleCenter;
                text.resizeTextForBestFit = false;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
            }
            else
            {
                Debug.LogWarning($"[BuildingBlueprintActionSetupWizard] 按钮模板下没有 Text，「{label}」没有文字。");
            }

            Button button = clone.GetComponent<Button>();

            if (button == null)
            {
                Debug.LogWarning($"[BuildingBlueprintActionSetupWizard] 按钮模板上没有 Button 组件，" +
                                 $"「{label}」无法点击。");
                return null;
            }

            // 模板可能带着商店的持久监听，清干净；运行时监听由 BuildingBlueprintActionUI 挂
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                UnityEditor.Events.UnityEventTools.RemovePersistentListener(button.onClick, i);
            }

            return button;
        }

        /// <summary>在 Canvas 里找一个可以当模板的按钮，跳过建造系统自建的面板。</summary>
        private static Transform FindTemplateButton(Canvas canvas)
        {
            Button[] buttons = canvas.GetComponentsInChildren<Button>(true);

            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null || buttons[i].transform.parent == null)
                {
                    continue;
                }

                string parentName = buttons[i].transform.parent.name;

                if (parentName == ExistingPanelName ||
                    parentName == MaterialPanelName ||
                    parentName == ContainerName)
                {
                    continue;
                }

                return buttons[i].transform;
            }

            return null;
        }

        private static T EnsureComponent<T>(GameObject target) where T : Component
        {
            T existing = target.GetComponent<T>();
            return existing != null ? existing : target.AddComponent<T>();
        }

        private static void SetObject(SerializedObject so, string fieldName, Object value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.objectReferenceValue = value;
            }
            else
            {
                Debug.LogWarning($"[BuildingBlueprintActionSetupWizard] {so.targetObject.GetType().Name} 上找不到字段 " +
                                 $"{fieldName}，该项跳过。");
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

        private static void SetVector2(SerializedObject so, string fieldName, Vector2 value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.vector2Value = value;
            }
        }

        private static void Fail(string message)
        {
            EditorUtility.DisplayDialog("蓝图按钮配置未执行", message, "知道了");
            Debug.LogError("[BuildingBlueprintActionSetupWizard] " + message);
        }
    }
}
