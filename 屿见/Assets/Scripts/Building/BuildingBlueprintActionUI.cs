using UnityEngine;
using UnityEngine.UI;

namespace Yujian.Building
{
    /// <summary>
    /// 蓝图旁的 × / √ / 撤回 按钮（2026-10-26 按建筑系统策划案改造）。
    ///
    /// 规格：
    ///   · 蓝图跟随指针时，左侧显示 ×、右侧显示 √；占地不合法时 √ 不可点；
    ///   · 点 √ 后蓝图落位，× / √ 消失，撤回出现在原 × 的位置；
    ///   · 材料凑齐后右侧 √ 重新出现，点它建成建筑；
    ///   · 撤回优先退最后放入的材料；蓝图里没有材料时，撤回蓝图本身。
    ///
    /// 为什么放在屏幕空间而不是世界空间：
    ///   蓝图占地 4×4，正面俯视时世界空间文字会被建筑挡住，而且字号随镜头远近变化。
    ///   用 uGUI 投影到蓝图的屏幕位置，按钮大小稳定，也直接沿用工程现有的按钮模板与 EventSystem。
    ///
    /// 组件挂在「蓝图按钮」容器上，容器始终激活；真正显隐的是 Content 子物体。
    /// 不能把组件挂到会被隐藏的 Content 上——那样组件停用后 LateUpdate 不再执行，按钮就再也回不来。
    /// </summary>
    public class BuildingBlueprintActionUI : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("建筑系统入口。用来读当前蓝图 / 相位 / 落位坐标")]
        [SerializeField] private BuildingManager buildingManager;

        [Tooltip("建造结算。落位后的 √ 与撤回都转给它")]
        [SerializeField] private BuildingConstruction construction;

        [Tooltip("放置控制器。落位前的 √ 与当前吸附位置都来自它")]
        [SerializeField] private BuildingPlacement placement;

        [Tooltip("把蓝图世界坐标投影到屏幕的相机。留空则用 MainCamera")]
        [SerializeField] private Camera viewCamera;

        [Tooltip("按钮所在的 Canvas。留空则从父物体上找")]
        [SerializeField] private Canvas canvas;

        [Header("界面")]
        [Tooltip("真正显示 / 隐藏的内容根。按钮都挂在它下面；它可以是关闭的，本组件仍能把它打开")]
        [SerializeField] private RectTransform actionRoot;

        [Tooltip("左侧 ×：放弃蓝图")]
        [SerializeField] private Button cancelButton;

        [Tooltip("右侧 √：落位 / 材料齐后建成")]
        [SerializeField] private Button confirmButton;

        [Tooltip("左侧撤回：退材料 / 退蓝图。只在蓝图落位后显示")]
        [SerializeField] private Button undoButton;

        [Header("布局")]
        [Tooltip("按钮中心相对蓝图屏幕位置的偏移（像素）。往上抬一点，避免压住蓝图本体")]
        [SerializeField] private Vector2 screenOffset = new Vector2(0f, 48f);

        [Tooltip("按钮整体离屏幕边缘的最小距离（参考分辨率像素）")]
        [SerializeField] private Vector2 edgePadding = new Vector2(80f, 64f);

        [Header("调试")]
        [SerializeField] private bool logActions = true;

        private RectTransform canvasRect;
        private bool wired;

        private void Awake()
        {
            if (buildingManager == null)
            {
                Debug.LogError("[BuildingBlueprintActionUI] BuildingManager 未配置，蓝图按钮不会工作。", this);
            }

            if (placement == null)
            {
                Debug.LogError("[BuildingBlueprintActionUI] BuildingPlacement 未配置，落位前的 √ 点了不会有反应。", this);
            }

            if (construction == null)
            {
                Debug.LogError("[BuildingBlueprintActionUI] BuildingConstruction 未配置，" +
                               "落位后的 √ 与撤回点了不会有反应。", this);
            }

            if (viewCamera == null)
            {
                viewCamera = Camera.main;

                if (viewCamera == null)
                {
                    Debug.LogError("[BuildingBlueprintActionUI] 没有相机，无法把蓝图位置投影到屏幕，按钮不会出现。", this);
                }
            }

            if (canvas == null)
            {
                canvas = GetComponentInParent<Canvas>();
            }

            if (canvas == null)
            {
                Debug.LogError("[BuildingBlueprintActionUI] 找不到 Canvas，按钮无法定位。", this);
            }
            else
            {
                canvasRect = canvas.transform as RectTransform;
            }

            if (actionRoot == null)
            {
                // 向导会把 Content 指过来。这里只做一个兜底，避免手摆的层级完全不能工作
                actionRoot = transform.childCount > 0
                    ? transform.GetChild(0) as RectTransform
                    : transform as RectTransform;

                Debug.LogWarning("[BuildingBlueprintActionUI] Action Root 未配置，已退回使用 " +
                                 $"「{actionRoot?.name}」。建议重跑菜单「屿见/建筑/配置蓝图按钮（×/√）」。", this);
            }

            if (actionRoot == transform as RectTransform)
            {
                Debug.LogError("[BuildingBlueprintActionUI] Action Root 指向了本组件所在的物体。" +
                               "隐藏它会把组件一起停掉，按钮再也回不来。" +
                               "请把本组件挂在「蓝图按钮」容器上，Action Root 指向其 Content 子物体。", this);
                actionRoot = null;
            }

            WarnIfMissing(cancelButton, "×");
            WarnIfMissing(confirmButton, "√");
            WarnIfMissing(undoButton, "撤回");

            SetVisible(false);
        }

        private void OnEnable()
        {
            WireButtons();
            Refresh();
        }

        private void OnDisable()
        {
            UnwireButtons();
            SetVisible(false);
        }

        private void LateUpdate()
        {
            // 每帧重算位置与可用状态：蓝图在动、材料在变、相位也会切换，用事件反而容易漏
            Refresh();
        }

        // ---------------- 按钮状态 ----------------

        private void Refresh()
        {
            if (buildingManager == null || actionRoot == null || !buildingManager.IsPlacing)
            {
                SetVisible(false);
                return;
            }

            if (!TryGetAnchorWorldPosition(out Vector3 worldPosition))
            {
                SetVisible(false);
                return;
            }

            if (!TryProjectToCanvas(worldPosition, out Vector2 localPosition))
            {
                SetVisible(false);
                return;
            }

            actionRoot.anchoredPosition = ClampToCanvas(localPosition);

            bool filling = buildingManager.Phase == BuildingPhase.Filling;
            bool canPlace = !filling && placement != null && placement.IsCurrentPlacementValid;
            bool canBuild = filling && construction != null && construction.IsSatisfied;

            // 落位前：× 与 √ 都在，√ 的可用性表示「这里能不能放」；
            // 落位后：撤回占 × 的位置，√ 先消失，材料齐了再出现
            SetButtonVisible(cancelButton, !filling);
            SetButtonVisible(undoButton, filling);
            SetButtonVisible(confirmButton, filling ? canBuild : true);

            if (confirmButton != null)
            {
                confirmButton.interactable = filling ? canBuild : canPlace;
            }

            SetVisible(true);
        }

        /// <summary>按钮要贴住的世界位置：落位后用锚点，落位前用最近一次吸附点，再退回蓝图当前位置。</summary>
        private bool TryGetAnchorWorldPosition(out Vector3 worldPosition)
        {
            if (buildingManager.Phase == BuildingPhase.Filling)
            {
                worldPosition = buildingManager.AnchoredPosition;
                return true;
            }

            if (placement != null && placement.HasCurrentSnappedPose)
            {
                worldPosition = placement.CurrentSnappedCenter;
                return true;
            }

            BuildingBlueprint blueprint = buildingManager.CurrentBlueprint;

            if (blueprint != null)
            {
                worldPosition = blueprint.transform.position;
                return true;
            }

            worldPosition = default;
            return false;
        }

        /// <summary>世界坐标 → Canvas 本地坐标。蓝图在相机背后时返回 false。</summary>
        private bool TryProjectToCanvas(Vector3 worldPosition, out Vector2 localPosition)
        {
            localPosition = default;

            if (viewCamera == null || canvasRect == null)
            {
                return false;
            }

            Vector3 screenPosition = viewCamera.WorldToScreenPoint(worldPosition);

            if (screenPosition.z <= 0f)
            {
                return false;
            }

            screenPosition.x += screenOffset.x;
            screenPosition.y += screenOffset.y;

            // Screen Space - Overlay 的 Canvas 用 null 相机；Camera / World Space 才传 worldCamera
            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screenPosition, uiCamera, out localPosition);
        }

        /// <summary>把按钮整体夹在屏幕内，避免蓝图贴边时按钮跑到屏幕外点不到。</summary>
        private Vector2 ClampToCanvas(Vector2 localPosition)
        {
            Rect canvasBounds = canvasRect.rect;
            Vector2 size = actionRoot.rect.size;

            float halfWidth = size.x * 0.5f;
            float halfHeight = size.y * 0.5f;
            float minX = canvasBounds.xMin + halfWidth + edgePadding.x;
            float maxX = canvasBounds.xMax - halfWidth - edgePadding.x;
            float minY = canvasBounds.yMin + halfHeight + edgePadding.y;
            float maxY = canvasBounds.yMax - halfHeight - edgePadding.y;

            // 屏幕比按钮还小（极端分辨率）时不夹，否则 Clamp 的 min > max 会返回 min
            if (minX <= maxX)
            {
                localPosition.x = Mathf.Clamp(localPosition.x, minX, maxX);
            }

            if (minY <= maxY)
            {
                localPosition.y = Mathf.Clamp(localPosition.y, minY, maxY);
            }

            return localPosition;
        }

        // ---------------- 点击 ----------------

        private void WireButtons()
        {
            if (wired)
            {
                return;
            }

            wired = true;

            if (cancelButton != null)
            {
                cancelButton.onClick.AddListener(HandleCancelClicked);
            }

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

            if (cancelButton != null)
            {
                cancelButton.onClick.RemoveListener(HandleCancelClicked);
            }

            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(HandleConfirmClicked);
            }

            if (undoButton != null)
            {
                undoButton.onClick.RemoveListener(HandleUndoClicked);
            }
        }

        private void HandleCancelClicked()
        {
            if (buildingManager == null)
            {
                return;
            }

            if (logActions)
            {
                Debug.Log("[BuildingBlueprintActionUI] ×：放弃当前蓝图。");
            }

            buildingManager.CancelSelection();
            Refresh();
        }

        private void HandleConfirmClicked()
        {
            if (buildingManager == null)
            {
                return;
            }

            if (buildingManager.Phase == BuildingPhase.Filling)
            {
                if (construction == null)
                {
                    Debug.LogError("[BuildingBlueprintActionUI] BuildingConstruction 未配置，无法建成。", this);
                    return;
                }

                construction.ConfirmBuild();
            }
            else
            {
                if (placement == null)
                {
                    Debug.LogError("[BuildingBlueprintActionUI] BuildingPlacement 未配置，无法落位。", this);
                    return;
                }

                placement.TryAnchorCurrentBlueprint();
            }

            // 落位成功后相位在同一帧内已经切成 Filling，立刻刷新一次，
            // 让 × / √ 立即换成「撤回 / 空」，而不是等到下一帧 LateUpdate
            Refresh();
        }

        private void HandleUndoClicked()
        {
            if (construction == null)
            {
                Debug.LogError("[BuildingBlueprintActionUI] BuildingConstruction 未配置，无法撤回。", this);
                return;
            }

            construction.Undo();
            Refresh();
        }

        // ---------------- 显隐 ----------------

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
                // 材料面板是从底部滑入的，可能比本容器晚创建；每次露出时提到最上层
                transform.SetAsLastSibling();
            }
        }

        private static void SetButtonVisible(Button button, bool visible)
        {
            if (button != null && button.gameObject.activeSelf != visible)
            {
                button.gameObject.SetActive(visible);
            }
        }

        private void WarnIfMissing(Button button, string label)
        {
            if (button == null)
            {
                Debug.LogError($"[BuildingBlueprintActionUI]「{label}」按钮未配置，点了不会有反应。" +
                               "请重跑菜单「屿见/建筑/配置蓝图按钮（×/√）」。", this);
            }
        }
    }
}
