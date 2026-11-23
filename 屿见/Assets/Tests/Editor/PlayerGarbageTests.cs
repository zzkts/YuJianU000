using NUnit.Framework;
using UnityEngine;
using Yujian.Core.Events;
using Yujian.Player;
using Yujian.Save;

namespace Yujian.Tests
{
    /// <summary>垃圾资源的增减、原子扣款与事件广播（与金币同一条规则）。</summary>
    public class PlayerGarbageTests : WhiteBoxTestBase
    {
        private PlayerGarbage garbage;

        [SetUp]
        public void CreateGarbage()
        {
            garbage = TestFactory.MakeGarbage(NewGameObject("Garbage"));
        }

        [Test]
        public void StartsAtZero()
        {
            Assert.That(garbage.CurrentGarbage, Is.EqualTo(0));
        }

        [Test]
        public void AddGarbage_IncreasesAndBroadcasts()
        {
            int eventValue = 0;
            int busAmount = 0;
            int busDelta = 0;

            garbage.OnGarbageChanged += value => eventValue = value;
            EventBus.Subscribe<GarbageChanged>(evt =>
            {
                busAmount = evt.Amount;
                busDelta = evt.Delta;
            });

            garbage.AddGarbage(30);

            Assert.That(garbage.CurrentGarbage, Is.EqualTo(30));
            Assert.That(eventValue, Is.EqualTo(30));
            Assert.That(busAmount, Is.EqualTo(30));
            Assert.That(busDelta, Is.EqualTo(30));
        }

        [Test]
        public void AddGarbage_NonPositive_IsIgnored()
        {
            garbage.AddGarbage(10);
            garbage.AddGarbage(0);
            garbage.AddGarbage(-5);

            Assert.That(garbage.CurrentGarbage, Is.EqualTo(10));
        }

        [Test]
        public void TrySpend_Sufficient_DecreasesAndReturnsTrue()
        {
            garbage.AddGarbage(20);

            Assert.That(garbage.TrySpendGarbage(8), Is.True);
            Assert.That(garbage.CurrentGarbage, Is.EqualTo(12));
        }

        [Test]
        public void TrySpend_Insufficient_ChangesNothing()
        {
            garbage.AddGarbage(5);

            Assert.That(garbage.TrySpendGarbage(6), Is.False);
            Assert.That(garbage.CurrentGarbage, Is.EqualTo(5));
        }

        [Test]
        public void TrySpend_NonPositive_IsRejected()
        {
            garbage.AddGarbage(5);

            Assert.That(garbage.TrySpendGarbage(0), Is.False);
            Assert.That(garbage.TrySpendGarbage(-1), Is.False);
            Assert.That(garbage.CurrentGarbage, Is.EqualTo(5));
        }

        [Test]
        public void RestoreGarbage_ClampsNegativeAndBroadcasts()
        {
            int eventValue = -1;
            int busCount = 0;

            garbage.OnGarbageChanged += value => eventValue = value;
            EventBus.Subscribe<GarbageChanged>(_ => busCount++);

            garbage.RestoreGarbage(-3);

            Assert.That(garbage.CurrentGarbage, Is.EqualTo(0));
            Assert.That(eventValue, Is.EqualTo(0));
            Assert.That(busCount, Is.EqualTo(0), "读档恢复不应发布 EventBus 事件");
        }

        [Test]
        public void GarbageSaveParticipant_CapturesAndRestores()
        {
            garbage.AddGarbage(30);

            GameObject host = NewGameObject("Save");
            GarbageSaveParticipant participant = AddComponent<GarbageSaveParticipant>(host);
            TestReflect.Set(participant, "garbage", garbage);

            SaveData data = NewValidSaveData();
            participant.Capture(data);

            Assert.That(data.garbage, Is.EqualTo(30));

            PlayerGarbage restored = TestFactory.MakeGarbage(NewGameObject("Player2"));
            GarbageSaveParticipant restoreParticipant =
                AddComponent<GarbageSaveParticipant>(NewGameObject("Save2"));
            TestReflect.Set(restoreParticipant, "garbage", restored);

            restoreParticipant.Restore(data);

            Assert.That(restored.CurrentGarbage, Is.EqualTo(30));
        }

        [Test]
        public void SaveData_V4_NormalizesRecycling()
        {
            Assert.That(SaveData.CurrentVersion, Is.EqualTo(4));

            SaveData data = new SaveData();
            data.Normalize();

            Assert.That(data.recycling, Is.Not.Null);
            Assert.That(data.garbage, Is.EqualTo(0));
        }
    }
}
