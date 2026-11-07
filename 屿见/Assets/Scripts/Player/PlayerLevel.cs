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
        private int highestLevel;
        private bool leveledUpSinceLastCheck;

        /// <summary>
        /// 当前玩家等级。Start 之前为 0。
        /// 策划案 4.2（2026-11-06）：等级只升不降，所以它始终等于历史最高等级。
        /// </summary>
        public int CurrentLevel => currentLevel;

        /// <summary>历史达到的最高玩家等级，供存档与进度 UI 读取。</summary>
        public int HighestLevel => highestLevel;

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

        /// <summary>按指定建筑总数查等级，不改变当前状态。</summary>
        public int GetLevelForBuildingCount(int buildingCount)
        {
            return levelTable != null ? levelTable.Evaluate(buildingCount) : 0;
        }

        /// <summary>
        /// 取出「上一次 Recalculate 是否让等级提升」并清零。
        /// 建筑进度 UI 用它决定要不要喷火花柱：拆除建筑后等级不回退，
        /// 不能再拿「当前建筑数 - 1」反查上一级来判断，否则会误判成升级。
        /// </summary>
        public bool ConsumeLevelUpFlag()
        {
            bool result = leveledUpSinceLastCheck;
            leveledUpSinceLastCheck = false;
            return result;
        }

        /// <summary>
        /// 读档时恢复历史最高等级。只升不降，不广播 OnLevelChanged
        /// （读档期间矿石等系统已按存档恢复，不需要再触发一次初始化）。
        /// </summary>
        public void RestoreHighestLevel(int level)
        {
            if (level <= 0)
            {
                return;
            }

            highestLevel = Mathf.Max(highestLevel, level);
            currentLevel = Mathf.Max(currentLevel, highestLevel);
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
        /// 策划案 4.2：等级只升不降，建筑拆除后保留历史最高等级。
        /// </summary>
        public void Recalculate()
        {
            if (buildingManager == null || levelTable == null)
            {
                return;
            }

            int buildingCount = buildingManager.PlacedBuildings.Count;
            int newLevel = levelTable.Evaluate(buildingCount);

            if (newLevel > highestLevel)
            {
                highestLevel = newLevel;
            }

            if (highestLevel == currentLevel)
            {
                leveledUpSinceLastCheck = false;
                return;
            }

            int previousLevel = currentLevel;
            currentLevel = highestLevel;
            leveledUpSinceLastCheck = currentLevel > previousLevel;

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
