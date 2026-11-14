using NUnit.Framework;
using UnityEngine;
using Yujian.Building;
using Yujian.Shop;

namespace Yujian.Tests
{
    /// <summary>数据资产的展示文案：原料效果描述、建筑材料需求文本。</summary>
    public class DataDescriptionTests : WhiteBoxTestBase
    {
        // ---------- MaterialData.EffectDescription ----------

        [Test]
        public void EffectDescription_NoBonus()
        {
            MaterialData material = Track(TestFactory.MakeMaterial("砖块", "mat_brick",
                damageBonus: 0f, buildingBonus: 0f));

            Assert.That(material.EffectDescription, Is.EqualTo("无额外效果"));
        }

        [Test]
        public void EffectDescription_PlayerBonusOnly()
        {
            MaterialData material = Track(TestFactory.MakeMaterial("砖块", "mat_brick",
                damageBonus: 0.1f, buildingBonus: 0f));

            Assert.That(material.EffectDescription,
                Is.EqualTo("玩家伤害倍率 +0.1（建成后生效）"));
        }

        [Test]
        public void EffectDescription_BuildingBonusOnly()
        {
            MaterialData material = Track(TestFactory.MakeMaterial("砖块", "mat_brick",
                damageBonus: 0f, buildingBonus: 0.5f));

            Assert.That(material.EffectDescription,
                Is.EqualTo("建筑伤害倍率 +0.5（建成后生效）"));
        }

        [Test]
        public void EffectDescription_BothBonuses()
        {
            MaterialData material = Track(TestFactory.MakeMaterial("砖块", "mat_brick",
                damageBonus: 0.1f, buildingBonus: 0.5f));

            Assert.That(material.EffectDescription,
                Is.EqualTo("玩家伤害倍率 +0.1，建筑伤害倍率 +0.5（建成后生效）"));
        }

        [Test]
        public void EffectDescription_NegativeBonus()
        {
            MaterialData material = Track(TestFactory.MakeMaterial("砖块", "mat_brick",
                damageBonus: -0.1f, buildingBonus: 0f));

            Assert.That(material.EffectDescription,
                Is.EqualTo("玩家伤害倍率 -0.1（建成后生效）"));
        }

        // ---------- BuildingData.GetRequirementText ----------

        [Test]
        public void RequirementText_NoRequirements()
        {
            BuildingData building = Track(TestFactory.MakeBuilding(
                "博物馆", "building_museum", new Vector2Int(2, 2)));

            Assert.That(building.GetRequirementText(), Is.EqualTo("无"));
            Assert.That(building.RequiredMaterials.Count, Is.EqualTo(0));
        }

        [Test]
        public void RequirementText_JoinsValidEntries()
        {
            MaterialData brick = Track(TestFactory.MakeMaterial("砖块", "mat_brick"));
            MaterialData wood = Track(TestFactory.MakeMaterial("木板", "mat_wood",
                type: MaterialType.Wood));

            BuildingData building = Track(TestFactory.MakeBuilding(
                "博物馆", "building_museum", new Vector2Int(2, 2),
                new BuildingMaterialRequirement(brick, 3),
                new BuildingMaterialRequirement(wood, 1)));

            Assert.That(building.GetRequirementText(), Is.EqualTo("砖块 ×3、木板 ×1"));
        }

        [Test]
        public void RequirementText_SkipsInvalidEntries()
        {
            MaterialData brick = Track(TestFactory.MakeMaterial("砖块", "mat_brick"));

            BuildingData building = Track(TestFactory.MakeBuilding(
                "博物馆", "building_museum", new Vector2Int(2, 2),
                new BuildingMaterialRequirement(null, 3),
                new BuildingMaterialRequirement(brick, 0),
                new BuildingMaterialRequirement(brick, 2)));

            Assert.That(building.GetRequirementText(), Is.EqualTo("砖块 ×2"));
        }

        [Test]
        public void RequirementText_AllInvalid_ReturnsNone()
        {
            BuildingData building = Track(TestFactory.MakeBuilding(
                "博物馆", "building_museum", new Vector2Int(2, 2),
                new BuildingMaterialRequirement(null, 1)));

            Assert.That(building.GetRequirementText(), Is.EqualTo("无"));
        }

        [Test]
        public void HasValidFootprint_RequiresBothAxesPositive()
        {
            Assert.That(Track(TestFactory.MakeBuilding("A", "a", new Vector2Int(2, 2)))
                .HasValidFootprint, Is.True);
            Assert.That(Track(TestFactory.MakeBuilding("B", "b", new Vector2Int(0, 2)))
                .HasValidFootprint, Is.False);
            Assert.That(Track(TestFactory.MakeBuilding("C", "c", new Vector2Int(2, -1)))
                .HasValidFootprint, Is.False);
        }

        [Test]
        public void Requirement_IsValid_RequiresMaterialAndPositiveAmount()
        {
            MaterialData brick = Track(TestFactory.MakeMaterial("砖块", "mat_brick"));

            Assert.That(new BuildingMaterialRequirement(brick, 1).IsValid, Is.True);
            Assert.That(new BuildingMaterialRequirement(brick, 0).IsValid, Is.False);
            Assert.That(new BuildingMaterialRequirement(null, 1).IsValid, Is.False);
        }
    }
}
