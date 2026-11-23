using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Yujian.Building;
using Yujian.Building.Effects;
using Yujian.Building.Recycling;
using Yujian.Core;
using Yujian.Mining;
using Yujian.Player;
using Yujian.Save;

namespace Yujian.Tests
{
    /// <summary>
    /// 回收站运行时：站点注册 / 注销、共享队列容量、拆站退料、
    /// 等级加速、全局速度、个体效率与金币到账。
    /// 系统与站点都在测试里手动初始化，不依赖 Play Mode 生命周期。
    /// </summary>
    public class RecyclingSystemTests : WhiteBoxTestBase
    {
        private RecyclingSettings settings;
        private PlayerGarbage garbage;
        private PlayerCurrency currency;
        private DamageSystem damageSystem;
        private RecyclingSystem system;

        [SetUp]
        public void CreateSystem()
        {
            settings = Track(ScriptableObject.CreateInstance<RecyclingSettings>());

            GameObject player = NewGameObject("Player");
            currency = TestFactory.MakeCurrency(player);
            garbage = TestFactory.MakeGarbage(player);
            damageSystem = AddComponent<DamageSystem>(NewGameObject("Damage"));

            GameObject host = NewGameObject("RecyclingSystem");
            system = AddComponent<RecyclingSystem>(host);
            TestReflect.Set(system, "settings", settings);
            TestReflect.Set(system, "garbage", garbage);
            TestReflect.Set(system, "currency", currency);
        }

        // ---------- 站点注册 / 容量 ----------

        [Test]
        public void RegisterStation_RaisesCapacityPerStation()
        {
            MakeStation(system, 1);

            Assert.That(system.IsReady, Is.True);
            Assert.That(system.StationCount, Is.EqualTo(1));
            Assert.That(system.Capacity, Is.EqualTo(200));
            Assert.That(system.FreeCapacity, Is.EqualTo(200));
        }

        [Test]
        public void RegisterTwoStations_CapacityAddsUp()
        {
            MakeStation(system, 1);
            MakeStation(system, 1);

            Assert.That(system.StationCount, Is.EqualTo(2));
            Assert.That(system.Capacity, Is.EqualTo(400));
        }

        [Test]
        public void RegisterSameStationTwice_DoesNotDuplicate()
        {
            RecyclingStationEffect station = MakeStation(system, 1);

            system.RegisterStation(station);

            Assert.That(system.StationCount, Is.EqualTo(1));
            Assert.That(system.Capacity, Is.EqualTo(200));
        }

        // ---------- 投料 ----------

        [Test]
        public void DepositFromPlayer_ClampsToFreeCapacity()
        {
            MakeStation(system, 1);
            garbage.AddGarbage(250);

            int accepted = system.DepositFromPlayer();

            Assert.That(accepted, Is.EqualTo(200));
            Assert.That(system.QueuedGarbage, Is.EqualTo(200));
            Assert.That(garbage.CurrentGarbage, Is.EqualTo(50), "超出容量的部分必须留在玩家手里");
        }

        [Test]
        public void DepositFromPlayer_NoStation_LeavesGarbageUntouched()
        {
            garbage.AddGarbage(10);

            int accepted = system.DepositFromPlayer();

            Assert.That(accepted, Is.EqualTo(0));
            Assert.That(system.QueuedGarbage, Is.EqualTo(0));
            Assert.That(garbage.CurrentGarbage, Is.EqualTo(10));
        }

        [Test]
        public void DepositFromPlayer_EmptyGarbage_DoesNothing()
        {
            MakeStation(system, 1);

            Assert.That(system.DepositFromPlayer(), Is.EqualTo(0));
            Assert.That(system.QueuedGarbage, Is.EqualTo(0));
        }

        // ---------- 转换 ----------

        [Test]
        public void Advance_BaseRate_ProducesOneCoinPerFiveGarbage()
        {
            MakeStation(system, 1);
            garbage.AddGarbage(5);
            system.DepositFromPlayer();

            system.Advance(1f);

            Assert.That(system.LastGarbageConverted, Is.EqualTo(5));
            Assert.That(system.QueuedGarbage, Is.EqualTo(0));
            Assert.That(currency.CurrentMoney, Is.EqualTo(1));
            Assert.That(system.TotalCoinsProduced, Is.EqualTo(1));
            Assert.That(system.CoinProgress, Is.EqualTo(0.0).Within(1e-6));
        }

