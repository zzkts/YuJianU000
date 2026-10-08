using UnityEngine;
using Yujian.Mining;
using Yujian.Player;

namespace Yujian.Core
{
    /// <summary>
    /// 游戏系统入口。
    /// 职责：持有核心系统引用、对外提供全局访问入口，并在启动时校验装配完整性。
    /// 不承载任何业务逻辑——不提供 MineOre / BuyMaterial / BuildBuilding 这类方法。
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("核心系统引用")]
        [SerializeField] private PlayerCurrency playerCurrency;
        [SerializeField] private PlayerStats playerStats;
        [SerializeField] private DamageSystem damageSystem;

        [Header("阶段 6：当前区域")]
        [Tooltip("区域状态。这里**只读转发**，不承载区域逻辑——" +
                 "区域本身由 GameAreaController 持有（谁在挖矿区、谁在建筑区，都不是 GameManager 的事）")]
        [SerializeField] private GameAreaController areas;

        /// <summary>全局唯一实例。供没有场景引用可挂的系统（例如后续的 UI）访问。</summary>
        public static GameManager Instance { get; private set; }

        /// <summary>玩家货币。</summary>
        public PlayerCurrency Currency => playerCurrency;

        /// <summary>玩家属性。</summary>
        public PlayerStats Stats => playerStats;

        /// <summary>伤害计算。</summary>
        public DamageSystem Damage => damageSystem;

        /// <summary>
        /// 当前所在区域（需求十：GameManager 至少要能区分挖矿 / 建筑 / 商店）。
        ///
        /// 只是把 <see cref="GameAreaController.CurrentArea"/> 读出来给外部用，
        /// 本类不判断、不切换、不广播——区域的一切仍然只在 GameAreaController 里。
        /// <see cref="areas"/> 没配时退化成挖矿区并报错（见 <see cref="ValidateWiring"/>），
        /// 不会抛 NullReferenceException。
        /// </summary>
        public GameArea CurrentArea => areas != null ? areas.CurrentArea : GameArea.Mining;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[GameManager] 场景中存在多个 GameManager，已保留最先初始化的实例。", this);
                return;
            }

            Instance = this;
            ValidateWiring();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>校验关键引用是否已配置，避免运行到一半才出现 NullReferenceException。</summary>
        private void ValidateWiring()
        {
            if (playerCurrency == null)
            {
                Debug.LogError("[GameManager] PlayerCurrency 未配置。", this);
            }

            if (playerStats == null)
            {
                Debug.LogError("[GameManager] PlayerStats 未配置。", this);
            }

            if (damageSystem == null)
            {
                Debug.LogError("[GameManager] DamageSystem 未配置。", this);
            }

            if (areas == null)
            {
                // 不拦着游戏跑：CurrentArea 会退化成挖矿区。但要说明白，否则
                // 外部读到的永远是「挖矿」，而没有任何报错——规则 5：不许静默失败
                Debug.LogError("[GameManager] GameAreaController 未配置，CurrentArea 会一直返回「挖矿」。" +
                               "请重跑菜单「屿见/配置 MVP 整合（阶段 6）」。", this);
            }
        }
    }
}
