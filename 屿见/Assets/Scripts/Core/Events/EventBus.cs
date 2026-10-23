using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yujian.Core.Events
{
    /// <summary>
    /// 跨模块事件的类型化总线。
    ///
    /// 定位：只服务「玩法系统 → UI / 音频 / 存档触发器」这一类跨模块通知。
    /// 模块内部仍然用自己的 C# event，不要为了统一把所有回调都搬进来。
    ///
    /// 约定：
    ///   · 事件必须是 readonly struct（见 <see cref="GameEvents"/>）；
    ///   · 先提交状态，再发布事件，订阅者读到的永远是已更新的值；
    ///   · 订阅在 OnEnable，退订在 OnDisable / OnDestroy；
    ///   · 不要每帧发布（指针移动、跳字等）。
    ///
    /// 静态实例会在进入 Play Mode 时清空（SubsystemRegistration），
    /// 避免关闭 Domain Reload 时把上一次运行的订阅者带进新一局。
    /// </summary>
    public static class EventBus
    {
        private static readonly Dictionary<Type, Delegate> handlers = new Dictionary<Type, Delegate>();

        /// <summary>调试开关：打开后每次发布都会打印事件类型与订阅者数量。</summary>
        public static bool LogEvents { get; set; }

        /// <summary>订阅一个事件。同一个方法重复订阅会被执行多次，由调用方自己保证成对调用。</summary>
        public static void Subscribe<T>(Action<T> handler) where T : struct
        {
            if (handler == null)
            {
                Debug.LogWarning("[EventBus] 忽略空的订阅回调。");
                return;
            }

            Type type = typeof(T);

            if (handlers.TryGetValue(type, out Delegate existing))
            {
                handlers[type] = Delegate.Combine(existing, handler);
            }
            else
            {
                handlers[type] = handler;
            }
        }

        /// <summary>退订一个事件。没有订阅过时是空操作。</summary>
        public static void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            if (handler == null)
            {
                return;
            }

            Type type = typeof(T);

            if (!handlers.TryGetValue(type, out Delegate existing))
            {
                return;
            }

            Delegate remaining = Delegate.Remove(existing, handler);

            if (remaining == null)
            {
                handlers.Remove(type);
            }
            else
            {
                handlers[type] = remaining;
            }
        }

        /// <summary>
        /// 发布事件。同步调用所有订阅者；单个订阅者抛异常不会影响其他订阅者，
        /// 但会把完整堆栈打进 Console，绝不静默吞掉。
        /// </summary>
        public static void Publish<T>(T evt) where T : struct
        {
            Type type = typeof(T);

            if (!handlers.TryGetValue(type, out Delegate existing) || existing == null)
            {
                if (LogEvents)
                {
                    Debug.Log($"[EventBus] {type.Name}：没有订阅者。");
                }

                return;
            }

            Delegate[] invocationList = existing.GetInvocationList();

            if (LogEvents)
            {
                Debug.Log($"[EventBus] {type.Name} → {invocationList.Length} 个订阅者。");
            }

            for (int i = 0; i < invocationList.Length; i++)
            {
                try
                {
                    ((Action<T>)invocationList[i]).Invoke(evt);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[EventBus] {type.Name} 的订阅者 {invocationList[i].Method.DeclaringType?.Name} " +
                                   $"抛出异常：{exception}");
                }
            }
        }

        /// <summary>当前某个事件的订阅者数量。供调试与回归检查使用。</summary>
        public static int SubscriberCount<T>() where T : struct
        {
            return handlers.TryGetValue(typeof(T), out Delegate existing) && existing != null
                ? existing.GetInvocationList().Length
                : 0;
        }

        /// <summary>清空全部订阅。场景切换 / 进入 Play Mode / 测试结束时调用。</summary>
        public static void ClearAll()
        {
            handlers.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayModeEnter()
        {
            ClearAll();
        }
    }
}
