using UnityEngine;

namespace Yujian.Building
{
    /// <summary>
    /// 汇总场上所有建筑的「全局」强化加成。
    ///
    /// 为什么单独一个类：伤害类的全局加成已经由 <see cref="Yujian.Building.Effects.BuildingDamageBonus"/>
    /// 汇总进 PlayerStats，但攻击速度这类数值没有那个汇总入口。
    /// 建筑效果如果各自扫一遍场景，很容易出现「这座建筑算了、那座没算」的分叉，
    /// 所以统一从这里取。
    ///
    /// 只统计 <see cref="BuildingEnhancementScope.Global"/>：个体加成由建筑读自己的
    /// PlacedBuildingInfo，不要在这里重复计入。
    /// </summary>
    public static class BuildingEnhancementTotals
    {
        /// <summary>
        /// 场上所有建筑对某一项数值的全局加成之和。
        /// 可叠加的选项选了 N 层就累加 N 次（由 PlacedBuildingInfo.GetEnhancementBonus 负责）。
        /// </summary>
        public static float SumGlobal(BuildingManager manager, BuildingEnhancementStat stat)
        {
            if (manager == null)
            {
                return 0f;
            }

            System.Collections.Generic.IReadOnlyList<GameObject> placed = manager.PlacedBuildings;
            float total = 0f;

            for (int i = 0; i < placed.Count; i++)
            {
                GameObject instance = placed[i];

                if (instance == null)
                {
                    continue;
                }

                PlacedBuildingInfo info = instance.GetComponent<PlacedBuildingInfo>();

                if (info == null)
                {
                    continue;
                }

                total += info.GetEnhancementBonus(BuildingEnhancementScope.Global, stat);
            }

            return total;
        }
    }
}