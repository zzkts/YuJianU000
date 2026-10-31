using System;
using UnityEngine;
using Yujian.Player;

namespace Yujian.Mining
{
    /// <summary>
    /// 照明工具（D17=A：商店购买）。
    ///
    /// 它本身不发光——矿洞氛围由 MiningLightingController 负责。
    /// 这里只做玩法层的事：玩家买到的等级 → 暴击点同时存在上限 + 暴击点停留时长。
    ///
    /// 等级：
    ///   0 未拥有：上限 1，停留 2.5 秒
    ///   1 火把：  上限 3，停留 4 秒
    ///   2 手电：  上限 5，停留 6 秒
    ///
    /// 数值全部在 Inspector 上可调，代码不写死。扣钱与升级入口与 MiningToolProgress 同一套规则。
    /// </summary>
    [DisallowMultipleComponent]
    public class MiningLightTool : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("玩家金币。留空时运行时自动查找")]
        [SerializeField] private PlayerCurrency playerCurrency;

        [Header("初始状态")]
        [Tooltip("进入游戏时的照明等级。0 = 未拥有，1 = 火把，2 = 手电")]
        [SerializeField, Min(0)] private int startingLevel;

        [Header("等级名称（下标 = 等级）")]
        [SerializeField] private string[] levelNames = { "未拥有", "火把", "手电" };

        [Header("暴击点上限（下标 = 等级）")]
        [SerializeField] private int[] capacityByLevel = { 1, 3, 5 };

        [Header("暴击点停留时长，秒（下标 = 等级）")]
        [SerializeField] private float[] lifetimeByLevel = { 2.5f, 4f, 6f };

        [Header("购买价格")]
        [Tooltip("从当前等级买到下一级需要的金币。长度 = 等级数 - 1")]
        [SerializeField] private int[] upgradeCosts = { 40, 120 };

        [Header("调试")]
        [SerializeField] private bool logUpgradeFlow = true;

        private int currentLevel;

        /// <summary>当前等级。0 = 未拥有。</summary>
        public int Level => Mathf.Clamp(currentLevel, 0, MaxLevel);

        /// <summary>最高等级（由名称数组长度决定）。</summary>
        public int MaxLevel => Mathf.Max(0, levelNames != null ? levelNames.Length - 1 : 0);

        /// <summary>是否已经买满。</summary>
        public bool IsMaxed => Level >= MaxLevel;

        /// <summary>当前等级下的暴击点同时存在上限。</summary>
        public int CritPointCapacity => LookupInt(capacityByLevel, Level, 1);

        /// <summary>当前等级下的暴击点停留时长（秒）。</summary>
        public float CritPointLifetime => LookupFloat(lifetimeByLevel, Level, 2.5f);

        /// <summary>下一级的暴击点上限；已满级时返回当前值。</summary>
        public int NextCritPointCapacity => IsMaxed
            ? CritPointCapacity
            : LookupInt(capacityByLevel, Level + 1, CritPointCapacity);

        /// <summary>下一级的停留时长；已满级时返回当前值。</summary>
        public float NextCritPointLifetime => IsMaxed
            ? CritPointLifetime
            : LookupFloat(lifetimeByLevel, Level + 1, CritPointLifetime);

        /// <summary>当前等级名称。</summary>
        public string LevelName => LookupName(Level);

        /// <summary>下一级名称；已满级时返回当前名称。</summary>
        public string NextLevelName => IsMaxed ? LevelName : LookupName(Level + 1);

        /// <summary>升到下一级需要的金币；已满级返回 0。</summary>
        public int NextUpgradeCost
        {
            get
            {
                if (IsMaxed || upgradeCosts == null || Level >= upgradeCosts.Length)
                {
                    return 0;
                }

                return Mathf.Max(0, upgradeCosts[Level]);
            }
        }

        /// <summary>等级变化时广播。</summary>
        public event Action<MiningLightTool> OnToolChanged;

        private void Awake()
        {
            currentLevel = Mathf.Clamp(startingLevel, 0, MaxLevel);

            if (playerCurrency == null)
            {
                playerCurrency = FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            }
        }

        /// <summary>编辑器向导调用。</summary>
        public void ConfigureForSetup(PlayerCurrency currency)
        {
            playerCurrency = currency;
        }

        /// <summary>
        /// 尝试买到下一级。金币不足 / 已满级时返回 false，状态不变。
        /// 价格为 0 表示免费发放。
        /// </summary>
        public bool TryUpgrade()
        {
            if (IsMaxed)
            {
                if (logUpgradeFlow)
                {
                    Debug.Log($"[MiningLightTool] 已是最高等级「{LevelName}」，无需再买。", this);
                }

                return false;
            }

            if (playerCurrency == null)
            {
                Debug.LogError("[MiningLightTool] 没有 PlayerCurrency，无法扣金币。", this);
                return false;
            }

            int cost = NextUpgradeCost;

            if (cost > 0 && !playerCurrency.TrySpendMoney(cost))
            {
                if (logUpgradeFlow)
                {
                    Debug.Log($"[MiningLightTool] 金币不足，购买「{NextLevelName}」需要 {cost}，" +
                              $"当前 {playerCurrency.CurrentMoney}。", this);
                }

                return false;
            }

            currentLevel++;
            OnToolChanged?.Invoke(this);

            if (logUpgradeFlow)
            {
                Debug.Log($"[MiningLightTool] 获得「{LevelName}」：暴击点上限 {CritPointCapacity}，" +
                          $"停留 {CritPointLifetime:0.#} 秒。", this);
            }

            return true;
        }

        /// <summary>读档恢复用：直接设置等级，不扣钱、不广播前先夹紧。</summary>
        public void SetLevel(int level)
        {
            int clamped = Mathf.Clamp(level, 0, MaxLevel);

            if (clamped == currentLevel)
            {
                return;
            }

            currentLevel = clamped;
            OnToolChanged?.Invoke(this);
        }

        private string LookupName(int level)
        {
            if (levelNames == null || levelNames.Length == 0)
            {
                return $"等级 {level}";
            }

            int index = Mathf.Clamp(level, 0, levelNames.Length - 1);
            return string.IsNullOrWhiteSpace(levelNames[index]) ? $"等级 {index}" : levelNames[index];
        }

        private static int LookupInt(int[] values, int level, int fallback)
        {
            if (values == null || values.Length == 0)
            {
                return fallback;
            }

            return values[Mathf.Clamp(level, 0, values.Length - 1)];
        }

        private static float LookupFloat(float[] values, int level, float fallback)
        {
            if (values == null || values.Length == 0)
            {
                return fallback;
            }

            return values[Mathf.Clamp(level, 0, values.Length - 1)];
        }
    }
}
