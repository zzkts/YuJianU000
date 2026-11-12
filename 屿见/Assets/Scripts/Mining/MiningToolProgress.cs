using System;
using UnityEngine;
using Yujian.Player;

namespace Yujian.Mining
{
    /// <summary>
    /// 挖矿工具升级进度。
    /// 负责持有当前等级、计算当前攻击力和升级花费，并在升级时扣除金币。
    /// 伤害计算仍由 DamageSystem 统一处理。
    /// </summary>
    public class MiningToolProgress : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("挖矿工具数据资产")]
        [SerializeField] private MiningToolData toolData;

        [Tooltip("玩家金币。留空时会在运行时自动查找")]
        [SerializeField] private PlayerCurrency playerCurrency;

        [Header("初始状态")]
        [Tooltip("进入游戏时的工具等级")]
        [SerializeField, Min(1)] private int startingLevel = 1;

        [Header("调试")]
        [SerializeField] private bool logUpgradeFlow = true;

        private int currentLevel;

        public MiningToolData Data => toolData;
        public PlayerCurrency Currency => playerCurrency;
        public int CurrentLevel => Mathf.Max(1, currentLevel);

        /// <summary>工具最高等级（数据资产配置，默认 6）。</summary>
        public int MaxLevel => toolData != null ? toolData.MaxLevel : 1;

        /// <summary>是否已经到顶。满级后不再提供升级与升级费用。</summary>
        public bool IsMaxLevel => CurrentLevel >= MaxLevel;

        /// <summary>当前攻击力。D7=A 起是几何曲线的浮点值，可能是 1.82 这种小数。</summary>
        public float CurrentAttack => toolData != null ? toolData.GetAttackAtLevel(CurrentLevel) : 1f;

        /// <summary>升到下一级的费用。满级时为 0。</summary>
        public int NextUpgradeCost => IsMaxLevel || toolData == null ? 0 : toolData.GetUpgradeCost(CurrentLevel);

        /// <summary>工具显示名（石头 / 后续的镐子等）。</summary>
        public string ToolName => toolData != null ? toolData.DisplayName : "石头";

        /// <summary>下一等级的攻击力，供界面直接显示。满级时等于当前攻击力。</summary>
        public float NextAttack => toolData != null
            ? toolData.GetAttackAtLevel(IsMaxLevel ? CurrentLevel : CurrentLevel + 1)
            : CurrentAttack;

        /// <summary>从当前等级连续升 count 级需要的总金币。</summary>
        public int GetUpgradeCostForLevels(int count)
        {
            if (toolData == null || count <= 0 || IsMaxLevel)
            {
                return 0;
            }

            int actualCount = Mathf.Min(count, MaxLevel - CurrentLevel);
            int total = 0;

            for (int i = 0; i < actualCount; i++)
            {
                total += toolData.GetUpgradeCost(CurrentLevel + i);
            }

            return total;
        }

        /// <summary>用当前金币一次性能升多少级（Max 模式用）。</summary>
        public int GetAffordableUpgradeCount(int money)
        {
            if (toolData == null || IsMaxLevel)
            {
                return 0;
            }

            int count = 0;
            int spent = 0;

            while (CurrentLevel + count < MaxLevel)
            {
                int cost = toolData.GetUpgradeCost(CurrentLevel + count);

                if (cost > 0 && spent + cost > money)
                {
                    break;
                }

                spent += cost;
                count++;
            }

            return count;
        }

        /// <summary>等级或攻击力变化时触发，后续 UI 订阅它刷新显示。</summary>
        public event Action<MiningToolProgress> OnToolChanged;

        private void Awake()
        {
            currentLevel = Mathf.Clamp(startingLevel, 1, MaxLevel);

            if (playerCurrency == null)
            {
                playerCurrency = FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            }

            if (toolData == null)
            {
                Debug.LogError("[MiningToolProgress] 挖矿工具数据未配置，攻击力将回退为 1。" +
                               "请运行菜单「屿见/配置挖矿工具升级（阶段 9）」。", this);
            }
        }

        /// <summary>编辑器向导调用，把资产和金币引用接好。</summary>
        public void ConfigureForSetup(MiningToolData data, PlayerCurrency currency, int level = 1)
        {
            toolData = data;
            playerCurrency = currency;
            startingLevel = Mathf.Clamp(level, 1, MaxLevel);
            currentLevel = startingLevel;
        }

        /// <summary>
        /// 读档恢复用：直接设置等级，不扣金币。
        /// 旧存档没有该字段时由调用方按 1 级处理。
        /// </summary>
        public void SetLevel(int level)
        {
            int restored = Mathf.Clamp(level, 1, MaxLevel);

            if (restored == currentLevel)
            {
                return;
            }

            currentLevel = restored;
            OnToolChanged?.Invoke(this);
        }

        /// <summary>尝试升一级。金币不足或数据缺失时返回 false，不改变等级。</summary>
        public bool TryUpgrade()
        {
            return TryUpgrade(1);
        }

        /// <summary>
        /// 连续升 count 级（x1 / Max 共用）。金币按实际级数一次性扣，
        /// 金币不足或数据缺失时返回 false，不改变等级。
        /// </summary>
        public bool TryUpgrade(int count)
        {
            if (toolData == null)
            {
                Debug.LogError("[MiningToolProgress] 没有挖矿工具数据，无法升级。", this);
                return false;
            }

            if (playerCurrency == null)
            {
                Debug.LogError("[MiningToolProgress] 没有 PlayerCurrency，无法扣金币。", this);
                return false;
            }

            if (count <= 0)
            {
                return false;
            }

            if (IsMaxLevel)
            {
                if (logUpgradeFlow)
                {
                    Debug.Log($"[MiningToolProgress]「{toolData.DisplayName}」已是 {MaxLevel} 级上限，不能再升。", this);
                }

                return false;
            }

            int actualCount = Mathf.Min(count, MaxLevel - CurrentLevel);
            int cost = GetUpgradeCostForLevels(actualCount);
            if (cost > 0 && !playerCurrency.TrySpendMoney(cost))
            {
                if (logUpgradeFlow)
                {
                    Debug.Log($"[MiningToolProgress] 金币不足，升级 {count} 级需要 {cost}，当前 {playerCurrency.CurrentMoney}。", this);
                }

                return false;
            }

            currentLevel += actualCount;
            OnToolChanged?.Invoke(this);

            if (logUpgradeFlow)
            {
                string next = IsMaxLevel ? "已满级" : $"下次升级需要 {NextUpgradeCost} 金币";
                Debug.Log($"[MiningToolProgress]「{toolData.DisplayName}」升到 {CurrentLevel} 级，" +
                          $"攻击力 {CurrentAttack}，{next}。", this);
            }

            return true;
        }

        [ContextMenu("调试：升一级")]
        private void DebugUpgrade()
        {
            TryUpgrade();
        }
    }
}
