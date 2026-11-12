using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.Core;
using Yujian.Save;
using Yujian.Shop;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 商店系统 2026-11-05 改版向导：把原来的大商店窗口改建成
    /// 「建筑区右下角入口 + 240×90 轻量化浮窗」。
    ///
    /// 一次运行会完成：
    ///   · 重建 Canvas/ShopPanel 的层级（标题 / 垃圾 / 原料列表 / 价格 / 购买 / 状态）；
    ///   · 保留并隐藏旧的「照明工具升级卡」，不删除它的引用；
    ///   · 创建木板原料资产，并把木板加进风琴博物馆的建造需求；
    ///   · 把木板登记进 SaveCatalog；
    ///   · 在 Canvas 下创建建筑区「商店」入口按钮；
    ///   · 删除底部导航的「商店」按钮并把剩余两个按钮居中；
    ///   · 清空 GameAreaController 的 shopAreaPanels（商店不再随区域自动显示）。
    ///
    /// 幂等：重复运行不会重复创建木板、配方或入口按钮。
    ///
    /// 运行入口：菜单栏 → 屿见 → 商店 → 重建轻量化商店窗口
    /// </summary>
    public static class ShopSetupWizard
    {
        private const string PanelName = "ShopPanel";
        private const string EntryButtonName = "ShopEntryButton";
        private const string ButtonTemplateName = "ButtonTemplate";
        private const string LightCardName = "MiningToolUpgradeCard";

        private const string BrickPath = "Assets/Data/Materials/Material_砖块.asset";
        private const string WoodPath = "Assets/Data/Materials/Material_木板.asset";
        private const string OrganMuseumPath = "Assets/Data/Buildings/Building_风琴博物馆.asset";
        private const string SaveCatalogPath = "Assets/Data/Save/SaveCatalog.asset";

        private const float ItemHeight = 52f;

        // ---------------- 配色 ----------------
        private static readonly Color TitleColor = new Color(0.96f, 0.96f, 0.96f, 1f);
        private static readonly Color BodyColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        private static readonly Color DimColor = new Color(0.72f, 0.72f, 0.72f, 1f);
        private static readonly Color PriceColor = new Color(1f, 0.85f, 0.42f, 1f);
        private static readonly Color LabelOnButtonColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        private static readonly Color SelectionColor = new Color(1f, 0.84f, 0.42f, 0.22f);
        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.80f);

        /// <summary>
        /// 批处理验证专用：打开主场景 → 配置 → 保存。
        /// 菜单入口仍然只做配置并留给用户 Ctrl+S，不会自动改场景文件。
        /// </summary>
        public static void ConfigureAndSaveForBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            Configure();
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("屿见/商店/重建轻量化商店窗口", false, 410)]
        public static void Configure()
        {
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();

            if (canvas == null)
            {
                Fail("场景里找不到 Canvas，无法重建商店窗口。");
                return;
            }

            Transform panel = canvas.transform.Find(PanelName);

            if (panel == null)
            {
                Fail($"Canvas 下找不到「{PanelName}」，无法重建商店窗口。");
                return;
            }

            ShopUI shopUI = panel.GetComponent<ShopUI>();

            if (shopUI == null)
            {
                Fail($"「{PanelName}」上没有 ShopUI 组件，无法接线。");
                return;
            }

            MaterialData brick = AssetDatabase.LoadAssetAtPath<MaterialData>(BrickPath);

            if (brick == null)
            {
                Fail($"找不到砖块原料资产 {BrickPath}。");
                return;
            }

            MaterialData wood = EnsureWoodMaterial();

            if (wood == null)
            {
                Fail($"创建木板原料资产失败：{WoodPath}");
                return;
            }

            EnsureSaveCatalogMaterial(wood);
            EnsureOrganMuseumWood(wood);

            GameAreaController areas = Object.FindFirstObjectByType<GameAreaController>(
                FindObjectsInactive.Include);

            if (areas == null)
            {
                Fail("场景里找不到 GameAreaController，商店入口无法随建筑区显隐。");
                return;
            }

            // 保留照明工具升级卡：它是旧商店里已有的功能，本次只是不显示，不删除引用。
            Transform lightCard = panel.Find(LightCardName);

            if (lightCard != null)
            {
                lightCard.gameObject.SetActive(false);
            }

            ClearChildrenExcept(panel, lightCard);
            ShopWindowLayout layout = ConfigurePanelRect(panel, canvas);

            if (layout == null)
            {
                return;
            }

            BuildContents(panel, brick, wood, shopUI);
            layout.ApplyLayout();
            EditorUtility.SetDirty(layout);
            EnsureEntryButton(canvas, panel.gameObject, areas);
            RemoveBottomNavShopButton(canvas);
            ClearShopAreaPanels(areas);

            // 关闭状态进 Play：由 ShopWindowController 在建筑区点入口时再打开。
            panel.gameObject.SetActive(false);

            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            Selection.activeGameObject = panel.gameObject;
            EditorGUIUtility.PingObject(panel.gameObject);
            AssetDatabase.SaveAssets();

            Debug.Log("[ShopSetupWizard] 轻量化商店配置完成：\n" +
                      $"  · {PanelName}：{layout.WindowSize.x:0}×{layout.WindowSize.y:0}，" +
                      "建筑区右下角浮窗（尺寸可在 ShopWindowLayout 里调）\n" +
                      $"  · 商品目录：{brick.MaterialName}、{wood.MaterialName}\n" +
                      $"  · 木板已加入「风琴博物馆」建造需求（1 个）并登记存档目录\n" +
                      $"  · Canvas/{EntryButtonName}：只在建筑区显示\n" +
                      "  · 底部导航「商店」已移除\n" +
                      "  · 照明工具升级卡已保留但隐藏\n" +
                      "请按 Ctrl+S 保存场景。");
        }

        // ---------------- 场景层级 ----------------

        private static void ClearChildrenExcept(Transform panel, Transform keep)
        {
            for (int i = panel.childCount - 1; i >= 0; i--)
            {
                Transform child = panel.GetChild(i);

                if (keep != null && child == keep)
                {
                    continue;
                }

                Undo.DestroyObjectImmediate(child.gameObject);
            }
        }

        private static ShopWindowLayout ConfigurePanelRect(Transform panel, Canvas canvas)
        {
            VerticalLayoutGroup oldLayout = panel.GetComponent<VerticalLayoutGroup>();

            if (oldLayout != null)
            {
                Undo.DestroyObjectImmediate(oldLayout);
            }

            RectTransform rect = panel.GetComponent<RectTransform>();

            if (rect == null)
            {
                Fail($"「{PanelName}」上没有 RectTransform。");
                return null;
            }

            // 尺寸交给 ShopWindowLayout：已有组件就沿用用户配置，没有就补一个默认 240×90。
            // 重跑向导不会再把尺寸冲回写死的值。
            ShopWindowLayout layout = panel.GetComponent<ShopWindowLayout>();

            if (layout == null)
            {
                layout = Undo.AddComponent<ShopWindowLayout>(panel.gameObject);
            }

            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-370f, 240f);
            rect.sizeDelta = layout.WindowSize;

            Image background = panel.GetComponent<Image>();

            if (background == null)
            {
                background = Undo.AddComponent<Image>(panel.gameObject);
            }

            background.color = PanelColor;
            background.raycastTarget = true;

            Outline outline = panel.GetComponent<Outline>();

            if (outline == null)
            {
                outline = Undo.AddComponent<Outline>(panel.gameObject);
            }

            outline.effectColor = new Color(1f, 1f, 1f, 0.28f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;

            ShopWindowDrag drag = panel.GetComponent<ShopWindowDrag>();

            if (drag == null)
            {
                drag = Undo.AddComponent<ShopWindowDrag>(panel.gameObject);
            }

            drag.ConfigureForSetup(canvas);
            EditorUtility.SetDirty(drag);
            EditorUtility.SetDirty(background);
            EditorUtility.SetDirty(layout);
            return layout;
        }

        private static void BuildContents(Transform panel, MaterialData brick, MaterialData wood, ShopUI shopUI)
        {
            Text titleText = CreateText(panel, "TitleText", "原料商店", 10, TitleColor, TextAnchor.MiddleLeft);
            titleText.fontStyle = FontStyle.Bold;
            Place(titleText.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                  new Vector2(6f, -4f), new Vector2(120f, 12f));

            Text moneyText = CreateText(panel, "MoneyText", "垃圾 0", 8, BodyColor, TextAnchor.MiddleRight);
            Place(moneyText.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                  new Vector2(-6f, -4f), new Vector2(96f, 12f));

            Text statusText = CreateText(panel, "StatusText", string.Empty, 8, BodyColor, TextAnchor.MiddleLeft);
            Place(statusText.gameObject, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                  new Vector2(6f, 4f), new Vector2(144f, 10f));

            Transform itemContainer;
            RectTransform itemTemplate = BuildScroll(panel, out itemContainer);

            Text priceText = CreateText(panel, "PriceText", "单价 —\n总价 —", 9, PriceColor, TextAnchor.MiddleRight);
            Place(priceText.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                  new Vector2(-4f, 42f), new Vector2(84f, 30f));

            Button buyButton = CreateButton(panel, "BuyButton", "购买", 10);
            Place(buyButton.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                  new Vector2(-4f, 16f), new Vector2(84f, 24f));

            // 给旧向导留一个可复用的隐藏按钮模板（MiningToolUpgradeSetupWizard 等会找它）。
            Button template = CreateButton(panel, ButtonTemplateName, "按钮", 12);
            Place(template.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                  Vector2.zero, new Vector2(160f, 44f));
            template.gameObject.SetActive(false);

            WireShopUI(shopUI, brick, wood, titleText, moneyText, priceText, statusText,
                       itemContainer, itemTemplate, buyButton);
        }

        private static RectTransform BuildScroll(Transform panel, out Transform itemContainer)
        {
            GameObject scrollGo = CreateUIObject("ItemScroll", panel);
            Place(scrollGo, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                  new Vector2(4f, 16f), new Vector2(150f, 56f));

            ScrollRect scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 20f;
            scroll.inertia = false;

            GameObject viewport = CreateUIObject("Viewport", scrollGo.transform);
            Place(viewport, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            Image viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0f);
            viewportImage.raycastTarget = true;
            viewport.AddComponent<RectMask2D>();

            GameObject content = CreateUIObject("Content", viewport.transform);
            RectTransform contentRect = Place(content, new Vector2(0f, 1f), new Vector2(1f, 1f),
                                              new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);

            VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = 2f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = (RectTransform)viewport.transform;
            scroll.content = contentRect;

            RectTransform itemTemplate = BuildItemTemplate(content.transform);
            itemContainer = content.transform;
            return itemTemplate;
        }

        private static RectTransform BuildItemTemplate(Transform parent)
        {
            GameObject root = CreateUIObject("ItemTemplate", parent);
            Place(root, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                  Vector2.zero, new Vector2(0f, ItemHeight));

            LayoutElement rootLayout = root.AddComponent<LayoutElement>();
            rootLayout.preferredHeight = ItemHeight;
            rootLayout.minHeight = ItemHeight;

            Image background = root.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.06f);
            background.raycastTarget = true;

            Button selectButton = root.AddComponent<Button>();
            selectButton.targetGraphic = background;

            GameObject highlight = CreateUIObject("SelectionHighlight", root.transform);
            Place(highlight, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Image highlightImage = highlight.AddComponent<Image>();
            highlightImage.color = SelectionColor;
            highlightImage.raycastTarget = false;
            highlightImage.enabled = false;

            Text nameText = CreateText(root.transform, "NameText", "原料", 9, TitleColor, TextAnchor.MiddleLeft);
            nameText.fontStyle = FontStyle.Bold;
            Place(nameText.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                  new Vector2(4f, -3f), new Vector2(92f, 11f));

            Text styleText = CreateText(root.transform, "StyleText", "样式：砖块", 7, DimColor, TextAnchor.MiddleLeft);
            Place(styleText.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                  new Vector2(4f, -14f), new Vector2(92f, 9f));

            Text effectText = CreateText(root.transform, "EffectText", "说明", 7, DimColor, TextAnchor.MiddleLeft);
            Place(effectText.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                  new Vector2(4f, -24f), new Vector2(142f, 9f));

            GameObject quantityRoot = CreateUIObject("QuantityRoot", root.transform);
            Place(quantityRoot, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                  new Vector2(-4f, -3f), new Vector2(46f, 20f));

            Text quantityLabel = CreateText(quantityRoot.transform, "QuantityLabel", "数量", 7,
                                            DimColor, TextAnchor.MiddleCenter);
            Place(quantityLabel.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                  new Vector2(0f, -1f), new Vector2(0f, 8f));

            Button minusButton = CreateButton(quantityRoot.transform, "MinusButton", "-", 11);
            Place(minusButton.gameObject, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                  Vector2.zero, new Vector2(14f, 12f));

            Text quantityText = CreateText(quantityRoot.transform, "QuantityText", "1", 8,
                                           BodyColor, TextAnchor.MiddleCenter);
            Place(quantityText.gameObject, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                  new Vector2(14f, 0f), new Vector2(18f, 12f));

            Button plusButton = CreateButton(quantityRoot.transform, "PlusButton", "+", 11);
            Place(plusButton.gameObject, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                  new Vector2(32f, 0f), new Vector2(14f, 12f));

            ShopItemUI item = root.AddComponent<ShopItemUI>();
            WireShopItem(item, nameText, styleText, effectText, quantityText,
                         selectButton, highlightImage, minusButton, plusButton);

            root.SetActive(false);
            return (RectTransform)root.transform;
        }

        // ---------------- 接线 ----------------

        private static void WireShopUI(ShopUI shopUI, MaterialData brick, MaterialData wood,
                                       Text titleText, Text moneyText, Text priceText, Text statusText,
                                       Transform itemContainer, RectTransform itemTemplate, Button buyButton)
        {
            SerializedObject so = new SerializedObject(shopUI);

            SerializedProperty shopProperty = so.FindProperty("shop");

            if (shopProperty != null && shopProperty.objectReferenceValue == null)
            {
                Yujian.Shop.Shop found = Object.FindFirstObjectByType<Yujian.Shop.Shop>();

                if (found != null)
                {
                    shopProperty.objectReferenceValue = found;
                }
                else
                {
                    Debug.LogWarning("[ShopSetupWizard] 场景里找不到 Shop 组件，购买按钮点下去会报错。");
                }
            }

            SerializedProperty materialsProperty = so.FindProperty("materials");

            if (materialsProperty != null)
            {
                materialsProperty.arraySize = 2;
                materialsProperty.GetArrayElementAtIndex(0).objectReferenceValue = brick;
                materialsProperty.GetArrayElementAtIndex(1).objectReferenceValue = wood;
            }

            SetObject(so, "titleText", titleText);
            SetObject(so, "moneyText", moneyText);
            SetObject(so, "priceText", priceText);
            SetObject(so, "statusText", statusText);
            SetObject(so, "itemContainer", itemContainer);
            SetObject(so, "itemTemplate", itemTemplate);
            SetObject(so, "buyButton", buyButton);
            SetString(so, "title", "原料商店");
            SetString(so, "successMessage", "成功购买");
            SetString(so, "insufficientMessage", "当前货币不足");
            SetString(so, "failureMessage", "购买失败");

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(shopUI);
        }

        private static void WireShopItem(ShopItemUI item, Text nameText, Text styleText, Text effectText,
                                         Text quantityText,
                                         Button selectButton, Image selectionHighlight,
                                         Button minusButton, Button plusButton)
        {
            SerializedObject so = new SerializedObject(item);
            SetObject(so, "nameText", nameText);
            SetObject(so, "styleText", styleText);
            SetObject(so, "effectText", effectText);
            SetObject(so, "quantityText", quantityText);
            SetObject(so, "selectButton", selectButton);
            SetObject(so, "selectionHighlight", selectionHighlight);
            SetObject(so, "minusButton", minusButton);
            SetObject(so, "plusButton", plusButton);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(item);
        }

        // ---------------- 入口与导航 ----------------

        private static void EnsureEntryButton(Canvas canvas, GameObject shopWindow, GameAreaController areas)
        {
            Transform existing = canvas.transform.Find(EntryButtonName);

            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            Button entry = CreateButton(canvas.transform, EntryButtonName, "商店", 14);
            Place(entry.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                  new Vector2(-370f, 160f), new Vector2(120f, 64f));
            entry.gameObject.SetActive(false);

            ShopWindowController controller = canvas.GetComponent<ShopWindowController>();

            if (controller == null)
            {
                controller = Undo.AddComponent<ShopWindowController>(canvas.gameObject);
            }

            controller.ConfigureForSetup(areas, shopWindow, entry.gameObject);
            EditorUtility.SetDirty(controller);

            Debug.Log($"[ShopSetupWizard] 已创建建筑区商店入口 Canvas/{EntryButtonName}。");
        }

        private static void RemoveBottomNavShopButton(Canvas canvas)
        {
            Transform bottomNav = canvas.transform.Find("BottomNav");

            if (bottomNav == null)
            {
                Debug.LogWarning("[ShopSetupWizard] Canvas 下找不到 BottomNav，底部「商店」按钮未处理。");
                return;
            }

            Transform shopButton = bottomNav.Find("Button_商店");

            if (shopButton != null)
            {
                Undo.DestroyObjectImmediate(shopButton.gameObject);
                Debug.Log("[ShopSetupWizard] 已删除底部导航「商店」按钮。");
            }
            else
            {
                Debug.Log("[ShopSetupWizard] 底部导航已经没有「商店」按钮（重复运行属正常）。");
            }

            // 剩余两个按钮居中，避免去掉第三个后整体偏左。
            MoveBottomButton(bottomNav, "Button_挖矿", -170f);
            MoveBottomButton(bottomNav, "Button_建筑", 170f);
        }

        private static void MoveBottomButton(Transform bottomNav, string childName, float x)
        {
            Transform child = bottomNav.Find(childName);

            if (child == null)
            {
                Debug.LogWarning($"[ShopSetupWizard] BottomNav 下找不到「{childName}」，未调整位置。");
                return;
            }

            RectTransform rect = child.GetComponent<RectTransform>();

            if (rect != null)
            {
                rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
                EditorUtility.SetDirty(rect);
            }
        }

        private static void ClearShopAreaPanels(GameAreaController areas)
        {
            SerializedObject so = new SerializedObject(areas);
            SerializedProperty panels = so.FindProperty("shopAreaPanels");

            if (panels == null)
            {
                Debug.LogWarning("[ShopSetupWizard] GameAreaController 上找不到 shopAreaPanels 字段，" +
                                 "商店面板仍会随区域自动显隐，可能与浮窗控制冲突。");
                return;
            }

            panels.arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(areas);
        }

        // ---------------- 数据资产 ----------------

        private static MaterialData EnsureWoodMaterial()
        {
            MaterialData wood = AssetDatabase.LoadAssetAtPath<MaterialData>(WoodPath);

            if (wood != null)
            {
                EnsureSaveId(wood, WoodPath);
                return wood;
            }

            wood = ScriptableObject.CreateInstance<MaterialData>();
            AssetDatabase.CreateAsset(wood, WoodPath);

            string guid = AssetDatabase.AssetPathToGUID(WoodPath);
            SerializedObject so = new SerializedObject(wood);
            SetString(so, "materialName", "木板");
            SetEnum(so, "materialType", (int)MaterialType.Wood);
            SetInt(so, "price", 2);
            SetFloat(so, "damageMultiplierBonus", 0f);
            SetFloat(so, "buildingDamageMultiplierBonus", 0.1f);

            SerializedProperty displayColor = so.FindProperty("displayColor");

            if (displayColor != null)
            {
                displayColor.colorValue = new Color(0.62f, 0.42f, 0.24f, 1f);
            }

            SetString(so, "saveId", guid);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(wood);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ShopSetupWizard] 已创建木板原料资产 {WoodPath}（价格 2，建筑伤害倍率 +0.1）。");
            return wood;
        }

        private static void EnsureSaveId(MaterialData material, string path)
        {
            SerializedObject so = new SerializedObject(material);
            SerializedProperty saveId = so.FindProperty("saveId");

            if (saveId == null || !string.IsNullOrWhiteSpace(saveId.stringValue))
            {
                return;
            }

            saveId.stringValue = AssetDatabase.AssetPathToGUID(path);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        private static void EnsureSaveCatalogMaterial(MaterialData wood)
        {
            SaveCatalog catalog = AssetDatabase.LoadAssetAtPath<SaveCatalog>(SaveCatalogPath);

            if (catalog == null)
            {
                Debug.LogWarning($"[ShopSetupWizard] 找不到存档目录 {SaveCatalogPath}，木板暂时不会写进存档。" +
                                 "请运行菜单「屿见/通用模块/配置存档系统」。");
                return;
            }

            SerializedObject so = new SerializedObject(catalog);
            SerializedProperty materials = so.FindProperty("materials");

            if (materials == null)
            {
                Debug.LogWarning("[ShopSetupWizard] SaveCatalog 上找不到 materials 字段，木板未登记。");
                return;
            }

            for (int i = 0; i < materials.arraySize; i++)
            {
                if (materials.GetArrayElementAtIndex(i).objectReferenceValue == wood)
                {
                    return;
                }
            }

            materials.arraySize++;
            materials.GetArrayElementAtIndex(materials.arraySize - 1).objectReferenceValue = wood;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[ShopSetupWizard] 已把木板登记进 SaveCatalog。");
        }

        private static void EnsureOrganMuseumWood(MaterialData wood)
        {
            BuildingData organ = AssetDatabase.LoadAssetAtPath<BuildingData>(OrganMuseumPath);

            if (organ == null)
            {
                Debug.LogWarning($"[ShopSetupWizard] 找不到风琴博物馆资产 {OrganMuseumPath}，木板未加入配方。");
                return;
            }

            SerializedObject so = new SerializedObject(organ);
            SerializedProperty requirements = so.FindProperty("requiredMaterials");

            if (requirements == null)
            {
                Debug.LogWarning("[ShopSetupWizard] BuildingData 上找不到 requiredMaterials 字段，木板未加入配方。");
                return;
            }

            for (int i = 0; i < requirements.arraySize; i++)
            {
                SerializedProperty element = requirements.GetArrayElementAtIndex(i);

                if (element.FindPropertyRelative("material").objectReferenceValue == wood)
                {
                    return;
                }
            }

            requirements.arraySize++;
            SerializedProperty added = requirements.GetArrayElementAtIndex(requirements.arraySize - 1);
            added.FindPropertyRelative("material").objectReferenceValue = wood;
            added.FindPropertyRelative("amount").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(organ);
            AssetDatabase.SaveAssets();

            Debug.Log("[ShopSetupWizard] 已把「木板 ×1」加入风琴博物馆的建造需求。");
        }

        // ---------------- 通用小工具 ----------------

        private static Font BuiltinFont()
        {
            // 项目规则：不引入 TextMeshPro，UI 文字用旧版 Text + 内置字体
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static Sprite ButtonSprite()
        {
            Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            if (sprite == null)
            {
                sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.png");
            }

            return sprite;
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI 层，和 Canvas 下其它物体一致
            go.transform.SetParent(parent, false);
            return go;
        }

        private static RectTransform Place(GameObject go, Vector2 anchorMin, Vector2 anchorMax,
                                           Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return rect;
        }

        private static Text CreateText(Transform parent, string name, string content, int fontSize,
                                       Color color, TextAnchor alignment)
        {
            GameObject go = CreateUIObject(name, parent);
            Text text = go.AddComponent<Text>();

            text.font = BuiltinFont();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;

            return text;
        }

        private static Button CreateButton(Transform parent, string name, string label, int fontSize)
        {
            GameObject go = CreateUIObject(name, parent);

            Image image = go.AddComponent<Image>();
            image.sprite = ButtonSprite();
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = true;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.95f, 0.95f, 0.95f, 1f);
            colors.pressedColor = new Color(0.80f, 0.80f, 0.80f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.50f, 0.50f, 0.50f, 0.5f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.1f;
            button.colors = colors;

            Text text = CreateText(go.transform, "Text", label, fontSize,
                                   LabelOnButtonColor, TextAnchor.MiddleCenter);
            Place(text.gameObject, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                  Vector2.zero, Vector2.zero);

            return button;
        }

        private static void SetObject(SerializedObject so, string fieldName, Object value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.objectReferenceValue = value;
                return;
            }

            Debug.LogWarning($"[ShopSetupWizard] {so.targetObject.GetType().Name} 上找不到字段「{fieldName}」，" +
                             "请检查字段名与脚本是否一致。");
        }

        private static void SetString(SerializedObject so, string fieldName, string value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.stringValue = value;
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

        private static void SetEnum(SerializedObject so, string fieldName, int enumValue)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.enumValueIndex = enumValue;
            }
        }

        private static void Fail(string message)
        {
            EditorUtility.DisplayDialog("商店配置未执行", message, "知道了");
            Debug.LogError("[ShopSetupWizard] " + message);
        }
    }
}
