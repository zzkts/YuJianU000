using NUnit.Framework;
using UnityEngine;
using Yujian.Core;
using Yujian.Player;

namespace Yujian.Tests
{
    /// <summary>
    /// 垃圾背包（策划案 2.1.6）的核心规则：
    /// 容量 / 升级占位公式、满包拒收、转移速度、按等级切换的转移方式。
    ///
    /// 这些用例是「背包当缰绳」的护栏：
    /// 满包时垃圾必须被拒收，**不能**悄悄落进岛屿存储。
    /// </summary>
    public class GarbageBagTests : WhiteBoxTestBase
    {
        private GarbageBagTable table;
        private GarbageBag bag;
        private PlayerCurrency currency;
        private GameAreaController areas;

        [SetUp]
        public void CreateBag()
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

            currency = TestFactory.MakeCurrency(NewGameObject("Currency"), 1000);
            areas = AddComponent<GameAreaController>(NewGameObject("Areas"));

            // EditMode 下 GameAreaController.Awake 不一定会跑，CurrentArea 可能停在默认值 Mining。
            // 显式切到观察模式，后面「背包关闭」的用例才有确定前提。
            areas.SetArea(GameArea.Observation);

            GameObject host = NewGameObject("Bag");
            host.SetActive(false);
            bag = host.AddComponent<GarbageBag>();
            bag.ConfigureForSetup(table, currency, areas, 1);
            host.SetActive(true);
        }

        [Test]
        public void Capacity_FollowsPlaceholderCurve()
        {
            Assert.That(table.GetCapacity(1), Is.EqualTo(200));
            Assert.That(table.GetCapacity(2), Is.EqualTo(230));
            Assert.That(table.GetCapacity(3), Is.InRange(260, 270));
            Assert.That(table.GetCapacity(50), Is.GreaterThan(180000),
                "50 级容量必须能装下第 4 关一趟的产出");
        }

        [Test]
        public void UpgradeCost_GrowsAndStopsAtMaxLevel()
        {
            Assert.That(table.GetUpgradeCost(1), Is.EqualTo(40));
            Assert.That(table.GetUpgradeCost(2), Is.GreaterThan(table.GetUpgradeCost(1)));
            Assert.That(table.GetUpgradeCost(50), Is.EqualTo(0));
        }

        [Test]
        public void TransferMode_SwitchesAtConfiguredLevels()
        {
            Assert.That(table.GetTransferMode(1), Is.EqualTo(GarbageBagTransferMode.LongPress));
            Assert.That(table.GetTransferMode(19), Is.EqualTo(GarbageBagTransferMode.LongPress));
            Assert.That(table.GetTransferMode(20), Is.EqualTo(GarbageBagTransferMode.SingleClick));
            Assert.That(table.GetTransferMode(49), Is.EqualTo(GarbageBagTransferMode.SingleClick));
            Assert.That(table.GetTransferMode(50), Is.EqualTo(GarbageBagTransferMode.AutoOnExit));
        }

        [Test]
        public void TryAdd_IsRejectedWhenBagClosed()
        {
            // 还没进关卡：背包关闭，什么都不收
            Assert.That(bag.IsOpen, Is.False);
            Assert.That(bag.TryAdd(50), Is.EqualTo(0));
            Assert.That(bag.Current, Is.EqualTo(0));
        }

        [Test]
        public void TryAdd_StopsAtCapacity()
        {
            EnterLevel();

            Assert.That(bag.TryAdd(120), Is.EqualTo(120));
            Assert.That(bag.TryAdd(120), Is.EqualTo(80), "只能再收 80，剩下 40 必须被拒");
            Assert.That(bag.Current, Is.EqualTo(200));
            Assert.That(bag.IsFull, Is.True);

            Assert.That(bag.TryAdd(10), Is.EqualTo(0));
            Assert.That(bag.Current, Is.EqualTo(200));
        }

        [Test]
        public void FullBag_ReportsRejection()
        {
            EnterLevel();
            bag.TryAdd(200);

            int rejected = 0;
            bag.OnDepositRejected += amount => rejected += amount;

            bag.TryAdd(35);

            Assert.That(rejected, Is.EqualTo(35));
        }

        [Test]
        public void GarbageRouting_InLevelGoesToBagNotStorage()
        {
            PlayerGarbage storage = AddComponent<PlayerGarbage>(NewGameObject("Storage"));
            TestReflect.Set(storage, "bag", bag);

            EnterLevel();
            storage.AddGarbage(50);

            Assert.That(bag.Current, Is.EqualTo(50));
            Assert.That(storage.CurrentGarbage, Is.EqualTo(0), "关卡内垃圾不能直接进岛屿存储");
        }

        [Test]
        public void GarbageRouting_FullBagRefusesInsteadOfFallingThrough()
        {
            PlayerGarbage storage = AddComponent<PlayerGarbage>(NewGameObject("Storage"));
            TestReflect.Set(storage, "bag", bag);

            EnterLevel();
            bag.TryAdd(200);

            storage.AddGarbage(60);

            Assert.That(bag.Current, Is.EqualTo(200));
            Assert.That(storage.CurrentGarbage, Is.EqualTo(0),
                "满包时必须拒收，绝不能静默溢到岛屿存储——这就是缰绳");
        }

        [Test]
        public void GarbageRouting_OutsideLevelGoesToStorage()
        {
            PlayerGarbage storage = AddComponent<PlayerGarbage>(NewGameObject("Storage"));
            TestReflect.Set(storage, "bag", bag);

            // 回岛：区域不再是清洁，背包关闭
            areas.SetArea(GameArea.Observation);
            Assert.That(bag.IsOpen, Is.False);

            storage.AddGarbage(50);

            Assert.That(storage.CurrentGarbage, Is.EqualTo(50));
            Assert.That(bag.Current, Is.EqualTo(0));
        }

        [Test]
        public void Upgrade_SpendsMoneyAndRaisesCapacity()
        {
            int before = bag.Capacity;

            Assert.That(bag.TryUpgrade(), Is.True);
            Assert.That(bag.Level, Is.EqualTo(2));
            Assert.That(bag.Capacity, Is.GreaterThan(before));
            Assert.That(currency.CurrentMoney, Is.EqualTo(1000 - table.GetUpgradeCost(1)));
        }

        [Test]
        public void Upgrade_FailsWhenMoneyIsShort()
        {
            PlayerCurrency broke = TestFactory.MakeCurrency(NewGameObject("Broke"), 0);
            bag.ConfigureForSetup(table, broke, areas, 1);

            Assert.That(bag.TryUpgrade(), Is.False);
            Assert.That(bag.Level, Is.EqualTo(1));
            Assert.That(broke.CurrentMoney, Is.EqualTo(0));
        }

        [Test]
        public void TryTake_MovesOutAndClamps()
        {
            EnterLevel();
            bag.TryAdd(120);

            Assert.That(bag.TryTake(50), Is.EqualTo(50));
            Assert.That(bag.Current, Is.EqualTo(70));
            Assert.That(bag.TryTake(999), Is.EqualTo(70));
            Assert.That(bag.Current, Is.EqualTo(0));
            Assert.That(bag.TryTake(1), Is.EqualTo(0));
        }

        [Test]
        public void Restore_ClampsToCapacity()
        {
            bag.Restore(3, 999999);

            Assert.That(bag.Level, Is.EqualTo(3));
            Assert.That(bag.Current, Is.EqualTo(bag.Capacity));
        }

        private void EnterLevel()
        {
            areas.SetArea(GameArea.Mining);
            Assert.That(bag.IsOpen, Is.True, "进入清洁区后背包必须打开");
        }
    }
}
