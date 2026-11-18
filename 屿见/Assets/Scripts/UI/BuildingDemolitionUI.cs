using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.Core;
using Yujian.Player;
using Yujian.Shop;

namespace Yujian.UI
{
    /// <summary>
    /// 迁移界面上的拆除入口（策划案 3.5）。
    ///
    /// 长按建筑进入迁移并松手后，在「撤回 / 确认」之间显示红色「拆除」按钮：
    ///   · 特殊建筑按钮变灰，点击提示「特殊建筑无法拆除」；
    ///   · 普通建筑第一次点击打开确认弹窗，勾选「不再显示」后本次建筑会话直接拆除；
    ///   · 弹窗内可分别勾选返还升级金币与建造原料。
    ///
    /// 场景里不需要手工加按钮或弹窗，运行时按现有确认按钮的样式自动生成。
    /// </summary>
    public class BuildingDemolitionUI : MonoBehaviour
    {
        private static readonly Color DemolishRed = new Color(0.82f, 0.22f, 0.2f, 1f);
        private static readonly Color DemolishRedLight = new Color(0.92f, 0.34f, 0.3f, 1f);
        private static readonly Color DemolishRedDark = new Color(0.62f, 0.13f, 0.12f, 1f);
        private static readonly Color DisabledGray = new Color(0.48f, 0.5f, 0.52f, 1f);
        private static readonly Color SuccessColor = new Color(0.35f, 0.9f, 0.45f, 1f);
        private static readonly Color FailureColor = new Color(0.95f, 0.32f, 0.28f, 1f);

        [Header("运行时接线")]
        [SerializeField] private BuildingMigration migration;
        [SerializeField] private BuildingDemolition demolition;
        [SerializeField] private BuildingManager buildingManager;
        [SerializeField] private BuildingUpgrade upgrade;
        [SerializeField] private MilestoneProgress milestoneProgress;
        [SerializeField] private PlayerCurrency currency;
        [SerializeField] private MaterialInventory inventory;
        [SerializeField] private GameAreaController areas;
        [SerializeField] private RectTransform actionRoot;
        [SerializeField] private Button templateButton;
        [SerializeField] private Button demolishButton;

        private RectTransform popupRoot;
        private Text popupBody;
        private Button refundMoneyButton;
        private Text refundMoneyLabel;
        private Button refundMaterialsButton;
        private Text refundMaterialsLabel;
        private Button skipButton;
        private Text skipLabel;
        private GameObject popupTarget;
        private bool skipConfirmationThisVisit;
        private bool refundMoneyThisDemolition;
        private bool refundMaterialsThisDemolition;
        private bool wired;

        /// <summary>由迁移界面在 Awake 时调用。</summary>
        public void Configure(
            BuildingMigration migrationSource,
            RectTransform contentRoot,
            Button styleTemplate,
            Canvas rootCanvas)
        {
            migration = migrationSource;
            actionRoot = contentRoot;
            templateButton = styleTemplate;

            ResolveDependencies();
            EnsureButton();
            WireButton();
        }

        private void Awake()
        {
            ResolveDependencies();
        }

        private void OnEnable()
        {
            WireButton();
        }

        private void OnDisable()
        {
            UnwireButton();
            ClosePopup();
        }

        private void LateUpdate()
        {
            if (areas != null && areas.CurrentArea != GameArea.Building)
            {
                skipConfirmationThisVisit = false;
            }

            if (migration == null || !migration.IsPendingConfirm || migration.Target == null)
            {
                ClosePopup();
                return;
            }

            EnsureButton();
            WireButton();
            RefreshButton(migration.Target);
        }

