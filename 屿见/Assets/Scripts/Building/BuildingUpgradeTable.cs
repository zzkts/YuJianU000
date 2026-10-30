using UnityEngine;

namespace Yujian.Building
{
    /// <summary>
    /// 建筑升级表（策划案 3.4 的「升级表」，例如 LevelUP_01）。
    ///
    /// 只回答两件事：
    ///   1. 等级 L 时建筑效果是基础值的多少倍（<see cref="GetEffectMultiplier"/>）；
    ///   2. 从等级 L 升到 L+1 要花多少金币（<see cref="GetUpgradeCost"/>）。
    ///
    /// 数值规则（2026-10-29 设定，全部是 Inspector 字段，改资产即可调）：
    ///   · 每级效果线性 +valueGrowthPerLevel（默认 8% 基础值）；
    ///   · 每到 interval（10）级，效果额外乘 intervalValueMultiplier，且**升到这一级**的费用乘
    ///     intervalCostMultiplier——效果与费用都落在同一个里程碑等级上；
    ///   · 每到 majorInterval（50）级，效果额外乘 majorValueMultiplier，费用乘 majorCostMultiplier；
    ///   · 单级费用 = baseCost × costGrowthPerLevel^(当前等级-1)，四舍五入。
    ///
    /// 不修改任何运行时状态，不读金币、不读建筑实例——纯公式表。
    /// 升级扣钱与等级写入在 <see cref="BuildingUpgrade"/>。
    /// </summary>
    [CreateAssetMenu(fileName = "LevelUP_", menuName = "屿见/建筑升级表", order = 2)]
    public class BuildingUpgradeTable : ScriptableObject
    {
        [Header("标识")]
        [Tooltip("配置表里的「升级表名称」，例如 LevelUP_01")]
        [SerializeField] private string tableId = "LevelUP_01";

        [Header("等级")]
        [Tooltip("等级上限。升到上限后升级按钮显示「已满级」")]
        [SerializeField, Min(1)] private int maxLevel = 50;

        [Header("效果成长")]
        [Tooltip("每升一级，效果在基础值上线性增加多少。0.08 = 每级 +8% 基础值")]
        [SerializeField, Min(0f)] private float valueGrowthPerLevel = 0.08f;

        [Tooltip("小间隔的级数。每隔这么多级来一次大提升，同时这一级的升级费更贵")]
        [SerializeField, Min(1)] private int interval = 10;

        [Tooltip("到达小间隔时，效果额外乘的倍数")]
        [SerializeField, Min(1f)] private float intervalValueMultiplier = 1.15f;

        [Tooltip("大间隔的级数。每隔这么多级来一次更大提升，升级费也更高")]
        [SerializeField, Min(1)] private int majorInterval = 50;

        [Tooltip("到达大间隔时，效果额外乘的倍数")]
        [SerializeField, Min(1f)] private float majorValueMultiplier = 1.5f;

        [Header("升级费用")]
        [Tooltip("1 → 2 级的基础费用（金币）")]
        [SerializeField, Min(0)] private int baseCost = 15;

        [Tooltip("每升一级费用的几何增长率。1.09 = 每级费用 ×1.09")]
        [SerializeField, Min(1f)] private float costGrowthPerLevel = 1.09f;

        [Tooltip("升到小间隔那一级时，费用额外乘的倍数")]
        [SerializeField, Min(1f)] private float intervalCostMultiplier = 2f;

        [Tooltip("升到大间隔那一级时，费用额外乘的倍数（优先于小间隔倍数）")]
        [SerializeField, Min(1f)] private float majorCostMultiplier = 3f;

        /// <summary>配置表里的升级表名称。</summary>
        public string TableId => string.IsNullOrEmpty(tableId) ? name : tableId;

        /// <summary>等级上限，至少 1。</summary>
        public int MaxLevel => Mathf.Max(1, maxLevel);

        /// <summary>小间隔级数（10）。</summary>
        public int Interval => Mathf.Max(1, interval);

        /// <summary>大间隔级数（50）。</summary>
        public int MajorInterval => Mathf.Max(1, majorInterval);

