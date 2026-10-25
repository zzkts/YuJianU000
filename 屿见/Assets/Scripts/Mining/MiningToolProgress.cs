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
        public int CurrentAttack => toolData != null ? toolData.GetAttackAtLevel(CurrentLevel) : 1;
        public int NextUpgradeCost => toolData != null ? toolData.GetUpgradeCost(CurrentLevel) : 0;

        /// <summary>等级或攻击力变化时触发，后续 UI 订阅它刷新显示。</summary>
        public event Action<MiningToolProgress> OnToolChanged;

        private void Awake()
        {
            currentLevel = Mathf.Max(1, startingLevel);

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
            startingLevel = Mathf.Max(1, level);
            currentLevel = startingLevel;
        }

        /// <summary>尝试升一级。金币不足或数据缺失时返回 false，不改变等级。</summary>
        public bool TryUpgrade()
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

            int cost = NextUpgradeCost;
            if (cost > 0 && !playerCurrency.TrySpendMoney(cost))
            {
                if (logUpgradeFlow)
                {
                    Debug.Log($"[MiningToolProgress] 金币不足，升级「{toolData.DisplayName}」需要 {cost}，当前 {playerCurrency.CurrentMoney}。", this);
                }

                return false;
            }

            currentLevel++;
            OnToolChanged?.Invoke(this);

            if (logUpgradeFlow)
            {
                Debug.Log($"[MiningToolProgress]「{toolData.DisplayName}」升到 {CurrentLevel} 级，" +
                          $"攻击力 {CurrentAttack}，下次升级需要 {NextUpgradeCost} 金币。", this);
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
