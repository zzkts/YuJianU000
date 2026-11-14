using System.Collections.Generic;
using NUnit.Framework;
using Yujian.Save;

namespace Yujian.Tests
{
    /// <summary>存档结构校验 + FNV-1a 校验和的白盒测试。</summary>
    public class SaveIntegrityTests : WhiteBoxTestBase
    {
        // ---------- 校验和 ----------

        [Test]
        public void Compute_SameText_IsDeterministic()
        {
            Assert.That(SaveIntegrity.Compute("屿见-存档"), Is.EqualTo(SaveIntegrity.Compute("屿见-存档")));
        }

        [Test]
        public void Compute_DifferentText_ChangesHash()
        {
            Assert.That(SaveIntegrity.Compute("a"), Is.Not.EqualTo(SaveIntegrity.Compute("b")));
        }

        [Test]
        public void Compute_NullEqualsEmpty()
        {
            Assert.That(SaveIntegrity.Compute(null), Is.EqualTo(SaveIntegrity.Compute(string.Empty)));
        }

        [Test]
        public void Compute_Always16LowerHexChars()
        {
            string hash = SaveIntegrity.Compute("任意内容-123");

            Assert.That(hash, Has.Length.EqualTo(16));
            Assert.That(hash, Does.Match("^[0-9a-f]{16}$"));
        }

        // ---------- 顶层结构 ----------

        [Test]
        public void Validate_Null_Fails()
        {
            Assert.That(SaveIntegrity.Validate(null, out string error), Is.False);
            Assert.That(error, Does.Contain("空"));
        }

        [Test]
        public void Validate_UnknownVersionZero_Fails()
        {
            SaveData data = new SaveData();
            data.Normalize();

            Assert.That(SaveIntegrity.Validate(data, out string error), Is.False);
            Assert.That(error, Does.Contain("version"));
        }

        [Test]
        public void Validate_FutureVersion_Fails()
        {
            SaveData data = NewValidSaveData(SaveData.CurrentVersion + 1);

            Assert.That(SaveIntegrity.Validate(data, out string error), Is.False);
            Assert.That(error, Does.Contain("高于"));
        }

        [Test]
        public void Validate_V2WithoutSavedAtUtc_Fails()
        {
            SaveData data = NewValidSaveData(2);
            data.savedAtUtc = 0;

            Assert.That(SaveIntegrity.Validate(data, out string error), Is.False);
            Assert.That(error, Does.Contain("保存时间"));
        }

        [Test]
        public void Validate_V1WithoutSavedAtUtc_Passes()
        {
            // v1 没有 savedAtUtc 字段，不能因为它是 0 就拒绝
            SaveData data = NewValidSaveData(1);

            Assert.That(SaveIntegrity.Validate(data, out string error), Is.True, error);
        }

        [Test]
        public void Validate_NegativeCurrency_Fails()
        {
            SaveData data = NewValidSaveData();
            data.currency = -1;

            Assert.That(SaveIntegrity.Validate(data, out string error), Is.False);
            Assert.That(error, Does.Contain("金币"));
        }

        [Test]
        public void Validate_ValidMinimalSave_Passes()
        {
            Assert.That(SaveIntegrity.Validate(NewValidSaveData(), out string error), Is.True, error);
        }

        // ---------- 库存 / 蓝图退款 ----------

        [Test]
        public void Validate_MaterialNullList_Fails()
        {
            SaveData data = NewValidSaveData();
            data.materials = null;

            Assert.That(SaveIntegrity.Validate(data, out string error), Is.False);
            Assert.That(error, Does.Contain("库存"));
        }

        [Test]
        public void Validate_MaterialNullEntry_Fails()
        {
            SaveData data = NewValidSaveData();
            data.materials.Add(null);

            Assert.That(SaveIntegrity.Validate(data, out string error), Is.False);
            Assert.That(error, Does.Contain("第 0 项"));
        }

        [Test]
        public void Validate_MaterialBlankId_Fails()
        {
            SaveData data = NewValidSaveData();
            data.materials.Add(new MaterialSave { materialId = " ", count = 1 });

            Assert.That(SaveIntegrity.Validate(data, out string error), Is.False);
            Assert.That(error, Does.Contain("materialId"));
        }

        [Test]
        public void Validate_MaterialNonPositiveCount_Fails()
        {
            SaveData data = NewValidSaveData();
            data.materials.Add(new MaterialSave { materialId = "brick", count = 0 });

            Assert.That(SaveIntegrity.Validate(data, out string error), Is.False);
            Assert.That(error, Does.Contain("数量"));
        }

