using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.Core;
using Yujian.Player;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 建筑迁移（策划案 3.3）与建筑升级（策划案 3.4）的一键配置工具。
    ///
    /// 运行入口：菜单栏 → 屿见 → 建筑 → 配置建筑迁移与升级
    ///
    /// 做五件事：
    ///   1. 生成 / 复用升级表资产 Assets/Data/Buildings/LevelUP_01.asset；
    ///   2. 生成 / 复用迁移预览材质 Assets/Materials/BuildingMigrationPreview.mat（绿 / 红外表）；
    ///   3. 给四座建筑资产挂上升级表（已有值的资产不覆盖）；
    ///   4. 在建筑管理器所在物体上挂 BuildingUpgrade / BuildingMigration / BuildingPointerInteraction 并接线；
    ///   5. 在 Canvas 下建「建筑升级界面」「建筑迁移按钮」两套 UI 并接线。
    ///
    /// 幂等：重复运行会复用已有组件与资产，UI 容器每次清空重建。
    /// 和其它向导一样不直接改 .unity 文件，只在 Unity 进程内改场景并保存。
    /// </summary>
    public static class BuildingMigrationUpgradeSetupWizard
    {
        private const string UpgradeAssetPath = "Assets/Data/Buildings/LevelUP_01.asset";
        private const string PreviewMaterialPath = "Assets/Materials/BuildingMigrationPreview.mat";
        private const string PreviewShaderName = "Yujian/Building Preview Tint";
        private const string UpgradeContainerName = "建筑升级界面";
        private const string MigrationContainerName = "建筑迁移按钮";

        private static readonly string[] SkipPanelNames =
        {
            "BuildingPanel",
            "材料面板",
            "蓝图按钮",
            UpgradeContainerName,
            MigrationContainerName,
        };

        [MenuItem("屿见/建筑/配置建筑迁移与升级", false, 532)]
        public static void Configure()
        {
            Camera mainCamera = Camera.main;

            if (mainCamera == null)
            {
                Fail("场景里找不到带 MainCamera 标签的相机。");
                return;
            }

            BuildingManager manager = Object.FindFirstObjectByType<BuildingManager>(FindObjectsInactive.Include);
            BuildingGrid grid = Object.FindFirstObjectByType<BuildingGrid>(FindObjectsInactive.Include);
            PlayerCurrency currency = Object.FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            GameAreaController areas = Object.FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);
            BuildingGridVisualizer visualizer =
                Object.FindFirstObjectByType<BuildingGridVisualizer>(FindObjectsInactive.Include);

            if (manager == null)
            {
                Fail("场景里找不到 BuildingManager，请先运行建筑系统配置向导。");
                return;
            }

            if (grid == null)
            {
                Fail("场景里找不到 BuildingGrid，请先运行菜单「屿见/建筑/配置建筑网格与建造相机（阶段 7）」。");
                return;
            }

            if (currency == null)
            {
                Fail("场景里找不到 PlayerCurrency，升级无法扣金币。");
                return;
            }

            Canvas canvas = FindCanvas();

            if (canvas == null)
            {
                Fail("场景里找不到 Canvas，无法创建升级 / 迁移 UI。");
                return;
            }

            Transform templateButton = FindTemplateButton(canvas);

            if (templateButton == null)
            {
                Fail("Canvas 里找不到可以当模板的按钮（已有的按钮都在自建面板里）。\n\n" +
                     "请先在商店面板里保留一个按钮，再运行本命令。");
                return;
            }

            Transform templateText = FindTemplateText(canvas);

            // ---------- 1. 升级表资产 ----------

            BuildingUpgradeTable table = EnsureUpgradeTable();
            AssignUpgradeTableToBuildings(table);
            Material previewMaterial = EnsurePreviewMaterial();

            // ---------- 2. 运行时组件 ----------

            BuildingUpgrade upgrade = EnsureComponent<BuildingUpgrade>(manager.gameObject);
            BuildingMigration migration = EnsureComponent<BuildingMigration>(manager.gameObject);
            BuildingPointerInteraction interaction = EnsureComponent<BuildingPointerInteraction>(manager.gameObject);

            float groundHeight = ReadGroundHeight(manager);

            SerializedObject upgradeSo = new SerializedObject(upgrade);
            SetObject(upgradeSo, "buildingManager", manager);
            SetObject(upgradeSo, "currency", currency);
            upgradeSo.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject migrationSo = new SerializedObject(migration);
            SetObject(migrationSo, "buildingManager", manager);
            SetObject(migrationSo, "grid", grid);
            SetObject(migrationSo, "gridVisualizer", visualizer);
            SetObject(migrationSo, "viewCamera", mainCamera);
            SetObject(migrationSo, "areas", areas);
            SetObject(migrationSo, "ghostMaterialTemplate", previewMaterial);
            SetFloat(migrationSo, "groundHeight", groundHeight);
            migrationSo.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject interactionSo = new SerializedObject(interaction);
            SetObject(interactionSo, "buildingManager", manager);
            SetObject(interactionSo, "migration", migration);
            SetObject(interactionSo, "upgrade", upgrade);
            SetObject(interactionSo, "viewCamera", mainCamera);
            SetObject(interactionSo, "areas", areas);
            interactionSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 3. 升级界面 ----------

            GameObject upgradeContainer = EnsureContainer(canvas.transform, UpgradeContainerName);
            RectTransform upgradeContent = RebuildContent(upgradeContainer, new Vector2(280f, 96f));
            BuildingUpgradeUI upgradeUI = EnsureComponent<BuildingUpgradeUI>(upgradeContainer);

            Button upgradeButton = CreateButton(
                upgradeContent, templateButton, "Button_升级", "升级 ×0",
                new Vector2(128f, 56f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -28f), 24);

            Text upgradeButtonText = upgradeButton != null ? upgradeButton.GetComponentInChildren<Text>(true) : null;

            Text levelText = CreateText(
                upgradeContent, templateText, "Text_等级", "Lv.1 / 50",
                new Vector2(280f, 28f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 14f), 18);

            // 左下角切换块：按钮 + 悬停说明
            GameObject toggleObject = new GameObject("Button_升级方式", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(toggleObject, "创建 升级方式");
            toggleObject.transform.SetParent(upgradeContainer.transform, false);

            RectTransform toggleRect = toggleObject.GetComponent<RectTransform>();
            toggleRect.anchorMin = new Vector2(0f, 0f);
            toggleRect.anchorMax = new Vector2(0f, 0f);
            toggleRect.pivot = new Vector2(0f, 0f);
            toggleRect.anchoredPosition = new Vector2(24f, 24f);
            toggleRect.sizeDelta = new Vector2(88f, 48f);

            // 复用工程按钮样式：把模板按钮作为子物体铺满
            Button toggleButton = Object.Instantiate(templateButton.gameObject, toggleRect, false)
                .GetComponent<Button>();
            toggleButton.name = "Button_升级方式_底";
            toggleButton.gameObject.SetActive(true);

            RectTransform toggleButtonRect = toggleButton.transform as RectTransform;

            if (toggleButtonRect != null)
            {
                toggleButtonRect.anchorMin = Vector2.zero;
                toggleButtonRect.anchorMax = Vector2.one;
                toggleButtonRect.offsetMin = Vector2.zero;
                toggleButtonRect.offsetMax = Vector2.zero;
            }

            ClearPersistentListeners(toggleButton);

            Text toggleLabel = toggleButton.GetComponentInChildren<Text>(true);

            if (toggleLabel != null)
            {
                toggleLabel.text = "x1";
                toggleLabel.fontSize = 20;
                toggleLabel.alignment = TextAnchor.MiddleCenter;
            }

            Text tooltipText = CreateText(
                upgradeContainer.transform, templateText, "Text_升级方式提示", string.Empty,
                new Vector2(220f, 26f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(24f, 80f), 16);

            if (tooltipText != null)
            {
                tooltipText.gameObject.SetActive(false);
            }

            BuildingUpgradeModeToggle modeToggle = EnsureComponent<BuildingUpgradeModeToggle>(toggleObject);
            SerializedObject toggleSo = new SerializedObject(modeToggle);
            SetObject(toggleSo, "button", toggleButton);
            SetObject(toggleSo, "labelText", toggleLabel);
            SetObject(toggleSo, "tooltipText", tooltipText);
            toggleSo.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject upgradeUiSo = new SerializedObject(upgradeUI);
            SetObject(upgradeUiSo, "upgrade", upgrade);
            SetObject(upgradeUiSo, "migration", migration);
            SetObject(upgradeUiSo, "buildingManager", manager);
            SetObject(upgradeUiSo, "areas", areas);
            SetObject(upgradeUiSo, "viewCamera", mainCamera);
            SetObject(upgradeUiSo, "canvas", canvas);
            SetObject(upgradeUiSo, "actionRoot", upgradeContent);
            SetObject(upgradeUiSo, "upgradeButton", upgradeButton);
            SetObject(upgradeUiSo, "upgradeButtonText", upgradeButtonText);
            SetObject(upgradeUiSo, "levelText", levelText);
            SetObject(upgradeUiSo, "modeToggle", modeToggle);
            upgradeUiSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 4. 迁移按钮 ----------

            GameObject migrationContainer = EnsureContainer(canvas.transform, MigrationContainerName);
            RectTransform migrationContent = RebuildContent(migrationContainer, new Vector2(220f, 104f));
            BuildingMigrationUI migrationUI = EnsureComponent<BuildingMigrationUI>(migrationContainer);

            Button migrateUndoButton = CreateButton(
                migrationContent, templateButton, "Button_撤回迁移", "撤回",
                new Vector2(80f, 56f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(8f, 8f), 20);

            Button migrateConfirmButton = CreateButton(
                migrationContent, templateButton, "Button_确认迁移", "√",
                new Vector2(56f, 56f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-8f, 8f), 30);

            // 状态行：绿色「可放置」/ 红色「不可放置：原因」。按钮置灰时玩家能知道为什么
            Text migrationStatus = CreateText(
                migrationContent, templateText, "Text_迁移状态", "可放置",
                new Vector2(220f, 20f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 8f), 14);

            SerializedObject migrationUiSo = new SerializedObject(migrationUI);
            SetObject(migrationUiSo, "migration", migration);
            SetObject(migrationUiSo, "viewCamera", mainCamera);
            SetObject(migrationUiSo, "canvas", canvas);
            SetObject(migrationUiSo, "actionRoot", migrationContent);
            SetObject(migrationUiSo, "confirmButton", migrateConfirmButton);
            SetObject(migrationUiSo, "undoButton", migrateUndoButton);
            SetObject(migrationUiSo, "statusText", migrationStatus);
            migrationUiSo.ApplyModifiedPropertiesWithoutUndo();

            // 内容根平时关闭；组件挂在常开的容器上，运行时由它打开
            upgradeContent.gameObject.SetActive(false);
            migrationContent.gameObject.SetActive(false);

            // 两个容器都提到最上层，免得被后创建的面板盖住
            upgradeContainer.transform.SetAsLastSibling();
            migrationContainer.transform.SetAsLastSibling();

            // ---------- 5. 收尾 ----------

            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = manager.gameObject;
            EditorGUIUtility.PingObject(manager.gameObject);

            Debug.Log("[BuildingMigrationUpgradeSetupWizard] 配置完成：\n" +
                      $"  · 升级表：{UpgradeAssetPath}（LevelUP_01，上限 50 级）已挂到四座建筑上\n" +
                      $"  · 迁移预览材质：{PreviewMaterialPath}（绿 / 红外表）\n" +
                      "  · BuildingUpgrade / BuildingMigration / BuildingPointerInteraction：已挂在 " +
                      $"{manager.name} 上并接线\n" +
                      $"  · Canvas/{UpgradeContainerName}：升级按钮 + 等级文字 + 左下角 x1/Max\n" +
                      $"  · Canvas/{MigrationContainerName}：撤回 / 确认迁移 + 状态行\n" +
                      "进 Play Mode 验证：\n" +
                      "  1. 短按已建建筑 → 建筑上方出现升级按钮；点左下角切 x1 / Max\n" +
                      "  2. 长按已建建筑并拖动 → 建筑自由跟手，能放绿、不能放红；松开 → 确认 / 撤回\n" +
                      "  3. 点空地 → 退出升级界面");
        }

        /// <summary>生成或复用 LevelUP_01 资产。</summary>
        private static BuildingUpgradeTable EnsureUpgradeTable()
        {
            BuildingUpgradeTable table = AssetDatabase.LoadAssetAtPath<BuildingUpgradeTable>(UpgradeAssetPath);

            if (table != null)
            {
                return table;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Data/Buildings"))
            {
                AssetDatabase.CreateFolder("Assets/Data", "Buildings");
            }

            table = ScriptableObject.CreateInstance<BuildingUpgradeTable>();
            AssetDatabase.CreateAsset(table, UpgradeAssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BuildingMigrationUpgradeSetupWizard] 已创建升级表 {UpgradeAssetPath}。");
            return table;
        }

        /// <summary>
        /// 生成或复用迁移预览材质（绿 / 红外表）。材质本身是模板，运行时由 BuildingMigration
        /// 克隆并改 _GhostColor，场景里的建筑不会引用到这份资产。
        /// </summary>
        private static Material EnsurePreviewMaterial()
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(PreviewMaterialPath);

            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find(PreviewShaderName);

            if (shader == null)
            {
                Debug.LogWarning($"[BuildingMigrationUpgradeSetupWizard] 找不到着色器「{PreviewShaderName}」，" +
                                 "迁移时没有绿 / 红反馈。请确认 Assets/Shaders/BuildingPreviewTint.shader 已导入。");
                return null;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }

            Material material = new Material(shader)
            {
                name = "BuildingMigrationPreview",
            };

            AssetDatabase.CreateAsset(material, PreviewMaterialPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BuildingMigrationUpgradeSetupWizard] 已创建预览材质 {PreviewMaterialPath}。");
            return material;
        }

        /// <summary>把升级表挂到 Assets/Data/Buildings 下的所有建筑资产上；已有值的不覆盖。</summary>
        private static void AssignUpgradeTableToBuildings(BuildingUpgradeTable table)
        {
            string[] guids = AssetDatabase.FindAssets("t:BuildingData", new[] { "Assets/Data/Buildings" });

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                BuildingData data = AssetDatabase.LoadAssetAtPath<BuildingData>(path);

                if (data == null)
                {
                    continue;
                }

                SerializedObject serialized = new SerializedObject(data);
                SerializedProperty property = serialized.FindProperty("upgradeTable");

                if (property == null)
                {
                    continue;
                }

                if (property.objectReferenceValue != null)
                {
                    continue;
                }

                property.objectReferenceValue = table;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(data);
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>从 BuildingPlacement 的 Ground Height 读地面高度，保证与建造系统一致。</summary>
        private static float ReadGroundHeight(BuildingManager manager)
        {
            BuildingPlacement placement = manager.GetComponent<BuildingPlacement>();

            if (placement == null)
            {
                placement = Object.FindFirstObjectByType<BuildingPlacement>(FindObjectsInactive.Include);
            }

            if (placement == null)
            {
                return 0f;
            }

            SerializedObject serialized = new SerializedObject(placement);
            SerializedProperty property = serialized.FindProperty("groundHeight");
            return property != null ? property.floatValue : 0f;
        }

        private static Canvas FindCanvas()
        {
            BuildingMaterialPanel panel =
                Object.FindFirstObjectByType<BuildingMaterialPanel>(FindObjectsInactive.Include);

            if (panel != null)
            {
                Canvas parent = panel.GetComponentInParent<Canvas>(true);

                if (parent != null)
                {
                    return parent;
                }
            }

            return Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        }

        /// <summary>把一个「容器 + Content」结构准备好：容器铺满 Canvas，Content 居中固定尺寸。</summary>
        private static GameObject EnsureContainer(Transform canvas, string name)
        {
            Transform existing = canvas.Find(name);
            GameObject container;

            if (existing != null)
            {
                container = existing.gameObject;
                container.SetActive(true);
            }
            else
            {
                container = new GameObject(name, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(container, "创建 " + name);
                container.GetComponent<RectTransform>().SetParent(canvas, false);
            }

            RectTransform rect = container.GetComponent<RectTransform>();

            if (rect == null)
            {
                rect = container.AddComponent<RectTransform>();
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return container;
        }

        /// <summary>清空容器并重建 Content。Content 的 anchoredPosition 直接就是 Canvas 本地坐标。</summary>
        private static RectTransform RebuildContent(GameObject container, Vector2 size)
        {
            for (int i = container.transform.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(container.transform.GetChild(i).gameObject);
            }

            GameObject content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(container.transform, false);

            RectTransform rect = content.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;

            return rect;
        }

        private static Button CreateButton(
            Transform parent, Transform templateButton, string name, string label,
            Vector2 size, Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition, int fontSize)
        {
            GameObject clone = Object.Instantiate(templateButton.gameObject, parent, false);
            clone.name = name;
            clone.SetActive(true);

            RectTransform rect = clone.GetComponent<RectTransform>();

            if (rect != null)
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

            Button button = clone.GetComponent<Button>();

            if (button == null)
            {
                Debug.LogWarning($"[BuildingMigrationUpgradeSetupWizard] 按钮模板上没有 Button 组件，「{label}」无法点击。");
                return null;
            }

            ClearPersistentListeners(button);
            return button;
        }

        private static Text CreateText(
            Transform parent, Transform templateText, string name, string content,
            Vector2 size, Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition, int fontSize)
        {
            if (templateText == null)
            {
                Debug.LogWarning($"[BuildingMigrationUpgradeSetupWizard] 找不到文字模板，「{name}」没有创建。");
                return null;
            }

            GameObject clone = Object.Instantiate(templateText.gameObject, parent, false);
            clone.name = name;
            clone.SetActive(true);

            RectTransform rect = clone.GetComponent<RectTransform>();

            if (rect != null)
            {
                rect.anchorMin = anchor;
                rect.anchorMax = anchor;
                rect.pivot = pivot;
                rect.anchoredPosition = anchoredPosition;
                rect.sizeDelta = size;
            }

            Text text = clone.GetComponent<Text>();

            if (text != null)
            {
                text.text = content;
                text.fontSize = fontSize;
                text.alignment = TextAnchor.MiddleCenter;
                text.resizeTextForBestFit = false;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;

                // 文字不能吃点击：否则建筑上方的说明会把「点建筑选中」挡掉
                text.raycastTarget = false;
            }

            return text;
        }

        /// <summary>在 Canvas 里找一个可以当模板的按钮，跳过建筑系统自建的容器。</summary>
        private static Transform FindTemplateButton(Canvas canvas)
        {
            Button[] buttons = canvas.GetComponentsInChildren<Button>(true);

            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null || buttons[i].transform.parent == null)
                {
                    continue;
                }

                if (IsInsideSkippedPanel(buttons[i].transform))
                {
                    continue;
                }

                return buttons[i].transform;
            }

            return null;
        }

        /// <summary>在 Canvas 里找一个可以当模板的文字。</summary>
        private static Transform FindTemplateText(Canvas canvas)
        {
            Text[] texts = canvas.GetComponentsInChildren<Text>(true);

            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] == null || texts[i].transform.parent == null)
                {
                    continue;
                }

                if (IsInsideSkippedPanel(texts[i].transform))
                {
                    continue;
                }

                return texts[i].transform;
            }

            return null;
        }

        private static bool IsInsideSkippedPanel(Transform target)
        {
            Transform current = target;

            for (int depth = 0; current != null && depth < 4; depth++)
            {
                for (int i = 0; i < SkipPanelNames.Length; i++)
                {
                    if (current.name == SkipPanelNames[i])
                    {
                        return true;
                    }
                }

                current = current.parent;
            }

            return false;
        }

        private static void ClearPersistentListeners(Button button)
        {
            if (button == null)
            {
                return;
            }

            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                UnityEditor.Events.UnityEventTools.RemovePersistentListener(button.onClick, i);
            }
        }

        private static T EnsureComponent<T>(GameObject target) where T : Component
        {
            T existing = target.GetComponent<T>();
            return existing != null ? existing : Undo.AddComponent<T>(target);
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
                Debug.LogWarning($"[BuildingMigrationUpgradeSetupWizard] {so.targetObject.GetType().Name} 上找不到字段 " +
                                 $"{fieldName}，该项跳过。");
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

        private static void Fail(string message)
        {
            EditorUtility.DisplayDialog("建筑迁移与升级未配置", message, "知道了");
            Debug.LogError("[BuildingMigrationUpgradeSetupWizard] " + message);
        }
    }
}
