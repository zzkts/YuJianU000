using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 主要建筑里程碑的一键配置。
    ///
    /// 做五件事：
    ///   1. 先重跑阶段 3 的建筑向导，确保面板有 4 座建筑按钮与顶部进度行；
    ///   2. 在 BuildingSystem 上挂 MilestoneProgress，填入八卦楼 / 菽庄花园 / 日光岩；
    ///   3. 给 BuildingPanel 挂 MilestonePanelUI，接上进度文本；
    ///   4. 在 Canvas 下建「MilestonePopup」小窗口，挂 MilestoneCompletePopup；
    ///   5. 跑一次存档配置（补三座新建筑的 Save Id 与存档目录），保存场景。
    ///
    /// 幂等：已有组件与子物体全部复用，可重复运行。
    /// 入口：菜单栏 → 屿见 → 建筑 → 配置主要建筑里程碑
    /// </summary>
    public static class MilestoneSetupWizard
    {
        private const string PanelName = "BuildingPanel";
        private const string ProgressTextName = "MilestoneProgressText";
        private const string PopupName = "MilestonePopup";
        private const string BuildingFolder = "Assets/Data/Buildings";

        private static readonly string[] MilestoneFileNames =
        {
            "Building_八卦楼.asset",
            "Building_菽庄花园.asset",
            "Building_日光岩.asset",
        };

        [MenuItem("屿见/建筑/配置主要建筑里程碑", false, 515)]
        public static void Configure()
        {
            // ---------- 前置：三座资产必须在 ----------

            BuildingData[] milestones = LoadMilestones(out string missing);

            if (milestones == null)
            {
                Fail($"缺少主要建筑资产：\n{missing}\n\n" +
                     "请确认这三个 .asset 已导入 Assets/Data/Buildings。");
                return;
            }

            BuildingManager manager = Object.FindFirstObjectByType<BuildingManager>();
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();

            if (manager == null || canvas == null)
            {
                Fail("场景里找不到 BuildingManager 或 Canvas。\n\n" +
                     "请先运行菜单「屿见/建筑/配置建筑系统（阶段 3）」。");
                return;
            }

            // ---------- 1. 重建建筑面板（4 座按钮 + 进度行） ----------

            BuildingSetupWizard.Configure();

            Transform panel = canvas.transform.Find(PanelName);
            Text progressText = panel != null
                ? panel.Find(ProgressTextName)?.GetComponent<Text>()
                : null;

            if (panel == null || progressText == null)
            {
                Fail($"Canvas/{PanelName} 或 {ProgressTextName} 没有建出来。\n\n" +
                     "请先单独运行一次「屿见/建筑/配置建筑系统（阶段 3）」，确认 Console 无报错后再跑本命令。");
                return;
            }

            // ---------- 2. MilestoneProgress ----------

            MilestoneProgress progress = EnsureComponent<MilestoneProgress>(manager.gameObject);
            SerializedObject progressSo = new SerializedObject(progress);
            SetObject(progressSo, "buildingManager", manager);
            SetObjectArray(progressSo, "milestones", milestones);
            progressSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 3. 面板进度文本 ----------

            MilestonePanelUI panelUI = EnsureComponent<MilestonePanelUI>(panel.gameObject);
            SerializedObject panelSo = new SerializedObject(panelUI);
            SetObject(panelSo, "progress", progress);
            SetObject(panelSo, "progressText", progressText);
            panelSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 4. 达成小窗口 ----------

            BuildPopup(canvas, progress);

            // ---------- 5. 存档：补 Save Id 与目录 ----------

            // 三座新建筑没有 Save Id 时，BuildingSaveParticipant 会在存档时跳过它们。
            // 这里顺手跑一次存档配置；它是幂等的，不会覆盖已有设置。
            SaveSetupWizard.Configure();

            // ---------- 收尾 ----------

            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            EditorSceneManager.SaveScene(manager.gameObject.scene);
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = manager.gameObject;
            EditorGUIUtility.PingObject(manager.gameObject);

            Debug.Log("[MilestoneSetupWizard] 配置完成：\n" +
                      "  · BuildingSystem：MilestoneProgress（八卦楼 / 菽庄花园 / 日光岩）\n" +
                      $"  · Canvas/{PanelName}：MilestonePanelUI → {ProgressTextName}\n" +
                      $"  · Canvas/{PopupName}：MilestoneCompletePopup（标题 / 正文 / 继续按钮）\n" +
                      "  · 存档：三座建筑的 Save Id 与目录已补齐\n" +
                      "场景已保存。进入 Play Mode，依次建成三座即可验证。", manager);
        }

        // ---------------- 弹窗 ----------------

        private static MilestoneCompletePopup BuildPopup(Canvas canvas, MilestoneProgress progress)
        {
            RectTransform root = FindOrCreateRect(canvas.transform, PopupName);
            SetStretch(root);

            // 遮罩：挡住背后的点击
            RectTransform dim = FindOrCreateRect(root, "Dim");
            SetStretch(dim);
            Image dimImage = EnsureComponent<Image>(dim.gameObject);
            dimImage.color = new Color(0f, 0f, 0f, 0.60f);
            dimImage.raycastTarget = true;

            // 窗口底板
            RectTransform window = FindOrCreateRect(root, "Window");
            SetCentered(window, new Vector2(640f, 360f));
            Image windowImage = EnsureComponent<Image>(window.gameObject);
            windowImage.color = new Color(0.10f, 0.12f, 0.16f, 0.98f);
            windowImage.raycastTarget = true;

            // 标题
            Text title = FindOrCreateText(window, "Title");
            SetRect(title.rectTransform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -24f), new Vector2(560f, 70f));
            title.fontSize = 40;
            title.alignment = TextAnchor.MiddleCenter;
            title.color = Color.white;
            title.text = "恭喜";

            // 正文
            Text message = FindOrCreateText(window, "Message");
            SetRect(message.rectTransform,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            RectTransform messageRect = message.rectTransform;
            messageRect.offsetMin = new Vector2(40f, 110f);
            messageRect.offsetMax = new Vector2(-40f, -104f);
            message.fontSize = 28;
            message.alignment = TextAnchor.MiddleCenter;
            message.color = new Color(0.90f, 0.92f, 0.95f);
            message.horizontalOverflow = HorizontalWrapMode.Wrap;
            message.verticalOverflow = VerticalWrapMode.Overflow;
            message.text = "三座主要建筑已全部建成，屿见的故事还在继续。";

            // 继续按钮
            Button continueButton = FindOrCreateButton(window, "Button_继续", "继续");
            SetRect(continueButton.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 32f), new Vector2(240f, 70f));

            // 组件与显隐
            MilestoneCompletePopup popup = EnsureComponent<MilestoneCompletePopup>(root.gameObject);
            CanvasGroup group = EnsureComponent<CanvasGroup>(root.gameObject);
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            SerializedObject popupSo = new SerializedObject(popup);
            SetObject(popupSo, "progress", progress);
            SetObject(popupSo, "canvasGroup", group);
            SetObject(popupSo, "titleText", title);
            SetObject(popupSo, "messageText", message);
            SetObject(popupSo, "continueButton", continueButton);
            popupSo.ApplyModifiedPropertiesWithoutUndo();

            root.SetAsLastSibling();
            return popup;
        }

        // ---------------- 资产 ----------------

        private static BuildingData[] LoadMilestones(out string missing)
        {
            BuildingData[] result = new BuildingData[MilestoneFileNames.Length];
            missing = string.Empty;

            for (int i = 0; i < MilestoneFileNames.Length; i++)
            {
                string path = BuildingFolder + "/" + MilestoneFileNames[i];
                result[i] = AssetDatabase.LoadAssetAtPath<BuildingData>(path);

                if (result[i] == null)
                {
                    missing += path + "\n";
                }
            }

            return string.IsNullOrEmpty(missing) ? result : null;
        }

        // ---------------- 建 UI 的小工具 ----------------

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

        private static Button FindOrCreateButton(Transform parent, string objectName, string label)
        {
            RectTransform rect = FindOrCreateRect(parent, objectName);
            Image image = EnsureComponent<Image>(rect.gameObject);
            image.color = new Color(0.24f, 0.65f, 0.32f);
            image.raycastTarget = true;

            Button button = EnsureComponent<Button>(rect.gameObject);
            button.targetGraphic = image;

            Text text = FindOrCreateText(rect, "Text");
            SetStretch(text.rectTransform);
            text.fontSize = 30;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = label;

            return button;
        }

        private static void SetStretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetCentered(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
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
                Debug.LogWarning($"[MilestoneSetupWizard] {so.targetObject.GetType().Name} 上找不到字段 " +
                                 $"{fieldName}，该项跳过。");
            }
        }

        private static void SetObjectArray(SerializedObject so, string fieldName, Object[] values)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogWarning($"[MilestoneSetupWizard] {so.targetObject.GetType().Name} 上找不到字段 " +
                                 $"{fieldName}，里程碑清单需要手动填写。");
                return;
            }

            property.arraySize = values.Length;

            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static void Fail(string message)
        {
            EditorUtility.DisplayDialog("主要建筑里程碑配置未执行", message, "知道了");
            Debug.LogError("[MilestoneSetupWizard] " + message);
        }
    }
}
