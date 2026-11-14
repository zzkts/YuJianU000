using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yujian.Building;
using Yujian.Mining;
using Yujian.Player;
using Yujian.Save;
using Yujian.Shop;

namespace Yujian.Tests
{
    /// <summary>
    /// 各 SaveParticipant 的 Capture / Restore 契约：
    /// 写进存档的是稳定 ID 与普通数值，读档时能解析的回填、解析不了的跳过且不崩。
    /// </summary>
    public class SaveParticipantTests : WhiteBoxTestBase
    {
        // ---------- 金币 ----------

        [Test]
        public void Currency_CaptureAndRestore()
        {
            PlayerCurrency currency = TestFactory.MakeCurrency(NewGameObject("Currency"), 50);

            CurrencySaveParticipant participant =
                AddComponent<CurrencySaveParticipant>(NewGameObject("CurrencyParticipant"));
            TestReflect.Set(participant, "currency", currency);

            SaveData data = NewValidSaveData();
            participant.Capture(data);
            Assert.That(data.currency, Is.EqualTo(50));

            data.currency = 77;
            participant.Restore(data);
            Assert.That(currency.CurrentMoney, Is.EqualTo(77));
        }

        [Test]
        public void Currency_CaptureWithoutDependency_WritesZero()
        {
            LogAssert.ignoreFailingMessages = true;

            CurrencySaveParticipant participant =
                AddComponent<CurrencySaveParticipant>(NewGameObject("CurrencyParticipant"));

            SaveData data = NewValidSaveData();
            data.currency = 99;
            participant.Capture(data);

            Assert.That(data.currency, Is.EqualTo(0));
        }

        // ---------- 库存 ----------

        [Test]
        public void Inventory_CaptureWritesStableIds()
        {
            MaterialData brick = Track(TestFactory.MakeMaterial("砖块", "mat_brick"));
            MaterialData wood = Track(TestFactory.MakeMaterial("木板", "mat_wood", type: MaterialType.Wood));
            SaveCatalog catalog = Track(TestFactory.MakeCatalog(new[] { brick, wood }));

            MaterialInventory inventory = TestFactory.MakeInventory(
                NewGameObject("Inventory"), (brick, 3), (wood, 2));

            InventorySaveParticipant participant =
                AddComponent<InventorySaveParticipant>(NewGameObject("InventoryParticipant"));
            TestReflect.Set(participant, "inventory", inventory);
            TestReflect.Set(participant, "catalog", catalog);

            SaveData data = NewValidSaveData();
            participant.Capture(data);

            Assert.That(data.materials.Count, Is.EqualTo(2));
            Assert.That(data.materials[0].materialId, Is.EqualTo("mat_brick"));
            Assert.That(data.materials[0].count, Is.EqualTo(3));
            Assert.That(data.materials[1].materialId, Is.EqualTo("mat_wood"));
            Assert.That(data.materials[1].count, Is.EqualTo(2));
        }

        [Test]
        public void Inventory_RestoreMergesDuplicateIds()
        {
            MaterialData brick = Track(TestFactory.MakeMaterial("砖块", "mat_brick"));
            SaveCatalog catalog = Track(TestFactory.MakeCatalog(new[] { brick }));

            MaterialInventory inventory = TestFactory.MakeInventory(NewGameObject("Inventory"));

            InventorySaveParticipant participant =
                AddComponent<InventorySaveParticipant>(NewGameObject("InventoryParticipant"));
            TestReflect.Set(participant, "inventory", inventory);
            TestReflect.Set(participant, "catalog", catalog);

            SaveData data = NewValidSaveData();
            // 旧存档可能按颜色拆成两条，读档必须合并求和
            data.materials.Add(new MaterialSave { materialId = "mat_brick", count = 2 });
            data.materials.Add(new MaterialSave { materialId = "mat_brick", count = 5 });

            participant.Restore(data);

            Assert.That(inventory.Stacks.Count, Is.EqualTo(1));
            Assert.That(inventory.GetMaterialCount(brick), Is.EqualTo(7));
        }

