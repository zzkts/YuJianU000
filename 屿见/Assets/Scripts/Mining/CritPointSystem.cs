using System.Collections.Generic;
using UnityEngine;
using Yujian.Core;
using Yujian.Core.Events;
using Yujian.Player;

namespace Yujian.Mining
{
    /// <summary>
    /// 暴击点系统（飞书 2.1.4 照明系统）。
    ///
    /// 规则：
    ///   · 装备的照明工具「刷新冷却」结束时生成一个暴击点，普通攻击不再掷骰子；
    ///   · 暴击点贴在矿石朝向相机的可见表面上，离矿石中心不超过照明工具配置的半径；
    ///   · 同时存在数量不超过照明工具配置的上限；
    ///   · 每个暴击点有存在时间（照明工具等级表），头顶进度条显示剩余时间；
    ///   · 点中后只结算暴击伤害，暴击点原地保留到时间结束，期间可以再次点击；
    ///   · 离开矿区 / 矿石死亡时清场。
    ///
    /// 本类只管生成、上限、存在时间、换位与表现；伤害由 DamageSystem，发币由 MiningInput。
    /// </summary>
    [DisallowMultipleComponent]
    public class CritPointSystem : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("场上唯一一块矿石。留空则运行时自动查找")]
        [SerializeField] private Ore ore;

        [Tooltip("照明工具。留空时不会生成暴击点")]
        [SerializeField] private MiningLightTool lightTool;

        [Tooltip("生成暴击点时用来判断矿石可见表面。留空会自动使用 Camera.main")]
        [SerializeField] private Camera viewCamera;

        [Tooltip("玩家属性：读取建筑提供的暴击点持续时间加成。留空会自动查找")]
        [SerializeField] private PlayerStats playerStats;

        [Header("生成")]
        [Tooltip("旧版「每次普通攻击 8% 概率」开关。飞书规则下应保持关闭")]
        [SerializeField] private bool legacyAttackChanceSpawn;

        [Tooltip("仅当旧版开关打开时生效（百分比）")]
        [SerializeField, Range(0f, 100f)] private float spawnChancePercent = 8f;

        [Tooltip("旧版外圈偏移，保留字段避免旧场景丢数据；新规则使用照明工具半径")]
        [SerializeField, Min(0f)] private float spawnMargin = 0.35f;

        [Tooltip("暴击点必须落在屏幕可视范围内的边距比例。0.08 = 屏幕四周各留 8%，" +
                 "避免点在屏幕外、贴边或被 HUD 挡住")]
        [SerializeField, Range(0f, 0.45f)] private float spawnScreenMargin = 0.08f;

        [Header("表现")]
        [Tooltip("暴击点直径（世界单位）。0.16 约等于屏幕上的一个小光点")]
        [SerializeField, Min(0.05f)] private float pointDiameter = 0.16f;

        [Tooltip("暴击点颜色")]
        [SerializeField] private Color pointColor = new Color(1f, 0.72f, 0.15f, 1f);

        [Tooltip("暴击火花颜色")]
        [SerializeField] private Color sparkColor = new Color(1f, 0.85f, 0.35f, 1f);

        [Tooltip("暴击点与剩余时间进度条是否透过垃圾 / 矿石显示（透视）")]
        [SerializeField] private bool showThroughObstacles = true;

        [Tooltip("暴击火花预制体；留空时优先加载 Resources/particle_Critichit，仍没有就用程序化火花")]
        [SerializeField] private GameObject sparkPrefab;

        [Tooltip("暴击音效，可留空；留空会优先加载 Resources/sound_Critichit，仍没有就用内置提示音")]
        [SerializeField] private AudioClip critSound;

        [SerializeField, Range(0f, 1f)] private float critSoundVolume = 0.8f;

        [Header("调试")]
        [SerializeField] private bool logSpawnFlow = true;

        private readonly List<CritPoint> activePoints = new List<CritPoint>();
        private Material pointMaterial;
        private Material sparkMaterial;
        private Material barBackgroundMaterial;
        private Material barFillMaterial;
        private AudioClip resolvedCritSound;
        private bool searchedCritSound;
        private GameObject resolvedSparkPrefab;
        private bool searchedSparkPrefab;
        private bool warnedMissingOre;
        private bool isInMiningArea;
        private GarbageAggregateVisual garbageVisual;
        private Ore garbageVisualOwner;
        private readonly List<GarbageAggregateVisual.CritSurface> surfaceCandidates =
            new List<GarbageAggregateVisual.CritSurface>();
        private int lastShellPieceCount = -1;
        private float cooldownRemaining;
        private Ore subscribedOre;
        private float refillDelay;
        private int burstRemaining;
        private const float BurstSpawnInterval = 0.08f;

        /// <summary>当前存活的暴击点数量。</summary>
        public int AliveCount
        {
            get
            {
                PruneConsumed();
                return activePoints.Count;
            }
        }

        /// <summary>当前生效的上限。未拥有照明工具时为 0。</summary>
        public int Capacity => lightTool != null ? lightTool.CritPointCapacity : 0;

        /// <summary>
        /// 当前生效的存在时长（秒）；0 表示永久保留、不显示进度条。
        /// = 照明工具等级的基础时长 ×（1 + 建筑提供的比例加成 PlayerStats.CritPointDurationBonus）。
        /// 已生成的暴击点不会因此变长，之后刷新的点才使用新值。
        /// </summary>
        public float Lifetime
        {
            get
            {
                if (lightTool == null || lightTool.CritPointsPersist)
                {
                    return 0f;
                }

                float baseLifetime = Mathf.Max(0.1f, lightTool.CritPointLifetime);
                float bonusRatio = playerStats != null ? playerStats.CritPointDurationBonus : 0f;

                return Mathf.Max(0.1f, baseLifetime * (1f + bonusRatio));
            }
        }

        /// <summary>当前照明工具的刷新冷却（秒）。</summary>
        public float RefreshCooldown => lightTool != null ? lightTool.RefreshCooldown : 0f;

        /// <summary>当前照明工具的暴击点生成半径。</summary>
        public float Radius => lightTool != null ? lightTool.CritPointRadius : 0f;

        /// <summary>当前照明工具的暴击伤害倍率。</summary>
        public float CriticalDamageMultiplier => lightTool != null ? lightTool.CritPointDamageMultiplier : 1f;

        /// <summary>距离下一个暴击点刷新的剩余秒数。</summary>
        public float CooldownRemaining => cooldownRemaining;

        /// <summary>当前是否具备刷点条件。</summary>
        public bool CanSpawn => lightTool != null && lightTool.CanSpawnCritPoints && ore != null && ore.IsAlive;

        /// <summary>旧版生成概率（百分比），仅保留给旧脚本读取。</summary>
        public float SpawnChancePercent => spawnChancePercent;

        private void Awake()
        {
            // 兼容旧场景：之前生成的 0.24 / 0.42 暴击点自动缩到新视觉尺寸。
            if (pointDiameter > 0.2f)
            {
                pointDiameter = 0.16f;
            }

            ResolveDependencies();
            BuildMaterial();
            cooldownRemaining = RefreshCooldown;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OreHit>(HandleOreHit);
            EventBus.Subscribe<AreaChanged>(HandleAreaChanged);

            if (lightTool != null)
            {
                lightTool.OnToolChanged += HandleLightToolChanged;
            }

            cooldownRemaining = RefreshCooldown;
        }

        private void OnDisable()
        {
            if (lightTool != null)
            {
                lightTool.OnToolChanged -= HandleLightToolChanged;
            }
            UnbindOre();
            EventBus.Unsubscribe<OreHit>(HandleOreHit);
            EventBus.Unsubscribe<AreaChanged>(HandleAreaChanged);
        }

        private void OnDestroy()
        {
            UnbindOre();
            DestroyRuntimeMaterial(pointMaterial);
            DestroyRuntimeMaterial(sparkMaterial);
            DestroyRuntimeMaterial(barBackgroundMaterial);
            DestroyRuntimeMaterial(barFillMaterial);
        }

        private static void DestroyRuntimeMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }
        }
        private void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ResolveDependencies();

            if (!isInMiningArea)
            {
                return;
            }

            // 新聚合体刚出现时，先等它的「放大→缩小」生成表现播完再刷暴击点。
            // 否则点会在聚合体还在展开时就扣存在时间，玩家看到时进度条已经少了一截。
            if (refillDelay > 0f)
            {
                refillDelay -= Time.deltaTime;
                return;
            }

            if (!CanSpawn)
            {
                // 矿石刷新期间保留补点队列，矿石重新出现后立即继续补满。
                if (burstRemaining <= 0)
                {
                    cooldownRemaining = RefreshCooldown;
                }

                return;
            }

            PruneConsumed();
            TrackShellChanges();

            if (activePoints.Count >= Capacity)
            {
                // 满员：冷却停在 0，等玩家点掉一个后立刻补位
                burstRemaining = 0;
                cooldownRemaining = 0f;
                return;
            }

            if (burstRemaining > 0)
            {
                // 进入矿洞 / 换照明工具 / 新矿石出现时，先快速补满上限。
                cooldownRemaining -= Time.deltaTime;

                if (cooldownRemaining > 0f)
                {
                    return;
                }

                if (SpawnAt(ore) != null)
                {
                    burstRemaining--;
                }
                else
                {
                    burstRemaining = 0;
                }

                cooldownRemaining = burstRemaining > 0 ? BurstSpawnInterval : RefreshCooldown;
                return;
            }

            cooldownRemaining -= Time.deltaTime;

            if (cooldownRemaining > 0f)
            {
                return;
            }

            SpawnAt(ore);
            cooldownRemaining = RefreshCooldown;
        }

        /// <summary>
        /// 玩家完成一次普通攻击后调用。
        /// 飞书规则下普通攻击不再生成暴击点，只保留给旧版开关使用。
        /// </summary>
        public void NotifyPlayerAttack(Ore target)
        {
            if (!legacyAttackChanceSpawn || target == null || !target.IsAlive)
            {
                return;
            }

            PruneConsumed();

            if (activePoints.Count >= Capacity)
            {
                return;
            }

            if (Random.value * 100f >= spawnChancePercent)
            {
                return;
            }

            SpawnAt(target);
        }

        /// <summary>
        /// 点中暴击点后调用：播放暴击表现。
        /// 倒计时模式（默认）：暴击点原地保留到存在时间结束，期间可以继续点；
        /// 永久模式（Inspector 勾选「暴击点永久保留」）：沿用旧规则，点中后换到新位置。
        /// </summary>
        public void NotifyCritPointHit(CritPoint point)
        {
            if (point == null || point.IsConsumed)
            {
                return;
            }

            PlayCritFeedback(point.transform.position);

            if (!point.IsPersistent)
            {
                return;
            }

            Ore owner = point.Owner;

            if (owner != null && owner.IsAlive && CanSpawn)
            {
                Vector3 newPosition = ResolveSpawnPosition(owner, point,
                    out Transform anchor, out Vector3 localPoint);
                point.Relocate(newPosition, anchor, localPoint);
                return;
            }

            activePoints.Remove(point);
            point.Consume();
        }

        /// <summary>清空所有暴击点（矿石死亡 / 离开矿区 / 读档）。</summary>
        public void ClearAll()
        {
            for (int i = 0; i < activePoints.Count; i++)
            {
                if (activePoints[i] != null)
                {
                    activePoints[i].Consume();
                }
            }

            activePoints.Clear();
            burstRemaining = Capacity;
            cooldownRemaining = 0f;
            lastShellPieceCount = -1;
            refillDelay = 0f;
        }

        /// <summary>生成一个暴击点。公开出来方便批处理验证不依赖冷却计时。</summary>
        public CritPoint SpawnAt(Ore target)
        {
            if (target == null)
            {
                return null;
            }

            PruneConsumed();

            if (activePoints.Count >= Capacity)
            {
                return null;
            }

            Vector3 position = ResolveSpawnPosition(target, null,
                out Transform anchor, out Vector3 localPoint);
            GameObject pointObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pointObject.name = "CritPoint";
            pointObject.layer = target.gameObject.layer;
            pointObject.transform.position = position;
            pointObject.transform.localScale = Vector3.one * pointDiameter;

            Renderer renderer = pointObject.GetComponent<Renderer>();

            if (renderer != null)
            {
                renderer.sharedMaterial = pointMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            Collider collider = pointObject.GetComponent<Collider>();

            if (collider != null)
            {
                // MiningInput 的射线用 QueryTriggerInteraction.Ignore，这里必须是实体碰撞体
                collider.isTrigger = false;
            }

            CritPoint point = pointObject.AddComponent<CritPoint>();
            point.Initialize(target, Lifetime,
                viewCamera != null ? viewCamera : Camera.main,
                barBackgroundMaterial, barFillMaterial);
            point.Attach(anchor, localPoint);
            activePoints.Add(point);

            if (logSpawnFlow)
            {
                Debug.Log($"[CritPointSystem] 生成暴击点（{activePoints.Count}/{Capacity}），" +
                          $"冷却 {RefreshCooldown:0.#} 秒，半径 {Radius:0.#}，位置 {position}。", this);
            }

            return point;
        }
        private static readonly RaycastHit[] SurfaceHits = new RaycastHit[16];

        /// <summary>
        /// 世界点是否落在玩家当前可视范围内（视口内，并按 spawnScreenMargin 留出四周边距）。
        /// 相机为空时视为可见，交给调用方兜底。
        /// </summary>
        private bool IsInsideVisibleViewport(Camera camera, Vector3 worldPoint)
        {
            if (camera == null)
            {
                return true;
            }

            Vector3 viewport = camera.WorldToViewportPoint(worldPoint);

            // z <= 0：点在相机背后
            if (viewport.z <= 0.01f)
            {
                return false;
            }

            float margin = Mathf.Clamp(spawnScreenMargin, 0f, 0.45f);

            return viewport.x >= margin && viewport.x <= 1f - margin
                && viewport.y >= margin && viewport.y <= 1f - margin;
        }

        /// <summary>
        /// 在矿石朝向相机的可见表面上取点，让暴击点看起来像砖块上的发光弱点。
        /// 飞书只写了「生成在矿石平面上、与矿石重合」，实际做成贴在矿石表面，
        /// 玩家点暴击点时鼠标同时压在矿石上，符合 2.1.4 的检测规则。
        /// 配置半径继续作为离矿石中心的最大距离。
        /// </summary>
        private Vector3 ResolveSpawnPosition(Ore target, CritPoint exclude,
            out Transform anchor, out Vector3 localPoint)
        {
            anchor = null;
            localPoint = default;

            Bounds bounds;

            if (!TryGetOreBounds(target, out bounds))
            {
                bounds = new Bounds(target.transform.position, Vector3.one);
            }

            float radius = Radius;

            if (radius <= 0.01f)
            {
                radius = Mathf.Max(0.5f, bounds.extents.magnitude + spawnMargin);
            }

            Camera camera = viewCamera != null ? viewCamera : Camera.main;

            // 垃圾聚合体的外壳块没有碰撞体，射线只会打到点击用的包围盒上，
            // 看起来就是「暴击点悬在空气里」。这里优先直接在外壳块上取表面点。
            if (camera != null)
            {
                GarbageAggregateVisual visual = GetGarbageVisual(target);

                if (visual != null)
                {
                    // 候选已按「离摄像机由近到远」排序：第一个就是最靠近玩家的外层垃圾，
                    // 优先落在这一层，避免点到被前面垃圾挡住的里层。
                    visual.GetCritPointSurfaces(camera, surfaceCandidates);

                    Vector3 firstSurface = default;
                    Transform firstAnchor = null;
                    Vector3 firstLocalPoint = default;
                    bool hasSurface = false;

                    for (int i = 0; i < surfaceCandidates.Count; i++)
                    {
                        GarbageAggregateVisual.CritSurface surface = surfaceCandidates[i];

                        Vector3 worldOffset = surface.WorldNormal * (pointDiameter * 0.35f);
                        Vector3 candidate = surface.WorldPosition + worldOffset;
                        Vector3 anchoredLocalPoint = surface.LocalPoint
                            + surface.Anchor.InverseTransformVector(worldOffset);

                        if ((candidate - bounds.center).sqrMagnitude > radius * radius)
                        {
                            continue;
                        }

                        if (!IsInsideVisibleViewport(camera, candidate))
                        {
                            continue;
                        }

                        // 记下最靠近玩家的那个可视落点，作为「都离别的点太近」时的兜底
                        if (!hasSurface)
                        {
                            firstSurface = candidate;
                            firstAnchor = surface.Anchor;
                            firstLocalPoint = anchoredLocalPoint;
                            hasSurface = true;
                        }

                        if (IsClearOfOtherPoints(candidate, exclude))
                        {
                            anchor = surface.Anchor;
                            localPoint = anchoredLocalPoint;
                            return candidate;
                        }
                    }

                    // 外壳上已经找到最外层可视落点，只是离别的暴击点太近时也返回它，
                    // 不要退回包围盒表面（那正是点浮在空气里的原因）。
                    if (hasSurface)
                    {
                        anchor = firstAnchor;
                        localPoint = firstLocalPoint;
                        return firstSurface;
                    }
                }
            }

            if (camera != null && TryGetScreenRect(
                    bounds, camera, spawnScreenMargin, out Rect screenRect))
            {
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    Vector2 screenPoint = new Vector2(
                        Random.Range(screenRect.xMin, screenRect.xMax),
                        Random.Range(screenRect.yMin, screenRect.yMax));

                    if (!TryGetSurfacePoint(target, camera, screenPoint, out Vector3 surface, out Vector3 normal))
                    {
                        continue;
                    }

                    Vector3 candidate = surface + normal * (pointDiameter * 0.35f);

                    if ((candidate - bounds.center).sqrMagnitude > radius * radius)
                    {
                        continue;
                    }

                    // 表面点沿法线偏移后可能又贴到可视边缘，再确认一次
                    if (!IsInsideVisibleViewport(camera, candidate))
                    {
                        continue;
                    }

                    if (IsClearOfOtherPoints(candidate, exclude))
                    {
                        return candidate;
                    }
                }
            }

            // 兜底：相机 / 碰撞体异常时仍按旧规则在中心平面取点，保证玩法不断。
            // 有相机时只接受落在可视范围内的候选点；一个都没有就退回矿石中心。
            Vector3 best = bounds.center;

            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector2 offset = Random.insideUnitCircle * radius;
                Vector3 candidate = bounds.center + new Vector3(offset.x, 0f, offset.y);

                if (!IsInsideVisibleViewport(camera, candidate))
                {
                    continue;
                }

                best = candidate;

                if (IsClearOfOtherPoints(candidate, exclude))
                {
                    break;
                }
            }

            return best;
        }

        /// <summary>找到矿石下面的垃圾外壳，用于把暴击点贴到真实垃圾块上。</summary>
        private GarbageAggregateVisual GetGarbageVisual(Ore target)
        {
            if (target == null)
            {
                return null;
            }

            if (garbageVisualOwner != target)
            {
                garbageVisualOwner = target;
                garbageVisual = target.GetComponentInChildren<GarbageAggregateVisual>(true);
            }

            return garbageVisual;
        }

        /// <summary>
        /// 外壳掉块后，原来贴在那些块上的暴击点会悬空；
        /// 外壳块数变化时把现有暴击点重新贴到剩下的垃圾上。
        /// </summary>
        private void TrackShellChanges()
        {
            if (ore == null)
            {
                return;
            }

            GarbageAggregateVisual visual = GetGarbageVisual(ore);
            int count = visual != null ? visual.RemainingPieceCount : -1;

            if (count >= 0 && lastShellPieceCount >= 0 && count != lastShellPieceCount
                && activePoints.Count > 0)
            {
                RelocateActivePoints(visual);
            }

            lastShellPieceCount = count;
        }

        private void RelocateActivePoints(GarbageAggregateVisual visual)
        {
            if (ore == null || !ore.IsAlive)
            {
                return;
            }

            if (visual != null && visual.RemainingPieceCount <= 0)
            {
                ClearAll();
                return;
            }

            for (int i = 0; i < activePoints.Count; i++)
            {
                CritPoint point = activePoints[i];

                if (point == null || point.IsConsumed)
                {
                    continue;
                }

                Vector3 newPosition = ResolveSpawnPosition(ore, point,
                    out Transform anchor, out Vector3 localPoint);
                point.Relocate(newPosition, anchor, localPoint);
            }
        }

        private static bool TryGetOreBounds(Ore target, out Bounds bounds)
        {
            bounds = new Bounds(target.transform.position, Vector3.one);
            bool hasBounds = false;

            Collider[] colliders = target.GetComponentsInChildren<Collider>(true);

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];

                if (collider == null || !collider.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            if (hasBounds)
            {
                return true;
            }

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null || !renderers[i].enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderers[i].bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            return hasBounds;
        }

        /// <summary>
        /// 把矿石的包围盒投影到屏幕上，向内收 15% 避开轮廓边缘，
        /// 再按 viewportMargin 裁到玩家可视范围内，保证采样点不会落到屏幕外。
        /// </summary>
        private static bool TryGetScreenRect(
            Bounds bounds, Camera camera, float viewportMargin, out Rect rect)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = center + new Vector3(
                    ((i & 1) == 0 ? -1f : 1f) * extents.x,
                    ((i & 2) == 0 ? -1f : 1f) * extents.y,
                    ((i & 4) == 0 ? -1f : 1f) * extents.z);

                Vector3 screen = camera.WorldToScreenPoint(corner);

                if (screen.z <= 0.01f)
                {
                    rect = default;
                    return false;
                }

                min = Vector2.Min(min, screen);
                max = Vector2.Max(max, screen);
            }

            float insetX = (max.x - min.x) * 0.15f;
            float insetY = (max.y - min.y) * 0.15f;
            float width = max.x - min.x - insetX * 2f;
            float height = max.y - min.y - insetY * 2f;

            if (width <= 1f || height <= 1f)
            {
                rect = default;
                return false;
            }

            float margin = Mathf.Clamp(viewportMargin, 0f, 0.45f);
            float marginX = Screen.width * margin;
            float marginY = Screen.height * margin;
            float xMin = Mathf.Max(min.x + insetX, marginX);
            float yMin = Mathf.Max(min.y + insetY, marginY);
            float xMax = Mathf.Min(min.x + insetX + width, Screen.width - marginX);
            float yMax = Mathf.Min(min.y + insetY + height, Screen.height - marginY);

            if (xMax - xMin <= 1f || yMax - yMin <= 1f)
            {
                rect = default;
                return false;
            }

            rect = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
            return true;
        }

        private static bool TryGetSurfacePoint(Ore target, Camera camera, Vector2 screenPoint,
            out Vector3 point, out Vector3 normal)
        {
            Ray ray = camera.ScreenPointToRay(screenPoint);
            int count = Physics.RaycastNonAlloc(ray, SurfaceHits, 200f, ~0,
                QueryTriggerInteraction.Ignore);

            point = default;
            normal = Vector3.up;

            float nearestDistance = float.MaxValue;
            bool found = false;

            // RaycastNonAlloc 的顺序不保证由近到远，这里手动取最近的一层，
            // 保证暴击点贴在玩家最先看到的表面上。
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = SurfaceHits[i];

                if (hit.collider == null || hit.collider.GetComponentInParent<CritPoint>() != null)
                {
                    continue;
                }

                Ore hitOre = hit.collider.GetComponentInParent<Ore>();

                if (hitOre != target || hit.distance >= nearestDistance)
                {
                    continue;
                }

                nearestDistance = hit.distance;
                point = hit.point;
                normal = hit.normal;
                found = true;
            }

            return found;
        }

        private bool IsClearOfOtherPoints(Vector3 position, CritPoint exclude)
        {
            float minDistance = Mathf.Max(0.12f, pointDiameter * 1.6f);

            for (int i = 0; i < activePoints.Count; i++)
            {
                CritPoint other = activePoints[i];

                if (other == null || other == exclude || other.IsConsumed)
                {
                    continue;
                }

                if ((other.transform.position - position).sqrMagnitude < minDistance * minDistance)
                {
                    return false;
                }
            }

            return true;
        }

        private void PruneConsumed()
        {
            for (int i = activePoints.Count - 1; i >= 0; i--)
            {
                if (activePoints[i] == null || activePoints[i].IsConsumed)
                {
                    activePoints.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 换装 / 升级照明工具后，新上限和冷却立刻生效：
        /// 超出上限的暴击点马上收掉，冷却按新工具重新计时。
        /// </summary>
        private void HandleLightToolChanged(MiningLightTool _)
        {
            // 换装 / 升级后补到新上限，之后再按正常冷却刷新。
            PruneConsumed();

            while (activePoints.Count > Capacity)
            {
                int last = activePoints.Count - 1;
                CritPoint point = activePoints[last];
                activePoints.RemoveAt(last);

                if (point != null)
                {
                    point.Consume();
                }
            }

            burstRemaining = Mathf.Max(0, Capacity - activePoints.Count);
            cooldownRemaining = 0f;
        }

        private void HandleOreHit(OreHit evt)
        {
            if (evt.Destroyed)
            {
                ClearAll();
            }
        }

        /// <summary>
        /// 新聚合体 / BOSS 被配置出来时：清掉旧点，等生成表现播完再按满上限立即补点。
        /// 不再等照明工具的刷新冷却，避免聚合体出现后还要空等几秒才有暴击点。
        /// </summary>
        private void HandleOreDefinitionApplied(Ore appliedOre)
        {
            if (appliedOre == null || appliedOre != ore)
            {
                return;
            }

            ClearAll();
            refillDelay = Mathf.Max(0f, appliedOre.SpawnVisualDuration);

            if (logSpawnFlow)
            {
                Debug.Log($"[CritPointSystem] 新聚合体已生成，暴击点将在 {refillDelay:0.##} 秒生成表现后立即刷新。", this);
            }
        }

        private void BindOre(Ore target)
        {
            if (subscribedOre == target)
            {
                return;
            }

            if (subscribedOre != null)
            {
                subscribedOre.OnDefinitionApplied -= HandleOreDefinitionApplied;
            }

            subscribedOre = target;

            if (subscribedOre != null)
            {
                subscribedOre.OnDefinitionApplied += HandleOreDefinitionApplied;
            }
        }

        private void UnbindOre()
        {
            if (subscribedOre != null)
            {
                subscribedOre.OnDefinitionApplied -= HandleOreDefinitionApplied;
            }

            subscribedOre = null;
        }

        private void HandleAreaChanged(AreaChanged evt)
        {
            if (evt.Area != GameArea.Mining)
            {
                isInMiningArea = false;
                ClearAll();
                return;
            }

            isInMiningArea = true;
            // 进入矿洞先补满暴击点，避免玩家以为照明工具没有效果。
            burstRemaining = Mathf.Max(0, Capacity - activePoints.Count);
            cooldownRemaining = 0f;
        }
        private void ResolveDependencies()
        {
            if (ore == null)
            {
                ore = FindFirstObjectByType<Ore>(FindObjectsInactive.Include);
            }

            if (lightTool == null)
            {
                lightTool = FindFirstObjectByType<MiningLightTool>(FindObjectsInactive.Include);
            }

            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }

            if (playerStats == null)
            {
                playerStats = FindFirstObjectByType<PlayerStats>(FindObjectsInactive.Include);
            }

            // 订阅当前矿石的「重新配置」事件：新聚合体一出现就能立刻补暴击点
            BindOre(ore);

            if (ore == null && !warnedMissingOre)
            {
                warnedMissingOre = true;
                Debug.LogWarning("[CritPointSystem] 场景里找不到 Ore，暴击点无法定位。" +
                                 "请运行菜单「屿见/挖矿/配置暴击点系统」。", this);
            }
        }

        private const string OverlayShaderName = "Yujian/Crit Point Overlay";

        private void BuildMaterial()
        {
            Shader overlayShader = showThroughObstacles ? Shader.Find(OverlayShaderName) : null;

            if (showThroughObstacles && overlayShader == null)
            {
                Debug.LogWarning($"[CritPointSystem] 找不到 {OverlayShaderName}，暴击点无法透视显示。" +
                                 "请确认 Assets/Shaders/CritPointOverlay.shader 已导入。", this);
            }

            pointMaterial = CreateUnlitMaterial("CritPoint_Runtime", pointColor, overlayShader);
            barBackgroundMaterial = CreateUnlitMaterial(
                "CritPointBar_Runtime", new Color(0.06f, 0.06f, 0.06f, 1f), overlayShader);
            barFillMaterial = CreateUnlitMaterial("CritPointBarFill_Runtime", pointColor, overlayShader);

            if (logSpawnFlow)
            {
                string shaderName = pointMaterial != null && pointMaterial.shader != null
                    ? pointMaterial.shader.name
                    : "无";
                int queue = pointMaterial != null ? pointMaterial.renderQueue : 0;

                Debug.Log($"[CritPointSystem] 暴击点材质：{shaderName}，" +
                          $"透视 {(overlayShader != null ? "开" : "关")}，渲染队列 {queue}。", this);
            }
        }

        /// <summary>
        /// 暴击点与剩余时间进度条共用同一套材质创建逻辑。
        /// preferred 为透视着色器（ZTest Always）；为空时退回普通 URP Unlit。
        /// </summary>
        private Material CreateUnlitMaterial(string name, Color color, Shader preferred = null)
        {
            Shader shader = preferred;

            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                Debug.LogWarning($"[CritPointSystem] 找不到可用着色器，{name} 会使用默认材质。", this);
                return null;
            }

            Material material = new Material(shader) { name = name };

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (preferred != null)
            {
                // 关键：必须明确写到 Overlay 队列，保证在不透明垃圾之后绘制。
                // 只靠 ZTest Always 还不够——如果队列落在不透明段，先画的点会被后画的垃圾盖掉。
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;
            }

            return material;
        }

        /// <summary>暴击表现：焊铁式火花 + 可选音效，持续约 0.5 秒。</summary>
        private void PlayCritFeedback(Vector3 position)
        {
            SpawnSparkBurst(position);

            AudioClip clip = ResolveCritSound();

            if (clip != null)
            {
                AudioSource.PlayClipAtPoint(clip, position, critSoundVolume);
            }
        }

        /// <summary>
        /// 优先用 Inspector 里指定的音效；没指定时尝试 Resources/sound_Critichit，
        /// 美术把文件放到 Assets/Resources 下即可自动生效，不用再改场景。
        /// </summary>
        private AudioClip ResolveCritSound()
        {
            if (critSound != null)
            {
                return critSound;
            }

            if (!searchedCritSound)
            {
                searchedCritSound = true;
                resolvedCritSound = Resources.Load<AudioClip>("sound_Critichit");

                if (resolvedCritSound == null)
                {
                    resolvedCritSound = CreateRuntimeCritSound();
                }
            }

            return resolvedCritSound;
        }

        /// <summary>
        /// 没有音频素材时的兜底音效：短促、清脆的金属「叮」。
        /// 只在第一次暴击时生成并缓存，不需要 Resources 或第三方插件。
        /// </summary>
        private static AudioClip CreateRuntimeCritSound()
        {
            const int sampleRate = 44100;
            const float duration = 0.2f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float attack = Mathf.Clamp01(t / 0.004f);
                float decay = Mathf.Exp(-t * 22f);
                float ring = Mathf.Sin(2f * Mathf.PI * 1760f * t) * 0.42f
                    + Mathf.Sin(2f * Mathf.PI * 2637.02f * t) * 0.26f
                    + Mathf.Sin(2f * Mathf.PI * 3520f * t) * 0.12f;
                float click = Mathf.Sin(2f * Mathf.PI * 5200f * t) * Mathf.Exp(-t * 90f) * 0.18f;
                samples[i] = (ring * decay + click) * attack;
            }

            AudioClip clip = AudioClip.Create("CritHit_Runtime", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void SpawnSparkBurst(Vector3 position)
        {
            if (TrySpawnSparkPrefab(position))
            {
                return;
            }

            GameObject sparkObject = new GameObject("CritSpark");
            sparkObject.transform.position = position;

            Camera sparkCamera = viewCamera != null ? viewCamera : Camera.main;

            if (sparkCamera != null)
            {
                Vector3 toCamera = sparkCamera.transform.position - position;

                if (toCamera.sqrMagnitude > 0.0001f)
                {
                    sparkObject.transform.rotation = Quaternion.LookRotation(toCamera.normalized);
                }
            }

            ParticleSystem particles = sparkObject.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particles.main;
            main.duration = 0.45f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.52f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.2f, 8.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.060f);
            main.startColor = sparkColor;
            main.gravityModifier = 1.4f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 72;
            main.stopAction = ParticleSystemStopAction.Destroy;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)24) });

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 40f;
            shape.radius = 0.02f;

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(sparkColor, 0.18f),
                    new GradientColorKey(new Color(1f, 0.32f, 0.08f), 0.65f),
                    new GradientColorKey(new Color(0.25f, 0.08f, 0.01f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.15f)));

            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            Material material = GetSparkMaterial();

            if (renderer != null)
            {
                if (material != null)
                {
                    renderer.sharedMaterial = material;
                }

                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.lengthScale = 1.4f;
                renderer.velocityScale = 0.08f;
            }

            CreateSparkFlash(position);
            particles.Play();
            Destroy(sparkObject, 1.4f);
        }

        private bool TrySpawnSparkPrefab(Vector3 position)
        {
            GameObject prefab = ResolveSparkPrefab();

            if (prefab == null)
            {
                return false;
            }

            GameObject instance = Instantiate(prefab, position, Quaternion.identity);
            Destroy(instance, 2f);
            return true;
        }

        private GameObject ResolveSparkPrefab()
        {
            if (sparkPrefab != null)
            {
                return sparkPrefab;
            }

            if (!searchedSparkPrefab)
            {
                searchedSparkPrefab = true;
                resolvedSparkPrefab = Resources.Load<GameObject>("particle_Critichit");
            }

            return resolvedSparkPrefab;
        }

        private void CreateSparkFlash(Vector3 position)
        {
            Camera sparkCamera = viewCamera != null ? viewCamera : Camera.main;
            GameObject flashObject = new GameObject("CritSparkFlash");
            flashObject.transform.position = position + (sparkCamera != null
                ? (sparkCamera.transform.position - position).normalized * 0.05f
                : Vector3.zero);

            Light flash = flashObject.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = new Color(1f, 0.72f, 0.3f, 1f);
            flash.intensity = 2.2f;
            flash.range = 1.8f;
            flash.shadows = LightShadows.None;

            Destroy(flashObject, 0.11f);
        }

        private Material GetSparkMaterial()
        {
            if (sparkMaterial != null)
            {
                return sparkMaterial;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

            if (shader == null)
            {
                shader = Shader.Find("Particles/Standard Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                return null;
            }

            sparkMaterial = new Material(shader) { name = "CritSpark_Runtime" };
            return sparkMaterial;
        }

        /// <summary>编辑器向导调用，把暴击点大小刷成当前美术设定。</summary>
        public void ConfigureVisualSize(float diameter)
        {
            pointDiameter = Mathf.Max(0.05f, diameter);
        }

        /// <summary>编辑器向导调用，接好矿石与照明工具。</summary>
        public void ConfigureForSetup(Ore targetOre, MiningLightTool tool)
        {
            ore = targetOre;
            lightTool = tool;
            cooldownRemaining = RefreshCooldown;
        }
    }
}