using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.Player;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 建筑进度 UI 一键配置（策划案 4.1 / 4.2）。
    ///
    /// 做四件事：
    ///   1. Canvas 顶部建「BuildingProgressBanner」横幅（文字 + 进度条 + 粒子层）；
    ///   2. 在 BuildingSystem 上挂 BuildingProgressTracker 并接线；
    ///   3. 给 BuildingUI 接上特殊建筑清单，禁止同一座特殊建筑重复建造；
    ///   4. 给 BuildingPanel 挂 SpecialBuildingAvailabilityUI，置灰已建成的特殊建筑按钮，
    ///      并在 Canvas 上建一个跟随光标的提示文字。
    ///
    /// 幂等：已有物体与组件全部复用，可重复运行。
    /// 入口：菜单栏 → 屿见 → 建筑 → 配置建筑进度 UI
    /// </summary>
    public static class BuildingProgressSetupWizard
    {
        private const string BannerName = "BuildingProgressBanner";
        private const string HintName = "SpecialBuildingHint";
        private const string PanelName = "BuildingPanel";

        private static readonly Color GoldBorder = new Color(1f, 0.78f, 0.25f);
        private static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.11f, 0.94f);

        [MenuItem("屿见/建筑/配置建筑进度 UI", false, 516)]
        public static void Configure()
        {
            BuildingManager manager = Object.FindFirstObjectByType<BuildingManager>();
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            MilestoneProgress milestones = Object.FindFirstObjectByType<MilestoneProgress>();
            PlayerLevel playerLevel = Object.FindFirstObjectByType<PlayerLevel>();
            BuildingUI buildingUI = Object.FindFirstObjectByType<BuildingUI>();

            if (manager == null || canvas == null)
            {
                Fail("场景里找不到 BuildingManager 或 Canvas。\n\n" +
                     "请先运行菜单「屿见/建筑/配置建筑系统（阶段 3）」。");
                return;
            }

            if (milestones == null)
            {
                Fail("场景里找不到 MilestoneProgress（特殊建筑清单）。\n\n" +
                     "请先运行菜单「屿见/建筑/配置主要建筑里程碑」。");
                return;
            }

            if (playerLevel == null)
            {
                Fail("场景里找不到 PlayerLevel（建筑数 → 等级）。\n\n" +
                     "请先运行菜单「屿见/矿石/配置矿石进阶」把 PlayerLevel 接上。");
                return;
            }

            if (buildingUI == null)
            {
                Fail("场景里找不到 BuildingUI。\n\n" +
                     "请先运行菜单「屿见/建筑/配置建筑系统（阶段 3）」。");
                return;
            }

            Transform panel = canvas.transform.Find(PanelName);

            if (panel == null)
            {
                Fail($"Canvas/{PanelName} 不存在。\n\n" +
                     "请先运行菜单「屿见/建筑/配置建筑系统（阶段 3）」。");
                return;
            }

            // ---------- 1. 顶部横幅 ----------

            BuildingProgressBannerUI banner = BuildBanner(canvas);

            // ---------- 2. 结算组件 ----------

            BuildingProgressTracker tracker = EnsureComponent<BuildingProgressTracker>(manager.gameObject);
            SerializedObject trackerSo = new SerializedObject(tracker);
            SetObject(trackerSo, "buildingManager", manager);
            SetObject(trackerSo, "milestoneProgress", milestones);
            SetObject(trackerSo, "playerLevel", playerLevel);
            SetObject(trackerSo, "banner", banner);
            trackerSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 3. 特殊建筑唯一性 ----------

            SerializedObject buildingUISo = new SerializedObject(buildingUI);
            SetObject(buildingUISo, "milestoneProgress", milestones);
            buildingUISo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 4. 按钮置灰 + 提示 ----------

            ConfigureAvailability(panel, canvas, buildingUI);

            // ---------- 收尾 ----------

            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            EditorSceneManager.SaveScene(manager.gameObject.scene);
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = banner.gameObject;
            EditorGUIUtility.PingObject(banner.gameObject);

            Debug.Log("[BuildingProgressSetupWizard] 配置完成：\n" +
                      $"  · Canvas/{BannerName}：顶部建筑进度横幅（特殊 / 普通两套样式 + 程序化礼炮）\n" +
                      "  · BuildingSystem：BuildingProgressTracker（建筑建成时结算进度）\n" +
                      "  · BuildingSystem：BuildingUI.milestoneProgress（禁止重复建造特殊建筑）\n" +
                      $"  · Canvas/{PanelName}：SpecialBuildingAvailabilityUI（按钮置灰 + 光标提示）\n" +
                      "场景已保存。进入 Play Mode 建成任意建筑即可看到进度横幅。", manager);
        }

        // ---------------- 横幅 ----------------

        private static BuildingProgressBannerUI BuildBanner(Canvas canvas)
        {
            RectTransform root = FindOrCreateRect(canvas.transform, BannerName);
            root.anchorMin = new Vector2(0.5f, 1f);
            root.anchorMax = new Vector2(0.5f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.anchoredPosition = new Vector2(0f, -40f);
            root.sizeDelta = new Vector2(660f, 200f);

            // 最外层就是边框：内部再盖一层深色，只留 4px 金边
            Image border = EnsureComponent<Image>(root.gameObject);
            border.sprite = WhiteSprite();
            border.color = GoldBorder;
            border.raycastTarget = false;

            RectTransform inner = FindOrCreateRect(root, "Inner");
            SetStretch(inner, 4f);
            Image innerImage = EnsureComponent<Image>(inner.gameObject);
            innerImage.sprite = WhiteSprite();
            innerImage.color = PanelColor;
            innerImage.raycastTarget = false;

            Text valueText = FindOrCreateText(root, "ValueText");
            SetRect(valueText.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -10f), new Vector2(0f, 66f));
            valueText.fontSize = 44;
            valueText.alignment = TextAnchor.MiddleCenter;
            valueText.color = Color.white;
            valueText.text = "0/3";

            Text subText = FindOrCreateText(root, "SubText");
            SetRect(subText.rectTransform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -76f), new Vector2(0f, 40f));
            subText.fontSize = 24;
            subText.alignment = TextAnchor.MiddleCenter;
            subText.color = new Color(0.88f, 0.90f, 0.94f);
            subText.horizontalOverflow = HorizontalWrapMode.Wrap;
            subText.verticalOverflow = VerticalWrapMode.Overflow;
            subText.text = "当前等级——1";

            RectTransform barRoot = FindOrCreateRect(root, "BarRoot");
            barRoot.anchorMin = new Vector2(0f, 0f);
            barRoot.anchorMax = new Vector2(1f, 0f);
            barRoot.pivot = new Vector2(0.5f, 0f);
            barRoot.anchoredPosition = new Vector2(0f, 20f);
            barRoot.sizeDelta = new Vector2(-56f, 30f);

            RectTransform barBackground = FindOrCreateRect(barRoot, "BarBackground");
            SetStretch(barBackground);
            Image barBackgroundImage = EnsureComponent<Image>(barBackground.gameObject);
            barBackgroundImage.sprite = WhiteSprite();
            barBackgroundImage.color = Color.white;
            barBackgroundImage.raycastTarget = false;

            RectTransform barFillRect = FindOrCreateRect(barRoot, "BarFill");
            SetStretch(barFillRect);
            Image barFill = EnsureComponent<Image>(barFillRect.gameObject);
            barFill.sprite = WhiteSprite();
            barFill.color = GoldBorder;
            barFill.raycastTarget = false;
            barFill.type = Image.Type.Filled;
            barFill.fillMethod = Image.FillMethod.Horizontal;
            barFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            barFill.fillAmount = 0f;

            RectTransform fxLayer = FindOrCreateRect(root, "Fx");
            SetStretch(fxLayer);
            fxLayer.SetAsLastSibling();

            CanvasGroup group = EnsureComponent<CanvasGroup>(root.gameObject);
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            BuildingProgressBannerUI banner = EnsureComponent<BuildingProgressBannerUI>(root.gameObject);
            SerializedObject bannerSo = new SerializedObject(banner);
            SetObject(bannerSo, "canvasGroup", group);
            SetObject(bannerSo, "bannerRect", root);
            SetObject(bannerSo, "valueText", valueText);
            SetObject(bannerSo, "subText", subText);
            SetObject(bannerSo, "borderImage", border);
            SetObject(bannerSo, "barRoot", barRoot);
            SetObject(bannerSo, "barBackground", barBackgroundImage);
            SetObject(bannerSo, "barFill", barFill);
            SetObject(bannerSo, "fxLayer", fxLayer);
            bannerSo.ApplyModifiedPropertiesWithoutUndo();

            root.SetAsLastSibling();
            return banner;
        }

        // ---------------- 按钮置灰与提示 ----------------

        private static void ConfigureAvailability(Transform panel, Canvas canvas, BuildingUI buildingUI)
        {
            SerializedObject buildingUISo = new SerializedObject(buildingUI);
            SerializedProperty buildingsProperty = buildingUISo.FindProperty("availableBuildings");

            if (buildingsProperty == null)
            {
                Debug.LogWarning("[BuildingProgressSetupWizard] BuildingUI 上找不到 availableBuildings 字段，" +
                                 "特殊建筑按钮无法自动置灰。");
                return;
            }

            List<Button> buttons = new List<Button>(buildingsProperty.arraySize);

            for (int i = 0; i < buildingsProperty.arraySize; i++)
            {
                BuildingData data = buildingsProperty.GetArrayElementAtIndex(i).objectReferenceValue as BuildingData;

                if (data == null)
                {
                    buttons.Add(null);
                    continue;
                }

                Transform buttonTransform = panel.Find("Button_" + data.BuildingName);
                Button button = buttonTransform != null ? buttonTransform.GetComponent<Button>() : null;

                if (button == null)
                {
                    Debug.LogWarning($"[BuildingProgressSetupWizard] 面板里找不到「{data.BuildingName}」的按钮" +
                                     $"(Button_{data.BuildingName})，该按钮不会置灰。");
                }

                buttons.Add(button);
            }

            Text hint = BuildHint(canvas);

            SpecialBuildingAvailabilityUI availability =
                EnsureComponent<SpecialBuildingAvailabilityUI>(panel.gameObject);

            SerializedObject availabilitySo = new SerializedObject(availability);
            SetObject(availabilitySo, "buildingUI", buildingUI);
            SetObjectArray(availabilitySo, "buildingButtons", buttons.ConvertAll(button => (Object)button));
            SetObject(availabilitySo, "hintText", hint);
            availabilitySo.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Text BuildHint(Canvas canvas)
        {
            Text hint = FindOrCreateText(canvas.transform, HintName);
            RectTransform rect = hint.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(520f, 52f);

            hint.fontSize = 24;
            hint.alignment = TextAnchor.MiddleLeft;
            hint.color = Color.white;
            hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            hint.verticalOverflow = VerticalWrapMode.Overflow;
            hint.raycastTarget = false;

            Outline outline = EnsureComponent<Outline>(hint.gameObject);
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);

            hint.text = string.Empty;
            hint.gameObject.SetActive(false);
            hint.transform.SetAsLastSibling();
            return hint;
        }

        // ---------------- 建 UI 的小工具 ----------------

        private static Sprite WhiteSprite()
        {
            return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        }

        private static RectTransform FindOrCreateRect(Transform parent, string childName)
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

            return (RectTransform)child.transform;
        }

        private static Text FindOrCreateText(Transform parent, string childName)
        {
            RectTransform rect = FindOrCreateRect(parent, childName);
            Text text = EnsureComponent<Text>(rect.gameObject);
            text.font = BuiltinFont();
            text.raycastTarget = false;
            return text;
        }

        private static void SetStretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void SetRect(
            RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static Font BuiltinFont()
        {
            // 项目规则：不引入 TextMeshPro，UI 文字用旧版 Text + 内置字体
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
                Debug.LogWarning($"[BuildingProgressSetupWizard] {so.targetObject.GetType().Name} 上找不到字段 " +
                                 $"{fieldName}，该项跳过。");
            }
        }

        private static void SetObjectArray(SerializedObject so, string fieldName, IReadOnlyList<Object> values)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogWarning($"[BuildingProgressSetupWizard] {so.targetObject.GetType().Name} 上找不到字段 " +
                                 $"{fieldName}，该项跳过。");
                return;
            }

            property.arraySize = values.Count;

            for (int i = 0; i < values.Count; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static void Fail(string message)
        {
            EditorUtility.DisplayDialog("建筑进度 UI 配置未执行", message, "知道了");
            Debug.LogError("[BuildingProgressSetupWizard] " + message);
        }
    }
}
