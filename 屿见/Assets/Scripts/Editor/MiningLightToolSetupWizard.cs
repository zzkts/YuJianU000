using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Mining;
using Yujian.Player;
using Yujian.Save;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 配置照明工具（D17=A：商店购买）。
    /// 入口：菜单栏 → 屿见 → 挖矿 → 配置照明工具
    ///
    /// 做三件事：
    ///   1. 在挖矿工具所在的物体上补一个 MiningLightTool（已有就复用）；
    ///   2. 在商店面板里创建 / 重建「照明工具」购买卡；
    ///   3. 把照明工具等级接进存档参与者列表。
    /// </summary>
    public static class MiningLightToolSetupWizard
    {
        private const string ShopPanelName = "ShopPanel";
        private const string CardName = "MiningLightToolCard";
        private const string ButtonTemplateName = "ButtonTemplate";
        private const string ItemContainerName = "ItemContainer";

        private static readonly Color TitleColor = new Color(0.96f, 0.96f, 0.96f, 1f);
        private static readonly Color BodyColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        private static readonly Color DimColor = new Color(0.72f, 0.72f, 0.72f, 1f);
        private static readonly Color PriceColor = new Color(1f, 0.85f, 0.42f, 1f);
        private static readonly Color LabelOnButtonColor = new Color(0.15f, 0.15f, 0.15f, 1f);

        [MenuItem("屿见/挖矿/配置照明工具", false, 336)]
        public static void Configure()
        {
            if (!ConfigureLightTool(true))
            {
                return;
            }

            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[MiningLightToolSetupWizard] 照明工具已配置并保存。" +
                      "进入 Play 后打开底部「商店」即可购买火把 / 手电。");
        }

        /// <summary>给总向导复用。返回 false 表示工具或商店面板不完整。</summary>
        public static bool ConfigureLightTool(bool showDialog)
        {
            MiningToolProgress progress = Object.FindFirstObjectByType<MiningToolProgress>(
                FindObjectsInactive.Include);
            PlayerStats playerStats = Object.FindFirstObjectByType<PlayerStats>(FindObjectsInactive.Include);

            GameObject host = progress != null ? progress.gameObject
                : playerStats != null ? playerStats.gameObject : null;

            if (host == null)
            {
                return Fail(showDialog, "场景里找不到 MiningToolProgress 或 PlayerStats，" +
                                        "不确定照明工具该挂在哪里。");
            }

            PlayerCurrency currency = Object.FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            MiningLightTool tool = host.GetComponent<MiningLightTool>();

            if (tool == null)
            {
                tool = Undo.AddComponent<MiningLightTool>(host);
            }

            tool.ConfigureForSetup(currency);
            EditorUtility.SetDirty(tool);

            if (!CreateCard(showDialog, tool))
            {
                return false;
            }

            EnsureSaveParticipant(tool);

            EditorSceneManager.MarkSceneDirty(host.scene);
            return true;
        }

        private static bool CreateCard(bool showDialog, MiningLightTool tool)
        {
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();

            if (canvas == null)
            {
                return Fail(showDialog, "场景里找不到 Canvas，无法创建照明工具卡。");
            }

            Transform panel = canvas.transform.Find(ShopPanelName);

            if (panel == null)
            {
                return Fail(showDialog, $"Canvas 下找不到「{ShopPanelName}」，无法创建照明工具卡。");
            }

            Transform buttonSource = panel.Find(ButtonTemplateName);

            if (buttonSource == null)
            {
                Button anyButton = panel.GetComponentInChildren<Button>(true);

                if (anyButton != null)
                {
                    buttonSource = anyButton.transform;
                }
            }

            if (buttonSource == null)
            {
                return Fail(showDialog, "商店面板里找不到可复用的按钮模板，" +
                                        "请先运行「屿见/配置原料商店（阶段 2 重构）」。");
            }

            Transform existing = panel.Find(CardName);

            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            PlayerCurrency currency = Object.FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);

            GameObject card = CreateUIObject(CardName, panel);
            Undo.RegisterCreatedObjectUndo(card, "创建照明工具卡");

            Image background = card.AddComponent<Image>();
            background.sprite = null;
            background.color = new Color(1f, 1f, 1f, 0.08f);
            background.raycastTarget = false;

            VerticalLayoutGroup layout = card.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            MiningLightToolUI ui = card.AddComponent<MiningLightToolUI>();

            Text titleText = CreateText(card.transform, "TitleText", "照明工具", 20,
                                        TextAnchor.MiddleCenter, TitleColor, 26f);
            titleText.fontStyle = FontStyle.Bold;

            Text statsText = CreateText(card.transform, "StatsText", "暴击点上限 1　停留 2.5 秒", 16,
                                        TextAnchor.MiddleCenter, BodyColor, 24f);

            Text costText = CreateText(card.transform, "CostText", "购买「火把」需要 40 金币", 14,
                                       TextAnchor.MiddleCenter, PriceColor, 22f);

            Text buttonLabel;
            Button buyButton = CreateButton(card.transform, buttonSource.gameObject,
                                            "BuyButton", "购买火把", out buttonLabel);

            Text feedbackText = CreateText(card.transform, "FeedbackText", string.Empty, 14,
                                           TextAnchor.MiddleCenter, DimColor, 20f);

            ui.ConfigureForSetup(tool, currency, titleText, statsText, costText,
                                 buyButton, buttonLabel, feedbackText);
            EditorUtility.SetDirty(ui);

            Transform itemContainer = panel.Find(ItemContainerName);

            if (itemContainer != null)
            {
                // 放在挖矿升级卡后面；没有就紧跟在商品列表之后
                Transform upgradeCard = panel.Find("MiningToolUpgradeCard");

                if (upgradeCard != null)
                {
                    card.transform.SetSiblingIndex(upgradeCard.GetSiblingIndex() + 1);
                }
                else
                {
                    card.transform.SetSiblingIndex(itemContainer.GetSiblingIndex() + 1);
                }
            }

            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            Selection.activeGameObject = card;
            EditorGUIUtility.PingObject(card);

            Debug.Log("[MiningLightToolSetupWizard] 已创建「照明工具」购买卡。");
            return true;
        }

        private static void EnsureSaveParticipant(MiningLightTool tool)
        {
            SaveSystem saveSystem = Object.FindFirstObjectByType<SaveSystem>(FindObjectsInactive.Include);

            if (saveSystem == null)
            {
                Debug.LogWarning("[MiningLightToolSetupWizard] 场景里没有 SaveSystem，" +
                                 "照明工具等级暂时不会写档。运行一次「配置存档系统」后重跑本向导即可。");
                return;
            }

            GameObject host = saveSystem.gameObject;
            MiningLightToolSaveParticipant participant =
                host.GetComponent<MiningLightToolSaveParticipant>();

            if (participant == null)
            {
                participant = Undo.AddComponent<MiningLightToolSaveParticipant>(host);
            }

            SerializedObject serializedParticipant = new SerializedObject(participant);
            SerializedProperty toolProperty = serializedParticipant.FindProperty("lightTool");

            if (toolProperty != null)
            {
                toolProperty.objectReferenceValue = tool;
                serializedParticipant.ApplyModifiedPropertiesWithoutUndo();
            }

            SerializedObject serializedSave = new SerializedObject(saveSystem);
            SerializedProperty list = serializedSave.FindProperty("participants");

            if (list == null)
            {
                Debug.LogError("[MiningLightToolSetupWizard] SaveSystem 上找不到 participants 字段。", saveSystem);
                return;
            }

            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == participant)
                {
                    return;
                }
            }

            int index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            list.GetArrayElementAtIndex(index).objectReferenceValue = participant;
            serializedSave.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(saveSystem);

            Debug.Log("[MiningLightToolSetupWizard] 已把 MiningLightToolSaveParticipant 加入存档执行列表。");
        }

        private static Button CreateButton(Transform parent, GameObject buttonSource, string name,
            string label, out Text labelText)
        {
            GameObject buttonObject = Object.Instantiate(buttonSource, parent);
            Undo.RegisterCreatedObjectUndo(buttonObject, "创建照明工具购买按钮");
            buttonObject.name = name;
            buttonObject.SetActive(true);

            SetPreferredHeight(buttonObject, 44f);

            Button button = buttonObject.GetComponent<Button>();

            if (button != null)
            {
                ClearPersistentCalls(button);
            }

            labelText = buttonObject.GetComponentInChildren<Text>(true);

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

            return button;
        }

        private static void ClearPersistentCalls(Button button)
        {
            SerializedObject serializedButton = new SerializedObject(button);
            SerializedProperty calls = serializedButton.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");

            if (calls == null)
            {
                return;
            }

            calls.ClearArray();
            serializedButton.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Text CreateText(Transform parent, string name, string content, int fontSize,
            TextAnchor alignment, Color color, float preferredHeight)
        {
            GameObject textObject = CreateUIObject(name, parent);
            Text text = textObject.AddComponent<Text>();

            text.font = BuiltinFont();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            SetPreferredHeight(textObject, preferredHeight);
            return text;
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            GameObject uiObject = new GameObject(name, typeof(RectTransform));
            uiObject.layer = 5;
            uiObject.transform.SetParent(parent, false);
            return uiObject;
        }

        private static void SetPreferredHeight(GameObject target, float height)
        {
            LayoutElement element = target.GetComponent<LayoutElement>();

            if (element == null)
            {
                element = target.AddComponent<LayoutElement>();
            }

            element.preferredHeight = height;
        }

        private static Font BuiltinFont()
        {
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static bool Fail(bool showDialog, string message)
        {
            if (showDialog)
            {
                EditorUtility.DisplayDialog("照明工具未配置", message, "知道了");
            }

            Debug.LogError("[MiningLightToolSetupWizard] " + message);
            return false;
        }
    }
}
