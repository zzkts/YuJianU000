using NUnit.Framework;
using UnityEngine;
using Yujian.Building.Recycling;

namespace Yujian.Tests
{
    /// <summary>
    /// 回收站核心公式：共享队列容量、投料截断、按比例逐点转换、金币小数累计、
    /// 多站产能叠加、效率倍率、拆站退料与读档恢复。
    /// 纯逻辑，不需要场景。
    /// </summary>
    public class RecyclingQueueTests : WhiteBoxTestBase
    {
        private const int Ratio = 5;

        // ---------- 队列容量与投料 ----------

        [Test]
        public void Constructor_NegativeCapacity_ClampsToZero()
        {
            RecyclingQueue queue = new RecyclingQueue(-10, Ratio);

            Assert.That(queue.Capacity, Is.EqualTo(0));
            Assert.That(queue.Deposit(3), Is.EqualTo(0));
            Assert.That(queue.QueuedGarbage, Is.EqualTo(0));
        }

        [Test]
        public void Constructor_InvalidRatio_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new RecyclingQueue(10, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new RecyclingQueue(10, -1));
        }

        [Test]
        public void Deposit_WithinCapacity_AcceptsAll()
        {
            RecyclingQueue queue = new RecyclingQueue(10, Ratio);

            Assert.That(queue.Deposit(7), Is.EqualTo(7));
            Assert.That(queue.QueuedGarbage, Is.EqualTo(7));
            Assert.That(queue.FreeCapacity, Is.EqualTo(3));
            Assert.That(queue.IsFull, Is.False);
        }

        [Test]
        public void Deposit_OverCapacity_AcceptsOnlyFreeSpace()
        {
            RecyclingQueue queue = new RecyclingQueue(10, Ratio);
            queue.Deposit(8);

            Assert.That(queue.Deposit(5), Is.EqualTo(2));
            Assert.That(queue.QueuedGarbage, Is.EqualTo(10));
            Assert.That(queue.IsFull, Is.True);
            Assert.That(queue.Deposit(1), Is.EqualTo(0));
        }

        [Test]
        public void Deposit_NonPositive_IsIgnored()
        {
            RecyclingQueue queue = new RecyclingQueue(10, Ratio);

            Assert.That(queue.Deposit(0), Is.EqualTo(0));
            Assert.That(queue.Deposit(-3), Is.EqualTo(0));
            Assert.That(queue.QueuedGarbage, Is.EqualTo(0));
        }

        [Test]
        public void IsFull_ZeroCapacity_IsAlwaysFull()
        {
            RecyclingQueue queue = new RecyclingQueue(0, Ratio);

            Assert.That(queue.IsFull, Is.True);
            Assert.That(queue.Deposit(5), Is.EqualTo(0));
        }

        // ---------- 基础转换 ----------

