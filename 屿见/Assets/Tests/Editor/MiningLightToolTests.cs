using NUnit.Framework;
using UnityEngine.TestTools;
using Yujian.Mining;
using Yujian.Player;

namespace Yujian.Tests
{
    /// <summary>
    /// 照明工具的 6 档数值、等级夹紧、购买价格与 Max 模式。
    /// 数值直接对照 MiningLightTool 顶部的设计表。
    /// </summary>
    public class MiningLightToolTests : WhiteBoxTestBase
    {
        private PlayerCurrency currency;
        private MiningLightTool tool;

        [SetUp]
        public void CreateTool()
        {
            // Awake 会尝试自动查找金币，但这里随后显式注入
            LogAssert.ignoreFailingMessages = true;

            currency = TestFactory.MakeCurrency(NewGameObject("Currency"), 10000);
            tool = AddComponent<MiningLightTool>(NewGameObject("LightTool"));
            tool.ConfigureForSetup(currency);
        }

        // ---------- 未拥有状态 ----------

        [Test]
        public void Default_StartsUnowned()
        {
            Assert.That(tool.Level, Is.EqualTo(0));
            Assert.That(tool.MaxLevel, Is.EqualTo(6));
            Assert.That(tool.IsUnlocked, Is.False);
            Assert.That(tool.IsMaxed, Is.False);

            Assert.That(tool.ItemId, Is.EqualTo("none"));
            Assert.That(tool.LevelName, Is.EqualTo("未拥有"));
            Assert.That(tool.CritPointCapacity, Is.EqualTo(0));
            Assert.That(tool.RefreshCooldown, Is.EqualTo(0f));
            Assert.That(tool.CritPointRadius, Is.EqualTo(0f));
            Assert.That(tool.CritPointDamageMultiplier, Is.EqualTo(1f));
            Assert.That(tool.CanSpawnCritPoints, Is.False);

            Assert.That(tool.NextUpgradeCost, Is.EqualTo(286));
            Assert.That(tool.NextLevelName, Is.EqualTo("火把"));
        }

        // ---------- 各档数值 ----------

        [Test]
        public void Level3_MatchesDesignTable()
        {
            tool.ConfigureStartingLevel(3);

            Assert.That(tool.Level, Is.EqualTo(3));
            Assert.That(tool.ItemId, Is.EqualTo("light_03"));
            Assert.That(tool.LevelName, Is.EqualTo("矿灯"));
            Assert.That(tool.CritPointCapacity, Is.EqualTo(2));
            Assert.That(tool.RefreshCooldown, Is.EqualTo(4f));
            Assert.That(tool.CritPointRadius, Is.EqualTo(3.5f));
            Assert.That(tool.CritPointDamageMultiplier, Is.EqualTo(2.2f));
            Assert.That(tool.CritPointLifetime, Is.EqualTo(5f));
            Assert.That(tool.CanSpawnCritPoints, Is.True);

            Assert.That(tool.NextItemId, Is.EqualTo("light_04"));
            Assert.That(tool.NextLevelName, Is.EqualTo("强光灯"));
            Assert.That(tool.NextCritPointCapacity, Is.EqualTo(3));
            Assert.That(tool.NextRefreshCooldown, Is.EqualTo(3.5f));
            Assert.That(tool.NextCritPointRadius, Is.EqualTo(3.5f));
            Assert.That(tool.NextCritPointDamageMultiplier, Is.EqualTo(2.3f));
            Assert.That(tool.NextUpgradeCost, Is.EqualTo(40950));
        }

