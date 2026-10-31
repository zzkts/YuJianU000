using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 矿石上的暴击点（D3=A）。
    ///
    /// 由 <see cref="CritPointSystem"/> 在「穿过矿石中心的水平面」上生成，
    /// 玩家点中后由 MiningInput 按暴击倍数结算伤害。
    ///
    /// 只负责：记住所属矿石、倒计时、脉动表现、到期自毁。
    /// 不计算伤害、不发金币、不处理输入。
    /// </summary>
    [DisallowMultipleComponent]
    public class CritPoint : MonoBehaviour
    {
        [Header("表现")]
        [Tooltip("脉动幅度。0.12 表示大小在 ±12% 之间来回")]
        [SerializeField, Range(0f, 0.5f)] private float pulseAmount = 0.12f;

        [Tooltip("脉动速度")]
        [SerializeField, Min(0f)] private float pulseSpeed = 8f;

        [Tooltip("到期前多少秒开始缩小，给出即将消失的提示。0 表示不缩")]
        [SerializeField, Min(0f)] private float shrinkWarningDuration = 0.5f;

        private Ore owner;
        private float expireAt;
        private Vector3 baseScale;
        private float pulseSeed;
        private bool consumed;

        /// <summary>所属矿石，可能为空（矿石先消失）。</summary>
        public Ore Owner => owner;

        /// <summary>是否已经被点掉 / 过期 / 随矿石清场。</summary>
        public bool IsConsumed => consumed;

        /// <summary>剩余存活秒数，已过期返回 0。</summary>
        public float RemainingLifetime => Mathf.Max(0f, expireAt - Time.time);

        /// <summary>由系统在生成后调用。lifetime 必须为正。</summary>
        public void Initialize(Ore ore, float lifetime)
        {
            owner = ore;
            expireAt = Time.time + Mathf.Max(0.05f, lifetime);
            baseScale = transform.localScale;
            pulseSeed = Random.value * 10f;
        }

        /// <summary>立即消失。重复调用安全。</summary>
        public void Consume()
        {
            if (consumed)
            {
                return;
            }

            consumed = true;

            // 编辑器里跑批处理验证时 Destroy 不被允许，直接用 DestroyImmediate
            if (Application.isPlaying)
            {
                Destroy(gameObject);
            }
            else
            {
                DestroyImmediate(gameObject);
            }
        }

        private void Update()
        {
            if (consumed)
            {
                return;
            }

            // 矿石被打碎 / 隐藏时，暴击点跟着消失，避免点到空气
            if (owner == null || !owner.IsAlive)
            {
                Consume();
                return;
            }

            if (Time.time >= expireAt)
            {
                Consume();
                return;
            }

            float remaining = expireAt - Time.time;
            float pulse = 1f + Mathf.Sin((Time.time + pulseSeed) * pulseSpeed) * pulseAmount;
            float shrink = 1f;

            if (shrinkWarningDuration > 0f && remaining < shrinkWarningDuration)
            {
                shrink = Mathf.Clamp01(remaining / shrinkWarningDuration);
            }

            transform.localScale = baseScale * pulse * shrink;
        }
    }
}
