using NUnit.Framework;
using UnityEngine.TestTools;
using Yujian.Mining;
using Yujian.Player;

namespace Yujian.Tests
{
    /// <summary>
    /// 照明工具（2026-11-26 改版）：无限等级、超级暴击概率/倍率、里程碑花费。
    ///
    /// 核心契约：
    ///   · 等级没有上限（maxLevel = 999），不再有「6 件装备」这回事；
    ///   · 每 10 级涨一次超级暴击概率（封顶 36%），倍率每级都涨；
    ///   · 升到 10/20/30… 级那一档要花 4 倍金币。
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

            currency = TestFactory.MakeCurrency(NewGameObject("Currency"), 100000);
            tool = AddComponent<MiningLightTool>(NewGameObject("LightTool"));
            tool.ConfigureForSetup(currency);
        }

        // ---------- 未拥有状态 ----------

        [Test]
        public void Default_StartsUnowned()
        {
            Assert.That(tool.Level, Is.EqualTo(0));
            Assert.That(tool.MaxLevel, Is.EqualTo(999), "无限升级：上限只是防溢出");
            Assert.That(tool.IsUnlocked, Is.False);
            Assert.That(tool.IsMaxed, Is.False);

            Assert.That(tool.LevelName, Is.EqualTo("未拥有"));
            Assert.That(tool.CritPointCapacity, Is.EqualTo(0));
            Assert.That(tool.RefreshCooldown, Is.EqualTo(0f));
            Assert.That(tool.CritPointDamageMultiplier, Is.EqualTo(1f));
            Assert.That(tool.CanSpawnCritPoints, Is.False);

            Assert.That(tool.SuperCritChance, Is.EqualTo(0f), "没有照明工具就不该有超级暴击");
        }

        [Test]
        public void Level1_UnlocksCritPointsAndSuperCrit()
        {
            tool.SetLevel(1);

            Assert.That(tool.IsUnlocked, Is.True);
            Assert.That(tool.CanSpawnCritPoints, Is.True);
            Assert.That(tool.CritPointCapacity, Is.EqualTo(1));
            Assert.That(tool.RefreshCooldown, Is.EqualTo(5f));
            Assert.That(tool.CritPointDamageMultiplier, Is.EqualTo(2f), "普通暴击固定 2 倍");

            Assert.That(tool.SuperCritChance, Is.EqualTo(0.08f).Within(0.0001f));
            Assert.That(tool.SuperCritMultiplier, Is.EqualTo(5f).Within(0.0001f));
        }

        [Test]
        public void Milestone10_RaisesChanceAndMultiplier()
        {
            tool.SetLevel(10);

            // 概率：8% + 7% × 1 = 15%
            Assert.That(tool.SuperCritChance, Is.EqualTo(0.15f).Within(0.0001f));

            // 倍率：5 + 0.15 × 9 + 1 × 1 = 7.35
            Assert.That(tool.SuperCritMultiplier, Is.EqualTo(7.35f).Within(0.01f));

            // 覆盖率封顶在第 6 档的数值
            Assert.That(tool.CritPointCapacity, Is.EqualTo(3));
            Assert.That(tool.RefreshCooldown, Is.EqualTo(3f));
        }

        [Test]
        public void Level40_ChanceCapsButMultiplierKeepsGrowing()
        {
            tool.SetLevel(40);

            Assert.That(tool.SuperCritChance, Is.EqualTo(0.36f).Within(0.0001f), "概率封顶 36%");
            Assert.That(tool.SuperCritMultiplier, Is.EqualTo(14.85f).Within(0.01f));

            tool.SetLevel(60);

            Assert.That(tool.SuperCritChance, Is.EqualTo(0.36f).Within(0.0001f), "封顶后不再涨");
            Assert.That(tool.SuperCritMultiplier, Is.GreaterThan(16f), "倍率无上限");
        }

        [Test]
        public void Costs_GrowGeometrically_AndSpikeOnMilestones()
        {
            Assert.That(tool.GetUpgradeCostAt(0), Is.EqualTo(4), "0 → 1 级只要 4 金币");

            int normal = tool.GetUpgradeCostAt(8);   // 8 → 9
            int milestone = tool.GetUpgradeCostAt(9); // 9 → 10（里程碑）

            // 里程碑是「基础花费再 ×4」，而基础花费本身还带一级成长，所以比值略大于 4
            Assert.That(milestone, Is.GreaterThan(normal * 4), "里程碑那一档要明显更贵");
            Assert.That(tool.GetUpgradeCostAt(999), Is.EqualTo(0), "到顶后没有下一级");
        }

        [Test]
        public void Upgrade_SpendsMoneyAndUnlocks()
        {
            int before = currency.CurrentMoney;

            Assert.That(tool.TryUpgrade(), Is.True);

            Assert.That(tool.Level, Is.EqualTo(1));
            Assert.That(currency.CurrentMoney, Is.EqualTo(before - 4));
        }

        [Test]
        public void Upgrade_StopsWhenMoneyIsShort()
        {
            PlayerCurrency broke = TestFactory.MakeCurrency(NewGameObject("Broke"), 0);
            tool.ConfigureForSetup(broke);

            Assert.That(tool.TryUpgrade(), Is.False);
            Assert.That(tool.Level, Is.EqualTo(0));
        }

        [Test]
        public void MaxMode_SkipsMilestoneInOneGo()
        {
            tool.SetLevel(8);
            int spentBefore = currency.CurrentMoney;

            // 8 → 10 连升两级，第二级是里程碑
            Assert.That(tool.TryUpgrade(2), Is.True);
            Assert.That(tool.Level, Is.EqualTo(10));
            Assert.That(currency.CurrentMoney, Is.LessThan(spentBefore - tool.GetUpgradeCostAt(8)));
        }
    }
}