        [Test]
        public void MaxLevel_MatchesDesignTableAndStops()
        {
            tool.ConfigureStartingLevel(6);

            Assert.That(tool.IsMaxed, Is.True);
            Assert.That(tool.ItemId, Is.EqualTo("light_06"));
            Assert.That(tool.LevelName, Is.EqualTo("头灯"));
            Assert.That(tool.CritPointCapacity, Is.EqualTo(3));
            Assert.That(tool.RefreshCooldown, Is.EqualTo(3f));
            Assert.That(tool.CritPointRadius, Is.EqualTo(4f));
            Assert.That(tool.CritPointDamageMultiplier, Is.EqualTo(2.5f));
            Assert.That(tool.CritPointLifetime, Is.EqualTo(6.5f));
            Assert.That(tool.NextUpgradeCost, Is.EqualTo(0));
            Assert.That(tool.NextLevelName, Is.EqualTo("头灯"));
        }

        [Test]
        public void SetLevel_ClampsAndRaisesEventOnlyOnChange()
        {
            int changes = 0;
            tool.OnToolChanged += _ => changes++;

            tool.SetLevel(2);
            Assert.That(tool.Level, Is.EqualTo(2));
            Assert.That(changes, Is.EqualTo(1));

            tool.SetLevel(2);
            Assert.That(changes, Is.EqualTo(1), "等级没变就不广播");

            tool.SetLevel(99);
            Assert.That(tool.Level, Is.EqualTo(6));

            tool.SetLevel(-5);
            Assert.That(tool.Level, Is.EqualTo(0));

            Assert.That(currency.CurrentMoney, Is.EqualTo(10000), "读档恢复不扣金币");
        }

        [Test]
        public void ConfigureStartingLevel_Clamps()
        {
            tool.ConfigureStartingLevel(99);
            Assert.That(tool.Level, Is.EqualTo(6));

            tool.ConfigureStartingLevel(-1);
            Assert.That(tool.Level, Is.EqualTo(0));
        }

        // ---------- 价格 ----------

        [Test]
        public void UpgradeCostAt_MapsEveryOwnedLevel()
        {
            Assert.That(tool.GetUpgradeCostAt(0), Is.EqualTo(286));
            Assert.That(tool.GetUpgradeCostAt(1), Is.EqualTo(1508));
            Assert.That(tool.GetUpgradeCostAt(2), Is.EqualTo(7800));
            Assert.That(tool.GetUpgradeCostAt(3), Is.EqualTo(40950));
            Assert.That(tool.GetUpgradeCostAt(4), Is.EqualTo(217750));

            Assert.That(tool.GetUpgradeCostAt(-1), Is.EqualTo(0));
            Assert.That(tool.GetUpgradeCostAt(6), Is.EqualTo(0));
        }

        [Test]
        public void UpgradeCosts_LastStepHasNoPrice_CurrentData()
        {
            // 6 档却只配了 5 个价格：5 → 6（探照灯 → 头灯）实际免费。
            // 先按现状钉住行为；如果这是漏配，改数据后这里会提醒同步更新。
            Assert.That(tool.GetUpgradeCostAt(5), Is.EqualTo(0));
        }

        [Test]
        public void UpgradeCostForLevels_SumsAndClamps()
        {
            tool.SetLevel(1);
            Assert.That(tool.GetUpgradeCostForLevels(2), Is.EqualTo(9308));  // 1508 + 7800
            Assert.That(tool.GetUpgradeCostForLevels(99), Is.EqualTo(268008)); // 到 5 级为止（最后一步免费）

            tool.SetLevel(6);
            Assert.That(tool.GetUpgradeCostForLevels(1), Is.EqualTo(0));
        }

        [Test]
        public void AffordableUpgradeCount_StopsAtMoneyLimit()
        {
            tool.SetLevel(1);

            Assert.That(tool.GetAffordableUpgradeCount(1507), Is.EqualTo(0));
            Assert.That(tool.GetAffordableUpgradeCount(1508), Is.EqualTo(1));
            Assert.That(tool.GetAffordableUpgradeCount(9308), Is.EqualTo(2));
            Assert.That(tool.GetAffordableUpgradeCount(999999), Is.EqualTo(5));

            tool.SetLevel(6);
            Assert.That(tool.GetAffordableUpgradeCount(999999), Is.EqualTo(0));
        }

