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
            Refresh();
        }

        private void Refresh()
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
