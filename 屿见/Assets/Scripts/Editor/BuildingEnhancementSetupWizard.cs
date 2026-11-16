using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 建筑强化节点窗口（策划案 3.4）的场景 UI 配置工具。
    ///
    /// 入口：菜单栏 → 屿见 → 建筑 → 配置建筑强化节点界面
    ///
    /// 做两件事：
    ///   1. 场景里还没有「建筑强化节点界面」时，生成一套**普通场景物体**：
    ///      全屏 50% 黑遮罩、中央面板、标题 / 说明、VerticalLayoutGroup 选项列表、
    ///      选项模板、确认遮罩 + 确认文字 + 是 / 否按钮；
    ///   2. 把所有引用接到 BuildingUpgradeNodeUI / BuildingUpgradeOptionUI / BuildingUpgradeUI。
    ///
    /// 生成后所有位置、字体、颜色、按钮样式都可以直接在 Hierarchy / Inspector 里改，
    /// 运行时不会重建、不会覆盖你的排版。
    ///
    /// 如果场景里已经有 BuildingUpgradeNodeUI，本命令**不重建**，只重新接线，避免冲掉你的修改。
    /// 想恢复初始布局：删掉「建筑强化节点界面」物体再运行一次。
    /// </summary>
    public static class BuildingEnhancementSetupWizard
    {
        private const string ContainerName = "建筑强化节点界面";

        private static readonly Color OverlayColor = new Color(0f, 0f, 0f, 0.5f);
        private static readonly Color PanelColor = new Color(0.11f, 0.13f, 0.17f, 0.98f);
        private static readonly Color OptionColor = new Color(0.19f, 0.23f, 0.29f, 1f);
        private static readonly Color AccentColor = new Color(0.35f, 0.9f, 0.45f, 1f);
        private static readonly Color TextColor = new Color(0.95f, 0.95f, 0.92f, 1f);
        private static readonly Color MutedColor = new Color(0.72f, 0.75f, 0.78f, 1f);

        [MenuItem("屿见/建筑/配置建筑强化节点界面", false, 533)]
        public static void Configure()
        {
            Canvas canvas = FindCanvas();
            BuildingUpgrade upgrade = Object.FindFirstObjectByType<BuildingUpgrade>(FindObjectsInactive.Include);
            BuildingUpgradeUI upgradeUI = Object.FindFirstObjectByType<BuildingUpgradeUI>(FindObjectsInactive.Include);

            if (canvas == null)
            {
                Fail("场景里找不到 Canvas，无法创建强化节点界面。");
                return;
            }

            if (upgrade == null)
            {
                Fail("场景里找不到 BuildingUpgrade。请先运行菜单「屿见/建筑/配置建筑迁移与升级」。");
                return;
            }

            BuildingUpgradeNodeUI existing = Object.FindFirstObjectByType<BuildingUpgradeNodeUI>(
                FindObjectsInactive.Include);

            if (existing != null)
            {
                ConfigureReferences(existing, upgrade, upgradeUI);
                ConvertExistingOptionListToColumns(existing);
                Finish(existing.gameObject,
                    "场景里已有「建筑强化节点界面」，本次**没有重建**，只重新接线。\n" +
                    "位置 / 字体 / 颜色 / 按钮样式都按你现在的 Hierarchy 保留。\n" +
                    "选项列表若原本是竖排，已换成 2 列 GridLayoutGroup；列数 / 卡片尺寸可在 Inspector 改。\n" +
                    "想恢复初始布局：删掉该物体后重新运行本命令。");
                return;
            }

            BuildingUpgradeNodeUI nodeUI = Build(canvas, upgrade);

            if (nodeUI == null)
            {
                return;
            }

            ConfigureReferences(nodeUI, upgrade, upgradeUI);
            Finish(nodeUI.gameObject,
                "已生成可自由编辑的「建筑强化节点界面」。\n" +
                "· 遮罩 / 面板 / 标题 / 说明 / 选项列表 / 选项模板 / 确认框都是普通场景物体；\n" +
                "· 改位置、字体、颜色、按钮样式直接改 Hierarchy；\n" +
                "· 选项按钮会克隆「选项列表/选项模板」，所以模板改一次，所有选项同步；\n" +
                "· 文案模板在 BuildingUpgradeNodeUI 的 Inspector（标题 / 说明 / 确认格式）。");
        }

        // ---------- 搭建 ----------

        private static BuildingUpgradeNodeUI Build(Canvas canvas, BuildingUpgrade upgrade)
        {
            Transform canvasRoot = canvas.rootCanvas != null ? canvas.rootCanvas.transform : canvas.transform;

            RectTransform container = CreateRect(ContainerName, canvasRoot);
            Stretch(container, 0f);
            BuildingUpgradeNodeUI nodeUI = container.gameObject.AddComponent<BuildingUpgradeNodeUI>();

            // 全屏遮罩（运行时显隐的就是它）
            RectTransform overlay = CreateRect("遮罩", container);
            Stretch(overlay, 0f);
            AddImage(overlay.gameObject, OverlayColor, true);

            RectTransform panel = CreateRect("面板", overlay);
            Center(panel, new Vector2(820f, 560f));
            AddImage(panel.gameObject, PanelColor, true);

            Text title = CreateText(panel, "标题", "建筑强化 · 10 级节点", 30, FontStyle.Bold, TextColor);
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(40f, -78f), new Vector2(-40f, -24f));

            Text subtitle = CreateText(panel, "说明", "「建筑」已升到 10 级，请选择一项强化（选择后不可更改）",
                19, FontStyle.Normal, MutedColor);
            SetRect(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(40f, -128f), new Vector2(-40f, -80f));

            RectTransform optionList = CreateRect("选项列表", panel);
            optionList.anchorMin = Vector2.zero;
            optionList.anchorMax = Vector2.one;
            optionList.offsetMin = new Vector2(40f, 40f);
            optionList.offsetMax = new Vector2(-40f, -140f);

            // 分栏排布：默认 2 列，列数 / 卡片尺寸 / 间距都可在 Inspector 改
            ApplyColumnLayout(optionList);

            BuildingUpgradeOptionUI optionTemplate = CreateOptionTemplate(optionList);

            RectTransform confirmOverlay = CreateRect("确认遮罩", overlay);
            Stretch(confirmOverlay, 0f);
            AddImage(confirmOverlay.gameObject, new Color(0f, 0f, 0f, 0.35f), true);

            RectTransform confirmBox = CreateRect("确认框", confirmOverlay);
            Center(confirmBox, new Vector2(640f, 360f));
            AddImage(confirmBox.gameObject, PanelColor, true);

            Text confirmText = CreateText(confirmBox, "确认文字",
                "确认选择「强化」？\n\n效果说明\n\n选择后不可更改。", 20, FontStyle.Normal, TextColor);
            SetRect(confirmText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(32f, 110f), new Vector2(-32f, -24f));

            Button yesButton = CreateLabeledButton(confirmBox, "是", "是", AccentColor, Color.black);
            SetRect(yesButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0.5f, 0f),
                new Vector2(48f, 28f), new Vector2(-24f, 96f));

            Button noButton = CreateLabeledButton(confirmBox, "否", "否", OptionColor, TextColor);
            SetRect(noButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(1f, 0f),
                new Vector2(24f, 28f), new Vector2(-48f, 96f));

            // 默认收起；运行时由 BuildingUpgradeNodeUI 控制
            overlay.gameObject.SetActive(false);
            confirmOverlay.gameObject.SetActive(false);
            optionTemplate.gameObject.SetActive(false);
            container.SetAsLastSibling();

            SerializedObject so = new SerializedObject(nodeUI);
            SetObject(so, "upgrade", upgrade);
            SetObject(so, "root", overlay.gameObject);
            SetObject(so, "titleText", title);
            SetObject(so, "subtitleText", subtitle);
            SetObject(so, "optionContainer", optionList);
            SetObject(so, "optionTemplate", optionTemplate);
            SetObject(so, "confirmPanel", confirmOverlay.gameObject);
            SetObject(so, "confirmText", confirmText);
            SetObject(so, "confirmYesButton", yesButton);
            SetObject(so, "confirmNoButton", noButton);
            so.ApplyModifiedPropertiesWithoutUndo();

            return nodeUI;
        }

        /// <summary>
        /// 选项区默认做成 2 列分栏。已存在 GridLayoutGroup 时保留用户设置，不被覆盖。
        /// </summary>
        private static void ApplyColumnLayout(RectTransform optionList)
        {
            if (optionList == null)
            {
                return;
            }

            GridLayoutGroup grid = optionList.GetComponent<GridLayoutGroup>();

            if (grid == null)
            {
                // 旧版生成的是 VerticalLayoutGroup；换成 Grid 之前先清掉，避免两个布局组打架
                LayoutGroup legacy = optionList.GetComponent<LayoutGroup>();

                if (legacy != null)
                {
                    Object.DestroyImmediate(legacy);
                }

                grid = optionList.gameObject.AddComponent<GridLayoutGroup>();
            }

            grid.cellSize = new Vector2(356f, 180f);
            grid.spacing = new Vector2(16f, 16f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.childAlignment = TextAnchor.UpperCenter;
        }

        private static void ConvertExistingOptionListToColumns(BuildingUpgradeNodeUI nodeUI)
        {
            if (nodeUI == null)
            {
                return;
            }

            SerializedObject so = new SerializedObject(nodeUI);
            SerializedProperty property = so.FindProperty("optionContainer");
            RectTransform optionList = property != null
                ? property.objectReferenceValue as RectTransform
                : null;

            // 旧版生成器可能把模板留成激活状态，导致占位文字被当成一张选项显示
            SerializedProperty templateProperty = so.FindProperty("optionTemplate");
            BuildingUpgradeOptionUI template = templateProperty != null
                ? templateProperty.objectReferenceValue as BuildingUpgradeOptionUI
                : null;

            if (template != null)
            {
                template.gameObject.SetActive(false);

                LayoutElement layout = template.GetComponent<LayoutElement>();

                if (layout != null)
                {
                    layout.preferredHeight = 180f;
                    layout.flexibleWidth = 1f;
                }
            }

            if (optionList == null || optionList.GetComponent<GridLayoutGroup>() != null)
            {
                // 引用为空，或用户已经自己配过 Grid，保持现状
                return;
            }

            ApplyColumnLayout(optionList);
        }

        private static BuildingUpgradeOptionUI CreateOptionTemplate(Transform parent)
        {
            GameObject templateObject = new GameObject(
                "选项模板", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            templateObject.transform.SetParent(parent, false);

            Image image = templateObject.GetComponent<Image>();
            image.color = OptionColor;
            image.raycastTarget = true;

            Button button = templateObject.GetComponent<Button>();
            button.targetGraphic = image;

            ColorBlock colors = button.colors;
            colors.normalColor = OptionColor;
            colors.highlightedColor = Color.Lerp(OptionColor, Color.white, 0.15f);
            colors.pressedColor = Color.Lerp(OptionColor, Color.black, 0.15f);
            colors.selectedColor = OptionColor;
            button.colors = colors;

            LayoutElement layout = templateObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 180f;
            layout.flexibleWidth = 1f;

            BuildingUpgradeOptionUI optionUI = templateObject.AddComponent<BuildingUpgradeOptionUI>();

            Text nameText = CreateText(templateObject.transform, "名称", "强化名称　全局",
                22, FontStyle.Bold, TextColor);
            SetRect(nameText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -40f), new Vector2(-24f, -8f));

            Text descriptionText = CreateText(templateObject.transform, "描述", "强化效果描述",
                17, FontStyle.Normal, MutedColor);
            SetRect(descriptionText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(24f, 8f), new Vector2(-24f, -44f));

            optionUI.ConfigureForSetup(button, nameText, descriptionText);
            return optionUI;
        }

        // ---------- 接线 ----------

        private static void ConfigureReferences(
            BuildingUpgradeNodeUI nodeUI, BuildingUpgrade upgrade, BuildingUpgradeUI upgradeUI)
        {
            SerializedObject nodeSo = new SerializedObject(nodeUI);
            SetObject(nodeSo, "upgrade", upgrade);
            nodeSo.ApplyModifiedPropertiesWithoutUndo();

            if (upgradeUI != null)
            {
                SerializedObject uiSo = new SerializedObject(upgradeUI);
                SetObject(uiSo, "nodeUI", nodeUI);
                uiSo.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("[BuildingEnhancementSetupWizard] 场景里找不到 BuildingUpgradeUI，" +
                                 "升级界面不会主动找到节点窗口；请在它的 Inspector 里把 Node UI 拖上。");
            }
        }

        private static void Finish(GameObject target, string message)
        {
            EditorSceneManager.MarkSceneDirty(target.scene);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = target;
            EditorGUIUtility.PingObject(target);

            Debug.Log("[BuildingEnhancementSetupWizard] " + message);
        }

        // ---------- UI 小工具 ----------

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static Image AddImage(GameObject target, Color color, bool raycast)
        {
            Image image = target.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static Text CreateText(
            Transform parent, string name, string content, int size, FontStyle style, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            Text text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateLabeledButton(
            Transform parent, string name, string label, Color background, Color textColor)
        {
            GameObject go = new GameObject(
                name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.color = background;
            image.raycastTarget = true;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;

            ColorBlock colors = button.colors;
            colors.normalColor = background;
            colors.highlightedColor = Color.Lerp(background, Color.white, 0.15f);
            colors.pressedColor = Color.Lerp(background, Color.black, 0.15f);
            colors.selectedColor = background;
            button.colors = colors;

            Text text = CreateText(go.transform, "文字", label, 22, FontStyle.Bold, textColor);
            Stretch(text.rectTransform, 0f);
            text.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        private static Canvas FindCanvas()
        {
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            Canvas best = null;

            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];

                if (canvas == null || canvas.renderMode == RenderMode.WorldSpace)
                {
                    continue;
                }

                // 优先根 Canvas，子 Canvas 上的坐标不好摆
                if (canvas.rootCanvas != canvas)
                {
                    continue;
                }

                if (best == null || canvas.transform.childCount > best.transform.childCount)
                {
                    best = canvas;
                }
            }

            return best;
        }

        private static void Stretch(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        private static void Center(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        private static void SetRect(
            RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
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
                Debug.LogWarning($"[BuildingEnhancementSetupWizard] " +
                                 $"{so.targetObject.GetType().Name} 上找不到字段 {fieldName}，该项跳过。");
            }
        }

        private static void Fail(string message)
        {
            EditorUtility.DisplayDialog("建筑强化节点界面未配置", message, "知道了");
            Debug.LogError("[BuildingEnhancementSetupWizard] " + message);
        }
    }
}
