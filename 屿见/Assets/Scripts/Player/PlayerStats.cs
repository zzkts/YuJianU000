using System;
using UnityEngine;

namespace Yujian.Player
{
    /// <summary>
    /// 玩家属性。
    /// 职责：只持有玩家的基础攻击力与伤害倍率，供 DamageSystem 读取。
    /// 不参与伤害计算、不接触货币、不处理输入。
    ///
    /// 伤害倍率分两层：Inspector 里的基础值 + 运行时由建筑加成写进来的附加项。
    /// 附加项不写进存档——读档后由 BuildingDamageBonus 按场上建筑重新汇总，
    /// 避免「存档里的倍率」和「建筑算出来的倍率」两份真相。
    /// </summary>
    public class PlayerStats : MonoBehaviour
    {
        [Header("攻击属性")]
        [Tooltip("玩家基础攻击力，MVP 默认 1")]
        [SerializeField] private float baseAttack = 1f;

        [Tooltip("玩家基础伤害倍率，MVP 默认 1。最终伤害 = 基础攻击力 ×（基础倍率 + 建筑加成）")]
        [SerializeField] private float damageMultiplier = 1f;

        /// <summary>运行时由建筑等来源写入的附加倍率（D1=B：只有材料被建成消耗才计入）。</summary>
        private float bonusDamageMultiplier;

        /// <summary>基础攻击力。</summary>
        public float BaseAttack => baseAttack;

        /// <summary>基础伤害倍率（不含建筑加成）。</summary>
        public float BaseDamageMultiplier => damageMultiplier;

        /// <summary>建筑加成的附加倍率。</summary>
        public float BonusDamageMultiplier => bonusDamageMultiplier;

        /// <summary>最终伤害倍率 = 基础倍率 + 建筑加成，不会低于 0。</summary>
        public float DamageMultiplier => Mathf.Max(0f, damageMultiplier + bonusDamageMultiplier);

        /// <summary>最终伤害倍率变化时广播（参数为新值）。</summary>
        public event Action<float> OnDamageMultiplierChanged;

        /// <summary>
        /// 设置建筑附加倍率。由 <see cref="BuildingDamageBonus"/> 在建筑增删 / 读档后重新汇总。
        /// 传 0 就是清空加成。
        /// </summary>
        public void SetBonusDamageMultiplier(float bonus)
        {
            float sanitized = Mathf.Max(-damageMultiplier, bonus);

            if (Mathf.Approximately(sanitized, bonusDamageMultiplier))
            {
                return;
            }

            bonusDamageMultiplier = sanitized;
            OnDamageMultiplierChanged?.Invoke(DamageMultiplier);
        }

        private void Awake()
        {
            if (baseAttack < 0f)
            {
                Debug.LogWarning($"[PlayerStats] 基础攻击力配置为负值({baseAttack})，已按 0 处理。", this);
                baseAttack = 0f;
            }

            if (damageMultiplier < 0f)
            {
                Debug.LogWarning($"[PlayerStats] 伤害倍率配置为负值({damageMultiplier})，已按 0 处理。", this);
                damageMultiplier = 0f;
            }
        }
    }
}