        [Test]
        public void Advance_NoStations_DoesNothing()
        {
            garbage.AddGarbage(5);

            system.Advance(10f);

            Assert.That(currency.CurrentMoney, Is.EqualTo(0));
            Assert.That(garbage.CurrentGarbage, Is.EqualTo(5));
        }

        [Test]
        public void Advance_UsesUpgradeLevelMultiplier()
        {
            // 默认升级表每级 +8% 基础产能；3 级 = ×1.16 → 5.8 垃圾/秒
            MakeStation(system, 3);
            garbage.AddGarbage(6);
            system.DepositFromPlayer();

            system.Advance(1f);

            Assert.That(system.LastGarbageConverted, Is.EqualTo(5));
            Assert.That(currency.CurrentMoney, Is.EqualTo(1));
            Assert.That(system.CoinProgress, Is.EqualTo(0.0).Within(1e-5),
                "等级只提高垃圾处理速度，不改变 5:1 的转换比");
            Assert.That(system.CurrentGarbagePerSecond, Is.EqualTo(5.8f).Within(1e-5));
        }

        [Test]
        public void Advance_IndividualEfficiency_AddsFractionalCoin()
        {
            MakeStation(system, 1, efficiencyTable: MakeEfficiencyTable(0.1f));
            garbage.AddGarbage(5);
            system.DepositFromPlayer();

            system.Advance(1f);

            Assert.That(currency.CurrentMoney, Is.EqualTo(1));
            Assert.That(system.CoinProgress, Is.EqualTo(0.1).Within(1e-5),
                "5 垃圾 × 1.1 ÷ 5 = 1.1 金币，整数 1 枚、小数 0.1 留到下一批");
        }

        [Test]
        public void Advance_GlobalSpeed_AppliesToAllStations()
        {
            // 两座站中有一座选了「全局·回收速度 +20%」：
            // 总速度 (5 + 5) × 1.2 = 12 垃圾/秒；1 秒消耗 12，产出 2.4 → 2 金币 + 0.4 进度
            MakeStation(system, 1);
            MakeStation(system, 1, speedTable: MakeSpeedTable(0.2f));

            garbage.AddGarbage(12);
            system.DepositFromPlayer();

            system.Advance(1f);

            Assert.That(system.LastGarbageConverted, Is.EqualTo(12));
            Assert.That(currency.CurrentMoney, Is.EqualTo(2));
            Assert.That(system.CoinProgress, Is.EqualTo(0.4).Within(1e-5));
            Assert.That(system.CurrentGarbagePerSecond, Is.EqualTo(12f).Within(1e-5));
        }

        [Test]
        public void Advance_EmptyQueue_DoesNotAccumulateCredit()
        {
            MakeStation(system, 1);

            system.Advance(100f);

            garbage.AddGarbage(1);
            system.DepositFromPlayer();
            system.Advance(0.1f);

            Assert.That(system.QueuedGarbage, Is.EqualTo(1));
            Assert.That(system.LastGarbageConverted, Is.EqualTo(0));
        }

        // ---------- 拆站退料 ----------

        [Test]
        public void UnregisterLastStation_RefundsWholeQueue()
        {
            RecyclingStationEffect station = MakeStation(system, 1);
            garbage.AddGarbage(50);
            system.DepositFromPlayer();

            system.UnregisterStation(station);

            Assert.That(system.StationCount, Is.EqualTo(0));
            Assert.That(system.Capacity, Is.EqualTo(0));
            Assert.That(system.QueuedGarbage, Is.EqualTo(0));
            Assert.That(garbage.CurrentGarbage, Is.EqualTo(50), "拆最后一座时整条队列退回玩家");
        }

