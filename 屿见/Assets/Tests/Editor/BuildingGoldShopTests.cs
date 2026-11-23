using NUnit.Framework;
using UnityEngine;
using Yujian.Building;
using Yujian.Player;
using Yujian.Shop;

namespace Yujian.Tests
{
    /// <summary>
    /// 隔离商店系统的核心规则：建筑查价、余额判断、扣款、退款、模式开关。
    /// 不测 BuildingConstruction 的蓝图流程（那需要完整场景与相机），只测金币商店这一层。
    /// </summary>
    public class BuildingGoldShopTests : WhiteBoxTestBase
    {
        private BuildingData building;
        private BuildingShopCatalog catalog;
        private PlayerCurrency currency;
        private BuildingGoldShop shop;

        [SetUp]
        public void CreateShop()
        {
            building = Track(ScriptableObject.CreateInstance<BuildingData>());
            building.name = "测试建筑";
            TestReflect.Set(building, "buildingName", "测试建筑");

            catalog = Track(ScriptableObject.CreateInstance<BuildingShopCatalog>());
            TestReflect.Set(catalog, "entries", new[]
            {
                new BuildingShopEntry(building, 80),
            });

            currency = TestFactory.MakeCurrency(NewGameObject("Currency"), 100);

            // 先在未激活的物体上挂组件，把依赖注入完再激活，避免 Awake 读到空引用
            GameObject shopHost = NewGameObject("BuildingGoldShop");
            shopHost.SetActive(false);
            shop = shopHost.AddComponent<BuildingGoldShop>();
            TestReflect.Set(shop, "currency", currency);
            TestReflect.Set(shop, "catalog", catalog);
            shopHost.SetActive(true);
        }

        [Test]
        public void EnabledByDefault_AndFindsConfiguredPrice()
        {
            Assert.That(shop.IsEnabled, Is.True);
            Assert.That(shop.TryGetPrice(building, out int price), Is.True);
            Assert.That(price, Is.EqualTo(80));
        }

        [Test]
        public void UnknownBuilding_HasNoPrice()
        {
            BuildingData unknown = Track(ScriptableObject.CreateInstance<BuildingData>());

            Assert.That(shop.TryGetPrice(unknown, out int price), Is.False);
            Assert.That(price, Is.EqualTo(0));
        }

        [Test]
        public void NullBuilding_HasNoPrice()
        {
            Assert.That(shop.TryGetPrice(null, out int price), Is.False);
            Assert.That(price, Is.EqualTo(0));
        }

        [Test]
        public void CanAfford_ComparesAgainstCurrentMoney()
        {
            Assert.That(currency.CurrentMoney, Is.EqualTo(100));
            Assert.That(shop.CanAfford(80), Is.True);
            Assert.That(shop.CanAfford(100), Is.True);
            Assert.That(shop.CanAfford(101), Is.False);
        }

        [Test]
        public void CanAfford_FreePrice_IsAlwaysTrue()
        {
            Assert.That(shop.CanAfford(0), Is.True);
            Assert.That(shop.CanAfford(-5), Is.True);
        }

        [Test]
        public void TrySpend_DeductsMoney()
        {
            Assert.That(shop.TrySpend(30), Is.True);
            Assert.That(currency.CurrentMoney, Is.EqualTo(70));
        }

        [Test]
        public void TrySpend_Insufficient_ChangesNothing()
        {
            Assert.That(shop.TrySpend(101), Is.False);
            Assert.That(currency.CurrentMoney, Is.EqualTo(100));
        }

        [Test]
        public void TrySpend_Free_DoesNotChangeMoney()
        {
            Assert.That(shop.TrySpend(0), Is.True);
            Assert.That(currency.CurrentMoney, Is.EqualTo(100));
        }

        [Test]
        public void Refund_AddsMoneyBack()
        {
            shop.TrySpend(80);

            shop.Refund(80);

            Assert.That(currency.CurrentMoney, Is.EqualTo(100));
        }

        [Test]
        public void DisabledSwitch_ReportsDisabledAndHasNoPrices()
        {
            TestReflect.Set(shop, "useGoldPurchase", false);

            Assert.That(shop.IsEnabled, Is.False);
            Assert.That(shop.TryGetPrice(building, out _), Is.False);
        }
    }
}
