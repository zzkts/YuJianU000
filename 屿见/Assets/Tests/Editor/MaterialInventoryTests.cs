using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;
using Yujian.Core.Events;
using Yujian.Shop;

namespace Yujian.Tests
{
    /// <summary>库存唯一真相源的增减、合并、原子性与事件。</summary>
    public class MaterialInventoryTests : WhiteBoxTestBase
    {
        private MaterialData brick;
        private MaterialData wood;
        private MaterialInventory inventory;

        [SetUp]
        public void CreateInventory()
        {
            brick = Track(TestFactory.MakeMaterial("砖块", "mat_brick"));
            wood = Track(TestFactory.MakeMaterial("木板", "mat_wood", type: MaterialType.Wood));
            inventory = TestFactory.MakeInventory(NewGameObject("Inventory"));
        }

        [Test]
        public void AddMaterial_NewMaterial_CreatesStack()
        {
            inventory.AddMaterial(brick, 2);

            Assert.That(inventory.Stacks.Count, Is.EqualTo(1));
            Assert.That(inventory.GetMaterialCount(brick), Is.EqualTo(2));
        }

        [Test]
        public void AddMaterial_SameMaterial_MergesIntoOneStack()
        {
            inventory.AddMaterial(brick, 2);
            inventory.AddMaterial(brick, 3);

            Assert.That(inventory.Stacks.Count, Is.EqualTo(1));
            Assert.That(inventory.GetMaterialCount(brick), Is.EqualTo(5));
        }

        [Test]
        public void AddMaterial_DifferentMaterials_KeepSeparateStacks()
        {
            inventory.AddMaterial(brick, 2);
            inventory.AddMaterial(wood, 1);

            Assert.That(inventory.Stacks.Count, Is.EqualTo(2));
            Assert.That(inventory.GetTotalCount(), Is.EqualTo(3));
        }

        [Test]
        public void AddMaterial_InvalidArguments_AreIgnored()
        {
            LogAssert.ignoreFailingMessages = true;

            inventory.AddMaterial(null, 1);
            inventory.AddMaterial(brick, 0);
            inventory.AddMaterial(brick, -3);

            Assert.That(inventory.Stacks.Count, Is.EqualTo(0));
            Assert.That(inventory.GetTotalCount(), Is.EqualTo(0));
        }

        [Test]
        public void AddMaterial_RaisesLocalAndBusEvents()
        {
            int localChanges = 0;
            int busDelta = 0;
            inventory.OnInventoryChanged += () => localChanges++;
            EventBus.Subscribe<InventoryChanged>(evt => busDelta = evt.Delta);

            inventory.AddMaterial(brick, 4);

            Assert.That(localChanges, Is.EqualTo(1));
            Assert.That(busDelta, Is.EqualTo(4));
        }

        [Test]
        public void RemoveMaterial_Partial_KeepsRemainder()
        {
            inventory.AddMaterial(brick, 5);

            Assert.That(inventory.RemoveMaterial(brick, 2), Is.True);
            Assert.That(inventory.GetMaterialCount(brick), Is.EqualTo(3));
        }

        [Test]
        public void RemoveMaterial_Exact_RemovesEmptyStack()
        {
            inventory.AddMaterial(brick, 5);

            Assert.That(inventory.RemoveMaterial(brick, 5), Is.True);
            Assert.That(inventory.GetMaterialCount(brick), Is.EqualTo(0));
            Assert.That(inventory.Stacks.Count, Is.EqualTo(0));
        }

        [Test]
        public void RemoveMaterial_Insufficient_ChangesNothing()
        {
            inventory.AddMaterial(brick, 2);

            Assert.That(inventory.RemoveMaterial(brick, 3), Is.False);
            Assert.That(inventory.GetMaterialCount(brick), Is.EqualTo(2));
        }

        [Test]
        public void RemoveMaterial_NotOwned_ReturnsFalse()
        {
            Assert.That(inventory.RemoveMaterial(brick, 1), Is.False);
            Assert.That(inventory.GetTotalCount(), Is.EqualTo(0));
        }

