using UnityEngine;
using Yujian.Player;

namespace Yujian.Mining
{
    /// <summary>
    /// 伤害计算。
    /// 职责：全工程唯一的玩家伤害公式入口 —— 基础攻击力 × 伤害倍率。
    /// 不修改矿石生命值、不修改玩家金币、不查找矿石、不处理点击、不播放表现。
    /// 玩家攻击与后续的建筑自动攻击都必须调用本类，避免公式分叉。
    /// </summary>
    public class DamageSystem : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("玩家属性，提供基础攻击力与伤害倍率")]
        [SerializeField] private PlayerStats playerStats;

        [Header("调试")]
        [Tooltip("是否在 Console 打印每次伤害计算结果")]
        [SerializeField] private bool logCalculatedDamage = false;

        /// <summary>
        /// 计算玩家当前的单次伤害。
        /// </summary>
        /// <returns>基础攻击力 × 伤害倍率；PlayerStats 未配置时返回 0 并报错。</returns>
        public float CalculatePlayerDamage()
        {
            if (playerStats == null)
            {
                Debug.LogError("[DamageSystem] PlayerStats 未配置，无法计算伤害。", this);
                return 0f;
            }

            float damage = playerStats.BaseAttack * playerStats.DamageMultiplier;

            if (logCalculatedDamage)
            {
                Debug.Log($"[DamageSystem] 伤害 = {playerStats.BaseAttack} × {playerStats.DamageMultiplier} = {damage}");
            }

            return damage;
        }
    }
}
