using UnityEngine;

namespace Yujian.UI
{
    /// <summary>
    /// 一个可被 <see cref="UIManager"/> 统一开关的面板。
    ///
    /// 三种显隐方式，按优先级：
    ///   1. 配了 <see cref="SlidingPanel"/> → 交给它做滑入 / 滑出（会停用它的 target）；
    ///   2. 配了 CanvasGroup（或运行时自动补一个）→ 只改 alpha / interactable / blocksRaycasts，
    ///      **不停用物体**。这样挂在面板上的其他脚本不会因为 SetActive(false) 而停止响应；
    ///   3. 兜底才是 SetActive。target 与本组件同物体时禁止走这条（会把本组件一起停掉）。
    ///
    /// 本组件**不改变**场景里面板的初始状态。现有面板（商店 / 建筑 / 材料）仍由
    /// GameAreaController / BuildingMaterialPanel 自己管显隐，接入 UIManager 是后续逐步迁移的事。
    /// </summary>
    [DisallowMultipleComponent]
    public class UIPanel : MonoBehaviour
    {
        [Header("标识")]
        [Tooltip("全局唯一的面板 ID。同一个 ID 出现两次时 UIManager 会报错并保留第一个")]
        [SerializeField] private UIPanelId panelId = UIPanelId.None;

        [Header("显隐")]
        [Tooltip("面板内容根物体。留空表示本物体。" +
                 "**不要**指向本物体又用 SetActive 方式——那种情况会自动改用 CanvasGroup")]
        [SerializeField] private GameObject target;

        [Tooltip("用 CanvasGroup 淡入淡出 / 屏蔽点击，而不是停用物体。留空会在需要时自动补一个")]
        [SerializeField] private CanvasGroup canvasGroup;

        [Tooltip("滑入滑出过渡。配置后显隐交给它，CanvasGroup 不再参与")]
        [SerializeField] private SlidingPanel slide;

        [Header("行为")]
        [Tooltip("模态面板：打开时 UIManager.HasModalOpen 为 true，可以用来挡住挖矿 / 建造输入")]
        [SerializeField] private bool modal;

        [Header("调试")]
        [SerializeField] private bool logPanelFlow = false;

        private bool initialized;
        private bool visible;
        private bool warnedAboutSelfTarget;

        /// <summary>面板 ID。</summary>
        public UIPanelId Id => panelId;

        /// <summary>是否是模态面板。</summary>
        public bool IsModal => modal;

        /// <summary>当前是否处于显示状态（以本组件最后一次操作为准）。</summary>
        public bool IsVisible
        {
            get
            {
                EnsureInitialized();
                return visible;
            }
        }

        private void Awake()
        {
            EnsureInitialized();
            UIManager.Register(this);
        }

        /// <summary>显示面板。</summary>
        public void Show(bool instant = false)
        {
            SetVisible(true, instant);
        }

        /// <summary>隐藏面板。</summary>
        public void Hide(bool instant = false)
        {
            SetVisible(false, instant);
        }

        /// <summary>切换显隐。</summary>
        public void Toggle()
        {
            SetVisible(!IsVisible);
        }

        /// <summary>设置显隐。重复设置同一个值不会有副作用。</summary>
        public void SetVisible(bool value, bool instant = false)
        {
            EnsureInitialized();

            if (visible == value)
            {
                return;
            }

            visible = value;

            if (slide != null)
            {
                slide.SetShown(value, instant);
            }
            else if (EnsureCanvasGroup() != null)
            {
                ApplyCanvasGroup(value);
            }
            else
            {
                ApplyActive(value);
            }

            if (logPanelFlow)
            {
                Debug.Log($"[UIPanel] {panelId} → {(value ? "显示" : "隐藏")}", this);
            }
        }

        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;

            if (target == null)
            {
                target = gameObject;
            }

            if (slide != null)
            {
                visible = slide.IsShown;
            }
            else if (canvasGroup != null)
            {
                visible = canvasGroup.alpha > 0.001f && target.activeInHierarchy;
            }
            else
            {
                visible = target.activeSelf;
            }
        }

        private CanvasGroup EnsureCanvasGroup()
        {
            if (canvasGroup != null)
            {
                return canvasGroup;
            }

            // 滑入过渡优先；没有过渡时才考虑 CanvasGroup
            if (slide != null)
            {
                return null;
            }

            canvasGroup = target.GetComponent<CanvasGroup>();

            if (canvasGroup == null)
            {
                canvasGroup = target.AddComponent<CanvasGroup>();
            }

            return canvasGroup;
        }

        private void ApplyCanvasGroup(bool value)
        {
            canvasGroup.alpha = value ? 1f : 0f;
            canvasGroup.interactable = value;
            canvasGroup.blocksRaycasts = value;

            // CanvasGroup 不会自动恢复被停用的物体：如果之前用 SetActive 隐藏过同一目标，
            // 这里要把它打开，否则 alpha 改了也看不见
            if (value && !target.activeSelf)
            {
                target.SetActive(true);
            }
        }

        private void ApplyActive(bool value)
        {
            if (target == gameObject)
            {
                if (!warnedAboutSelfTarget)
                {
                    warnedAboutSelfTarget = true;
                    Debug.LogError("[UIPanel] Target 指向了本组件所在物体，且没有 CanvasGroup / SlidingPanel。" +
                                   "SetActive(false) 会把这个组件一起停掉，面板再也回不来。已改用 CanvasGroup。", this);
                }

                canvasGroup = gameObject.AddComponent<CanvasGroup>();
                ApplyCanvasGroup(value);
                return;
            }

            target.SetActive(value);
        }
    }
}
