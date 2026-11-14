using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yujian.Core.Events;
using Yujian.Player;
using Yujian.Shop;
// Yujian.Tests 里裸写 Shop 会被解析成命名空间 Yujian.Shop，这里用别名指名组件
using ShopComponent = Yujian.Shop.Shop;

namespace Yujian.Tests
{
    /// <summary>购买入口的原子性、免费原料、失败原因与事件。</summary>
    public class ShopTests : WhiteBoxTestBase
    {
        private PlayerCurrency currency;
        private MaterialInventory inventory;
        private ShopComponent shop;
        private MaterialData brick;   // 单价 2
        private MaterialData free;    // 单价 0

        [SetUp]
        public void CreateShop()
        {
            // Shop.Awake 在依赖注入前执行时会报错，这是构造顺序问题，不是被测行为
            LogAssert.ignoreFailingMessages = true;

            currency = TestFactory.MakeCurrency(NewGameObject("Currency"), 100);
            inventory = TestFactory.MakeInventory(NewGameObject("Inventory"));
            brick = Track(TestFactory.MakeMaterial("砖块", "mat_brick", price: 2));
            free = Track(TestFactory.MakeMaterial("赠品砖", "mat_free", price: 0));

            shop = AddComponent<ShopComponent>(NewGameObject("Shop"));
            TestReflect.Set(shop, "playerCurrency", currency);
            TestReflect.Set(shop, "inventory", inventory);
        }

        [Test]
        public void TryBuy_SufficientMoney_DeductsAndAdds()
        {
            Assert.That(shop.TryBuy(brick, 3), Is.True);

            Assert.That(currency.CurrentMoney, Is.EqualTo(94));
            Assert.That(inventory.GetMaterialCount(brick), Is.EqualTo(3));
        }

        [Test]
        public void TryBuy_PublishesSucceededWithCost()
        {
            int amount = 0;
            int totalCost = -1;
            EventBus.Subscribe<PurchaseSucceeded>(evt =>
            {
                amount = evt.Amount;
                totalCost = evt.TotalCost;
            });

            Assert.That(shop.TryBuy(brick, 2), Is.True);

            Assert.That(amount, Is.EqualTo(2));
            Assert.That(totalCost, Is.EqualTo(4));
        }

        [Test]
        public void TryBuy_InsufficientMoney_ChangesNothing()
        {
            string reason = null;
            EventBus.Subscribe<PurchaseFailed>(evt => reason = evt.Reason);

            Assert.That(shop.TryBuy(brick, 1000), Is.False);

            Assert.That(currency.CurrentMoney, Is.EqualTo(100));
            Assert.That(inventory.GetTotalCount(), Is.EqualTo(0));
            Assert.That(reason, Is.EqualTo("垃圾不足"));
        }

        [Test]
        public void TryBuy_NullMaterial_Fails()
        {
            string reason = null;
            EventBus.Subscribe<PurchaseFailed>(evt => reason = evt.Reason);

            // 空原料会打一条明确的 Error，白盒测试要确认错误类型与来源
            LogAssert.Expect(LogType.Error, new Regex("TryBuy.*MaterialData"));

            Assert.That(shop.TryBuy(null, 1), Is.False);
            Assert.That(reason, Does.Contain("为空"));
        }

        [Test]
        public void TryBuy_NonPositiveAmount_Fails()
        {
            string reason = null;
            EventBus.Subscribe<PurchaseFailed>(evt => reason = evt.Reason);

            Assert.That(shop.TryBuy(brick, 0), Is.False);
            Assert.That(shop.TryBuy(brick, -2), Is.False);
            Assert.That(currency.CurrentMoney, Is.EqualTo(100));
            Assert.That(reason, Is.EqualTo("购买数量无效"));
        }

        [Test]
        public void TryBuy_FreeMaterial_DoesNotTouchMoney()
        {
            Assert.That(shop.TryBuy(free, 4), Is.True);

            Assert.That(currency.CurrentMoney, Is.EqualTo(100));
            Assert.That(inventory.GetMaterialCount(free), Is.EqualTo(4));
        }

        [Test]
        public void TryBuy_MissingDependencies_FailsWithoutThrowing()
        {
            ShopComponent incomplete = AddComponent<ShopComponent>(NewGameObject("IncompleteShop"));
            string reason = null;
            EventBus.Subscribe<PurchaseFailed>(evt => reason = evt.Reason);

            Assert.That(incomplete.TryBuy(brick, 1), Is.False);
            Assert.That(reason, Is.EqualTo("商店依赖未配置"));
        }

        [Test]
        public void TryBuy_FailureDoesNotRaiseSucceeded()
        {
            bool succeeded = false;
            EventBus.Subscribe<PurchaseSucceeded>(_ => succeeded = true);

            shop.TryBuy(brick, 1000);

            Assert.That(succeeded, Is.False);
        }
    }
}
