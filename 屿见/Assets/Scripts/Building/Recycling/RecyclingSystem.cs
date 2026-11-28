using System;
using System.Collections.Generic;
using UnityEngine;
using Yujian.Building.Effects;
using Yujian.Core.Events;
using Yujian.Player;

namespace Yujian.Building.Recycling
{
    /// <summary>
    /// 回收站系统的运行时入口（策划案「垃圾回收站系统」）。
    ///
    /// 职责：
    ///   1. 持有**所有回收站共享**的那一条队列；
    ///   2. 按每座回收站的**等级**维护容量（单站容量 = 基础容量 +（等级 - 1）× 每级增量）；
    ///   3. 每帧按各站等级 / 强化汇总垃圾处理速度，推进转换；
    ///   4. 产出的金币直接交给 <see cref="PlayerCurrency"/>（金币只由回收站产出）；
    ///   5. 拆站时把缩容溢出的未转换垃圾退还 <see cref="PlayerGarbage"/>。
    ///
    /// 不做的事：不处理输入与 UI（第 3 步）、不写存档（第 4 步）、不认识具体模型。
    /// 站点通过 <see cref="RegisterStation"/> / <see cref="UnregisterStation"/> 接入，
    /// 具体由挂在回收站预制体上的 <see cref="RecyclingStationEffect"/> 调用。
    /// </summary>
    [DisallowMultipleComponent]
    public class RecyclingSystem : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("回收站数值配置。留空会明确报错，回收站不会转换任何垃圾")]
        [SerializeField] private RecyclingSettings settings;

        [Tooltip("玩家垃圾。拆站退料与投料都走它。留空时自动在场景里查找")]
        [SerializeField] private PlayerGarbage garbage;

        [Tooltip("玩家金币。回收站产出的金币直接加到这里。留空时自动在场景里查找")]
        [SerializeField] private PlayerCurrency currency;

        [Header("迁移标记")]
        [Tooltip("旧经济的场景数值（开局金币 / 照明升级价格）是否已经 ÷ 转换比。" +
                 "由回收站配置向导写入，不要手改；它必须存在场景里，场景没保存时下次向导会重新迁移")]
        [SerializeField] private bool legacyScenePricesConverted = false;

        [Header("调试")]
        [Tooltip("是否在 Console 打印建站 / 拆站 / 金币到账")]
        [SerializeField] private bool logRecyclingFlow = true;

        private readonly List<RecyclingStationEffect> stations = new List<RecyclingStationEffect>();
        private RecyclingQueue queue;

        /// <summary>
        /// 读档时先记下队列，等所有站点注册完再一次性恢复。
        /// 如果一开始就按「当前站数」恢复，第一座站注册时容量只有 200，会把 3 座站的 600 个垃圾截掉。
        /// </summary>
        private bool hasPendingRestore;
        private int pendingRestoreQueued;
        private double pendingRestoreProgress;

        private bool warnedAboutSettings;
        private bool warnedAboutGarbage;
        private bool warnedAboutCurrency;
        private bool garbageLookupDone;
        private bool currencyLookupDone;

        /// <summary>场景里当前生效的回收站系统。站点在场景里找不到引用时用它兜底。</summary>
        public static RecyclingSystem Instance { get; private set; }

        /// <summary>本帧产出的金币数（0 表示没有结算）。UI 用它做金币弹跳。</summary>
        public event Action<int> CoinsProduced;

        /// <summary>队列 / 容量 / 站数 / 转换进度发生变化时触发。UI 订阅后重新读状态。</summary>
        public event Action Changed;

        /// <summary>场上回收站数量。</summary>
        public int StationCount => stations.Count;

        /// <summary>共享队列是否已经建立（配置齐全）。</summary>
        public bool IsReady => queue != null;

        /// <summary>当前队列容量；没有站点或配置缺失时为 0。</summary>
        public int Capacity => queue != null ? queue.Capacity : 0;

        /// <summary>队列中尚未转换的垃圾数。</summary>
        public int QueuedGarbage => queue != null ? queue.QueuedGarbage : 0;

        /// <summary>还能再投入多少垃圾。</summary>
        public int FreeCapacity => queue != null ? queue.FreeCapacity : 0;

        /// <summary>队列是否已满。</summary>
        public bool IsFull => queue != null && queue.IsFull;

        /// <summary>当前批次金币进度（0 ≤ 值 &lt; 1），供站点进度条使用。</summary>
        public double CoinProgress => queue != null ? queue.CoinProgress : 0.0;

        /// <summary>累计产出金币（本次运行 / 本次读档以来）。</summary>
        public int TotalCoinsProduced => queue != null ? queue.TotalCoinsProduced : 0;

        /// <summary>累计转换掉的垃圾数。</summary>
        public int TotalGarbageConsumed => queue != null ? queue.TotalGarbageConsumed : 0;

        /// <summary>最近一次 <see cref="Advance"/> 使用的垃圾处理速度（垃圾 / 秒）。</summary>
        public float CurrentGarbagePerSecond { get; private set; }

        /// <summary>最近一次 <see cref="Advance"/> 使用的加权回收效率（1 = 基础）。</summary>
        public float CurrentEfficiency { get; private set; } = 1f;

        /// <summary>最近一次 <see cref="Advance"/> 实际转换掉的垃圾数。</summary>
        public int LastGarbageConverted { get; private set; }

        /// <summary>当前配置，供 UI / 调试读取，可能为 null。</summary>
        public RecyclingSettings Settings => settings;

        /// <summary>旧经济的场景数值是否已迁移（开局金币 / 照明价格）。</summary>
        public bool LegacyScenePricesConverted => legacyScenePricesConverted;

        /// <summary>向导调用：标记场景数值已按转换比迁移。</summary>
        public void MarkLegacyScenePricesConverted()
        {
            legacyScenePricesConverted = true;
        }

        /// <summary>已注册的站点，只读。</summary>
        public IReadOnlyList<RecyclingStationEffect> Stations => stations;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[RecyclingSystem] 场景中存在多个 RecyclingSystem，已保留最先初始化的实例，" +
                                 "本实例不会参与回收结算。", this);

                // 只 return 不够：Update 仍会跑，两套系统会把同一批垃圾各结算一次。
                enabled = false;
                return;
            }

            Instance = this;
            ResolveDependencies();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<BuildingUpgraded>(HandleBuildingUpgraded);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<BuildingUpgraded>(HandleBuildingUpgraded);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 回收站升级会提高自身队列容量；其它建筑升级对容量没影响，直接忽略。
        /// </summary>
        private void HandleBuildingUpgraded(BuildingUpgraded evt)
        {
            if (evt.Data == null || evt.Data.BuildingType != BuildingType.RecyclingStation)
            {
                return;
            }

            RecomputeCapacity();

            if (logRecyclingFlow)
            {
                Debug.Log($"[RecyclingSystem] 回收站升级到 {evt.NewLevel} 级，" +
                          $"重算队列容量：{Capacity}。", this);
            }
        }

        private void Update()
        {
            // 读档恢复：站点在各自 Start 里注册，全部注册完（第一次 Update 之前）再恢复队列
            ApplyPendingRestore();

            Advance(Time.deltaTime);
        }

        /// <summary>
        /// 推进一次转换。输入 / UI 之外的逻辑都在这里，测试可以直接调用。
        /// </summary>
        /// <param name="deltaSeconds">经过的秒数。</param>
        public void Advance(float deltaSeconds)
        {
            LastGarbageConverted = 0;

            if (deltaSeconds <= 0f)
            {
                return;
            }

            if (!EnsureQueue())
            {
                CurrentGarbagePerSecond = 0f;
                CurrentEfficiency = 1f;
                return;
            }

            if (stations.Count == 0)
            {
                CurrentGarbagePerSecond = 0f;
                CurrentEfficiency = 1f;
                return;
            }

            // 全局强化：所有回收站共享（Node_Huishouzhan_01「回收速度提升」）
            float globalSpeedBonus = 0f;

            for (int i = 0; i < stations.Count; i++)
            {
                RecyclingStationEffect station = stations[i];

                if (station != null)
                {
                    globalSpeedBonus += station.GetGlobalSpeedBonus();
                }
            }

            float globalSpeedMultiplier = Mathf.Max(0f, 1f + globalSpeedBonus);

            float weightSum = 0f;
            float weightedEfficiency = 0f;

            for (int i = 0; i < stations.Count; i++)
            {
                RecyclingStationEffect station = stations[i];

                if (station == null)
                {
                    continue;
                }

                float weight = station.GetLevelSpeedMultiplier();

                if (weight <= 0f)
                {
                    continue;
                }

                weightSum += weight;
                weightedEfficiency += weight * Mathf.Max(0.01f, station.GetEfficiencyMultiplier());
            }

            if (weightSum <= 0f)
            {
                CurrentGarbagePerSecond = 0f;
                CurrentEfficiency = 1f;
                return;
            }

            float garbagePerSecond = settings.BaseGarbagePerSecond * weightSum * globalSpeedMultiplier;
            float efficiency = weightedEfficiency / weightSum;

            CurrentGarbagePerSecond = garbagePerSecond;
            CurrentEfficiency = efficiency;

            int coins = queue.Tick(deltaSeconds, garbagePerSecond, efficiency, out int converted);
            LastGarbageConverted = converted;

            if (converted > 0 || coins > 0)
            {
                Changed?.Invoke();
            }

            if (coins <= 0)
            {
                return;
            }

            ResolveDependencies();

            if (currency == null)
            {
                if (!warnedAboutCurrency)
                {
                    warnedAboutCurrency = true;
                    Debug.LogError("[RecyclingSystem] 找不到 PlayerCurrency，回收站产出的金币无法入账。" +
                                   "请把玩家身上的 PlayerCurrency 拖到本组件的 Currency 字段。", this);
                }

                return;
            }

            currency.AddMoney(coins);
            CoinsProduced?.Invoke(coins);

            if (logRecyclingFlow)
            {
                Debug.Log($"[RecyclingSystem] {stations.Count} 座回收站完成一批：" +
                          $"转换 {converted} 垃圾，产出 {coins} 金币（当前 {currency.CurrentMoney}）。", this);
            }
        }

        /// <summary>
        /// 把玩家手里的垃圾一次性投入共享队列。
        /// 超出容量的部分留在玩家手里；返回实际投入的数量。
        /// </summary>
        public int DepositFromPlayer()
        {
            ResolveDependencies();

            if (!EnsureQueue())
            {
                return 0;
            }

            if (garbage == null)
            {
                if (!warnedAboutGarbage)
                {
                    warnedAboutGarbage = true;
                    Debug.LogError("[RecyclingSystem] 找不到 PlayerGarbage，无法投料。" +
                                   "请把玩家身上的 PlayerGarbage 拖到本组件的 Garbage 字段。", this);
                }

                return 0;
            }

            int available = garbage.CurrentGarbage;

            if (available <= 0)
            {
                return 0;
            }

            int accepted = Mathf.Min(available, queue.FreeCapacity);

            if (accepted <= 0)
            {
                return 0;
            }

            if (!garbage.TrySpendGarbage(accepted))
            {
                return 0;
            }

            int placed = queue.Deposit(accepted);

            // 单线程下容量不会在两步之间变化；真发生了就把差额退回，绝不吞垃圾
            if (placed < accepted)
            {
                garbage.AddToStorage(accepted - placed);
            }

            Changed?.Invoke();

            if (logRecyclingFlow)
            {
                Debug.Log($"[RecyclingSystem] 投料 {placed} 垃圾，队列 {queue.QueuedGarbage}/{queue.Capacity}。", this);
            }

            return placed;
        }

        /// <summary>站点初始化时调用。重复注册同一座站点会被忽略。</summary>
        public void RegisterStation(RecyclingStationEffect station)
        {
            if (station == null)
            {
                return;
            }

            if (stations.Contains(station))
            {
                Debug.LogWarning($"[RecyclingSystem] 回收站「{station.name}」已经注册过，本次忽略。" +
                                 "如果有两份效果组件挂在同一座建筑上，请删掉多余的那个。", this);
                return;
            }

            stations.Add(station);
            RecomputeCapacity();
            Changed?.Invoke();

            if (logRecyclingFlow)
            {
                Debug.Log($"[RecyclingSystem] 回收站「{station.name}」接入，当前 {stations.Count} 座，" +
                          $"队列容量 {Capacity}。", this);
            }
        }

        /// <summary>
        /// 站点销毁 / 拆除时调用。
        /// 容量按剩余站数缩减，超出的未转换垃圾退回玩家（拆最后一座时整条队列退回）。
        /// </summary>
        public void UnregisterStation(RecyclingStationEffect station)
        {
            if (station == null || !stations.Remove(station))
            {
                return;
            }

            RecomputeCapacity();
            Changed?.Invoke();

            if (logRecyclingFlow)
            {
                Debug.Log($"[RecyclingSystem] 回收站「{station.name}」移除，当前 {stations.Count} 座，" +
                          $"队列容量 {Capacity}。", this);
            }
        }

        /// <summary>
        /// 读档 / 重置用：清空队列、待恢复状态与累计值（站点注册表由建筑重建流程自然刷新）。
        /// </summary>
        public void ResetQueue()
        {
            hasPendingRestore = false;
            pendingRestoreQueued = 0;
            pendingRestoreProgress = 0.0;

            if (!EnsureQueue())
            {
                return;
            }

            queue.Reset();
            Changed?.Invoke();
        }

        /// <summary>
        /// 读档用：记下队列数据，等站点注册完再恢复。
        /// 不要在这里直接写队列：读档时建筑刚被重建，站点的 Start 还没跑，站数为 0。
        /// </summary>
        public void RestoreQueue(int queuedGarbage, double coinProgress)
        {
            hasPendingRestore = true;
            pendingRestoreQueued = Mathf.Max(0, queuedGarbage);
            pendingRestoreProgress = coinProgress;
        }

        /// <summary>
        /// 把待恢复的队列真正写入。第一次 Update 时站点的 Start 已全部跑完，容量是正确的。
        /// 如果存档里有队列但场上没有任何回收站（正常游玩不会出现），把垃圾退回玩家。
        /// </summary>
        /// <returns>是否已经没有待恢复数据。</returns>
        public bool ApplyPendingRestore()
        {
            if (!hasPendingRestore)
            {
                return true;
            }

            if (!EnsureQueue())
            {
                return false;
            }

            if (stations.Count > 0)
            {
                int capacity = ComputeCapacity();
                queue.SetCapacity(capacity);
                queue.RestoreState(pendingRestoreQueued, pendingRestoreProgress);
                hasPendingRestore = false;
                Changed?.Invoke();

                if (logRecyclingFlow)
                {
                    Debug.Log($"[RecyclingSystem] 已恢复回收队列：{queue.QueuedGarbage}/{queue.Capacity}，" +
                              $"批次进度 {queue.CoinProgress:0.###}。", this);
                }

                return true;
            }

            // 没有任何回收站：队列无处安放，退回玩家，避免读档吞掉垃圾
            ResolveDependencies();

            if (pendingRestoreQueued > 0)
            {
                if (garbage != null)
                {
                    garbage.AddToStorage(pendingRestoreQueued);
                }
                else
                {
                    Debug.LogError($"[RecyclingSystem] 存档里有 {pendingRestoreQueued} 个未转换垃圾，" +
                                   "但场上没有回收站、也找不到 PlayerGarbage，这些垃圾无法退回。", this);
                }

                if (logRecyclingFlow)
                {
                    Debug.Log($"[RecyclingSystem] 存档队列没有回收站承接，{pendingRestoreQueued} 个垃圾已退回玩家。", this);
                }
            }

            hasPendingRestore = false;
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// 汇总每座回收站当前等级对应的队列容量：
        /// 单站容量 = 基础容量 + (等级 - 1) × 每级增量（数值在 RecyclingSettings 里占位，可调）。
        /// </summary>
        private int ComputeCapacity()
        {
            if (settings == null || stations.Count == 0)
            {
                return 0;
            }

            long total = 0;

            for (int i = 0; i < stations.Count; i++)
            {
                RecyclingStationEffect station = stations[i];

                if (station == null)
                {
                    continue;
                }

                int level = station.OwnerInfo != null ? station.OwnerInfo.Level : 1;
                total += settings.GetQueueCapacityForLevel(level);

                if (total >= int.MaxValue)
                {
                    return int.MaxValue;
                }
            }

            return (int)total;
        }

        /// <summary>按各站等级重算容量，并把溢出的垃圾退回玩家。</summary>
        private void RecomputeCapacity()
        {
            if (!EnsureQueue())
            {
                return;
            }

            int previousCapacity = queue.Capacity;
            int newCapacity = ComputeCapacity();
            int overflow = queue.SetCapacity(newCapacity);

            // 升级提高容量时没有溢出，但 UI 仍需要刷新
            if (previousCapacity != newCapacity)
            {
                Changed?.Invoke();
            }

            if (overflow <= 0)
            {
                return;
            }

            ResolveDependencies();

            if (garbage != null)
            {
                garbage.AddToStorage(overflow);

                if (logRecyclingFlow)
                {
                    Debug.Log($"[RecyclingSystem] 拆站导致容量缩减，{overflow} 个未转换垃圾已退还给玩家。", this);
                }
            }
            else if (!warnedAboutGarbage)
            {
                warnedAboutGarbage = true;
                Debug.LogError($"[RecyclingSystem] 拆站后有 {overflow} 个垃圾溢出队列，但找不到 PlayerGarbage，" +
                               "这些垃圾已经丢失。请把玩家的 PlayerGarbage 接到本组件。", this);
            }
        }

        /// <summary>
        /// 确保共享队列已按当前各站等级建立。配置缺失时只报一次错，避免每帧刷屏。
        /// </summary>
        private bool EnsureQueue()
        {
            ResolveDependencies();

            if (settings == null)
            {
                if (!warnedAboutSettings)
                {
                    warnedAboutSettings = true;
                    Debug.LogError("[RecyclingSystem] RecyclingSettings 未配置，回收站不会转换任何垃圾。" +
                                   "请运行菜单「屿见/建筑/配置垃圾回收站系统」自动生成并接线。", this);
                }

                return false;
            }

            if (queue == null)
            {
                queue = new RecyclingQueue(
                    ComputeCapacity(), settings.GarbagePerCoin);
            }

            return true;
        }

        private void ResolveDependencies()
        {
            // 每次只找一次：玩家对象在启动时就存在，找不到就是没配置。
            // 不设这个标记的话，缺配置时每帧都会 Find 一次（EnsureQueue 每帧调用）。
            if (garbage == null && !garbageLookupDone)
            {
                garbageLookupDone = true;
                garbage = FindFirstObjectByType<PlayerGarbage>(FindObjectsInactive.Include);
            }

            if (currency == null && !currencyLookupDone)
            {
                currencyLookupDone = true;
                currency = FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            }
        }
    }
}
