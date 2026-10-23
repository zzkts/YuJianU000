using System.Collections.Generic;
using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 所有矿石等级的集合。等级按资产里的顺序推进。
    /// </summary>
    [CreateAssetMenu(fileName = "OreSpawnTable", menuName = "屿见/挖矿/矿石生成表", order = 12)]
    public class OreSpawnTable : ScriptableObject
    {
        [SerializeField] private List<OreLevelData> levels = new List<OreLevelData>();

        public IReadOnlyList<OreLevelData> Levels => levels;

        public int LevelCount => levels != null ? levels.Count : 0;

        public OreLevelData GetLevelByIndex(int index)
        {
            if (levels == null || index < 0 || index >= levels.Count)
            {
                return null;
            }

            return levels[index];
        }

        /// <summary>按 OreLevelData.Level 找下标；找不到返回 -1。</summary>
        public int FindIndexByLevelNumber(int levelNumber)
        {
            if (levels == null)
            {
                return -1;
            }

            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i] != null && levels[i].Level == levelNumber)
                {
                    return i;
                }
            }

            return -1;
        }

        public void ConfigureForSetup(List<OreLevelData> newLevels)
        {
            levels = newLevels ?? new List<OreLevelData>();
        }
    }
}
