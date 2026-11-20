using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.Core.Events;

namespace Yujian.UI
{
    /// <summary>
    /// 迁移确认 / 撤回按钮（策划案 3.3）。
    ///
    /// 松开指针后出现在被移动建筑上方：「确认」提交新占格，「撤回」回到原位并退出迁移。
    /// 位置每帧跟建筑走；只在 PendingConfirm 状态出现，拖动过程中不显示。
    ///
    /// 本类只转发点击，不登记占格也不改状态机。
    /// </summary>
    public class BuildingMigrationUI : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("建筑迁移状态机")]
        [SerializeField] private BuildingMigration migration;

        [Tooltip("把建筑世界坐标投影到屏幕的相机。留空则用 MainCamera")]
        [SerializeField] private Camera viewCamera;

        [Tooltip("按钮所在的 Canvas。留空则从父物体上找")]
        [SerializeField] private Canvas canvas;

        [Header("界面")]
        [Tooltip("真正显示 / 隐藏的内容根（组件挂在常开的容器上）")]
        [SerializeField] private RectTransform actionRoot;

        [Tooltip("确认迁移")]
        [SerializeField] private Button confirmButton;

        [Tooltip("撤回迁移")]
        [SerializeField] private Button undoButton;

        [Tooltip("按钮下方的状态文字：可放置 = 绿字，不可放置 = 红字 + 原因")]
        [SerializeField] private Text statusText;

        [Tooltip("拆除入口。留空会在运行时自动挂到本物体上")]
        [SerializeField] private BuildingDemolitionUI demolitionUI;

        [Header("旋转（3.3 第 4 条）")]
        [Tooltip("建筑下方的旋转按钮容器。留空会在运行时自动生成")]
        [SerializeField] private RectTransform rotationRoot;

        [Tooltip("左边按钮：顺时针转 90°。留空会按确认按钮的样式自动生成")]
        [SerializeField] private Button rotateLeftButton;

        [Tooltip("右边按钮：逆时针转 90°。留空会按确认按钮的样式自动生成")]
        [SerializeField] private Button rotateRightButton;

        [Tooltip("旋转按钮相对建筑底部的屏幕偏移（像素）。负 Y = 往屏幕下方")]
        [SerializeField] private Vector2 rotationScreenOffset = new Vector2(0f, -84f);

        [Tooltip("投影用的世界高度：0 = 建筑轴心（一般就是地面）")]
        [SerializeField] private float rotationWorldHeightOffset = 0.1f;

        [Tooltip("可放置时状态文字的颜色")]
        [SerializeField] private Color validColor = new Color(0.35f, 0.9f, 0.45f, 1f);

        [Tooltip("不可放置时状态文字的颜色")]
        [SerializeField] private Color invalidColor = new Color(0.95f, 0.4f, 0.35f, 1f);

        [Header("布局")]
        [Tooltip("按钮组相对建筑顶部的屏幕偏移（像素）")]
        [SerializeField] private Vector2 screenOffset = new Vector2(0f, 90f);

        [Tooltip("建筑中心往上抬多少世界单位再投影")]
        [SerializeField] private float worldHeightOffset = 4.2f;

        [Header("调试")]
        [SerializeField] private bool logActions = true;

        private bool wired;
        private bool rotationWired;

        private void Awake()
        {
            if (migration == null)
            {
                Debug.LogError("[BuildingMigrationUI] BuildingMigration 未配置，确认 / 撤回按钮不会工作。", this);
            }

            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }

            if (canvas == null)
            {
                canvas = GetComponentInParent<Canvas>();
            }

            if (actionRoot == null)
            {
                actionRoot = transform.childCount > 0
                    ? transform.GetChild(0) as RectTransform
                    : transform as RectTransform;
            }

            if (actionRoot == transform as RectTransform)
            {
                Debug.LogError("[BuildingMigrationUI] Action Root 不能指向本组件所在的物体：" +
                               "隐藏它会把组件一起停掉。请指向其 Content 子物体。", this);
                actionRoot = null;
            }

            WarnIfMissing(confirmButton, "确认");
            WarnIfMissing(undoButton, "撤回");
            EnsureDemolitionUI();
            EnsureRotationControls();
        }

        private void OnEnable()
        {
            WireButtons();
            WireRotationButtons();
            Refresh();
        }

        private void OnDisable()
        {
            UnwireButtons();
            UnwireRotationButtons();

            if (demolitionUI != null)
            {
                demolitionUI.ClosePopup();
            }

            SetVisible(false);
            SetRotationVisible(false);
        }

        private void LateUpdate()
        {
            Refresh();
        }

        private void Refresh()
        {
            RefreshActionPanel();
            RefreshRotationControls();
        }

        private void RefreshActionPanel()
        {
            if (actionRoot == null || migration == null || !migration.IsPendingConfirm)
            {
                SetVisible(false);
                return;
            }

            GameObject target = migration.Target;

            if (target == null)
            {
                SetVisible(false);
                return;
            }

            Vector3 worldPosition = target.transform.position + Vector3.up * worldHeightOffset;

            if (!WorldToCanvas.TryProject(viewCamera, canvas, worldPosition, screenOffset,
                    out Vector2 localPosition))
            {
                SetVisible(false);
                return;
            }

            actionRoot.anchoredPosition = WorldToCanvas.ClampToCanvas(localPosition, actionRoot, canvas);

            if (confirmButton != null)
            {
                confirmButton.interactable = migration.CanConfirm;
            }

            if (statusText != null)
            {
                if (migration.IsValid)
                {
                    statusText.text = "可放置，点 √ 确认";
                    statusText.color = validColor;
                }
                else
                {
                    statusText.text = $"不可放置：{migration.StatusReason}";
                    statusText.color = invalidColor;
                }
            }

            SetVisible(true);
        }

        private void RefreshRotationControls()
        {
            if (rotationRoot == null || migration == null || !migration.CanRotate)
            {
                SetRotationVisible(false);
                return;
            }

            GameObject target = migration.Target;

            if (target == null)
            {
                SetRotationVisible(false);
                return;
            }

            Vector3 worldPosition = target.transform.position + Vector3.up * rotationWorldHeightOffset;

            if (!WorldToCanvas.TryProject(viewCamera, canvas, worldPosition, rotationScreenOffset,
                    out Vector2 localPosition))
            {
                SetRotationVisible(false);
                return;
            }

            rotationRoot.anchoredPosition = WorldToCanvas.ClampToCanvas(localPosition, rotationRoot, canvas);
            SetRotationVisible(true);
        }

        private void WireButtons()
        {
            if (wired)
            {
                return;
            }

            wired = true;

            if (confirmButton != null)
            {
                confirmButton.onClick.AddListener(HandleConfirmClicked);
            }

            if (undoButton != null)
            {
                undoButton.onClick.AddListener(HandleUndoClicked);
            }
        }

        private void UnwireButtons()
        {
            if (!wired)
            {
                return;
            }

            wired = false;

            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(HandleConfirmClicked);
            }

            if (undoButton != null)
            {
                undoButton.onClick.RemoveListener(HandleUndoClicked);
            }
        }

        private void WireRotationButtons()
        {
            if (rotationWired)
            {
                return;
            }

            rotationWired = true;

            if (rotateLeftButton != null)
            {
                rotateLeftButton.onClick.AddListener(HandleRotateClockwiseClicked);
            }

            if (rotateRightButton != null)
            {
                rotateRightButton.onClick.AddListener(HandleRotateCounterClockwiseClicked);
            }
        }

        private void UnwireRotationButtons()
        {
            if (!rotationWired)
            {
                return;
            }

            rotationWired = false;

            if (rotateLeftButton != null)
            {
                rotateLeftButton.onClick.RemoveListener(HandleRotateClockwiseClicked);
            }

            if (rotateRightButton != null)
            {
                rotateRightButton.onClick.RemoveListener(HandleRotateCounterClockwiseClicked);
            }
        }

        private void HandleConfirmClicked()
        {
            if (migration == null)
            {
                return;
            }

            bool success = migration.Confirm(out string message);

            if (logActions)
            {
                Debug.Log($"[BuildingMigrationUI] 确认迁移：{message}");
            }

            EventBus.Publish(new MessageShown(message, success));
            Refresh();
        }

        private void HandleUndoClicked()
        {
            if (migration == null)
            {
                return;
            }

            if (logActions)
            {
                Debug.Log("[BuildingMigrationUI] 撤回迁移。");
            }

            migration.Revert();
            Refresh();
        }

        /// <summary>左边按钮：顺时针 90°（策划案 3.3 第 4 条）。</summary>
        private void HandleRotateClockwiseClicked()
        {
            if (migration == null || !migration.RotateClockwise())
            {
                return;
            }

            if (logActions)
            {
                Debug.Log($"[BuildingMigrationUI] 顺时针旋转，当前朝向 {migration.QuarterTurns * 90}°。");
            }

            Refresh();
        }

        /// <summary>右边按钮：逆时针 90°（策划案 3.3 第 4 条）。</summary>
        private void HandleRotateCounterClockwiseClicked()
        {
            if (migration == null || !migration.RotateCounterClockwise())
            {
                return;
            }

            if (logActions)
            {
                Debug.Log($"[BuildingMigrationUI] 逆时针旋转，当前朝向 {migration.QuarterTurns * 90}°。");
            }

            Refresh();
        }

        private void SetVisible(bool visible)
        {
            if (actionRoot == null)
            {
                return;
            }

            if (actionRoot.gameObject.activeSelf == visible)
            {
                return;
            }

            actionRoot.gameObject.SetActive(visible);

            if (visible)
            {
                transform.SetAsLastSibling();
            }
        }

        private void SetRotationVisible(bool visible)
        {
            if (rotationRoot == null)
            {
                return;
            }

            if (rotationRoot.gameObject.activeSelf == visible)
            {
                return;
            }

            rotationRoot.gameObject.SetActive(visible);

            if (visible)
            {
                rotationRoot.SetAsLastSibling();
            }
        }

        /// <summary>
        /// 场景里没手工摆按钮时，按「确认」按钮的样式自动生成建筑下方的左 / 右旋转按钮。
        /// 和拆除按钮同一个思路：运行时生成，策划不用改场景也能直接用。
        /// </summary>
        private void EnsureRotationControls()
        {
            if (rotationRoot != null && rotateLeftButton != null && rotateRightButton != null)
            {
                return;
            }

            if (actionRoot == null)
            {
                return;
            }

            if (rotationRoot == null)
            {
                Transform parent = actionRoot.parent != null ? actionRoot.parent : actionRoot;
                GameObject rootObject = new GameObject("RotationControls", typeof(RectTransform));
                rootObject.layer = 5;
                rotationRoot = (RectTransform)rootObject.transform;
                rotationRoot.SetParent(parent, false);
                rotationRoot.anchorMin = new Vector2(0.5f, 0.5f);
                rotationRoot.anchorMax = new Vector2(0.5f, 0.5f);
                rotationRoot.pivot = new Vector2(0.5f, 0.5f);
                rotationRoot.sizeDelta = new Vector2(150f, 60f);
                rotationRoot.gameObject.SetActive(false);
            }

            if (rotateLeftButton == null)
            {
                rotateLeftButton = CreateRotationButton("Button_顺时针", -38f, '↻', '◀');
            }

            if (rotateRightButton == null)
            {
                rotateRightButton = CreateRotationButton("Button_逆时针", 38f, '↺', '▶');
            }
        }

        private Button CreateRotationButton(string name, float x, char preferredGlyph, char fallbackGlyph)
        {
            Button template = confirmButton != null ? confirmButton : undoButton;
            Button button = null;

            if (template != null)
            {
                GameObject clone = Instantiate(template.gameObject, rotationRoot, false);
                clone.name = name;
                clone.SetActive(true);
                button = clone.GetComponent<Button>();

                if (button != null)
                {
                    // 克隆会带走模板按钮上的监听，先清空再挂旋转自己的
                    button.onClick.RemoveAllListeners();
                }
            }

            if (button == null)
            {
                GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                go.layer = 5;
                go.transform.SetParent(rotationRoot, false);

                Image image = go.GetComponent<Image>();
                image.color = new Color(0.16f, 0.18f, 0.22f, 0.85f);

                button = go.GetComponent<Button>();
                button.targetGraphic = image;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
            }

            if (button.transform is RectTransform rect)
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(x, 0f);
                rect.sizeDelta = new Vector2(62f, 62f);
                rect.localScale = Vector3.one;
            }

            // 黑底：模板按钮是白底，白箭头会完全看不见
            if (button.targetGraphic != null)
            {
                button.targetGraphic.color = new Color(0.07f, 0.08f, 0.1f, 0.95f);
            }

            ApplyRotationButtonColors(button);

            LayoutElement layout = button.GetComponent<LayoutElement>();

            if (layout != null)
            {
                layout.ignoreLayout = true;
            }

            Text label = button.GetComponentInChildren<Text>(true);

            if (label != null)
            {
                Font font = label.font != null
                    ? label.font
                    : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                label.font = font;
                label.text = PickRotationGlyph(font, preferredGlyph, fallbackGlyph);
                label.fontSize = 36;
                label.fontStyle = FontStyle.Bold;
                label.color = Color.white;
                label.alignment = TextAnchor.MiddleCenter;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.verticalOverflow = VerticalWrapMode.Overflow;
            }

            return button;
        }

        /// <summary>
        /// 黑底按钮的按下 / 悬停反馈。正常态保持纯白倍率，让底图的黑不会被提亮成灰色。
        /// </summary>
        private static void ApplyRotationButtonColors(Button button)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.pressedColor = new Color(0.62f, 0.62f, 0.62f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
        }

        /// <summary>
        /// 优先用旋转符号（↻ / ↺）；内置字体缺字时退回三角箭头，避免按钮上出现空白方块。
        /// </summary>
        private static string PickRotationGlyph(Font font, char preferred, char fallback)
        {
            if (font == null)
            {
                return fallback.ToString();
            }

            try
            {
                return font.HasCharacter(preferred) ? preferred.ToString() : fallback.ToString();
            }
            catch (System.Exception)
            {
                return fallback.ToString();
            }
        }

        private void EnsureDemolitionUI()
        {
            if (demolitionUI == null)
            {
                demolitionUI = GetComponent<BuildingDemolitionUI>();
            }

            if (demolitionUI == null)
            {
                demolitionUI = gameObject.AddComponent<BuildingDemolitionUI>();
            }

            demolitionUI.Configure(migration, actionRoot, confirmButton, canvas);
        }

        private void WarnIfMissing(Button button, string label)
        {
            if (button == null)
            {
                Debug.LogError($"[BuildingMigrationUI]「{label}」按钮未配置，点了不会有反应。" +
                               "请运行菜单「屿见/建筑/配置建筑迁移与升级」。", this);
            }
        }
    }
}
