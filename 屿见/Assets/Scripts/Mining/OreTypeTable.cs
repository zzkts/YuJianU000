using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 一种垃圾的基础数值。全表唯一来源：矿石组里不再反复填同一套生命 / 奖励。
    /// </summary>
    [Serializable]
    public class OreTypeStats
    {
        [SerializeField] private OreType type = OreType.QingShi;

        [Tooltip("第 1 层的基础生命值。实际生命 = 基础生命 × 当前矿层倍数")]
        [SerializeField, Min(0f)] private float baseHealth = 10f;

        [Tooltip("第 1 层的基础奖励金币。实际奖励 = 基础奖励 × 当前矿层倍数（四舍五入）")]
        [SerializeField, Min(0)] private int baseReward = 10;

        [Tooltip("这一种垃圾的生命成长倍数。0 = 跟随矿石等级表的全局成长（现在 ×5 / 层）")]
        [SerializeField, Min(0f)] private float healthGrowthPerTier;

        [Tooltip("这一种垃圾的奖励成长倍数。0 = 跟随矿石等级表的全局成长（现在 ×5 / 层）")]
        [SerializeField, Min(0f)] private float rewardGrowthPerTier;

        public OreType Type => type;
        public float BaseHealth => Mathf.Max(0f, baseHealth);
        public int BaseReward => Mathf.Max(0, baseReward);

        /// <summary>生命成长倍数；<=1 表示没有单独配置，调用方改用全局成长。</summary>
        public float HealthGrowthPerTier => healthGrowthPerTier;

        /// <summary>奖励成长倍数；<=1 表示没有单独配置，调用方改用全局成长。</summary>
        public float RewardGrowthPerTier => rewardGrowthPerTier;

        /// <summary>供编辑器向导生成默认数据用。</summary>
        public void Configure(OreType newType, float newBaseHealth, int newBaseReward,
            float newHealthGrowth = 0f, float newRewardGrowth = 0f)
        {
            type = newType;
            baseHealth = Mathf.Max(0f, newBaseHealth);
            baseReward = Mathf.Max(0, newBaseReward);
            healthGrowthPerTier = Mathf.Max(0f, newHealthGrowth);
            rewardGrowthPerTier = Mathf.Max(0f, newRewardGrowth);
        }
    }

    /// <summary>
    /// 垃圾种类表：每种垃圾只写一次基础生命 / 基础奖励，
    /// 实际数值 = 基础值 × 成长^（矿层-1）。加新矿层时组里只需要挑权重，不用再填生命。
    ///
    /// 与 OreEntry 的关系：条目上的 maxHealth / reward 变成「覆盖值」，
    /// 大于 0 时压过本表，等于 0 时跟随本表。正常情况下保持 0。
    /// </summary>
    [CreateAssetMenu(menuName = "屿见/挖矿/垃圾种类表", fileName = "OreTypeTable", order = 31)]
    public class OreTypeTable : ScriptableObject
    {
        [Tooltip("每种垃圾一条。同一种垃圾只能出现一次")]
        [SerializeField] private List<OreTypeStats> types = new List<OreTypeStats>();

        public IReadOnlyList<OreTypeStats> Types => types;

        /// <summary>按种类查配置。表里没配这种垃圾时返回 false。</summary>
        public bool TryGetStats(OreType type, out OreTypeStats stats)
        {
            stats = null;

            if (types == null)
            {
                return false;
            }

            for (int i = 0; i < types.Count; i++)
            {
                OreTypeStats candidate = types[i];
                if (candidate != null && candidate.Type == type)
                {
                    stats = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 算出某条矿石定义在指定矿层的实际生命与奖励。
        /// 条目上的值大于 0 时作为基准值，否则用本表的基础值；
        /// 成长倍数优先用本表这类垃圾的单独配置，没配就用等级表的全局成长。
        /// </summary>
        public void Resolve(OreEntry entry, int tier, OreLevelTable levelTable,
            out float baseHealth, out int baseReward, out float health, out int reward)
        {
            int safeTier = Mathf.Max(1, tier);

            OreTypeStats stats = null;
            if (entry != null)
            {
                TryGetStats(entry.Type, out stats);
            }

            float entryHealth = entry != null ? entry.MaxHealth : 0f;
            int entryReward = entry != null ? entry.Reward : 0;

            baseHealth = entryHealth > 0f ? entryHealth : (stats != null ? stats.BaseHealth : 0f);
            baseReward = entryReward > 0 ? entryReward : (stats != null ? stats.BaseReward : 0);

            float healthGrowth = stats != null ? stats.HealthGrowthPerTier : 0f;
            float rewardGrowth = stats != null ? stats.RewardGrowthPerTier : 0f;

            if (healthGrowth > 1.0001f)
            {
                health = baseHealth * Mathf.Pow(healthGrowth, safeTier - 1);
            }
            else
            {
                health = baseHealth * levelTable.GetHealthMultiplier(safeTier);
            }

            if (rewardGrowth > 1.0001f)
            {
                reward = Mathf.RoundToInt(baseReward * Mathf.Pow(rewardGrowth, safeTier - 1));
            }
            else
            {
                reward = levelTable.GetRewardAtTier(baseReward, safeTier);
            }
        }

        /// <summary>供编辑器向导生成默认数据用。</summary>
        public void Configure(IEnumerable<OreTypeStats> newStats)
        {
            types = new List<OreTypeStats>();

            if (newStats == null)
            {
                return;
            }

            foreach (OreTypeStats stats in newStats)
            {
                if (stats != null)
                {
                    types.Add(stats);
                }
            }
        }

        private void OnValidate()
        {
            if (types == null)
            {
                types = new List<OreTypeStats>();
                return;
            }

            // 同一种垃圾配两条会让 TryGetStats 只认第一条，必须在编辑器里就喊出来
            HashSet<OreType> seen = new HashSet<OreType>();

            for (int i = 0; i < types.Count; i++)
            {
                OreTypeStats stats = types[i];
                if (stats == null)
                {
                    continue;
                }

                if (!seen.Add(stats.Type))
                {
                    Debug.LogWarning($"[OreTypeTable] 垃圾种类 {OreTypeInfo.DisplayName(stats.Type)} " +
                                     "配置了多次，只会使用第一条。", this);
                }

                if (stats.BaseHealth <= 0f)
                {
                    Debug.LogWarning($"[OreTypeTable] 垃圾种类 {OreTypeInfo.DisplayName(stats.Type)} " +
                                     "的基础生命值不大于 0，生成出来会立刻碎掉。", this);
                }
            }
        }
    }
}
