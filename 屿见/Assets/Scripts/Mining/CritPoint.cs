using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 矿石上的暴击点（飞书 2.1.4 照明系统）。
    ///
    /// 由 <see cref="CritPointSystem"/> 在矿石平面上随机生成，玩家点中后由 MiningInput 结算暴击伤害。
    /// 只负责：记住所属矿石、生成/换位动画、脉动表现、存在时间倒计时与进度条、被清理时的自毁。
    /// 存在时间由照明工具的等级表决定；大于 0 时头顶显示剩余时间进度条，
    /// 点中只结算伤害、不消失也不换位，一直保留到时间结束。
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

        [Header("存在时间进度条")]
        [Tooltip("进度条宽度（世界单位）")]
        [SerializeField, Min(0.01f)] private float barWidth = 0.18f;

        [Tooltip("进度条高度（世界单位）")]
        [SerializeField, Min(0.004f)] private float barHeight = 0.026f;

        [Tooltip("进度条相对暴击点向上的偏移（世界单位）")]
        [SerializeField, Min(0f)] private float barOffset = 0.1f;

        private Ore owner;
        private float expireAt;
        private float totalLifetime;
        private bool persistent;
        private Vector3 baseScale;
        private float pulseSeed;
        private float spawnedAt;
        private bool consumed;
        private Transform anchor;
        private Vector3 localAnchorPoint;
        private bool anchored;

        private Transform barRoot;
        private Transform barFill;
        private Camera barCamera;
        private Material barBackgroundMaterial;
        private Material barFillMaterial;

        /// <summary>所属矿石，可能为空（矿石先消失）。</summary>
        public Ore Owner => owner;

        /// <summary>是否已经被点掉 / 到期 / 随矿石清场。</summary>
        public bool IsConsumed => consumed;

        /// <summary>是否永久保留（不显示进度条）。</summary>
        public bool IsPersistent => persistent;

        /// <summary>生成时配置的总存在时间（秒）；永久保留时为 0。</summary>
        public float TotalLifetime => totalLifetime;

        /// <summary>剩余存活秒数；永久保留时返回正无穷，已过期返回 0。</summary>
        public float RemainingLifetime => persistent ? float.PositiveInfinity : Mathf.Max(0f, expireAt - Time.time);
        /// <summary>
        /// 由系统在生成后调用。lifetime &lt;= 0 表示永久保留（不创建进度条）。
        /// 进度条材质由系统共享，避免每个暴击点各自 new 一份。
        /// </summary>
        public void Initialize(Ore ore, float lifetime, Camera camera,
            Material backgroundMaterial, Material fillMaterial)
        {
            owner = ore;
            persistent = lifetime <= 0f;
            totalLifetime = persistent ? 0f : Mathf.Max(0.05f, lifetime);
            expireAt = persistent ? float.PositiveInfinity : Time.time + totalLifetime;
            baseScale = transform.localScale;
            pulseSeed = Random.value * 10f;
            spawnedAt = Time.time;
            barCamera = camera;
            barBackgroundMaterial = backgroundMaterial;
            barFillMaterial = fillMaterial;

            if (!persistent)
            {
                CreateLifetimeBar();
            }
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
        /// 换位：垃圾外壳掉块导致原来的贴点悬空时，把暴击点挪到新的表面点（不销毁对象）。
        /// 重新播放一次「放大→缩小」的生成动画；存在时间不重置。
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
            DestroySafely(barRoot != null ? barRoot.gameObject : null);
            barRoot = null;
            barFill = null;

            DestroySafely(gameObject);
        }

        private void OnDestroy()
        {
            if (barRoot != null)
            {
                DestroySafely(barRoot.gameObject);
                barRoot = null;
                barFill = null;
            }
        }

        /// <summary>
        /// 进度条是两个面片（背景 + 填充），挂在暴击点外层的独立物体上：
        /// 这样暴击点的脉动缩放不会带着进度条一起抖。
        /// </summary>
        private void CreateLifetimeBar()
        {
            GameObject rootObject = new GameObject("CritPointLifetimeBar");
            barRoot = rootObject.transform;

            GameObject background = CreateBarQuad("Background", barBackgroundMaterial);
            background.transform.SetParent(barRoot, false);
            background.transform.localScale = new Vector3(barWidth, barHeight, 1f);

            GameObject fill = CreateBarQuad("Fill", barFillMaterial);
            fill.transform.SetParent(barRoot, false);
            fill.transform.localScale = new Vector3(barWidth, barHeight, 1f);
            // -Z 在相机空间里更靠近镜头，保证填充盖在背景上
            fill.transform.localPosition = new Vector3(0f, 0f, -0.001f);
            barFill = fill.transform;

            UpdateLifetimeBar();
        }

        private static GameObject CreateBarQuad(string name, Material material)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.layer = 0;

            Collider collider = quad.GetComponent<Collider>();

            if (collider != null)
            {
                // 进度条只负责显示，必须立刻退出射线检测，避免挡住暴击点本体
                collider.enabled = false;
                DestroySafely(collider);
            }

            Renderer quadRenderer = quad.GetComponent<Renderer>();

            if (quadRenderer != null && material != null)
            {
                quadRenderer.sharedMaterial = material;
                quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                quadRenderer.receiveShadows = false;
            }

            return quad;
        }

        /// <summary>
        /// 每帧把进度条摆到暴击点头顶、朝向相机，并按剩余时间收缩填充条。
        /// </summary>
        private void UpdateLifetimeBar()
        {
            if (barRoot == null || barFill == null)
            {
                return;
            }

            Camera camera = barCamera != null ? barCamera : Camera.main;

            if (camera == null)
            {
                barRoot.gameObject.SetActive(false);
                return;
            }

            if (!barRoot.gameObject.activeSelf)
            {
                barRoot.gameObject.SetActive(true);
            }

            barRoot.position = transform.position + camera.transform.up * barOffset;
            barRoot.rotation = camera.transform.rotation;

            float fraction = totalLifetime > 0f
                ? Mathf.Clamp01(RemainingLifetime / totalLifetime)
                : 0f;

            float fillWidth = barWidth * fraction;
            barFill.localScale = new Vector3(fillWidth, barHeight, 1f);
            // 填充条左对齐：从左边沿开始向右缩短
            barFill.localPosition = new Vector3((-barWidth + fillWidth) * 0.5f, 0f, -0.001f);
        }

        private static void DestroySafely(Object target)
        {
            if (target == null)
            {
                return;
            }

            // 编辑器里跑批处理验证时 Destroy 不被允许，直接用 DestroyImmediate
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
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
        /// 在矿石受击动画更新之后再同步位置，避免暴击点比垃圾慢一帧；
        /// 进度条也放在这里更新，保证它不会比暴击点本体晚一帧。
        /// </summary>
        private void LateUpdate()
        {
            if (consumed)
            {
                return;
            }

            if (anchored && anchor != null)
            {
                transform.position = anchor.TransformPoint(localAnchorPoint);
            }

            UpdateLifetimeBar();
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