using UnityEngine;
using Yujian.Core;
using Yujian.Core.Events;

namespace Yujian.Lighting
{
    /// <summary>
    /// 矿洞第一人称手持道具（占位版）。
    ///
    /// 左手提灯、右手拿镐，挂在主相机下面，只在挖矿区显示：
    ///   · 提灯自带一盏暖色点光，进入矿洞时随区域过渡渐亮，离开时渐灭；
    ///   · 点矿石时右手镐会挥一下，提灯跟着轻轻晃；
    ///   · 道具位置按相机 FOV 与屏幕比例换算，换分辨率不会跑出画面。
    ///
    /// 显隐与亮度只跟随 <see cref="GameArea"/>，不碰矿石、金币和相机移动。
    /// 模型全部由基础几何体拼成，美术可直接替换 Pivot 下的子物体。
    /// </summary>
    [DisallowMultipleComponent]
    public class MiningHandheldRig : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("区域状态。进入 / 离开挖矿区时控制手持道具显隐与提灯亮度")]
        [SerializeField] private GameAreaController areaController;

        [Tooltip("主相机。留空则运行时自动取 Camera.main")]
        [SerializeField] private Camera viewCamera;

        [Header("手持道具")]
        [Tooltip("左手提灯的根节点。位置由本类按屏幕比例摆放")]
        [SerializeField] private Transform lanternPivot;

        [Tooltip("右手镐的根节点。点矿石时绕它做挥动")]
        [SerializeField] private Transform toolPivot;

        [Tooltip("提灯里的点光")]
        [SerializeField] private Light handLight;

        [Tooltip("提灯发光核心，会随火光轻微呼吸")]
        [SerializeField] private Transform lanternGlow;

        [Header("屏幕位置")]
        [Tooltip("提灯在画面中的位置。X / Y 是相对半屏的比例，Z 是离相机的距离（米）")]
        [SerializeField] private Vector2 lanternAnchor = new Vector2(-0.56f, -0.92f);

        [Tooltip("提灯离相机的距离。太小会被近裁剪面切掉，太大会被矿石挡住")]
        [SerializeField, Min(0.2f)] private float lanternDistance = 0.92f;

        [Tooltip("镐在画面中的位置。X / Y 是相对半屏的比例")]
        [SerializeField] private Vector2 toolAnchor = new Vector2(0.60f, -0.96f);

        [Tooltip("镐离相机的距离")]
        [SerializeField, Min(0.2f)] private float toolDistance = 0.94f;

        [Header("过渡")]
        [Tooltip("进入 / 离开矿洞时提灯渐亮渐灭的秒数。建议与矿洞照明保持一致")]
        [SerializeField, Min(0.01f)] private float transitionDuration = 0.6f;

        [Tooltip("道具模型在过渡到多少比例时出现。0.8 表示镜头基本到位后才出现")]
        [SerializeField, Range(0f, 1f)] private float showThreshold = 0.8f;

        [Header("提灯")]
        [Tooltip("火光闪烁幅度，0 表示不闪")]
        [SerializeField, Range(0f, 0.5f)] private float flickerAmount = 0.07f;

        [Tooltip("火光闪烁速度")]
        [SerializeField, Min(0f)] private float flickerSpeed = 5.5f;

        [Tooltip("提灯待机时的最大摆动角度")]
        [SerializeField, Range(0f, 10f)] private float idleSwayDegrees = 1.8f;

        [Tooltip("待机摆动速度")]
        [SerializeField, Min(0f)] private float idleSwaySpeed = 0.7f;

        [Header("挥镐")]
        [Tooltip("一次挥镐的时长（秒）")]
        [SerializeField, Min(0.05f)] private float swingDuration = 0.34f;

        [Tooltip("镐头向前压的角度")]
        [SerializeField] private float swingPitch = 38f;

        [Tooltip("挥镐时附带的一点侧向角度")]
        [SerializeField] private float swingRoll = -10f;

        [Header("调试")]
        [SerializeField] private bool logHandheldFlow = true;

        private Renderer[] propRenderers;
        private Quaternion lanternBaseRotation;
        private Quaternion toolBaseRotation;
        private Vector3 glowBaseScale;
        private float baseLightIntensity;

        private float blend;
        private float targetBlend;
        private float noiseSeed;
        private float swingTime = -1f;
        private bool propsVisible;
        private int lastScreenWidth;
        private int lastScreenHeight;
        private bool warnedMissingSetup;

        private void Awake()
        {
            noiseSeed = Random.value * 100f;

            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }

            if (lanternPivot != null)
            {
                lanternBaseRotation = lanternPivot.localRotation;
            }

            if (toolPivot != null)
            {
                toolBaseRotation = toolPivot.localRotation;
            }

            if (lanternGlow != null)
            {
                glowBaseScale = lanternGlow.localScale;
            }

            if (handLight != null)
            {
                baseLightIntensity = handLight.intensity;
                handLight.intensity = 0f;
                handLight.enabled = false;
            }

            propRenderers = GetComponentsInChildren<Renderer>(true);

            // 先假设可见，再关一次：SetPropsVisible 内部有“状态没变就跳过”的短路，
            // 若从默认 false 直接调 false，场景里的模型会一直亮着。
            propsVisible = true;
            SetPropsVisible(false);
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OreHit>(HandleOreHit);

            if (areaController != null)
            {
                areaController.OnAreaChanged += HandleAreaChanged;

                // 组件在挖矿区被重新启用时，让道具重新淡入，而不是停在隐藏状态。
                if (blend < 0.001f)
                {
                    targetBlend = areaController.CurrentArea == GameArea.Mining ? 1f : 0f;
                }
            }
        }

        private void Start()
        {
            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }

            LayoutProps();

            if (areaController == null)
            {
                WarnMissingSetupOnce();
                return;
            }

            bool mining = areaController.CurrentArea == GameArea.Mining;
            blend = mining ? 1f : 0f;
            targetBlend = blend;
            ApplyBlend(blend);

            if (logHandheldFlow)
            {
                Debug.Log($"[MiningHandheld] 初始区域：{areaController.CurrentArea.ToChineseName()}，" +
                          (mining ? "手持道具显示。" : "手持道具隐藏。"));
            }
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OreHit>(HandleOreHit);

            if (areaController != null)
            {
                areaController.OnAreaChanged -= HandleAreaChanged;
            }

            blend = 0f;
            targetBlend = 0f;
            swingTime = -1f;
            ApplyBlend(0f);

            if (lanternPivot != null)
            {
                lanternPivot.localRotation = lanternBaseRotation;
            }

            if (toolPivot != null)
            {
                toolPivot.localRotation = toolBaseRotation;
            }
        }

        private void Update()
        {
            if (viewCamera != null &&
                (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight))
            {
                LayoutProps();
            }

            if (!Mathf.Approximately(blend, targetBlend))
            {
                blend = Mathf.MoveTowards(blend, targetBlend,
                    Time.unscaledDeltaTime / transitionDuration);
            }

            float swingArc = EvaluateSwingArc();
            ApplyIdleMotion(swingArc);
            ApplyBlend(blend);
        }

        private void HandleAreaChanged(GameArea area)
        {
            bool mining = area == GameArea.Mining;
            targetBlend = mining ? 1f : 0f;

            if (logHandheldFlow)
            {
                Debug.Log($"[MiningHandheld] 区域切换为 {area.ToChineseName()}：" +
                          (mining ? "提灯渐亮、道具准备出现。" : "提灯渐灭、道具隐藏。"));
            }
        }

        private void HandleOreHit(OreHit hit)
        {
            if (areaController == null || areaController.CurrentArea != GameArea.Mining)
            {
                return;
            }

            // 连续点击时不重置进度，避免挥镐动作一帧跳回起点。
            if (swingTime < 0f)
            {
                swingTime = 0f;
            }
        }

        private float EvaluateSwingArc()
        {
            if (swingTime < 0f)
            {
                return 0f;
            }

            swingTime += Time.deltaTime;
            float k = swingTime / swingDuration;

            if (k >= 1f)
            {
                swingTime = -1f;
                return 0f;
            }

            const float downPhase = 0.32f;

            if (k < downPhase)
            {
                return Mathf.SmoothStep(0f, 1f, k / downPhase);
            }

            return 1f - Mathf.SmoothStep(0f, 1f, (k - downPhase) / (1f - downPhase));
        }

        private void ApplyIdleMotion(float swingArc)
        {
            if (toolPivot != null)
            {
                toolPivot.localRotation = toolBaseRotation *
                    Quaternion.Euler(swingPitch * swingArc, 0f, swingRoll * swingArc);
            }

            if (lanternPivot != null)
            {
                float sway = (Mathf.PerlinNoise(noiseSeed, Time.time * idleSwaySpeed) - 0.5f)
                    * 2f * idleSwayDegrees;

                lanternPivot.localRotation = lanternBaseRotation *
                    Quaternion.Euler(0f, 0f, sway - swingArc * 2.5f);
            }
        }

        private void ApplyBlend(float value)
        {
            float clamped = Mathf.Clamp01(value);
            SetPropsVisible(clamped >= showThreshold);

            if (handLight == null)
            {
                return;
            }

            bool lightOn = clamped > 0.001f;

            if (handLight.enabled != lightOn)
            {
                handLight.enabled = lightOn;
            }

            if (!lightOn)
            {
                handLight.intensity = 0f;
                return;
            }

            float flicker = 1f + (Mathf.PerlinNoise(noiseSeed + 11.7f, Time.time * flickerSpeed) - 0.5f)
                * 2f * flickerAmount;
            handLight.intensity = baseLightIntensity * clamped * flicker;

            if (lanternGlow != null)
            {
                float breathe = 1f + (Mathf.PerlinNoise(noiseSeed + 5.1f, Time.time * flickerSpeed * 0.6f)
                    - 0.5f) * 0.12f;
                lanternGlow.localScale = glowBaseScale * (breathe * Mathf.Lerp(0.85f, 1f, clamped));
            }
        }

        private void SetPropsVisible(bool visible)
        {
            if (propsVisible == visible)
            {
                return;
            }

            propsVisible = visible;

            if (propRenderers == null)
            {
                return;
            }

            for (int i = 0; i < propRenderers.Length; i++)
            {
                if (propRenderers[i] != null)
                {
                    propRenderers[i].enabled = visible;
                }
            }
        }

        private void LayoutProps()
        {
            if (viewCamera == null)
            {
                return;
            }

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;

            float aspect = viewCamera.aspect > 0.01f ? viewCamera.aspect : 16f / 9f;

            if (lanternPivot != null)
            {
                float halfHeight = Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad)
                    * lanternDistance;
                float halfWidth = halfHeight * aspect;
                lanternPivot.localPosition = new Vector3(
                    lanternAnchor.x * halfWidth, lanternAnchor.y * halfHeight, lanternDistance);
            }

            if (toolPivot != null)
            {
                float halfHeight = Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad)
                    * toolDistance;
                float halfWidth = halfHeight * aspect;
                toolPivot.localPosition = new Vector3(
                    toolAnchor.x * halfWidth, toolAnchor.y * halfHeight, toolDistance);
            }
        }

        private void WarnMissingSetupOnce()
        {
            if (warnedMissingSetup)
            {
                return;
            }

            warnedMissingSetup = true;
            Debug.LogWarning("[MiningHandheld] GameAreaController 未配置，手持提灯不会随区域显隐。" +
                             "请运行菜单「屿见/挖矿/配置手持提灯与挖矿工具（阶段 13）」。", this);
        }

        /// <summary>编辑器向导调用：一次性接好区域、相机和道具引用。</summary>
        public void ConfigureForSetup(GameAreaController controller, Camera camera,
            Transform lantern, Transform tool, Light light, Transform glow)
        {
            areaController = controller;
            viewCamera = camera;
            lanternPivot = lantern;
            toolPivot = tool;
            handLight = light;
            lanternGlow = glow;
        }
    }
}