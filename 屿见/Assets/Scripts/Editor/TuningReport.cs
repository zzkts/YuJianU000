using System.Text;
using UnityEditor;
using UnityEngine;
using Yujian.Mining;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 数值自检（2026-11-11）：按 ×5 / 6 档曲线打印每层击杀点击数、收入与工具升级占比。
    ///
    /// 设计目标（见《屿见-核心循环数值设计-v1》）：
    ///   1. 每只垃圾恒定 5 次点击（加权平均口径），暴击另算；
    ///   2. 工具升级费 = 该层收入 28% ±1%；
    ///   3. 第 k 层用第 k 级工具（6 档一一对应）。
    ///
    /// 入口：菜单栏 → 屿见 → 数值 → 打印各层击杀次数
    /// 垃圾种类表挂在 OreLevelTable 资产上（不是场景），所以这里只读资产，不需要打开场景。
    /// </summary>
    public static class TuningReport
    {
        private const string OreLevelTablePath = "Assets/Data/Mining/OreLevelTable.asset";
        private const string ToolPath = "Assets/Data/Mining/Tools/MiningTool_石头.asset";

        /// <summary>每层波次数（5 次点击/只、2 点击/秒、spawnDelay 0.2 秒、5 分钟含 BOSS）。</summary>
        private const int WavesPerTier = 104;

        private const float TargetHitsPerOre = 5f;
        private const float TargetHitsTolerance = 0.5f;

        [MenuItem("屿见/数值/打印各层击杀次数", false, 401)]
        public static void PrintHitTable()
        {
            OreLevelTable levelTable = AssetDatabase.LoadAssetAtPath<OreLevelTable>(OreLevelTablePath);
            MiningToolData tool = AssetDatabase.LoadAssetAtPath<MiningToolData>(ToolPath);

            if (levelTable == null || tool == null)
            {
                Debug.LogError("[数值自检] 找不到矿石等级表或挖矿工具资产，无法计算。");
                return;
            }

            OreTypeTable typeTable = levelTable.TypeTable;

            if (typeTable == null)
            {
                Debug.LogWarning("[数值自检] 矿石等级表上没有接垃圾种类表，" +
                                 "下面按条目上的覆盖值计算（新数据里是 0，结果会全 0）。");
            }

            StringBuilder report = new StringBuilder();
            report.AppendLine("[数值自检] ×5 / 6 档曲线：每层击杀点击数、收入、工具升级占比");
            report.AppendLine($"工具曲线：{DescribeToolCurve(tool)}（满级 {tool.MaxLevel}）");
            report.AppendLine($"目标：加权平均每只 {TargetHitsPerOre:0.#} 次点击，升级费 = 该层收入 28% ±1%");

            int maxLevel = Mathf.Max(1, levelTable.MaxLevel);
            bool warned = false;

            for (int level = 1; level <= maxLevel; level++)
            {
                if (!levelTable.TryGetLevel(level, out OreLevelDefinition definition))
                {
                    report.AppendLine($"第 {level} 层：等级表里没有配置");
                    continue;
                }

                int toolLevel = level;
                float attack = tool.GetAttackAtLevel(toolLevel);

                report.AppendLine();
                report.AppendLine($"--- 第 {level} 层（工具 {toolLevel} 级，攻击 {attack:0.##}）---");

                float weightedHealth = 0f;
                float totalWeight = 0f;

                for (int g = 0; g < definition.Groups.Count; g++)
                {
                    OreGroup group = definition.Groups[g];

                    if (group == null)
                    {
                        report.AppendLine("  （组为空引用）");
                        continue;
                    }

                    for (int i = 0; i < group.Entries.Count; i++)
                    {
                        OreEntry entry = group.Entries[i];

                        if (entry == null || !entry.IsSelectable)
                        {
                            continue;
                        }

                        float health;
                        int reward;

                        if (typeTable != null)
                        {
                            typeTable.Resolve(entry, level, levelTable, out _, out _, out health, out reward);
                        }
                        else
                        {
                            health = entry.MaxHealth * levelTable.GetHealthMultiplier(level);
                            reward = levelTable.GetRewardAtTier(entry.Reward, level);
                        }

                        float hits = health / Mathf.Max(0.0001f, attack);

                        weightedHealth += health * entry.Weight;
                        totalWeight += entry.Weight;

                        report.AppendLine($"  {OreTypeInfo.DisplayName(entry.Type)} 权重 {entry.Weight:0.##} " +
                                          $"HP {health:0.#} 奖励 {reward} 击杀 {hits:0.##} 次");
                    }
                }

                if (totalWeight <= 0f)
                {
                    report.AppendLine("  该层没有任何可抽取条目");
                    continue;
                }

                float averageHealth = weightedHealth / totalWeight;
                float averageHits = averageHealth / Mathf.Max(0.0001f, attack);
                float income = averageHealth * WavesPerTier;
                int upgradeCost = tool.GetUpgradeCost(toolLevel);
                bool hitsOff = Mathf.Abs(averageHits - TargetHitsPerOre) > TargetHitsTolerance;

                report.AppendLine($"  加权平均：HP {averageHealth:0.#}，每只 {averageHits:0.##} 次" +
                                  (hitsOff ? $"  ← 偏离 {TargetHitsPerOre:0.#} 次" : string.Empty) +
                                  $"，该层收入约 {income:0}");

                if (upgradeCost > 0 && income > 0f)
                {
                    float share = upgradeCost / income;
                    bool shareOff = share < 0.27f || share > 0.29f;
                    warned |= hitsOff || shareOff;

                    report.AppendLine($"  升级费 {upgradeCost}（占该层收入 {share * 100f:0.#}%）" +
                                      (shareOff ? "  ← 目标 28% ±1%" : string.Empty));
                }
                else
                {
                    warned |= hitsOff;
                    report.AppendLine("  已满级，无升级费");
                }
            }

            if (warned)
            {
                Debug.LogWarning(report.ToString());
            }
            else
            {
                Debug.Log(report.ToString());
            }
        }

        private static string DescribeToolCurve(MiningToolData tool)
        {
            StringBuilder builder = new StringBuilder();

            for (int level = 1; level <= tool.MaxLevel; level++)
            {
                if (level > 1)
                {
                    builder.Append(" / ");
                }

                builder.Append(tool.GetAttackAtLevel(level).ToString("0.##"));
            }

            builder.Append("；升级费 ");
            for (int level = 1; level < tool.MaxLevel; level++)
            {
                if (level > 1)
                {
                    builder.Append(" / ");
                }

                builder.Append(tool.GetUpgradeCost(level));
            }

            return builder.ToString();
        }
    }
}
