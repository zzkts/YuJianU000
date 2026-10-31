using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;

namespace Yujian.UI
{
    /// <summary>
    /// 三座主要建筑全部建成时的小窗口提示。
    ///
    /// 当前是骨架：标题 + 一句正文 + 「继续」按钮，文案都能在 Inspector 里改，
    /// 后续要填结局内容时不用改脚本。
    ///
    /// 显隐用 CanvasGroup，而不是 SetActive(false)：
    /// 把自己停用会连 OnAllCompleted 的订阅一起停掉，之后再也弹不出来。
    /// 本组件自己订阅 <see cref="MilestoneProgress.OnAllCompleted"/>，
    /// 不依赖建筑面板是否打开——万一将来在别的区域达成也能弹。
    ///
    /// 读档恢复到 3/3 不会触发达成事件，所以重新进游戏不会重复弹窗。
    /// </summary>
    public class MilestoneCompletePopup : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("里程碑进度。留空会明确报错，弹窗不会出现")]
        [SerializeField] private MilestoneProgress progress;

        [Tooltip("整块弹窗的 CanvasGroup。留空会在运行时自动补一个")]
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("内容")]
        [Tooltip("标题")]
        [SerializeField] private string title = "恭喜";

        [Tooltip("正文。后续填结局内容时改这里")]
        [TextArea(2, 4)]
        [SerializeField] private string message = "三座主要建筑已全部建成，屿见的故事还在继续。";

        [Tooltip("正文文本")]
        [SerializeField] private Text messageText;

        [Tooltip("标题文本")]
        [SerializeField] private Text titleText;

        [Tooltip("关闭弹窗的按钮")]
        [SerializeField] private Button continueButton;

        [Header("调试")]
        [Tooltip("是否在 Console 打印弹窗显隐")]
        [SerializeField] private bool logPopup = true;

        private bool shown;
        private bool warnedAboutMissingProgress;

        private void Awake()
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }

            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            HideImmediate();

            if (continueButton != null)
            {
                continueButton.onClick.AddListener(Hide);
            }
        }

        private void OnEnable()
        {
            if (progress == null)
            {
                if (!warnedAboutMissingProgress)
                {
                    warnedAboutMissingProgress = true;
                    Debug.LogError("[MilestoneCompletePopup] MilestoneProgress 未配置，达成时不会弹窗。" +
                                   "请运行菜单「屿见/建筑/配置主要建筑里程碑」自动接线。", this);
                }

                return;
            }

            progress.OnAllCompleted += HandleAllCompleted;
        }

        private void OnDisable()
        {
            if (progress != null)
            {
                progress.OnAllCompleted -= HandleAllCompleted;
            }
        }

        private void OnDestroy()
        {
            if (continueButton != null)
            {
                continueButton.onClick.RemoveListener(Hide);
            }
        }

        private void HandleAllCompleted()
        {
            if (shown)
            {
                return;
            }

            Show();
        }

        /// <summary>显示弹窗。重复调用是幂等的。</summary>
        public void Show()
        {
            if (shown)
            {
                return;
            }

            shown = true;

            if (titleText != null)
            {
                titleText.text = title;
            }

            if (messageText != null)
            {
                messageText.text = message;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }

            if (logPopup)
            {
                Debug.Log("[MilestoneCompletePopup] 已显示主要建筑达成提示。", this);
            }
        }

        /// <summary>关闭弹窗。重复调用是幂等的。</summary>
        public void Hide()
        {
            if (!shown)
            {
                return;
            }

            shown = false;
            HideImmediate();

            if (logPopup)
            {
                Debug.Log("[MilestoneCompletePopup] 已关闭主要建筑达成提示。", this);
            }
        }

        private void HideImmediate()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
        }
    }
}
