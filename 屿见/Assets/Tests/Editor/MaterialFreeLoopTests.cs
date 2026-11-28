using NUnit.Framework;
using UnityEngine;
using Yujian.Building;
using Yujian.Core;
using Yujian.Player;
using Yujian.Shop;

namespace Yujian.Tests
{
    /// <summary>
    /// 原料退出核心循环后的规则：建筑一律花金币、建筑栏只看金币、材料进不了蓝图。
    ///
    /// 这些用例保护的是 2026-11-24 修掉的那个死锁：
    /// 建筑栏在运行时会自动把 MaterialInventory 接进 BuildingUI，于是玩家只要没买过原料，
    /// 所有建筑按钮都会因为「原料不足」被置灰——金币建造再好也点不进去。
    /// </summary>
    public class MaterialFreeLoopTests : WhiteBoxTestBase
    {
        private BuildingData building;
        private BuildingShopCatalog catalog;
        private PlayerCurrency currency;
        private BuildingGoldShop shop;
        private BuildingUI buildingUI;

        [SetUp]
        public void CreateWorld()
        {
            GameFeatureFlags.ResetToDefault();

            building = Track(ScriptableObject.CreateInstance<BuildingData>());
            building.name = "测试楼";
            TestReflect.Set(building, "buildingName", "测试楼");
            TestReflect.Set(building, "unlockLevel", 1);

            catalog = Track(ScriptableObject.CreateInstance<BuildingShopCatalog>());
            TestReflect.Set(catalog, "entries", new[]
            {
                new BuildingShopEntry(building, 80),
            });

            currency = TestFactory.MakeCurrency(NewGameObject("Currency"), 100);

            GameObject shopHost = NewGameObject("BuildingGoldShop");
            shopHost.SetActive(false);
            shop = shopHost.AddComponent<BuildingGoldShop>();
            TestReflect.Set(shop, "currency", currency);
            TestReflect.Set(shop, "catalog", catalog);
            shopHost.SetActive(true);

            GameObject uiHost = NewGameObject("BuildingUI");
            buildingUI = uiHost.AddComponent<BuildingUI>();
            TestReflect.Set(buildingUI, "goldShop", shop);
            TestReflect.Set(buildingUI, "availableBuildings", new[] { building });
        }

        [TearDown]
        public void ResetFeatureFlags()
        {
            GameFeatureFlags.ResetToDefault();
        }

        [Test]
        public void MaterialFree_IsTheDefault()
        {
            Assert.That(GameFeatureFlags.MaterialsEnabled, Is.False);
            Assert.That(GameFeatureFlags.MaterialFreeLoop, Is.True);
            Assert.That(shop.IsGoldModeActive, Is.True);
        }

        [Test]
        public void MaterialFree_KeepsGoldModeEvenWhenInspectorSwitchIsOff()
        {
            TestReflect.Set(shop, "useGoldPurchase", false);

            Assert.That(shop.IsEnabled, Is.False, "开关本身仍是关的");
            Assert.That(shop.IsGoldModeActive, Is.True, "但原料没了，金币是唯一路径");
            Assert.That(shop.TryGetPrice(building, out int price), Is.True);
            Assert.That(price, Is.EqualTo(80));
        }

        [Test]
        public void BuildingBar_AvailableAndShowsPrice_WhenAffordable()
        {
            BuildingAvailabilityInfo info = buildingUI.Evaluate(0);

            Assert.That(info.State, Is.EqualTo(BuildingAvailabilityState.Available));
            Assert.That(info.Price, Is.EqualTo(80));
            Assert.That(info.JustEnoughMaterials, Is.False, "原料已剔除，不该再出现「原料刚好够」");
        }

        [Test]
        public void BuildingBar_InsufficientGold_GraysOutWithReason()
        {
            TestReflect.Set(shop, "currency", TestFactory.MakeCurrency(NewGameObject("Poor"), 10));

            BuildingAvailabilityInfo info = buildingUI.Evaluate(0);

            Assert.That(info.State, Is.EqualTo(BuildingAvailabilityState.InsufficientGold));
            Assert.That(info.Price, Is.EqualTo(80));
            Assert.That(info.Reason, Does.Contain("80"));
            Assert.That(info.IsAvailable, Is.False);
        }

