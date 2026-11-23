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

        [Tooltip("覆盖值：大于 0 时覆盖「垃圾种类表」里的基础生命；0 = 跟随种类表（推荐）")]
        [SerializeField, Min(0f)] private float maxHealth;

        [Tooltip("覆盖值：大于 0 时覆盖「垃圾种类表」里的基础奖励；0 = 跟随种类表（推荐）")]
        [SerializeField, Min(0)] private int reward;

        [Tooltip("顺序组专用：勾选后作为中Boss出场。会放大体型并播报，但不会触发关卡最终Boss的倒计时/通关流程")]
        [SerializeField] private bool isMidBoss;

        [Tooltip("顺序组专用：出场体型倍数。1 = 普通大小；中Boss建议 1.3~1.4")]
        [SerializeField, Min(1f)] private float visualScale = 1f;

        /// <summary>矿石种类。</summary>
        public OreType Type => type;

        /// <summary>抽取权重。</summary>
        public float Weight => weight;

        /// <summary>生成后的最大生命值。</summary>
        public float MaxHealth => maxHealth;

        /// <summary>击碎奖励。</summary>
        public int Reward => reward;

        /// <summary>顺序组里是否作为中Boss出场。</summary>
        public bool IsMidBoss => isMidBoss;

        /// <summary>出场体型倍数，最小 1。</summary>
        public float VisualScale => Mathf.Max(1f, visualScale);

        /// <summary>
        /// 是否能参与抽取。权重非正的条目永远不会被抽到。
        /// 生命值是否有效要等生成时按「垃圾种类表」解析出来才知道（条目上的 maxHealth 只是覆盖值，
        /// 0 表示跟随种类表），所以这里不再用生命值做过滤，由 OreSpawner 在解析后兜底。
        /// </summary>
        public bool IsSelectable => weight > 0f;

        /// <summary>供编辑器向导生成默认数据用。</summary>
        public void Configure(OreType newType, float newWeight, float newMaxHealth, int newReward,
            bool newIsMidBoss = false, float newVisualScale = 1f)
        {
            type = newType;
            weight = newWeight;
            maxHealth = newMaxHealth;
            reward = newReward;
            isMidBoss = newIsMidBoss;
            visualScale = Mathf.Max(1f, newVisualScale);
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

        [Tooltip("勾选后按 entries 列表顺序生成，不随机、不按权重。用于关卡脚本化波次；" +
                 "只在关卡入口的 aggregateGroups 里使用时生效")]
        [SerializeField] private bool sequential;

        /// <summary>组内全部条目（含不可抽取的条目，供编辑器检查）。</summary>
        public IReadOnlyList<OreEntry> Entries => entries;

        /// <summary>是否按列表顺序生成。顺序组用于关卡波次脚本，不参与随机抽取。</summary>
        public bool IsSequential => sequential;

        /// <summary>顺序组再往后的波次数为 list 长度；随机组没有固定波次。</summary>
        public int EntryCount => entries != null ? entries.Count : 0;

        /// <summary>按序号取顺序组里的条目；越界或空表返回 null。</summary>
        public OreEntry GetEntry(int index)
        {
            return entries != null && index >= 0 && index < entries.Count ? entries[index] : null;
        }

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
