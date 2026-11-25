using System;
using UnityEngine;

namespace Yujian.Player
{
    /// <summary>垃圾背包在不同等级下的转移方式（策划案 2.1.6）。</summary>
    public enum GarbageBagTransferMode
    {
        /// <summary>长按背包才转移，松手停止（1~19 级）。</summary>
        LongPress = 0,

        /// <summary>点一下背包开始转移，再点一下停止（20~49 级）。</summary>
        SingleClick = 1,

        /// <summary>退出关卡时自动把包里的垃圾转走（50 级起）。</summary>
        AutoOnExit = 2,
    }

    /// <summary>
    /// 垃圾背包的等级表（策划案 2.1.6）。
    ///
    /// **本表全部是占位公式**，用户 2026-11-25 明确「垃圾有等级，公式占位你先做」：
    ///   · 容量 = BaseCapacity × CapacityGrowth^(等级-1)
    ///   · 升级花费 = UpgradeCostBase × UpgradeCostGrowth^(等级-1)
    ///
    /// 选 1.15 作为容量增长不是随手填的：第 4 关一趟产出 180,000 垃圾，
    /// 200 × 1.15^49 ≈ 188,000，正好让「50 级满级 = 一包吃得下最后一关」这条线成立。
    /// 如果以后把等级上限改掉，容量增长要跟着 190,000/200 的 等比分重新算，见开发进度第 139 节。
    ///
    /// 转移方式的三个分段也在这里配置，方便 Demo 期直接压到 1~4 / 5~9 / 10+。
    /// </summary>
    [CreateAssetMenu(menuName = "屿见/玩家/垃圾背包表", fileName = "GarbageBagTable", order = 40)]
    public class GarbageBagTable : ScriptableObject
    {
        [Header("容量（占位公式）")]
        [Tooltip("1 级的存储上限")]
        [SerializeField, Min(1)] private int baseCapacity = 200;

        [Tooltip("每升一级的容量倍数。1.15 表示 200 / 230 / 264 …；50 级约 188,000")]
        [SerializeField, Min(1f)] private float capacityGrowthPerLevel = 1.15f;

        [Tooltip("背包最高等级。到顶后不再提供升级")]
        [SerializeField, Min(1)] private int maxLevel = 50;

        [Header("升级花费（占位公式）")]
        [Tooltip("1 级升 2 级的花费（金币）")]
        [SerializeField, Min(0)] private int upgradeCostBase = 40;

        [Tooltip("每升一级的花费倍数")]
        [SerializeField, Min(1f)] private float upgradeCostGrowth = 1.18f;

        [Header("转移")]
        [Tooltip("每秒从背包转移到岛屿存储站的垃圾数量。基础 10 个/s")]
        [SerializeField, Min(1f)] private float transferPerSecond = 10f;

        [Tooltip("长按转移的最高等级。等级 > 该值后改为点击")]
        [SerializeField, Min(1)] private int longPressMaxLevel = 19;

        [Tooltip("点击转移的最高等级。等级 > 该值后改为退出关卡自动转移")]
        [SerializeField, Min(2)] private int clickMaxLevel = 49;

        /// <summary>1 级容量。</summary>
        public int BaseCapacity => Mathf.Max(1, baseCapacity);

        /// <summary>容量逐级倍数。</summary>
        public float CapacityGrowthPerLevel => Mathf.Max(1f, capacityGrowthPerLevel);

        /// <summary>最高等级。</summary>
        public int MaxLevel => Mathf.Max(1, maxLevel);

        /// <summary>1 级升 2 级的花费。</summary>
        public int UpgradeCostBase => Mathf.Max(0, upgradeCostBase);

        /// <summary>升级花费逐级倍数。</summary>
        public float UpgradeCostGrowth => Mathf.Max(1f, upgradeCostGrowth);

        /// <summary>每秒转移量。</summary>
        public float TransferPerSecond => Mathf.Max(1f, transferPerSecond);

        /// <summary>指定等级的存储上限。等级会被夹到 1..MaxLevel。</summary>
        public int GetCapacity(int level)
        {
            int safeLevel = Mathf.Clamp(level, 1, MaxLevel);

            double capacity = BaseCapacity * Math.Pow(CapacityGrowthPerLevel, safeLevel - 1);

            if (capacity >= int.MaxValue)
            {
                Debug.LogWarning($"[GarbageBagTable] 第 {safeLevel} 级的容量超出 int 上限，已按 {int.MaxValue} 处理。", this);
                return int.MaxValue;
            }

            return Mathf.Max(1, Mathf.RoundToInt((float)capacity));
        }

        /// <summary>
        /// 从 level 升到 level+1 的花费。已满级返回 0——调用方要先用 <see cref="MaxLevel"/> 判断。
        /// </summary>
        public int GetUpgradeCost(int level)
        {
            int safeLevel = Mathf.Clamp(level, 1, MaxLevel);

            if (safeLevel >= MaxLevel)
            {
                return 0;
            }

            double cost = UpgradeCostBase * Math.Pow(UpgradeCostGrowth, safeLevel - 1);

            if (cost >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return Mathf.Max(0, Mathf.RoundToInt((float)cost));
        }

        /// <summary>指定等级用哪种转移方式。</summary>
        public GarbageBagTransferMode GetTransferMode(int level)
        {
            int safeLevel = Mathf.Max(1, level);

            if (safeLevel <= longPressMaxLevel)
            {
                return GarbageBagTransferMode.LongPress;
            }

            if (safeLevel <= clickMaxLevel)
            {
                return GarbageBagTransferMode.SingleClick;
            }

            return GarbageBagTransferMode.AutoOnExit;
        }

        /// <summary>供编辑器向导填默认值用。</summary>
        public void ConfigureForSetup(int newBaseCapacity, float newCapacityGrowth, int newMaxLevel,
            int newUpgradeCostBase, float newUpgradeCostGrowth, float newTransferPerSecond,
            int newLongPressMaxLevel, int newClickMaxLevel)
        {
            baseCapacity = Mathf.Max(1, newBaseCapacity);
            capacityGrowthPerLevel = Mathf.Max(1f, newCapacityGrowth);
            maxLevel = Mathf.Max(1, newMaxLevel);
            upgradeCostBase = Mathf.Max(0, newUpgradeCostBase);
            upgradeCostGrowth = Mathf.Max(1f, newUpgradeCostGrowth);
            transferPerSecond = Mathf.Max(1f, newTransferPerSecond);
            longPressMaxLevel = Mathf.Max(1, newLongPressMaxLevel);
            clickMaxLevel = Mathf.Max(newLongPressMaxLevel + 1, newClickMaxLevel);
        }

        private void OnValidate()
        {
            maxLevel = Mathf.Max(1, maxLevel);
            longPressMaxLevel = Mathf.Clamp(longPressMaxLevel, 1, maxLevel);
            clickMaxLevel = Mathf.Clamp(clickMaxLevel, longPressMaxLevel, Mathf.Max(longPressMaxLevel, maxLevel));
            baseCapacity = Mathf.Max(1, baseCapacity);
            transferPerSecond = Mathf.Max(1f, transferPerSecond);

            if (upgradeCostGrowth < 1f)
            {
                Debug.LogWarning("[GarbageBagTable] 升级花费倍数小于 1，意味着越升越便宜。确认这是有意为之。", this);
            }
        }
    }
}
