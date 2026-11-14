using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;
using Yujian.Save;

namespace Yujian.Tests
{
    /// <summary>JsonUtility 存档序列化层的往返与坏输入测试。</summary>
    public class JsonSaveSerializerTests : WhiteBoxTestBase
    {
        private JsonSaveSerializer serializer;

        [SetUp]
        public void CreateSerializer()
        {
            serializer = new JsonSaveSerializer();
        }

        [Test]
        public void Serialize_Null_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => serializer.Serialize(null));
        }

        [Test]
        public void Deserialize_NullOrWhitespace_ReturnsNull()
        {
            LogAssert.ignoreFailingMessages = true;

            Assert.That(serializer.Deserialize(null), Is.Null);
            Assert.That(serializer.Deserialize(string.Empty), Is.Null);
            Assert.That(serializer.Deserialize("   "), Is.Null);
        }

        [Test]
        public void Deserialize_InvalidJson_ReturnsNull()
        {
            LogAssert.ignoreFailingMessages = true;

            Assert.That(serializer.Deserialize("这不是 JSON"), Is.Null);
            Assert.That(serializer.Deserialize("{ \"currency\": "), Is.Null);
        }

        [Test]
        public void Deserialize_EmptyObject_NormalizesCollections()
        {
            SaveData data = serializer.Deserialize("{}");

            Assert.That(data, Is.Not.Null);
            Assert.That(data.version, Is.EqualTo(0));
            Assert.That(data.materials, Is.Not.Null);
            Assert.That(data.buildings, Is.Not.Null);
            Assert.That(data.mining, Is.Not.Null);
            Assert.That(data.fragments, Is.Not.Null);
        }

        [Test]
        public void Roundtrip_PreservesAllFields()
        {
            SaveData original = new SaveData
            {
                version = SaveData.CurrentVersion,
                savedAtUtc = 638000000000000000L,
                currency = 1234,
                playerHighestLevel = 7,
            };

            original.materials.Add(new MaterialSave { materialId = "brick", count = 3 });
            original.blueprintRefund.Add(new MaterialSave { materialId = "wood", count = 1 });
            original.buildings.Add(new BuildingSave
            {
                buildingId = "museum",
                x = -2,
                y = 4,
                width = 2,
                height = 2,
                level = 4,
                layerMaterialIds = new List<string> { "brick", "brick", "wood", "" },
            });
            original.mining.tier = 3;
            original.mining.toolLevel = 5;
            original.mining.lightToolLevel = 2;
            original.mining.tiers.Add(new MiningTierSave
            {
                tier = 1,
                entryIds = new List<string> { "qingshi", "hongzhuan" },
            });
            original.fragments.totalCollected = 9;
            original.fragments.unlockedIndices.Add(0);
            original.fragments.unlockedIndices.Add(2);

            string json = serializer.Serialize(original);
            SaveData restored = serializer.Deserialize(json);

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.version, Is.EqualTo(original.version));
            Assert.That(restored.savedAtUtc, Is.EqualTo(original.savedAtUtc));
            Assert.That(restored.currency, Is.EqualTo(1234));
            Assert.That(restored.playerHighestLevel, Is.EqualTo(7));
            Assert.That(restored.materials.Count, Is.EqualTo(1));
            Assert.That(restored.materials[0].materialId, Is.EqualTo("brick"));
            Assert.That(restored.materials[0].count, Is.EqualTo(3));
            Assert.That(restored.blueprintRefund.Count, Is.EqualTo(1));
            Assert.That(restored.buildings.Count, Is.EqualTo(1));
            Assert.That(restored.buildings[0].x, Is.EqualTo(-2));
            Assert.That(restored.buildings[0].level, Is.EqualTo(4));
            Assert.That(restored.buildings[0].layerMaterialIds.Count, Is.EqualTo(4));
            Assert.That(restored.buildings[0].layerMaterialIds[3], Is.Empty);
            Assert.That(restored.mining.tier, Is.EqualTo(3));
            Assert.That(restored.mining.toolLevel, Is.EqualTo(5));
            Assert.That(restored.mining.lightToolLevel, Is.EqualTo(2));
            Assert.That(restored.mining.tiers.Count, Is.EqualTo(1));
            Assert.That(restored.mining.tiers[0].entryIds, Is.EqualTo(new[] { "qingshi", "hongzhuan" }));
            Assert.That(restored.fragments.totalCollected, Is.EqualTo(9));
            Assert.That(restored.fragments.unlockedIndices, Is.EqualTo(new[] { 0, 2 }));
        }

        [Test]
        public void Serialize_IsPrettyPrintedStableFormat()
        {
            SaveData data = NewValidSaveData();

            string first = serializer.Serialize(data);
            string second = serializer.Serialize(data);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Does.Contain("\n"));
        }

        [Test]
        public void Deserialize_ChineseMaterialId_Roundtrips()
        {
            SaveData original = NewValidSaveData();
            original.materials.Add(new MaterialSave { materialId = "原料-砖块", count = 2 });

            SaveData restored = serializer.Deserialize(serializer.Serialize(original));

            Assert.That(restored.materials[0].materialId, Is.EqualTo("原料-砖块"));
        }
    }
}
