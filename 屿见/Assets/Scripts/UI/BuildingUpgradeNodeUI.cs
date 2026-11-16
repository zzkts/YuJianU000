using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.Core.Events;

namespace Yujian.UI
{
    /// <summary>
    /// 建筑升级节点选择窗口（策划案 3.4）。
    ///
    /// 规则：
    ///   · 建筑升到节点等级时立刻弹出，背景黑色 50% 遮罩；
    ///   · 必须选一项并二次确认（是 / 否），**没有关闭按钮**；
    ///   · 没选完之前不能切建筑、不能点空地退出（由 BuildingUpgrade 与
    ///     BuildingPointerInteraction 一起兜住）；
    ///   · 选完后如果同一等级还有下一个未解决节点，窗口会继续显示下一个节点。
    ///
    /// 为什么这里自建 UI：
    ///   节点窗口是 2026-11-16 新增的占位功能，先保证不跑向导、不改场景就能验收；
    ///   以后美术要精修时，把 root / 各文字按钮引用挂到本组件的 Inspector 上即可，
    ///   逻辑不用动。
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildingUpgradeNodeUI : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("建筑升级规则层。留空时由 BuildingUpgradeUI 在运行时自动接线")]
        [SerializeField] private BuildingUpgrade upgrade;

        [Header("场景 UI（留空则运行时自建占位界面）")]
        [SerializeField] private RectTransform root;
        [SerializeField] private RectTransform optionContainer;
        [SerializeField] private Text titleText;
        [SerializeField] private Text subtitleText;
        [SerializeField] private GameObject confirmPanel;
        [SerializeField] private Text confirmText;
        [SerializeField] private Button confirmYesButton;
        [SerializeField] private Button confirmNoButton;

        [Header("配色")]
        [SerializeField] private Color overlayColor = new Color(0f, 0f, 0f, 0.5f);
        [SerializeField] private Color panelColor = new Color(0.11f, 0.13f, 0.17f, 0.98f);
        [SerializeField] private Color optionColor = new Color(0.19f, 0.23f, 0.29f, 1f);
        [SerializeField] private Color accentColor = new Color(0.35f, 0.9f, 0.45f, 1f);
        [SerializeField] private Color textColor = new Color(0.95f, 0.95f, 0.92f, 1f);
        [SerializeField] private Color mutedTextColor = new Color(0.72f, 0.75f, 0.78f, 1f);

        [Header("调试")]
        [SerializeField] private bool logNodeFlow = true;

        private Font font;
        private bool built;
        private bool ownsRoot;
        private bool yesWired;
        private bool noWired;
        private string shownNodeId;
        private GameObject shownBuilding;
        private BuildingEnhancementOption pendingChoice;

        /// <summary>由 BuildingUpgradeUI 在 Awake 时调用。</summary>
        public void Configure(BuildingUpgrade newUpgrade)
        {
            upgrade = newUpgrade;
            EnsureBuilt();
        }

        private void Awake()
        {
            ResolveFont();
            EnsureBuilt();
        }

        private void OnEnable()
        {
            WireConfirmButtons();
        }

        private void LateUpdate()
        {
            if (upgrade == null)
            {
                Hide();
                return;
            }

            BuildingEnhancementNode node = upgrade.PendingNode;

            if (node == null || upgrade.SelectedBuilding == null)
            {
                Hide();
                return;
            }

            EnsureBuilt();

            if (!built)
            {
                return;
            }

            root.gameObject.SetActive(true);

            if (shownNodeId != node.NodeId || shownBuilding != upgrade.SelectedBuilding)
            {
                shownNodeId = node.NodeId;
                shownBuilding = upgrade.SelectedBuilding;
                RebuildOptions(node);
            }
        }

        /// <summary>把窗口拉到最前并立即刷新。窗口本来就常显时是幂等的。</summary>
        public void ShowPendingNode()
        {
            EnsureBuilt();

            if (!built || upgrade == null || !upgrade.HasPendingNode)
            {
                return;
            }

            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
            LateUpdate();
        }

        /// <summary>没有待选节点时收起。有待选节点时什么都不做（强制选择不能关）。</summary>
        public void HideIfNoPending()
        {
            if (upgrade != null && upgrade.HasPendingNode)
            {
                return;
            }

            Hide();
        }

        private void Hide()
        {
            if (root != null && root.gameObject.activeSelf)
            {
                root.gameObject.SetActive(false);
            }

            HideConfirm();
            shownNodeId = null;
            shownBuilding = null;
            pendingChoice = null;
        }

        /// <summary>摧毁 / 禁用时把自建窗口一起收掉，避免留下一个挡输入的空遮罩。</summary>
        private void OnDestroy()
        {
            // 只在播放模式销毁自建窗口；Editor 下编辑模式销毁会触发 Unity 的警告
            if (Application.isPlaying && ownsRoot && root != null && root.gameObject != null)
            {
                Destroy(root.gameObject);
            }
        }

        // ---------- 运行时自建占位 UI ----------

        private void EnsureBuilt()
        {
            if (built || root != null)
            {
                built = true;
                return;
            }

            Canvas canvas = GetComponentInParent<Canvas>();

            if (canvas == null)
            {
                canvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            }

            if (canvas == null)
            {
                Debug.LogError("[BuildingUpgradeNodeUI] 场景里找不到 Canvas，强化节点窗口无法显示。", this);
                return;
            }

            Transform canvasRoot = canvas.rootCanvas != null ? canvas.rootCanvas.transform : canvas.transform;

            // 全屏黑 50% 遮罩：raycastTarget = true，挡住后面的点击
            GameObject rootObject = new GameObject(
                "建筑强化节点界面", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

            root = rootObject.GetComponent<RectTransform>();
            root.SetParent(canvasRoot, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            Image overlay = rootObject.GetComponent<Image>();
            overlay.color = overlayColor;
            overlay.raycastTarget = true;

            // 中央面板
            GameObject panelObject = new GameObject(
                "面板", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

            RectTransform panel = panelObject.GetComponent<RectTransform>();
            panel.SetParent(root, false);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(820f, 560f);

            Image panelImage = panelObject.GetComponent<Image>();
            panelImage.color = panelColor;
            panelImage.raycastTarget = true;

            titleText = CreateText(panel, "标题", 30, FontStyle.Bold, textColor);
            SetRect(titleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(40f, -76f), new Vector2(-40f, -24f));

            subtitleText = CreateText(panel, "说明", 19, FontStyle.Normal, mutedTextColor);
            SetRect(subtitleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(40f, -124f), new Vector2(-40f, -80f));

            GameObject optionObject = new GameObject(
                "选项容器", typeof(RectTransform), typeof(CanvasRenderer));

            optionContainer = optionObject.GetComponent<RectTransform>();
            optionContainer.SetParent(panel, false);
            optionContainer.anchorMin = Vector2.zero;
            optionContainer.anchorMax = Vector2.one;
            optionContainer.offsetMin = new Vector2(40f, 40f);
            optionContainer.offsetMax = new Vector2(-40f, -140f);

            BuildConfirmPanel(root);

            root.gameObject.SetActive(false);
            root.SetAsLastSibling();
            ownsRoot = true;
            built = true;

            if (logNodeFlow)
            {
                Debug.Log("[BuildingUpgradeNodeUI] 已自建强化节点占位窗口（黑底 50%、选项列表、二次确认）。", this);
            }
        }

        private void RebuildOptions(BuildingEnhancementNode node)
        {
            if (optionContainer == null)
            {
                return;
            }

            for (int i = optionContainer.childCount - 1; i >= 0; i--)
            {
                GameObject child = optionContainer.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            HideConfirm();

            PlacedBuildingInfo info = upgrade.SelectedInfo;
            string buildingName = info != null && info.Data != null ? info.Data.BuildingName : "建筑";

            if (titleText != null)
            {
                titleText.text = $"建筑强化 · {node.TriggerLevel} 级节点";
            }

            if (subtitleText != null)
            {
                subtitleText.text = $"「{buildingName}」已升到 {upgrade.CurrentLevel} 级，" +
                                    "请选择一项强化（选择后不可更改）";
            }

            List<BuildingEnhancementOption> options = upgrade.GetPendingNodeOptions();

            if (options.Count == 0)
            {
                Text empty = CreateText(optionContainer, "无选项", 22, FontStyle.Normal, textColor);
                SetRect(empty.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0f, -70f), new Vector2(0f, -10f));
                empty.text = "该强化节点没有配置任何有效选项，请检查强化节点表。";
                Debug.LogError($"[BuildingUpgradeNodeUI] 节点「{node.NodeId}」没有有效选项。" +
                               "为避免锁死玩家，这个节点会被运行时跳过。", this);
                return;
            }

            for (int i = 0; i < options.Count; i++)
            {
                BuildingEnhancementOption option = options[i];
                float top = -(i * 104f);

                Button button = CreateButton(optionContainer, $"选项_{option.OptionId}", optionColor);
                RectTransform rect = button.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.offsetMin = new Vector2(0f, top - 92f);
                rect.offsetMax = new Vector2(0f, top);

                Text nameText = CreateText(button.transform, "名称", 22, FontStyle.Bold, textColor);
                SetRect(nameText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(24f, -40f), new Vector2(-24f, -8f));
                nameText.text = $"{option.DisplayName}　<color=#8ee6a0>{option.ScopeName}</color>";

                Text descriptionText = CreateText(
                    button.transform, "描述", 17, FontStyle.Normal, mutedTextColor);
                SetRect(descriptionText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                    new Vector2(24f, 8f), new Vector2(-24f, -44f));
                descriptionText.text = option.GetFullDescription();

                BuildingEnhancementOption captured = option;
                button.onClick.AddListener(() => ShowConfirm(captured));
            }

            root.SetAsLastSibling();
        }

        private void BuildConfirmPanel(RectTransform parent)
        {
            GameObject confirmRootObject = new GameObject(
                "确认遮罩", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

            RectTransform confirmRoot = confirmRootObject.GetComponent<RectTransform>();
            confirmRoot.SetParent(parent, false);
            confirmRoot.anchorMin = Vector2.zero;
            confirmRoot.anchorMax = Vector2.one;
            confirmRoot.offsetMin = Vector2.zero;
            confirmRoot.offsetMax = Vector2.zero;

            Image confirmOverlay = confirmRootObject.GetComponent<Image>();
            confirmOverlay.color = new Color(0f, 0f, 0f, 0.35f);
            confirmOverlay.raycastTarget = true;

            GameObject boxObject = new GameObject(
                "确认框", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

            RectTransform box = boxObject.GetComponent<RectTransform>();
            box.SetParent(confirmRoot, false);
            box.anchorMin = new Vector2(0.5f, 0.5f);
            box.anchorMax = new Vector2(0.5f, 0.5f);
            box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(640f, 340f);

            Image boxImage = boxObject.GetComponent<Image>();
            boxImage.color = panelColor;
            boxImage.raycastTarget = true;

            confirmText = CreateText(box, "确认文字", 20, FontStyle.Normal, textColor);
            SetRect(confirmText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(32f, -236f), new Vector2(-32f, -24f));

            confirmYesButton = CreateButton(box, "是", accentColor);
            SetRect(confirmYesButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0.5f, 0f),
                new Vector2(48f, 28f), new Vector2(-24f, 96f));
            Text yesText = CreateText(confirmYesButton.transform, "文字", 22, FontStyle.Bold, Color.black);
            Stretch(yesText.rectTransform, 0f);
            yesText.text = "是";

            confirmNoButton = CreateButton(box, "否", optionColor);
            SetRect(confirmNoButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(1f, 0f),
                new Vector2(24f, 28f), new Vector2(-48f, 96f));
            Text noText = CreateText(confirmNoButton.transform, "文字", 22, FontStyle.Bold, textColor);
            Stretch(noText.rectTransform, 0f);
            noText.text = "否";

            confirmPanel = confirmRootObject;
            confirmPanel.SetActive(false);
            WireConfirmButtons();
        }

        private void ShowConfirm(BuildingEnhancementOption option)
        {
            if (option == null || confirmPanel == null)
            {
                return;
            }

            pendingChoice = option;

            if (confirmText != null)
            {
                confirmText.text = $"确认选择「{option.DisplayName}」？\n\n" +
                                   $"{option.GetFullDescription()}\n\n选择后不可更改。";
            }

            confirmPanel.SetActive(true);
            confirmPanel.transform.SetAsLastSibling();
        }

        private void HideConfirm()
        {
            if (confirmPanel != null && confirmPanel.activeSelf)
            {
                confirmPanel.SetActive(false);
            }

            pendingChoice = null;
        }

        private void WireConfirmButtons()
        {
            if (confirmYesButton != null && !yesWired)
            {
                yesWired = true;
                confirmYesButton.onClick.AddListener(HandleConfirmYes);
            }

            if (confirmNoButton != null && !noWired)
            {
                noWired = true;
                confirmNoButton.onClick.AddListener(HandleConfirmNo);
            }
        }

        private void HandleConfirmYes()
        {
            if (upgrade == null || pendingChoice == null)
            {
                HideConfirm();
                return;
            }

            string optionId = pendingChoice.OptionId;
            bool success = upgrade.TrySelectEnhancement(optionId, out string message);

            EventBus.Publish(new MessageShown(message, success));

            if (success)
            {
                HideConfirm();
                shownNodeId = null;
                shownBuilding = null;

                if (logNodeFlow)
                {
                    Debug.Log($"[BuildingUpgradeNodeUI] {message}");
                }

                LateUpdate();
                return;
            }

            // 失败（例如不可叠加 / 配置改了）时留在确认框里，给玩家看原因
            if (confirmText != null)
            {
                confirmText.text = $"{message}\n\n请重新选择。";
            }
        }

        private void HandleConfirmNo()
        {
            HideConfirm();
        }

        // ---------- UI 小工具 ----------

        private void ResolveFont()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            if (font == null)
            {
                Debug.LogError("[BuildingUpgradeNodeUI] 找不到内置字体，强化节点窗口的文字将不显示。", this);
            }
        }

        private Text CreateText(Transform parent, string name, int size, FontStyle style, Color color)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            Text text = textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.supportRichText = true;
            return text;
        }

        private Button CreateButton(Transform parent, string name, Color color)
        {
            GameObject buttonObject = new GameObject(
                name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            Image image = buttonObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = true;

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            ColorBlock colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.15f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.15f);
            colors.selectedColor = color;
            button.colors = colors;

            return button;
        }

        private static void SetRect(
            RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void Stretch(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }
    }
}
