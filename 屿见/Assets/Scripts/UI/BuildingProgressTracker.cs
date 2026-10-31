using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Yujian.Building;
using Yujian.Core.Events;
using Yujian.Player;

namespace Yujian.UI
{
    /// <summary>
    /// 把「建筑建成」翻译成两种进度横幅（策划案 4.1 / 4.2）。
    ///
    /// 数据全部按场上建筑现算，不存一份进度：
    ///   · 特殊建筑进度来自 <see cref="MilestoneProgress"/> 的配置与场上实例；
    ///   · 普通建筑进度来自 <see cref="PlayerLevel"/> 的建筑总数与下一级阈值。
    ///
    /// 特殊建筑先播特殊进度，再播普通进度；普通建筑只播普通进度。
    /// 读档恢复不走 BuildingPlaced，所以不会在进游戏时弹一堆横幅。
    /// </summary>
    public class BuildingProgressTracker : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("建筑管理器")]
        [SerializeField] private BuildingManager buildingManager;

        [Tooltip("特殊建筑清单与计数")]
        [SerializeField] private MilestoneProgress milestoneProgress;

        [Tooltip("建筑数 → 等级")]
        [SerializeField] private PlayerLevel playerLevel;

        [Tooltip("顶部进度横幅")]
        [SerializeField] private BuildingProgressBannerUI banner;

        [Header("调试")]
        [Tooltip("是否在 Console 打印进度结算")]
        [SerializeField] private bool logProgress = true;

        private void OnEnable()
        {
            EventBus.Subscribe<BuildingPlaced>(HandleBuildingPlaced);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<BuildingPlaced>(HandleBuildingPlaced);
        }

        private void HandleBuildingPlaced(BuildingPlaced evt)
        {
            if (banner == null)
            {
                return;
            }

            if (buildingManager == null || playerLevel == null)
            {
                Debug.LogError("[BuildingProgressTracker] BuildingManager / PlayerLevel 未配置，" +
                               "建筑进度横幅不会显示。请重新运行「屿见/建筑/配置建筑进度 UI」。", this);
                return;
            }

            bool isSpecial = milestoneProgress != null && milestoneProgress.IsMilestone(evt.Data);

            if (isSpecial)
            {
                List<BuildingData> remaining = milestoneProgress.GetRemainingMilestones();
                int total = milestoneProgress.Milestones.Count;
                int completed = Mathf.Max(0, total - remaining.Count);

                banner.ShowSpecial(completed, total, BuildRemainingText(remaining));

                if (logProgress)
                {
                    Debug.Log($"[BuildingProgressTracker] 特殊建筑 {completed}/{total}，" +
                              $"还差：{BuildRemainingText(remaining)}");
                }
            }

            int current = playerLevel.BuildingCount;
            int previousLevel = playerLevel.GetLevelForBuildingCount(Mathf.Max(0, current - 1));
            int level = playerLevel.CurrentLevel;
            bool leveledUp = level > previousLevel;

            bool hasNext = playerLevel.TryGetNextLevel(out int nextBuildingCount, out _);
            bool isMaxLevel = playerLevel.IsMaxLevel;
            int target = hasNext ? nextBuildingCount : current;

            banner.ShowNormal(current, target, level, isMaxLevel, leveledUp);

            if (logProgress)
            {
                Debug.Log(hasNext
                    ? $"[BuildingProgressTracker] 普通建筑进度 {current}/{target}，等级 {level}" +
                      (leveledUp ? "（升级）" : string.Empty)
                    : $"[BuildingProgressTracker] 普通建筑进度：已满级，等级 {level}，共 {current} 座");
            }
        }

        private static string BuildRemainingText(IReadOnlyList<BuildingData> remaining)
        {
            if (remaining == null || remaining.Count == 0)
            {
                return "特殊建筑已全部建成";
            }

            StringBuilder builder = new StringBuilder("还差：");

            for (int i = 0; i < remaining.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append('、');
                }

                BuildingData data = remaining[i];
                builder.Append(data != null ? data.BuildingName : "未命名");
            }

            return builder.ToString();
        }
    }
}