        [Test]
        public void Inventory_RestoreSkipsUnknownIdAndNonPositiveCount()
        {
            LogAssert.ignoreFailingMessages = true;

            MaterialData brick = Track(TestFactory.MakeMaterial("砖块", "mat_brick"));
            SaveCatalog catalog = Track(TestFactory.MakeCatalog(new[] { brick }));

            MaterialInventory inventory = TestFactory.MakeInventory(NewGameObject("Inventory"));

            InventorySaveParticipant participant =
                AddComponent<InventorySaveParticipant>(NewGameObject("InventoryParticipant"));
            TestReflect.Set(participant, "inventory", inventory);
            TestReflect.Set(participant, "catalog", catalog);

            SaveData data = NewValidSaveData();
            data.materials.Add(new MaterialSave { materialId = "mat_brick", count = 1 });
            data.materials.Add(new MaterialSave { materialId = "mat_missing", count = 9 });
            data.materials.Add(new MaterialSave { materialId = "mat_brick", count = 0 });

            participant.Restore(data);

            Assert.That(inventory.GetMaterialCount(brick), Is.EqualTo(1));
            Assert.That(inventory.GetTotalCount(), Is.EqualTo(1));
        }

        // ---------- 玩家等级 ----------

        [Test]
        public void PlayerLevel_RestoreOnlyRaisesNeverLowers()
        {
            PlayerLevel level = AddComponent<PlayerLevel>(NewGameObject("PlayerLevel"));
            PlayerLevelSaveParticipant participant =
                AddComponent<PlayerLevelSaveParticipant>(NewGameObject("LevelParticipant"));
            TestReflect.Set(participant, "playerLevel", level);

            SaveData data = NewValidSaveData();
            data.playerHighestLevel = 5;
            participant.Restore(data);
            Assert.That(level.HighestLevel, Is.EqualTo(5));

            data.playerHighestLevel = 2;
            participant.Restore(data);
            Assert.That(level.HighestLevel, Is.EqualTo(5));

            data.playerHighestLevel = 0;
            participant.Restore(data);
            Assert.That(level.HighestLevel, Is.EqualTo(5));
        }

        [Test]
        public void PlayerLevel_CaptureWritesHighest()
        {
            PlayerLevel level = AddComponent<PlayerLevel>(NewGameObject("PlayerLevel"));
            level.RestoreHighestLevel(6);

            PlayerLevelSaveParticipant participant =
                AddComponent<PlayerLevelSaveParticipant>(NewGameObject("LevelParticipant"));
            TestReflect.Set(participant, "playerLevel", level);

            SaveData data = NewValidSaveData();
            participant.Capture(data);

            Assert.That(data.playerHighestLevel, Is.EqualTo(6));
        }

        // ---------- 建筑楼层材料 ----------

        [Test]
        public void Building_RestoreLayerMaterials_V2FallsBackToRecipeOrder()
        {
            MaterialData brick = Track(TestFactory.MakeMaterial("砖块", "mat_brick"));
            MaterialData wood = Track(TestFactory.MakeMaterial("木板", "mat_wood", type: MaterialType.Wood));
            SaveCatalog catalog = Track(TestFactory.MakeCatalog(new[] { brick, wood }));

            BuildingData building = Track(TestFactory.MakeBuilding(
                "风琴博物馆", "bld_museum", new Vector2Int(2, 2),
                new BuildingMaterialRequirement(brick, 3),
                new BuildingMaterialRequirement(wood, 1)));

            BuildingSaveParticipant participant =
                AddComponent<BuildingSaveParticipant>(NewGameObject("BuildingParticipant"));
            TestReflect.Set(participant, "catalog", catalog);

            BuildingSave legacy = new BuildingSave
            {
                buildingId = "bld_museum",
                layerMaterialIds = new List<string>(),
            };

            var materials = (List<MaterialData>)TestReflect.Invoke(
                participant, "RestoreLayerMaterials", building, legacy);

            Assert.That(materials, Is.EqualTo(new[] { brick, brick, brick, wood }));
        }