        [Test]
        public void Tick_BaseRate_FiveGarbageMakesOneCoin()
        {
            RecyclingQueue queue = new RecyclingQueue(200, Ratio);
            queue.Deposit(5);

            int coins = queue.Tick(1.0, 5.0, 1.0, out int converted);

            Assert.That(converted, Is.EqualTo(5));
            Assert.That(coins, Is.EqualTo(1));
            Assert.That(queue.QueuedGarbage, Is.EqualTo(0));
            Assert.That(queue.CoinProgress, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(queue.TotalCoinsProduced, Is.EqualTo(1));
            Assert.That(queue.TotalGarbageConsumed, Is.EqualTo(5));
        }

        [Test]
        public void Tick_PartialBatch_AccumulatesFractionAcrossBatches()
        {
            RecyclingQueue queue = new RecyclingQueue(200, Ratio);
            queue.Deposit(3);

            int coins = queue.Tick(1.0, 5.0, 1.0, out int converted);

            Assert.That(converted, Is.EqualTo(3));
            Assert.That(coins, Is.EqualTo(0), "3 垃圾不满 5:1 的一批，不应产出整数金币");
            Assert.That(queue.CoinProgress, Is.EqualTo(0.6).Within(1e-9));
            Assert.That(queue.QueuedGarbage, Is.EqualTo(0));

            queue.Deposit(2);
            coins = queue.Tick(1.0, 5.0, 1.0, out converted);

            Assert.That(converted, Is.EqualTo(2));
            Assert.That(coins, Is.EqualTo(1), "0.6 + 0.4 应补齐 1 枚金币");
            Assert.That(queue.CoinProgress, Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        public void Tick_DoesNotConsumeMoreThanQueued()
        {
            RecyclingQueue queue = new RecyclingQueue(200, Ratio);
            queue.Deposit(2);

            int coins = queue.Tick(100.0, 5.0, 1.0, out int converted);

            Assert.That(converted, Is.EqualTo(2));
            Assert.That(coins, Is.EqualTo(0));
            Assert.That(queue.QueuedGarbage, Is.EqualTo(0));
            Assert.That(queue.CoinProgress, Is.EqualTo(0.4).Within(1e-9));
        }

        [Test]
        public void Tick_EmptyQueue_DoesNotBankWork()
        {
            RecyclingQueue queue = new RecyclingQueue(200, Ratio);

            queue.Tick(10.0, 5.0, 1.0, out _);

            // 空队列期间攒下的“工作量”必须作废，否则投料 1 个垃圾会瞬间被消化
            queue.Deposit(1);
            queue.Tick(0.1, 5.0, 1.0, out _);

            Assert.That(queue.QueuedGarbage, Is.EqualTo(1));

            queue.Tick(0.1, 5.0, 1.0, out int converted);

            Assert.That(converted, Is.EqualTo(1));
            Assert.That(queue.CoinProgress, Is.EqualTo(0.2).Within(1e-9));
        }

        [Test]
        public void Tick_MultipleStations_RatesAddUp()
        {
            RecyclingQueue queue = new RecyclingQueue(600, Ratio);
            queue.Deposit(10);

            int coins = queue.Tick(1.0, 10.0, 1.0, out int converted);

            Assert.That(converted, Is.EqualTo(10));
            Assert.That(coins, Is.EqualTo(2));
            Assert.That(queue.QueuedGarbage, Is.EqualTo(0));
        }

        [Test]
        public void Tick_FractionalDeltas_AccumulateWithoutLoss()
        {
            RecyclingQueue queue = new RecyclingQueue(200, Ratio);
            queue.Deposit(5);

            int coins = 0;
            int converted = 0;

            for (int i = 0; i < 10; i++)
            {
                coins += queue.Tick(0.1, 5.0, 1.0, out int stepConverted);
                converted += stepConverted;
            }

            Assert.That(converted, Is.EqualTo(5));
            Assert.That(coins, Is.EqualTo(1));
            Assert.That(queue.QueuedGarbage, Is.EqualTo(0));
        }

        // ---------- 效率倍率 ----------

        [Test]
        public void Tick_EfficiencyMultiplier_AddsFractionalCoin()
        {
            RecyclingQueue queue = new RecyclingQueue(200, Ratio);
            queue.Deposit(5);

            int coins = queue.Tick(1.0, 5.0, 1.1, out _);

            Assert.That(coins, Is.EqualTo(1));
            Assert.That(queue.CoinProgress, Is.EqualTo(0.1).Within(1e-9));

            queue.Deposit(45);
            coins = queue.Tick(9.0, 5.0, 1.1, out _);

            // 45 垃圾 × 1.1 ÷ 5 = 9.9 金币，加上上一批的 0.1，正好 10 枚
            Assert.That(coins, Is.EqualTo(10));
            Assert.That(queue.CoinProgress, Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        public void Tick_ZeroOrNegativeEfficiency_FallsBackToBase()
        {
            RecyclingQueue queue = new RecyclingQueue(200, Ratio);
            queue.Deposit(10);

            int coins = queue.Tick(1.0, 10.0, 0.0, out _);

            Assert.That(coins, Is.EqualTo(2));
        }

        // ---------- 非法 / 空闲步长 ----------

        [Test]
        public void Tick_ZeroOrNegativeDelta_DoesNothing()
        {
            RecyclingQueue queue = new RecyclingQueue(200, Ratio);
            queue.Deposit(10);

            Assert.That(queue.Tick(0.0, 5.0, 1.0, out int converted), Is.EqualTo(0));
            Assert.That(converted, Is.EqualTo(0));
            Assert.That(queue.Tick(-1.0, 5.0, 1.0, out _), Is.EqualTo(0));
            Assert.That(queue.QueuedGarbage, Is.EqualTo(10));
        }

        [Test]
        public void Tick_ZeroRate_DoesNothing()
        {
            RecyclingQueue queue = new RecyclingQueue(200, Ratio);
            queue.Deposit(10);

            Assert.That(queue.Tick(1.0, 0.0, 1.0, out _), Is.EqualTo(0));
            Assert.That(queue.QueuedGarbage, Is.EqualTo(10));
        }

        // ---------- 退料与容量变化 ----------

        [Test]
        public void WithdrawAll_ReturnsQueuedGarbageAndStopsProcessing()
        {
            RecyclingQueue queue = new RecyclingQueue(200, Ratio);
            queue.Deposit(10);
            queue.Tick(0.1, 5.0, 1.0, out _);

            int returned = queue.WithdrawAll();

            Assert.That(returned, Is.EqualTo(10));
            Assert.That(queue.QueuedGarbage, Is.EqualTo(0));
            Assert.That(queue.Tick(1.0, 5.0, 1.0, out _), Is.EqualTo(0));
        }

        [Test]
        public void WithdrawAll_KeepsPendingCoinProgress()
        {
            RecyclingQueue queue = new RecyclingQueue(200, Ratio);
            queue.Deposit(3);
            queue.Tick(1.0, 5.0, 1.0, out _);

            queue.WithdrawAll();

            Assert.That(queue.CoinProgress, Is.EqualTo(0.6).Within(1e-9),
                "已经消化掉的 3 个垃圾对应的小数金币不能在退料时消失");
        }

        [Test]
        public void SetCapacity_Shrink_ReturnsOverflow()
        {
            RecyclingQueue queue = new RecyclingQueue(10, Ratio);
            queue.Deposit(10);

            int overflow = queue.SetCapacity(4);

            Assert.That(overflow, Is.EqualTo(6));
            Assert.That(queue.QueuedGarbage, Is.EqualTo(4));
            Assert.That(queue.Capacity, Is.EqualTo(4));
        }

        [Test]
        public void SetCapacity_Grow_KeepsQueueAndAddsFreeSpace()
        {
            RecyclingQueue queue = new RecyclingQueue(4, Ratio);
            queue.Deposit(4);

            int overflow = queue.SetCapacity(10);

            Assert.That(overflow, Is.EqualTo(0));
            Assert.That(queue.QueuedGarbage, Is.EqualTo(4));
            Assert.That(queue.FreeCapacity, Is.EqualTo(6));
        }

        // ---------- 读档恢复 ----------

        [Test]
        public void RestoreState_ClampsQueueAndKeepsCoinProgress()
        {
            RecyclingQueue queue = new RecyclingQueue(10, Ratio);

            queue.RestoreState(20, 0.75);

            Assert.That(queue.QueuedGarbage, Is.EqualTo(10));
            Assert.That(queue.CoinProgress, Is.EqualTo(0.75).Within(1e-9));
            Assert.That(queue.TotalCoinsProduced, Is.EqualTo(0));
        }

        [Test]
        public void RestoreState_NormalizesInvalidValues()
        {
            RecyclingQueue queue = new RecyclingQueue(10, Ratio);

            queue.RestoreState(-5, 2.25);

            Assert.That(queue.QueuedGarbage, Is.EqualTo(0));
            Assert.That(queue.CoinProgress, Is.EqualTo(0.25).Within(1e-9));

            queue.RestoreState(3, double.NaN);

            Assert.That(queue.QueuedGarbage, Is.EqualTo(3));
            Assert.That(queue.CoinProgress, Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        public void Reset_ClearsEverything()
        {
            RecyclingQueue queue = new RecyclingQueue(10, Ratio);
            queue.Deposit(10);
            queue.Tick(1.0, 5.0, 1.0, out _);

            queue.Reset();

            Assert.That(queue.QueuedGarbage, Is.EqualTo(0));
            Assert.That(queue.CoinProgress, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(queue.TotalCoinsProduced, Is.EqualTo(0));
            Assert.That(queue.TotalGarbageConsumed, Is.EqualTo(0));
        }
    }

    /// <summary>回收站配置资产：容量 / 转换比 / 基础产能公式。</summary>
    public class RecyclingSettingsTests : WhiteBoxTestBase
    {
        private RecyclingSettings settings;

        [SetUp]
        public void CreateSettings()
        {
            settings = Track(ScriptableObject.CreateInstance<RecyclingSettings>());
        }

        [Test]
        public void Defaults_MatchDesignDocument()
        {
            Assert.That(settings.BaseQueueCapacityPerStation, Is.EqualTo(200));
            Assert.That(settings.MaxStations, Is.EqualTo(3));
            Assert.That(settings.GarbagePerCoin, Is.EqualTo(5));
            Assert.That(settings.BaseCoinsPerSecond, Is.EqualTo(1f).Within(1e-5));
            Assert.That(settings.BaseGarbagePerSecond, Is.EqualTo(5f).Within(1e-5));
        }

        [Test]
        public void GetQueueCapacity_ScalesWithStationCount()
        {
            Assert.That(settings.GetQueueCapacity(0), Is.EqualTo(0));
            Assert.That(settings.GetQueueCapacity(1), Is.EqualTo(200));
            Assert.That(settings.GetQueueCapacity(3), Is.EqualTo(600));
            Assert.That(settings.GetQueueCapacity(-2), Is.EqualTo(0));
        }

        [Test]
        public void GetQueueCapacity_DoesNotClampToMaxStations()
        {
            // 旧存档里如果多出一座，容量要按真实站数算，不能静默吞掉
            Assert.That(settings.GetQueueCapacity(4), Is.EqualTo(800));
        }

        [Test]
        public void GetBaseGarbagePerSecond_ScalesWithStationCount()
        {
            Assert.That(settings.GetBaseGarbagePerSecond(0), Is.EqualTo(0f));
            Assert.That(settings.GetBaseGarbagePerSecond(2), Is.EqualTo(10f).Within(1e-5));
        }

        [Test]
        public void CustomCycle_ChangesBaseRates()
        {
            TestReflect.Set(settings, "baseCoinsPerCycle", 10);
            TestReflect.Set(settings, "baseCycleSeconds", 5f);

            Assert.That(settings.BaseCoinsPerSecond, Is.EqualTo(2f).Within(1e-5));
            Assert.That(settings.BaseGarbagePerSecond, Is.EqualTo(10f).Within(1e-5));
        }

        [Test]
        public void OnValidate_ClampsInvalidValues()
        {
            TestReflect.Set(settings, "baseQueueCapacityPerStation", 0);
            TestReflect.Set(settings, "maxStations", 0);
            TestReflect.Set(settings, "garbagePerCoin", 0);
            TestReflect.Set(settings, "baseCoinsPerCycle", 0);
            TestReflect.Set(settings, "baseCycleSeconds", 0f);

            TestReflect.Invoke(settings, "OnValidate");

            Assert.That(settings.BaseQueueCapacityPerStation, Is.EqualTo(1));
            Assert.That(settings.MaxStations, Is.EqualTo(1));
            Assert.That(settings.GarbagePerCoin, Is.EqualTo(1));
            Assert.That(settings.BaseCoinsPerCycle, Is.EqualTo(1));
            Assert.That(settings.BaseCycleSeconds, Is.EqualTo(0.01f).Within(1e-6));
        }
    }
}
