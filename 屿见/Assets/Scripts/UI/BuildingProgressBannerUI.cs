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
    ///   · 特殊建筑：X/N、还差哪些特殊建筑、金色边框、完成部分呼吸 RGB、左右礼炮；
    ///   · 普通建筑：X/N（N = 下一等级所需建筑数）、当前等级、黑色边框、进度条缩放脉冲，
    ///     升级时左右喷火焰柱。
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
        [Tooltip("是否生成程序化礼炮 / 火焰柱")]
        [SerializeField] private bool enableEffects = true;

        [Tooltip("特殊建筑每侧礼炮粒子数")]
        [SerializeField] private int fireworkParticlesPerSide = 18;

        [Tooltip("升级时每侧火焰柱粒子数")]
        [SerializeField] private int flameParticlesPerSide = 14;

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
                SpawnFirework(new Vector2(-halfWidth - 12f, -6f), -1f);
                SpawnFirework(new Vector2(halfWidth + 12f, -6f), 1f);
            }
            else if (item.LeveledUp || item.IsMaxLevel)
            {
                SpawnFlameColumn(new Vector2(-halfWidth - 16f, -60f));
                SpawnFlameColumn(new Vector2(halfWidth + 16f, -60f));
            }
        }

        private void SpawnFirework(Vector2 origin, float outward)
        {
            int count = Mathf.Max(0, fireworkParticlesPerSide);

            for (int i = 0; i < count; i++)
            {
                Color color = Color.HSVToRGB(Random.value, 0.85f, 1f);
                Vector2 velocity = new Vector2(
                    outward * Random.Range(90f, 260f),
                    Random.Range(120f, 320f));
                float size = particleSize * Random.Range(0.6f, 1.4f);
                SpawnParticle(origin, velocity, Random.Range(0.65f, 1.05f), color, size, -760f);
            }
        }

        private void SpawnFlameColumn(Vector2 origin)
        {
            int count = Mathf.Max(0, flameParticlesPerSide);

            for (int i = 0; i < count; i++)
            {
                Color color = Color.Lerp(
                    new Color(1f, 0.35f, 0.05f),
                    new Color(1f, 0.92f, 0.35f),
                    Random.value);
                Vector2 velocity = new Vector2(
                    Random.Range(-35f, 35f),
                    Random.Range(120f, 260f));
                float size = particleSize * Random.Range(0.7f, 1.5f);
                SpawnParticle(origin, velocity, Random.Range(0.35f, 0.7f), color, size, 420f);
            }
        }

        private void SpawnParticle(
            Vector2 origin, Vector2 velocity, float lifetime, Color color, float size, float accelerationY)
        {
            GameObject particle = new GameObject("ProgressParticle", typeof(RectTransform), typeof(Image));
            particle.transform.SetParent(fxLayer, false);

            RectTransform rect = (RectTransform)particle.transform;
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = origin;

            Image image = particle.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            StartCoroutine(AnimateParticle(rect, image, velocity, lifetime, accelerationY));
        }

        private IEnumerator AnimateParticle(
            RectTransform rect, Image image, Vector2 velocity, float lifetime, float accelerationY)
        {
            float elapsed = 0f;
            Vector2 position = rect.anchoredPosition;
            Vector3 baseScale = rect.localScale;

            while (elapsed < lifetime && rect != null && image != null)
            {
                float deltaTime = Time.unscaledDeltaTime;
                elapsed += deltaTime;

                velocity.y += accelerationY * deltaTime;
                position += velocity * deltaTime;
                rect.anchoredPosition = position;

                float fade = 1f - Mathf.Clamp01(elapsed / lifetime);
                Color color = image.color;
                color.a = fade;
                image.color = color;
                rect.localScale = baseScale * Mathf.Lerp(0.5f, 1f, fade);

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
