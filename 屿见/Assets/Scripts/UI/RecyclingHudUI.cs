using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Yujian.Building.Effects;
using Yujian.Building.Recycling;
using Yujian.Core;
using Yujian.Player;

namespace Yujian.UI
{
    /// <summary>
    /// 全局唯一的「垃圾 · 金币」资源栏 + 回收站投料入口。
    ///
    /// **只有这一份资源 UI**：清洁关卡内和岛上 / 建筑系统都显示同一个物件、同一套样式，
    /// 区域切换时不再由 MiningHudUI 另画一份，避免两套 UI 各长各的。
    ///
    /// 关卡外（岛 / 建筑系统）：
    ///   · 右上角常驻资源栏（垃圾桶图标 +「垃圾 N · 金币 M」）；
    ///   · 资源栏下方是「队列 Q/C」与「投入」按钮，点击或按空格把全部垃圾投入共享队列；
    ///   · 只有场上至少 1 座回收站时才显示队列行与按钮；
    ///   · 队列满 / 没有垃圾时按钮置灰并写明原因。
    /// 关卡内：
    ///   · 资源栏原样保留，只显示「垃圾 N · 金币 M」；
    ///   · 队列 / 投料入口隐藏（关卡内不投料）。
    /// 世界表现：
    ///   · 每座回收站上方的进度条（共享队列当前批次的剩余进度）；
    ///   · 每完成一批，站点上方冒出「+N」金币提示；
    ///   · 投料时几枚图标从按钮飞向最近的回收站。
    ///
    /// UI 在运行时生成，不往场景里存控件；
    /// 位置 / 颜色 / 尺寸都是 Inspector 字段，后续可换成预制体。
    /// </summary>
    [DisallowMultipleComponent]
    public class RecyclingHudUI : MonoBehaviour
    {
        [Header("依赖（留空会自动查找）")]
        [SerializeField] private GameAreaController areas;
        [SerializeField] private PlayerGarbage garbage;
        [SerializeField] private PlayerCurrency currency;
        [SerializeField] private RecyclingSystem recycling;

        [Header("布局")]
        [Tooltip("右上角资源栏的锚点偏移。与清洁界面 CoinBar 相同：(-32,-26)")]
        [SerializeField] private Vector2 anchoredPosition = new Vector2(-32f, -26f);

        [Tooltip("资源栏尺寸。与清洁界面 CoinBar 相同：240×46")]
        [SerializeField] private Vector2 panelSize = new Vector2(240f, 46f);

        [Tooltip("资源栏与下一行（队列 / 状态栏）之间的间距")]
        [SerializeField] private float barGap = 8f;

        [Header("资源栏图标")]
        [Tooltip("资源栏图标。留空时在运行时绘制一个垃圾桶图标（与清洁界面同一套画法）")]
        [SerializeField] private Sprite resourceIconSprite;

        [Tooltip("图标底框颜色。清洁界面用 14% 白")]
        [SerializeField] private Color iconBoxColor = new Color(1f, 1f, 1f, 0.14f);

        [Header("配色")]
        [Tooltip("面板底色。与清洁界面面板一致：86% 不透明的深蓝黑")]
        [SerializeField] private Color panelColor = new Color(0.07f, 0.09f, 0.12f, 0.86f);
        [SerializeField] private Color textColor = new Color(0.95f, 0.95f, 0.95f, 1f);
        [SerializeField] private Color accentColor = new Color(1f, 0.84f, 0.42f, 1f);
        [SerializeField] private Color disabledColor = new Color(0.65f, 0.65f, 0.65f, 1f);
        [SerializeField] private Color progressColor = new Color(0.36f, 0.82f, 0.42f, 1f);
        [SerializeField] private Color progressBackColor = new Color(0f, 0f, 0f, 0.55f);

        [Header("反馈")]
        [Tooltip("投料提示停留时长（秒）")]
        [SerializeField, Min(0.1f)] private float feedbackSeconds = 1.4f;

