using System.Collections.Generic;
using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 一个矿石组：某种矿石在本等级里出现几次。
    /// count 是“配置的总次数”，运行时由 OreSpawner 记录剩余次数，不直接修改资产。
    /// </summary>
    [System.Serializable]
    public class OreGroup
    {
        [SerializeField] private OreDefinition ore;

        [Tooltip("本等级里这种矿石一共出现几次")]
        [SerializeField, Min(1)] private int count = 1;

        [Tooltip("随机挑选时的权重，越大越常出现")]
        [SerializeField, Min(0f)] private float weight = 1f;

        public OreDefinition Ore => ore;
        public int Count => Mathf.Max(1, count);
        public float Weight => Mathf.Max(0f, weight);

        public OreGroup(OreDefinition oreDefinition, int oreCount, float oreWeight)
        {
            ore = oreDefinition;
            count = Mathf.Max(1, oreCount);
            weight = Mathf.Max(0f, oreWeight);
        }

        public void Configure(OreDefinition oreDefinition, int oreCount, float oreWeight)
        {
            ore = oreDefinition;
            count = Mathf.Max(1, oreCount);
            weight = Mathf.Max(0f, oreWeight);
        }
    }

    /// <summary>
    /// 一个矿石等级的数据：包含多个矿石组，生成时按权重随机挑一个还有剩余的组。
    /// </summary>
    [CreateAssetMenu(fileName = "OreLevel_", menuName = "屿见/挖矿/矿石等级", order = 11)]
    public class OreLevelData : ScriptableObject
    {
        [SerializeField, Min(1)] private int level = 1;
        [SerializeField] private string displayName = "第 1 级";
        [SerializeField] private List<OreGroup> groups = new List<OreGroup>();

        public int Level => Mathf.Max(1, level);

        public string DisplayName =>
            string.IsNullOrWhiteSpace(displayName) ? $"第 {Level} 级" : displayName;

        public IReadOnlyList<OreGroup> Groups => groups;

        public void ConfigureForSetup(int newLevel, string newDisplayName, List<OreGroup> newGroups)
        {
            level = Mathf.Max(1, newLevel);
            displayName = newDisplayName;
            groups = newGroups ?? new List<OreGroup>();
        }
    }
}
