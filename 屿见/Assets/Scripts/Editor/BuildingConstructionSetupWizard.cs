using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.Shop;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 阶段 4 建造系统的一键配置工具。
    ///
    /// 为什么用编辑器脚本而不是直接改 .unity / .prefab：
    /// Unity 打开着场景时，从外部改动磁盘上的文件会让编辑器整场重载，未保存的改动会一起丢失。
    /// 让 Unity 自己写就没有这个问题（见 开发进度.md 踩坑 2）。
    ///
    /// 本工具可重复运行：已存在的物体会被复用，面板会先清空再重建。
    /// 运行入口：菜单栏 → 屿见 → 配置建造系统（阶段 4）
    /// </summary>
    public static class BuildingConstructionSetupWizard
    {
        private const string BuildingPrefabPath = "Assets/Prefabs/建筑/建筑.prefab";
        private const string BlueprintPrefabPath = "Assets/Prefabs/建筑/蓝图_建筑.prefab";
        private const string MaterialDataPath = "Assets/Data/Materials/Material_砖块.asset";

        private const string LayerNodeName = "楼层";
        private const string ModelNodeName = "模型";

        private const string SystemObjectName = "BuildingSystem";
        private const string ExistingPanelName = "BuildingPanel";
        private const string MaterialPanelName = "材料面板";

        /// <summary>预制体里预置几层当样板。BuildingLayerStack 会在运行时按实际材料数克隆到够用。</summary>
        private const int SampleLayerCount = 3;

        private static readonly Vector3 DefaultFootprint = new Vector3(4f, 4f, 4f);

        [MenuItem("屿见/配置建造系统（阶段 4）")]
        public static void Configure()
        {
            BuildingData data = AssetDatabase.LoadAssetAtPath<BuildingData>(
                "Assets/Data/Buildings/Building_风琴博物馆.asset");

            if (data == null)
            {
                Fail("找不到建筑数据资产 Assets/Data/Buildings/Building_风琴博物馆.asset。" +
                     "请先确认该文件存在，或先运行阶段 3 的配置。");
                return;
            }

            Camera mainCamera = Camera.main;

            if (mainCamera == null)
            {
                Fail("场景里找不到带 MainCamera 标签的相机，无法配置拖拽落点判定。");
                return;
            }

            Canvas canvas = Object.FindFirstObjectByType<Canvas>();

            if (canvas == null)
            {
                Fail("场景里找不到 Canvas，无法创建材料面板。");
                return;
            }

            Transform templateButton = FindTemplateButton(canvas);

            if (templateButton == null)
            {
                Fail("Canvas 里找不到可以当模板的按钮（已有的按钮都在建造面板里）。\n\n" +
                     "请先在商店面板里保留一个按钮，再运行本命令。");
                return;
            }

            MaterialInventory inventory = Object.FindFirstObjectByType<MaterialInventory>();

            if (inventory == null)
            {
                Debug.LogWarning("[BuildingConstructionSetupWizard] 场景里找不到 MaterialInventory，" +
                                 "材料面板与建造结算的库存引用需要手动指定。");
            }

            BuildingManager manager = Object.FindFirstObjectByType<BuildingManager>();

            if (manager == null)
            {
                Fail("场景里找不到 BuildingManager，请先运行菜单「屿见/配置建筑系统（阶段 3）」。");
                return;
            }

            // ---------- 1. 重建两个预制体 ----------

            bool buildingPrefabChanged = ConfigureBuildingPrefab();
            bool blueprintPrefabChanged = ConfigureBlueprintPrefab();

            if (buildingPrefabChanged || blueprintPrefabChanged)
            {
                AssetDatabase.SaveAssets();
            }

            // ---------- 2. BuildingConstruction ----------

            GameObject systemObject = manager.gameObject;
            BuildingConstruction construction = EnsureComponent<BuildingConstruction>(systemObject);

            SerializedObject constructionSo = new SerializedObject(construction);
            SetObject(constructionSo, "buildingManager", manager);
            SetObject(constructionSo, "inventory", inventory);
            SetObject(constructionSo, "viewCamera", mainCamera);
            SetFloat(constructionSo, "groundHeight", 0f);
            constructionSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 3. 材料面板 ----------

            BuildMaterialPanel(canvas, templateButton, construction, manager, inventory, mainCamera);

            // ---------- 4. 收尾 ----------

            EditorSceneManager.MarkSceneDirty(systemObject.scene);
            Selection.activeGameObject = systemObject;
            EditorGUIUtility.PingObject(systemObject);

            Debug.Log("[BuildingConstructionSetupWizard] 配置完成：\n" +
                      $"  · {BuildingPrefabPath}：根物体加 BoxCollider + BuildingLayerStack，子物体「{LayerNodeName}」下 {SampleLayerCount} 层样板\n" +
                      $"  · {BlueprintPrefabPath}：加「{LayerNodeName}」节点并接到 BuildingBlueprint.Material Layers\n" +
                      $"  · {SystemObjectName}：新增 BuildingConstruction\n" +
                      $"  · Canvas/{MaterialPanelName}：标题 / 进度 / 条目容器 / 条目模板 / 状态行 / 撤回 / 确认建造\n" +
                      "请按 Ctrl+S 保存场景。");
        }

        // ---------------- 预制体 ----------------

        /// <summary>把建筑预制体改造成「根上挂 BoxCollider + BuildingLayerStack，子节点「楼层」下若干层样板」。</summary>
        /// <returns>预制体是否真的被改过。</returns>
        private static bool ConfigureBuildingPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(BuildingPrefabPath);

            if (root == null)
            {
                Debug.LogError($"[BuildingConstructionSetupWizard] 加载不了建筑预制体：{BuildingPrefabPath}");
                return false;
            }

            try
            {
                Transform layerRoot = root.transform.Find(LayerNodeName);

                if (layerRoot == null)
                {
                    // 首次运行：从原来的「模型」抄尺寸、材质，然后把「模型」删掉
                    Transform model = root.transform.Find(ModelNodeName);
                    Vector3 footprint = DefaultFootprint;
                    Material layerMaterial = null;

                    if (model != null)
                    {
                        // 预制体里「模型」是一个 scale 4 的 Cube，父级 scale 是 1，所以 localScale 就是世界尺寸
                        footprint = model.localScale;
                        Renderer modelRenderer = model.GetComponent<Renderer>();

                        if (modelRenderer != null)
                        {
                            layerMaterial = modelRenderer.sharedMaterial;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[BuildingConstructionSetupWizard] 建筑预制体里找不到「{ModelNodeName}」，" +
                                         $"按默认尺寸 {DefaultFootprint} 建楼层。");
                    }

                    layerRoot = BuildLayerNode(root.transform, footprint, layerMaterial);

                    if (model != null)
                    {
                        Object.DestroyImmediate(model.gameObject);
                    }
                }

                EnsureRootCollider(root, layerRoot);
                EnsureLayerStack(root, layerRoot, null, 1f);

                PrefabUtility.SaveAsPrefabAsset(root, BuildingPrefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>给蓝图预制体补上「楼层」节点并接到 BuildingBlueprint.Material Layers。</summary>
        /// <returns>预制体是否真的被改过。</returns>
        private static bool ConfigureBlueprintPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(BlueprintPrefabPath);

            if (root == null)
            {
                Debug.LogError($"[BuildingConstructionSetupWizard] 加载不了蓝图预制体：{BlueprintPrefabPath}");
                return false;
            }

            try
            {
                Transform layerRoot = root.transform.Find(LayerNodeName);

                if (layerRoot == null)
                {
                    Transform model = root.transform.Find(ModelNodeName);
                    Vector3 footprint = DefaultFootprint;
                    Material layerMaterial = null;

                    if (model != null)
                    {
                        footprint = model.localScale;
                        Renderer modelRenderer = model.GetComponent<Renderer>();

                        if (modelRenderer != null)
                        {
                            layerMaterial = modelRenderer.sharedMaterial;
                        }
                    }

                    layerRoot = BuildLayerNode(root.transform, footprint, layerMaterial);
                }

                BuildingLayerStack stack = EnsureLayerStack(root, layerRoot, null, 0.85f);

                // 把楼层接到 BuildingBlueprint 上。不接的话，蓝图的绿/红整体换材质会把楼层颜色盖掉
                BuildingBlueprint blueprint = root.GetComponent<BuildingBlueprint>();

                if (blueprint == null)
                {
                    Debug.LogError("[BuildingConstructionSetupWizard] 蓝图预制体的根物体上没有 BuildingBlueprint 组件。");
                }
                else
                {
                    SerializedObject blueprintSo = new SerializedObject(blueprint);
                    SetObject(blueprintSo, "materialLayers", stack);
                    blueprintSo.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(root, BlueprintPrefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>在父物体下建「楼层」节点，并按总高均分出若干层样板 Cube。</summary>
        private static Transform BuildLayerNode(Transform parent, Vector3 footprint, Material layerMaterial)
        {
            GameObject node = new GameObject(LayerNodeName);
            Transform layerRoot = node.transform;
            layerRoot.SetParent(parent, false);

            float totalHeight = footprint.y > 0f ? footprint.y : 4f;
            float layerHeight = totalHeight / SampleLayerCount;

            for (int i = 0; i < SampleLayerCount; i++)
            {
                GameObject layer = GameObject.CreatePrimitive(PrimitiveType.Cube);
                layer.name = $"层{i}";

                // 楼层的碰撞交给根物体上的那一个 BoxCollider，层本身不要碰撞体
                Collider collider = layer.GetComponent<Collider>();

                if (collider != null)
                {
                    Object.DestroyImmediate(collider);
                }

                layer.transform.SetParent(layerRoot, false);
                layer.transform.localScale = new Vector3(footprint.x, layerHeight, footprint.z);
                layer.transform.localPosition = new Vector3(0f, (i + 0.5f) * layerHeight, 0f);

                Renderer renderer = layer.GetComponent<Renderer>();

                if (renderer != null && layerMaterial != null)
                {
                    renderer.sharedMaterial = layerMaterial;
                }
            }

            return layerRoot;
        }

        /// <summary>保证根物体上有一个覆盖「0 ~ 总高」的 BoxCollider，重叠检测靠它。</summary>
        private static void EnsureRootCollider(GameObject root, Transform layerRoot)
        {
            float totalHeight = TotalHeightFromLayers(layerRoot);
            Vector3 footprint = FootprintFromLayers(layerRoot);

            BoxCollider collider = root.GetComponent<BoxCollider>();

            if (collider == null)
            {
                collider = root.AddComponent<BoxCollider>();
            }

            // 根物体 scale 是 1，所以 size 就是世界尺寸
            collider.center = new Vector3(0f, totalHeight * 0.5f, 0f);
            collider.size = new Vector3(footprint.x, totalHeight, footprint.z);
        }

        private static BuildingLayerStack EnsureLayerStack(
            GameObject root, Transform layerRoot, Material layerMaterial, float layerAlpha)
        {
            BuildingLayerStack stack = root.GetComponent<BuildingLayerStack>();

            if (stack == null)
            {
                stack = root.AddComponent<BuildingLayerStack>();
            }

            SerializedObject so = new SerializedObject(stack);
            SetObject(so, "layerRoot", layerRoot);
            SetFloat(so, "totalHeight", TotalHeightFromLayers(layerRoot));

            if (layerMaterial != null)
            {
                SetObject(so, "layerMaterial", layerMaterial);
            }

            SetFloat(so, "layerAlpha", layerAlpha);
            so.ApplyModifiedPropertiesWithoutUndo();

            return stack;
        }

        /// <summary>楼层挂点下的样板层围出来的总高（最上面那层的顶）。</summary>
        private static float TotalHeightFromLayers(Transform layerRoot)
        {
            float top = 0f;

            for (int i = 0; i < layerRoot.childCount; i++)
            {
                Transform child = layerRoot.GetChild(i);
                float layerTop = child.localPosition.y + child.localScale.y * 0.5f;

                if (layerTop > top)
                {
                    top = layerTop;
                }
            }

            return top > 0f ? top : 4f;
        }

        private static Vector3 FootprintFromLayers(Transform layerRoot)
        {
            if (layerRoot.childCount == 0)
            {
                return DefaultFootprint;
            }

            return layerRoot.GetChild(0).localScale;
        }

        // ---------------- 场景面板 ----------------

        private static void BuildMaterialPanel(
            Canvas canvas,
            Transform templateButton,
            BuildingConstruction construction,
            BuildingManager manager,
            MaterialInventory inventory,
            Camera mainCamera)
        {
            Transform existing = canvas.transform.Find(MaterialPanelName);
            GameObject panel;

            if (existing != null)
            {
                panel = existing.gameObject;
            }
            else
            {
                panel = new GameObject(MaterialPanelName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(panel, "创建 " + MaterialPanelName);

                RectTransform rect = panel.GetComponent<RectTransform>();
                rect.SetParent(canvas.transform, false);
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(-30f, -30f);
                rect.sizeDelta = new Vector2(380f, 560f);
            }

            // 每次运行都清空重建，避免越堆越多
            for (int i = panel.transform.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(panel.transform.GetChild(i).gameObject);
            }

            BuildingMaterialPanel panelComponent = EnsureComponent<BuildingMaterialPanel>(panel);

            // 内容容器：面板组件挂在父物体上，运行时只隐藏这个子物体，
            // 否则关掉 panelRoot 会把组件自己也停掉，面板再也回不来
            GameObject content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(panel.transform, false);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;

            Image background = content.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.55f);
            background.raycastTarget = true;

            Text titleText = CreateText(content.transform, "TitleText", "建筑", 22, TextAnchor.UpperLeft, 14f, 34f);
            Text progressText = CreateText(content.transform, "ProgressText", "材料：—", 18, TextAnchor.UpperLeft, 56f, 28f);

            GameObject containerObject = new GameObject("ItemContainer", typeof(RectTransform));
            containerObject.transform.SetParent(content.transform, false);
            RectTransform containerRect = containerObject.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0f, 1f);
            containerRect.anchorMax = new Vector2(1f, 1f);
            containerRect.pivot = new Vector2(0.5f, 1f);
            containerRect.anchoredPosition = new Vector2(0f, -92f);
            containerRect.sizeDelta = new Vector2(-24f, 320f);

            Text statusText = CreateText(content.transform, "StatusText", string.Empty, 16, TextAnchor.LowerLeft, 0f, 30f);
            RectTransform statusRect = statusText.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0f, 0f);
            statusRect.anchorMax = new Vector2(1f, 0f);
            statusRect.pivot = new Vector2(0.5f, 0f);
            statusRect.anchoredPosition = new Vector2(0f, 146f);
            statusRect.sizeDelta = new Vector2(-24f, 30f);

            // 条目模板：拿商店按钮当样板（字体、图片、配色都跟着过来），再把 Button 拆掉。
            // 留着 Button 的话拖拽会被它的点击逻辑插一脚
            GameObject itemTemplate = Object.Instantiate(templateButton.gameObject, content.transform, false);
            itemTemplate.name = "ItemTemplate";

            Button templateButtonComponent = itemTemplate.GetComponent<Button>();

            if (templateButtonComponent != null)
            {
                Object.DestroyImmediate(templateButtonComponent);
            }

            EnsureComponent<MaterialDragItem>(itemTemplate);

            RectTransform templateRect = itemTemplate.GetComponent<RectTransform>();

            if (templateRect != null)
            {
                // 模板在 ItemContainer 外面，不占排版位置
                templateRect.SetParent(content.transform, false);
                templateRect.sizeDelta = new Vector2(0f, 56f);
            }

            itemTemplate.SetActive(false);

            Button undoButton = CreateButton(content.transform, templateButton, "Button_撤回", "撤回");
            RectTransform undoRect = undoButton.GetComponent<RectTransform>();
            undoRect.anchorMin = new Vector2(0f, 0f);
            undoRect.anchorMax = new Vector2(1f, 0f);
            undoRect.pivot = new Vector2(0.5f, 0f);
            undoRect.anchoredPosition = new Vector2(0f, 78f);
            undoRect.sizeDelta = new Vector2(-24f, 56f);

            Button confirmButton = CreateButton(content.transform, templateButton, "Button_确认建造", "确认建造");
            RectTransform confirmRect = confirmButton.GetComponent<RectTransform>();
            confirmRect.anchorMin = new Vector2(0f, 0f);
            confirmRect.anchorMax = new Vector2(1f, 0f);
            confirmRect.pivot = new Vector2(0.5f, 0f);
            confirmRect.anchoredPosition = new Vector2(0f, 14f);
            confirmRect.sizeDelta = new Vector2(-24f, 56f);

            // ---------- 接线 ----------

            SerializedObject so = new SerializedObject(panelComponent);
            SetObject(so, "construction", construction);
            SetObject(so, "buildingManager", manager);
            SetObject(so, "inventory", inventory);
            SetObject(so, "panelRoot", content);
            SetObject(so, "titleText", titleText);
            SetObject(so, "progressText", progressText);
            SetObject(so, "statusText", statusText);
            SetObject(so, "itemContainer", containerObject.transform);
            SetObject(so, "itemTemplate", itemTemplate.GetComponent<RectTransform>());
            SetObject(so, "ghostParent", canvas.transform as RectTransform);
            SetObject(so, "undoButton", undoButton);
            SetObject(so, "confirmButton", confirmButton);
            so.ApplyModifiedPropertiesWithoutUndo();

            // 撤回 / 确认的点击在 BuildingMaterialPanel.Awake 里用 AddListener 接，
            // 这里不用 UnityEventTools 挂持久监听，免得重复运行时叠一堆
            content.SetActive(false);
        }

        private static Text CreateText(
            Transform parent, string name, string content, int fontSize, TextAnchor alignment, float top, float height)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(-24f, height);

            Text text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            if (text.font == null)
            {
                Debug.LogWarning("[BuildingConstructionSetupWizard] 找不到内置字体 LegacyRuntime.ttf，" +
                                 "新建的文字不会显示。请在 Inspector 里手动指定字体。");
            }

            return text;
        }

        private static Button CreateButton(Transform parent, Transform templateButton, string name, string label)
        {
            GameObject clone = Object.Instantiate(templateButton.gameObject, parent, false);
            clone.name = name;

            Text text = clone.GetComponentInChildren<Text>(true);

            if (text != null)
            {
                text.text = label;
            }

            Button button = clone.GetComponent<Button>();

            if (button == null)
            {
                Debug.LogWarning($"[BuildingConstructionSetupWizard] 按钮模板上没有 Button 组件，「{label}」无法点击。");
                return null;
            }

            // 模板上可能带着商店的持久监听，清干净
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                UnityEditor.Events.UnityEventTools.RemovePersistentListener(button.onClick, i);
            }

            return button;
        }

        /// <summary>在 Canvas 里找一个可以当模板的按钮，跳过两个自建面板里的按钮。</summary>
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

                if (parentName == ExistingPanelName || parentName == MaterialPanelName)
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
                Debug.LogWarning($"[BuildingConstructionSetupWizard] {so.targetObject.GetType().Name} 上找不到字段 " +
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
            EditorUtility.DisplayDialog("建造系统配置未执行", message, "知道了");
            Debug.LogError("[BuildingConstructionSetupWizard] " + message);
        }
    }
}
