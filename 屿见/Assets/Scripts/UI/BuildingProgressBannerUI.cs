using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Yujian.UI
{
    /// <summary>
    /// 建筑进度横幅（策划案 4.1 / 4.2）。
    ///
    /// 两种形态共用一套物体：
    ///   · 特殊建筑：X/N、还差哪些特殊建筑、金色边框、完成部分呼吸 RGB、左右礼炮彩带 + 火花柱；
    ///   · 普通建筑：X/N（N = 下一等级所需建筑数）、当前等级、黑色边框、进度条缩放脉冲，
    ///     升级时左右喷火花柱（2026-11-06 美术需求：礼炮 / 火花柱 / 尘埃）。
    ///
    /// 播放队列由本类自己维护：特殊建筑先播，播完自动接普通建筑。
    /// 连续建成多座时不会叠在一起，多条普通进度只保留最新一条。
    ///
    /// 动画与填充都用 unscaledDeltaTime，暂停时间缩放也不会卡住。
    /// </summary>
    public class BuildingProgressBannerUI : MonoBehaviour
    {
        [Header("界面")]
        [Tooltip("整块横幅的 CanvasGroup。留空会在运行时自动补一个")]
        [SerializeField] private CanvasGroup canvasGroup;

        [Tooltip("横幅根节点，滑入 / 滑出改它的 anchoredPosition")]
        [SerializeField] private RectTransform bannerRect;

        [Tooltip("大字：特殊建筑 X/N / 普通建筑 X/N / 已满级")]
        [SerializeField] private Text valueText;

        [Tooltip("小字：还差哪些特殊建筑 / 当前等级——X")]
        [SerializeField] private Text subText;

        [Tooltip("最外层边框 Image，颜色随特殊 / 普通切换")]
        [SerializeField] private Image borderImage;

        [Tooltip("普通进度条整体缩放脉冲作用在这个节点上")]
        [SerializeField] private RectTransform barRoot;

        [Tooltip("进度条底色（未完成部分），保持白色")]
        [SerializeField] private Image barBackground;

        [Tooltip("进度条填充，必须是 Image.Type.Filled + Horizontal")]
        [SerializeField] private Image barFill;

        [Tooltip("粒子的父节点。留空则粒子直接挂在横幅根节点下")]
        [SerializeField] private RectTransform fxLayer;

        [Header("布局与节奏")]
        [Tooltip("显示时的 anchoredPosition")]
        [SerializeField] private Vector2 shownPosition = new Vector2(0f, -40f);

        [Tooltip("隐藏时的 anchoredPosition（屏幕上方）")]
        [SerializeField] private Vector2 hiddenPosition = new Vector2(0f, 220f);

        [Tooltip("滑入 / 滑出时长（秒）")]
        [SerializeField] private float slideDuration = 0.28f;

        [Tooltip("每条进度停留时长（秒）")]
        [SerializeField] private float holdDuration = 2.4f;

        [Tooltip("特殊与普通进度之间的间隔（秒）")]
        [SerializeField] private float gapBetween = 0.18f;

        [Tooltip("进度条从 0 填到目标值的时长（秒）")]
        [SerializeField] private float fillDuration = 0.45f;

        [Header("特殊建筑样式")]
        [SerializeField] private Color specialBorderColor = new Color(1f, 0.78f, 0.25f);
        [SerializeField] private Color specialSubtitleColor = new Color(1f, 0.93f, 0.72f);
        [SerializeField] private float specialHueSecondsPerCycle = 2.8f;
        [SerializeField] private float specialHueSaturation = 0.72f;
        [SerializeField] private float specialHueValue = 1f;
        [SerializeField] private float specialValuePulse = 0.16f;

        [Header("普通建筑样式")]
        [SerializeField] private Color normalBorderColor = new Color(0.05f, 0.05f, 0.05f);
        [SerializeField] private Color normalFillColor = new Color(0.92f, 0.72f, 0.28f);
        [SerializeField] private Color normalSubtitleColor = new Color(0.88f, 0.90f, 0.94f);
        [SerializeField] private float barPulseAmplitude = 0.035f;
        [SerializeField] private float barPulseSecondsPerCycle = 1.1f;

        [Header("特效")]
        [Tooltip("是否生成程序化礼炮 / 火花柱")]
        [SerializeField] private bool enableEffects = true;

        [Tooltip("特殊建筑每侧礼炮彩带数量")]
        [SerializeField] private int confettiParticlesPerSide = 16;

        [Tooltip("每侧火花柱的火花数量")]
        [SerializeField] private int sparkParticlesPerSide = 22;

        [Tooltip("火花柱持续喷射时长（秒）")]
        [SerializeField] private float sparkEmissionDuration = 0.45f;

        [Tooltip("粒子基础尺寸（像素）")]
        [SerializeField] private float particleSize = 10f;

        [Header("调试")]
        [Tooltip("是否在 Console 打印进度播放")]
        [SerializeField] private bool logProgress = true;

        private readonly List<BannerItem> queue = new List<BannerItem>();
        private bool isPlaying;
        private Coroutine player;

        private void Awake()
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }

            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            if (fxLayer == null)
            {
                fxLayer = bannerRect;
            }

            HideImmediate();
        }

        private void OnDisable()
        {
            if (player != null)
            {
                StopCoroutine(player);
                player = null;
            }

            isPlaying = false;
            queue.Clear();
            HideImmediate();
        }

        /// <summary>
        /// 显示特殊建筑进度。特殊建筑播完后会自动接一条普通建筑进度。
        /// </summary>
        /// <param name="current">已建成的不同特殊建筑数。</param>
        /// <param name="total">特殊建筑总数（配置数量）。</param>
        /// <param name="remainingText">「还差：八卦楼、菽庄花园」之类的文案。</param>
        public void ShowSpecial(int current, int total, string remainingText)
        {
            Enqueue(new BannerItem
            {
                IsSpecial = true,
                Current = current,
                Target = total,
                SubText = remainingText,
            });
        }

        /// <summary>
        /// 显示普通建筑进度。
        /// </summary>
        /// <param name="current">当前已建成建筑总数（X）。</param>
        /// <param name="target">下一等级所需建筑数（N）。已满级时传当前建筑数。</param>
        /// <param name="level">当前等级。</param>
        /// <param name="isMaxLevel">是否已满级。</param>
        /// <param name="leveledUp">这次建造是否让等级提升（决定是否喷火焰柱）。</param>
        public void ShowNormal(int current, int target, int level, bool isMaxLevel, bool leveledUp)
        {
            Enqueue(new BannerItem
            {
                IsSpecial = false,
                Current = current,
                Target = target,
                Level = level,
                IsMaxLevel = isMaxLevel,
                LeveledUp = leveledUp,
            });
        }

        /// <summary>立即停止播放并隐藏（例如切场景 / 读档）。</summary>
        public void HideNow()
        {
            if (player != null)
            {
                StopCoroutine(player);
                player = null;
            }

            isPlaying = false;
            queue.Clear();
            HideImmediate();
        }

        private void Enqueue(BannerItem item)
        {
            // 多条普通进度只保留最新一条，避免连续建造时排队到十几秒后
            if (!item.IsSpecial)
            {
                for (int i = queue.Count - 1; i >= 0; i--)
                {
                    if (!queue[i].IsSpecial)
                    {
                        queue[i] = item;
                        return;
                    }
                }
            }

            queue.Add(item);

            if (!isPlaying)
            {
                player = StartCoroutine(PlayQueue());
            }
        }

        private IEnumerator PlayQueue()
        {
            isPlaying = true;

            while (queue.Count > 0)
            {
                BannerItem item = queue[0];
                queue.RemoveAt(0);

                yield return PlayOne(item);

                if (queue.Count > 0 && gapBetween > 0f)
                {
                    yield return WaitUnscaled(gapBetween);
                }
            }

            isPlaying = false;
            player = null;
        }

        private IEnumerator PlayOne(BannerItem item)
        {
            ApplyStyle(item);

            canvasGroup.alpha = 0f;
            bannerRect.anchoredPosition = hiddenPosition;
            barFill.fillAmount = 0f;
            barRoot.localScale = Vector3.one;

            yield return Slide(true);
            SpawnEffects(item);

            float elapsed = 0f;

            while (elapsed < holdDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                UpdateAnimatedVisuals(item);
                yield return null;
            }

            yield return Slide(false);
            HideImmediate();

            if (logProgress)
            {
                Debug.Log(item.IsSpecial
                    ? $"[BuildingProgressBannerUI] 特殊建筑进度 {item.Current}/{item.Target} 已播放。"
                    : $"[BuildingProgressBannerUI] 建筑进度 {item.Current}/{item.Target}（等级 {item.Level}）已播放。",
                    this);
            }
        }

        private IEnumerator Slide(bool show)
        {
            Vector2 from = show ? hiddenPosition : shownPosition;
            Vector2 to = show ? shownPosition : hiddenPosition;
            float alphaFrom = show ? 0f : 1f;
            float alphaTo = show ? 1f : 0f;

            if (slideDuration <= 0f)
            {
                bannerRect.anchoredPosition = to;
                canvasGroup.alpha = alphaTo;
                yield break;
            }

            float elapsed = 0f;

            while (elapsed < slideDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / slideDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f); // ease-out cubic

                bannerRect.anchoredPosition = Vector2.LerpUnclamped(from, to, eased);
                canvasGroup.alpha = Mathf.LerpUnclamped(alphaFrom, alphaTo, eased);
                yield return null;
            }

            bannerRect.anchoredPosition = to;
            canvasGroup.alpha = alphaTo;
        }

        private void ApplyStyle(BannerItem item)
        {
            if (borderImage != null)
            {
                borderImage.color = item.IsSpecial ? specialBorderColor : normalBorderColor;
            }

            if (barBackground != null)
            {
                barBackground.color = Color.white;
            }

            if (barFill != null)
            {
                barFill.color = item.IsSpecial ? Color.white : normalFillColor;
            }

            if (subText != null)
            {
                subText.color = item.IsSpecial ? specialSubtitleColor : normalSubtitleColor;
                subText.text = item.IsSpecial ? item.SubText : $"当前等级——{item.Level}";
            }

            if (valueText != null)
            {
                if (!item.IsSpecial && item.IsMaxLevel)
                {
                    valueText.text = "已满级";
                }
                else
                {
                    valueText.text = $"{item.Current}/{item.Target}";
                }
            }
        }

        private void UpdateAnimatedVisuals(BannerItem item)
        {
            float targetFill = 1f;

            if (!item.IsSpecial && item.IsMaxLevel)
            {
                targetFill = 1f;
            }
            else if (item.Target > 0)
            {
                targetFill = Mathf.Clamp01((float)item.Current / item.Target);
            }

            if (barFill != null)
            {
                float step = fillDuration > 0f
                    ? Time.unscaledDeltaTime / fillDuration
                    : 1f;

                barFill.fillAmount = Mathf.MoveTowards(barFill.fillAmount, targetFill, step);
            }

            if (item.IsSpecial)
            {
                if (barFill != null)
                {
                    float cycle = specialHueSecondsPerCycle > 0f
                        ? Time.unscaledTime / specialHueSecondsPerCycle
                        : 0f;

                    float hue = Mathf.Repeat(cycle, 1f);
                    float value = specialHueValue * (1f - specialValuePulse * 0.5f * (1f + Mathf.Sin(Time.unscaledTime * 3f)));
                    barFill.color = Color.HSVToRGB(hue, specialHueSaturation, Mathf.Clamp01(value));
                }

                if (barRoot != null)
                {
                    barRoot.localScale = Vector3.one;
                }
            }
            else if (barRoot != null)
            {
                float cycle = barPulseSecondsPerCycle > 0f
                    ? Time.unscaledTime / barPulseSecondsPerCycle
                    : 0f;

                float pulse = 1f + barPulseAmplitude * Mathf.Sin(cycle * Mathf.PI * 2f);
                barRoot.localScale = new Vector3(pulse, pulse, 1f);
            }
        }

        private void SpawnEffects(BannerItem item)
        {
            if (!enableEffects || fxLayer == null)
            {
                return;
            }

            float halfWidth = bannerRect != null ? bannerRect.rect.width * 0.5f : 320f;

            if (item.IsSpecial)
            {
                // 策划案 4.1（2026-11-06）：左右同时生成礼炮与火花柱
                SpawnConfetti(new Vector2(-halfWidth - 12f, -6f), -1f);
                SpawnConfetti(new Vector2(halfWidth + 12f, -6f), 1f);
                SpawnSparkColumn(new Vector2(-halfWidth - 16f, -60f), -1f);
                SpawnSparkColumn(new Vector2(halfWidth + 16f, -60f), 1f);
            }
            else if (item.LeveledUp || item.IsMaxLevel)
            {
                SpawnSparkColumn(new Vector2(-halfWidth - 16f, -60f), -1f);
                SpawnSparkColumn(new Vector2(halfWidth + 16f, -60f), 1f);
            }
        }

        /// <summary>
        /// 礼炮：彩带向上喷出后受重力下坠，每条的长度、宽度、颜色、旋转速度都不同。
        /// </summary>
        private void SpawnConfetti(Vector2 origin, float outward)
        {
            int count = Mathf.Max(0, confettiParticlesPerSide);

            for (int i = 0; i < count; i++)
            {
                Color color = Color.HSVToRGB(Random.value, Random.Range(0.72f, 1f), 1f);
                Vector2 velocity = new Vector2(
                    outward * Random.Range(80f, 250f),
                    Random.Range(160f, 350f));

                float width = Mathf.Max(2f, particleSize * Random.Range(0.18f, 0.34f));
                float height = Mathf.Max(6f, particleSize * Random.Range(0.9f, 1.8f));
                float angularVelocity = Random.Range(180f, 540f) * (Random.value < 0.5f ? -1f : 1f);

                SpawnRibbon(
                    origin,
                    velocity,
                    Random.Range(0.9f, 1.6f),
                    color,
                    width,
                    height,
                    -780f,
                    angularVelocity);
            }
        }

        /// <summary>
        /// 火花柱：持续向上喷射亮色小火花，带明暗闪烁和轻微下坠。
        /// </summary>
        private void SpawnSparkColumn(Vector2 origin, float outward)
        {
            if (sparkParticlesPerSide <= 0 || !isActiveAndEnabled)
            {
                return;
            }

            StartCoroutine(EmitSparkColumn(origin, outward));
        }

        private IEnumerator EmitSparkColumn(Vector2 origin, float outward)
        {
            int count = Mathf.Max(1, sparkParticlesPerSide);
            float duration = Mathf.Max(0.05f, sparkEmissionDuration);
            float interval = duration / count;
            float elapsed = 0f;
            int emitted = 0;

            while (emitted < count)
            {
                elapsed += Time.unscaledDeltaTime;

                while (emitted < count && elapsed >= emitted * interval)
                {
                    SpawnSpark(origin, outward);
                    emitted++;
                }

                yield return null;
            }
        }

        private void SpawnSpark(Vector2 origin, float outward)
        {
            Color color = Color.Lerp(
                new Color(1f, 0.86f, 0.45f),
                Color.white,
                Random.value);

            Vector2 velocity = new Vector2(
                outward * Random.Range(0f, 45f) + Random.Range(-28f, 28f),
                Random.Range(180f, 430f));

            float size = particleSize * Random.Range(0.16f, 0.32f);
            SpawnSparkParticle(origin, velocity, Random.Range(0.45f, 0.95f), color, size);
        }

        private void SpawnRibbon(
            Vector2 origin, Vector2 velocity, float lifetime, Color color,
            float width, float height, float accelerationY, float angularVelocity)
        {
            GameObject particle = new GameObject("ProgressRibbon", typeof(RectTransform), typeof(Image));
            particle.transform.SetParent(fxLayer, false);

            RectTransform rect = (RectTransform)particle.transform;
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = origin;
            rect.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

            Image image = particle.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            StartCoroutine(AnimateRibbon(rect, image, velocity, lifetime, accelerationY, angularVelocity));
        }

        private IEnumerator AnimateRibbon(
            RectTransform rect, Image image, Vector2 velocity, float lifetime,
            float accelerationY, float angularVelocity)
        {
            float elapsed = 0f;
            Vector2 position = rect.anchoredPosition;
            float angle = rect.localEulerAngles.z;
            Color startColor = image.color;

            while (elapsed < lifetime && rect != null && image != null)
            {
                float deltaTime = Time.unscaledDeltaTime;
                elapsed += deltaTime;

                velocity.y += accelerationY * deltaTime;
                position += velocity * deltaTime;
                rect.anchoredPosition = position;

                angle += angularVelocity * deltaTime;
                rect.localRotation = Quaternion.Euler(0f, 0f, angle);

                float t = Mathf.Clamp01(elapsed / lifetime);
                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, t));
                Color color = startColor;
                color.a = startColor.a * fade;
                image.color = color;

                yield return null;
            }

            if (rect != null)
            {
                Destroy(rect.gameObject);
            }
        }

        private void SpawnSparkParticle(Vector2 origin, Vector2 velocity, float lifetime, Color color, float size)
        {
            GameObject particle = new GameObject("ProgressSpark", typeof(RectTransform), typeof(Image));
            particle.transform.SetParent(fxLayer, false);

            RectTransform rect = (RectTransform)particle.transform;
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = origin;
            rect.localRotation = Quaternion.Euler(0f, 0f, 45f);

            Image image = particle.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            StartCoroutine(AnimateSpark(rect, image, velocity, lifetime, color));
        }

        private IEnumerator AnimateSpark(
            RectTransform rect, Image image, Vector2 velocity, float lifetime, Color startColor)
        {
            float elapsed = 0f;
            float flickerPhase = Random.Range(0f, Mathf.PI * 2f);
            Vector2 position = rect.anchoredPosition;

            while (elapsed < lifetime && rect != null && image != null)
            {
                float deltaTime = Time.unscaledDeltaTime;
                elapsed += deltaTime;

                velocity.y -= 140f * deltaTime;
                velocity.x *= 1f - 1.2f * deltaTime;
                position += velocity * deltaTime;
                rect.anchoredPosition = position;

                float t = Mathf.Clamp01(elapsed / lifetime);
                float fade = 1f - Mathf.SmoothStep(0f, 1f, t);
                float flicker = 0.72f + 0.28f * Mathf.Sin(elapsed * 38f + flickerPhase);

                Color color = startColor;
                color.a = fade * flicker;
                image.color = color;

                float scale = Mathf.Lerp(1f, 0.25f, t) * (0.85f + 0.3f * flicker);
                rect.localScale = new Vector3(scale, scale, 1f);

                yield return null;
            }

            if (rect != null)
            {
                Destroy(rect.gameObject);
            }
        }

        private IEnumerator WaitUnscaled(float seconds)
        {
            float elapsed = 0f;

            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private void HideImmediate()
        {
            if (bannerRect != null)
            {
                bannerRect.anchoredPosition = hiddenPosition;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
        }

        private struct BannerItem
        {
            public bool IsSpecial;
            public int Current;
            public int Target;
            public int Level;
            public bool IsMaxLevel;
            public bool LeveledUp;
            public string SubText;
        }
    }
}
