using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Mining;
using Yujian.Player;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 在商店面板里创建正式的挖矿工具升级卡。
    /// 入口：菜单栏 → 屿见 → 配置挖矿工具升级按钮（阶段 9）
    /// </summary>
    public static class MiningToolUpgradeSetupWizard
    {
        private const string ShopPanelName = "ShopPanel";
        private const string CardName = "MiningToolUpgradeCard";
        private const string ButtonTemplateName = "ButtonTemplate";
        private const string ItemContainerName = "ItemContainer";

        private static readonly Color TitleColor = new Color(0.96f, 0.96f, 0.96f, 1f);
        private static readonly Color BodyColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        private static readonly Color DimColor = new Color(0.72f, 0.72f, 0.72f, 1f);
        private static readonly Color PriceColor = new Color(1f, 0.85f, 0.42f, 1f);
        private static readonly Color LabelOnButtonColor = new Color(0.15f, 0.15f, 0.15f, 1f);

        [MenuItem("屿见/配置挖矿工具升级按钮（阶段 9）")]
        public static void Configure()
        {
            if (!ConfigureCard(true))
            {
                return;
            }

            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[MiningToolUpgradeSetupWizard] 挖矿工具升级卡已创建并保存。" +
                      "进入 Play 后打开底部「商店」即可看到升级按钮。");
        }

        /// <summary>给阶段 9 总向导复用；返回 false 时不影响工具本身已完成的配置。</summary>
        public static bool ConfigureCard(bool showDialog)
        {
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();

            if (canvas == null)
            {
                return Fail(showDialog, "场景里找不到 Canvas，无法创建升级卡。");
            }

            Transform panel = canvas.transform.Find(ShopPanelName);

            if (panel == null)
            {
                return Fail(showDialog, $"Canvas 下找不到「{ShopPanelName}」，无法创建升级卡。");
            }

            MiningToolProgress progress = Object.FindFirstObjectByType<MiningToolProgress>(
                FindObjectsInactive.Include);

            if (progress == null)
            {
                return Fail(showDialog, "Player 上还没有 MiningToolProgress。\n\n" +
                                        "请先运行菜单「屿见/配置挖矿工具升级（阶段 9）」。");
            }

            PlayerCurrency currency = progress.Currency;

            if (currency == null)
            {
                currency = Object.FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            }

            if (currency == null)
            {
                Debug.LogWarning("[MiningToolUpgradeSetupWizard] 没找到 PlayerCurrency，升级卡会显示无法升级。");
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

            GameObject card = CreateUIObject(CardName, panel);
            Undo.RegisterCreatedObjectUndo(card, "创建挖矿工具升级卡");

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

            MiningToolUpgradeUI upgradeUI = card.AddComponent<MiningToolUpgradeUI>();

            Text titleText = CreateText(card.transform, "TitleText", "挖矿工具", 20,
                                        TextAnchor.MiddleCenter, TitleColor, 26f);
            titleText.fontStyle = FontStyle.Bold;

            Text statsText = CreateText(card.transform, "StatsText", "等级 1　攻击力 1 → 2", 16,
                                        TextAnchor.MiddleCenter, BodyColor, 24f);

            Text costText = CreateText(card.transform, "CostText", "升级花费 15 金币", 14,
                                       TextAnchor.MiddleCenter, PriceColor, 22f);

            Text buttonLabel;
            Button upgradeButton = CreateButton(card.transform, buttonSource.gameObject,
                                                "UpgradeButton", "升级", out buttonLabel);

            Text feedbackText = CreateText(card.transform, "FeedbackText", string.Empty, 14,
                                           TextAnchor.MiddleCenter, DimColor, 20f);

            upgradeUI.ConfigureForSetup(progress, currency, titleText, statsText, costText,
                                        upgradeButton, buttonLabel, feedbackText);
            EditorUtility.SetDirty(upgradeUI);

            Transform itemContainer = panel.Find(ItemContainerName);

            if (itemContainer != null)
            {
                card.transform.SetSiblingIndex(itemContainer.GetSiblingIndex() + 1);
            }

            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            Selection.activeGameObject = card;
            EditorGUIUtility.PingObject(card);

            Debug.Log("[MiningToolUpgradeSetupWizard] 已创建「挖矿工具 · 石头」升级卡。" +
                      "按钮会在金币足够时自动启用。");
            return true;
        }

        private static Button CreateButton(Transform parent, GameObject buttonSource, string name,
            string label, out Text labelText)
        {
            GameObject buttonObject = Object.Instantiate(buttonSource, parent);
            Undo.RegisterCreatedObjectUndo(buttonObject, "创建挖矿升级按钮");
            buttonObject.name = name;
            buttonObject.SetActive(true);

            SetPreferredHeight(buttonObject, 44f);

            Button button = buttonObject.GetComponent<Button>();

            if (button != null)
            {
                ClearPersistentCalls(button);
            }
            else
            {
                Debug.LogWarning("[MiningToolUpgradeSetupWizard] 按钮模板上没有 Button 组件。");
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
            else
            {
                Debug.LogWarning("[MiningToolUpgradeSetupWizard] 按钮模板上没有 Text 子物体。");
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
                EditorUtility.DisplayDialog("挖矿工具升级按钮未配置", message, "知道了");
            }

            Debug.LogError("[MiningToolUpgradeSetupWizard] " + message);
            return false;
        }
    }
}
