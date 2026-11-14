using NUnit.Framework;
using Yujian.Core.Events;
using Yujian.Player;

namespace Yujian.Tests
{
    /// <summary>货币增减、原子扣款与事件广播。</summary>
    public class PlayerCurrencyTests : WhiteBoxTestBase
    {
        private PlayerCurrency currency;

        [SetUp]
        public void CreateCurrency()
        {
            currency = TestFactory.MakeCurrency(NewGameObject("Currency"));
        }

        [Test]
        public void StartsAtZero_WhenConfiguredZero()
        {
            Assert.That(currency.CurrentMoney, Is.EqualTo(0));
        }

        [Test]
        public void AddMoney_IncreasesAndBroadcasts()
        {
            int eventValue = 0;
            int busDelta = 0;
            int busAmount = 0;

            currency.OnMoneyChanged += value => eventValue = value;
            EventBus.Subscribe<MoneyChanged>(evt =>
            {
                busAmount = evt.Amount;
                busDelta = evt.Delta;
            });

            currency.AddMoney(30);

            Assert.That(currency.CurrentMoney, Is.EqualTo(30));
            Assert.That(eventValue, Is.EqualTo(30));
            Assert.That(busAmount, Is.EqualTo(30));
            Assert.That(busDelta, Is.EqualTo(30));
        }

        [Test]
        public void AddMoney_NonPositive_IsIgnored()
        {
            currency.AddMoney(10);
            currency.AddMoney(0);
            currency.AddMoney(-5);

            Assert.That(currency.CurrentMoney, Is.EqualTo(10));
        }

        [Test]
        public void TrySpend_Sufficient_DecreasesAndReturnsTrue()
        {
            currency.AddMoney(20);

            Assert.That(currency.TrySpendMoney(8), Is.True);
            Assert.That(currency.CurrentMoney, Is.EqualTo(12));
        }

        [Test]
        public void TrySpend_Insufficient_ChangesNothing()
        {
            currency.AddMoney(5);

            Assert.That(currency.TrySpendMoney(6), Is.False);
            Assert.That(currency.CurrentMoney, Is.EqualTo(5));
        }

        [Test]
        public void TrySpend_NonPositive_IsRejected()
        {
            currency.AddMoney(5);

            Assert.That(currency.TrySpendMoney(0), Is.False);
            Assert.That(currency.TrySpendMoney(-3), Is.False);
            Assert.That(currency.CurrentMoney, Is.EqualTo(5));
        }

        [Test]
        public void RestoreMoney_SetsExactValue()
        {
            currency.AddMoney(5);

            currency.RestoreMoney(88);

            Assert.That(currency.CurrentMoney, Is.EqualTo(88));
        }

        [Test]
        public void RestoreMoney_Negative_ClampsToZero()
        {
            currency.AddMoney(5);

            currency.RestoreMoney(-100);

            Assert.That(currency.CurrentMoney, Is.EqualTo(0));
        }

        [Test]
        public void RestoreMoney_DoesNotPublishBusEvent()
        {
            // 读档不是“赚到钱”，不能触发金币音效
            bool published = false;
            EventBus.Subscribe<MoneyChanged>(_ => published = true);

            currency.RestoreMoney(50);

            Assert.That(published, Is.False);
        }

        [Test]
        public void RestoreMoney_StillRaisesLocalEvent()
        {
            int eventValue = -1;
            currency.OnMoneyChanged += value => eventValue = value;

            currency.RestoreMoney(50);

            Assert.That(eventValue, Is.EqualTo(50));
        }
    }
}
