using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Shop;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 原料商店面板的一键重构工具。
    ///
    /// 把 ShopPanel 从「手接的单原料 + 一个颜色一个按钮」
    /// 改造成「模板克隆的原料模块列表 + 上一个/下一个 选色」。
    ///
    /// 为什么用编辑器脚本而不是直接改 .unity：Unity 打开着场景时从外部改文件会让编辑器整场重载，
    /// 未保存的改动会一起丢失（见 开发进度.md 踩坑 2 / 项目规则 13）。
    ///
    /// 本工具**幂等**：面板子物体会被整体重建；色块 PNG 已存在就跳过，**绝不覆盖**，
    /// 所以你以后把占位图换成美术图，重跑本命令不会把图冲掉。
    ///
    /// 运行入口：菜单栏 → 屿见 → 配置原料商店（阶段 2 重构）
    /// </summary>
    public static class ShopSetupWizard
    {
        private const string PanelName = "ShopPanel";
        private const string ButtonTemplateName = "ButtonTemplate";

        /// <summary>旧版「一个颜色一个按钮」里的那个按钮，第一次运行时拿它当按钮模板。</summary>
        private const string LegacyColorButtonName = "RedButton";

        private const string SwatchFolder = "Assets/Textures/Materials";
        private const string MaterialDataPath = "Assets/Data/Materials/Material_砖块.asset";

        private const int SwatchSize = 64;
        private const int SwatchBorder = 4;

        // ---------------- 配色 ----------------
        // 面板底图是半透明黑，深色字在上面基本读不出来，新建的文字一律用浅色。
        private static readonly Color TitleColor = new Color(0.96f, 0.96f, 0.96f, 1f);
        private static readonly Color BodyColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        private static readonly Color DimColor = new Color(0.72f, 0.72f, 0.72f, 1f);
        private static readonly Color PriceColor = new Color(1f, 0.85f, 0.42f, 1f);

        /// <summary>按钮底图是白色，按钮上的字必须用深色。</summary>
        private static readonly Color LabelOnButtonColor = new Color(0.15f, 0.15f, 0.15f, 1f);

        // ---------------- 尺寸 ----------------
        private const float TitleHeight = 44f;
        private const float MoneyHeight = 34f;
        private const float StatusHeight = 30f;
        private const float ItemNameHeight = 30f;
        private const float ItemLineHeight = 22f;
        private const float ColorRowHeight = 52f;
        private const float SwatchSide = 48f;
        private const float SmallButtonWidth = 76f;
        private const float SmallButtonHeight = 40f;
        private const float BuyButtonWidth = 160f;
        private const float BuyButtonHeight = 44f;

        [MenuItem("屿见/配置原料商店（阶段 2 重构）")]
        public static void Configure()
        {
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();

            if (canvas == null)
            {
                Fail("场景里找不到 Canvas，无法重建商店面板。");
                return;
            }

            Transform panel = canvas.transform.Find(PanelName);

            if (panel == null)
            {
                Fail($"Canvas 下找不到「{PanelName}」，无法重建商店面板。");
                return;
            }

            ShopUI shopUI = panel.GetComponent<ShopUI>();

            if (shopUI == null)
            {
                Fail($"「{PanelName}」上没有 ShopUI 组件，无法接线。");
                return;
            }

            MaterialData materialAsset = AssetDatabase.LoadAssetAtPath<MaterialData>(MaterialDataPath);

            if (materialAsset == null)
            {
                Fail($"找不到原料数据资产 {MaterialDataPath}。");
                return;
            }

            // ---------- 1. 按钮模板 ----------
            // 必须在清空子物体之前克隆出来：旧的颜色按钮马上就会被删掉
            GameObject buttonProto = CloneButtonPrototype(panel);

            if (buttonProto == null)
            {
                Fail("商店面板里找不到任何 Button 可以当模板，无法创建按钮。\n\n" +
                     "请先确认 ShopPanel 下还有按钮，或从别的面板借一个。");
                return;
            }

            // ---------- 2. 清空旧子物体 ----------
            // 只清子物体：ShopPanel 本体、它的 Image、VerticalLayoutGroup、ShopUI 都保留，
            // 这样场景里指向 ShopUI 的引用不会断。
            for (int i = panel.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(panel.GetChild(i).gameObject);
            }

            // ---------- 3. 色块贴图 ----------
            Sprite defaultSwatch = EnsureSwatchSprite(materialAsset.DefaultColor);

            // ---------- 4. 重建层级 ----------
            Text titleText = CreateText(panel, "TitleText", "原料商店", 22, TextAnchor.MiddleCenter, TitleColor, TitleHeight);
            titleText.fontStyle = FontStyle.Bold;

            Transform itemContainer = CreateContainer(panel, "ItemContainer", 8f, 10f);
            RectTransform itemTemplate = CreateItemTemplate(itemContainer, buttonProto, defaultSwatch);

            Text moneyText = CreateText(panel, "MoneyText", "金币：0", 16, TextAnchor.MiddleLeft, BodyColor, MoneyHeight);
            Text statusText = CreateText(panel, "StatusText", string.Empty, 15, TextAnchor.MiddleCenter, BodyColor, StatusHeight);

            // 按钮模板挂在最前面并保持隐藏：
            // 阶段 3/4 的向导用 GetComponentsInChildren<Button>(true) 找模板，那会**遍历到隐藏物体**。
            // 把全尺寸的模板排在第一个，它们就仍然挑到和以前一样大的按钮，
            // 不会误挑条目里 40×40 的小按钮（那会把建造面板的按钮缩小）。
            buttonProto.transform.SetParent(panel, false);
            buttonProto.name = ButtonTemplateName;
            buttonProto.transform.SetSiblingIndex(0);
            buttonProto.SetActive(false);

            // ---------- 5. 接线 ----------
            WirePanel(shopUI, materialAsset, titleText, moneyText, statusText, itemContainer, itemTemplate);
            WireColorSprites(shopUI);

            // ---------- 6. 收尾 ----------
            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            Selection.activeGameObject = panel.gameObject;
            EditorGUIUtility.PingObject(panel.gameObject);

            Debug.Log("[ShopSetupWizard] 配置完成：\n" +
                      $"  · {PanelName}：重建为 标题 / 条目容器 / 条目模板 / 金币 / 状态行\n" +
                      $"  · {ButtonTemplateName}：全尺寸按钮模板（隐藏），放在第一个子物体位置\n" +
                      $"  · 商品目录：{materialAsset.MaterialName}\n" +
                      $"  · 色块贴图：{SwatchFolder}/（已存在的文件不会被覆盖）\n" +
                      "请按 Ctrl+S 保存场景。\n" +
                      "注意：阶段 3/4 的向导若再运行，会挑 ButtonTemplate 作按钮模板 —— 这是有意为之，" +
                      "尺寸与以前一致。");
        }

        // ---------------- 按钮 ----------------

        /// <summary>克隆一个脱离层级的按钮当模板。优先复用上一轮建的 ButtonTemplate，其次旧的 RedButton。</summary>
        private static GameObject CloneButtonPrototype(Transform panel)
        {
            Transform source = panel.Find(ButtonTemplateName);

            if (source == null)
            {
                source = panel.Find(LegacyColorButtonName);
            }

            if (source == null)
            {
                Button any = panel.GetComponentInChildren<Button>(true);

                if (any != null)
                {
                    source = any.transform;
                }
            }

            if (source == null)
            {
                return null;
            }

            GameObject proto = Object.Instantiate(source.gameObject);
            proto.name = ButtonTemplateName + "（临时）";

            // 持久监听会跟着克隆过来，必须清掉，否则新按钮会连带触发旧按钮的行为
            Button protoButton = proto.GetComponent<Button>();

            if (protoButton != null)
            {
                SerializedObject so = new SerializedObject(protoButton);
                SerializedProperty calls = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");

                if (calls != null)
                {
                    calls.ClearArray();
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            return proto;
        }

        private static Button CreateButtonFromTemplate(Transform parent, GameObject proto, string name, string label,
                                                       float width, float height)
        {
            GameObject go = Object.Instantiate(proto, parent);
            go.name = name;
            go.SetActive(true);
            SetPreferredSize(go, width, height);

            Text labelText = go.GetComponentInChildren<Text>(true);

            if (labelText != null)
            {
                labelText.text = label;
                labelText.font = BuiltinFont();
                labelText.fontSize = 16;
                labelText.fontStyle = FontStyle.Normal;
                labelText.alignment = TextAnchor.MiddleCenter;
                labelText.raycastTarget = false;
                labelText.color = LabelOnButtonColor;
            }
            else
            {
                Debug.LogWarning($"[ShopSetupWizard] 按钮模板上没有 Text 子物体，「{label}」按钮会没有文字。");
            }

            Button button = go.GetComponent<Button>();

            if (button == null)
            {
                Debug.LogWarning($"[ShopSetupWizard] 按钮模板上没有 Button 组件，「{label}」将不可点击。");
            }

            return button;
        }

        // ---------------- 条目模板 ----------------

        private static RectTransform CreateItemTemplate(Transform parent, GameObject buttonProto, Sprite defaultSwatch)
        {
            GameObject root = CreateUIObject("ItemTemplate", parent);
            RectTransform rect = root.GetComponent<RectTransform>();

            // 刻意不给条目根节点挂 LayoutElement：
            // VerticalLayoutGroup 自己就是 ILayoutElement，会按内容算出首选高度并传给外层；
            // 一旦挂上 LayoutElement，那个值会反过来覆盖布局组的计算结果。
            ConfigureVertical(root.AddComponent<VerticalLayoutGroup>(), 10f, 6f);
            ShopItemUI item = root.AddComponent<ShopItemUI>();

            // 原料名称
            Text nameText = CreateText(root.transform, "NameText", "原料", 20, TextAnchor.MiddleCenter, TitleColor, ItemNameHeight);
            nameText.fontStyle = FontStyle.Bold;

            // 原料类型
            Text typeText = CreateText(root.transform, "TypeText", "类型：", 15, TextAnchor.MiddleCenter, DimColor, ItemLineHeight);

            // ---- 原料说明块：上半部分是颜色选择器，下半部分是效果与持有数量 ----
            GameObject description = CreateUIObject("DescriptionRoot", root.transform);
            Image descriptionBackground = description.AddComponent<Image>();
            descriptionBackground.sprite = null;
            descriptionBackground.color = new Color(1f, 1f, 1f, 0.06f);
            descriptionBackground.raycastTarget = false;
            ConfigureVertical(description.AddComponent<VerticalLayoutGroup>(), 8f, 4f);

            // 颜色选择行：[上一个] [色块] [下一个]
            GameObject colorRow = CreateUIObject("ColorRow", description.transform);
            HorizontalLayoutGroup colorRowLayout = colorRow.AddComponent<HorizontalLayoutGroup>();
            colorRowLayout.padding = new RectOffset(0, 0, 2, 2);
            colorRowLayout.spacing = 12f;
            colorRowLayout.childAlignment = TextAnchor.MiddleCenter;
            colorRowLayout.childControlWidth = true;
            colorRowLayout.childControlHeight = true;
            colorRowLayout.childForceExpandWidth = false;
            colorRowLayout.childForceExpandHeight = false;
            SetPreferredSize(colorRow, -1f, ColorRowHeight);

            Button previousButton = CreateButtonFromTemplate(colorRow.transform, buttonProto,
                                                             "PrevButton", "上一个", SmallButtonWidth, SmallButtonHeight);
            Image swatchImage = CreateSwatch(colorRow.transform, defaultSwatch);
            Button nextButton = CreateButtonFromTemplate(colorRow.transform, buttonProto,
                                                         "NextButton", "下一个", SmallButtonWidth, SmallButtonHeight);

            Text colorNameText = CreateText(description.transform, "ColorNameText", "红色", 15, TextAnchor.MiddleCenter, BodyColor, ItemLineHeight);
            Text effectText = CreateText(description.transform, "EffectText", "效果：", 14, TextAnchor.MiddleCenter, DimColor, ItemLineHeight);
            Text ownedText = CreateText(description.transform, "OwnedText", "持有：", 14, TextAnchor.MiddleCenter, DimColor, ItemLineHeight);

            // 价格与购买
            Text priceText = CreateText(root.transform, "PriceText", "价格：", 16, TextAnchor.MiddleCenter, PriceColor, ItemLineHeight + 4f);
            Button buyButton = CreateButtonFromTemplate(root.transform, buttonProto,
                                                        "BuyButton", "购买", BuyButtonWidth, BuyButtonHeight);

            // 模板本身不显示，只在运行时被克隆
            root.SetActive(false);

            SerializedObject so = new SerializedObject(item);
            SetObject(so, "nameText", nameText);
            SetObject(so, "typeText", typeText);
            SetObject(so, "colorNameText", colorNameText);
            SetObject(so, "effectText", effectText);
            SetObject(so, "ownedText", ownedText);
            SetObject(so, "priceText", priceText);
            SetObject(so, "swatchImage", swatchImage);
            SetObject(so, "previousButton", previousButton);
            SetObject(so, "nextButton", nextButton);
            SetObject(so, "buyButton", buyButton);
            so.ApplyModifiedPropertiesWithoutUndo();

            return rect;
        }

        private static Image CreateSwatch(Transform parent, Sprite sprite)
        {
            GameObject go = CreateUIObject("Swatch", parent);
            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = false;
            SetPreferredSize(go, SwatchSide, SwatchSide);
            return image;
        }

        // ---------------- ShopUI 接线 ----------------

        private static void WirePanel(ShopUI shopUI, MaterialData materialAsset, Text titleText, Text moneyText,
                                      Text statusText, Transform itemContainer, RectTransform itemTemplate)
        {
            SerializedObject so = new SerializedObject(shopUI);

            // shop 引用保持原样；万一为空就找场景里的补上，别让购买按钮点了才报错
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
                materialsProperty.arraySize = 1;
                materialsProperty.GetArrayElementAtIndex(0).objectReferenceValue = materialAsset;
            }

            SetObject(so, "titleText", titleText);
            SetObject(so, "moneyText", moneyText);
            SetObject(so, "statusText", statusText);
            SetObject(so, "itemContainer", itemContainer);
            SetObject(so, "itemTemplate", itemTemplate);

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireColorSprites(ShopUI shopUI)
        {
            SerializedObject so = new SerializedObject(shopUI);

            // 先记下手工配过的贴图，重跑时不能覆盖掉（你可能已经换成美术图了）
            Dictionary<MaterialColor, Sprite> manual = ReadConfiguredSprites(so);
            System.Array colors = System.Enum.GetValues(typeof(MaterialColor));

            SerializedProperty spritesProperty = so.FindProperty("colorSprites");

            if (spritesProperty == null)
            {
                Debug.LogWarning("[ShopSetupWizard] ShopUI 上找不到 colorSprites 字段，色块贴图未写入。");
                return;
            }

            spritesProperty.arraySize = colors.Length;

            for (int i = 0; i < colors.Length; i++)
            {
                MaterialColor color = (MaterialColor)colors.GetValue(i);
                SerializedProperty entry = spritesProperty.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("color").intValue = (int)color;

                Sprite sprite;

                if (manual.TryGetValue(color, out Sprite configured) && configured != null)
                {
                    sprite = configured;
                }
                else
                {
                    sprite = EnsureSwatchSprite(color);
                }

                entry.FindPropertyRelative("sprite").objectReferenceValue = sprite;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>读当前已经配好的颜色 → 贴图，用来判断哪些是手工指定过的。</summary>
        private static Dictionary<MaterialColor, Sprite> ReadConfiguredSprites(SerializedObject so)
        {
            Dictionary<MaterialColor, Sprite> map = new Dictionary<MaterialColor, Sprite>();
            SerializedProperty spritesProperty = so.FindProperty("colorSprites");

            if (spritesProperty == null)
            {
                return map;
            }

            for (int i = 0; i < spritesProperty.arraySize; i++)
            {
                SerializedProperty entry = spritesProperty.GetArrayElementAtIndex(i);
                int colorValue = entry.FindPropertyRelative("color").intValue;
                Sprite sprite = entry.FindPropertyRelative("sprite").objectReferenceValue as Sprite;

                if (sprite != null)
                {
                    map[(MaterialColor)colorValue] = sprite;
                }
            }

            return map;
        }

        // ---------------- 占位色块 ----------------

        /// <summary>
        /// 保证某种颜色有一张色块贴图。
        /// **文件已存在就完全不碰它** —— 这是你以后换成美术图后重跑向导不会被冲掉的保证。
        /// </summary>
        private static Sprite EnsureSwatchSprite(MaterialColor color)
        {
            string path = $"{SwatchFolder}/Color_{color}.png";

            if (!File.Exists(path))
            {
                EnsureFolder(SwatchFolder);
                WritePlaceholderTexture(path, color);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

                if (File.Exists(path))
                {
                    Debug.Log($"[ShopSetupWizard] 生成占位色块 {path}（换成美术图直接替换该文件即可，重跑不会覆盖）。");
                }
            }

            ConfigureSwatchImporter(path);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void WritePlaceholderTexture(string path, MaterialColor color)
        {
            Color fill = color.ToDisplayColor();
            Color edge = new Color(fill.r * 0.45f, fill.g * 0.45f, fill.b * 0.45f, 1f);

            Texture2D texture = new Texture2D(SwatchSize, SwatchSize, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[SwatchSize * SwatchSize];

            for (int y = 0; y < SwatchSize; y++)
            {
                for (int x = 0; x < SwatchSize; x++)
                {
                    bool onBorder = x < SwatchBorder || y < SwatchBorder ||
                                    x >= SwatchSize - SwatchBorder || y >= SwatchSize - SwatchBorder;

                    // 描边是必须的：深色面板上纯色块和背景容易糊在一起
                    pixels[y * SwatchSize + x] = onBorder ? edge : fill;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            byte[] png = texture.EncodeToPNG();
            File.WriteAllBytes(path, png);

            Object.DestroyImmediate(texture);
        }

        private static void ConfigureSwatchImporter(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null)
            {
                Debug.LogWarning($"[ShopSetupWizard] 取不到 {path} 的贴图导入器，色块可能不是 Sprite 类型。");
                return;
            }

            bool changed = false;

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                changed = true;
            }

            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                changed = true;
            }

            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                changed = true;
            }

            // 占位图是纯平色块，Point 更锐利；换成手绘美术图时可以改回 Bilinear
            if (importer.filterMode != FilterMode.Point)
            {
                importer.filterMode = FilterMode.Point;
                changed = true;
            }

            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                changed = true;
            }

            if (importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                changed = true;
            }

            if (!importer.sRGBTexture)
            {
                importer.sRGBTexture = true;
                changed = true;
            }

            if (importer.npotScale != TextureImporterNPOTScale.None)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                changed = true;
            }

            if (importer.maxTextureSize != 128)
            {
                importer.maxTextureSize = 128;
                changed = true;
            }

            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                changed = true;
            }

            if (!Mathf.Approximately(importer.spritePixelsPerUnit, 100f))
            {
                importer.spritePixelsPerUnit = 100f;
                changed = true;
            }

            if (changed)
            {
                // 必须存盘重导，否则 AssetDatabase.LoadAssetAtPath<Sprite> 取到的还是 null
                importer.SaveAndReimport();
            }
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = Path.GetFileName(folder);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ---------------- 通用小工具 ----------------

        private static Font BuiltinFont()
        {
            // 项目规则：不引入 TextMeshPro，UI 文字用旧版 Text + 内置字体
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI 层，和 Canvas 下其它物体一致
            go.transform.SetParent(parent, false);
            return go;
        }

        private static Text CreateText(Transform parent, string name, string content, int fontSize,
                                       TextAnchor alignment, Color color, float preferredHeight)
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

            SetPreferredSize(go, -1f, preferredHeight);
            return text;
        }

        /// <summary>
        /// 建一个纵向排布的容器。
        /// **刻意不挂 LayoutElement**：VerticalLayoutGroup 自己会按子物体算出首选高度并向上汇报，
        /// 挂了 LayoutElement 反而会把这个高度覆盖掉（容器会塌成你写死的那点高度）。
        /// </summary>
        private static Transform CreateContainer(Transform parent, string name, float padding, float spacing)
        {
            GameObject go = CreateUIObject(name, parent);
            ConfigureVertical(go.AddComponent<VerticalLayoutGroup>(), padding, spacing);
            return go.transform;
        }

        private static void ConfigureVertical(VerticalLayoutGroup layout, float padding, float spacing)
        {
            int pad = Mathf.RoundToInt(padding);

            layout.padding = new RectOffset(pad, pad, pad, pad);
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        /// <summary>
        /// 给物体挂上 LayoutElement 并设定首选尺寸。
        ///
        /// 这一步不能省：ShopPanel 上的 VerticalLayoutGroup 开了 childControlHeight，
        /// 子物体由布局驱动；而 Text / Button 不会可靠地提供 preferred height，
        /// 缺了 LayoutElement 的叶节点会被压成 0 高。
        /// 传 &lt;= 0 表示该方向不指定。
        /// </summary>
        private static void SetPreferredSize(GameObject go, float width, float height)
        {
            LayoutElement element = go.GetComponent<LayoutElement>();

            if (element == null)
            {
                element = go.AddComponent<LayoutElement>();
            }

            if (width > 0f)
            {
                element.preferredWidth = width;
            }

            if (height > 0f)
            {
                element.preferredHeight = height;
            }
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

        private static void Fail(string message)
        {
            EditorUtility.DisplayDialog("原料商店配置未执行", message, "知道了");
            Debug.LogError("[ShopSetupWizard] " + message);
        }
    }
}
