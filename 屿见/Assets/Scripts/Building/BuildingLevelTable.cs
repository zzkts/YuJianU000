using System.Collections.Generic;
using UnityEngine;

namespace Yujian.Building
{
    /// <summary>
    /// 一条“建筑数量 → 矿石等级”的规则。
    /// 普通建筑和特殊建筑分别计数，两个门槛都满足时这条规则才生效。
    /// </summary>
    [System.Serializable]
    public class BuildingLevelEntry
    {
        [Tooltip("至少要完成多少座建筑（一般建筑 + 特殊建筑）")]
        [SerializeField, Min(0)] private int buildingCount;

        [Tooltip("满足条件后使用的矿石等级编号，对应 OreLevelData.Level")]
        [SerializeField, Min(1)] private int oreLevel = 1;

        public int BuildingCount => Mathf.Max(0, buildingCount);
        public int OreLevel => Mathf.Max(1, oreLevel);

        public BuildingLevelEntry(int count, int level)
        {
            buildingCount = Mathf.Max(0, count);
            oreLevel = Mathf.Max(1, level);
        }

        public void Configure(int count, int level)
        {
            buildingCount = Mathf.Max(0, count);
            oreLevel = Mathf.Max(1, level);
        }

        public bool IsMet(int totalBuildingCount)
        {
            return totalBuildingCount >= BuildingCount;
        }
    }

    /// <summary>
    /// 建筑数量等级表。取所有满足条件的规则里矿石等级最高的一条。
    /// </summary>
    [CreateAssetMenu(fileName = "BuildingLevelTable", menuName = "屿见/建筑/建筑等级表", order = 20)]
    public class BuildingLevelTable : ScriptableObject
    {
        [SerializeField] private List<BuildingLevelEntry> entries = new List<BuildingLevelEntry>();

        public IReadOnlyList<BuildingLevelEntry> Entries => entries;

        public BuildingLevelEntry Evaluate(int totalBuildingCount)
        {
            BuildingLevelEntry best = null;

            if (entries == null)
            {
                return null;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                BuildingLevelEntry entry = entries[i];
                if (entry == null || !entry.IsMet(totalBuildingCount))
                {
                    continue;
                }

                if (best == null || entry.OreLevel >= best.OreLevel)
                {
                    best = entry;
                }
            }

            return best;
        }

        public int EvaluateOreLevel(int totalBuildingCount)
        {
            BuildingLevelEntry entry = Evaluate(totalBuildingCount);
            return entry != null ? entry.OreLevel : 1;
        }

        public void ConfigureForSetup(List<BuildingLevelEntry> newEntries)
        {
            entries = newEntries ?? new List<BuildingLevelEntry>();
        }
    }
}
