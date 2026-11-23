using UnityEngine;

namespace Yujian.Building.Recycling
{
    /// <summary>
    /// 回收站系统的数值配置（策划案「屿见-垃圾回收站系统」）。
    ///
    /// 只放公式需要的基础数值，运行时状态（队列里的垃圾、已产出金币）不在这里。
    /// 所有数值都可以在 Inspector 里调，代码不写死（项目规则 3）。
    ///
    /// 基础产能按「每座回收站」计：
    ///   · 1 座 = 每 <see cref="BaseCycleSeconds"/> 秒产出 <see cref="BaseCoinsPerCycle"/> 金币，
    ///     即 5 金币 / 5 秒 = 1 金币 / 秒；
    ///   · 垃圾消耗速度 = 金币速度 × <see cref="GarbagePerCoin"/>，即每座 5 垃圾 / 秒。
    /// 多座回收站的产能相加；等级 / 强化带来的加速由建筑升级表与强化选项另行提供倍率。
    /// 队列容量同时随回收站等级增长：单站容量 = <see cref="BaseQueueCapacityPerStation"/>
    /// + (等级 - 1) × <see cref="QueueCapacityPerLevel"/>。
    /// </summary>
    [CreateAssetMenu(fileName = "RecyclingSettings", menuName = "屿见/回收站设置", order = 4)]
    public class RecyclingSettings : ScriptableObject
    {
        [Header("队列")]
        [Tooltip("每座回收站 1 级时的基础队列容量。策划案基础值 200 / 座 [PH]")]
        [SerializeField, Min(1)] private int baseQueueCapacityPerStation = 200;

        [Tooltip("回收站每升 1 级，该站队列容量增加的数值。占位值，先给 +20 / 级，后续再调")]
        [SerializeField, Min(0)] private int queueCapacityPerLevel = 20;

        [Tooltip("回收站建造上限。两份文档冲突，2026-11-19 用户拍板按回收站设计稿的 3 座")]
        [SerializeField, Min(1)] private int maxStations = 3;

        [Header("转换")]
        [Tooltip("多少垃圾换 1 金币。策划案基础转换比 5:1")]
        [SerializeField, Min(1)] private int garbagePerCoin = 5;

        [Tooltip("一个基础产能周期产出的金币数。策划案「每 5 金币 5 秒」的前一半")]
        [SerializeField, Min(1)] private int baseCoinsPerCycle = 5;

        [Tooltip("一个基础产能周期的秒数。策划案「每 5 金币 5 秒」的后一半")]
        [SerializeField, Min(0.01f)] private float baseCycleSeconds = 5f;

        /// <summary>每座回收站 1 级时的基础队列容量。</summary>
        public int BaseQueueCapacityPerStation => baseQueueCapacityPerStation;

        /// <summary>回收站每升 1 级增加的队列容量。</summary>
        public int QueueCapacityPerLevel => queueCapacityPerLevel;

        /// <summary>
        /// 单座回收站在指定等级下的队列容量。
        /// 占位公式：基础容量 + (等级 - 1) × 每级增加量。
        /// </summary>
        public int GetQueueCapacityForLevel(int level)
        {
            int safeLevel = Mathf.Max(1, level);
            long capacity = baseQueueCapacityPerStation
                + (long)(safeLevel - 1) * queueCapacityPerLevel;

            return capacity >= int.MaxValue ? int.MaxValue : (int)capacity;
        }

        /// <summary>回收站建造上限（座）。</summary>
        public int MaxStations => maxStations;

        /// <summary>多少垃圾换 1 金币。</summary>
        public int GarbagePerCoin => garbagePerCoin;

        /// <summary>单座回收站一个周期产出的金币数。</summary>
        public int BaseCoinsPerCycle => baseCoinsPerCycle;

        /// <summary>单座回收站一个周期的秒数。</summary>
        public float BaseCycleSeconds => baseCycleSeconds;

        /// <summary>单座回收站的基础金币产出速度（金币 / 秒）。</summary>
        public float BaseCoinsPerSecond => baseCoinsPerCycle / baseCycleSeconds;

        /// <summary>单座回收站的基础垃圾消耗速度（垃圾 / 秒）。</summary>
        public float BaseGarbagePerSecond => BaseCoinsPerSecond * garbagePerCoin;

        /// <summary>
        /// 指定数量回收站（**全部按 1 级**）的总队列容量，保留给旧公式 / 测试使用。
        /// 运行时请用 <see cref="GetQueueCapacityForLevel"/> 按每座站的实际等级汇总。
        /// 数量为负按 0 处理；不按 <see cref="MaxStations"/> 截断，旧存档里多出来的站也要算容量。
        /// </summary>
        public int GetQueueCapacity(int stationCount)
        {
            long capacity = (long)Mathf.Max(0, stationCount) * baseQueueCapacityPerStation;
            return capacity >= int.MaxValue ? int.MaxValue : (int)capacity;
        }

        /// <summary>指定数量回收站的基础垃圾消耗速度（未计等级 / 强化倍率）。</summary>
        public float GetBaseGarbagePerSecond(int stationCount)
        {
            return Mathf.Max(0, stationCount) * BaseGarbagePerSecond;
        }

        private void OnValidate()
        {
            baseQueueCapacityPerStation = Mathf.Max(1, baseQueueCapacityPerStation);
            maxStations = Mathf.Max(1, maxStations);
            garbagePerCoin = Mathf.Max(1, garbagePerCoin);
            baseCoinsPerCycle = Mathf.Max(1, baseCoinsPerCycle);
            baseCycleSeconds = Mathf.Max(0.01f, baseCycleSeconds);
        }
    }
}
