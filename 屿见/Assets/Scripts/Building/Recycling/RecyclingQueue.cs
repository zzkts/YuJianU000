using System;

namespace Yujian.Building.Recycling
{
    /// <summary>
    /// 所有回收站共享的垃圾队列与转换进度（策划案「垃圾回收站系统」）。
    ///
    /// 纯逻辑类：不继承 MonoBehaviour、不读场景、不碰金币 / 垃圾组件、不保存配置。
    /// 谁调用它，谁负责把产出的金币交给玩家、把接收不下的垃圾留在玩家手里。
    ///
    /// 规则：
    ///   · 队列容量由外部按「每座容量 × 站数」算好后传入；
    ///   · 投料超过容量时只接收能放下的部分（返回值即实际接收量）；
    ///   · 转换按垃圾为单位逐点推进，**不满一批（<see cref="GarbagePerCoin"/>）也按比例累计**，
    ///     小数部分留在 <see cref="CoinProgress"/>，凑满 1 枚金币才结算；
    ///   · 队列为空时产能闲置，不累积任何“欠下的工作”；
    ///   · 效率倍率（强化「回收效率提升」）直接乘在金币产出上，因此可产出 5 垃圾 → 1.1 金币，
    ///     多出来的 0.1 同样累计到下一批（策划案：小数部分累计到下一批）。
    /// </summary>
    public sealed class RecyclingQueue
    {
        /// <summary>浮点累计的容差：0.1 秒 × 5 次累加不应因为二进制误差少算 1 个垃圾。</summary>
        private const double ProgressEpsilon = 1e-9;

        private readonly int garbagePerCoin;

        private int capacity;
        private int queuedGarbage;

        /// <summary>已经“加工”但还不足 1 个垃圾的进度（0 ≤ 值 &lt; 1）。</summary>
        private double consumptionCredit;

        /// <summary>已转换但还不足 1 枚金币的进度（0 ≤ 值 &lt; 1）。</summary>
        private double coinCredit;

        private int totalCoinsProduced;
        private int totalGarbageConsumed;

        /// <summary>
        /// 创建一条共享队列。
        /// </summary>
        /// <param name="capacity">队列容量（垃圾个数），小于 0 按 0 处理。</param>
        /// <param name="garbagePerCoin">多少垃圾换 1 金币，必须为正数。</param>
        public RecyclingQueue(int capacity, int garbagePerCoin)
        {
            if (garbagePerCoin <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(garbagePerCoin), garbagePerCoin, "转换比必须为正数（多少垃圾换 1 金币）。");
            }

            this.capacity = Math.Max(0, capacity);
            this.garbagePerCoin = garbagePerCoin;
        }

        /// <summary>当前队列容量。</summary>
        public int Capacity => capacity;

        /// <summary>队列里尚未转换的垃圾数。</summary>
        public int QueuedGarbage => queuedGarbage;

        /// <summary>还能再放进多少垃圾。</summary>
        public int FreeCapacity => Math.Max(0, capacity - queuedGarbage);

        /// <summary>队列是否已满。</summary>
        public bool IsFull => queuedGarbage >= capacity;

        /// <summary>当前批次的金币进度（0 ≤ 值 &lt; 1），供进度条平滑缩短使用。</summary>
        public double CoinProgress => coinCredit;

        /// <summary>累计产出的金币数（本次运行 / 本次读档以来）。</summary>
        public int TotalCoinsProduced => totalCoinsProduced;

        /// <summary>累计转换掉的垃圾数。</summary>
        public int TotalGarbageConsumed => totalGarbageConsumed;

        /// <summary>多少垃圾换 1 金币。</summary>
        public int GarbagePerCoin => garbagePerCoin;

        /// <summary>
        /// 投入垃圾。超出容量的部分不会被接收，调用方需要把它们留在玩家手里。
        /// </summary>
        /// <param name="amount">玩家想投入的垃圾数，≤ 0 时不做任何事。</param>
        /// <returns>实际放入队列的垃圾数（0 ～ amount）。</returns>
        public int Deposit(int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            int accepted = Math.Min(amount, FreeCapacity);

            if (accepted <= 0)
            {
                return 0;
            }

            queuedGarbage += accepted;
            return accepted;
        }

