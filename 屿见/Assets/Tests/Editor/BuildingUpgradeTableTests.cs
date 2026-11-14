using NUnit.Framework;
using UnityEngine;
using Yujian.Building;

namespace Yujian.Tests
{
    /// <summary>
    /// 建筑升级表：效果倍率、单步费用、总费用、Max 模式目标级、可连升级数。
    /// 用一份小数值配置（max 5、间隔 2 / 4、成长 2 倍）把每一档都钉死，
    /// 避免只验证“和公式一样”却验不出常量写错。
    /// </summary>
    public class BuildingUpgradeTableTests : WhiteBoxTestBase
    {
        private BuildingUpgradeTable table;

        [SetUp]
        public void CreateTable()
        {
            table = Track(ScriptableObject.CreateInstance<BuildingUpgradeTable>());

            TestReflect.Set(table, "maxLevel", 5);
            TestReflect.Set(table, "valueGrowthPerLevel", 0.1f);
            TestReflect.Set(table, "interval", 2);
            TestReflect.Set(table, "intervalValueMultiplier", 2f);
            TestReflect.Set(table, "majorInterval", 4);
            TestReflect.Set(table, "majorValueMultiplier", 3f);
            TestReflect.Set(table, "baseCost", 10);
            TestReflect.Set(table, "costGrowthPerLevel", 2f);
            TestReflect.Set(table, "intervalCostMultiplier", 3f);
            TestReflect.Set(table, "majorCostMultiplier", 5f);
        }

        // ---------- 默认资产值 ----------

        [Test]
        public void Defaults_MatchDesignTable()
        {
            BuildingUpgradeTable defaults = Track(ScriptableObject.CreateInstance<BuildingUpgradeTable>());

            Assert.That(defaults.TableId, Is.EqualTo("LevelUP_01"));
            Assert.That(defaults.MaxLevel, Is.EqualTo(50));
            Assert.That(defaults.Interval, Is.EqualTo(10));
            Assert.That(defaults.MajorInterval, Is.EqualTo(50));

            Assert.That(defaults.GetEffectMultiplier(1), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(defaults.GetEffectMultiplier(2), Is.EqualTo(1.08f).Within(1e-4f));
            Assert.That(defaults.GetUpgradeCost(1), Is.EqualTo(15));
        }

        [Test]
        public void TableId_FallsBackToAssetName()
        {
            TestReflect.Set(table, "tableId", string.Empty);

            Assert.That(table.TableId, Is.EqualTo(table.name));
        }

        // ---------- 效果倍率 ----------

        [Test]
        public void EffectMultiplier_Level1IsAlwaysOne()
        {
            Assert.That(table.GetEffectMultiplier(1), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void EffectMultiplier_ClampsLowLevelsToOne()
        {
            Assert.That(table.GetEffectMultiplier(0), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(table.GetEffectMultiplier(-7), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void EffectMultiplier_LinearGrowthPlusIntervalAndMajor()
        {
            // 2 级：1.1 × 2^(2/2)
            Assert.That(table.GetEffectMultiplier(2), Is.EqualTo(2.2f).Within(1e-4f));

            // 3 级：1.2 × 2^(3/2 向下取整=1)
            Assert.That(table.GetEffectMultiplier(3), Is.EqualTo(2.4f).Within(1e-4f));

            // 4 级：1.3 × 2^2 × 3^1（大间隔优先，同时命中 2 和 4 时小间隔次数仍按整除算）
            Assert.That(table.GetEffectMultiplier(4), Is.EqualTo(15.6f).Within(1e-3f));

            // 5 级：1.4 × 2^2 × 3^1
            Assert.That(table.GetEffectMultiplier(5), Is.EqualTo(16.8f).Within(1e-3f));
        }

        [Test]
        public void EffectMultiplier_ClampsHighLevelsToMax()
        {
            Assert.That(table.GetEffectMultiplier(99), Is.EqualTo(table.GetEffectMultiplier(5)).Within(1e-4f));
        }

        // ---------- 升级费用 ----------

        [Test]
        public void UpgradeCost_MilestoneLevelsCostMore()
        {
            // 1 → 2：10 × 2^0，目标 2 是间隔级 → ×3
            Assert.That(table.GetUpgradeCost(1), Is.EqualTo(30));

            // 2 → 3：10 × 2^1，普通级
            Assert.That(table.GetUpgradeCost(2), Is.EqualTo(20));

            // 3 → 4：10 × 2^2，目标 4 是大间隔 → ×5（不再叠乘小间隔 3）
            Assert.That(table.GetUpgradeCost(3), Is.EqualTo(200));

            // 4 → 5：10 × 2^3，普通级
            Assert.That(table.GetUpgradeCost(4), Is.EqualTo(80));
        }

        [Test]
        public void UpgradeCost_AtMaxLevel_IsZero()
        {
            Assert.That(table.GetUpgradeCost(5), Is.EqualTo(0));
            Assert.That(table.GetUpgradeCost(6), Is.EqualTo(0));
        }

        [Test]
        public void TotalCost_SumsEveryStep()
        {
            Assert.That(table.GetTotalCost(1, 5), Is.EqualTo(330)); // 30 + 20 + 200 + 80
            Assert.That(table.GetTotalCost(1, 1), Is.EqualTo(0));
            Assert.That(table.GetTotalCost(3, 3), Is.EqualTo(0));
        }

        [Test]
        public void TotalCost_ClampsTargetToMaxAndIgnoresReverseRange()
        {
            Assert.That(table.GetTotalCost(1, 99), Is.EqualTo(330));
            Assert.That(table.GetTotalCost(4, 1), Is.EqualTo(0));
        }

        // ---------- Max 模式 ----------

        [Test]
        public void MaxModeTarget_GoesToNextInterval()
        {
            Assert.That(table.GetMaxModeTargetLevel(1), Is.EqualTo(2));
            Assert.That(table.GetMaxModeTargetLevel(2), Is.EqualTo(4), "正好在间隔上时去下一个间隔");
            Assert.That(table.GetMaxModeTargetLevel(3), Is.EqualTo(4));
            Assert.That(table.GetMaxModeTargetLevel(4), Is.EqualTo(5), "下一个间隔超过上限时取上限");
            Assert.That(table.GetMaxModeTargetLevel(5), Is.EqualTo(5));
        }

        // ---------- 可连升级数 ----------

        [Test]
        public void AffordableLevelCount_StopsBeforeUnaffordableStep()
        {
            Assert.That(table.GetAffordableLevelCount(1, 5, 29), Is.EqualTo(0));
            Assert.That(table.GetAffordableLevelCount(1, 5, 30), Is.EqualTo(1));
            Assert.That(table.GetAffordableLevelCount(1, 5, 49), Is.EqualTo(1));
            Assert.That(table.GetAffordableLevelCount(1, 5, 50), Is.EqualTo(2));
            Assert.That(table.GetAffordableLevelCount(1, 5, 249), Is.EqualTo(2));
            Assert.That(table.GetAffordableLevelCount(1, 5, 250), Is.EqualTo(3));
            Assert.That(table.GetAffordableLevelCount(1, 5, 329), Is.EqualTo(3));
            Assert.That(table.GetAffordableLevelCount(1, 5, 330), Is.EqualTo(4));
        }

        [Test]
        public void AffordableLevelCount_RespectsTarget()
        {
            Assert.That(table.GetAffordableLevelCount(1, 2, 9999), Is.EqualTo(1));
        }
    }
}
