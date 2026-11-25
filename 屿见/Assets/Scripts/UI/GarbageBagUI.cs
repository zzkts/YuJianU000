using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Yujian.Core;
using Yujian.Player;

namespace Yujian.UI
{
    /// <summary>
    /// 垃圾背包 UI（策划案 2.1.6，2026-11-25）。
    ///
    /// 位置：屏幕底部正中。2026-11-25 用户拍板「清洁与建筑的按钮放置到两侧」，
    /// 底部中间让给背包，所以这里不再和底部导航抢位置。
    ///
    /// 显隐：**常驻显示，不按区域隐藏**（用户明确「去除不显示的逻辑」）。
    /// 未交互时向下缩，只露出背包图标的上班部分；鼠标悬停 / 按下 / 转移中会自动升起。
    ///
    /// 转移手势按背包等级变化（<see cref="GarbageBagTransferMode"/>）：
    ///   · 1~19 级：按住不放才转移，松手停；
    ///   · 20~49 级：点一下开始，再点一下停；
    ///   · 50 级起：退出关卡时自动把包里剩下的转完。
    ///
    /// 转移目标永远是 <see cref="PlayerGarbage"/>（岛屿垃圾存储站），
    /// 并且只在**关卡外**允许——关卡内背包是缰绳，不能自己放水。
    ///
    /// 本类不持有数据：等级 / 存量 / 容量都在 <see cref="GarbageBag"/> 里。
    /// </summary>
    [DisallowMultipleComponent]
    public class GarbageBagUI : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [Header("依赖")]
        [Tooltip("垃圾背包。留空时自动查找")]
        [SerializeField] private GarbageBag bag;

        [Tooltip("岛屿垃圾存储站。转移目标；留空时自动查找")]
        [SerializeField] private PlayerGarbage storage;

        [Tooltip("资源栏。转移图标飞向它；留空时自动查找")]
        [SerializeField] private RecyclingHudUI resourceHud;

        [Tooltip("区域状态。用来判断能不能转移（关卡内禁止）以及建筑区要不要抬高")]
        [SerializeField] private GameAreaController areas;

        [Header("布局")]
        [Tooltip("平时的底部高度（像素，相对 Canvas 底部）")]
        [SerializeField] private float bottomOffset = 16f;

        [Tooltip("建筑区时抬高多少，避免压住建筑栏（栏位是底部居中 1100×186）。" +
                 "实机看一眼再调，0 = 不抬高")]
        [SerializeField] private float buildingAreaLift = 200f;

        [Tooltip("收起时向下沉多少像素。面板高 150 时，90 大约只露出上 40%")]
        [SerializeField] private float collapsedDrop = 92f;

        [Tooltip("展开 / 收起的跟随速度")]
        [SerializeField, Min(0.1f)] private float expandSpeed = 10f;

        [Tooltip("失去交互后多久自动收起（秒）")]
        [SerializeField, Min(0f)] private float autoCollapseDelay = 3f;

        [Header("手势")]
        [Tooltip("长按判定时长（秒）。1~19 级按住超过这个时间才开始转移")]
        [SerializeField, Min(0.05f)] private float longPressSeconds = 0.35f;

        [Header("表现")]
        [Tooltip("转移时每秒最多生成几个飞行图标")]
        [SerializeField, Min(1f)] private float iconPerSecond = 8f;

        [Tooltip("单个图标从背包飞到资源栏的时长")]
        [SerializeField, Min(0.05f)] private float iconFlightDuration = 0.42f;

        [Tooltip("同时在飞的图标上限")]
        [SerializeField, Min(1)] private int maxFlyingIcons = 8;