        /// <summary>
        /// 等级 <paramref name="level"/> 时建筑效果相对基础值的倍数。
        /// 1 级恒为 1（不改变现有手感）。
        /// </summary>
        public float GetEffectMultiplier(int level)
        {
            int clamped = Mathf.Clamp(level, 1, MaxLevel);
            double value = 1.0 + valueGrowthPerLevel * (clamped - 1);

            // 用 level / interval 而不是 (level-1) / interval：
            // 大提升必须和「升到这一级」的那笔贵费用落在同一级
            value *= System.Math.Pow(intervalValueMultiplier, clamped / Interval);
            value *= System.Math.Pow(majorValueMultiplier, clamped / MajorInterval);

            return (float)value;
        }

        /// <summary>
        /// 从 <paramref name="currentLevel"/> 升到下一级要花的金币。已满级时返回 0。
        /// </summary>
        public int GetUpgradeCost(int currentLevel)
        {
            if (currentLevel >= MaxLevel)
            {
                return 0;
            }

            return GetCostToReachLevel(Mathf.Max(1, currentLevel) + 1, currentLevel);
        }

        /// <summary>
        /// 从 <paramref name="currentLevel"/> 升到 <paramref name="targetLevel"/> 的总费用。
        /// 区间非法或已满级时返回 0。
        /// </summary>
        public int GetTotalCost(int currentLevel, int targetLevel)
        {
            int from = Mathf.Clamp(currentLevel, 1, MaxLevel);
            int to = Mathf.Clamp(targetLevel, from, MaxLevel);
            int total = 0;

            for (int level = from; level < to; level++)
            {
                total += GetUpgradeCost(level);
            }

            return total;
        }

        /// <summary>
        /// 「Max」模式一次要升到哪一级：**下一个间隔**（interval 的整数倍，严格大于当前等级），
        /// 不超过等级上限。例如 7 级 → 10 级，10 级 → 20 级，满级时返回上限。
        ///
        /// 用户 2026-10-29 拍板：按策划案字面「升级到等级间隔处」，不是一次点满。
        /// </summary>
        public int GetMaxModeTargetLevel(int currentLevel)
        {
            int clamped = Mathf.Clamp(currentLevel, 1, MaxLevel);

            if (clamped >= MaxLevel)
            {
                return MaxLevel;
            }

            int step = Interval;
            int boundary = (clamped / step + 1) * step;
            return Mathf.Min(boundary, MaxLevel);
        }

        /// <summary>
        /// 从 <paramref name="currentLevel"/> 起，金币最多够连升几级（不超过 <paramref name="targetLevel"/>）。
        /// 供升级按钮显示「可升级次数」。
        /// </summary>
        public int GetAffordableLevelCount(int currentLevel, int targetLevel, int money)
        {
            int from = Mathf.Clamp(currentLevel, 1, MaxLevel);
            int to = Mathf.Clamp(targetLevel, from, MaxLevel);
            int remaining = money;
            int count = 0;

            for (int level = from; level < to; level++)
            {
                int cost = GetUpgradeCost(level);

                if (cost > remaining)
                {
                    break;
                }

                remaining -= cost;
                count++;
            }

            return count;
        }

        /// <summary>升到某一级的单步费用：base × growth^(该级-2)，再按间隔乘倍数。</summary>
        private int GetCostToReachLevel(int targetLevel, int currentLevel)
        {
            // 费用按「当前等级」计算：1 → 2 用基础价，2 → 3 乘一次成长率
            double cost = baseCost * System.Math.Pow(costGrowthPerLevel, Mathf.Max(0, currentLevel - 1));

            // 大间隔优先：50 同时也是 10 的倍数，此时只乘大间隔倍数
            if (targetLevel % MajorInterval == 0)
            {
                cost *= majorCostMultiplier;
            }
            else if (targetLevel % Interval == 0)
            {
                cost *= intervalCostMultiplier;
            }

            return Mathf.Max(1, Mathf.RoundToInt((float)cost));
        }

        private void OnValidate()
        {
            maxLevel = Mathf.Max(1, maxLevel);
            interval = Mathf.Max(1, interval);
            majorInterval = Mathf.Max(1, majorInterval);

            if (majorInterval < interval)
            {
                Debug.LogWarning($"[BuildingUpgradeTable]「{name}」的大间隔 {majorInterval} 小于小间隔 {interval}，" +
                                 "50 级的大提升会比 10 级先到，配置可能写反了。", this);
            }
        }
    }
}
