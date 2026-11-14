using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yujian.Mining;
using Yujian.Player;

namespace Yujian.Tests
{
    /// <summary>
    /// 挖矿工具进度的运行逻辑：等级夹紧、攻击力、连续升级扣钱、金币不足时不改状态、事件。
    /// 用线性成长（1 级 10 攻击，每级 +5；升级 15 / 30）把数值钉死。
    /// </summary>
    public class MiningToolProgressTests : WhiteBoxTestBase
    {
        private MiningToolData data;
        private PlayerCurrency currency;
        private MiningToolProgress progress;

        [SetUp]
        public void CreateProgress()
        {
            // Awake 在配置工具数据前执行会报“未配置”，这是构造顺序问题
            LogAssert.ignoreFailingMessages = true;

            data = Track(TestFactory.MakeMiningTool());
            currency = TestFactory.MakeCurrency(NewGameObject("Currency"), 100);

            progress = AddComponent<MiningToolProgress>(NewGameObject("ToolProgress"));
            progress.ConfigureForSetup(data, currency, 1);
        }

        // ---------- 基础显示 ----------

        [Test]
        public void ConfigureForSetup_ExposesLevelAndAttack()
        {
            Assert.That(progress.Data, Is.SameAs(data));
            Assert.That(progress.Currency, Is.SameAs(currency));
            Assert.That(progress.CurrentLevel, Is.EqualTo(1));
            Assert.That(progress.MaxLevel, Is.EqualTo(3));
            Assert.That(progress.IsMaxLevel, Is.False);
            Assert.That(progress.ToolName, Is.EqualTo("石头"));
            Assert.That(progress.CurrentAttack, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(progress.NextAttack, Is.EqualTo(15f).Within(1e-4f));
            Assert.That(progress.NextUpgradeCost, Is.EqualTo(15));
        }

        [Test]
        public void ConfigureForSetup_ClampsStartingLevel()
        {
            progress.ConfigureForSetup(data, currency, 99);

            Assert.That(progress.CurrentLevel, Is.EqualTo(3));
            Assert.That(progress.IsMaxLevel, Is.True);
        }

        [Test]
        public void NextAttackAtMaxLevel_EqualsCurrentAttack()
        {
            progress.SetLevel(3);

            Assert.That(progress.CurrentAttack, Is.EqualTo(20f).Within(1e-4f));
            Assert.That(progress.NextAttack, Is.EqualTo(20f).Within(1e-4f));
            Assert.That(progress.NextUpgradeCost, Is.EqualTo(0));
        }

        // ---------- 升级 ----------

        [Test]
        public void TryUpgrade_SpendsMoneyAndRaisesEvent()
        {
            int changes = 0;
            progress.OnToolChanged += _ => changes++;

            Assert.That(progress.TryUpgrade(), Is.True);

            Assert.That(progress.CurrentLevel, Is.EqualTo(2));
            Assert.That(progress.CurrentAttack, Is.EqualTo(15f).Within(1e-4f));
            Assert.That(currency.CurrentMoney, Is.EqualTo(85));
            Assert.That(changes, Is.EqualTo(1));
        }

        [Test]
        public void TryUpgrade_InsufficientMoney_ChangesNothing()
        {
            currency.RestoreMoney(14);
            int changes = 0;
            progress.OnToolChanged += _ => changes++;

            Assert.That(progress.TryUpgrade(), Is.False);

            Assert.That(progress.CurrentLevel, Is.EqualTo(1));
            Assert.That(currency.CurrentMoney, Is.EqualTo(14));
            Assert.That(changes, Is.EqualTo(0));
        }

        [Test]
        public void TryUpgrade_MultipleLevels_ChargesOnlyActualLevels()
        {
            currency.RestoreMoney(45);

            Assert.That(progress.TryUpgrade(5), Is.True);

            Assert.That(progress.CurrentLevel, Is.EqualTo(3));
            Assert.That(currency.CurrentMoney, Is.EqualTo(0));
            Assert.That(progress.IsMaxLevel, Is.True);
        }

        [Test]
        public void TryUpgrade_MultipleLevels_NotEnoughForAll_DoesNothing()
        {
            currency.RestoreMoney(44);

            Assert.That(progress.TryUpgrade(2), Is.False);
            Assert.That(progress.CurrentLevel, Is.EqualTo(1));
            Assert.That(currency.CurrentMoney, Is.EqualTo(44));
        }

        [Test]
        public void TryUpgrade_AtMaxLevel_ReturnsFalse()
        {
            progress.SetLevel(3);
            int changes = 0;
            progress.OnToolChanged += _ => changes++;

            Assert.That(progress.TryUpgrade(), Is.False);
            Assert.That(progress.CurrentLevel, Is.EqualTo(3));
            Assert.That(currency.CurrentMoney, Is.EqualTo(100));
            Assert.That(changes, Is.EqualTo(0));
        }

        [Test]
        public void TryUpgrade_NonPositiveCount_ReturnsFalse()
        {
            Assert.That(progress.TryUpgrade(0), Is.False);
            Assert.That(progress.TryUpgrade(-2), Is.False);
            Assert.That(progress.CurrentLevel, Is.EqualTo(1));
            Assert.That(currency.CurrentMoney, Is.EqualTo(100));
        }

        [Test]
        public void TryUpgrade_WithoutData_ReturnsFalse()
        {
            MiningToolProgress bare = AddComponent<MiningToolProgress>(NewGameObject("BareProgress"));
            TestReflect.Set(bare, "playerCurrency", currency);

            LogAssert.Expect(LogType.Error, new Regex("没有挖矿工具数据|MiningToolProgress"));

            Assert.That(bare.TryUpgrade(), Is.False);
            Assert.That(bare.CurrentAttack, Is.EqualTo(1f), "没有数据时攻击力回退为 1");
        }

        [Test]
        public void TryUpgrade_WithoutCurrency_ReturnsFalse()
        {
            TestReflect.Set(progress, "playerCurrency", null);

            LogAssert.Expect(LogType.Error, new Regex("PlayerCurrency"));

            Assert.That(progress.TryUpgrade(), Is.False);
            Assert.That(progress.CurrentLevel, Is.EqualTo(1));
        }

        // ---------- SetLevel（读档恢复） ----------

        [Test]
        public void SetLevel_ClampsAndRaisesEventOnlyOnChange()
        {
            int changes = 0;
            progress.OnToolChanged += _ => changes++;

            progress.SetLevel(2);
            Assert.That(progress.CurrentLevel, Is.EqualTo(2));
            Assert.That(changes, Is.EqualTo(1));

            progress.SetLevel(2);
            Assert.That(changes, Is.EqualTo(1), "等级没变就不广播");

            progress.SetLevel(99);
            Assert.That(progress.CurrentLevel, Is.EqualTo(3));

            progress.SetLevel(-4);
            Assert.That(progress.CurrentLevel, Is.EqualTo(1));
            Assert.That(currency.CurrentMoney, Is.EqualTo(100), "读档恢复不扣金币");
        }

        // ---------- 费用查询 ----------

        [Test]
        public void GetUpgradeCostForLevels_SumsAndCaps()
        {
            Assert.That(progress.GetUpgradeCostForLevels(1), Is.EqualTo(15));
            Assert.That(progress.GetUpgradeCostForLevels(5), Is.EqualTo(45), "超过上限时按实际级数算");
            Assert.That(progress.GetUpgradeCostForLevels(0), Is.EqualTo(0));
            Assert.That(progress.GetUpgradeCostForLevels(-1), Is.EqualTo(0));

            progress.SetLevel(3);
            Assert.That(progress.GetUpgradeCostForLevels(1), Is.EqualTo(0), "满级后没有费用");
        }

        [Test]
        public void GetAffordableUpgradeCount_StopsAtMoneyLimit()
        {
            Assert.That(progress.GetAffordableUpgradeCount(14), Is.EqualTo(0));
            Assert.That(progress.GetAffordableUpgradeCount(15), Is.EqualTo(1));
            Assert.That(progress.GetAffordableUpgradeCount(44), Is.EqualTo(1));
            Assert.That(progress.GetAffordableUpgradeCount(45), Is.EqualTo(2));
            Assert.That(progress.GetAffordableUpgradeCount(999), Is.EqualTo(2), "最多到满级");

            progress.SetLevel(3);
            Assert.That(progress.GetAffordableUpgradeCount(999), Is.EqualTo(0));
        }
    }
}
