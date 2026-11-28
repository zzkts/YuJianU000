using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Player;
using Yujian.UI;

namespace Yujian.Mining
{
    /// <summary>
    /// 掉落垃圾的统一收集。
    ///
    /// 掉下来的垃圾先在世界里原地缩一下、排队；每隔 <see cref="collectInterval"/> 秒
    /// 把这一批**一起**收走，收走的瞬间整体换成 UI 图标，沿弧线飞向容器
    /// （关卡内是垃圾背包，岛上是右上角资源栏），到达时入账并让容器弹一下。
    ///
    /// 2026-11-26 用户反馈「转移视觉割裂，像垃圾突然消失」，改的就是这里：
    /// 旧实现让**世界空间的垃圾块**飞向「屏幕坐标反算出来的世界点」，
    /// 飞到尽头时既不在 UI 层、也没真正进入 UI，画面上就是凭空消失。
    /// 现在整段飞行都在 Canvas 上做：
    ///   · 世界块一被收走就销毁，同一帧在它的屏幕位置生成 UI 图标，位置连续；
    ///   · 图标在 UI 层飞，终点就是背包 / 资源栏图标的屏幕坐标，落点准确；
    ///   · 不再需要拖尾、材质替换、世界弧线，也不会有深度错位。
    ///
    /// 本类不产垃圾（GarbageDropper 负责）、不决定入账规则（PlayerGarbage 负责）。
    /// </summary>
    public class GarbageCollector : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("全局唯一的「垃圾 · 金币」资源栏。留空时自动查找；岛上的垃圾飞向它")]
        [SerializeField] private RecyclingHudUI resourceHud;

        [Tooltip("玩家垃圾。留空时自动查找（2026-11-19 起垃圾是独立资源，金币只由回收站产出）")]
        [SerializeField] private PlayerGarbage garbage;

        [Tooltip("垃圾背包。关卡内垃圾飞向它；留空时自动查找")]
        [SerializeField] private GarbageBag bag;

        [Tooltip("背包 UI。用来取图标的屏幕位置；留空时自动查找")]
        [SerializeField] private GarbageBagUI bagUI;

        [Tooltip("取景相机，用来把垃圾块的世界坐标换算成屏幕坐标。留空时用 Main Camera")]
        [SerializeField] private Camera viewCamera;

        [Tooltip("承载飞行动画的 Canvas。留空时跟着背包 / 资源栏找")]
        [SerializeField] private Canvas uiCanvas;

        [Header("收集节奏")]
        [Tooltip("每隔多少秒把队列里攒下的垃圾统一收走一批")]
        [SerializeField, Min(0.01f)] private float collectInterval = 0.5f;

        [Tooltip("单个 UI 图标从垃圾块飞到容器的时长（秒）")]
        [SerializeField, Min(0.05f)] private float flightDuration = 0.45f;

        [Header("等待回收")]
        [Tooltip("垃圾脱落后在原地缩小的时长（秒）。0 = 不缩小，直接按原尺寸等待收集")]
        [SerializeField, Min(0f)] private float shrinkDuration = 0.15f;

        [Tooltip("缩小到脱落时尺寸的比例。0.45 = 缩到 45% 后停在原地排队")]
        [SerializeField, Range(0.05f, 1f)] private float queuedScale = 0.45f;

        [Header("UI 飞行表现")]
        [Tooltip("飞行图标的大小（像素）")]
        [SerializeField] private Vector2 iconSize = new Vector2(46f, 46f);

        [Tooltip("弧线最高点（像素）。0 = 直线")]
        [SerializeField, Min(0f)] private float iconArcHeight = 70f;

        [Tooltip("到达前最后这段开始淡出（0.75 = 只淡出最后 25%）")]
        [SerializeField, Range(0f, 1f)] private float iconFadeStart = 0.75f;

        [Header("调试")]
        [Tooltip("是否在 Console 打印收集日志")]
        [SerializeField] private bool logCollectFlow = false;

        private struct PendingPiece
        {
            public GameObject Piece;
            public int Amount;
        }

        /// <summary>正在原地缩小的垃圾块。缩小完就移出列表，停在 queuedScale 等待收集。</summary>
        private struct ShrinkState
        {
            public GameObject Piece;
            public Vector3 StartScale;
            public Vector3 TargetScale;
            public float Elapsed;
        }

        private readonly Queue<PendingPiece> pending = new Queue<PendingPiece>();
        private readonly List<PendingPiece> batchBuffer = new List<PendingPiece>();
        private readonly List<ShrinkState> shrinking = new List<ShrinkState>();
        private readonly Stack<Image> iconPool = new Stack<Image>();

        private Coroutine collectRoutine;
        private Sprite iconSprite;
        private Camera uiCamera;

        /// <summary>还没被收走的垃圾块数量。</summary>
        public int PendingCount => pending.Count;

        private void Awake()
        {
            ResolveDependencies();
        }

        private void OnEnable()
        {
            if (pending.Count > 0)
            {
                EnsureCollecting();
            }
        }

        private void OnDisable()
        {
            if (collectRoutine != null)
            {
                StopCoroutine(collectRoutine);
                collectRoutine = null;
            }
        }

        /// <summary>推进「脱落后原地缩小」的动画。缩小完的块停在 queuedScale 等待收集。</summary>
        private void Update()
        {
            if (shrinking.Count == 0)
            {
                return;
            }

            float delta = Time.deltaTime;

            for (int i = shrinking.Count - 1; i >= 0; i--)
            {
                ShrinkState state = shrinking[i];

                if (state.Piece == null)
                {
                    shrinking.RemoveAt(i);
                    continue;
                }

                state.Elapsed += delta;

                float duration = Mathf.Max(0.0001f, shrinkDuration);
                float t = Mathf.Clamp01(state.Elapsed / duration);

                // 先快后慢：脱手那一下收得最快，像被压扁收拢
                float eased = 1f - (1f - t) * (1f - t);
                state.Piece.transform.localScale =
                    Vector3.LerpUnclamped(state.StartScale, state.TargetScale, eased);

                if (t >= 1f)
                {
                    shrinking.RemoveAt(i);
                }
                else
                {
                    shrinking[i] = state;
                }
            }
        }

        /// <summary>
        /// 编辑器向导调用。
        /// trailMaterial 是旧世界飞行留下的参数，现在不再使用，保留签名只为不破坏已有向导。
        /// </summary>
        public void ConfigureForSetup(RecyclingHudUI newResourceHud, PlayerGarbage newGarbage,
            Camera newCamera, Material trailMaterial)
        {
            resourceHud = newResourceHud;
            garbage = newGarbage;
            viewCamera = newCamera;
        }

        private void ResolveDependencies()
        {
            if (resourceHud == null)
            {
                resourceHud = FindFirstObjectByType<RecyclingHudUI>(FindObjectsInactive.Include);
            }

            if (garbage == null)
            {
                garbage = FindFirstObjectByType<PlayerGarbage>(FindObjectsInactive.Include);
            }

            if (bag == null)
            {
                bag = FindFirstObjectByType<GarbageBag>(FindObjectsInactive.Include);
            }

            if (bagUI == null)
            {
                bagUI = FindFirstObjectByType<GarbageBagUI>(FindObjectsInactive.Include);
            }

            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }
        }

        /// <summary>
        /// 把一批刚掉下来的垃圾块放进收集队列。
        /// 奖励会平均分到这些块上；没有可视块时直接结算，避免奖励丢失。
        /// </summary>
        public void Enqueue(IReadOnlyList<GameObject> pieces, int totalAmount)
        {
            int amount = Mathf.Max(0, totalAmount);
            int validCount = CountValidPieces(pieces);

            if (validCount == 0)
            {
                AddGarbage(amount);
                return;
            }

            int baseAmount = amount / validCount;
            int remainder = amount % validCount;
            int index = 0;

            for (int i = 0; i < pieces.Count; i++)
            {
                GameObject piece = pieces[i];

                if (piece == null)
                {
                    continue;
                }

                // 掉落表现：先在原地缩小，再停在缩小的尺寸上排队等这一批被收走
                BeginShrink(piece);

                pending.Enqueue(new PendingPiece
                {
                    Piece = piece,
                    Amount = baseAmount + (index < remainder ? 1 : 0),
                });
                index++;
            }

            EnsureCollecting();
        }

        private static int CountValidPieces(IReadOnlyList<GameObject> pieces)
        {
            if (pieces == null)
            {
                return 0;
            }

            int count = 0;

            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i] != null)
                {
                    count++;
                }
            }

            return count;
        }

        private void EnsureCollecting()
        {
            if (collectRoutine == null && isActiveAndEnabled)
            {
                collectRoutine = StartCoroutine(CollectRoutine());
            }
        }

        private IEnumerator CollectRoutine()
        {
            while (true)
            {
                // 攒一个间隔，再把这一段时间里掉下来的垃圾作为一批统一收走。
                yield return new WaitForSeconds(collectInterval);

                if (pending.Count == 0)
                {
                    continue;
                }

                batchBuffer.Clear();

                while (pending.Count > 0)
                {
                    batchBuffer.Add(pending.Dequeue());
                }

                int batchAmount = 0;

                for (int i = 0; i < batchBuffer.Count; i++)
                {
                    batchAmount += batchBuffer[i].Amount;
                }

                if (!HasVisibleTarget())
                {
                    // 容器不在屏幕上（界面未构建 / 被关掉）：
                    // 整批直接结算并清掉世界里的垃圾块，避免垃圾飘在场景里。
                    for (int i = 0; i < batchBuffer.Count; i++)
                    {
                        if (batchBuffer[i].Piece != null)
                        {
                            Destroy(batchBuffer[i].Piece);
                        }
                    }

                    AddGarbage(batchAmount);

                    if (logCollectFlow)
                    {
                        Debug.Log($"[GarbageCollector] 容器不可见，直接结算一批 {batchBuffer.Count} 块，垃圾 +{batchAmount}。", this);
                    }

                    continue;
                }

                // 同一批的垃圾块同时起飞：它们的飞行时长相同，
                // 会在同一帧前后一起到达，视觉上就是「一起被收走」。
                for (int i = 0; i < batchBuffer.Count; i++)
                {
                    PendingPiece entry = batchBuffer[i];

                    if (entry.Piece == null)
                    {
                        AddGarbage(entry.Amount);
                    }
                    else
                    {
                        StartCoroutine(FlyRoutine(entry));
                    }
                }

                if (logCollectFlow)
                {
                    Debug.Log($"[GarbageCollector] 统一收走一批 {batchBuffer.Count} 块，垃圾合计 +{batchAmount}。", this);
                }
            }
        }

        /// <summary>
        /// 单个垃圾块的「世界 → UI 图标 → 容器」动画。
        /// 世界块在这里就销毁，UI 图标接在同一屏幕位置上，所以玩家看到的是连续的。
        /// </summary>
        private IEnumerator FlyRoutine(PendingPiece entry)
        {
            GameObject piece = entry.Piece;

            Vector2 start = Vector2.zero;
            bool hasStart = piece != null && TryWorldToCanvasPoint(piece.transform.position, out start);

            if (piece != null)
            {
                // 立刻退场：接下来由 UI 图标代表这块垃圾
                Destroy(piece);
            }

            Image icon = null;
            Canvas canvas = ResolveUiCanvas();

            if (hasStart && canvas != null)
            {
                icon = RentIcon(canvas);
                icon.rectTransform.anchoredPosition = start;
                icon.rectTransform.localScale = Vector3.one;
                icon.color = Color.white;
                icon.gameObject.SetActive(true);
            }

            Vector2 target = ResolveTargetCanvasPoint();
            float duration = Mathf.Max(0.05f, flightDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;

                float t = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0f, 1f, t);

                if (icon != null)
                {
                    Vector2 position = Vector2.Lerp(start, target, eased);
                    position.y += Mathf.Sin(t * Mathf.PI) * iconArcHeight;
                    icon.rectTransform.anchoredPosition = position;

                    // 排队时世界块已经缩到 45%，图标如果一上来就是原尺寸会“跳”一下。
                    // 入场先小一点，飞到 30% 时回到原尺寸，再收进容器。
                    float scale = t < 0.3f
                        ? Mathf.Lerp(0.6f, 1f, t / 0.3f)
                        : Mathf.Lerp(1f, 0.5f, (t - 0.3f) / 0.7f);
                    icon.rectTransform.localScale = Vector3.one * scale;

                    float fadeStart = Mathf.Clamp01(iconFadeStart);

                    if (t > fadeStart)
                    {
                        Color color = icon.color;
                        color.a = 1f - Mathf.InverseLerp(fadeStart, 1f, t);
                        icon.color = color;
                    }
                }

                yield return null;
            }

            AddGarbage(entry.Amount);

            // 一批 4 块只分到 2 点垃圾时，会有 0 点的块；
            // 它们只负责飞行动画，不应该让容器空弹一下。
            // 同批的块在同一帧到达，重复调用会重启同一个弹跳协程，视觉上只弹一次。
            if (entry.Amount > 0)
            {
                bool intoBag = bag != null && bag.IsOpen;

                if (intoBag && bagUI != null)
                {
                    bagUI.PlayPunch();
                }
                else if (resourceHud != null)
                {
                    resourceHud.PlayResourcePunch();
                }
            }

            if (icon != null)
            {
                ReturnIcon(icon);
            }
        }

        /// <summary>当前有没有可见的容器可以飞——关卡内看背包，岛上卡资源栏。</summary>
        private bool HasVisibleTarget()
        {
            if (bag != null && bag.IsOpen && bagUI != null)
            {
                return true;
            }

            return resourceHud != null && resourceHud.IsResourceVisible;
        }

        /// <summary>容器图标在屏幕上的位置：关卡内是背包，岛上是资源栏。</summary>
        private Vector2 ResolveTargetScreenPoint()
        {
            if (bag != null && bag.IsOpen && bagUI != null && bagUI.TryGetBagScreenPoint(out Vector2 bagPoint))
            {
                return bagPoint;
            }

            if (resourceHud != null && resourceHud.TryGetResourceScreenPoint(out Vector2 resourcePoint))
            {
                return resourcePoint;
            }

            return new Vector2(Screen.width * 0.92f, Screen.height * 0.95f);
        }

        private Vector2 ResolveTargetCanvasPoint()
        {
            Vector2 screen = ResolveTargetScreenPoint();
            Canvas canvas = ResolveUiCanvas();

            if (canvas != null &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvas.transform as RectTransform, screen, uiCamera, out Vector2 local))
            {
                return local;
            }

            return screen;
        }

        private bool TryWorldToCanvasPoint(Vector3 worldPosition, out Vector2 canvasPoint)
        {
            canvasPoint = default;

            Canvas canvas = ResolveUiCanvas();
            Camera worldCamera = viewCamera != null ? viewCamera : Camera.main;

            if (canvas == null || worldCamera == null)
            {
                return false;
            }

            Vector3 screen = worldCamera.WorldToScreenPoint(worldPosition);

            // 在相机背后就不飞了，免得图标从屏幕外窜进来
            if (screen.z < 0f)
            {
                return false;
            }

            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.transform as RectTransform, screen, uiCamera, out canvasPoint);
        }

        private Canvas ResolveUiCanvas()
        {
            if (uiCanvas == null && bagUI != null)
            {
                uiCanvas = bagUI.GetComponentInParent<Canvas>(true);
            }

            if (uiCanvas == null && resourceHud != null)
            {
                uiCanvas = resourceHud.GetComponentInParent<Canvas>(true);
            }

            if (uiCanvas != null)
            {
                // Overlay 画布要传 null，Camera 模式要传 worldCamera
                uiCamera = uiCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : uiCanvas.worldCamera;
            }

            return uiCanvas;
        }

        private Image RentIcon(Canvas canvas)
        {
            while (iconPool.Count > 0)
            {
                Image pooled = iconPool.Pop();

                if (pooled != null)
                {
                    return pooled;
                }
            }

            GameObject go = new GameObject("GarbageFlyIcon", typeof(RectTransform), typeof(Image));
            Image image = go.GetComponent<Image>();
            image.sprite = ResolveIconSprite();
            image.raycastTarget = false;
            image.preserveAspect = true;

            RectTransform rect = image.rectTransform;
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = iconSize;

            return image;
        }

        private void ReturnIcon(Image icon)
        {
            if (icon == null)
            {
                return;
            }

            icon.gameObject.SetActive(false);
            iconPool.Push(icon);
        }

        private Sprite ResolveIconSprite()
        {
            if (iconSprite == null)
            {
                iconSprite = GarbageIconFactory.Create(64);
            }

            return iconSprite;
        }

        /// <summary>开始「脱落后原地缩小」的动画。</summary>
        private void BeginShrink(GameObject piece)
        {
            if (piece == null || shrinkDuration <= 0f)
            {
                return;
            }

            float ratio = Mathf.Clamp(queuedScale, 0.05f, 1f);

            if (ratio >= 0.999f)
            {
                return;
            }

            Vector3 startScale = piece.transform.localScale;

            shrinking.Add(new ShrinkState
            {
                Piece = piece,
                StartScale = startScale,
                TargetScale = startScale * ratio,
                Elapsed = 0f,
            });
        }

        private void AddGarbage(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            if (garbage == null)
            {
                Debug.LogError("[GarbageCollector] 找不到 PlayerGarbage，垃圾无法入账。" +
                               "请运行菜单「屿见/建筑/配置垃圾回收站系统」自动接线。", this);
                return;
            }

            // AddGarbage 对 <= 0 会报警，这里已经挡掉了
            garbage.AddGarbage(amount);
        }

        private void OnValidate()
        {
            collectInterval = Mathf.Max(0.01f, collectInterval);
            flightDuration = Mathf.Max(0.05f, flightDuration);
            shrinkDuration = Mathf.Max(0f, shrinkDuration);
            queuedScale = Mathf.Clamp(queuedScale, 0.05f, 1f);
            iconSize = new Vector2(Mathf.Max(8f, iconSize.x), Mathf.Max(8f, iconSize.y));
            iconArcHeight = Mathf.Max(0f, iconArcHeight);
            iconFadeStart = Mathf.Clamp(iconFadeStart, 0f, 1f);
        }
    }
}