        /// <summary>
        /// 推进转换。调用方每帧（或固定步长）调用一次。
        /// </summary>
        /// <param name="deltaSeconds">经过的秒数，≤ 0 时不做任何事。</param>
        /// <param name="garbagePerSecond">当前总垃圾消耗速度（各回收站叠加后的值）。</param>
        /// <param name="efficiencyMultiplier">回收效率倍率（1 = 基础；1.1 = 每批金币 +10%）。≤ 0 按 1 处理。</param>
        /// <param name="garbageConverted">本次实际转换掉的垃圾数（整数）。</param>
        /// <returns>本次产出的金币数（整数，可能为 0）。</returns>
        public int Tick(
            double deltaSeconds, double garbagePerSecond, double efficiencyMultiplier,
            out int garbageConverted)
        {
            garbageConverted = 0;

            if (deltaSeconds <= 0 || garbagePerSecond <= 0)
            {
                return 0;
            }

            // 队列为空：产能闲置，不累积任何工作进度（策划案边界情况）
            if (queuedGarbage <= 0)
            {
                consumptionCredit = 0;
                return 0;
            }

            double efficiency = efficiencyMultiplier > 0 ? efficiencyMultiplier : 1.0;

            consumptionCredit += deltaSeconds * garbagePerSecond;

            double wholeDouble = Math.Floor(consumptionCredit + ProgressEpsilon);
            int whole = wholeDouble >= queuedGarbage ? queuedGarbage : (int)wholeDouble;

            if (whole <= 0)
            {
                return 0;
            }

            queuedGarbage -= whole;
            consumptionCredit -= whole;
            garbageConverted = whole;
            totalGarbageConsumed += whole;

            coinCredit += whole * efficiency / garbagePerCoin;

            int coins = (int)Math.Floor(coinCredit + ProgressEpsilon);

            if (coins > 0)
            {
                coinCredit -= coins;
                totalCoinsProduced += coins;
            }

            // 队列清空后残余的“加工中”进度作废：没有垃圾可加工时不能攒进度
            if (queuedGarbage <= 0)
            {
                consumptionCredit = 0;
            }

            return coins;
        }

        /// <summary>
        /// 取走队列里全部垃圾（拆站 / 退出时返还玩家用），并清空加工进度。
        /// 已经转换出来的金币小数进度 <see cref="CoinProgress"/> 保留——它是垃圾已消化的部分，
        /// 下次有垃圾/回收站时继续累计。
        /// </summary>
        /// <returns>返还给玩家的垃圾数。</returns>
        public int WithdrawAll()
        {
            int amount = queuedGarbage;
            queuedGarbage = 0;
            consumptionCredit = 0;
            return amount;
        }

        /// <summary>
        /// 调整容量（拆站后容量缩减 / 新站建成后扩容）。
        /// 容量缩小时，超出新容量的垃圾会被取出并由调用方返还玩家。
        /// </summary>
        /// <param name="newCapacity">新容量，小于 0 按 0 处理。</param>
        /// <returns>因容量缩减而溢出的垃圾数（需要返还给玩家）。</returns>
        public int SetCapacity(int newCapacity)
        {
            capacity = Math.Max(0, newCapacity);

            int overflow = Math.Max(0, queuedGarbage - capacity);

            if (overflow > 0)
            {
                queuedGarbage -= overflow;
            }

            return overflow;
        }

        /// <summary>
        /// 读档用：直接写入队列状态并清空运行时累计值。
        /// 超出容量的队列数会被截断，负值按 0 处理。
        /// </summary>
        /// <param name="queued">队列里的垃圾数。</param>
        /// <param name="coinProgress">当前批次金币进度（0 ～ 1）。</param>
        public void RestoreState(int queued, double coinProgress)
        {
            queuedGarbage = Math.Min(Math.Max(0, queued), capacity);
            consumptionCredit = 0;

            if (double.IsNaN(coinProgress) || double.IsInfinity(coinProgress))
            {
                coinCredit = 0;
            }
            else
            {
                // 0.9999999 之类的浮点值允许保留；超出 [0,1) 的值按余数处理
                coinCredit = Math.Max(0, coinProgress);
                int whole = (int)Math.Floor(coinCredit + ProgressEpsilon);
                coinCredit -= whole;
            }

            totalCoinsProduced = 0;
            totalGarbageConsumed = 0;
        }

        /// <summary>清空队列与全部进度（新游戏 / 重置存档用）。</summary>
        public void Reset()
        {
            queuedGarbage = 0;
            consumptionCredit = 0;
            coinCredit = 0;
            totalCoinsProduced = 0;
            totalGarbageConsumed = 0;
        }
    }
}