        [Test]
        public void UnregisterOneOfTwoStations_RefundsOnlyOverflow()
        {
            RecyclingStationEffect first = MakeStation(system, 1);
            MakeStation(system, 1);

            garbage.AddGarbage(300);
            system.DepositFromPlayer();

            system.UnregisterStation(first);

            Assert.That(system.Capacity, Is.EqualTo(200));
            Assert.That(system.QueuedGarbage, Is.EqualTo(200));
            Assert.That(garbage.CurrentGarbage, Is.EqualTo(100), "只退回超过新容量的 100 个");
        }

        [Test]
        public void UnregisterUnknownStation_DoesNothing()
        {
            MakeStation(system, 1);

            RecyclingStationEffect stranger = MakeStation(system, 1);
            system.UnregisterStation(stranger);
            system.UnregisterStation(stranger);

            Assert.That(system.StationCount, Is.EqualTo(1));
        }

        // ---------- 读档队列 ----------

        [Test]
        public void RestoreQueue_WaitsUntilStationsRegistered()
        {
            system.RestoreQueue(50, 0.4);

            Assert.That(system.QueuedGarbage, Is.EqualTo(0), "读档恢复前不能按 0 座站截断队列");

            MakeStation(system, 1);

            Assert.That(system.QueuedGarbage, Is.EqualTo(0), "站点注册完之前仍应挂起");

            Assert.That(system.ApplyPendingRestore(), Is.True);
            Assert.That(system.QueuedGarbage, Is.EqualTo(50));
            Assert.That(system.CoinProgress, Is.EqualTo(0.4).Within(1e-5));
        }

        [Test]
        public void RestoreQueue_UsesCapacityOfAllStations()
        {
            MakeStation(system, 1);
            MakeStation(system, 1);

            system.RestoreQueue(350, 0.2);
            system.ApplyPendingRestore();

            Assert.That(system.Capacity, Is.EqualTo(400));
            Assert.That(system.QueuedGarbage, Is.EqualTo(350));
        }

        [Test]
        public void RestoreQueue_NoStations_RefundsToPlayer()
        {
            system.RestoreQueue(50, 0.4);

            Assert.That(system.ApplyPendingRestore(), Is.True);

            Assert.That(system.QueuedGarbage, Is.EqualTo(0));
            Assert.That(garbage.CurrentGarbage, Is.EqualTo(50), "没有回收站承接时，存档里的队列垃圾要退回玩家");
        }

        [Test]
        public void RecyclingSaveParticipant_CapturesAndRestoresQueue()
        {
            MakeStation(system, 1);
            garbage.AddGarbage(50);
            system.DepositFromPlayer();

            GameObject host = NewGameObject("Save");
            RecyclingSaveParticipant participant = AddComponent<RecyclingSaveParticipant>(host);
            TestReflect.Set(participant, "recycling", system);

            SaveData data = NewValidSaveData();
            participant.Capture(data);

            Assert.That(data.recycling.queuedGarbage, Is.EqualTo(50));

            system.ResetQueue();
            participant.Restore(data);

            Assert.That(system.QueuedGarbage, Is.EqualTo(0), "读档时应先挂起，等站点注册完");

            system.ApplyPendingRestore();

            Assert.That(system.QueuedGarbage, Is.EqualTo(50));
        }

        // ---------- 辅助 ----------

        private RecyclingStationEffect MakeStation(
            RecyclingSystem target,
            int level,
            BuildingEnhancementNodeTable efficiencyTable = null,
            BuildingEnhancementNodeTable speedTable = null)
        {
            BuildingData data = Track(ScriptableObject.CreateInstance<BuildingData>());
            TestReflect.Set(data, "upgradeTable", MakeUpgradeTable(efficiencyTable, speedTable));

            GameObject go = NewGameObject($"Station{level}");
            PlacedBuildingInfo info = AddComponent<PlacedBuildingInfo>(go);

            List<string> enhancements = new List<string>();

            if (efficiencyTable != null)
            {
                enhancements.Add("Node_Huishouzhan_02");
            }

            if (speedTable != null)
            {
                enhancements.Add("Node_Huishouzhan_01");
            }

            info.Initialize(data, new RectInt(0, 0, 1, 1), null, level, enhancements);

            RecyclingStationEffect effect = AddComponent<RecyclingStationEffect>(go);

            // 效果挂在预制体上时由 GameManager 注入服务；测试里直接给一份最小上下文
            effect.Initialize(new BuildingEffectContext(go, damageSystem, currency));

            Assert.That(target.Stations, Does.Contain(effect), "站点应在初始化时自动注册到当前系统");
            return effect;
        }

