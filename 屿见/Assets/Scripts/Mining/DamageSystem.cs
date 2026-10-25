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

        [Tooltip("挖矿工具升级进度。配置后优先使用工具的当前攻击力")]
        [SerializeField] private MiningToolProgress miningTool;

        [Header("暴击")]
        [Tooltip("暴击概率（百分比）。20 表示每次玩家攻击有 20% 的概率触发暴击")]
        [Range(0f, 100f)]
        [SerializeField] private float criticalChancePercent = 20f;

        [Tooltip("暴击伤害倍数。2 表示暴击造成双倍伤害；不会低于 1 倍")]
        [Min(1f)]
        [SerializeField] private float criticalDamageMultiplier = 2f;

        [Header("调试")]
        [Tooltip("是否在 Console 打印每次伤害计算结果")]
        [SerializeField] private bool logCalculatedDamage = false;

        /// <summary>暴击概率（百分比）。</summary>
        public float CriticalChancePercent => criticalChancePercent;

        /// <summary>暴击伤害倍数。</summary>
        public float CriticalDamageMultiplier => criticalDamageMultiplier;

        /// <summary>
        /// 计算玩家当前的单次伤害（不判定暴击）。
        /// 建筑自动攻击等「稳定伤害」场景使用这个重载，保证 DPS 不会随机跳动。
        /// </summary>
        /// <returns>基础攻击力 × 伤害倍率；PlayerStats 未配置时返回 0 并报错。</returns>
        public float CalculatePlayerDamage()
        {
            return CalculatePlayerDamage(out bool _);
        }

        /// <summary>
        /// 计算玩家当前的单次伤害，并判定本次是否暴击。
        /// 只有玩家主动点击挖矿使用这个重载；暴击后伤害再乘以暴击倍数。
        /// </summary>
        /// <param name="isCritical">本次攻击是否触发暴击。</param>
        public float CalculatePlayerDamage(out bool isCritical)
        {
            isCritical = false;

            if (playerStats == null)
            {
                Debug.LogError("[DamageSystem] PlayerStats 未配置，无法计算伤害。", this);
                return 0f;
            }

            float baseAttack = miningTool != null ? miningTool.CurrentAttack : playerStats.BaseAttack;
            float damage = baseAttack * playerStats.DamageMultiplier;

            if (criticalChancePercent > 0f && UnityEngine.Random.value * 100f < criticalChancePercent)
            {
                isCritical = true;
                damage *= Mathf.Max(1f, criticalDamageMultiplier);
            }

            if (logCalculatedDamage)
            {
                Debug.Log($"[DamageSystem] 伤害 = {baseAttack} × {playerStats.DamageMultiplier} = {damage}" +
                          (isCritical ? $"（暴击 ×{criticalDamageMultiplier}）" : string.Empty));
            }

            return damage;
        }

        /// <summary>编辑器向导调用，把挖矿工具进度接进来。</summary>
        public void ConfigureForSetup(MiningToolProgress tool)
        {
            miningTool = tool;
        }

        /// <summary>编辑器向导调用，配置暴击概率与倍数。</summary>
        public void ConfigureCriticalHit(float chancePercent, float multiplier)
        {
            criticalChancePercent = Mathf.Clamp(chancePercent, 0f, 100f);
            criticalDamageMultiplier = Mathf.Max(1f, multiplier);
        }
    }
}
