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

        [Tooltip("照明工具。配置后暴击倍率从当前照明工具读取（飞书 2.1.4）")]
        [SerializeField] private MiningLightTool miningLightTool;

        [Header("暴击")]
        [Tooltip("暴击伤害倍数。2 表示点中暴击点造成双倍伤害；不会低于 1 倍。" +
                 "D3=A 起暴击只来自矿石上的暴击点，不再有随机暴击")]
        [Min(1f)]
        [SerializeField] private float criticalDamageMultiplier = 2f;

        [Header("调试")]
        [Tooltip("是否在 Console 打印每次伤害计算结果")]
        [SerializeField] private bool logCalculatedDamage = false;

        /// <summary>
        /// 当前暴击伤害倍数：优先读当前照明工具，未拥有照明工具时回退到本组件的默认值。
        /// </summary>
        public float CriticalDamageMultiplier => miningLightTool != null && miningLightTool.IsUnlocked
            ? Mathf.Max(1f, miningLightTool.CritPointDamageMultiplier)
            : Mathf.Max(1f, criticalDamageMultiplier);

        /// <summary>
        /// 计算玩家当前的单次伤害（不判定暴击）。
        /// 建筑自动攻击等「稳定伤害」场景使用这个重载，保证 DPS 不会随机跳动。
        /// </summary>
        /// <returns>基础攻击力 × 伤害倍率；PlayerStats 未配置时返回 0 并报错。</returns>
        public float CalculatePlayerDamage()
        {
            if (playerStats == null)
            {
                Debug.LogError("[DamageSystem] PlayerStats 未配置，无法计算伤害。", this);
                return 0f;
            }

            float baseAttack = miningTool != null ? miningTool.CurrentAttack : playerStats.BaseAttack;
            float damage = baseAttack * playerStats.DamageMultiplier;

            if (logCalculatedDamage)
            {
                Debug.Log($"[DamageSystem] 伤害 = {baseAttack} × {playerStats.DamageMultiplier} = {damage}");
            }

            return damage;
        }

        /// <summary>
        /// 计算建筑自动攻击的单次伤害：玩家最终伤害 × 建筑伤害倍率。
        /// 2026-11-05 商店策划案：木板等材料提供建筑伤害加成，
        /// 建筑倍率由 <see cref="PlayerStats.BuildingDamageMultiplier"/> 汇总（基础 1 + 附加）。
        /// 建筑自动攻击必须走这个入口，不能直接调用 <see cref="CalculatePlayerDamage"/>。
        /// </summary>
        /// <returns>玩家最终伤害 × 建筑伤害倍率；PlayerStats 未配置时返回 0 并报错。</returns>
        public float CalculateBuildingDamage()
        {
            if (playerStats == null)
            {
                Debug.LogError("[DamageSystem] PlayerStats 未配置，无法计算建筑伤害。", this);
                return 0f;
            }

            float playerDamage = CalculatePlayerDamage();
            float damage = playerDamage * playerStats.BuildingDamageMultiplier;

            if (logCalculatedDamage)
            {
                Debug.Log($"[DamageSystem] 建筑伤害 = 玩家 {playerDamage} × 建筑倍率 " +
                          $"{playerStats.BuildingDamageMultiplier} = {damage}");
            }

            return damage;
        }

        /// <summary>
        /// 计算玩家当前的单次伤害。
        /// 保留 out 参数只是为了不改动既有调用方：D3=A 起普通攻击**永不**随机暴击，
        /// isCritical 恒为 false。暴击只由玩家点中矿石上的暴击点触发，
        /// 走 <see cref="CalculateCritPointDamage"/>。
        /// </summary>
        /// <param name="isCritical">恒为 false，保留仅为兼容旧调用。</param>
        public float CalculatePlayerDamage(out bool isCritical)
        {
            isCritical = false;
            return CalculatePlayerDamage();
        }

        /// <summary>
        /// 计算一次暴击点伤害：普通单次伤害 × 暴击倍数。
        /// 暴击点由 <see cref="CritPointSystem"/> 生成，玩家点中时调用这里，
        /// 而不是在普通攻击里偷偷掷骰子。
        /// </summary>
        public float CalculateCritPointDamage()
        {
            float multiplier = CriticalDamageMultiplier;
            float damage = CalculatePlayerDamage() * multiplier;

            if (logCalculatedDamage)
            {
                Debug.Log($"[DamageSystem] 暴击点伤害 = 普通伤害 × {multiplier} = {damage}");
            }

            return damage;
        }

        /// <summary>编辑器向导调用，把挖矿工具进度接进来。</summary>
        public void ConfigureForSetup(MiningToolProgress tool)
        {
            miningTool = tool;
        }

        /// <summary>编辑器向导调用，把照明工具接进来（暴击倍率由它决定）。</summary>
        public void ConfigureLightTool(MiningLightTool light)
        {
            miningLightTool = light;
        }

        /// <summary>编辑器向导调用，配置没有照明工具时的兜底暴击倍数。</summary>
        public void ConfigureCriticalHit(float multiplier)
        {
            criticalDamageMultiplier = Mathf.Max(1f, multiplier);
        }
    }
}