        [Tooltip("金币到账时资源栏放大的倍数")]
        [SerializeField, Min(1f)] private float coinPunchScale = 1.12f;

        [Tooltip("金币到账时资源栏旋转的角度（度）")]
        [SerializeField] private float coinPunchAngle = 4f;

        [Tooltip("金币到账弹跳时长（秒）")]
        [SerializeField, Min(0.05f)] private float coinPunchDuration = 0.32f;

        [Tooltip("站点进度条的像素尺寸")]
        [SerializeField] private Vector2 stationBarSize = new Vector2(120f, 12f);

        [Tooltip("站点进度条相对锚点的屏幕像素偏移（往上抬，避开建筑本体）")]
        [SerializeField] private Vector2 stationBarScreenOffset = new Vector2(0f, 26f);

        [Tooltip("投料时最多生成几枚飞向站点的图标")]
        [SerializeField, Min(1)] private int maxFlightIcons = 6;

        [Tooltip("投料图标飞行时长（秒）")]
        [SerializeField, Min(0.05f)] private float flightDuration = 0.45f;

        [Tooltip("金币到账提示上浮时长（秒）")]
        [SerializeField, Min(0.1f)] private float coinPopDuration = 0.9f;

        private class StationBar
        {
            public RectTransform Root;
            public Image Fill;
            public RecyclingStationEffect Station;
        }

        private class CoinPop
        {
            public RectTransform Root;
            public Text Text;
            public Vector2 Start;
            public float Elapsed;
        }

        private class FlightIcon
        {
            public RectTransform Root;
            public Image Image;
            public Vector2 Start;
            public Vector2 Target;
            public float Elapsed;
            public float Delay;
        }

        private Canvas canvas;
        private RectTransform root;
        private RectTransform resourceBar;
        private Image resourceIconImage;
        private Text resourceText;
        private RectTransform queueBar;
        private Text queueText;
        private Button depositButton;
        private Text depositButtonText;
        private Text feedbackText;
        private RectTransform overlay;

        private Sprite generatedIconSprite;

        private readonly List<StationBar> bars = new List<StationBar>();
        private readonly List<CoinPop> coinPops = new List<CoinPop>();
        private readonly List<FlightIcon> flights = new List<FlightIcon>();

        private float feedbackUntil;
        private bool built;
        private bool subscribed;
        private Coroutine punchRoutine;
        private Vector3 textBaseScale = Vector3.one;
        private Quaternion textBaseRotation = Quaternion.identity;
        private Vector3 iconBaseScale = Vector3.one;
        private Quaternion iconBaseRotation = Quaternion.identity;

        private void Awake()
        {
            NormalizeLayout();
            ResolveDependencies();
            Build();
        }

        /// <summary>
        /// 旧的回收站 HUD 在场景里序列化过 400×110 的大面板 / (-30,-100) 偏移，
        /// 和清洁界面同款的 240×46 / (-32,-26) 不一致。
        /// 场景还没重存时，这里自动升级到统一布局，保证关卡内外长得一样。
        /// </summary>
        private void NormalizeLayout()
        {
            bool legacyPanel = Mathf.Approximately(panelSize.x, 400f) &&
                               Mathf.Approximately(panelSize.y, 110f);
            bool legacyOffset = Mathf.Approximately(anchoredPosition.y, -100f);

            if (legacyPanel)
            {
                panelSize = new Vector2(240f, 46f);
            }

            if (legacyPanel || legacyOffset)
            {
                anchoredPosition = new Vector2(-32f, -26f);
            }
        }

        private void OnEnable()
        {
            ResolveDependencies();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            StopPunch();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            StopPunch();

            if (generatedIconSprite != null)
            {
                Texture2D texture = generatedIconSprite.texture;
                Destroy(generatedIconSprite);

                if (texture != null)
                {
                    Destroy(texture);
                }
            }
        }

