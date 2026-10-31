using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 一个玩家等级对应的矿石组列表。
    /// </summary>
    [Serializable]
    public class OreLevelDefinition
    {
        [Tooltip("玩家等级。该等级生效时会从这里取矿石组")]
        [SerializeField] private int level = 1;

        [Tooltip("该等级下的矿石组。组间等概率抽取，组内条目按权重抽取")]
        [SerializeField] private List<OreGroup> groups = new List<OreGroup>();

        /// <summary>玩家等级。</summary>
        public int Level => level;

        /// <summary>该等级下的矿石组。</summary>
        public IReadOnlyList<OreGroup> Groups => groups;

        /// <summary>供编辑器向导生成默认数据用。</summary>
        public void Configure(int newLevel, List<OreGroup> newGroups)
        {
            level = newLevel;
            groups = newGroups ?? new List<OreGroup>();
        }
    }

    /// <summary>
    /// 矿石等级表：玩家等级 → 该等级可用的矿石组。
    /// 职责：只提供按等级查询，不记录"哪些组已经挖过"——挖矿进度是运行时状态，由 OreSpawner 持有。
    /// </summary>
    [CreateAssetMenu(menuName = "屿见/挖矿/矿石等级表", fileName = "OreLevelTable")]
    public class OreLevelTable : ScriptableObject
    {
        [Tooltip("按等级配置矿石组。等级不必连续，但不要重复")]
        [SerializeField] private List<OreLevelDefinition> levels = new List<OreLevelDefinition>();

        [Header("逐层成长（D7=A 几何曲线）")]
        [Tooltip("矿石生命值每升一层的倍数。2.2 表示第 2 层是第 1 层的 2.2 倍，第 3 层是 4.84 倍。" +
                 "矿石组里填的是第 1 层的基准值，生成时按等级乘上去")]
        [SerializeField, Min(1f)] private float healthGrowthPerTier = 2.2f;

        [Tooltip("矿石击碎奖励每升一层的倍数，规则同生命值")]
        [SerializeField, Min(1f)] private float rewardGrowthPerTier = 2.2f;

        /// <summary>全部等级配置。</summary>
        public IReadOnlyList<OreLevelDefinition> Levels => levels;

        /// <summary>生命值逐层成长倍数。</summary>
        public float HealthGrowthPerTier => Mathf.Max(1f, healthGrowthPerTier);

        /// <summary>奖励逐层成长倍数。</summary>
        public float RewardGrowthPerTier => Mathf.Max(1f, rewardGrowthPerTier);

        /// <summary>
        /// 指定矿层相对第 1 层的生命值倍数。
        /// 第 1 层返回 1，第 2 层返回 2.2，第 3 层返回 4.84……
        /// </summary>
        public float GetHealthMultiplier(int tier)
        {
            int safeTier = Mathf.Max(1, tier);
            return Mathf.Pow(HealthGrowthPerTier, safeTier - 1);
        }

        /// <summary>
        /// 把矿石组里填的「第 1 层基准奖励」换算成指定矿层的实际奖励（四舍五入）。
        /// </summary>
        public int GetRewardAtTier(int baseReward, int tier)
        {
            if (baseReward <= 0)
            {
                return Mathf.Max(0, baseReward);
            }

            double scaled = baseReward * (double)Mathf.Pow(RewardGrowthPerTier, Mathf.Max(1, tier) - 1);

            if (scaled >= int.MaxValue)
            {
                Debug.LogWarning($"[OreLevelTable] 第 {tier} 层的奖励超出 int 上限，已按 {int.MaxValue} 处理。", this);
                return int.MaxValue;
            }

            return Mathf.RoundToInt((float)scaled);
        }

        /// <summary>表里配置过的最高等级；空表返回 0。</summary>
        public int MaxLevel
        {
            get
            {
                if (levels == null)
                {
                    return 0;
                }

                int max = 0;

                for (int i = 0; i < levels.Count; i++)
                {
                    OreLevelDefinition definition = levels[i];
                    if (definition != null && definition.Level > max)
                    {
                        max = definition.Level;
                    }
                }

                return max;
            }
        }

        /// <summary>
        /// 按等级查配置。找不到返回 false，调用方必须处理，不能静默继续。
        /// </summary>
        public bool TryGetLevel(int level, out OreLevelDefinition definition)
        {
            definition = null;

            if (levels == null)
            {
                return false;
            }

            for (int i = 0; i < levels.Count; i++)
            {
                OreLevelDefinition candidate = levels[i];
                if (candidate != null && candidate.Level == level)
                {
                    definition = candidate;
                    return true;
                }
            }

            return false;
        }

        private void OnValidate()
        {
            if (levels == null)
            {
                levels = new List<OreLevelDefinition>();
                return;
            }

            // 重复等级会让 TryGetLevel 只认第一条，必须在编辑器里就喊出来
            HashSet<int> seen = new HashSet<int>();

            for (int i = 0; i < levels.Count; i++)
            {
                OreLevelDefinition definition = levels[i];
                if (definition == null)
                {
                    continue;
                }

                if (!seen.Add(definition.Level))
                {
                    Debug.LogWarning($"[OreLevelTable] 等级 {definition.Level} 配置了多次，只会使用第一条。", this);
                }
            }
        }
    }
}
