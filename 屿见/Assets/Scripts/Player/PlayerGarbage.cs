using System;
using UnityEngine;
using Yujian.Core.Events;

namespace Yujian.Player
{
    /// <summary>
    /// 玩家持有的垃圾（2026-11-25 起改称「岛屿垃圾存储站」）。
    ///
    /// 与 <see cref="PlayerCurrency"/> 并列的第二种资源（2026-11-19 用户拍板：垃圾 / 金币严格拆分）：
    ///   · 采矿击碎矿石、建筑自动攻击击碎矿石都只产垃圾；
    ///   · 垃圾投入回收站后按 5:1 转换成金币；
    ///   · 金币只由回收站产出，商店 / 工具 / 建筑升级花金币。
    ///
    /// 2026-11-25 垃圾背包接入后，本类成为**垃圾的唯一入口**：
    ///   · 关卡内（<see cref="GarbageBag.IsOpen"/>）垃圾只进背包，背包满了就拒收——不回落到这里；
    ///   · 关卡外的垃圾，以及背包转移过来的垃圾，才进本类持有的岛屿存储。
    /// 这样「除本类外任何系统都不得直接读写垃圾数量」这条规则仍然成立。
    ///
    /// 职责边界与 PlayerCurrency 相同：只持有当前垃圾、提供增加 / 消耗接口、变化时广播事件。
    /// 不知道矿石、回收站、商店的存在。
    /// </summary>
    public class PlayerGarbage : MonoBehaviour
    {
        [Header("初始状态")]
        [Tooltip("进入游戏时玩家持有的垃圾")]
        [SerializeField] private int startingGarbage = 0;

        [Header("垃圾背包")]
        [Tooltip("关卡内的临时容器。配置后：背包打开时垃圾只进背包，满了就拒收；" +
                 "留空则所有垃圾直接进岛屿存储（旧行为）")]
        [SerializeField] private GarbageBag bag;

        private int currentGarbage;

        /// <summary>当前垃圾数量（岛屿存储站的存量，不含背包里的）。</summary>
        public int CurrentGarbage => currentGarbage;

        /// <summary>垃圾变化时触发，参数为变化后的数量。UI / 回收站入口通过该事件刷新。</summary>
        public event Action<int> OnGarbageChanged;

        /// <summary>背包是否正在接管垃圾（关卡内）。</summary>
        public bool IsBagCollecting => bag != null && bag.IsOpen;

        private void Awake()
        {
            if (startingGarbage < 0)
            {
                Debug.LogWarning($"[PlayerGarbage] 初始垃圾配置为负值({startingGarbage})，已按 0 处理。", this);
                startingGarbage = 0;
            }

            currentGarbage = startingGarbage;

            if (bag == null)
            {
                bag = FindFirstObjectByType<GarbageBag>(FindObjectsInactive.Include);
            }
        }

        /// <summary>
        /// 增加垃圾。**这是关卡内外所有垃圾的统一入口。**
        ///
        /// 关卡内且有背包时：垃圾进背包；背包满了就拒收（背包自己广播「已达上限」），
        /// 绝不会悄悄溢到岛屿存储——「背包当缰绳」靠的就是这一条。
        /// 其余情况：直接进岛屿存储。
        /// </summary>
        /// <param name="amount">增加量。</param>
        public void AddGarbage(int amount)
        {
            if (amount <= 0)
            {
                Debug.LogWarning($"[PlayerGarbage] AddGarbage 收到非正数({amount})，已忽略。", this);
                return;
            }

            if (bag != null && bag.IsOpen)
            {
                // 拒收与部分接收由背包自己广播，这里不重复发事件
                bag.TryAdd(amount);
                return;
            }

            AddToStorage(amount);
        }

        /// <summary>
        /// 直接写进岛屿存储站，**跳过背包**。
        /// 只给三类调用方用：背包转移、回收站退回 / 溢出、读档。
        /// 普通产垃圾请走 <see cref="AddGarbage"/>。
        /// </summary>
        public void AddToStorage(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            currentGarbage += amount;
            OnGarbageChanged?.Invoke(currentGarbage);
            EventBus.Publish(new GarbageChanged(currentGarbage, amount));
        }

        /// <summary>
        /// 读档用：直接把垃圾恢复成存档里的值，并广播一次变化。
        /// 负数按 0 处理并明确警告，避免坏存档把垃圾变成负数。
        /// </summary>
        /// <param name="amount">存档里的垃圾数量。</param>
        public void RestoreGarbage(int amount)
        {
            int clamped = Mathf.Max(0, amount);

            if (clamped != amount)
            {
                Debug.LogWarning($"[PlayerGarbage] 存档里的垃圾为 {amount}，已按 0 恢复。", this);
            }

            currentGarbage = clamped;
            // 故意不发 EventBus：读档不是“获得垃圾”，订阅了收集音效的系统不该响。
            // UI 走旧事件 OnGarbageChanged 已经能正确刷新。
            OnGarbageChanged?.Invoke(currentGarbage);
        }

        /// <summary>
        /// 尝试消耗垃圾（例如投入回收站队列）。余额不足时返回 false，且不扣除任何垃圾。
        /// </summary>
        /// <param name="amount">消耗量，必须为正数。</param>
        /// <returns>是否消耗成功。</returns>
        public bool TrySpendGarbage(int amount)
        {
            if (amount <= 0)
            {
                Debug.LogWarning($"[PlayerGarbage] TrySpendGarbage 收到非正数({amount})，已拒绝。", this);
                return false;
            }

            if (currentGarbage < amount)
            {
                return false;
            }

            currentGarbage -= amount;
            OnGarbageChanged?.Invoke(currentGarbage);
            EventBus.Publish(new GarbageChanged(currentGarbage, -amount));
            return true;
        }
    }
}
