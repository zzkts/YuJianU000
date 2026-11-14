using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yujian.Mining;

namespace Yujian.Tests
{
    /// <summary>挖矿数值曲线：逐层成长、奖励取整、工具攻击/升级费用的几何与线性两种模式。</summary>
    public class MiningNumbersTests : WhiteBoxTestBase
    {
        private OreLevelTable levelTable;

        [SetUp]
        public void CreateLevelTable()
        {
            levelTable = Track(ScriptableObject.CreateInstance<OreLevelTable>());

            OreLevelDefinition level1 = new OreLevelDefinition();
            level1.Configure(1, new List<OreGroup>());

            TestReflect.Set(levelTable, "levels", new List<OreLevelDefinition> { level1 });
            TestReflect.Set(levelTable, "healthGrowthPerTier", 6f);
            TestReflect.Set(levelTable, "rewardGrowthPerTier", 5f);
        }

        // ---------- 矿石等级表 ----------

        [Test]
        public void HealthMultiplier_IsGeometric()
        {
            Assert.That(levelTable.GetHealthMultiplier(1), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(levelTable.GetHealthMultiplier(2), Is.EqualTo(6f).Within(1e-4f));
            Assert.That(levelTable.GetHealthMultiplier(3), Is.EqualTo(36f).Within(1e-3f));
            Assert.That(levelTable.GetHealthMultiplier(6), Is.EqualTo(7776f).Within(1e-2f));
        }

        [Test]
        public void HealthMultiplier_ClampsTierToOne()
        {
            Assert.That(levelTable.GetHealthMultiplier(0), Is.EqualTo(1f));
            Assert.That(levelTable.GetHealthMultiplier(-5), Is.EqualTo(1f));
        }

        [Test]
        public void Growth_IsClampedToOne()
        {
            TestReflect.Set(levelTable, "healthGrowthPerTier", 0.1f);
            TestReflect.Set(levelTable, "rewardGrowthPerTier", 0f);

            Assert.That(levelTable.HealthGrowthPerTier, Is.EqualTo(1f));
            Assert.That(levelTable.RewardGrowthPerTier, Is.EqualTo(1f));
            Assert.That(levelTable.GetHealthMultiplier(4), Is.EqualTo(1f));
        }

        [Test]
        public void RewardAtTier_RoundsAndScales()
        {
            Assert.That(levelTable.GetRewardAtTier(11, 1), Is.EqualTo(11));
            Assert.That(levelTable.GetRewardAtTier(11, 2), Is.EqualTo(55));
            Assert.That(levelTable.GetRewardAtTier(11, 3), Is.EqualTo(275));
            Assert.That(levelTable.GetRewardAtTier(11, 6), Is.EqualTo(34375));
        }

        [Test]
        public void RewardAtTier_NonPositiveBase_ReturnsNonNegative()
        {
            Assert.That(levelTable.GetRewardAtTier(0, 5), Is.EqualTo(0));
            Assert.That(levelTable.GetRewardAtTier(-10, 5), Is.EqualTo(0));
        }

        [Test]
        public void RewardAtTier_Overflow_Saturates()
        {
            // 预期会打一条溢出警告
            LogAssert.ignoreFailingMessages = true;

            Assert.That(levelTable.GetRewardAtTier(int.MaxValue, 6), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void TryGetLevel_FindsConfiguredLevel()
        {
            Assert.That(levelTable.TryGetLevel(1, out OreLevelDefinition found), Is.True);
            Assert.That(found.Level, Is.EqualTo(1));

            Assert.That(levelTable.TryGetLevel(2, out _), Is.False);
        }

        [Test]
        public void MaxLevel_ReflectsHighestConfigured()
        {
            OreLevelDefinition level4 = new OreLevelDefinition();
            level4.Configure(4, new List<OreGroup>());

            var levels = (List<OreLevelDefinition>)TestReflect.GetObject(levelTable, "levels");
            levels.Add(level4);

            Assert.That(levelTable.MaxLevel, Is.EqualTo(4));
        }

        // ---------- 挖矿工具 ----------

        [Test]
        public void ToolAttack_GeometricMode()
        {
            MiningToolData tool = Track(ScriptableObject.CreateInstance<MiningToolData>());
            tool.ConfigureForSetup("石头", "mine_01", 2.2f, 0, 15,
                newAttackGrowthPerLevel: 5.25f, newMaxLevel: 6);

            Assert.That(tool.GetAttackAtLevel(1), Is.EqualTo(2.2f).Within(1e-3f));
            Assert.That(tool.GetAttackAtLevel(2), Is.EqualTo(11.55f).Within(1e-2f));
            Assert.That(tool.GetAttackAtLevel(6), Is.EqualTo(2.2f * Mathf.Pow(5.25f, 5)).Within(1e-2f));
        }

        [Test]
        public void ToolAttack_ClampsLevel()
        {
            MiningToolData tool = Track(ScriptableObject.CreateInstance<MiningToolData>());
            tool.ConfigureForSetup("石头", "mine_01", 2f, 0, 15,
                newAttackGrowthPerLevel: 3f, newMaxLevel: 4);

            Assert.That(tool.GetAttackAtLevel(0), Is.EqualTo(2f));
            Assert.That(tool.GetAttackAtLevel(-10), Is.EqualTo(2f));
            Assert.That(tool.GetAttackAtLevel(99), Is.EqualTo(2f * 27f).Within(1e-3f));
        }

        [Test]
        public void ToolAttack_LinearFallback()
        {
            MiningToolData tool = Track(ScriptableObject.CreateInstance<MiningToolData>());
            tool.ConfigureForSetup("石头", "mine_01", 1f, 2, 15,
                newAttackGrowthPerLevel: 1f, newGoldCostGrowthPerLevel: 1f, newMaxLevel: 5);

            Assert.That(tool.AttackGrowthPerLevel, Is.EqualTo(1f));
            Assert.That(tool.GetAttackAtLevel(1), Is.EqualTo(1f));
            Assert.That(tool.GetAttackAtLevel(3), Is.EqualTo(5f));
            Assert.That(tool.GetAttackAtLevel(5), Is.EqualTo(9f));
        }

        [Test]
        public void ToolUpgradeCost_GeometricAndMaxLevel()
        {
            MiningToolData tool = Track(ScriptableObject.CreateInstance<MiningToolData>());
            tool.ConfigureForSetup("石头", "mine_01", 2.2f, 0, 15,
                newGoldCostGrowthPerLevel: 1.35f, newMaxLevel: 6);

            Assert.That(tool.GetUpgradeCost(1), Is.EqualTo(15));
            Assert.That(tool.GetUpgradeCost(2), Is.EqualTo(20)); // 15 × 1.35 = 20.25 → 20
            Assert.That(tool.GetUpgradeCost(5), Is.EqualTo(Mathf.RoundToInt(15f * Mathf.Pow(1.35f, 4))));
            Assert.That(tool.GetUpgradeCost(6), Is.EqualTo(0), "满级后没有下一级");
        }

        [Test]
        public void ToolUpgradeCost_LinearFallback()
        {
            MiningToolData tool = Track(ScriptableObject.CreateInstance<MiningToolData>());
            tool.ConfigureForSetup("石头", "mine_01", 1f, 1, 15,
                newGoldCostIncreasePerLevel: 10,
                newAttackGrowthPerLevel: 1f, newGoldCostGrowthPerLevel: 1f, newMaxLevel: 4);

            Assert.That(tool.GetUpgradeCost(1), Is.EqualTo(15));
            Assert.That(tool.GetUpgradeCost(2), Is.EqualTo(25));
            Assert.That(tool.GetUpgradeCost(3), Is.EqualTo(35));
            Assert.That(tool.GetUpgradeCost(4), Is.EqualTo(0));
        }

        // ---------- 垃圾种类表与条目覆盖 ----------

        [Test]
        public void Resolve_UsesTypeTableBaseValues()
        {
            OreTypeTable typeTable = Track(ScriptableObject.CreateInstance<OreTypeTable>());
            OreTypeStats stats = new OreTypeStats();
            stats.Configure(OreType.QingShi, 10f, 5);
            typeTable.Configure(new[] { stats });

            OreEntry entry = new OreEntry();
            entry.Configure(OreType.QingShi, 1f, 0f, 0);

            typeTable.Resolve(entry, 2, levelTable,
                out float baseHealth, out int baseReward, out float health, out int reward);

            Assert.That(baseHealth, Is.EqualTo(10f));
            Assert.That(baseReward, Is.EqualTo(5));
            Assert.That(health, Is.EqualTo(60f).Within(1e-3f));
            Assert.That(reward, Is.EqualTo(25));
        }

        [Test]
        public void Resolve_EntryOverridesBaseValues()
        {
            OreTypeTable typeTable = Track(ScriptableObject.CreateInstance<OreTypeTable>());
            OreTypeStats stats = new OreTypeStats();
            stats.Configure(OreType.QingShi, 10f, 5);
            typeTable.Configure(new[] { stats });

            OreEntry entry = new OreEntry();
            entry.Configure(OreType.QingShi, 1f, 100f, 50);

            typeTable.Resolve(entry, 2, levelTable,
                out float baseHealth, out int baseReward, out float health, out int reward);

            Assert.That(baseHealth, Is.EqualTo(100f));
            Assert.That(baseReward, Is.EqualTo(50));
            Assert.That(health, Is.EqualTo(600f).Within(1e-3f));
            Assert.That(reward, Is.EqualTo(250));
        }

        [Test]
        public void Resolve_PerTypeGrowth_OverridesGlobal()
        {
            OreTypeTable typeTable = Track(ScriptableObject.CreateInstance<OreTypeTable>());
            OreTypeStats stats = new OreTypeStats();
            stats.Configure(OreType.HongZhuan, 10f, 5, 3f, 2f);
            typeTable.Configure(new[] { stats });

            OreEntry entry = new OreEntry();
            entry.Configure(OreType.HongZhuan, 1f, 0f, 0);

            typeTable.Resolve(entry, 3, levelTable,
                out _, out _, out float health, out int reward);

            Assert.That(health, Is.EqualTo(90f).Within(1e-3f)); // 10 × 3²
            Assert.That(reward, Is.EqualTo(20));                // 5 × 2²
        }

        [Test]
        public void Resolve_UnknownTypeWithoutOverride_ReturnsZero()
        {
            OreTypeTable typeTable = Track(ScriptableObject.CreateInstance<OreTypeTable>());

            OreEntry entry = new OreEntry();
            entry.Configure(OreType.MuLiao, 1f, 0f, 0);

            typeTable.Resolve(entry, 2, levelTable,
                out _, out _, out float health, out int reward);

            Assert.That(health, Is.EqualTo(0f));
            Assert.That(reward, Is.EqualTo(0));
        }
    }
}