        private void ResolveDependencies()
        {
            if (buildingManager == null)
            {
                buildingManager = FindFirstObjectByType<BuildingManager>(FindObjectsInactive.Include);
            }

            if (upgrade == null)
            {
                upgrade = FindFirstObjectByType<BuildingUpgrade>(FindObjectsInactive.Include);
            }

            if (milestoneProgress == null)
            {
                milestoneProgress = FindFirstObjectByType<MilestoneProgress>(FindObjectsInactive.Include);
            }

            if (currency == null)
            {
                currency = FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            }

            if (inventory == null)
            {
                inventory = FindFirstObjectByType<MaterialInventory>(FindObjectsInactive.Include);
            }

            if (areas == null)
            {
                areas = FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);
            }

            if (demolition == null)
            {
                demolition = GetComponent<BuildingDemolition>();
            }

            if (demolition == null)
            {
                demolition = gameObject.AddComponent<BuildingDemolition>();
            }

            demolition.Configure(buildingManager, migration, upgrade, milestoneProgress, currency, inventory);
        }

        private void EnsureButton()
        {
            if (demolishButton != null || actionRoot == null || templateButton == null)
            {
                return;
            }

            // 复制现有「确认」按钮的视觉样式。Instantiate 只复制序列化状态，
            // 原按钮上的运行时代码监听不会一起带过来。
            GameObject clone = Instantiate(templateButton.gameObject, actionRoot, false);
            clone.name = "Button_拆除";
            clone.SetActive(true);
            demolishButton = clone.GetComponent<Button>();

            if (demolishButton == null)
            {
                Destroy(clone);
                return;
            }

            demolishButton.onClick.RemoveAllListeners();

            Text label = demolishButton.GetComponentInChildren<Text>(true);

            if (label != null)
            {
                label.text = "拆除";
                label.fontSize = 18;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = Color.white;
            }

            if (clone.transform is RectTransform rect)
            {
                // 撤回 8~88，确认 156~212；拆除正好放在中间的 92~156。
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(92f, 8f);
                rect.sizeDelta = new Vector2(64f, 56f);
            }

            LayoutElement layout = clone.GetComponent<LayoutElement>();

            if (layout != null)
            {
                layout.ignoreLayout = true;
            }

            WireButton();
        }

        private void WireButton()
        {
            if (wired || demolishButton == null)
            {
                return;
            }

            wired = true;
            demolishButton.onClick.AddListener(HandleDemolishClicked);
        }

        private void UnwireButton()
        {
            if (!wired || demolishButton == null)
            {
                return;
            }

            wired = false;
            demolishButton.onClick.RemoveListener(HandleDemolishClicked);
        }

        private void RefreshButton(GameObject target)
        {
            if (demolishButton == null)
            {
                return;
            }

            bool canDemolish = demolition != null && demolition.CanDemolish(target, out _);
            ApplyButtonColor(demolishButton, canDemolish ? DemolishRed : DisabledGray);

            // 特殊建筑保持可点击：灰色按钮仍需要弹出「特殊建筑无法拆除」。
            demolishButton.interactable = true;
        }

        private void HandleDemolishClicked()
        {
            if (migration == null || demolition == null || !migration.IsPendingConfirm)
            {
                return;
            }

            GameObject target = migration.Target;

            if (target == null)
            {
                return;
            }

            if (demolition.IsSpecialBuilding(target))
            {
                ToastUI.Show("特殊建筑无法拆除", 3f, FailureColor);
                return;
            }

            if (!demolition.CanDemolish(target, out string reason))
            {
                ToastUI.Show(reason, 2.4f, FailureColor);
                return;
            }

            if (skipConfirmationThisVisit)
            {
                ExecuteDemolition(
                    target,
                    demolition.DefaultRefundUpgradeMoney,
                    demolition.DefaultRefundBuildMaterials);
                return;
            }

            ShowPopup(target);
        }

        private void ExecuteDemolition(
            GameObject target,
            bool refundMoney,
            bool refundMaterials)
        {
            if (target == null || demolition == null)
            {
                return;
            }

            bool success = demolition.TryDemolish(
                target,
                refundMoney,
                refundMaterials,
                out string message);
            ToastUI.Show(message, success ? 2.6f : 3f, success ? SuccessColor : FailureColor);
        }

