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

        /// <summary>全局唯一实例。供没有场景引用可挂的系统（例如后续的 UI）访问。</summary>
        public static GameManager Instance { get; private set; }

        /// <summary>玩家货币。</summary>
        public PlayerCurrency Currency => playerCurrency;

        /// <summary>玩家属性。</summary>
        public PlayerStats Stats => playerStats;

        /// <summary>伤害计算。</summary>
        public DamageSystem Damage => damageSystem;

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
        }
    }
}
