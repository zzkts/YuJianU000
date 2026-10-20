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

        /// <summary>全部等级配置。</summary>
        public IReadOnlyList<OreLevelDefinition> Levels => levels;

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
