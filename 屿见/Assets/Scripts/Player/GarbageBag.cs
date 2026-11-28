using System;
using UnityEngine;
using Yujian.Core;

namespace Yujian.Player
{
    /// <summary>
    /// 垃圾背包（策划案 2.1.6）：关卡内暂时容纳垃圾的容器，回岛后按等级规定的方式转移到岛屿存储站。
    ///
    /// 2026-11-25 用户拍板：**背包当缰绳**。
    ///   · 进关卡（区域 = 清洁）时打开，关卡内的垃圾只进背包；
    ///   · 包满就拒收并提示「存储已达上限」，玩家必须回岛卸货；
    ///   · 在岛上（区域 ≠ 清洁）按等级用长按 / 点击 / 自动把垃圾转进 <see cref="PlayerGarbage"/>。
    ///
    /// 与 <see cref="PlayerGarbage"/> 的分工：
    ///   · 本类是「关卡内的临时容器」，会满、会被清空；
    ///   · PlayerGarbage 是「岛屿垃圾存储站」，是真正的资源真相源，不设上限。
    ///
    /// 本类不碰 UI（GarbageBagUI 负责）、不碰存档（GarbageBagSaveParticipant 负责）、
    /// 不生成垃圾块（GarbageCollector 负责）。
    /// </summary>
    [DisallowMultipleComponent]
    public class GarbageBag : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("等级表。留空时背包容量恒为 1，等于关掉本系统")]
        [SerializeField] private GarbageBagTable table;

        [Tooltip("玩家金币。升级时从这里扣；留空时自动查找")]
        [SerializeField] private PlayerCurrency currency;

        [Tooltip("区域状态。进清洁区打开背包，离开关闭。留空时自动查找")]
        [SerializeField] private GameAreaController areas;

        [Header("初始状态")]
        [Tooltip("进入游戏时的背包等级")]
        [SerializeField, Min(1)] private int startingLevel = 1;

        [Header("调试")]
        [SerializeField] private bool logBagFlow = true;

        private int level = 1;
        private int current;

        /// <summary>背包等级或存量变化时触发：(等级, 当前, 上限)。</summary>
        public event Action<int, int, int> OnBagChanged;

        /// <summary>
        /// 拒收了垃圾时触发，参数是被拒收的数量。
        /// UI 用它弹「存储已达上限」，不用每帧轮询。
        /// </summary>
        public event Action<int> OnDepositRejected;

        /// <summary>背包等级，恒 >= 1。</summary>
        public int Level => Mathf.Max(1, level);

        /// <summary>当前存储上限。没有等级表时为 1。</summary>
        public int Capacity => table != null ? table.GetCapacity(Level) : 1;

        /// <summary>当前背包里的垃圾。</summary>
        public int Current => current;

        /// <summary>还能装多少。</summary>
        public int Room => Mathf.Max(0, Capacity - current);

        /// <summary>是否已满。</summary>
        public bool IsFull => current >= Capacity;

        /// <summary>是否满级。</summary>
        public bool IsMaxLevel => table == null || Level >= table.MaxLevel;

        /// <summary>升到下一级的花费；满级返回 0。</summary>
        public int NextUpgradeCost => table != null ? table.GetUpgradeCost(Level) : 0;

        /// <summary>下一级的容量，供 UI 显示 200 → 230。满级时等于当前容量。</summary>
        public int NextCapacity => table != null
            ? table.GetCapacity(IsMaxLevel ? Level : Level + 1)
            : Capacity;

        /// <summary>当前等级的转移方式。</summary>
        public GarbageBagTransferMode TransferMode => table != null
            ? table.GetTransferMode(Level)
            : GarbageBagTransferMode.LongPress;

        /// <summary>每秒转移速度。</summary>
        public float TransferPerSecond => table != null ? table.TransferPerSecond : 10f;

        /// <summary>
        /// 背包是否处于「收集」状态。只有关卡内（区域 = 清洁）为 true——
        /// 这也是 <see cref="PlayerGarbage"/> 判断「垃圾该进包还是进岛屿存储」的依据。
        ///
        /// 刻意做成**实时查询**而不是缓存：缓存要依赖 Start / 区域事件，少一个订阅点就多一处
        /// 「进了关卡背包还没打开」的空窗。区域本身就是唯一真相，直接读它。
        /// </summary>
        public bool IsOpen => areas != null && areas.CurrentArea == GameArea.Mining;

        /// <summary>等级表，供 UI 读取。可能为空。</summary>
        public GarbageBagTable Table => table;

        private void Awake()
        {
            level = Mathf.Max(1, startingLevel);
            current = 0;

            if (table == null)
            {
                Debug.LogError("[GarbageBag] 等级表未配置，背包容量恒为 1，垃圾基本收不进来。" +
                               "请运行菜单「屿见/垃圾背包/配置垃圾背包系统」。", this);
            }

            if (currency == null)
            {
                currency = FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            }

            if (areas == null)
            {
                areas = FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);
            }
        }

        private void Start()
        {
            if (areas == null)
            {
                Debug.LogError("[GarbageBag] 找不到 GameAreaController，无法判断进出关卡，" +
                               "背包会一直处于关闭状态（垃圾直接进岛屿存储）。", this);
            }

            if (logBagFlow && table != null)
            {
                // 这行是给调参用的：想知道「我改的速度到底生效没」，看这里打印的数字和资产名
                Debug.Log($"[GarbageBag] 等级表「{table.name}」→ 容量 {Capacity}、" +
                          $"转移速度 {table.TransferPerSecond}/s、方式 {TransferMode.ToChineseName()}。", this);
            }
        }

        /// <summary>
        /// 往背包里塞垃圾，返回实际收下的数量。
        /// 关闭状态或已满时返回 0——**不会**自动溢到岛屿存储，缰绳就是靠这一点成立的。
        /// </summary>
        public int TryAdd(int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            if (!IsOpen)
            {
                return 0;
            }

            int accepted = Mathf.Min(amount, Room);

            if (accepted <= 0)
            {
                OnDepositRejected?.Invoke(amount);
                return 0;
            }

            current += accepted;
            RaiseChanged();

            if (accepted < amount)
            {
                OnDepositRejected?.Invoke(amount - accepted);
            }

            return accepted;
        }

        /// <summary>
        /// 从背包取走最多 amount 个（转移用）。返回实际取走数量。
        /// 只改背包，不碰岛屿存储——加到哪里由调用方决定。
        /// </summary>
        public int TryTake(int amount)
        {
            if (amount <= 0 || current <= 0)
            {
                return 0;
            }

            int taken = Mathf.Min(amount, current);
            current -= taken;
            RaiseChanged();
            return taken;
        }

        /// <summary>
        /// 花金币升一级。满级、余额不足、没有等级表时返回 false，不扣钱。
        /// </summary>
        public bool TryUpgrade()
        {
            if (table == null)
            {
                Debug.LogError("[GarbageBag] 等级表未配置，无法升级。", this);
                return false;
            }

            if (IsMaxLevel)
            {
                return false;
            }

            int cost = NextUpgradeCost;

            if (currency == null)
            {
                Debug.LogError("[GarbageBag] 找不到 PlayerCurrency，无法扣升级费用。", this);
                return false;
            }

            if (!currency.TrySpendMoney(cost))
            {
                return false;
            }

            level++;
            RaiseChanged();

            if (logBagFlow)
            {
                Debug.Log($"[GarbageBag] 升到 {Level} 级，容量 {Capacity}，花费 {cost} 金币，" +
                          $"转移方式 {TransferMode.ToChineseName()}。", this);
            }

            return true;
        }

        /// <summary>读档用：直接恢复等级与存量，并按当前容量夹紧。</summary>
        public void Restore(int savedLevel, int savedAmount)
        {
            level = Mathf.Clamp(savedLevel <= 0 ? 1 : savedLevel, 1, table != null ? table.MaxLevel : int.MaxValue);
            current = Mathf.Clamp(savedAmount, 0, Capacity);
            RaiseChanged();
        }

        /// <summary>编辑器向导调用。</summary>
        public void ConfigureForSetup(GarbageBagTable newTable, PlayerCurrency newCurrency,
            GameAreaController newAreas, int newStartingLevel)
        {
            table = newTable;
            currency = newCurrency;
            areas = newAreas;
            startingLevel = Mathf.Max(1, newStartingLevel);
        }

        private void RaiseChanged()
        {
            OnBagChanged?.Invoke(Level, current, Capacity);
        }
    }

    /// <summary>转移方式的中文名，给 UI / 提示用。</summary>
    public static class GarbageBagTransferModeText
    {
        public static string ToChineseName(this GarbageBagTransferMode mode)
        {
            switch (mode)
            {
                case GarbageBagTransferMode.LongPress:
                    return "长按转移";

                case GarbageBagTransferMode.SingleClick:
                    return "点击转移";

                case GarbageBagTransferMode.AutoOnExit:
                    return "自动转移";

                default:
                    return "未知";
            }
        }
    }
}
