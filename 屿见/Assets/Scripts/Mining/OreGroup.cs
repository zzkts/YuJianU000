using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 一条矿石定义。生成矿石时用它来配置场上唯一的那块 Ore。
    /// 所有数值都在 Inspector / ScriptableObject 里配置，不在代码里写死。
    /// </summary>
    [Serializable]
    public class OreEntry
    {
        [SerializeField] private OreType type = OreType.QingShi;

        [Tooltip("抽取权重，越大越容易被抽到。小于等于 0 的条目永远不会被抽到")]
        [SerializeField] private float weight = 1f;

        [Tooltip("生成后的最大生命值，必须大于 0")]
        [SerializeField] private float maxHealth = 10f;

        [Tooltip("击碎后发放给玩家的金币")]
        [SerializeField] private int reward = 10;

        /// <summary>矿石种类。</summary>
        public OreType Type => type;

        /// <summary>抽取权重。</summary>
        public float Weight => weight;

        /// <summary>生成后的最大生命值。</summary>
        public float MaxHealth => maxHealth;

        /// <summary>击碎奖励。</summary>
        public int Reward => reward;

        /// <summary>是否能参与抽取。权重非正或生命值非正的条目会被跳过，避免生成出零血矿石。</summary>
        public bool IsSelectable => weight > 0f && maxHealth > 0f;

        /// <summary>供编辑器向导生成默认数据用。</summary>
        public void Configure(OreType newType, float newWeight, float newMaxHealth, int newReward)
        {
            type = newType;
            weight = newWeight;
            maxHealth = newMaxHealth;
            reward = newReward;
        }
    }

    /// <summary>
    /// 矿石组：一个等级下的一组矿石定义。
    /// 职责：只保存数据与查询能力，不负责抽取、生成、计时——那些由 OreSpawner 负责。
    /// </summary>
    [CreateAssetMenu(menuName = "屿见/挖矿/矿石组", fileName = "OreGroup")]
    public class OreGroup : ScriptableObject
    {
        [Tooltip("组内可抽取的矿石条目。按权重随机，权重越大越常见")]
        [SerializeField] private List<OreEntry> entries = new List<OreEntry>();

        /// <summary>组内全部条目（含不可抽取的条目，供编辑器检查）。</summary>
        public IReadOnlyList<OreEntry> Entries => entries;

        /// <summary>组内是否还有可抽取的条目。整组都不可抽取时会被 Spawner 跳过。</summary>
        public bool HasSelectableEntry
        {
            get
            {
                if (entries == null)
                {
                    return false;
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    OreEntry entry = entries[i];
                    if (entry != null && entry.IsSelectable)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private void OnValidate()
        {
            if (entries == null)
            {
                entries = new List<OreEntry>();
            }
        }
    }
}
