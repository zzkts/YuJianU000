using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Yujian.Core;

namespace Yujian.Building
{
    /// <summary>
    /// 放置控制器。职责：把"鼠标屏幕坐标"变成"蓝图的世界坐标"，并判断那里能不能放。
    ///
    /// 按住左键拖动时：指针位置 → 屏幕坐标 → 射线 → 地面交点 → 移动蓝图 →
    /// Physics.CheckBox 重叠检测 → 蓝图变色。松开后蓝图停在原地，指针可以腾出来点 ×/√。
    /// 落位：蓝图右侧的 √ 调用 <see cref="TryAnchorCurrentBlueprint"/>，判定通过后调用
    /// BuildingManager.AnchorBlueprint()，自己既不生成建筑也不记账。
    ///
    /// 阶段 4 新增：蓝图落位（Filling 相位）之后本类完全让开——不再移动蓝图。
    /// 2026-10-26 按建筑系统策划案改造：不再订阅 Player/Attack，点击地面不会落位；
    /// × / √ 改由蓝图旁的 <see cref="BuildingBlueprintActionUI"/> 负责。
    ///
    /// 为什么是「按住拖动」而不是「一直跟随指针」：×/√ 挂在蓝图两侧。
    /// 如果蓝图始终贴着指针，按钮也会跟着指针跑，指针永远追不上按钮。
    /// 松开后蓝图停住，按钮位置固定，才点得到。
    ///
    /// 本类单向依赖 BuildingManager，BuildingManager 不知道本类存在。
    /// </summary>
    public class BuildingPlacement : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("建筑系统入口，用于查询当前是否有蓝图、建筑图层、以及提交放置")]
        [SerializeField] private BuildingManager buildingManager;

        [Tooltip("用于屏幕坐标转射线的相机。留空则用 MainCamera")]
        [SerializeField] private Camera viewCamera;

        [Tooltip("建筑网格。蓝图吸附、可建区域与占用判定都来自它。留空会报错，并且一律拒绝放置")]
        [SerializeField] private BuildingGrid grid;

        [Tooltip("网格可视化。蓝图移动时显示当前占地格。留空仍可放置，但没有格线与高亮")]
        [SerializeField] private BuildingGridVisualizer gridVisualizer;

        [Header("地面")]
        [Tooltip("地面高度。场景里的 Plane 在 y = 0。" +
                 "这里用一个数学平面求交，而不是物理射线——不会被矿石、建筑、玩家挡住")]
        [SerializeField] private float groundHeight = 0f;

        [Header("占地检测")]
        [Tooltip("检测盒从地面向上延伸的高度，必须覆盖建筑碰撞体的高度。" +
                 "建筑之间的重叠检测就用这个盒子做 Physics.CheckBox")]
        [SerializeField] private float overlapBoxHeight = 4f;

        [Header("UI 屏蔽")]
        [Tooltip("勾选后，指针落到 UI（商店面板、建筑按钮、蓝图旁的 ×/√）上时冻结蓝图位置。" +
                 "否则玩家把鼠标移向按钮时，蓝图会跟着跑到按钮背后的地面上")]
        [SerializeField] private bool blockClicksOverUI = true;

        [Header("调试")]
        [Tooltip("是否在 Console 打印放置成功日志")]
        [SerializeField] private bool logPlacementFlow = true;

        [Tooltip("是否在 Console 打印「位置不可放置，本次点击被忽略」的日志")]
        [SerializeField] private bool logBlockedPlacements = true;

        /// <summary>建筑碰撞体查询的复用缓冲，避免蓝图跟随指针时每帧分配数组。</summary>
        private readonly Collider[] overlapBuffer = new Collider[128];

        private bool warnedAboutMissingEventSystem;
        private bool warnedAboutMissingBuildingLayers;
        private bool warnedAboutBadFootprint;
        private bool warnedAboutMissingGrid;

        /// <summary>当前吸附位置属于哪个蓝图。换蓝图时必须丢弃旧位置，避免把新蓝图落到上一座的位置。</summary>
        private BuildingBlueprint posedBlueprint;

        private Vector3 currentSnappedCenter;
        private RectInt currentSnappedRect;
        private string currentPlacementReason;
        private bool hasSnappedPose;
        private bool currentPlacementValid;
        private bool dragging;

        /// <summary>当前是否有一个属于场上蓝图的吸附位置，可供 √ 落位。</summary>
        public bool HasCurrentSnappedPose =>
            hasSnappedPose && buildingManager != null && posedBlueprint == buildingManager.CurrentBlueprint;

        /// <summary>最近一次吸附后的建筑中心（世界坐标）。</summary>
        public Vector3 CurrentSnappedCenter => currentSnappedCenter;

        /// <summary>最近一次吸附后的占地格子。</summary>
        public RectInt CurrentSnappedRect => currentSnappedRect;

        /// <summary>当前吸附位置能否落位。蓝图还没吸附、已落位或换蓝图时一律为 false。</summary>
        public bool IsCurrentPlacementValid =>
            HasCurrentSnappedPose &&
            currentPlacementValid &&
            buildingManager.Phase == BuildingPhase.Placing;

        /// <summary>最近一次不可放置的原因，供按钮或调试日志使用。</summary>
        public string CurrentPlacementReason => currentPlacementReason;

        private void Awake()
        {
            if (buildingManager == null)
            {
                Debug.LogError("[BuildingPlacement] BuildingManager 未配置，放置功能完全不会工作。", this);
            }

            if (viewCamera == null)
            {
                viewCamera = Camera.main;

                if (viewCamera == null)
                {
                    Debug.LogError("[BuildingPlacement] 未配置 View Camera，且场景中没有带 MainCamera 标签的相机，" +
                                   "无法把鼠标位置换算成世界坐标。", this);
                }
            }

            if (grid == null)
            {
                Debug.LogError("[BuildingPlacement] BuildingGrid 未配置，任何位置都会被判为不可放置。" +
                               "请重新运行菜单「屿见/建筑/配置建筑网格与建造相机（阶段 7）」。", this);
            }

            if (gridVisualizer == null)
            {
                Debug.LogWarning("[BuildingPlacement] BuildingGridVisualizer 未配置，" +
                                 "蓝图仍会吸附并变绿/红，但看不到格线与占地高亮。", this);
            }

            if (overlapBoxHeight <= 0f)
            {
                Debug.LogWarning($"[BuildingPlacement] Overlap Box Height 为 {overlapBoxHeight}，已按 0.1 处理。", this);
                overlapBoxHeight = 0.1f;
            }
        }

        private void Update()
        {
            // 没有蓝图时不占用任何资源：不读指针、不发射线、不做物理查询
            if (buildingManager == null || !buildingManager.IsPlacing)
            {
                dragging = false;
                ClearSnappedPose();
                HideGridPreview();
                return;
            }

            // 蓝图一旦落位就不再跟鼠标。让开这一步是必须的：
            // 玩家要从材料面板往外拖，鼠标会长时间停在面板上，蓝图跟过去就会被面板整个盖住
            if (buildingManager.Phase == BuildingPhase.Filling)
            {
                dragging = false;
                ClearSnappedPose();
                HideGridPreview();
                return;
            }

            BuildingBlueprint blueprint = buildingManager.CurrentBlueprint;

            if (blueprint == null)
            {
                dragging = false;
                ClearSnappedPose();
                HideGridPreview();
                return;
            }

            // 换了一座建筑就丢掉上一座的位置。否则在指针还没落到地面上时点 √，
            // 会把新蓝图放到旧蓝图的位置上
            if (posedBlueprint != blueprint)
            {
                dragging = false;
                ClearSnappedPose();
                posedBlueprint = blueprint;
            }

            Pointer pointer = Pointer.current;

            if (pointer == null)
            {
                return;
            }

            // 松开就停止拖动。蓝图停在最后的位置，玩家可以腾出手去点 ×/√
            if (pointer.press.wasReleasedThisFrame)
            {
                dragging = false;
            }

            // 没有按住时蓝图不动。这就是「拖拽」与「跟随指针」的关键区别：
            // 如果蓝图一直跟着指针，×/√ 也会跟着跑，指针永远追不上按钮
            if (!pointer.press.isPressed && !pointer.press.wasPressedThisFrame)
            {
                dragging = false;
                return;
            }

            Vector2 screenPosition = pointer.position.ReadValue();

            if (!dragging)
            {
                // 只有从地面上按下才进入拖动；按在 UI 上交给 Button，不拖蓝图
                if (!pointer.press.wasPressedThisFrame || IsPointerOverUI(screenPosition))
                {
                    return;
                }

                dragging = true;
            }

            // 拖动途中指针经过 UI（例如从地面滑到 ×/√）时冻结蓝图。
            // 保留上一帧的吸附位置，手指松开后按钮仍可点
            if (IsPointerOverUI(screenPosition))
            {
                return;
            }

            if (!TryGetGroundPoint(screenPosition, out Vector3 point))
            {
                // 相机没看向地面（例如鼠标在屏幕上半部分、射线朝天）时保留蓝图上一次的位置，
                // 但不再允许落位——玩家已经离开地面目标了
                currentPlacementValid = false;
                currentPlacementReason = "指针没有落在地面上";
                blueprint.SetState(BlueprintState.Invalid);
                return;
            }

            if (grid == null ||
                !grid.TryGetSnappedPose(point, blueprint.Footprint, out Vector3 snappedCenter, out RectInt cellRect))
            {
                // 指针在网格外：蓝图上一次的位置保留，只把它标红，避免视觉上突然消失
                hasSnappedPose = false;
                currentPlacementValid = false;
                currentPlacementReason = "指针不在建筑网格内";
                blueprint.SetState(BlueprintState.Invalid);
                HideGridPreview();
                return;
            }

            // 先吸附再赋值。吸附后的位置才是蓝图与实体建筑共同的权威坐标
            blueprint.transform.position = snappedCenter;

            bool valid = IsPlacementValid(snappedCenter, cellRect, blueprint, out string reason);

            currentSnappedCenter = snappedCenter;
            currentSnappedRect = cellRect;
            currentPlacementReason = reason;
            currentPlacementValid = valid;
            hasSnappedPose = true;

            blueprint.SetState(valid ? BlueprintState.Valid : BlueprintState.Invalid);

            if (gridVisualizer != null)
            {
                gridVisualizer.ShowPreview(cellRect, valid);
            }

            // 这里不打印 reason：指针每帧移动，失败是常态，打印会刷屏。
            // 点 √ 时会在 TryAnchorCurrentBlueprint 里把具体原因打出来。
        }

        /// <summary>丢掉吸附位置。没有蓝图 / 已落位 / 换蓝图时调用。</summary>
        private void ClearSnappedPose()
        {
            posedBlueprint = null;
            hasSnappedPose = false;
            currentPlacementValid = false;
            currentPlacementReason = null;
        }

        /// <summary>隐藏占地高亮。visualizer 未配置时是空操作。</summary>
        private void HideGridPreview()
        {
            if (gridVisualizer != null)
            {
                gridVisualizer.HidePreview();
            }
        }

        /// <summary>
        /// 把当前蓝图落位到最近一次吸附的位置。由蓝图旁的 √ 按钮调用。
        ///
        /// 这里不重新读指针：玩家为了点 √ 已经把光标移到 UI 上了，
        /// 若重新读指针，蓝图会跑到按钮背后的地面上。用 Update 冻结下来的位置是正确的。
        /// </summary>
        /// <returns>是否落位成功。</returns>
        public bool TryAnchorCurrentBlueprint()
        {
            if (buildingManager == null)
            {
                Debug.LogError("[BuildingPlacement] BuildingManager 未配置，无法落位。", this);
                return false;
            }

            if (!buildingManager.IsPlacing)
            {
                Debug.LogWarning("[BuildingPlacement] 当前没有蓝图，√ 被忽略。", this);
                return false;
            }

            if (buildingManager.Phase == BuildingPhase.Filling)
            {
                Debug.LogWarning("[BuildingPlacement] 蓝图已经落位，不能重复落位。", this);
                return false;
            }

            BuildingBlueprint blueprint = buildingManager.CurrentBlueprint;

            if (blueprint == null)
            {
                Debug.LogWarning("[BuildingPlacement] 当前没有蓝图，√ 被忽略。", this);
                return false;
            }

            if (!HasCurrentSnappedPose)
            {
                if (logBlockedPlacements)
                {
                    Debug.LogWarning("[BuildingPlacement] 蓝图还没有吸附到网格上（指针可能不在可建区域），" +
                                     "本次落位被忽略。");
                }

                return false;
            }

            // 再判一次：从最后一帧到点击之间，场景里的占用状态可能已经变了
            if (!IsPlacementValid(currentSnappedCenter, currentSnappedRect, blueprint, out string reason))
            {
                currentPlacementValid = false;
                currentPlacementReason = reason;

                if (logBlockedPlacements)
                {
                    Debug.LogWarning($"[BuildingPlacement] 格子 {currentSnappedRect} 处不可放置：{reason}。" +
                                     "本次落位被忽略，蓝图保持跟随鼠标。");
                }

                return false;
            }

            if (logPlacementFlow)
            {
                Debug.Log($"[BuildingPlacement] 在 {currentSnappedCenter} 落位，占地格子 {currentSnappedRect}。");
            }

            return buildingManager.AnchorBlueprint(currentSnappedCenter, currentSnappedRect);
        }

        /// <summary>
        /// 判断吸附后的位置能否放置该蓝图。这是“这里能不能放”的唯一入口。
        ///
        /// 判定顺序：
        ///   1. 网格（BuildingGrid.CanPlace）：越界 / 已占用 / 不在可建台地上；
        ///   2. 建筑图层与占地尺寸配置是否有效；
        ///   3. Physics.CheckBox 兜底：发现没有经过网格登记的 Building 层碰撞体。
        ///
        /// 网格是建筑之间占用的权威判定；Physics 只负责兜住“场景里本来就有、但没走
        /// BuildAtAnchor 的碰撞体”，两者都通过才算可放。
        /// </summary>
        private bool IsPlacementValid(
            Vector3 snappedCenter, RectInt cellRect, BuildingBlueprint blueprint, out string reason)
        {
            reason = null;

            if (buildingManager == null)
            {
                reason = "BuildingManager 未配置";
                return false;
            }

            if (grid == null)
            {
                if (!warnedAboutMissingGrid)
                {
                    warnedAboutMissingGrid = true;
                    Debug.LogError("[BuildingPlacement] BuildingGrid 未配置，一律判定为不可放置。", this);
                }

                reason = "BuildingGrid 未配置";
                return false;
            }

            if (!grid.CanPlace(cellRect, out GridPlacementFailure failure))
            {
                reason = failure.ToChineseName();
                return false;
            }

            LayerMask buildingLayers = buildingManager.BuildingLayers;

            if (buildingLayers.value == 0)
            {
                if (!warnedAboutMissingBuildingLayers)
                {
                    warnedAboutMissingBuildingLayers = true;
                    Debug.LogError("[BuildingPlacement] Building Layers 没有勾选任何图层，重叠检测无法进行，" +
                                   "一律判定为不可放置。请在 BuildingManager 上勾选 Building 层。", this);
                }

                reason = "建筑图层未配置";
                return false;
            }

            Vector2 footprint = blueprint.Footprint;

            if (footprint.x <= 0f || footprint.y <= 0f)
            {
                if (!warnedAboutBadFootprint)
                {
                    warnedAboutBadFootprint = true;
                    Debug.LogError($"[BuildingPlacement] 蓝图「{blueprint.name}」的占地尺寸为 {footprint}，" +
                                   "含非正数，一律判定为不可放置。请检查 BuildingData 里的 Footprint。", this);
                }

                reason = "占地尺寸非法";
                return false;
            }

            Vector3 halfExtents = new Vector3(
                footprint.x * 0.5f,
                overlapBoxHeight * 0.5f,
                footprint.y * 0.5f);

            // 检测盒从吸附后的地面点向上延伸到 overlapBoxHeight 高度。
            // 不用以点为中心：那样盒子会有一半埋在地下，对"建筑只向上长"的情况没有额外收益
            Vector3 center = snappedCenter + Vector3.up * (overlapBoxHeight * 0.5f);

            if (IsBlockedByUnregisteredCollider(center, halfExtents, buildingLayers))
            {
                reason = "与已有建筑碰撞体重叠";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Physics 兜底：只拦「没有经过 BuildingGrid 登记」的 Building 层碰撞体。
        ///
        /// 必须排除已登记的建筑，否则刚好贴边的两块地会被判成重叠：
        /// PhysX 的 contactOffset 会让两个只接触、没有真正穿插的盒子也命中查询，
        /// 2×2 建筑的碰撞体正好是 4×4，紧挨着放时面贴面，就会表现为「必须空出一整格」。
        /// 已登记建筑之间的占用关系由 BuildingGrid.CanPlace 精确到格判定，不需要 Physics 再管。
        /// </summary>
        private bool IsBlockedByUnregisteredCollider(
            Vector3 center, Vector3 halfExtents, LayerMask buildingLayers)
        {
            int count = Physics.OverlapBoxNonAlloc(
                center, halfExtents, overlapBuffer, Quaternion.identity, buildingLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider hit = overlapBuffer[i];

                if (hit == null || IsRegisteredBuilding(hit))
                {
                    continue;
                }

                return true;
            }

            // 缓冲装满时可能还有没取到的碰撞体，保守地按不可放置处理
            return count >= overlapBuffer.Length;
        }

        /// <summary>该碰撞体是否属于一座已经登记到网格里的建筑。</summary>
        private bool IsRegisteredBuilding(Collider collider)
        {
            IReadOnlyList<GameObject> placed = buildingManager.PlacedBuildings;

            for (int i = 0; i < placed.Count; i++)
            {
                GameObject building = placed[i];

                if (building == null)
                {
                    continue;
                }

                Transform target = collider.transform;

                if (target == building.transform || target.IsChildOf(building.transform))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 屏幕坐标 → 地面上的世界坐标。
        /// 用一个数学平面求交，而不是 Physics.Raycast：不会被矿石、已放置建筑或玩家挡住，
        /// 也不依赖地面物体挂没挂 Collider。代价是它假定地面是 y = groundHeight 的水平面。
        /// </summary>
        private bool TryGetGroundPoint(Vector2 screenPosition, out Vector3 point)
        {
            return TryGetGroundPoint(viewCamera, groundHeight, screenPosition, out point);
        }

        /// <summary>
        /// 上面那个方法的静态版本，供 BuildingConstruction 判断"材料被拖到蓝图上了没"。
        /// 抽出来是为了让两处用的是同一套换算，不至于一处改了另一处忘改。
        /// </summary>
        /// <param name="camera">用于发射射线的相机，为空时返回 false。</param>
        /// <param name="groundHeight">地面高度。</param>
        /// <param name="screenPosition">屏幕坐标。</param>
        /// <param name="point">地面上的交点。</param>
        public static bool TryGetGroundPoint(
            Camera camera, float groundHeight, Vector2 screenPosition, out Vector3 point)
        {
            point = default;

            if (camera == null)
            {
                return false;
            }

            Ray ray = camera.ScreenPointToRay(screenPosition);
            Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, groundHeight, 0f));

            if (!groundPlane.Raycast(ray, out float enter))
            {
                // 射线与地面平行或朝上，说明鼠标指向天空
                return false;
            }

            point = ray.GetPoint(enter);
            return true;
        }

        /// <summary>
        /// 判断指定屏幕坐标是否落在 UI 上。实现统一在 <see cref="PointerOverUI"/>。
        /// </summary>
        private bool IsPointerOverUI(Vector2 screenPosition)
        {
            if (!blockClicksOverUI)
            {
                return false;
            }

            return PointerOverUI.IsOver(screenPosition, this, ref warnedAboutMissingEventSystem);
        }
    }
}
