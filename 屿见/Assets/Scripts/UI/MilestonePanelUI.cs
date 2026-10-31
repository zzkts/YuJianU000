using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;

namespace Yujian.UI
{
    /// <summary>
    /// 建筑面板顶部的「主要建筑 X/3」。
    ///
    /// 只负责文字：订阅 <see cref="MilestoneProgress.OnProgressChanged"/>，
    /// 进度一变才写一次，**没有 Update**（与 CoinHudUI 同一约定）。
    /// 达成时的弹窗由 <see cref="MilestoneCompletePopup"/> 自己订阅，不经过本类。
    ///
    /// 读档恢复到 3/3 时只显示「主要建筑 3/3」，不会把祝贺文案提前打出来——
    /// 祝贺只在本次运行内第一次达成（OnAllCompleted）时出现。
    /// </summary>
    public class MilestonePanelUI : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("里程碑进度。留空会明确报错，文字不会更新")]
        [SerializeField] private MilestoneProgress progress;

        [Header("显示")]
        [Tooltip("建筑面板顶部的进度文本。必须是旧版 UnityEngine.UI.Text")]
        [SerializeField] private Text progressText;

        [Tooltip("进度格式。{0} = 已完成数，{1} = 总数")]
        [SerializeField] private string format = "主要建筑 {0}/{1}";

        [Tooltip("本次运行内三座全部建成后替换显示的文案")]
        [SerializeField] private string completedText = "恭喜！三座主要建筑已全部建成";

        [Header("配色")]
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color completedColor = new Color(1f, 0.85f, 0.30f);

        private bool celebrated;
        private bool warnedAboutMissingProgress;
        private bool warnedAboutBadFormat;

        private void OnEnable()
        {
            if (progress == null)
            {
                if (!warnedAboutMissingProgress)
                {
                    warnedAboutMissingProgress = true;
                    Debug.LogError("[MilestonePanelUI] MilestoneProgress 未配置，进度文字不会更新。" +
                                   "请运行菜单「屿见/建筑/配置主要建筑里程碑」自动接线。", this);
                }

                return;
            }

            progress.OnProgressChanged += Refresh;
            progress.OnAllCompleted += HandleAllCompleted;

            Refresh();
        }

        private void OnDisable()
        {
            if (progress != null)
            {
                progress.OnProgressChanged -= Refresh;
                progress.OnAllCompleted -= HandleAllCompleted;
            }
        }

        private void HandleAllCompleted()
        {
            celebrated = true;
            Refresh();
        }

        private void Refresh()
        {
            if (progressText == null || progress == null)
            {
                return;
            }

            if (progress.TotalCount <= 0)
            {
                progressText.text = string.Empty;
                return;
            }

            if (celebrated)
            {
                progressText.text = completedText;
                progressText.color = completedColor;
                return;
            }

            progressText.color = normalColor;

            if (string.IsNullOrEmpty(format))
            {
                progressText.text = $"主要建筑 {progress.CompletedCount}/{progress.TotalCount}";
                return;
            }

            try
            {
                progressText.text = string.Format(format, progress.CompletedCount, progress.TotalCount);
            }
            catch (System.FormatException)
            {
                if (!warnedAboutBadFormat)
                {
                    warnedAboutBadFormat = true;
                    Debug.LogError($"[MilestonePanelUI] 格式串「{format}」不合法，已退化成默认写法。", this);
                }

                progressText.text = $"主要建筑 {progress.CompletedCount}/{progress.TotalCount}";
            }
        }
    }
}