        private void StopPunch()
        {
            if (punchRoutine != null)
            {
                StopCoroutine(punchRoutine);
                punchRoutine = null;
            }

            ResetResourceTransform();
        }

        private void Update()
        {
            if (!built)
            {
                return;
            }

            ResolveDependencies();
            Subscribe();

            bool hasStations = recycling != null && recycling.StationCount > 0;
            bool inLevel = areas != null && areas.CurrentArea == GameArea.Mining;

            // 关卡内外只有这一块资源栏：资源行始终显示，队列 / 投料入口只在关卡外出现。
            bool showEntry = !inLevel && hasStations;

            if (root != null && !root.gameObject.activeSelf)
            {
                root.gameObject.SetActive(true);
            }

            if (resourceBar != null && !resourceBar.gameObject.activeSelf)
            {
                resourceBar.gameObject.SetActive(true);
            }

            if (queueBar != null && queueBar.gameObject.activeSelf != showEntry)
            {
                queueBar.gameObject.SetActive(showEntry);
            }

            RefreshEntry(showEntry);

            if (showEntry)
            {
                HandleSpaceInput();
            }

            UpdateFeedback();

            // 站点在岛上：关卡内不画它们的进度条，避免屏幕边缘冒出无意义的条。
            if (hasStations && !inLevel)
            {
                SyncStationBars();
            }
            else
            {
                HideStationBars();
            }

            UpdateCoinPops();
            UpdateFlights();
        }

        // ---------------- 构建 ----------------

        private void Build()
        {
            if (built)
            {
                return;
            }

            canvas = FindHudCanvas();

            if (canvas == null)
            {
                Debug.LogError("[RecyclingHudUI] 场景里找不到 Canvas，回收站 HUD 无法生成。", this);
                return;
            }

            RectTransform canvasRect = canvas.transform as RectTransform;

            // 根节点只负责整体显隐，不再画底板；底板交给下面几块与清洁界面同款的面板。
            root = CreateRect("RecyclingHud", canvasRect);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            Vector2 nextRow = anchoredPosition + new Vector2(0f, -(panelSize.y + Mathf.Max(0f, barGap)));

            // 第一行：全局唯一的资源栏 —— 垃圾桶图标框 +「垃圾 N · 金币 M」
            resourceBar = CreatePanel(root, "ResourceBar", anchoredPosition, panelSize, panelColor);

            RectTransform iconBox = CreatePanel(resourceBar, "Icon",
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f),
                new Vector2(34f, 34f), iconBoxColor);

            RectTransform iconRect = CreateRect("GarbageIcon", iconBox);
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.offsetMin = new Vector2(2f, 2f);
            iconRect.offsetMax = new Vector2(-2f, -2f);

            resourceIconImage = iconRect.gameObject.AddComponent<Image>();
            resourceIconImage.sprite = ResolveResourceIconSprite();
            resourceIconImage.preserveAspect = true;
            resourceIconImage.raycastTarget = false;
            iconBaseScale = iconRect.localScale;
            iconBaseRotation = iconRect.localRotation;

            resourceText = CreateText(resourceBar, "ResourceText", 18,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(4f, 0f),
                new Vector2(190f, 40f), TextAnchor.MiddleRight, accentColor, true);
            resourceText.text = "垃圾 0 · 金币 0";
            textBaseScale = resourceText.rectTransform.localScale;
            textBaseRotation = resourceText.rectTransform.localRotation;

            // 第二行：队列 + 投入按钮，沿用同一套底板、字号与金色按钮
            queueBar = CreatePanel(root, "QueueBar", nextRow, panelSize, panelColor);

            queueText = CreateText(queueBar, "QueueText", 16,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f),
                new Vector2(118f, 40f), TextAnchor.MiddleLeft, textColor, false);
            queueText.rectTransform.pivot = new Vector2(0f, 0.5f);
            queueText.text = "队列 0/0";

