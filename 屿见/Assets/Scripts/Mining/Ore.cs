using System.Collections;
using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 矿石：可被点击攻击的目标。
    /// 职责：只负责自身生命值、自身奖励数据、承受伤害、受击表现与死亡。
    /// 不引用 PlayerCurrency、不查找 PlayerCurrency、不修改玩家金币、不处理玩家输入，
    /// 因此本组件可以作为完全独立的 Prefab 使用。
    /// </summary>
    public class Ore : MonoBehaviour
    {
        [Header("数值")]
        [Tooltip("矿石最大生命值，MVP 默认 10")]
        [SerializeField] private float maxHealth = 10f;

        [Tooltip("击碎后由 MiningInput 发放给玩家的金币，MVP 默认 10")]
        [SerializeField] private int reward = 10;

        [Header("受击表现 - 缩放")]
        [Tooltip("参与表现的节点。建议指定一个子物体；留空则使用自身 Transform")]
        [SerializeField] private Transform visualRoot;

        [Tooltip("受击瞬间的缩放倍数")]
        [SerializeField] private float hitScale = 1.25f;

        [Tooltip("一次受击表现的完整时长（秒）")]
        [SerializeField] private float hitFeedbackDuration = 0.12f;

        [Header("受击表现 - 旋转")]
        [Tooltip("受击随机旋转的最小角度（度）")]
        [SerializeField] private float hitRotationMin = -20f;

        [Tooltip("受击随机旋转的最大角度（度）")]
        [SerializeField] private float hitRotationMax = 20f;

        [Header("伤害数字")]
        [Tooltip("伤害数字预制体。留空则在 Console 中提示一次，且不显示伤害数字")]
        [SerializeField] private DamagePopup damagePopupPrefab;

        [Tooltip("伤害数字出现位置相对矿石的偏移")]
        [SerializeField] private Vector3 damagePopupOffset = new Vector3(0f, 1.2f, 0f);

        [Tooltip("伤害数字位置的随机范围（各轴上下浮动该值），该区域在 Scene 视图中用 Gizmos 显示")]
        [SerializeField] private Vector3 damagePopupRandomRange = new Vector3(0.4f, 0.2f, 0.4f);

        [Header("调试")]
        [Tooltip("是否在 Console 打印受击与击碎日志，阶段 1 验收依赖该项")]
        [SerializeField] private bool logMiningFlow = true;

        private float currentHealth;
        private Coroutine hitRoutine;
        private Vector3 baseScale;
        private Quaternion baseRotation;

        /// <summary>当前生命值。</summary>
        public float CurrentHealth => currentHealth;

        /// <summary>最大生命值。</summary>
        public float MaxHealth => maxHealth;

        /// <summary>击碎奖励。由 MiningInput 在 TakeDamage 返回 true 后读取并发放。</summary>
        public int Reward => reward;

        /// <summary>是否存活。存活时才接受伤害。</summary>
        public bool IsAlive => currentHealth > 0f;

        /// <summary>参与表现的实际节点。</summary>
        private Transform Visual => visualRoot != null ? visualRoot : transform;

        private void Awake()
        {
            if (maxHealth <= 0f)
            {
                Debug.LogWarning($"[Ore] 最大生命值配置为非正数({maxHealth})，已按 1 处理。", this);
                maxHealth = 1f;
            }

            if (reward < 0)
            {
                Debug.LogWarning($"[Ore] 奖励配置为负值({reward})，已按 0 处理。", this);
                reward = 0;
            }

            if (hitScale < 1f)
            {
                Debug.LogWarning($"[Ore] 受击缩放倍数小于 1({hitScale})，已按 1 处理。", this);
                hitScale = 1f;
            }

            if (hitRotationMin > hitRotationMax)
            {
                Debug.LogWarning($"[Ore] 受击旋转角下限({hitRotationMin})大于上限({hitRotationMax})，已自动交换。", this);
                (hitRotationMin, hitRotationMax) = (hitRotationMax, hitRotationMin);
            }

            if (damagePopupPrefab == null)
            {
                Debug.LogWarning("[Ore] DamagePopup 预制体未配置，受击时不会显示伤害数字。", this);
            }

            baseScale = Visual.localScale;
            baseRotation = Visual.localRotation;
            currentHealth = maxHealth;

            if (GetComponentsInChildren<Collider>(true).Length == 0)
            {
                Debug.LogError("[Ore] 未找到任何 Collider，该矿石无法被点击。请为本物体添加 Collider。", this);
            }
        }

        /// <summary>
        /// 对矿石施加一次伤害。扣血后会显示伤害数字并播放受击表现；生命值归零则死亡。
        /// 奖励不在这里发放——由调用方读取 <see cref="Reward"/> 后自行发放。
        /// </summary>
        /// <param name="damage">伤害值，由 DamageSystem 计算后传入。</param>
        /// <returns>本次伤害是否导致矿石被击碎。</returns>
        public bool TakeDamage(float damage)
        {
            if (!IsAlive)
            {
                return false;
            }

            if (damage <= 0f)
            {
                Debug.LogWarning($"[Ore] 收到非正数伤害({damage})，已忽略。", this);
                return false;
            }

            currentHealth = Mathf.Max(0f, currentHealth - damage);

            SpawnDamagePopup(damage);
            PlayHitFeedback();

            if (logMiningFlow)
            {
                Debug.Log($"[Ore] {name} 受到 {damage} 点伤害，剩余 {currentHealth}");
            }

            if (currentHealth > 0f)
            {
                return false;
            }

            Die();
            return true;
        }

        /// <summary>
        /// 生命值归零。
        /// 当前阶段直接销毁；未来的掉落物、死亡动画、矿石刷新都接在 Destroy 之前。
        /// </summary>
        private void Die()
        {
            currentHealth = 0f;

            if (logMiningFlow)
            {
                Debug.Log($"[Ore] {name} 已击碎，奖励 {reward} 金币由 MiningInput 发放");
            }

            // 未来接入点：掉落物、死亡动画、Respawn 都写在这一行之前
            Destroy(gameObject);
        }

        /// <summary>
        /// 在矿石上方的随机位置生成一次伤害数字。
        /// 预制体独立于矿石，因此矿石被销毁时伤害数字仍能正常播放完毕。
        /// </summary>
        private void SpawnDamagePopup(float damage)
        {
            if (damagePopupPrefab == null)
            {
                return;
            }

            Vector3 center = transform.position + damagePopupOffset;
            Vector3 jitter = new Vector3(
                Random.Range(-damagePopupRandomRange.x, damagePopupRandomRange.x),
                Random.Range(-damagePopupRandomRange.y, damagePopupRandomRange.y),
                Random.Range(-damagePopupRandomRange.z, damagePopupRandomRange.z));

            DamagePopup popup = Instantiate(damagePopupPrefab, center + jitter, Quaternion.identity);
            popup.Play(damage);
        }

        /// <summary>
        /// 播放受击表现：放大后缩小，同时随机方向旋转随机角度后转回。
        /// 若上一次表现尚未结束就再次受击，将从当前姿态续接并叠加新的角度，不会跳回初始姿态。
        /// </summary>
        private void PlayHitFeedback()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (hitRoutine != null)
            {
                StopCoroutine(hitRoutine);
            }

            hitRoutine = StartCoroutine(HitFeedbackRoutine());
        }

        private IEnumerator HitFeedbackRoutine()
        {
            Transform visual = Visual;

            // 从"当前"姿态出发，保证连续受击时姿态平滑接续
            Vector3 fromScale = visual.localScale;
            Quaternion fromRotation = visual.localRotation;

            Vector3 peakScale = baseScale * hitScale;

            // 当前旋转 × 新的随机角度：连续受击时角度叠加，而不是重置回初始姿态
            Quaternion peakRotation = fromRotation * Quaternion.AngleAxis(
                Random.Range(hitRotationMin, hitRotationMax),
                RandomHorizontalAxis());

            float upTime = Mathf.Max(0.0001f, hitFeedbackDuration * 0.4f);
            float downTime = Mathf.Max(0.0001f, hitFeedbackDuration * 0.6f);

            float elapsed = 0f;
            while (elapsed < upTime)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.Clamp01(elapsed / upTime);
                visual.localScale = Vector3.Lerp(fromScale, peakScale, k);
                visual.localRotation = Quaternion.Slerp(fromRotation, peakRotation, k);
                yield return null;
            }

            Vector3 downFromScale = visual.localScale;
            Quaternion downFromRotation = visual.localRotation;

            elapsed = 0f;
            while (elapsed < downTime)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.Clamp01(elapsed / downTime);
                visual.localScale = Vector3.Lerp(downFromScale, baseScale, k);
                visual.localRotation = Quaternion.Slerp(downFromRotation, baseRotation, k);
                yield return null;
            }

            visual.localScale = baseScale;
            visual.localRotation = baseRotation;
            hitRoutine = null;
        }

        /// <summary>取一个水平面内的随机方向，用于俯视视角下的受击倾斜。</summary>
        private static Vector3 RandomHorizontalAxis()
        {
            Vector3 axis = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
            if (axis.sqrMagnitude < 0.0001f)
            {
                axis = Vector3.right;
            }

            return axis.normalized;
        }

        /// <summary>在 Scene 视图中显示伤害数字的随机范围，方便直接调参。</summary>
        private void OnDrawGizmosSelected()
        {
            Vector3 center = transform.position + damagePopupOffset;

            Gizmos.color = new Color(1f, 0.6f, 0.1f, 1f);
            Gizmos.DrawWireCube(center, damagePopupRandomRange * 2f);
            Gizmos.DrawLine(transform.position, center);
        }
    }
}