        [Test]
        public void Building_RestoreLayerMaterials_V3KeepsEmptySlotsAligned()
        {
            LogAssert.ignoreFailingMessages = true;

            MaterialData brick = Track(TestFactory.MakeMaterial("砖块", "mat_brick"));
            SaveCatalog catalog = Track(TestFactory.MakeCatalog(new[] { brick }));

            BuildingData building = Track(TestFactory.MakeBuilding(
                "风琴博物馆", "bld_museum", new Vector2Int(2, 2),
                new BuildingMaterialRequirement(brick, 3)));

            BuildingSaveParticipant participant =
                AddComponent<BuildingSaveParticipant>(NewGameObject("BuildingParticipant"));
            TestReflect.Set(participant, "catalog", catalog);

            BuildingSave save = new BuildingSave
            {
                buildingId = "bld_museum",
                layerMaterialIds = new List<string> { "mat_brick", "", "mat_missing" },
            };

            var materials = (List<MaterialData>)TestReflect.Invoke(
                participant, "RestoreLayerMaterials", building, save);

            Assert.That(materials.Count, Is.EqualTo(3));
            Assert.That(materials[0], Is.SameAs(brick));
            Assert.That(materials[1], Is.Null);
            Assert.That(materials[2], Is.Null);
        }

        // ---------- 蓝图退款 ----------

        [Test]
        public void Blueprint_CaptureWritesPlacementHistory()
        {
            LogAssert.ignoreFailingMessages = true;

            MaterialData brick = Track(TestFactory.MakeMaterial("砖块", "mat_brick"));
            SaveCatalog catalog = Track(TestFactory.MakeCatalog(new[] { brick }));

            BuildingConstruction construction =
                AddComponent<BuildingConstruction>(NewGameObject("Construction"));

            var history = (List<BuildingConstruction.PlacedMaterial>)TestReflect.GetObject(
                construction, "placementHistory");
            history.Add(new BuildingConstruction.PlacedMaterial(brick));
            history.Add(new BuildingConstruction.PlacedMaterial(brick));

            BlueprintSaveParticipant participant =
                AddComponent<BlueprintSaveParticipant>(NewGameObject("BlueprintParticipant"));
            TestReflect.Set(participant, "construction", construction);
            TestReflect.Set(participant, "catalog", catalog);

            SaveData data = NewValidSaveData();
            participant.Capture(data);

            Assert.That(data.blueprintRefund.Count, Is.EqualTo(2));
            Assert.That(data.blueprintRefund[0].materialId, Is.EqualTo("mat_brick"));
            Assert.That(data.blueprintRefund[0].count, Is.EqualTo(1));
        }

        [Test]
        public void Blueprint_RestoreRefundsMaterials()
        {
            LogAssert.ignoreFailingMessages = true;

            MaterialData brick = Track(TestFactory.MakeMaterial("砖块", "mat_brick"));
            SaveCatalog catalog = Track(TestFactory.MakeCatalog(new[] { brick }));

            MaterialInventory inventory = TestFactory.MakeInventory(NewGameObject("Inventory"));

            BlueprintSaveParticipant participant =
                AddComponent<BlueprintSaveParticipant>(NewGameObject("BlueprintParticipant"));
            TestReflect.Set(participant, "inventory", inventory);
            TestReflect.Set(participant, "catalog", catalog);

            SaveData data = NewValidSaveData();
            data.blueprintRefund.Add(new MaterialSave { materialId = "mat_brick", count = 2 });

            participant.Restore(data);

            Assert.That(inventory.GetMaterialCount(brick), Is.EqualTo(2));
        }

        // ---------- 挖矿工具等级 ----------

        [Test]
        public void MiningTool_CaptureAndRestoreLevel()
        {
            LogAssert.ignoreFailingMessages = true;

            MiningToolData toolData = Track(ScriptableObject.CreateInstance<MiningToolData>());
            PlayerCurrency currency = TestFactory.MakeCurrency(NewGameObject("Currency"));

            MiningToolProgress progress =
                AddComponent<MiningToolProgress>(NewGameObject("ToolProgress"));
            progress.ConfigureForSetup(toolData, currency, 1);
            progress.SetLevel(3);

            MiningToolProgressSaveParticipant participant =
                AddComponent<MiningToolProgressSaveParticipant>(NewGameObject("ToolParticipant"));
            TestReflect.Set(participant, "toolProgress", progress);

            SaveData data = NewValidSaveData();
            participant.Capture(data);
            Assert.That(data.mining.toolLevel, Is.EqualTo(3));

            data.mining.toolLevel = 5;
            participant.Restore(data);
            Assert.That(progress.CurrentLevel, Is.EqualTo(5));

            // 旧存档缺字段（0）时按 1 级处理
            data.mining.toolLevel = 0;
            participant.Restore(data);
            Assert.That(progress.CurrentLevel, Is.EqualTo(1));
        }
    }
}