        private static void ApplyButtonColor(Button button, Color normalColor)
        {
            if (button == null)
            {
                return;
            }

            ColorBlock colors = button.colors;
            colors.normalColor = normalColor;
            colors.highlightedColor = Color.Lerp(normalColor, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(normalColor, Color.black, 0.2f);
            colors.selectedColor = normalColor;
            colors.disabledColor = DisabledGray;
            button.colors = colors;
        }

        private void ShowPopup(GameObject target)
        {
            if (target == null || demolition == null)
            {
                return;
            }

            EnsurePopup();

            if (popupRoot == null)
            {
                return;
            }

            popupTarget = target;
            refundMoneyThisDemolition = demolition.DefaultRefundUpgradeMoney;
            refundMaterialsThisDemolition = demolition.DefaultRefundBuildMaterials;
            RefreshRefundLabels();
            RefreshPopupBody();
            RefreshSkipLabel();
            popupRoot.gameObject.SetActive(true);
        }

        private void RefreshPopupBody()
        {
            if (popupTarget == null || demolition == null || popupBody == null)
            {
                return;
            }

            PlacedBuildingInfo info = popupTarget.GetComponent<PlacedBuildingInfo>();
            string buildingName = info != null && info.Data != null && !string.IsNullOrWhiteSpace(info.Data.BuildingName)
                ? info.Data.BuildingName
                : popupTarget.name;
            string refund = demolition.GetRefundPreview(
                popupTarget,
                refundMoneyThisDemolition,
                refundMaterialsThisDemolition);
            string refundLine = string.IsNullOrWhiteSpace(refund)
                ? "本次拆除不返还资源。"
                : $"拆除后返还：{refund}。";

            popupBody.text = $"确认拆除「{buildingName}」吗？\n\n{refundLine}";
        }

        /// <summary>关闭确认弹窗，不改变迁移状态。</summary>
        public void ClosePopup()
        {
            popupTarget = null;

            if (popupRoot != null && popupRoot.gameObject.activeSelf)
            {
                popupRoot.gameObject.SetActive(false);
            }
        }

        private void HandleSkipClicked()
        {
            skipConfirmationThisVisit = !skipConfirmationThisVisit;
            RefreshSkipLabel();
        }

        private void HandleRefundMoneyClicked()
        {
            refundMoneyThisDemolition = !refundMoneyThisDemolition;
            RefreshRefundLabels();
            RefreshPopupBody();
        }

        private void HandleRefundMaterialsClicked()
        {
            refundMaterialsThisDemolition = !refundMaterialsThisDemolition;
            RefreshRefundLabels();
            RefreshPopupBody();
        }

        private void RefreshRefundLabels()
        {
            if (refundMoneyLabel != null)
            {
                refundMoneyLabel.text = refundMoneyThisDemolition
                    ? "[√] 返还升级消耗的金币"
                    : "[  ] 返还升级消耗的金币";
            }

            if (refundMaterialsLabel != null)
            {
                refundMaterialsLabel.text = refundMaterialsThisDemolition
                    ? "[√] 返还建造消耗的原料"
                    : "[  ] 返还建造消耗的原料";
            }
        }

        private void RefreshSkipLabel()
        {
            if (skipLabel == null)
            {
                return;
            }

            skipLabel.text = skipConfirmationThisVisit
                ? "[√] 不再显示（离开建筑系统后重置）"
                : "[  ] 不再显示（离开建筑系统后重置）";
        }

        private void HandleConfirmClicked()
        {
            GameObject target = popupTarget;
            bool refundMoney = refundMoneyThisDemolition;
            bool refundMaterials = refundMaterialsThisDemolition;
            ClosePopup();
            ExecuteDemolition(target, refundMoney, refundMaterials);
        }

        private void HandleCancelClicked()
        {
            ClosePopup();
        }

        private void EnsurePopup()
        {
            if (popupRoot != null)
            {
                return;
            }

            GameObject go = new GameObject(
                "BuildingDemolitionPopup",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            go.layer = 5;

            Canvas popupCanvas = go.GetComponent<Canvas>();
            popupCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            popupCanvas.sortingOrder = 30005;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            popupRoot = go.GetComponent<RectTransform>();
            popupRoot.SetParent(null, false);
            popupRoot.anchorMin = Vector2.zero;
            popupRoot.anchorMax = Vector2.one;
            popupRoot.offsetMin = Vector2.zero;
            popupRoot.offsetMax = Vector2.zero;

            RectTransform dim = CreateRect("Dim", popupRoot);
            Stretch(dim);
            Image dimImage = dim.gameObject.AddComponent<Image>();
            dimImage.color = new Color(0f, 0f, 0f, 0.66f);
            dimImage.raycastTarget = true;

            RectTransform panel = CreateRect("Panel", popupRoot);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(600f, 440f);
            Image panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = new Color(0.10f, 0.11f, 0.14f, 0.98f);
            panelImage.raycastTarget = true;

            CreateText(
                panel, "Title", "拆除建筑", 30, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f),
                new Vector2(540f, 42f));

            popupBody = CreateText(
                panel, "Body", string.Empty, 20, new Color(0.9f, 0.91f, 0.94f, 1f),
                TextAnchor.UpperLeft,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -76f),
                new Vector2(540f, 150f));

            refundMoneyButton = CreateButton(
                panel, "Button_返还金币", string.Empty,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 170f),
                new Vector2(540f, 34f), new Color(0.22f, 0.24f, 0.28f, 1f), 17);
            refundMoneyLabel = refundMoneyButton.GetComponentInChildren<Text>(true);
            refundMoneyButton.onClick.AddListener(HandleRefundMoneyClicked);

