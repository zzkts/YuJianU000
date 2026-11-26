using NUnit.Framework;
using UnityEngine;
using Yujian.Core;
using Yujian.Player;

namespace Yujian.Tests
{
    /// <summary>
    /// 关卡暂存池（2026-11-26）：背包满时掉落的垃圾不消失，记在这一关里；
    /// 下次进关按背包剩余容量回灌，灌不下的继续留着。
    ///
    /// 这些用例守住的核心是「奖励不蒸发」：
    /// 背包满时 AddGarbage 必须把差额交给暂存池，而不是丢掉。
    /// </summary>
    public class GarbageOverflowTests : WhiteBoxTestBase
    {
        private GarbageBagTable table;
        private GarbageBag bag;
        private GameAreaController areas;
        private GarbageOverflow overflow;

        [SetUp]
        public void CreateWorld()
        {
            table = Track(ScriptableObject.CreateInstance<GarbageBagTable>());
            table.ConfigureForSetup(
                newBaseCapacity: 200,
                newCapacityGrowth: 1.15f,
                newMaxLevel: 50,
                newUpgradeCostBase: 40,
                newUpgradeCostGrowth: 1.18f,
                newTransferPerSecond: 10f,
                newLongPressMaxLevel: 19,
                newClickMaxLevel: 49);

            PlayerCurrency currency = TestFactory.MakeCurrency(NewGameObject("Currency"), 1000);
            areas = AddComponent<GameAreaController>(NewGameObject("Areas"));
            areas.SetArea(GameArea.Observation);

            GameObject bagHost = NewGameObject("Bag");
            bagHost.SetActive(false);
            bag = bagHost.AddComponent<GarbageBag>();
            bag.ConfigureForSetup(table, currency, areas, 1);
            bagHost.SetActive(true);

            overflow = AddComponent<GarbageOverflow>(NewGameObject("Overflow"));
            overflow.ConfigureForSetup(bag, areas, null);
        }

        [Test]
        public void Deposit_KeepsGarbagePerLevel()
        {
            overflow.DepositFor(1, 60);
            overflow.DepositFor(2, 25);

            Assert.That(overflow.GetPending(1), Is.EqualTo(60));
            Assert.That(overflow.GetPending(2), Is.EqualTo(25));
            Assert.That(overflow.GetPending(3), Is.EqualTo(0));
        }

        [Test]
        public void Drain_FillsBagUpToCapacity()
        {
            EnterLevel();
            overflow.DepositFor(1, 500);

            int moved = overflow.DrainFor(1);

            Assert.That(moved, Is.EqualTo(200));
            Assert.That(bag.Current, Is.EqualTo(200));
            Assert.That(overflow.GetPending(1), Is.EqualTo(300), "灌不下的部分必须继续留在关卡里");
        }

        [Test]
        public void Drain_DoesNothingWhenBagIsFull()
        {
            EnterLevel();
            bag.TryAdd(200);
            overflow.DepositFor(1, 50);

            Assert.That(overflow.DrainFor(1), Is.EqualTo(0));
            Assert.That(overflow.GetPending(1), Is.EqualTo(50));
        }

        [Test]
        public void Drain_OnlyTakesWhatFits()
        {
            EnterLevel();
            bag.TryAdd(150);
            overflow.DepositFor(1, 100);

            Assert.That(overflow.DrainFor(1), Is.EqualTo(50));
            Assert.That(bag.Current, Is.EqualTo(200));
            Assert.That(overflow.GetPending(1), Is.EqualTo(50));
        }

        [Test]
        public void GarbageRouting_FullBagSendsRemainderToOverflow()
        {
            PlayerGarbage storage = AddComponent<PlayerGarbage>(NewGameObject("Storage"));
            TestReflect.Set(storage, "bag", bag);
            TestReflect.Set(storage, "overflow", overflow);

            EnterLevel();
            bag.TryAdd(200);

            storage.AddGarbage(60);

            Assert.That(bag.Current, Is.EqualTo(200));
            Assert.That(overflow.GetPending(0), Is.EqualTo(60),
                "没有关卡号时落到兜底键，垃圾仍然不能丢");
        }

        [Test]
        public void GarbageRouting_PartialFitSendsOnlyTheRemainder()
        {
            PlayerGarbage storage = AddComponent<PlayerGarbage>(NewGameObject("Storage"));
            TestReflect.Set(storage, "bag", bag);
            TestReflect.Set(storage, "overflow", overflow);

            EnterLevel();
            bag.TryAdd(190);

            storage.AddGarbage(50);

            Assert.That(bag.Current, Is.EqualTo(200));
            Assert.That(overflow.GetPending(0), Is.EqualTo(40));
        }

        [Test]
        public void Capture_Restore_RoundTrips()
        {
            overflow.DepositFor(1, 30);
            overflow.DepositFor(4, 70);

            var entries = overflow.CaptureEntries();
            Assert.That(entries.Count, Is.EqualTo(2));

            overflow.ClearAll();
            Assert.That(overflow.GetPending(1), Is.EqualTo(0));

            for (int i = 0; i < entries.Count; i++)
            {
                overflow.RestoreOne(entries[i].Key, entries[i].Value);
            }

            Assert.That(overflow.GetPending(1), Is.EqualTo(30));
            Assert.That(overflow.GetPending(4), Is.EqualTo(70));
        }

        private void EnterLevel()
        {
            areas.SetArea(GameArea.Mining);
            Assert.That(bag.IsOpen, Is.True);
        }
    }
}
