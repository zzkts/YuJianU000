using UnityEngine;

namespace Yujian.UI
{
    /// <summary>
    /// 面板的滑入 / 滑出过渡。
    ///
    /// 不建 Animator、不引入补间库：这里就是把 RectTransform 的 Y 在两个位置之间插值。
    /// 「显示位置」= 在场景里摆好的位置（Awake 时记下来），「隐藏位置」= 再往下挪 Hidden Offset。
    /// 所以摆放位置就是面板弹出后停在哪；Hidden Offset 只要够大（≥ 面板高度 + 下边距），
    /// 效果就等价于「从屏幕底部外面滑上来」。
    ///
    /// 滑完之后会把 **target 停用**：省掉隐藏面板每帧的 UI 重建与射线检测，也保证它不在时点不到。
    /// 注意：停用的是 target，本组件自己必须留在激活的物体上，否则没人把它滑回来。
    ///
    /// 用 Time.unscaledDeltaTime 而不是 deltaTime：面板的显隐不该被暂停（Time.timeScale = 0）冻住，
    /// 与 ShopUI / BuildingMaterialPanel 里状态行清空用的是同一套时间。
    /// </summary>
    [DisallowMultipleComponent]
    public class SlidingPanel : MonoBehaviour
    {
        [Tooltip("要滑动的物体。留空则用本物体所在的那个 RectTransform")]
        [SerializeField] private RectTransform target;

        [Tooltip("隐藏时相对显示位置往下挪多少像素。必须是负数，且绝对值要大于面板高度，" +
                 "否则会露出半截。由向导按面板尺寸算好")]
        [SerializeField] private float hiddenOffsetY = -610f;

        [Tooltip("滑一趟的秒数。<= 0 表示不做动画，直接到位")]
        [SerializeField] private float duration = 0.22f;

        [Tooltip("游戏开始时就是隐藏状态")]
        [SerializeField] private bool startHidden = true;

        /// <summary>显示位置（场景里摆好的 Y）与隐藏位置（再往下挪 Hidden Offset）。</summary>
        private float shownY;
        private float hiddenY;

        /// <summary>0 = 完全显示，1 = 完全隐藏。</summary>
        private float progress = 1f;

        private bool shown;
        private bool ready;

        /// <summary>当前是不是处于「显示」状态（动画途中按目标算）。</summary>
        public bool IsShown => shown;

        private void Awake()
        {
            if (target == null)
            {
                target = transform as RectTransform;
            }

            if (target == null)
            {
                Debug.LogError("[SlidingPanel] 没有可滑动的 RectTransform，过渡不会生效。", this);
                enabled = false;
                return;
            }

            shownY = target.anchoredPosition.y;
            hiddenY = shownY + hiddenOffsetY;
            ready = true;

            // 直接摆到初始状态，不走动画：进场时不该看到面板从屏幕里滑出去
            SetShown(!startHidden, true);
        }

        /// <summary>滑出来。</summary>
        public void Show()
        {
            SetShown(true);
        }

        /// <summary>滑回去。</summary>
        public void Hide()
        {
            SetShown(false);
        }

        /// <summary>
        /// 设置显隐。<paramref name="instant"/> = true 时不做动画，直接到位。
        /// 反复调用同一个值不会有副作用（重复 Show() 不会重新播一遍）。
        /// </summary>
        public void SetShown(bool value, bool instant = false)
        {
            if (!ready)
            {
                // Awake 没跑完就被调用（例如同一个物体上另一个组件的 Awake 里），记下来，Awake 里会补上
                shown = value;
                return;
            }

            shown = value;

            if (value && !target.gameObject.activeSelf)
            {
                // 先激活再动，否则动画是看不见的
                target.gameObject.SetActive(true);
            }

            if (instant || duration <= 0f)
            {
                progress = value ? 0f : 1f;
                ApplyPosition();
                ApplyActiveState();
            }
        }

        private void Update()
        {
            if (!ready)
            {
                return;
            }

            float goal = shown ? 0f : 1f;

            if (Mathf.Approximately(progress, goal))
            {
                return;
            }

            float step = duration > 0f ? Time.unscaledDeltaTime / duration : 1f;

            progress = Mathf.MoveTowards(progress, goal, step);

            ApplyPosition();

            if (Mathf.Approximately(progress, goal))
            {
                // 完全到位：显示中 → 保证是激活的；隐藏完 → 停用，不再吃每帧的开销
                ApplyActiveState();
            }
        }

        private void ApplyPosition()
        {
            // SmoothStep 让两端各缓一下，滑出滑入都不生硬
            float eased = Mathf.SmoothStep(0f, 1f, progress);

            Vector2 position = target.anchoredPosition;
            position.y = Mathf.Lerp(shownY, hiddenY, eased);
            target.anchoredPosition = position;
        }

        private void ApplyActiveState()
        {
            bool shouldBeActive = progress < 1f;

            if (target.gameObject.activeSelf != shouldBeActive)
            {
                target.gameObject.SetActive(shouldBeActive);
            }
        }
    }
}
