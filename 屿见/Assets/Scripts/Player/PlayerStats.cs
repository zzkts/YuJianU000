using UnityEngine;

namespace Yujian.Player
{
    /// <summary>
    /// 玩家属性。
    /// 职责：只持有玩家的基础攻击力与伤害倍率，供 DamageSystem 读取。
    /// 不参与伤害计算、不接触货币、不处理输入。
    /// </summary>
    public class PlayerStats : MonoBehaviour
    {
        [Header("攻击属性")]
        [Tooltip("玩家基础攻击力，MVP 默认 1")]
        [SerializeField] private float baseAttack = 1f;

        [Tooltip("玩家伤害倍率，MVP 默认 1。最终伤害 = 基础攻击力 × 该值；后续由材料等来源提升")]
        [SerializeField] private float damageMultiplier = 1f;

        /// <summary>基础攻击力。</summary>
        public float BaseAttack => baseAttack;

        /// <summary>伤害倍率。</summary>
        public float DamageMultiplier => damageMultiplier;

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