        private BuildingUpgradeTable MakeUpgradeTable(
            BuildingEnhancementNodeTable efficiencyTable, BuildingEnhancementNodeTable speedTable)
        {
            BuildingUpgradeTable upgrade = Track(ScriptableObject.CreateInstance<BuildingUpgradeTable>());

            // 一张表只能挂一个节点组；测试里把效率 / 速度选项合到同一张表
            BuildingEnhancementNodeTable table = null;

            if (efficiencyTable != null && speedTable != null)
            {
                table = efficiencyTable;
                TestReflect.Set(table, "options", new[]
                {
                    MakeOption("Node_Huishouzhan_02", BuildingEnhancementScope.Individual,
                        BuildingEnhancementStat.RecyclingEfficiency, 0.1f),
                    MakeOption("Node_Huishouzhan_01", BuildingEnhancementScope.Global,
                        BuildingEnhancementStat.RecyclingSpeed, 0.2f),
                });
            }
            else
            {
                table = efficiencyTable != null ? efficiencyTable : speedTable;
            }

            if (table != null)
            {
                TestReflect.Set(upgrade, "enhancementNodeTable", table);
            }

            return upgrade;
        }

        private BuildingEnhancementNodeTable MakeEfficiencyTable(float magnitude)
        {
            BuildingEnhancementNodeTable table = Track(
                ScriptableObject.CreateInstance<BuildingEnhancementNodeTable>());

            TestReflect.Set(table, "options", new[]
            {
                MakeOption("Node_Huishouzhan_02", BuildingEnhancementScope.Individual,
                    BuildingEnhancementStat.RecyclingEfficiency, magnitude),
            });

            return table;
        }

        private BuildingEnhancementNodeTable MakeSpeedTable(float magnitude)
        {
            BuildingEnhancementNodeTable table = Track(
                ScriptableObject.CreateInstance<BuildingEnhancementNodeTable>());

            TestReflect.Set(table, "options", new[]
            {
                MakeOption("Node_Huishouzhan_01", BuildingEnhancementScope.Global,
                    BuildingEnhancementStat.RecyclingSpeed, magnitude),
            });

            return table;
        }

        private static BuildingEnhancementOption MakeOption(
            string id, BuildingEnhancementScope scope, BuildingEnhancementStat stat, float magnitude)
        {
            BuildingEnhancementOption option = new BuildingEnhancementOption();
            TestReflect.Set(option, "optionId", id);
            TestReflect.Set(option, "displayName", id);
            TestReflect.Set(option, "scope", scope);
            TestReflect.Set(option, "stat", stat);
            TestReflect.Set(option, "stackable", true);
            TestReflect.Set(option, "magnitude", magnitude);
            return option;
        }
    }

    /// <summary>建筑场上数量上限（回收站 3 座）与 BuildingData 字段。</summary>
    public class BuildingFieldLimitTests : WhiteBoxTestBase
    {
        [Test]
        public void MaxOnField_DefaultsToUnlimited()
        {
            BuildingData data = TestFactory.MakeBuilding("测试", "b1", Vector2Int.one);

            Assert.That(data.MaxOnField, Is.EqualTo(0));
            Assert.That(data.HasFieldLimit, Is.False);
        }

        [Test]
        public void CountPlaced_CountsMatchingBuildingData()
        {
            BuildingData target = TestFactory.MakeBuilding("回收站", "b1", Vector2Int.one);
            TestReflect.Set(target, "buildLimit", 3);

            BuildingManager manager = AddComponent<BuildingManager>(NewGameObject("BuildingSystem"));
            List<GameObject> placed = new List<GameObject>();

            for (int i = 0; i < 2; i++)
            {
                GameObject go = NewGameObject($"Placed{i}");
                PlacedBuildingInfo info = AddComponent<PlacedBuildingInfo>(go);
                info.Initialize(target, new RectInt(i, 0, 1, 1), null);
                placed.Add(go);
            }

            GameObject other = NewGameObject("Other");
            AddComponent<PlacedBuildingInfo>(other).Initialize(
                TestFactory.MakeBuilding("其他", "b2", Vector2Int.one), RectInt.zero, null);
            placed.Add(other);

            TestReflect.Set(manager, "placedBuildings", placed);

            Assert.That(manager.CountPlaced(target), Is.EqualTo(2));
            Assert.That(manager.CanPlaceAnother(target, out _), Is.True);
        }

