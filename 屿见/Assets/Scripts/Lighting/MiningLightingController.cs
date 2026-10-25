using UnityEngine;

namespace Yujian.Lighting
{
    using Yujian.Core;

    /// <summary>
    /// 矿洞照明。
    /// 职责：按当前区域切换「岛上白天」与「矿洞夜晚」两套光照。
    ///
    /// 进矿洞：太阳变暗变冷、环境光变暗、相机背景改为近黑色，并点亮矿灯；
    /// 出矿洞：全部恢复成进 Play 时记录的原值。
    ///
    /// 只改渲染状态，不碰矿石、金币、相机位置。相机位置仍由 AreaCameraDirector 负责。
    /// </summary>
    [DisallowMultipleComponent]
    public class MiningLightingController : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("区域控制器。根据当前区域决定用白天还是矿洞光照")]
        [SerializeField] private GameAreaController areaController;

        [Tooltip("岛上的平行光（太阳）。留空则不处理太阳")]
        [SerializeField] private Light sunLight;

        [Tooltip("主相机。留空则运行时自动取 Camera.main")]
        [SerializeField] private Camera viewCamera;

        [Header("矿洞氛围")]
        [SerializeField] private Color miningSunColor = new Color(0.55f, 0.62f, 0.78f, 1f);

        [Tooltip("矿洞里太阳保留的强度。0 表示完全关掉")]
        [SerializeField, Min(0f)] private float miningSunIntensity = 0.08f;

        [Tooltip("矿洞里的环境光颜色")]
        [SerializeField] private Color miningAmbient = new Color(0.05f, 0.06f, 0.08f, 1f);

        [Tooltip("矿洞里的相机背景色（代替天空盒）")]
        [SerializeField] private Color miningBackground = new Color(0.015f, 0.014f, 0.02f, 1f);

        [Header("过渡")]
        [Tooltip("白天 ↔ 矿洞的过渡时长（秒）")]
        [SerializeField, Min(0.01f)] private float transitionDuration = 0.6f;

        [Header("矿灯闪烁")]
        [Tooltip("矿灯强度的闪烁幅度，0 表示不闪")]
        [SerializeField, Range(0f, 0.3f)] private float flickerAmount = 0.06f;

        [Tooltip("闪烁速度")]
        [SerializeField, Min(0f)] private float flickerSpeed = 2.2f;

        [Header("调试")]
        [SerializeField] private bool logLightingFlow = true;

        private Light[] miningLights;
        private Renderer[] miningVisuals;
        private float[] baseIntensities;

        private Color originalSunColor;
        private float originalSunIntensity;
        private Color originalAmbient;
        private UnityEngine.Rendering.AmbientMode originalAmbientMode;
        private CameraClearFlags originalClearFlags;
        private Color originalBackground;

        private bool ambientOverridden;
        private float blend;
        private float targetBlend;
        private float noiseSeed;
        private bool warnedMissingSetup;

        private void Awake()
        {
            noiseSeed = Random.value * 100f;

            // 矿灯与灯笼都放在本物体下面，自动收集，不需要逐个拖引用
            miningLights = GetComponentsInChildren<Light>(true);
            miningVisuals = GetComponentsInChildren<Renderer>(true);
            baseIntensities = new float[miningLights.Length];

            for (int i = 0; i < miningLights.Length; i++)
            {
                baseIntensities[i] = miningLights[i].intensity;
                miningLights[i].intensity = 0f;
            }

            if (sunLight != null)
            {
                originalSunColor = sunLight.color;
                originalSunIntensity = sunLight.intensity;
            }

            originalAmbient = RenderSettings.ambientLight;
            originalAmbientMode = RenderSettings.ambientMode;

            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }

            if (viewCamera != null)
            {
                originalClearFlags = viewCamera.clearFlags;
                originalBackground = viewCamera.backgroundColor;
            }
        }

        private void OnEnable()
        {
            if (areaController != null)
            {
                areaController.OnAreaChanged += HandleAreaChanged;
            }
        }

        private void Start()
        {
            if (areaController == null)
            {
                WarnMissingSetupOnce();
                return;
            }

            bool mining = areaController.CurrentArea == GameArea.Mining;
            blend = mining ? 1f : 0f;
            targetBlend = blend;

            if (mining)
            {
                EnterMiningLook();
            }
            else
            {
                ExitMiningLook();
            }

            ApplyBlend(blend);
        }

        private void OnDisable()
        {
            if (areaController != null)
            {
                areaController.OnAreaChanged -= HandleAreaChanged;
            }

            // 组件被停用 / 退出 Play 时，不要把场景留在矿洞的黑暗状态
            if (blend > 0.001f || ambientOverridden)
            {
                blend = 0f;
                targetBlend = 0f;
                ApplyBlend(0f);
                ExitMiningLook();
            }
        }

        private void Update()
        {
            if (Mathf.Approximately(blend, targetBlend))
            {
                return;
            }

            blend = Mathf.MoveTowards(blend, targetBlend, Time.unscaledDeltaTime / transitionDuration);
            ApplyBlend(blend);
        }

        private void HandleAreaChanged(GameArea area)
        {
            bool mining = area == GameArea.Mining;
            targetBlend = mining ? 1f : 0f;

            if (mining)
            {
                EnterMiningLook();
            }
            else
            {
                ExitMiningLook();
            }

            if (logLightingFlow)
            {
                Debug.Log($"[MiningLighting] 区域切换为 {area.ToChineseName()}，" +
                          (mining ? "开启矿洞照明。" : "恢复岛上白天光照。"));
            }
        }

        private void EnterMiningLook()
        {
            if (!ambientOverridden)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                ambientOverridden = true;
            }

            if (viewCamera != null)
            {
                viewCamera.clearFlags = CameraClearFlags.SolidColor;
                viewCamera.backgroundColor = miningBackground;
            }
        }

        private void ExitMiningLook()
        {
            if (viewCamera != null)
            {
                viewCamera.clearFlags = originalClearFlags;
                viewCamera.backgroundColor = originalBackground;
            }

            if (ambientOverridden)
            {
                RenderSettings.ambientMode = originalAmbientMode;
                RenderSettings.ambientLight = originalAmbient;
                ambientOverridden = false;
            }
        }

        private void ApplyBlend(float t)
        {
            float clamped = Mathf.Clamp01(t);

            if (sunLight != null)
            {
                sunLight.intensity = Mathf.Lerp(originalSunIntensity, miningSunIntensity, clamped);
                sunLight.color = Color.Lerp(originalSunColor, miningSunColor, clamped);
            }

            if (ambientOverridden)
            {
                RenderSettings.ambientLight = Color.Lerp(originalAmbient, miningAmbient, clamped);
            }

            for (int i = 0; i < miningLights.Length; i++)
            {
                if (miningLights[i] == null)
                {
                    continue;
                }

                float flicker = 1f + (Mathf.PerlinNoise(noiseSeed + i * 17.3f, Time.time * flickerSpeed) - 0.5f)
                    * 2f * flickerAmount;
                miningLights[i].intensity = Mathf.Lerp(0f, baseIntensities[i], clamped) * flicker;
            }

            for (int i = 0; i < miningVisuals.Length; i++)
            {
                if (miningVisuals[i] != null)
                {
                    // 墙体/灯笼在过渡后段才出现：进矿洞时相机已快到位，
                    // 出矿洞时也会先隐藏，避免从观察机位看到外面的方盒子。
                    miningVisuals[i].enabled = clamped > 0.8f;
                }
            }
        }

        private void WarnMissingSetupOnce()
        {
            if (warnedMissingSetup)
            {
                return;
            }

            warnedMissingSetup = true;
            Debug.LogWarning("[MiningLighting] GameAreaController 未配置，矿洞照明不会切换。" +
                             "请运行菜单「屿见/挖矿/配置矿洞照明（阶段 12）」。", this);
        }

        /// <summary>编辑器向导调用，一次接好区域控制器、太阳和相机。</summary>
        public void ConfigureForSetup(GameAreaController controller, Light sun, Camera camera)
        {
            areaController = controller;
            sunLight = sun;
            viewCamera = camera;
        }
    }
}