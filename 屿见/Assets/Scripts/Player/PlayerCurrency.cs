using System;
using UnityEngine;
using Yujian.Core.Events;

namespace Yujian.Player
{
    /// <summary>
    /// 玩家货币。
    /// 职责：持有当前金币，提供增加与消耗接口，并在数值变化时广播事件。
    /// 不知道矿石、输入、建筑、商店的存在；除本类外任何系统都不得直接读写金币数值。
    /// </summary>
    public class PlayerCurrency : MonoBehaviour
    {
        [Header("初始状态")]
        [Tooltip("进入游戏时玩家持有的金币")]
        [SerializeField] private int startingMoney = 0;

        private int currentMoney;

        /// <summary>当前金币数量。</summary>
        public int CurrentMoney => currentMoney;

        /// <summary>金币变化时触发，参数为变化后的数量。后续 UI 通过订阅该事件刷新文本。</summary>
        public event Action<int> OnMoneyChanged;

        private void Awake()
        {
            if (startingMoney < 0)
            {
                Debug.LogWarning($"[PlayerCurrency] 初始金币配置为负值({startingMoney})，已按 0 处理。", this);
                startingMoney = 0;
            }

            currentMoney = startingMoney;
        }

        /// <summary>
        /// 增加金币。
        /// amount 必须为正数；0 与负数一律拒绝，保证金币不会因调用出错而减少。
        /// </summary>
        /// <param name="amount">增加量。</param>
        public void AddMoney(int amount)
        {
            if (amount <= 0)
            {
                Debug.LogWarning($"[PlayerCurrency] AddMoney 收到非正数({amount})，已忽略。", this);
                return;
            }

            currentMoney += amount;
            OnMoneyChanged?.Invoke(currentMoney);
            EventBus.Publish(new MoneyChanged(currentMoney, amount));
        }

        /// <summary>
        /// 读档用：直接把金币恢复成存档里的值，并广播一次变化。
        /// 不走 AddMoney / TrySpendMoney——它们只接受正数增量，无法表达“把余额改成 X”。
        /// 负数按 0 处理并明确警告，避免坏存档把金币变成负数。
        /// </summary>
        /// <param name="amount">存档里的金币数量。</param>
        public void RestoreMoney(int amount)
        {
            int clamped = Mathf.Max(0, amount);

            if (clamped != amount)
            {
                Debug.LogWarning($"[PlayerCurrency] 存档里的金币为 {amount}，已按 0 恢复。", this);
            }

            currentMoney = clamped;
            // 故意不发 EventBus：读档不是“赚到钱”，订阅了金币音的音频管理会连着响。
            // UI 走旧事件 OnMoneyChanged 已经能正确刷新。
            OnMoneyChanged?.Invoke(currentMoney);
        }

        /// <summary>
        /// 尝试消耗金币。余额不足时返回 false，且不扣除任何金币。
        /// </summary>
        /// <param name="amount">消耗量，必须为正数。</param>
        /// <returns>是否消耗成功。</returns>
        public bool TrySpendMoney(int amount)
        {
            if (amount <= 0)
            {
                Debug.LogWarning($"[PlayerCurrency] TrySpendMoney 收到非正数({amount})，已拒绝。", this);
                return false;
            }

            if (currentMoney < amount)
            {
                return false;
            }

            currentMoney -= amount;
            OnMoneyChanged?.Invoke(currentMoney);
            EventBus.Publish(new MoneyChanged(currentMoney, -amount));
            return true;
        }
    }
}