        [Test]
        public void CanPlaceAnother_RejectsAtLimitWithReason()
        {
            BuildingData target = TestFactory.MakeBuilding("回收站", "b1", Vector2Int.one);
            TestReflect.Set(target, "buildLimit", 3);

            BuildingManager manager = AddComponent<BuildingManager>(NewGameObject("BuildingSystem"));
            List<GameObject> placed = new List<GameObject>();

            for (int i = 0; i < 3; i++)
            {
                GameObject go = NewGameObject($"Placed{i}");
                AddComponent<PlacedBuildingInfo>(go).Initialize(target, new RectInt(i, 0, 1, 1), null);
                placed.Add(go);
            }

            TestReflect.Set(manager, "placedBuildings", placed);

            Assert.That(manager.CanPlaceAnother(target, out string reason), Is.False);
            Assert.That(reason, Does.Contain("3/3"));
        }

        [Test]
        public void CanPlaceAnother_UnlimitedData_AlwaysTrue()
        {
            BuildingData data = TestFactory.MakeBuilding("普通建筑", "b1", Vector2Int.one);
            BuildingManager manager = AddComponent<BuildingManager>(NewGameObject("BuildingSystem"));
            List<GameObject> placed = new List<GameObject>();

            for (int i = 0; i < 20; i++)
            {
                GameObject go = NewGameObject($"Placed{i}");
                AddComponent<PlacedBuildingInfo>(go).Initialize(data, new RectInt(i, 0, 1, 1), null);
                placed.Add(go);
            }

            TestReflect.Set(manager, "placedBuildings", placed);

            Assert.That(manager.CanPlaceAnother(data, out string reason), Is.True);
            Assert.That(reason, Is.Empty);
        }
    }

    /// <summary>经济拆分：旧价格 → 金币价格的换算规则。</summary>
    public class EconomySettingsTests : WhiteBoxTestBase
    {
        private EconomySettings settings;

        [SetUp]
        public void CreateSettings()
        {
            settings = Track(ScriptableObject.CreateInstance<EconomySettings>());
        }

        [Test]
        public void Defaults_MatchDesignDecision()
        {
            Assert.That(settings.GarbagePerCoin, Is.EqualTo(5));
            Assert.That(settings.StartingCoins, Is.EqualTo(0));
            Assert.That(settings.LegacyPricesConverted, Is.False);
        }

        [Test]
        public void ToCoinPrice_DividesAndRoundsUp()
        {
            Assert.That(settings.ToCoinPrice(2), Is.EqualTo(1));
            Assert.That(settings.ToCoinPrice(5), Is.EqualTo(1));
            Assert.That(settings.ToCoinPrice(15), Is.EqualTo(3));
            Assert.That(settings.ToCoinPrice(320), Is.EqualTo(64));
            Assert.That(settings.ToCoinPrice(286), Is.EqualTo(58));
        }

        [Test]
        public void ToCoinPrice_NonPositive_StaysZero()
        {
            Assert.That(settings.ToCoinPrice(0), Is.EqualTo(0));
            Assert.That(settings.ToCoinPrice(-10), Is.EqualTo(0));
        }

        [Test]
        public void MarkLegacyPricesConverted_FlipsFlag()
        {
            settings.MarkLegacyPricesConverted();
            Assert.That(settings.LegacyPricesConverted, Is.True);
        }

        [Test]
        public void OnValidate_ClampsInvalidValues()
        {
            TestReflect.Set(settings, "garbagePerCoin", 0);
            TestReflect.Set(settings, "startingCoins", -5);

            TestReflect.Invoke(settings, "OnValidate");

            Assert.That(settings.GarbagePerCoin, Is.EqualTo(1));
            Assert.That(settings.StartingCoins, Is.EqualTo(0));
        }
    }
}