        [Test]
        public void Validate_BlueprintRefundIsValidatedToo()
        {
            SaveData data = NewValidSaveData();
            data.blueprintRefund.Add(new MaterialSave { materialId = "brick", count = -2 });

            Assert.That(SaveIntegrity.Validate(data, out string error), Is.False);
            Assert.That(error, Does.Contain("蓝图内材料"));
        }

        // ---------- 建筑 ----------

        [Test]
        public void Validate_BuildingValid_Passes()
        {
            SaveData data = NewValidSaveData();
            data.buildings.Add(new BuildingSave
            {
                buildingId = "museum",
                x = 0,
                y = 0,
                width = 2,
                height = 2,
                layerMaterialIds = new List<string> { "brick", "wood" },
                level = 3,
            });

            Assert.That(SaveIntegrity.Validate(data, out string error), Is.True, error);
        }

        [Test]
        public void Validate_BuildingMissingId_Fails()
        {
            SaveData data = NewValidSaveData();
            data.buildings.Add(new BuildingSave { buildingId = "", width = 2, height = 2 });

            Assert.That(SaveIntegrity.Validate(data, out string error), Is.False);
            Assert.That(error, Does.Contain("buildingId"));
        }

        [Test]
        public void Validate_BuildingInvalidFootprint_Fails()
        {
            SaveData data = NewValidSaveData();
            data.buildings.Add(new BuildingSave
            {
                buildingId = "museum",
                width = 0,
                height = 2,
                layerMaterialIds = new List<string>(),
            });

            Assert.That(SaveIntegrity.Validate(data, out string error), Is.False);
            Assert.That(error, Does.Contain("占地"));
        }

        [Test]
        public void Validate_BuildingNullLayerList_Fails()
        {
            SaveData data = NewValidSaveData();
            data.buildings.Add(new BuildingSave
            {
                buildingId = "museum",
                width = 2,
                height = 2,
                layerMaterialIds = null,
            });

            Assert.That(SaveIntegrity.Validate(data, out string error), Is.False);
            Assert.That(error, Does.Contain("楼层材料"));
        }

        // ---------- 挖矿 ----------

        [Test]
        public void Validate_MiningTierNullEntryIds_Fails()
        {
            SaveData data = NewValidSaveData();
            data.mining.tiers.Add(new MiningTierSave { tier = 1, entryIds = null });

            Assert.That(SaveIntegrity.Validate(data, out string error), Is.False);
            Assert.That(error, Does.Contain("结构不完整"));
        }

        [Test]
        public void Validate_MiningTierBlankEntryId_Fails()
        {
            SaveData data = NewValidSaveData();
            data.mining.tiers.Add(new MiningTierSave
            {
                tier = 2,
                entryIds = new List<string> { "ok", " " },
            });

            Assert.That(SaveIntegrity.Validate(data, out string error), Is.False);
            Assert.That(error, Does.Contain("第 2 层").Or.Contain("记录 ID"));
        }

        [Test]
        public void Validate_MiningNull_Fails()
        {
            SaveData data = NewValidSaveData();
            data.mining = null;

            Assert.That(SaveIntegrity.Validate(data, out string error), Is.False);
            Assert.That(error, Does.Contain("矿石"));
        }

        // ---------- 文化碎片 ----------

        [Test]
        public void Validate_FragmentNegativeIndex_Fails()
        {
            SaveData data = NewValidSaveData();
            data.fragments.unlockedIndices.Add(-1);

            Assert.That(SaveIntegrity.Validate(data, out string error), Is.False);
            Assert.That(error, Does.Contain("下标"));
        }

        [Test]
        public void Normalize_FillsAllNullCollections()
        {
            SaveData data = new SaveData
            {
                materials = null,
                buildings = null,
                mining = null,
                fragments = null,
                blueprintRefund = null,
            };

            data.Normalize();

            Assert.That(data.materials, Is.Not.Null);
            Assert.That(data.buildings, Is.Not.Null);
            Assert.That(data.mining, Is.Not.Null);
            Assert.That(data.mining.tiers, Is.Not.Null);
            Assert.That(data.fragments, Is.Not.Null);
            Assert.That(data.fragments.unlockedIndices, Is.Not.Null);
            Assert.That(data.blueprintRefund, Is.Not.Null);
        }

        [Test]
        public void Normalize_FillsBuildingLayerLists()
        {
            SaveData data = new SaveData();
            data.buildings.Add(new BuildingSave { buildingId = "museum", layerMaterialIds = null });

            data.Normalize();

            Assert.That(data.buildings[0].layerMaterialIds, Is.Not.Null);
        }
    }
}
