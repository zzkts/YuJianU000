using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yujian.Player
{
    /// <summary>
    /// 一条"建筑数 → 玩家等级"的阈值。建筑总数达到 BuildingCount 时，等级为 Level。
    /// </summary>
    [Serializable]
    public class BuildingLevelThreshold
    {
        [Tooltip("建筑总数达到该值时开始生效")]
        [SerializeField] private int buildingCount = 0;

        [Tooltip("生效后的玩家等级")]
        [SerializeField] private int level = 1;

        /// <summary>触发该等级所需的建筑总数。</summary>
        public int BuildingCount => buildingCount;

        /// <summary>对应的玩家等级。</summary>
        public int Level => level;

        /// <summary>供编辑器向导生成默认数据用。</summary>
        public void Configure(int newBuildingCount, int newLevel)
        {
            buildingCount = newBuildingCount;
            level = newLevel;
        }
    }

    /// <summary>
    /// 建筑数 → 玩家等级表。
    /// 职责：只做数值判定，不统计建筑、不监听建造事件——那些由 PlayerLevel 负责。
    /// 文档默认值：0 座 → 1 级，3 座 → 2 级，6 座 → 3 级。
    /// </summary>
    [CreateAssetMenu(menuName = "屿见/玩家/等级表", fileName = "PlayerLevelTable")]
    public class PlayerLevelTable : ScriptableObject
    {
        [Tooltip("按建筑总数升序填写。判定与顺序无关，取不超过当前建筑数的最大阈值")]
        [SerializeField] private List<BuildingLevelThreshold> thresholds = new List<BuildingLevelThreshold>();

        /// <summary>全部阈值配置。</summary>
        public IReadOnlyList<BuildingLevelThreshold> Thresholds => thresholds;

        /// <summary>
        /// 根据建筑总数计算玩家等级。
        /// 取"阈值建筑数 ≤ buildingCount"中建筑数最大的那一条；没有匹配时返回 1 并警告。
        /// </summary>
        public int Evaluate(int buildingCount)
        {
            if (buildingCount < 0)
            {
                Debug.LogWarning($"[PlayerLevelTable] 建筑总数不应为负数({buildingCount})，已按 0 处理。", this);
                buildingCount = 0;
            }

            int bestBuildingCount = int.MinValue;
            int resultLevel = 0;

            if (thresholds != null)
            {
                for (int i = 0; i < thresholds.Count; i++)
                {
                    BuildingLevelThreshold threshold = thresholds[i];

                    if (threshold == null)
                    {
                        continue;
                    }

                    if (threshold.Level <= 0)
                    {
                        Debug.LogWarning($"[PlayerLevelTable] 等级配置为非正数({threshold.Level})，该条已忽略。", this);
                        continue;
                    }

                    if (threshold.BuildingCount <= buildingCount && threshold.BuildingCount >= bestBuildingCount)
                    {
                        bestBuildingCount = threshold.BuildingCount;
                        resultLevel = threshold.Level;
                    }
                }
            }

            if (resultLevel <= 0)
            {
                Debug.LogWarning("[PlayerLevelTable] 没有找到匹配的等级阈值（至少需要一条建筑数 0 的配置），" +
                                 "本次按 1 级处理。请检查等级表资产。", this);
                return 1;
            }

            return resultLevel;
        }

        /// <summary>
        /// 查找「比当前建筑数更高」的最小建筑数阈值，供建筑进度 UI 显示 X/N。
        /// 没有更高阈值（已满级）时返回 false，两个输出参数均为 0。
        /// </summary>
        /// <param name="buildingCount">当前已建成建筑总数。</param>
        /// <param name="nextBuildingCount">下一个等级需要的建筑总数。</param>
        /// <param name="nextLevel">达到该建筑数后的等级。</param>
        public bool TryGetNextThreshold(int buildingCount, out int nextBuildingCount, out int nextLevel)
        {
            nextBuildingCount = 0;
            nextLevel = 0;

            if (buildingCount < 0)
            {
                buildingCount = 0;
            }

            bool found = false;
            int smallestGreater = int.MaxValue;

            if (thresholds != null)
            {
                for (int i = 0; i < thresholds.Count; i++)
                {
                    BuildingLevelThreshold threshold = thresholds[i];

                    if (threshold == null || threshold.Level <= 0)
                    {
                        continue;
                    }

                    if (threshold.BuildingCount <= buildingCount ||
                        threshold.BuildingCount >= smallestGreater)
                    {
                        continue;
                    }

                    smallestGreater = threshold.BuildingCount;
                    nextBuildingCount = threshold.BuildingCount;
                    nextLevel = threshold.Level;
                    found = true;
                }
            }

            return found;
        }

        private void OnValidate()
        {
            if (thresholds == null)
            {
                thresholds = new List<BuildingLevelThreshold>();
                return;
            }

            // 同一建筑数配了多个等级时，Evaluate 取循环里最后一条，结果依赖数组顺序，必须提醒
            HashSet<int> seen = new HashSet<int>();

            for (int i = 0; i < thresholds.Count; i++)
            {
                BuildingLevelThreshold threshold = thresholds[i];
                if (threshold == null)
                {
                    continue;
                }

                if (!seen.Add(threshold.BuildingCount))
                {
                    Debug.LogWarning($"[PlayerLevelTable] 建筑数 {threshold.BuildingCount} 配置了多次，" +
                                     "结果会取数组里靠后的一条。请删掉重复项。", this);
                }
            }
        }
    }
}
