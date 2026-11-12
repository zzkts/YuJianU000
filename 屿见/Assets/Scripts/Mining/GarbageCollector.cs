using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Yujian.Player;
using Yujian.UI;

namespace Yujian.Mining
{
    /// <summary>
    /// 掉落垃圾的统一收集。
    /// 掉下来的垃圾先排队，每隔固定时间把这一批**一起**收走：
    /// 批次里的所有垃圾块同时飞向画面右上角的资源栏，
    /// 先加速后减速、带一段向上的弧线，拖着一条渐隐的尾迹；
    /// 到达时增加垃圾数量，并让资源栏数字 / 图标弹一下。
    ///
    /// 碎片在屏幕空间 Canvas 上画不出 Trail，所以这里用世界空间的物体飞行，
    /// 目标点由 HUD 的资源栏屏幕坐标反算得到。
    /// </summary>
    public class GarbageCollector : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("资源栏所在界面。留空时自动查找")]
        [SerializeField] private MiningHudUI hud;

        [Tooltip("玩家货币。留空时自动查找")]
        [SerializeField] private PlayerCurrency currency;

        [Tooltip("取景相机，用来把 UI 屏幕坐标换算成世界坐标。留空时用 Main Camera")]
        [SerializeField] private Camera viewCamera;

        [Header("收集节奏")]
        [Tooltip("每隔多少秒把队列里攒下的垃圾统一收走一批")]
        [SerializeField, Min(0.01f)] private float collectInterval = 0.5f;

        [Tooltip("单块垃圾从脱落点飞到资源栏的时长（秒）")]
        [SerializeField, Min(0.05f)] private float flightDuration = 0.45f;

        [Header("等待回收")]
        [Tooltip("垃圾脱落后在原地缩小的时长（秒）。0 = 不缩小，直接按原尺寸等待收集")]
        [SerializeField, Min(0f)] private float shrinkDuration = 0.15f;

        [Tooltip("缩小到脱落时尺寸的比例。0.45 = 缩到 45% 后停在原地排队")]
        [SerializeField, Range(0.05f, 1f)] private float queuedScale = 0.45f;

        [Tooltip("飞行弧线的最高点高度（世界单位）")]
        [SerializeField, Min(0f)] private float arcHeight = 1.2f;

        [Tooltip("目标点在相机前方的距离，用于把资源栏屏幕坐标换算成世界坐标")]
        [SerializeField, Min(0.1f)] private float targetDepth = 5f;

        [Header("飞行表现")]
        [Tooltip("起飞瞬间垃圾块的缩放倍数，让它在空中稍微显眼")]
        [SerializeField, Min(0.1f)] private float flightScaleMultiplier = 1.35f;

        [Tooltip("飞行时自转速度（度 / 秒）")]
        [SerializeField] private float spinSpeed = 240f;

        [Tooltip("脱落 / 飞行时替换的材质。留空时运行时生成一个 Unlit 材质，" +
                 "避免 Lit 材质在较暗的清洁区里看不清")]
        [SerializeField] private Material flightMaterial;

        [Tooltip("运行时生成的飞行材质颜色（flightMaterial 留空时生效）")]
        [SerializeField] private Color flightColor = new Color(0.93f, 0.94f, 0.97f, 1f);

        [Header("尾迹")]
        [Tooltip("尾迹材质。留空时用运行时创建的白色 Sprites/Default 材质")]
        [SerializeField] private Material trailMaterial;

        [SerializeField] private Color trailStartColor = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField] private Color trailEndColor = new Color(1f, 1f, 1f, 0f);
        [SerializeField, Min(0.01f)] private float trailTime = 0.35f;
        [SerializeField, Min(0f)] private float trailStartWidth = 0.12f;
        [SerializeField, Min(0f)] private float trailEndWidth = 0f;

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
        private Coroutine collectRoutine;
        private Material runtimeTrailMaterial;
        private Material runtimeFlightMaterial;

        /// <summary>还没被收走的垃圾块数量。</summary>
        public int PendingCount => pending.Count;

        private void Awake()
        {
            ResolveDependencies();

            if (trailMaterial == null)
            {
                runtimeTrailMaterial = CreateRuntimeTrailMaterial();
            }

            if (flightMaterial == null)
            {
                runtimeFlightMaterial = CreateRuntimeFlightMaterial(flightColor);
            }
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

        private void OnDestroy()
        {
            if (runtimeTrailMaterial != null)
            {
                Destroy(runtimeTrailMaterial);
            }

            if (runtimeFlightMaterial != null)
            {
                Destroy(runtimeFlightMaterial);
            }
        }

        /// <summary>编辑器向导调用。</summary>
        public void ConfigureForSetup(MiningHudUI newHud, PlayerCurrency newCurrency,
            Camera newCamera, Material newTrailMaterial)
        {
            hud = newHud;
            currency = newCurrency;
            viewCamera = newCamera;
            trailMaterial = newTrailMaterial;
        }

        private void ResolveDependencies()
        {
            if (hud == null)
            {
                hud = FindFirstObjectByType<MiningHudUI>(FindObjectsInactive.Include);
            }

            if (currency == null)
            {
                currency = FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
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

                // 一脱落就换成不依赖光照的材质：垃圾在暗处等待收集时也看得见。
                ApplyFlightMaterial(piece);

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

                if (hud != null && !hud.IsVisible)
                {
                    // 玩家已经离开清洁区，资源栏不在屏幕上：
                    // 整批直接结算并清掉世界里的垃圾块，避免垃圾飘在主岛上。
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
                        Debug.Log($"[GarbageCollector] 界面隐藏，直接结算一批 {batchBuffer.Count} 块，垃圾 +{batchAmount}。", this);
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

        private IEnumerator FlyRoutine(PendingPiece entry)
        {
            GameObject piece = entry.Piece;
            TrailRenderer trail = AttachTrail(piece);

            // 还在缩小的块直接落到目标尺寸再起飞，避免起飞后 Update 又把它改回大尺寸
            FinishShrinkImmediately(piece);

            Vector3 start = piece.transform.position;
            Vector3 startScale = piece.transform.localScale;
            piece.transform.localScale = startScale * Mathf.Max(0.1f, flightScaleMultiplier);

            float duration = Mathf.Max(0.05f, flightDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;

                float t = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0f, 1f, t);
                Vector3 target = ResolveTargetWorld();
                Vector3 position = Vector3.Lerp(start, target, eased);
                position += Vector3.up * (arcHeight * Mathf.Sin(t * Mathf.PI));

                if (piece != null)
                {
                    piece.transform.position = position;
                    piece.transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
                }

                yield return null;
            }

            AddGarbage(entry.Amount);

            // 一批 4 块只分到 2 点垃圾时，会有 0 点的块；
            // 它们只负责飞行动画，不应该让资源栏空弹一下。
            // 同批的块在同一帧到达，重复调用会重启同一个弹跳协程，视觉上只弹一次。
            if (entry.Amount > 0 && hud != null)
            {
                hud.PlayResourcePunch();
            }

            if (trail != null)
            {
                trail.emitting = false;
            }

            if (piece != null)
            {
                Destroy(piece);
            }
        }

        private Vector3 ResolveTargetWorld()
        {
            Camera camera = viewCamera != null ? viewCamera : Camera.main;

            if (camera == null)
            {
                return Vector3.zero;
            }

            Vector2 screenPoint;

            if (hud == null || !hud.TryGetResourceScreenPoint(out screenPoint))
            {
                screenPoint = new Vector2(Screen.width * 0.92f, Screen.height * 0.95f);
            }

            float depth = Mathf.Max(camera.nearClipPlane + 0.1f, targetDepth);
            return camera.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, depth));
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

        /// <summary>立刻结束缩小（起飞时用），把块固定到排队尺寸。</summary>
        private void FinishShrinkImmediately(GameObject piece)
        {
            if (piece == null)
            {
                return;
            }

            for (int i = shrinking.Count - 1; i >= 0; i--)
            {
                if (shrinking[i].Piece != piece)
                {
                    continue;
                }

                Vector3 target = shrinking[i].TargetScale;
                shrinking.RemoveAt(i);
                piece.transform.localScale = target;
                return;
            }
        }

        /// <summary>
        /// 把垃圾块的材质换成不依赖场景光照的版本。
        /// 只换 Renderer，不动刚加上的 TrailRenderer。
        /// </summary>
        private void ApplyFlightMaterial(GameObject piece)
        {
            Material material = flightMaterial != null ? flightMaterial : runtimeFlightMaterial;

            if (material == null || piece == null)
            {
                return;
            }

            Renderer[] renderers = piece.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null || renderers[i] is TrailRenderer)
                {
                    continue;
                }

                renderers[i].sharedMaterial = material;
            }
        }

        private TrailRenderer AttachTrail(GameObject piece)
        {
            TrailRenderer trail = piece.GetComponent<TrailRenderer>();

            if (trail == null)
            {
                trail = piece.AddComponent<TrailRenderer>();
            }

            Material material = trailMaterial != null ? trailMaterial : runtimeTrailMaterial;

            if (material != null)
            {
                trail.sharedMaterial = material;
            }

            trail.time = trailTime;
            trail.minVertexDistance = 0.02f;
            trail.startWidth = trailStartWidth;
            trail.endWidth = trailEndWidth;
            trail.startColor = trailStartColor;
            trail.endColor = trailEndColor;
            trail.numCapVertices = 2;
            trail.numCornerVertices = 2;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.emitting = true;
            trail.Clear();
            return trail;
        }

        private void AddGarbage(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            if (currency == null)
            {
                Debug.LogError("[GarbageCollector] 找不到 PlayerCurrency，垃圾无法入账。", this);
                return;
            }

            // AddMoney 对 <= 0 会报警，这里已经挡掉了
            currency.AddMoney(amount);
        }

        private static Material CreateRuntimeFlightMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                // 找不到 Unlit 时保留原材质，至少不会把垃圾换成洋红色。
                Debug.LogWarning("[GarbageCollector] 找不到 Unlit 着色器，飞行垃圾仍使用原材质。", null);
                return null;
            }

            Material material = new Material(shader)
            {
                name = "Garbage_Flight_Runtime",
            };

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            return material;
        }

        private static Material CreateRuntimeTrailMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default");

            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            }

            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader)
            {
                name = "Garbage_Trail_Runtime",
            };

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.white);
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }

            return material;
        }

        private void OnValidate()
        {
            collectInterval = Mathf.Max(0.01f, collectInterval);
            flightDuration = Mathf.Max(0.05f, flightDuration);
            shrinkDuration = Mathf.Max(0f, shrinkDuration);
            queuedScale = Mathf.Clamp(queuedScale, 0.05f, 1f);
            arcHeight = Mathf.Max(0f, arcHeight);
            targetDepth = Mathf.Max(0.1f, targetDepth);
            flightScaleMultiplier = Mathf.Max(0.1f, flightScaleMultiplier);
            trailTime = Mathf.Max(0.01f, trailTime);
            trailStartWidth = Mathf.Max(0f, trailStartWidth);
            trailEndWidth = Mathf.Max(0f, trailEndWidth);
        }
    }
}
