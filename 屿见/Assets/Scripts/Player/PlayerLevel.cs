using System;
using UnityEngine;
using Yujian.Building;
using Yujian.Core.Events;

namespace Yujian.Player
{
    /// <summary>
    /// 玩家等级。
    /// 职责：统计已建成建筑数 → 查等级表 → 广播等级变化。
    /// 不生成矿石、不碰金币、不碰伤害、不做 UI——矿石系统订阅 <see cref="OnLevelChanged"/> 即可。
    ///
    /// 为什么不改 BuildingManager：它已有 OnBlueprintClosed(bool)，参数为 true 表示建造成功，
    /// 且广播发生在 placedBuildings.Add 之后。直接订阅这个既有事件就能拿到准确建筑数。
    /// </summary>
    public class PlayerLevel : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("建筑管理器，用来读取已建成建筑数与建造完成事件")]
        [SerializeField] private BuildingManager buildingManager;

        [Tooltip("建筑数 → 玩家等级表")]
        [SerializeField] private PlayerLevelTable levelTable;

        [Header("调试")]
        [Tooltip("是否在 Console 打印等级变化")]
        [SerializeField] private bool logLevelFlow = true;

        private int currentLevel;

        /// <summary>当前玩家等级。Start 之前为 0。</summary>
        public int CurrentLevel => currentLevel;

        /// <summary>当前场上已建成的建筑总数。</summary>
        public int BuildingCount => buildingManager != null ? buildingManager.PlacedBuildings.Count : 0;

        /// <summary>
        /// 下一个等级所需的建筑总数（建筑进度 UI 的 N）。
        /// 已满级或等级表未配置时返回 false。
        /// </summary>
        public bool TryGetNextLevel(out int nextBuildingCount, out int nextLevel)
        {
            nextBuildingCount = 0;
            nextLevel = 0;

            if (levelTable == null)
            {
                return false;
            }

            return levelTable.TryGetNextThreshold(BuildingCount, out nextBuildingCount, out nextLevel);
        }

        /// <summary>是否已到等级表最高级。等级表未配置时恒为 false。</summary>
        public bool IsMaxLevel => levelTable != null && !TryGetNextLevel(out _, out _);

        /// <summary>
        /// 按指定建筑总数查等级，不改变当前状态。
        /// 供进度 UI 判断「这次建造是否升级」用（拿当前建筑数 - 1 反查上一级）。
        /// </summary>
        public int GetLevelForBuildingCount(int buildingCount)
        {
            return levelTable != null ? levelTable.Evaluate(buildingCount) : 0;
        }

        /// <summary>等级变化时广播（参数是**新**等级）。</summary>
        public event Action<int> OnLevelChanged;

        private void OnEnable()
        {
            if (buildingManager != null)
            {
                buildingManager.OnBlueprintClosed += HandleBlueprintClosed;
            }
        }

        private void OnDisable()
        {
            if (buildingManager != null)
            {
                buildingManager.OnBlueprintClosed -= HandleBlueprintClosed;
            }
        }

        private void Start()
        {
            if (buildingManager == null)
            {
                Debug.LogError("[PlayerLevel] BuildingManager 未配置，无法统计建筑数，等级将一直是 0。", this);
                return;
            }

            if (levelTable == null)
            {
                Debug.LogError("[PlayerLevel] PlayerLevelTable 未配置，无法计算等级。", this);
                return;
            }

            Recalculate();
        }

        /// <summary>
        /// 重新统计建筑数并刷新等级。可被外部调用（例如拆除建筑后）。
        /// 等级只按表计算；是否允许降级由表的数据决定，本类不做额外拦截。
        /// </summary>
        public void Recalculate()
        {
            if (buildingManager == null || levelTable == null)
            {
                return;
            }

            int buildingCount = buildingManager.PlacedBuildings.Count;
            int newLevel = levelTable.Evaluate(buildingCount);

            if (newLevel == currentLevel)
            {
                return;
            }

            int previousLevel = currentLevel;
            currentLevel = newLevel;

            if (logLevelFlow)
            {
                Debug.Log($"[PlayerLevel] 建筑 {buildingCount} 座，等级 {previousLevel} → {currentLevel}");
            }

            OnLevelChanged?.Invoke(currentLevel);
            EventBus.Publish(new LevelChanged(currentLevel));
        }

        /// <summary>
        /// 建造成功时重算。参数为 false（取消蓝图）时不做事。
        /// </summary>
        private void HandleBlueprintClosed(bool built)
        {
            if (built)
            {
                Recalculate();
            }
        }
    }
}
