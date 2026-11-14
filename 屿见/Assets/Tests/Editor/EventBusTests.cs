using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yujian.Core.Events;

namespace Yujian.Tests
{
    /// <summary>静态类型事件总线的订阅、退订、异常隔离测试。</summary>
    public class EventBusTests : WhiteBoxTestBase
    {
        private struct TestEvent
        {
            public int Value;
        }

        private struct OtherTestEvent
        {
            public string Text;
        }

        [Test]
        public void PublishWithoutSubscribers_DoesNothing()
        {
            Assert.That(EventBus.SubscriberCount<TestEvent>(), Is.EqualTo(0));
            Assert.DoesNotThrow(() => EventBus.Publish(new TestEvent { Value = 1 }));
        }

        [Test]
        public void SubscribeThenPublish_ReceivesEvent()
        {
            int received = 0;
            Action<TestEvent> handler = evt => received = evt.Value;

            EventBus.Subscribe(handler);
            EventBus.Publish(new TestEvent { Value = 42 });

            Assert.That(received, Is.EqualTo(42));
            Assert.That(EventBus.SubscriberCount<TestEvent>(), Is.EqualTo(1));
        }

        [Test]
        public void OnlyMatchingEventType_IsDelivered()
        {
            int matching = 0;
            int other = 0;
            Action<TestEvent> handler = _ => matching++;
            Action<OtherTestEvent> otherHandler = _ => other++;

            EventBus.Subscribe(handler);
            EventBus.Subscribe(otherHandler);
            EventBus.Publish(new TestEvent());

            Assert.That(matching, Is.EqualTo(1));
            Assert.That(other, Is.EqualTo(0));
        }

        [Test]
        public void DuplicateSubscribe_InvokesTwice()
        {
            // 文档明确约定：同一个回调重复订阅会执行多次，调用方自己保证成对调用
            int calls = 0;
            Action<TestEvent> handler = _ => calls++;

            EventBus.Subscribe(handler);
            EventBus.Subscribe(handler);
            EventBus.Publish(new TestEvent());

            Assert.That(calls, Is.EqualTo(2));
            Assert.That(EventBus.SubscriberCount<TestEvent>(), Is.EqualTo(2));
        }

        [Test]
        public void Unsubscribe_RemovesOnlyOneRegistration()
        {
            int calls = 0;
            Action<TestEvent> handler = _ => calls++;

            EventBus.Subscribe(handler);
            EventBus.Subscribe(handler);
            EventBus.Unsubscribe(handler);
            EventBus.Publish(new TestEvent());

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void UnsubscribeLastHandler_RemovesEntry()
        {
            Action<TestEvent> handler = _ => { };

            EventBus.Subscribe(handler);
            EventBus.Unsubscribe(handler);

            Assert.That(EventBus.SubscriberCount<TestEvent>(), Is.EqualTo(0));
        }

        [Test]
        public void UnsubscribeUnknown_IsNoOp()
        {
            Assert.DoesNotThrow(() => EventBus.Unsubscribe<TestEvent>(_ => { }));
            Assert.That(EventBus.SubscriberCount<TestEvent>(), Is.EqualTo(0));
        }

        [Test]
        public void NullHandler_IsIgnored()
        {
            EventBus.Subscribe<TestEvent>(null);
            EventBus.Unsubscribe<TestEvent>(null);

            Assert.That(EventBus.SubscriberCount<TestEvent>(), Is.EqualTo(0));
        }

        [Test]
        public void HandlerException_DoesNotStopOtherHandlers()
        {
            LogAssert.Expect(LogType.Error, new Regex("EventBus.*抛出异常"));

            int afterException = 0;

            EventBus.Subscribe<TestEvent>(_ => throw new InvalidOperationException("boom"));
            EventBus.Subscribe<TestEvent>(_ => afterException++);

            Assert.DoesNotThrow(() => EventBus.Publish(new TestEvent()));
            Assert.That(afterException, Is.EqualTo(1));
        }

        [Test]
        public void ClearAll_RemovesEverySubscription()
        {
            EventBus.Subscribe<TestEvent>(_ => { });
            EventBus.Subscribe<OtherTestEvent>(_ => { });

            EventBus.ClearAll();

            Assert.That(EventBus.SubscriberCount<TestEvent>(), Is.EqualTo(0));
            Assert.That(EventBus.SubscriberCount<OtherTestEvent>(), Is.EqualTo(0));
        }
    }
}