        [Header("配色")]
        [SerializeField] private Color panelColor = new Color(0.07f, 0.09f, 0.12f, 0.86f);
        [SerializeField] private Color textColor = new Color(0.95f, 0.95f, 0.95f, 1f);
        [SerializeField] private Color accentColor = new Color(1f, 0.84f, 0.42f, 1f);
        [SerializeField] private Color fullColor = new Color(0.90f, 0.34f, 0.30f, 1f);
        [SerializeField] private Color barBackColor = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField] private Color barFillColor = new Color(0.42f, 0.78f, 0.52f, 1f);

        [Header("调试")]
        [SerializeField] private bool logBagUiFlow = true;

        private const float PanelWidth = 340f;
        private const float PanelHeight = 150f;

        private static Sprite whiteSprite;

        /// <summary>
        /// 1×1 纯白 sprite。Image 没有 sprite 时 fillAmount 不生效（血条会一直满），
        /// 所以进度条必须给它一张图。
        /// </summary>
        private static Sprite WhiteSprite
        {
            get
            {
                if (whiteSprite == null)
                {
                    whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f),
                        new Vector2(0.5f, 0.5f));
                    whiteSprite.name = "BagUI_White";
                }

                return whiteSprite;
            }
        }

        private RectTransform panelRect;
        private RectTransform canvasRect;
        private Camera canvasCamera;
        private Image panelImage;
        private Image bagIcon;
        private Image barFill;
        private Text countText;
        private Text levelText;
        private Button upgradeButton;
        private Text upgradeLabel;

        private readonly List<FlyingIcon> flyingIcons = new List<FlyingIcon>();
        private readonly Stack<Image> iconPool = new Stack<Image>();

        private bool expanded;
        private bool pointerInside;
        private bool pointerDown;
        private float pointerDownTime;
        private float lastInteractionTime;
        private float transferAccumulator;
        private float iconAccumulator;
        private bool transferActive;
        private float lastRejectToastTime = -10f;
        private float lastMoneyRefreshTime;

        private struct FlyingIcon
        {
            public RectTransform Rect;
            public Vector2 Start;
            public Vector2 Target;
            public float Elapsed;
        }

        private void Awake()
        {
            ResolveDependencies();

            panelRect = transform as RectTransform;

            if (panelRect == null)
            {
                Debug.LogError("[GarbageBagUI] 本组件必须挂在带 RectTransform 的 UI 物体上。", this);
                enabled = false;
                return;
            }

            Canvas canvas = GetComponentInParent<Canvas>();

            if (canvas == null)
            {
                Debug.LogError("[GarbageBagUI] 找不到 Canvas，垃圾背包 UI 无法显示。", this);
                enabled = false;
                return;
            }

            // 场景里有两个根级画布：主 Canvas，和「清洁界面/MiningHUDRuntime」。
            // 后者只在关卡里激活；背包若挂错，就会变成「只有清洁关卡内才看得到」
            // （2026-11-25 用户报的就是这个）。这里自动迁到常驻画布。
            Canvas host = ResolveHostCanvas(canvas);

            if (host != null && host != canvas)
            {
                Debug.LogWarning($"[GarbageBagUI] 背包面板原来挂在「{canvas.name}」下，" +
                                 $"该画布会随关卡显隐。已自动迁到常驻画布「{host.name}」。", this);
                transform.SetParent(host.transform, false);
                transform.SetAsLastSibling();
                canvas = host;
            }

            canvasRect = canvas.transform as RectTransform;
            // Overlay 画布要传 null，Camera 模式要传 worldCamera，传错坐标会整体偏掉
            canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

            BuildIfNeeded();
            ApplyStaticLayout();
        }

        private void OnEnable()
        {
            if (bag != null)
            {
                bag.OnBagChanged += HandleBagChanged;
                bag.OnDepositRejected += HandleDepositRejected;
            }

            if (areas != null)
            {
                areas.OnAreaChanged += HandleAreaChanged;
            }

            RefreshAll();
        }

        private void OnDisable()
        {
            if (bag != null)
            {
                bag.OnBagChanged -= HandleBagChanged;
                bag.OnDepositRejected -= HandleDepositRejected;
            }

            if (areas != null)
            {
                areas.OnAreaChanged -= HandleAreaChanged;
            }

            transferActive = false;
        }

        private void Update()
        {
            float delta = Time.deltaTime;

            UpdateExpand(delta);
            UpdateGesture(delta);
            UpdateTransfer(delta);
            UpdateFlyingIcons(delta);
        }

        // ---------------- 转移 ----------------

        private void UpdateGesture(float delta)
        {
            if (bag == null)
            {
                return;
            }

            switch (bag.TransferMode)
            {
                case GarbageBagTransferMode.LongPress:
                    if (pointerDown)
                    {
                        if (!transferActive && Time.unscaledTime - pointerDownTime >= longPressSeconds)
                        {
                            BeginTransfer("长按");
                        }
                    }
                    else if (transferActive)
                    {
                        EndTransfer("松手");
                    }

                    break;

                case GarbageBagTransferMode.SingleClick:
                    // 点击在 OnPointerUp 里切换，这里不处理
                    break;

                case GarbageBagTransferMode.AutoOnExit:
                    // 自动模式：由区域切换触发，这里不处理
                    break;
            }
        }

        private void UpdateTransfer(float delta)
        {
            if (!transferActive || bag == null || storage == null)
            {
                return;
            }

            // 关卡内不允许转移（背包是缰绳）
            if (bag.IsOpen)
            {
                EndTransfer("进入关卡");
                return;
            }

            if (bag.Current <= 0)
            {
                EndTransfer("背包已空");
                return;
            }

            transferAccumulator += Mathf.Max(1f, bag.TransferPerSecond) * delta;
            int want = Mathf.FloorToInt(transferAccumulator);

            if (want <= 0)
            {
                return;
            }

            transferAccumulator -= want;

            int moved = bag.TryTake(want);

            if (moved <= 0)
            {
                EndTransfer("背包已空");
                return;
            }

            storage.AddToStorage(moved);

            iconAccumulator += delta;

            float interval = 1f / Mathf.Max(1f, iconPerSecond);

            while (iconAccumulator >= interval && flyingIcons.Count < maxFlyingIcons)
            {
                iconAccumulator -= interval;
                SpawnFlyingIcon();
            }

            // 转移过程中保持展开
            lastInteractionTime = Time.unscaledTime;
            expanded = true;
        }

        private void BeginTransfer(string reason)
        {
            if (bag == null || bag.IsOpen || bag.Current <= 0)
            {
                return;
            }

            transferActive = true;
            transferAccumulator = 0f;
            iconAccumulator = 0f;
            expanded = true;
            lastInteractionTime = Time.unscaledTime;

            if (logBagUiFlow)
            {
                Debug.Log($"[GarbageBagUI] 开始转移（{reason}），{bag.Current}/{bag.Capacity}。", this);
            }
        }

        private void EndTransfer(string reason)
        {
            if (!transferActive)
            {
                return;
            }

            transferActive = false;

            if (logBagUiFlow)
            {
                Debug.Log($"[GarbageBagUI] 停止转移（{reason}），剩余 {bag.Current}/{bag.Capacity}。", this);
            }
        }

        // ---------------- 指针 ----------------

        public void OnPointerEnter(PointerEventData eventData)
        {
            pointerInside = true;
            expanded = true;
            lastInteractionTime = Time.unscaledTime;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            pointerInside = false;
            lastInteractionTime = Time.unscaledTime;

            if (bag != null && bag.TransferMode == GarbageBagTransferMode.LongPress)
            {
                // 手指/鼠标移出去，长按自然也断了
                pointerDown = false;
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            // 点升级按钮时不要把「按下」当成转移手势——事件会从按钮冒泡到面板上
            if (IsOverUpgradeButton(eventData))
            {
                return;
            }

            pointerDown = true;
            pointerDownTime = Time.unscaledTime;
            expanded = true;
            lastInteractionTime = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            // 无论松手在哪，都要清掉按住状态，否则长按会一直转下去
            pointerDown = false;
            lastInteractionTime = Time.unscaledTime;

            if (IsOverUpgradeButton(eventData))
            {
                return;
            }

            if (bag == null)
            {
                return;
            }

            if (bag.TransferMode == GarbageBagTransferMode.SingleClick)
            {
                if (transferActive)
                {
                    EndTransfer("再次点击");
                }
                else
                {
                    BeginTransfer("点击");
                }
            }
        }

        /// <summary>指针是否落在升级按钮上。用来把手势和按钮点击分开。</summary>
        private bool IsOverUpgradeButton(PointerEventData eventData)
        {
            if (upgradeButton == null || eventData == null)
            {
                return false;
            }

            RectTransform rect = upgradeButton.transform as RectTransform;

            return rect != null &&
                   RectTransformUtility.RectangleContainsScreenPoint(rect, eventData.position, canvasCamera);
        }

        // ---------------- 升级 ----------------

        private void HandleUpgradeClicked()
        {
            if (bag == null)
            {
                return;
            }

            GarbageBagTransferMode before = bag.TransferMode;

            if (!bag.TryUpgrade())
            {
                if (bag.IsMaxLevel)
                {
                    ToastUI.Show($"背包已满级（{bag.Level} 级）");
                }
                else
                {
                    ToastUI.Show($"金币不足，升级需要 {bag.NextUpgradeCost}");
                }

                return;
            }

            GarbageBagTransferMode after = bag.TransferMode;

            if (after != before)
            {
                // 只在发生变化时点破新能力，避免提前剧透
                ToastUI.Show($"背包 {bag.Level} 级：现在{after.ToChineseName()}");
            }
            else
            {
                ToastUI.Show($"背包升到 {bag.Level} 级，容量 {bag.Capacity}");
            }

            RefreshAll();
        }

        // ---------------- 刷新 ----------------

        private void HandleBagChanged(int level, int current, int capacity)
        {
            RefreshAll();
        }

        private void HandleDepositRejected(int rejected)
        {
            // 满包时每块垃圾都会拒一次，节流一下，别把屏幕刷满
            if (Time.unscaledTime - lastRejectToastTime < 1.5f)
            {
                return;
            }

            lastRejectToastTime = Time.unscaledTime;
            ToastUI.Show("存储已达上限，回岛卸货");
        }

        private void HandleAreaChanged(GameArea area)
        {
            if (bag == null)
            {
                return;
            }

            // 50 级起：退出关卡自动把包里剩下的转走
            if (bag.TransferMode == GarbageBagTransferMode.AutoOnExit &&
                area != GameArea.Mining && bag.Current > 0)
            {
                BeginTransfer("退出关卡自动转移");
            }
        }

        private void RefreshAll()
        {
            if (bag == null)
            {
                return;
            }

            if (countText != null)
            {
                countText.text = $"{bag.Current} / {bag.Capacity}";
                countText.color = bag.IsFull ? fullColor : textColor;
            }

            if (levelText != null)
            {
                levelText.text = bag.IsMaxLevel
                    ? $"Lv.{bag.Level} · 满级 · {bag.TransferMode.ToChineseName()}"
                    : $"Lv.{bag.Level} · {bag.TransferMode.ToChineseName()}";
            }

            if (barFill != null)
            {
                float ratio = bag.Capacity <= 0 ? 0f : Mathf.Clamp01((float)bag.Current / bag.Capacity);
                barFill.fillAmount = ratio;
                barFill.color = bag.IsFull ? fullColor : barFillColor;
            }

            if (upgradeLabel != null)
            {
                if (bag.IsMaxLevel)
                {
                    upgradeLabel.text = "已满级";
                }
                else
                {
                    upgradeLabel.text = $"升级 {bag.NextUpgradeCost}";
                }
            }

            if (upgradeButton != null)
            {
                upgradeButton.interactable = !bag.IsMaxLevel;
            }

            if (panelImage != null)
            {
                panelImage.color = bag.IsFull ? WithAlpha(fullColor, 0.28f) : panelColor;
            }
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        // ---------------- 动画 ----------------

        private void UpdateExpand(float delta)
        {
            if (panelRect == null)
            {
                return;
            }

            if (!transferActive && !pointerInside && !pointerDown &&
                Time.unscaledTime - lastInteractionTime >= autoCollapseDelay)
            {
                expanded = false;
            }

            float lift = areas != null && areas.CurrentArea == GameArea.Building ? buildingAreaLift : 0f;
            float targetY = bottomOffset + lift - (expanded ? 0f : collapsedDrop);

            Vector2 position = panelRect.anchoredPosition;
            float speed = Mathf.Max(0.1f, expandSpeed);
            position.y = Mathf.Lerp(position.y, targetY, 1f - Mathf.Exp(-speed * delta));
            panelRect.anchoredPosition = position;
        }

        private void SpawnFlyingIcon()
        {
            if (canvasRect == null || bagIcon == null || bagIcon.sprite == null)
            {
                return;
            }

            Vector2 start;
            Vector2 target;

            Vector2 bagScreen = RectTransformUtility.WorldToScreenPoint(canvasCamera, bagIcon.rectTransform.position);

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, bagScreen, canvasCamera, out start))
            {
                return;
            }

            if (resourceHud == null || !resourceHud.TryGetResourceScreenPoint(out Vector2 resourceScreen))
            {
                resourceScreen = new Vector2(Screen.width * 0.92f, Screen.height * 0.95f);
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, resourceScreen, canvasCamera, out target))
            {
                return;
            }

            Image icon = RentIcon();

            if (icon == null)
            {
                return;
            }

            RectTransform rect = icon.rectTransform;
            rect.SetParent(canvasRect, false);
            rect.anchoredPosition = start;
            rect.localScale = Vector3.one * 0.6f;
            icon.gameObject.SetActive(true);

            flyingIcons.Add(new FlyingIcon
            {
                Rect = rect,
                Start = start,
                Target = target,
                Elapsed = 0f,
            });
        }

        private void UpdateFlyingIcons(float delta)
        {
            if (flyingIcons.Count == 0)
            {
                return;
            }

            float duration = Mathf.Max(0.05f, iconFlightDuration);

            for (int i = flyingIcons.Count - 1; i >= 0; i--)
            {
                FlyingIcon item = flyingIcons[i];
                item.Elapsed += delta;

                float t = Mathf.Clamp01(item.Elapsed / duration);
                float eased = Mathf.SmoothStep(0f, 1f, t);

                if (item.Rect != null)
                {
                    Vector2 position = Vector2.Lerp(item.Start, item.Target, eased);
                    position.y += Mathf.Sin(t * Mathf.PI) * 26f;
                    item.Rect.anchoredPosition = position;
                    item.Rect.localScale = Vector3.one * Mathf.Lerp(0.6f, 0.32f, eased);
                }

                if (t >= 1f)
                {
                    if (item.Rect != null)
                    {
                        item.Rect.gameObject.SetActive(false);
                        iconPool.Push(item.Rect.GetComponent<Image>());
                    }

                    flyingIcons.RemoveAt(i);
                }
                else
                {
                    flyingIcons[i] = item;
                }
            }
        }

        private Image RentIcon()
        {
            while (iconPool.Count > 0)
            {
                Image pooled = iconPool.Pop();

                if (pooled != null)
                {
                    return pooled;
                }
            }

            GameObject go = new GameObject("BagFlyIcon", typeof(RectTransform), typeof(Image));
            Image image = go.GetComponent<Image>();
            image.sprite = bagIcon != null ? bagIcon.sprite : null;
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.color = new Color(1f, 1f, 1f, 0.95f);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(40f, 40f);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            return image;
        }

        // ---------------- 构建 ----------------

        /// <summary>
        /// 背包该挂哪个画布：跟着底部导航走。
        /// 底部导航是常驻 HUD，一定挂在主 Canvas 上；而场景里还有一个
        /// 「清洁界面/MiningHUDRuntime」，只在关卡里激活——挂到它下面就会变成
        /// 「只有清洁关卡内才看得到背包」。
        /// </summary>
        private static Canvas ResolveHostCanvas(Canvas fallback)
        {
            BottomNavUI nav = FindFirstObjectByType<BottomNavUI>(FindObjectsInactive.Include);

            if (nav != null)
            {
                Canvas navCanvas = nav.GetComponentInParent<Canvas>();

                if (navCanvas != null)
                {
                    return navCanvas;
                }
            }

            return fallback;
        }

        private void ResolveDependencies()
        {
            if (bag == null)
            {
                bag = FindFirstObjectByType<GarbageBag>(FindObjectsInactive.Include);
            }

            if (storage == null)
            {
                storage = FindFirstObjectByType<PlayerGarbage>(FindObjectsInactive.Include);
            }

            if (resourceHud == null)
            {
                resourceHud = FindFirstObjectByType<RecyclingHudUI>(FindObjectsInactive.Include);
            }

            if (areas == null)
            {
                areas = FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);
            }
        }

        private void ApplyStaticLayout()
        {
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            panelRect.anchoredPosition = new Vector2(0f, bottomOffset - collapsedDrop);
        }

        /// <summary>
        /// 子物体在运行时搭出来（向导只放一个空面板）。
        /// 这样美术要换图时只改 Bag Icon 的 Sprite，不用重跑向导。
        /// </summary>
        private void BuildIfNeeded()
        {
            panelImage = GetComponent<Image>();

            if (panelImage == null)
            {
                panelImage = gameObject.AddComponent<Image>();
            }

            panelImage.color = panelColor;
            panelImage.raycastTarget = true;

            bagIcon = FindChild<Image>("BagIcon");

            if (bagIcon == null)
            {
                bagIcon = CreateImage("BagIcon", new Vector2(-112f, 78f), new Vector2(116f, 116f));
                bagIcon.sprite = GarbageIconFactory.Create();
                bagIcon.preserveAspect = true;
                bagIcon.raycastTarget = false;
            }

            countText = FindChild<Text>("CountText");

            if (countText == null)
            {
                countText = CreateText("CountText", new Vector2(45f, 112f), new Vector2(200f, 32f), 26,
                    TextAnchor.MiddleLeft, textColor);
            }

            levelText = FindChild<Text>("LevelText");

            if (levelText == null)
            {
                levelText = CreateText("LevelText", new Vector2(45f, 86f), new Vector2(200f, 24f), 16,
                    TextAnchor.MiddleLeft, accentColor);
            }

            RectTransform barBack = FindChildRect("BarBack");

            if (barBack == null)
            {
                barBack = CreateBar("BarBack", new Vector2(45f, 64f), new Vector2(200f, 10f), barBackColor);
            }

            barFill = FindChild<Image>("BarFill");

            if (barFill == null)
            {
                Image fill = CreateImage("BarFill", new Vector2(45f, 64f), new Vector2(200f, 10f));
                fill.sprite = WhiteSprite;
                fill.color = barFillColor;
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillOrigin = (int)Image.OriginHorizontal.Left;
                fill.fillAmount = 0f;
                fill.raycastTarget = false;
                barFill = fill;
            }

            upgradeButton = FindChild<Button>("UpgradeButton");

            if (upgradeButton == null)
            {
                upgradeButton = CreateButton("UpgradeButton", new Vector2(45f, 24f), new Vector2(200f, 38f));
            }

            upgradeLabel = upgradeButton.GetComponentInChildren<Text>();

            if (upgradeLabel == null)
            {
                upgradeLabel = CreateText("Label", Vector2.zero, new Vector2(200f, 38f), 18,
                    TextAnchor.MiddleCenter, textColor);
                upgradeLabel.transform.SetParent(upgradeButton.transform, false);
                upgradeLabel.rectTransform.anchoredPosition = Vector2.zero;
            }

            upgradeButton.onClick.RemoveListener(HandleUpgradeClicked);
            upgradeButton.onClick.AddListener(HandleUpgradeClicked);
        }

        private RectTransform CreateBar(string name, Vector2 position, Vector2 size, Color color)
        {
            Image image = CreateImage(name, position, size);
            image.color = color;
            image.raycastTarget = false;
            return image.rectTransform;
        }

        private Image CreateImage(string name, Vector2 position, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            return go.GetComponent<Image>();
        }

        private Text CreateText(string name, Vector2 position, Vector2 size, int fontSize,
            TextAnchor alignment, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Text text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private Button CreateButton(string name, Vector2 position, Vector2 size)
        {
            Image background = CreateImage(name, position, size);
            background.color = new Color(1f, 0.84f, 0.42f, 0.22f);

            Button button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);
            button.colors = colors;

            return button;
        }

        private T FindChild<T>(string name) where T : Component
        {
            Transform child = transform.Find(name);
            return child != null ? child.GetComponent<T>() : null;
        }

        private RectTransform FindChildRect(string name)
        {
            Transform child = transform.Find(name);
            return child as RectTransform;
        }
    }
}
