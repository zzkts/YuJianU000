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

        [Header("刷新")]
        [Tooltip("所有矿石都被击碎后，再等多少秒一起刷新。设为 0 则保持原来的直接销毁。")]
        [SerializeField] private float respawnDelay = 6f;

        [Tooltip("刷新时随机换一种矿石，需要物体上挂了 OreTypeTag")]
        [SerializeField] private bool randomizeTypeOnRespawn = true;

        [Tooltip("刷新时从零放大到正常大小的时长（秒）")]
        [SerializeField] private float respawnPopDuration = 0.35f;

        [Tooltip("由外部生成器（OreSpawner）接管刷新。为 true 时死亡不再自毁、不再自动刷新，" +
                 "只隐藏并广播 OnDied，由生成器调用 ApplyDefinition 生成下一块")]
        [SerializeField] private bool externalSpawnControl = false;

        [Header("生成表现")]
        [Tooltip("生成时从零放大到峰值的时长（秒）")]
        [SerializeField] private float spawnGrowDuration = 0.25f;

        [Tooltip("生成时从峰值缩回正常大小的时长（秒）")]
        [SerializeField] private float spawnShrinkDuration = 0.15f;

        [Tooltip("生成时放大到的峰值倍数（相对正常大小）")]
        [SerializeField] private float spawnPeakScale = 1.15f;

        private float currentHealth;
        private Coroutine hitRoutine;
        private Coroutine spawnRoutine;
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

        /// <summary>是否已交给外部生成器接管生成与刷新。</summary>
        public bool UsesExternalSpawnControl => externalSpawnControl;

        /// <summary>矿石被击碎时广播（参数为自身）。外部生成器用它接续生成下一块。</summary>
        public event System.Action<Ore> OnDied;

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
        /// <param name="source">伤害来源，只用于决定伤害数字的样式（策划案 2.1-3）。</param>
        /// <returns>本次伤害是否导致矿石被击碎。</returns>
        public bool TakeDamage(float damage, DamageSource source)
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

            SpawnDamagePopup(damage, source);
            PlayHitFeedback();

            OreHitFlash hitFlash = GetComponent<OreHitFlash>();
            if (hitFlash != null) hitFlash.Play();

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
        /// 用一条矿石定义重新配置本矿石并让它出现。只由外部生成器（OreSpawner）调用。
        /// 会重置生命值、奖励、种类与姿态，并播放"放大后缩小"的生成表现。
        /// </summary>
        /// <param name="type">矿石种类，会同步给 OreTypeTag 并重新上色。</param>
        /// <param name="newMaxHealth">新的最大生命值，必须为正数。</param>
        /// <param name="newReward">新的击碎奖励，不能为负。</param>
        public void ApplyDefinition(OreType type, float newMaxHealth, int newReward)
        {
            if (newMaxHealth <= 0f)
            {
                Debug.LogWarning($"[Ore] 生成的矿石生命值配置为非正数({newMaxHealth})，已按 1 处理。", this);
                newMaxHealth = 1f;
            }

            if (newReward < 0)
            {
                Debug.LogWarning($"[Ore] 生成的矿石奖励配置为负值({newReward})，已按 0 处理。", this);
                newReward = 0;
            }

            // 停掉上一次受击 / 生成表现，避免两个协程同时改 scale
            if (hitRoutine != null)
            {
                StopCoroutine(hitRoutine);
                hitRoutine = null;
            }

            if (spawnRoutine != null)
            {
                StopCoroutine(spawnRoutine);
                spawnRoutine = null;
            }

            maxHealth = newMaxHealth;
            currentHealth = maxHealth;
            reward = newReward;

            OreTypeTag typeTag = GetComponent<OreTypeTag>();
            if (typeTag != null)
            {
                typeTag.SetType(type);
            }
            else
            {
                Debug.LogWarning("[Ore] 缺少 OreTypeTag，生成的矿石无法切换种类与颜色。", this);
            }

            Transform visual = Visual;
            visual.localRotation = baseRotation;
            visual.localScale = baseScale;

            SetOreVisible(true);

            if (isActiveAndEnabled && spawnGrowDuration + spawnShrinkDuration > 0f)
            {
                // 先生成在零尺寸，避免生成动画开始前闪一帧正常大小
                visual.localScale = Vector3.zero;
                spawnRoutine = StartCoroutine(SpawnPopRoutine());
            }
        }

        /// <summary>
        /// 生成表现：从零平滑放大到峰值，再缩回正常大小。
        /// </summary>
        private IEnumerator SpawnPopRoutine()
        {
            Transform visual = Visual;

            float growDuration = Mathf.Max(0f, spawnGrowDuration);
            float shrinkDuration = Mathf.Max(0f, spawnShrinkDuration);
            Vector3 peakScale = baseScale * Mathf.Max(1f, spawnPeakScale);

            float elapsed = 0f;
            while (elapsed < growDuration)
            {
                elapsed += Time.deltaTime;
                float k = growDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / growDuration);
                visual.localScale = Vector3.Lerp(Vector3.zero, peakScale, k);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < shrinkDuration)
            {
                elapsed += Time.deltaTime;
                float k = shrinkDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / shrinkDuration);
                visual.localScale = Vector3.Lerp(peakScale, baseScale, k);
                yield return null;
            }

            visual.localScale = baseScale;
            spawnRoutine = null;
        }

        /// <summary>
        /// 生命值归零。默认原地延时刷新；respawnDelay 设为 0 时保持直接销毁。
        /// 开启 externalSpawnControl 时只隐藏并广播 OnDied，由外部生成器接续。
        /// </summary>
        private void Die()
        {
            currentHealth = 0f;

            if (logMiningFlow)
            {
                Debug.Log($"[Ore] {name} 已击碎，奖励 {reward} 金币由 MiningInput 发放");
            }

            // 击碎表现：碎片与彩蛋都独立于矿石本体，可以在 Destroy 前触发
            OreShatter shatter = GetComponent<OreShatter>();
            if (shatter != null) shatter.Burst();

            CulturalFragmentDropper dropper = GetComponent<CulturalFragmentDropper>();
            if (dropper != null) dropper.Roll();

            if (externalSpawnControl)
            {
                SetOreVisible(false);

                if (OnDied == null)
                {
                    Debug.LogError("[Ore] 已开启外部生成控制，但没有任何生成器订阅 OnDied，" +
                                   "这块矿石会一直隐藏。请检查 OreSpawner 是否已接线。", this);
                }
            }

            OnDied?.Invoke(this);

            if (externalSpawnControl)
            {
                return;
            }

            if (respawnDelay <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            StartCoroutine(RespawnRoutine());
        }

        /// <summary>
        /// 隐藏矿石，等待一段时间后恢复血量并重新出现。
        /// 隐藏期间 IsAlive 为 false，MiningInput 打不到，建筑自动攻击也会跳过。
        /// </summary>
        private IEnumerator RespawnRoutine()
        {
            SetOreVisible(false);

            // 等场上其他矿石也被击碎，避免这块刚碎就单独长回来
            while (AnyOtherOreAlive())
            {
                yield return new WaitForSeconds(0.2f);
            }

            yield return new WaitForSeconds(respawnDelay);

            currentHealth = maxHealth;

            OreTypeTag typeTag = GetComponent<OreTypeTag>();
            if (randomizeTypeOnRespawn && typeTag != null)
            {
                typeTag.RandomizeType();
            }

            SetOreVisible(true);
            yield return RespawnPopRoutine();

            if (logMiningFlow)
            {
                Debug.Log($"[Ore] {name} 已刷新，生命值 {currentHealth}");
            }
        }

        /// <summary>场上是否还有别的存活矿石。用来把刷新做成“一波清完再整批重生”。</summary>
        private bool AnyOtherOreAlive()
        {
#if UNITY_2023_1_OR_NEWER
            Ore[] ores = Object.FindObjectsByType<Ore>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            Ore[] ores = Object.FindObjectsOfType<Ore>();
#endif

            for (int i = 0; i < ores.Length; i++)
            {
                if (ores[i] != this && ores[i].IsAlive)
                {
                    return true;
                }
            }

            return false;
        }

        private void SetOreVisible(bool visible)
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = visible;
            }

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = visible;
            }
        }

        private IEnumerator RespawnPopRoutine()
        {
            if (respawnPopDuration <= 0f)
            {
                Visual.localScale = baseScale;
                yield break;
            }

            Transform visual = Visual;
            float elapsed = 0f;
            while (elapsed < respawnPopDuration)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.Clamp01(elapsed / respawnPopDuration);
                visual.localScale = Vector3.Lerp(Vector3.zero, baseScale, k * k);
                yield return null;
            }

            visual.localScale = baseScale;
        }

        /// <summary>
        /// 在矿石上方的随机位置生成一次伤害数字。
        /// 预制体独立于矿石，因此矿石被销毁时伤害数字仍能正常播放完毕。
        /// </summary>
        private void SpawnDamagePopup(float damage, DamageSource source)
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
            popup.Play(damage, source);
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