            refundMaterialsButton = CreateButton(
                panel, "Button_返还原料", string.Empty,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 132f),
                new Vector2(540f, 34f), new Color(0.22f, 0.24f, 0.28f, 1f), 17);
            refundMaterialsLabel = refundMaterialsButton.GetComponentInChildren<Text>(true);
            refundMaterialsButton.onClick.AddListener(HandleRefundMaterialsClicked);

            skipButton = CreateButton(
                panel, "Button_不再显示", string.Empty,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 94f),
                new Vector2(540f, 34f), new Color(0.22f, 0.24f, 0.28f, 1f), 17);
            skipLabel = skipButton.GetComponentInChildren<Text>(true);
            skipButton.onClick.AddListener(HandleSkipClicked);

            Button confirm = CreateButton(
                panel, "Button_确认拆除", "确认拆除",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-92f, 32f),
                new Vector2(160f, 48f), DemolishRed, 20);
            confirm.onClick.AddListener(HandleConfirmClicked);

            Button cancel = CreateButton(
                panel, "Button_取消", "取消",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(92f, 32f),
                new Vector2(160f, 48f), new Color(0.32f, 0.34f, 0.38f, 1f), 20);
            cancel.onClick.AddListener(HandleCancelClicked);

            RefreshRefundLabels();
            RefreshSkipLabel();
            popupRoot.gameObject.SetActive(false);
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Text CreateText(
            Transform parent,
            string name,
            string content,
            int fontSize,
            Color color,
            TextAnchor alignment,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 position,
            Vector2 size)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Text text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Button CreateButton(
            Transform parent,
            string name,
            string label,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 position,
            Vector2 size,
            Color normalColor,
            int fontSize)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image image = rect.gameObject.AddComponent<Image>();
            image.color = Color.white;

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ApplyButtonColor(button, normalColor);

            Text text = CreateText(
                rect, "Text", label, fontSize, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                size - new Vector2(12f, 8f));
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            return button;
        }
    }
}