        [Test]
        public void BuildingBar_FreeBuilding_IsAvailableAtZeroMoney()
        {
            TestReflect.Set(catalog, "entries", new[]
            {
                new BuildingShopEntry(building, 0),
            });

            TestReflect.Set(shop, "currency", TestFactory.MakeCurrency(NewGameObject("Broke"), 0));

            BuildingAvailabilityInfo info = buildingUI.Evaluate(0);

            Assert.That(info.State, Is.EqualTo(BuildingAvailabilityState.Available));
            Assert.That(info.Price, Is.EqualTo(0));
        }

        [Test]
        public void BuildingBar_LevelLockWinsOverGold()
        {
            TestReflect.Set(building, "unlockLevel", 3);

            PlayerLevel level = NewGameObject("PlayerLevel").AddComponent<PlayerLevel>();
            TestReflect.Set(level, "currentLevel", 1);
            TestReflect.Set(buildingUI, "playerLevel", level);

            BuildingAvailabilityInfo info = buildingUI.Evaluate(0);

            Assert.That(info.State, Is.EqualTo(BuildingAvailabilityState.LevelLocked),
                "等级不够时应该先提示等级，而不是先喊没钱");
            Assert.That(info.Reason, Does.Contain("3"));
        }

        [Test]
        public void BuildingBar_NoGoldShop_IsInvalidNotSilentlyFree()
        {
            TestReflect.Set(buildingUI, "goldShop", null);

            // 光清引用不够：Evaluate 会 FindFirstObjectByType 把场景里的商店再找回来。
            // 要测「真的没有商店」，只能把它从场景里拿掉。
            Object.DestroyImmediate(shop.gameObject);

            BuildingAvailabilityInfo info = buildingUI.Evaluate(0);

            Assert.That(info.State, Is.EqualTo(BuildingAvailabilityState.Invalid));
            Assert.That(info.IsAvailable, Is.False);
        }

        [Test]
        public void MaterialFree_RefusesMaterialPlacement()
        {
            BuildingConstruction construction =
                NewGameObject("BuildingConstruction").AddComponent<BuildingConstruction>();

            bool accepted = construction.CanAcceptMaterial(
                TestFactory.MakeMaterial("小砖", "brick"), out string reason);

            Assert.That(accepted, Is.False);
            Assert.That(reason, Does.Contain("原料系统已关闭"));
        }

        [Test]
        public void MaterialsEnabled_RestoresMaterialGating()
        {
            GameFeatureFlags.MaterialsEnabled = true;

            MaterialData brick = Track(TestFactory.MakeMaterial("小砖", "brick", price: 1, damageBonus: 0.1f));
            BuildingData withRecipe = Track(TestFactory.MakeBuilding(
                "需要砖的建筑", "b_material", Vector2Int.one,
                new BuildingMaterialRequirement(brick, 3)));

            TestReflect.Set(buildingUI, "availableBuildings", new[] { withRecipe });
            TestReflect.Set(buildingUI, "inventory", TestFactory.MakeInventory(NewGameObject("Inventory")));

            BuildingAvailabilityInfo missing = buildingUI.Evaluate(0);
            Assert.That(missing.State, Is.EqualTo(BuildingAvailabilityState.MissingMaterial),
                "原料系统重新开启后，缺料必须重新拦住玩家");

            MaterialInventory stocked = TestFactory.MakeInventory(
                NewGameObject("Stocked"), (brick, 3));
            TestReflect.Set(buildingUI, "inventory", stocked);

            BuildingAvailabilityInfo ready = buildingUI.Evaluate(0);
            Assert.That(ready.State, Is.EqualTo(BuildingAvailabilityState.Available));
            Assert.That(ready.JustEnoughMaterials, Is.True);
            Assert.That(ready.Price, Is.EqualTo(0), "原料模式不该显示金币价");
        }
    }
}