            depositButton = CreateButton(queueBar, "DepositButton", "投入", 16,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f, 0f),
                new Vector2(96f, 32f));
            depositButtonText = depositButton.GetComponentInChildren<Text>(true);
            depositButton.onClick.AddListener(HandleDepositClicked);

            feedbackText = CreateText(root, "FeedbackText", 15,
                new Vector2(1f, 1f), new Vector2(1f, 1f),
                nextRow + new Vector2(-panelSize.x * 0.5f, -(panelSize.y + 4f)),
                new Vector2(panelSize.x, 24f), TextAnchor.MiddleRight, textColor, false);
            feedbackText.text = string.Empty;

            overlay = CreateRect("RecyclingOverlay", canvasRect);
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = Vector2.zero;
            overlay.offsetMax = Vector2.zero;
            overlay.SetAsLastSibling();

            root.SetAsLastSibling();
            built = true;
        }

        private Canvas FindHudCanvas()
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            Canvas fallback = null;

            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas candidate = canvases[i];

                if (candidate == null)
                {
                    continue;
                }

                if (fallback == null)
                {
                    fallback = candidate;
                }

                if (candidate.transform.Find("BuildingPanel") != null ||
                    candidate.transform.Find("CoinHud") != null)
                {
                    return candidate;
                }
            }

            return fallback;
        }

        // ---------------- 投料 ----------------

        private void HandleDepositClicked()
        {
            TryDeposit();
        }

        private void HandleSpaceInput()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null || !keyboard.spaceKey.wasPressedThisFrame)
            {
                return;
            }

            TryDeposit();
        }

        private void TryDeposit()
        {
            if (recycling == null)
            {
                return;
            }

            if (recycling.FreeCapacity <= 0)
            {
                ShowFeedback("队列已满", false);
                return;
            }

            if (garbage == null || garbage.CurrentGarbage <= 0)
            {
                ShowFeedback("没有垃圾可投入", false);
                return;
            }

            int accepted = recycling.DepositFromPlayer();

            if (accepted <= 0)
            {
                ShowFeedback("投料失败", false);
                return;
            }

            ShowFeedback($"已投入 {accepted} 垃圾", true);
            SpawnFlightIcons(accepted);
        }

        private void RefreshEntry(bool showEntry)
        {
            if (resourceText == null)
            {
                return;
            }

            int garbageCount = garbage != null ? garbage.CurrentGarbage : 0;
            int coinCount = currency != null ? currency.CurrentMoney : 0;
            int queued = recycling != null ? recycling.QueuedGarbage : 0;
            int capacity = recycling != null ? recycling.Capacity : 0;
            int free = recycling != null ? recycling.FreeCapacity : 0;

            // 资源行在关卡内外都只有这一份：图标 +「垃圾 N · 金币 M」
            resourceText.text = $"垃圾 {FormatNumber(garbageCount)} · 金币 {FormatNumber(coinCount)}";

            // 队列行只在关卡外、且有回收站时出现
            if (queueText != null)
            {
                queueText.text = showEntry
                    ? $"队列 {FormatNumber(queued)}/{FormatNumber(capacity)}"
                    : string.Empty;
            }

            if (depositButton == null)
            {
                return;
            }

            depositButton.gameObject.SetActive(showEntry);

            if (!showEntry)
            {
                return;
            }

            bool hasGarbage = garbageCount > 0;
            bool hasSpace = free > 0;
            depositButton.interactable = hasGarbage && hasSpace;

            if (depositButtonText != null)
            {
                if (!hasSpace)
                {
                    depositButtonText.text = "队列已满";
                    depositButtonText.color = disabledColor;
                }
                else if (!hasGarbage)
                {
                    depositButtonText.text = "没有垃圾";
                    depositButtonText.color = disabledColor;
                }
                else
                {
                    depositButtonText.text = $"投入 {FormatNumber(Mathf.Min(garbageCount, free))}";
                    depositButtonText.color = Color.black;
                }
            }
        }

        private void ShowFeedback(string message, bool success)
        {
            if (feedbackText == null)
            {
                return;
            }

            feedbackText.text = message;
            feedbackText.color = success ? progressColor : disabledColor;
            feedbackUntil = Time.unscaledTime + Mathf.Max(0.1f, feedbackSeconds);
        }

        private void UpdateFeedback()
        {
            if (feedbackText == null)
            {
                return;
            }

            if (feedbackUntil > 0f && Time.unscaledTime >= feedbackUntil)
            {
                feedbackUntil = 0f;
                feedbackText.text = string.Empty;
            }
        }

        // ---------------- 站点进度条 ----------------

        private void SyncStationBars()
        {
            IReadOnlyList<RecyclingStationEffect> stations = recycling.Stations;
            Camera camera = Camera.main;

            // 队列空时产能闲置：不显示进度条，避免满条一直挂着
            if (recycling.QueuedGarbage <= 0)
            {
                HideStationBars();
                return;
            }

            EnsureBarCount(stations.Count);

            float remaining = 1f - Mathf.Clamp01((float)recycling.CoinProgress);

            for (int i = 0; i < bars.Count; i++)
            {
                StationBar bar = bars[i];

                if (i >= stations.Count || stations[i] == null || camera == null)
                {
                    bar.Root.gameObject.SetActive(false);
                    continue;
                }

                RecyclingStationEffect station = stations[i];
                bar.Station = station;

                if (!WorldToCanvas.TryProject(
                        camera, canvas, station.ProgressAnchor, stationBarScreenOffset,
                        out Vector2 local))
                {
                    bar.Root.gameObject.SetActive(false);
                    continue;
                }

                bar.Root.gameObject.SetActive(true);
                bar.Root.anchoredPosition = local;
                bar.Root.sizeDelta = stationBarSize;
                bar.Fill.rectTransform.localScale = new Vector3(remaining, 1f, 1f);
            }
        }

        private void EnsureBarCount(int count)
        {
            while (bars.Count < count)
            {
                bars.Add(CreateStationBar());
            }
        }

        private StationBar CreateStationBar()
        {
            RectTransform barRoot = CreateRect("StationProgress", overlay);
            barRoot.anchorMin = new Vector2(0.5f, 0.5f);
            barRoot.anchorMax = new Vector2(0.5f, 0.5f);
            barRoot.pivot = new Vector2(0.5f, 0.5f);
            barRoot.sizeDelta = stationBarSize;

            Image background = barRoot.gameObject.AddComponent<Image>();
            background.color = progressBackColor;
            background.raycastTarget = false;

            RectTransform fillRect = CreateRect("Fill", barRoot);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(1f, 1f);
            fillRect.offsetMax = new Vector2(-1f, -1f);
            fillRect.pivot = new Vector2(0f, 0.5f);

            Image fill = fillRect.gameObject.AddComponent<Image>();
            fill.color = progressColor;
            fill.raycastTarget = false;

            return new StationBar
            {
                Root = barRoot,
                Fill = fill,
                Station = null,
            };
        }

        private void HideStationBars()
        {
            for (int i = 0; i < bars.Count; i++)
            {
                if (bars[i].Root != null)
                {
                    bars[i].Root.gameObject.SetActive(false);
                }
            }
        }

        // ---------------- 金币到账提示 ----------------

        private void HandleCoinsProduced(int coins)
        {
            if (coins <= 0 || recycling == null || overlay == null)
            {
                return;
            }

            PlayCoinPunch();

            // 关卡内只保留右上角资源栏的弹跳，不在世界里的站点上方冒「+N」。
            if (areas != null && areas.CurrentArea == GameArea.Mining)
            {
                return;
            }

            Camera camera = Camera.main;

            if (camera == null)
            {
                return;
            }

            for (int i = 0; i < recycling.Stations.Count; i++)
            {
                RecyclingStationEffect station = recycling.Stations[i];

                if (station == null)
                {
                    continue;
                }

                if (!WorldToCanvas.TryProject(
                        camera, canvas, station.ProgressAnchor, stationBarScreenOffset,
                        out Vector2 local))
                {
                    continue;
                }

                Text text = CreateText(overlay, "CoinPop", 24,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), local,
                    new Vector2(120f, 34f), TextAnchor.MiddleCenter, accentColor, true);
                text.text = $"+{coins} 金币";

                coinPops.Add(new CoinPop
                {
                    Root = text.rectTransform,
                    Text = text,
                    Start = local,
                    Elapsed = 0f,
                });
            }
        }

        private void PlayCoinPunch()
        {
            if (resourceText == null)
            {
                return;
            }

            if (punchRoutine != null)
            {
                StopCoroutine(punchRoutine);
            }

            punchRoutine = StartCoroutine(CoinPunchRoutine());
        }

        private IEnumerator CoinPunchRoutine()
        {
            float duration = Mathf.Max(0.05f, coinPunchDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float phase = t < 0.4f ? t / 0.4f : 1f - (t - 0.4f) / 0.6f;
                float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phase));

                float scale = Mathf.Lerp(1f, Mathf.Max(1f, coinPunchScale), eased);

                if (resourceText != null)
                {
                    RectTransform rect = resourceText.rectTransform;
                    rect.localScale = textBaseScale * scale;
                    rect.localRotation = textBaseRotation * Quaternion.Euler(0f, 0f, coinPunchAngle * eased);
                }

                // 图标跟着放大但不旋转，和清洁界面资源栏一致
                if (resourceIconImage != null)
                {
                    RectTransform rect = resourceIconImage.rectTransform;
                    rect.localScale = iconBaseScale * scale;
                    rect.localRotation = iconBaseRotation;
                }

                yield return null;
            }

            ResetResourceTransform();
            punchRoutine = null;
        }

        private void ResetResourceTransform()
        {
            if (resourceText != null)
            {
                resourceText.rectTransform.localScale = textBaseScale;
                resourceText.rectTransform.localRotation = textBaseRotation;
            }

            if (resourceIconImage != null)
            {
                resourceIconImage.rectTransform.localScale = iconBaseScale;
                resourceIconImage.rectTransform.localRotation = iconBaseRotation;
            }
        }

        private void UpdateCoinPops()
        {
            for (int i = coinPops.Count - 1; i >= 0; i--)
            {
                CoinPop pop = coinPops[i];
                pop.Elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(pop.Elapsed / Mathf.Max(0.1f, coinPopDuration));

                if (pop.Root != null)
                {
                    pop.Root.anchoredPosition = pop.Start + new Vector2(0f, 46f * t);
                    pop.Root.localScale = Vector3.one * Mathf.Lerp(1f, 0.8f, t);
                }

                if (pop.Text != null)
                {
                    Color color = pop.Text.color;
                    color.a = 1f - t;
                    pop.Text.color = color;
                }

                if (t >= 1f)
                {
                    if (pop.Root != null)
                    {
                        Destroy(pop.Root.gameObject);
                    }

                    coinPops.RemoveAt(i);
                }
            }
        }

        // ---------------- 投料飞行图标 ----------------

        private void SpawnFlightIcons(int accepted)
        {
            if (overlay == null || recycling == null || recycling.Stations.Count == 0)
            {
                return;
            }

            Camera camera = Camera.main;
            RectTransform canvasRect = canvas.transform as RectTransform;

            if (camera == null || canvasRect == null || depositButton == null)
            {
                return;
            }

            if (!TryGetCanvasPoint(depositButton.transform.position, canvasRect, out Vector2 start))
            {
                return;
            }

            RecyclingStationEffect nearest = FindNearestStation(camera);

            if (nearest == null)
            {
                return;
            }

            if (!WorldToCanvas.TryProject(
                    camera, canvas, nearest.ProgressAnchor, stationBarScreenOffset,
                    out Vector2 target))
            {
                return;
            }

            int count = Mathf.Clamp(accepted, 1, Mathf.Max(1, maxFlightIcons));

            for (int i = 0; i < count; i++)
            {
                RectTransform icon = CreateRect("DepositFlight", overlay);
                icon.anchorMin = new Vector2(0.5f, 0.5f);
                icon.anchorMax = new Vector2(0.5f, 0.5f);
                icon.pivot = new Vector2(0.5f, 0.5f);
                icon.sizeDelta = new Vector2(14f, 14f);
                icon.anchoredPosition = start;

                Image image = icon.gameObject.AddComponent<Image>();
                image.color = accentColor;
                image.raycastTarget = false;

                flights.Add(new FlightIcon
                {
                    Root = icon,
                    Image = image,
                    Start = start + new Vector2(Random.Range(-18f, 18f), Random.Range(-10f, 10f)),
                    Target = target + new Vector2(Random.Range(-10f, 10f), Random.Range(-6f, 6f)),
                    Elapsed = 0f,
                    Delay = i * 0.04f,
                });
            }
        }

        private RecyclingStationEffect FindNearestStation(Camera camera)
        {
            RecyclingStationEffect nearest = null;
            Vector3 cameraPosition = camera.transform.position;
            float best = float.MaxValue;

            for (int i = 0; i < recycling.Stations.Count; i++)
            {
                RecyclingStationEffect station = recycling.Stations[i];

                if (station == null)
                {
                    continue;
                }

                float distance = (station.ProgressAnchor - cameraPosition).sqrMagnitude;

                if (distance < best)
                {
                    best = distance;
                    nearest = station;
                }
            }

            return nearest;
        }

        private static bool TryGetCanvasPoint(
            Vector3 worldPosition, RectTransform canvasRect, out Vector2 local)
        {
            local = default;

            Camera uiCamera = CanvasUtility.GetCanvasCamera(canvasRect);

            Vector2 screen = RectTransformUtility.WorldToScreenPoint(uiCamera, worldPosition);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screen, uiCamera, out local);
        }

        private void UpdateFlights()
        {
            for (int i = flights.Count - 1; i >= 0; i--)
            {
                FlightIcon flight = flights[i];
                flight.Elapsed += Time.unscaledDeltaTime;

                float progress = Mathf.Clamp01((flight.Elapsed - flight.Delay) / Mathf.Max(0.05f, flightDuration));

                if (progress <= 0f)
                {
                    continue;
                }

                if (flight.Root != null)
                {
                    Vector2 position = Vector2.Lerp(flight.Start, flight.Target, Mathf.SmoothStep(0f, 1f, progress));
                    position.y += Mathf.Sin(progress * Mathf.PI) * 36f;
                    flight.Root.anchoredPosition = position;
                }

                if (flight.Image != null)
                {
                    Color color = flight.Image.color;
                    color.a = 1f - progress;
                    flight.Image.color = color;
                }

                if (progress >= 1f)
                {
                    if (flight.Root != null)
                    {
                        Destroy(flight.Root.gameObject);
                    }

                    flights.RemoveAt(i);
                }
            }
        }

        // ---------------- 依赖与工具 ----------------

        private void ResolveDependencies()
        {
            if (areas == null)
            {
                areas = FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);
            }

            if (garbage == null)
            {
                garbage = FindFirstObjectByType<PlayerGarbage>(FindObjectsInactive.Include);
            }

            if (currency == null)
            {
                currency = FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            }

            if (recycling == null)
            {
                recycling = FindFirstObjectByType<RecyclingSystem>(FindObjectsInactive.Include);
            }
        }

        private void Subscribe()
        {
            if (subscribed || recycling == null)
            {
                return;
            }

            recycling.CoinsProduced += HandleCoinsProduced;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || recycling == null)
            {
                return;
            }

            recycling.CoinsProduced -= HandleCoinsProduced;
            subscribed = false;
        }

        /// <summary>编辑器向导调用。</summary>
        public void ConfigureForSetup(
            GameAreaController newAreas, PlayerGarbage newGarbage,
            PlayerCurrency newCurrency, RecyclingSystem newRecycling)
        {
            areas = newAreas;
            garbage = newGarbage;
            currency = newCurrency;
            recycling = newRecycling;
        }

        /// <summary>资源栏此刻是否真正显示在屏幕上（垃圾收集器据此决定飞过去还是直接结算）。</summary>
        public bool IsResourceVisible =>
            built && root != null && root.gameObject.activeInHierarchy &&
            resourceBar != null && resourceBar.gameObject.activeInHierarchy;

        /// <summary>
        /// 资源栏在屏幕上的位置。垃圾收集器用它反算世界坐标里的飞行目标。
        /// 界面还没构建时返回 false。
        /// </summary>
        public bool TryGetResourceScreenPoint(out Vector2 screenPoint)
        {
            RectTransform target = resourceBar != null ? resourceBar : root;

            if (target == null)
            {
                screenPoint = default;
                return false;
            }

            screenPoint = RectTransformUtility.WorldToScreenPoint(
                CanvasUtility.GetCanvasCamera(target), target.position);
            return true;
        }

        /// <summary>一块垃圾入账时调用：资源栏数字放大后缩回。</summary>
        public void PlayResourcePunch()
        {
            PlayCoinPunch();
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static RectTransform CreatePanel(Transform parent, string name,
            Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        /// <summary>默认锚在右上角的面板，和清洁界面 CoinBar 的定位方式一致。</summary>
        private static RectTransform CreatePanel(Transform parent, string name,
            Vector2 anchoredPosition, Vector2 size, Color color)
        {
            return CreatePanel(parent, name, new Vector2(1f, 1f), new Vector2(1f, 1f),
                anchoredPosition, size, color);
        }

        /// <summary>资源栏图标：优先用美术拖进来的 Sprite，没有就运行时画一个垃圾桶。</summary>
        private Sprite ResolveResourceIconSprite()
        {
            if (resourceIconSprite != null)
            {
                return resourceIconSprite;
            }

            if (generatedIconSprite == null)
            {
                generatedIconSprite = GarbageIconFactory.Create();
            }

            return generatedIconSprite;
        }

        private static Text CreateText(
            Transform parent, string name, int fontSize, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 position, Vector2 size, TextAnchor alignment, Color color, bool bold)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Text text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Button CreateButton(
            Transform parent, string name, string label, int fontSize, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.9f, 0.75f, 0.38f, 0.95f);

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            // 与清洁界面的金色按钮同一套状态色
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.9f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            button.colors = colors;

            Text text = CreateText(rect, "Text", fontSize,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                TextAnchor.MiddleCenter, new Color(0.12f, 0.12f, 0.12f, 1f), true);
            text.rectTransform.offsetMin = new Vector2(4f, 2f);
            text.rectTransform.offsetMax = new Vector2(-4f, -2f);
            text.text = label;

            return button;
        }

        private static string FormatNumber(int value)
        {
            if (value < 1000)
            {
                return value.ToString();
            }

            if (value < 1000000)
            {
                return (value / 1000f).ToString("0.#") + "k";
            }

            return (value / 1000000f).ToString("0.#") + "m";
        }
    }

    /// <summary>取 Canvas 的渲染相机（Overlay 为 null），供世界坐标投影使用。</summary>
    internal static class CanvasUtility
    {
        public static Camera GetCanvasCamera(RectTransform canvasRect)
        {
            Canvas canvas = canvasRect != null ? canvasRect.GetComponentInParent<Canvas>() : null;

            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return canvas.worldCamera;
        }
    }
}