        // ---------- 购买 ----------

        [Test]
        public void TryUpgrade_SpendsMoneyAndRaisesEvent()
        {
            tool.SetLevel(1);
            int changes = 0;
            tool.OnToolChanged += _ => changes++;

            Assert.That(tool.TryUpgrade(), Is.True);

            Assert.That(tool.Level, Is.EqualTo(2));
            Assert.That(currency.CurrentMoney, Is.EqualTo(8492)); // 10000 - 1508
            Assert.That(changes, Is.EqualTo(1));
        }

        [Test]
        public void TryUpgrade_InsufficientMoney_ChangesNothing()
        {
            tool.SetLevel(1);
            currency.RestoreMoney(1000);
            int changes = 0;
            tool.OnToolChanged += _ => changes++;

            Assert.That(tool.TryUpgrade(), Is.False);

            Assert.That(tool.Level, Is.EqualTo(1));
            Assert.That(currency.CurrentMoney, Is.EqualTo(1000));
            Assert.That(changes, Is.EqualTo(0));
        }

        [Test]
        public void TryUpgrade_MultipleLevels_ChargesSumOnce()
        {
            tool.SetLevel(1);
            currency.RestoreMoney(9308);

            Assert.That(tool.TryUpgrade(2), Is.True);

            Assert.That(tool.Level, Is.EqualTo(3));
            Assert.That(currency.CurrentMoney, Is.EqualTo(0));
        }

        [Test]
        public void TryUpgrade_MultipleLevels_NotEnough_DoesNothing()
        {
            tool.SetLevel(1);
            currency.RestoreMoney(9307);

            Assert.That(tool.TryUpgrade(2), Is.False);
            Assert.That(tool.Level, Is.EqualTo(1));
            Assert.That(currency.CurrentMoney, Is.EqualTo(9307));
        }

        [Test]
        public void TryUpgrade_AtMax_ReturnsFalse()
        {
            tool.SetLevel(6);
            int changes = 0;
            tool.OnToolChanged += _ => changes++;

            Assert.That(tool.TryUpgrade(), Is.False);
            Assert.That(tool.Level, Is.EqualTo(6));
            Assert.That(currency.CurrentMoney, Is.EqualTo(10000));
            Assert.That(changes, Is.EqualTo(0));
        }

        [Test]
        public void TryUpgrade_NonPositiveCount_ReturnsFalse()
        {
            tool.SetLevel(1);

            Assert.That(tool.TryUpgrade(0), Is.False);
            Assert.That(tool.TryUpgrade(-3), Is.False);
            Assert.That(tool.Level, Is.EqualTo(1));
        }

        // ---------- 界面查询辅助 ----------

        [Test]
        public void LookupHelpers_ClampLevelsToRange()
        {
            Assert.That(tool.GetRefreshCooldownAt(99), Is.EqualTo(3f));
            Assert.That(tool.GetRefreshCooldownAt(-3), Is.EqualTo(0f));
            Assert.That(tool.GetCritPointCapacityAt(99), Is.EqualTo(3));
            Assert.That(tool.GetCritPointCapacityAt(-3), Is.EqualTo(0));
            Assert.That(tool.GetCritPointMultiplierAt(99), Is.EqualTo(2.5f));
            Assert.That(tool.GetCritPointMultiplierAt(-3), Is.EqualTo(1f));
            Assert.That(tool.GetCritPointRadiusAt(99), Is.EqualTo(4f));
            Assert.That(tool.GetCritPointRadiusAt(-3), Is.EqualTo(0f));
            Assert.That(tool.GetLevelName(99), Is.EqualTo("头灯"));
            Assert.That(tool.GetLevelName(-3), Is.EqualTo("未拥有"));
            Assert.That(tool.GetItemId(99), Is.EqualTo("light_06"));
            Assert.That(tool.GetItemId(-3), Is.EqualTo("none"));
        }
    }
}