        [Test]
        public void RemoveMaterial_InvalidArguments_ReturnFalse()
        {
            LogAssert.ignoreFailingMessages = true;

            Assert.That(inventory.RemoveMaterial(null, 1), Is.False);
            Assert.That(inventory.RemoveMaterial(brick, 0), Is.False);
        }

        [Test]
        public void RemoveMaterial_PublishesNegativeDelta()
        {
            inventory.AddMaterial(brick, 5);
            int delta = 0;
            EventBus.Subscribe<InventoryChanged>(evt => delta = evt.Delta);

            inventory.RemoveMaterial(brick, 2);

            Assert.That(delta, Is.EqualTo(-2));
        }

        [Test]
        public void HasMaterial_ZeroAmount_IsAlwaysTrue()
        {
            Assert.That(inventory.HasMaterial(brick, 0), Is.True);
            Assert.That(inventory.HasMaterial(brick, -1), Is.True);
            Assert.That(inventory.HasMaterial(brick, 1), Is.False);
        }

        [Test]
        public void GetMaterialCount_Null_ReturnsZero()
        {
            Assert.That(inventory.GetMaterialCount(null), Is.EqualTo(0));
        }

        [Test]
        public void RestoreStacks_Null_ClearsInventory()
        {
            inventory.AddMaterial(brick, 3);

            inventory.RestoreStacks(null);

            Assert.That(inventory.GetTotalCount(), Is.EqualTo(0));
        }

        [Test]
        public void RestoreStacks_MergesDuplicateMaterials()
        {
            var snapshot = new List<MaterialStack>
            {
                new MaterialStack(brick, 2),
                new MaterialStack(wood, 1),
                new MaterialStack(brick, 3),
            };

            inventory.RestoreStacks(snapshot);

            Assert.That(inventory.Stacks.Count, Is.EqualTo(2));
            Assert.That(inventory.GetMaterialCount(brick), Is.EqualTo(5));
            Assert.That(inventory.GetMaterialCount(wood), Is.EqualTo(1));
        }

        [Test]
        public void RestoreStacks_SkipsInvalidEntries()
        {
            LogAssert.ignoreFailingMessages = true;

            var snapshot = new List<MaterialStack>
            {
                new MaterialStack(null, 5),
                new MaterialStack(brick, 0),
                new MaterialStack(wood, 2),
            };

            inventory.RestoreStacks(snapshot);

            Assert.That(inventory.Stacks.Count, Is.EqualTo(1));
            Assert.That(inventory.GetMaterialCount(wood), Is.EqualTo(2));
        }

        [Test]
        public void RestoreStacks_RaisesOnlyLocalEvent_NotBus()
        {
            int localChanges = 0;
            bool busPublished = false;
            inventory.OnInventoryChanged += () => localChanges++;
            EventBus.Subscribe<InventoryChanged>(_ => busPublished = true);

            inventory.RestoreStacks(new List<MaterialStack> { new MaterialStack(brick, 1) });

            Assert.That(localChanges, Is.EqualTo(1));
            Assert.That(busPublished, Is.False);
        }

        [Test]
        public void MergeDuplicateStacks_CompactsLegacyColorEntries()
        {
            // 模拟旧场景里按颜色拆开的多条记录
            var legacy = new List<MaterialStack>
            {
                new MaterialStack(brick, 2),
                new MaterialStack(brick, 3),
                new MaterialStack(null, 9),
                new MaterialStack(wood, 0),
            };

            TestReflect.Set(inventory, "stacks", legacy);
            TestReflect.Invoke(inventory, "MergeDuplicateStacks");

            var refreshed = new List<MaterialStack>(inventory.Stacks);
            Assert.That(refreshed.Count, Is.EqualTo(1));
            Assert.That(refreshed[0].Material, Is.SameAs(brick));
            Assert.That(refreshed[0].Count, Is.EqualTo(5));
        }
    }
}
