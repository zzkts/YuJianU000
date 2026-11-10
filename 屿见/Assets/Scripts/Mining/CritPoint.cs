using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 矿石上的暴击点（飞书 2.1.4 照明系统）。
    ///
    /// 由 <see cref="CritPointSystem"/> 在矿石平面上随机生成，玩家点中后由 MiningInput 结算暴击伤害。
    /// 只负责：记住所属矿石、生成/换位动画、脉动表现、被清理时的自毁。
    /// 停留时间传 0 表示永久保留到被点击，符合飞书「生成后保持不变」的规则。
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

        [Tooltip("生成时先放大到该倍数，再回落到 1")]
        [SerializeField, Min(1f)] private float spawnOvershoot = 1.25f;

        [Tooltip("生成动画时长（秒）")]
        [SerializeField, Min(0.01f)] private float spawnDuration = 0.28f;

        [Tooltip("倒计时模式下，到期前多少秒开始缩小；永久模式不使用")]
        [SerializeField, Min(0f)] private float shrinkWarningDuration = 0.5f;

        private Ore owner;
        private float expireAt;
        private bool persistent;
        private Vector3 baseScale;
        private float pulseSeed;
        private float spawnedAt;
        private bool consumed;
        private Transform anchor;
        private Vector3 localAnchorPoint;
        private bool anchored;

        /// <summary>所属矿石，可能为空（矿石先消失）。</summary>
        public Ore Owner => owner;

        /// <summary>是否已经被点掉 / 到期 / 随矿石清场。</summary>
        public bool IsConsumed => consumed;

        /// <summary>是否永久保留到被点击。</summary>
        public bool IsPersistent => persistent;

        /// <summary>剩余存活秒数；永久保留时返回正无穷，已过期返回 0。</summary>
        public float RemainingLifetime => persistent ? float.PositiveInfinity : Mathf.Max(0f, expireAt - Time.time);
        /// <summary>由系统在生成后调用。lifetime &lt;= 0 表示永久保留。</summary>
        public void Initialize(Ore ore, float lifetime)
        {
            owner = ore;
            persistent = lifetime <= 0f;
            expireAt = persistent ? float.PositiveInfinity : Time.time + Mathf.Max(0.05f, lifetime);
            baseScale = transform.localScale;
            pulseSeed = Random.value * 10f;
            spawnedAt = Time.time;
        }

        /// <summary>
        /// 挂到某块垃圾上：之后每帧跟随该块的 Transform。
        /// 矿石受击缩放 / 旋转、垃圾块被摘下来时都不会脱离表面。
        /// </summary>
        public void Attach(Transform newAnchor, Vector3 localPoint)
        {
            anchor = newAnchor;
            localAnchorPoint = localPoint;
            anchored = newAnchor != null;

            if (anchored)
            {
                transform.position = anchor.TransformPoint(localAnchorPoint);
            }
        }

        /// <summary>
        /// 换位：点中后暴击点消失，并在新位置重新出现（不销毁对象）。
        /// 重新播放一次「放大→缩小」的生成动画。
        /// </summary>
        public void Relocate(Vector3 worldPosition, Transform newAnchor, Vector3 localPoint)
        {
            if (consumed)
            {
                return;
            }

            anchor = newAnchor;
            localAnchorPoint = localPoint;
            anchored = newAnchor != null;
            transform.position = anchored ? anchor.TransformPoint(localAnchorPoint) : worldPosition;
            spawnedAt = Time.time;
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

            if (!persistent && Time.time >= expireAt)
            {
                Consume();
                return;
            }

            if (anchored && anchor == null)
            {
                // 挂靠的垃圾块已经被收走 / 销毁，点跟着消失，不要留在半空。
                Consume();
                return;
            }

            float pulse = 1f + Mathf.Sin((Time.time + pulseSeed) * pulseSpeed) * pulseAmount;
            float spawn = EvaluateSpawnScale();
            float shrink = 1f;

            if (!persistent && shrinkWarningDuration > 0f)
            {
                float remaining = expireAt - Time.time;

                if (remaining < shrinkWarningDuration)
                {
                    shrink = Mathf.Clamp01(remaining / shrinkWarningDuration);
                }
            }

            transform.localScale = baseScale * pulse * spawn * shrink;
        }

        /// <summary>
        /// 在矿石受击动画更新之后再同步位置，避免暴击点比垃圾慢一帧。
        /// </summary>
        private void LateUpdate()
        {
            if (consumed || !anchored || anchor == null)
            {
                return;
            }

            transform.position = anchor.TransformPoint(localAnchorPoint);
        }

        /// <summary>生成动画：0 → 1.25 倍 → 1 倍，平滑过渡。</summary>
        private float EvaluateSpawnScale()
        {
            if (spawnDuration <= 0f)
            {
                return 1f;
            }

            float t = Mathf.Clamp01((Time.time - spawnedAt) / spawnDuration);

            if (t >= 1f)
            {
                return 1f;
            }

            if (t < 0.6f)
            {
                return Mathf.SmoothStep(0f, spawnOvershoot, t / 0.6f);
            }

            return Mathf.SmoothStep(spawnOvershoot, 1f, (t - 0.6f) / 0.4f);
        }
    }
}