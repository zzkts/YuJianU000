using System;
using System.Collections.Generic;
using UnityEngine;
using Yujian.Core;

namespace Yujian.Building
{
    /// <summary>迁移状态。新增值一律追加到末尾。</summary>
    public enum BuildingMigrationState
    {
        /// <summary>不在迁移中。</summary>
        Idle = 0,

        /// <summary>正拖着建筑跟指针走。</summary>
        Dragging = 1,

        /// <summary>已松开指针，建筑停在候选位置，等玩家点确认 / 撤回。</summary>
        PendingConfirm = 2,
    }

    /// <summary>
    /// 建筑迁移系统（策划案 3.3，配合 3.2 建筑面积系统的绿 / 红反馈）。
    ///
    /// 流程：
    ///   长按已建建筑 → 建筑跟手（保持抓取偏移），视觉上**平滑追向地图网格的吸附点**，
    ///   不会在格线附近硬跳；每帧用「建筑中心吸附后的占格」去查面积系统：
    ///   能放 → 绿色 shader + 闪，不能放 → 红色 shader + 闪；
    ///   松开 → 建筑收尾到吸附点，上方出现「确认 / 撤回」；
    ///   确认 → 这时才把建筑固定（占格提交）；撤回 → 回原位并退出迁移。
    ///
    /// 位移与占格分两步：拖动中只动 transform（平滑插值 + 地面高亮），
    /// 占格登记仍然只在确认时做——所以「拖到一半」永远不会有半截占用记录。
    ///
    /// 职责：迁移状态机、面积 / 占格合法性、绿红 shader。
    /// 不做：长按判定（<see cref="BuildingPointerInteraction"/>）、按钮与投影（<see cref="Yujian.UI.BuildingMigrationUI"/>）、
    /// 占格登记（BuildingManager）。
    /// </summary>
    public class BuildingMigration : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("建筑管理器。提交迁移时由它释放旧占格、登记新占格")]
        [SerializeField] private BuildingManager buildingManager;

        [Tooltip("建筑网格。吸附与合法性判定都来自它")]
        [SerializeField] private BuildingGrid grid;

        [Tooltip("网格可视化。拖动时显示候选占格。留空仍可迁移，只是没有地面高亮")]
        [SerializeField] private BuildingGridVisualizer gridVisualizer;

        [Tooltip("用于把屏幕坐标换算成地面点的相机。留空则用 MainCamera")]
        [SerializeField] private Camera viewCamera;

        [Tooltip("区域状态。切出建筑区时自动撤回本次迁移")]
        [SerializeField] private GameAreaController areas;

        [Header("地面")]
        [Tooltip("地面高度。与 BuildingPlacement 的 Ground Height 保持一致")]
        [SerializeField] private float groundHeight = 0f;

        [Header("吸附过渡")]
        [Tooltip("建筑朝吸附点移动的速度（世界单位 / 秒）。拖动时不会硬跳到格点，而是平滑追上去；" +
                 "0 = 直接吸附。调大更跟手、调小更柔和")]
        [SerializeField, Min(0f)] private float snapMoveSpeed = 24f;

        [Header("绿 / 红外表（Assets/Shaders/BuildingPreviewTint.shader）")]
        [Tooltip("迁移预览材质模板。由菜单「屿见/建筑/配置建筑迁移与升级」自动生成并挂上。" +
                 "留空时迁移仍可用，但不会有绿 / 红反馈")]
        [SerializeField] private Material ghostMaterialTemplate;

        [Tooltip("可以放下时的颜色（Alpha 会被闪烁曲线覆盖）")]
        [SerializeField] private Color validTint = new Color(0.24f, 0.85f, 0.35f, 0.5f);

        [Tooltip("放不下时的颜色（Alpha 会被闪烁曲线覆盖）")]
        [SerializeField] private Color invalidTint = new Color(0.85f, 0.25f, 0.22f, 0.5f);

        [Header("闪烁（策划案：数值留给策划调）")]
        [Tooltip("闪烁时最不透明的值")]
        [SerializeField, Range(0f, 1f)] private float ghostMinAlpha = 0.35f;

        [Tooltip("闪烁时最透明的值。必须大于等于 Ghost Min Alpha")]
        [SerializeField, Range(0f, 1f)] private float ghostMaxAlpha = 0.75f;

        [Tooltip("闪烁速度（次 / 秒）。0 表示不闪，只保持半透明")]
        [SerializeField, Min(0f)] private float ghostBlinkSpeed = 1.2f;

        [Tooltip("贴图对最终颜色的影响。0 = 纯绿 / 红块，1 = 完全按原贴图")]
        [SerializeField, Range(0f, 1f)] private float textureInfluence = 0.35f;

        [Header("调试")]
        [SerializeField] private bool logMigrationFlow = true;

        private const string GhostColorProperty = "_GhostColor";
        private const string BaseMapProperty = "_BaseMap";
        private const string BaseMapStProperty = "_BaseMap_ST";
        private const string TextureInfluenceProperty = "_TextureInfluence";

        private BuildingMigrationState state = BuildingMigrationState.Idle;
        private GameObject target;
        private PlacedBuildingInfo targetInfo;

        private RectInt originalRect;
        private Vector3 originalPosition;

        // 抓取偏移：长按那一刻「建筑中心 − 指针地面点」，拖动时保持这个偏移，建筑不会瞬移
        private Vector3 grabOffset;

        private RectInt currentRect;
        private Vector3 snappedCenter;
        private bool hasSnappedPose;
        private bool isValid;
        private string invalidReason;

        // 视觉目标：建筑每帧朝它平滑移动（吸附点；没有吸附点时是自由跟手的点）
        private Vector3 visualTarget;

        private float blinkTime;

        // 迁移期间换上的预览材质，结束时逐个换回原材质
        private readonly List<Renderer> ghostRenderers = new List<Renderer>();
        private readonly List<Material[]> originalMaterials = new List<Material[]>();
        private readonly List<Material> ghostMaterials = new List<Material>();

        private bool warnedAboutMissingCamera;
        private bool warnedAboutMissingTargetInfo;
        private bool warnedAboutMissingTemplate;

        /// <summary>迁移状态变化（开始 / 松开 / 结束）时触发，UI 据此显隐。</summary>
        public event Action OnMigrationChanged;

        /// <summary>当前状态。</summary>
        public BuildingMigrationState State => state;

        /// <summary>是否正在拖动。</summary>
        public bool IsDragging => state == BuildingMigrationState.Dragging;

        /// <summary>是否在等确认 / 撤回。</summary>
        public bool IsPendingConfirm => state == BuildingMigrationState.PendingConfirm;

        /// <summary>是否处于迁移中的任一状态。</summary>
        public bool IsActive => state != BuildingMigrationState.Idle;

        /// <summary>正在迁移的建筑实例，没有则为 null。</summary>
        public GameObject Target => target;

        /// <summary>当前位置能不能放下（绿色 = true，红色 = false）。</summary>
        public bool IsValid => isValid;

        /// <summary>不能放下的原因，能放下时为 null。</summary>
        public string StatusReason => invalidReason;

        /// <summary>当前候选占格（吸附后的格子矩形）。</summary>
        public RectInt CurrentRect => currentRect;

        /// <summary>当前候选中心吸附到格子后的世界坐标。</summary>
        public Vector3 SnappedCenter => snappedCenter;

        /// <summary>当前候选位置是否可提交。</summary>
        public bool CanConfirm =>
            state == BuildingMigrationState.PendingConfirm && isValid && target != null;

        /// <summary>候选中心世界坐标（UI 定位用）。拖动中就是建筑当前所在位置。</summary>
        public Vector3 CurrentCenter => target != null ? target.transform.position : default;

        private void Awake()
        {
            if (buildingManager == null)
            {
                Debug.LogError("[BuildingMigration] BuildingManager 未配置，迁移无法提交。", this);
            }

            if (grid == null)
            {
                Debug.LogError("[BuildingMigration] BuildingGrid 未配置，迁移一律会被拒绝。" +
                               "请重新运行菜单「屿见/建筑/配置建筑迁移与升级」。", this);
            }

            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }
        }

        private void OnDisable()
        {
            // 组件被停掉（退场景、退出 Play）时不能留下预览材质的建筑
            if (IsActive)
            {
                Revert();
            }
        }

        private void Update()
        {
            if (IsActive && areas != null && areas.CurrentArea != GameArea.Building)
            {
                Revert();
                return;
            }

            if (!IsActive)
            {
                return;
            }

            blinkTime += Time.deltaTime;
            UpdateGhostColor();
            TickVisual(Time.deltaTime);
        }

        // ---------------- 开始 / 拖动 / 松开 ----------------

        /// <summary>开始迁移一座建筑（调用方不知道指针位置时用；抓取偏移为 0）。</summary>
        public bool Begin(GameObject building)
        {
            return BeginInternal(building, false, default);
        }

        /// <summary>
        /// 开始迁移一座建筑。<paramref name="screenPosition"/> 用来记下抓取偏移：
        /// 长按的位置在建筑上的哪一点，拖动时这一点就贴着指针，建筑不会在长按瞬间跳到指针下。
        /// </summary>
        public bool Begin(GameObject building, Vector2 screenPosition)
        {
            return BeginInternal(building, true, screenPosition);
        }

        private bool BeginInternal(GameObject building, bool hasScreenPosition, Vector2 screenPosition)
        {
            if (building == null)
            {
                return false;
            }

            if (IsActive)
            {
                // 同一座建筑重复开始不重入；换建筑时先撤回上一次
                if (building == target)
                {
                    return true;
                }

                Revert();
            }

            if (buildingManager == null || grid == null)
            {
                Debug.LogError("[BuildingMigration] 依赖未配置，无法开始迁移。", this);
                return false;
            }

            if (!IsPlaced(building))
            {
                Debug.LogWarning($"[BuildingMigration]「{building.name}」不在已放置建筑列表里，迁移被忽略。", this);
                return false;
            }

            PlacedBuildingInfo info = building.GetComponent<PlacedBuildingInfo>();

            if (info == null || info.Data == null)
            {
                if (!warnedAboutMissingTargetInfo)
                {
                    warnedAboutMissingTargetInfo = true;
                    Debug.LogError($"[BuildingMigration]「{building.name}」没有 PlacedBuildingInfo 或建筑数据为空，" +
                                   "无法迁移。", this);
                }

                return false;
            }

            target = building;
            targetInfo = info;
            originalRect = info.FootprintRect;
            originalPosition = building.transform.position;
            currentRect = originalRect;
            snappedCenter = originalPosition;
            grabOffset = Vector3.zero;

            if (hasScreenPosition && TryGetGroundPoint(screenPosition, out Vector3 grabPoint))
            {
                grabOffset = originalPosition - grabPoint;
                grabOffset.y = 0f;
            }

            blinkTime = 0f;
            visualTarget = originalPosition;
            state = BuildingMigrationState.Dragging;

            BeginGhost();
            EvaluatePose(originalPosition);
            visualTarget = hasSnappedPose ? snappedCenter : originalPosition;

            if (logMigrationFlow)
            {
                Debug.Log($"[BuildingMigration] 开始迁移「{info.Data.BuildingName}」（原占格 {originalRect}），" +
                          "拖动到目标位置后松开指针。");
            }

            OnMigrationChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 拖动中：按指针下的地面点更新目标位置。视觉上平滑追向吸附点（<see cref="TickVisual"/>），
        /// 合法性按「建筑中心吸附后的占格」实时判定，绿 / 红反馈由 <see cref="IsValid"/> 表达。
        /// </summary>
        public void DragToScreen(Vector2 screenPosition)
        {
            if (state != BuildingMigrationState.Dragging || target == null)
            {
                return;
            }

            if (!TryGetGroundPoint(screenPosition, out Vector3 groundPoint))
            {
                // 指针不在可见地面上：建筑停在最后一次的位置，但明确标成不可确认
                SetValidity(false, "指针没有落在地面上");
                return;
            }

            DragToGroundPoint(groundPoint);
        }

        /// <summary>
        /// 拖动到某个地面点（世界坐标）。给 <see cref="DragToScreen"/> 与批处理验证共用，
        /// 这样验证不必依赖真实指针和相机。
        /// </summary>
        /// <returns>当前位置是否可以放下。</returns>
        public bool DragToGroundPoint(Vector3 groundPoint)
        {
            if (state != BuildingMigrationState.Dragging || target == null)
            {
                return false;
            }

            // 自由跟手：保持抓取偏移，Y 不变（地面高度已由射线给出，但建筑轴心不一定在地面）
            Vector3 desiredCenter = new Vector3(
                groundPoint.x + grabOffset.x,
                originalPosition.y,
                groundPoint.z + grabOffset.z);

            EvaluatePose(desiredCenter);

            // 视觉目标 = 吸附点（没有吸附点时退回自由位置），由 TickVisual 平滑追上去，
            // 这样既有「贴到格子上」的对齐感，又不会在格线附近硬跳
            visualTarget = hasSnappedPose ? snappedCenter : desiredCenter;
            return isValid;
        }

        /// <summary>
        /// 待确认时再次按住建筑：不重新长按，直接继续拖动。
        /// 确认被拒绝（红色）后玩家要能拖着试别的位置，而不是先撤回再长按一次。
        /// </summary>
        public bool ResumeDrag()
        {
            return ResumeDragInternal(false, default);
        }

        /// <summary>同上，同时按当前指针位置重算抓取偏移（指针下的点继续贴着指针）。</summary>
        public bool ResumeDrag(Vector2 screenPosition)
        {
            return ResumeDragInternal(true, screenPosition);
        }

        private bool ResumeDragInternal(bool hasScreenPosition, Vector2 screenPosition)
        {
            if (state != BuildingMigrationState.PendingConfirm || target == null)
            {
                return false;
            }

            if (hasScreenPosition && TryGetGroundPoint(screenPosition, out Vector3 grabPoint))
            {
                grabOffset = target.transform.position - grabPoint;
                grabOffset.y = 0f;
            }

            state = BuildingMigrationState.Dragging;
            EvaluatePose(target.transform.position);
            visualTarget = hasSnappedPose ? snappedCenter : target.transform.position;

            if (logMigrationFlow)
            {
                Debug.Log("[BuildingMigration] 继续拖动。");
            }

            OnMigrationChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 推进吸附过渡：建筑朝 <see cref="visualTarget"/> 匀速移动。
        /// 由 <see cref="Update"/> 每帧调用；批处理验证里没有 Update，可以用它手动推进。
        /// </summary>
        public void TickVisual(float deltaTime)
        {
            if (target == null)
            {
                return;
            }

            if (snapMoveSpeed <= 0f)
            {
                target.transform.position = visualTarget;
                return;
            }

            float step = snapMoveSpeed * Mathf.Max(0f, deltaTime);
            target.transform.position = Vector3.MoveTowards(
                target.transform.position, visualTarget, step);
        }

        /// <summary>松开指针：进入「等确认」状态。建筑继续收尾到吸附点，但占格还没提交。</summary>
        public void EndDrag()
        {
            if (state != BuildingMigrationState.Dragging)
            {
                return;
            }

            state = BuildingMigrationState.PendingConfirm;

            if (logMigrationFlow)
            {
                Debug.Log($"[BuildingMigration] 已松开指针，候选占格 {currentRect}，" +
                          $"{(isValid ? "可以确认" : $"不可确认（{invalidReason}）")}。");
            }

            OnMigrationChanged?.Invoke();
        }

        // ---------------- 确认 / 撤回 ----------------

        /// <summary>
        /// 确认迁移：把建筑吸附到候选格并提交占格。
        /// 位置不合法时停在待确认状态，玩家可以继续拖（需要重新长按）或撤回。
        /// </summary>
        public bool Confirm(out string message)
        {
            message = null;

            if (state != BuildingMigrationState.PendingConfirm || target == null)
            {
                message = "没有待确认的迁移";
                return false;
            }

            if (!isValid)
            {
                message = $"当前位置不可放置：{invalidReason}";
                return false;
            }

            // 唯一固定位置的地方：先吸附到格子，再登记占格
            Vector3 freePosition = target.transform.position;
            visualTarget = snappedCenter;
            target.transform.position = snappedCenter;

            if (buildingManager.TryMoveBuilding(target, snappedCenter, currentRect, out string error))
            {
                message = $"已迁移「{targetInfo.Data.BuildingName}」";
                EndMigration();
                return true;
            }

            // 理论上不会走到：合法性在拖动时已经判过。真失败了就把建筑退回松手时的自由位置，
            // 不让它停在「看着吸附了、网格里没登记」的中间态
            target.transform.position = freePosition;
            Debug.LogWarning($"[BuildingMigration] 迁移提交失败：{error}", this);
            message = $"迁移失败：{error}";
            return false;
        }

        /// <summary>撤回本次迁移：建筑回到原位并退出迁移。也在切出建筑区时自动调用。</summary>
        public void Revert()
        {
            if (!IsActive)
            {
                return;
            }

            if (target != null)
            {
                target.transform.position = originalPosition;

                if (logMigrationFlow)
                {
                    Debug.Log($"[BuildingMigration] 已撤回迁移，「{targetInfo?.Data?.BuildingName}」回到 {originalPosition}。");
                }
            }

            EndMigration();
        }

        // ---------------- 内部 ----------------

        /// <summary>
        /// 按建筑中心算出吸附后的占格，查面积系统（可建台地）与其它建筑占用，更新绿 / 红状态。
        /// 顺序按策划案 3.2「优先检测面积」：先网格范围 / 可建台地，再占用。
        /// </summary>
        private void EvaluatePose(Vector3 desiredCenter)
        {
            if (grid == null || targetInfo == null || target == null)
            {
                hasSnappedPose = false;
                SetValidity(false, "网格未配置");
                HidePreview();
                return;
            }

            // FootprintCells 是「格数」，TryGetSnappedPose 要的是「世界尺寸」。
            // 之前直接把 FootprintCells 传进去，2×2 的建筑会被当成 2×2 世界单位 → 只算出 1×1 格，
            // 确认时又被 BuildingManager 的尺寸校验拒绝，表现就是「怎么点都没法确认」。
            Vector2 worldFootprint = grid.CellsToWorldFootprint(targetInfo.Data.FootprintCells);

            if (!grid.TryGetSnappedPose(desiredCenter, worldFootprint,
                    out Vector3 candidateCenter, out RectInt candidateRect))
            {
                hasSnappedPose = false;
                SetValidity(false, "超出建筑区域");
                HidePreview();
                return;
            }

            hasSnappedPose = true;
            snappedCenter = candidateCenter;
            currentRect = candidateRect;

            if (!grid.CanPlaceIgnoringOwner(candidateRect, target.GetInstanceID(),
                    out GridPlacementFailure failure))
            {
                SetValidity(false, failure.ToChineseName());
                ShowPreview(candidateRect, false);
                return;
            }

            SetValidity(true, null);
            ShowPreview(candidateRect, true);
        }

        private void SetValidity(bool value, string reason)
        {
            bool changed = isValid != value || invalidReason != reason;

            isValid = value;
            invalidReason = value ? null : reason;

            if (changed)
            {
                UpdateGhostColor();

                if (logMigrationFlow)
                {
                    Debug.Log($"[BuildingMigration] {(value ? "可放置(绿)" : $"不可放置(红)：{reason}")}");
                }
            }
        }

        private bool TryGetGroundPoint(Vector2 screenPosition, out Vector3 point)
        {
            point = default;

            if (viewCamera == null)
            {
                if (!warnedAboutMissingCamera)
                {
                    warnedAboutMissingCamera = true;
                    Debug.LogError("[BuildingMigration] 没有相机，无法把指针换算成地面点，迁移不会移动。", this);
                }

                return false;
            }

            return BuildingPlacement.TryGetGroundPoint(viewCamera, groundHeight, screenPosition, out point);
        }

        private void EndMigration()
        {
            EndGhost();
            HidePreview();

            target = null;
            targetInfo = null;
            hasSnappedPose = false;
            isValid = false;
            invalidReason = null;
            state = BuildingMigrationState.Idle;

            OnMigrationChanged?.Invoke();
        }

        private bool IsPlaced(GameObject building)
        {
            IReadOnlyList<GameObject> placed = buildingManager.PlacedBuildings;

            for (int i = 0; i < placed.Count; i++)
            {
                if (placed[i] == building)
                {
                    return true;
                }
            }

            return false;
        }

        private void ShowPreview(RectInt rect, bool valid)
        {
            if (gridVisualizer != null)
            {
                gridVisualizer.ShowPreview(rect, valid);
            }
        }

        private void HidePreview()
        {
            if (gridVisualizer != null)
            {
                gridVisualizer.HidePreview();
            }
        }

        // ---------------- 绿 / 红外表 ----------------

        /// <summary>
        /// 把每个 Renderer 的材质换成预览材质克隆。同一个源材质只克隆一次，
        /// 贴图沿用源材质的 _BaseMap，这样绿 / 红块上还能看出建筑的表面起伏。
        /// </summary>
        private void BeginGhost()
        {
            EndGhost();

            if (target == null)
            {
                return;
            }

            if (ghostMaterialTemplate == null)
            {
                if (!warnedAboutMissingTemplate)
                {
                    warnedAboutMissingTemplate = true;
                    Debug.LogWarning("[BuildingMigration] 预览材质模板未配置，迁移时没有绿 / 红反馈。" +
                                     "请重新运行菜单「屿见/建筑/配置建筑迁移与升级」。", this);
                }

                return;
            }

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            Dictionary<Material, Material> cache = new Dictionary<Material, Material>();

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];

                // 粒子 / 拖尾 / 线段是表现件，换成平面 tint 会看出破绽
                if (renderer == null ||
                    renderer is ParticleSystemRenderer ||
                    renderer is TrailRenderer ||
                    renderer is LineRenderer)
                {
                    continue;
                }

                Material[] originals = renderer.sharedMaterials;
                Material[] copies = new Material[originals.Length];

                for (int j = 0; j < originals.Length; j++)
                {
                    Material source = originals[j];

                    if (source == null)
                    {
                        copies[j] = null;
                        continue;
                    }

                    if (!cache.TryGetValue(source, out Material ghost))
                    {
                        ghost = BuildGhostMaterial(source);
                        cache[source] = ghost;

                        if (ghost != null)
                        {
                            ghostMaterials.Add(ghost);
                        }
                    }

                    copies[j] = ghost;
                }

                ghostRenderers.Add(renderer);
                originalMaterials.Add(originals);
                renderer.sharedMaterials = copies;
            }

            UpdateGhostColor();
        }

        private Material BuildGhostMaterial(Material source)
        {
            Material ghost = new Material(ghostMaterialTemplate)
            {
                name = source.name + "（迁移预览）",
            };

            if (ghost.HasProperty(TextureInfluenceProperty))
            {
                ghost.SetFloat(TextureInfluenceProperty, textureInfluence);
            }

            Texture baseMap = null;

            if (source.HasProperty(BaseMapProperty))
            {
                baseMap = source.GetTexture(BaseMapProperty);
            }

            if (baseMap == null && source.HasProperty("_MainTex"))
            {
                baseMap = source.GetTexture("_MainTex");
            }

            if (baseMap != null && ghost.HasProperty(BaseMapProperty))
            {
                ghost.SetTexture(BaseMapProperty, baseMap);
            }

            if (source.HasProperty(BaseMapStProperty) && ghost.HasProperty(BaseMapStProperty))
            {
                ghost.SetVector(BaseMapStProperty, source.GetVector(BaseMapStProperty));
            }

            return ghost;
        }

        /// <summary>按绿 / 红状态与闪烁曲线刷新所有预览材质的颜色。</summary>
        private void UpdateGhostColor()
        {
            if (ghostMaterials.Count == 0)
            {
                return;
            }

            Color tint = isValid ? validTint : invalidTint;

            float t = ghostBlinkSpeed <= 0f
                ? 1f
                : Mathf.PingPong(blinkTime * ghostBlinkSpeed, 1f);

            tint.a = Mathf.Lerp(ghostMaxAlpha, ghostMinAlpha, t);

            for (int i = 0; i < ghostMaterials.Count; i++)
            {
                Material material = ghostMaterials[i];

                if (material != null && material.HasProperty(GhostColorProperty))
                {
                    material.SetColor(GhostColorProperty, tint);
                }
            }
        }

        private void EndGhost()
        {
            for (int i = 0; i < ghostRenderers.Count; i++)
            {
                if (ghostRenderers[i] != null)
                {
                    ghostRenderers[i].sharedMaterials = originalMaterials[i];
                }
            }

            for (int i = 0; i < ghostMaterials.Count; i++)
            {
                if (ghostMaterials[i] == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(ghostMaterials[i]);
                }
                else
                {
                    DestroyImmediate(ghostMaterials[i]);
                }
            }

            ghostRenderers.Clear();
            originalMaterials.Clear();
            ghostMaterials.Clear();
        }

        [ContextMenu("调试：撤回迁移")]
        private void DebugRevert()
        {
            Revert();
        }
    }
}
