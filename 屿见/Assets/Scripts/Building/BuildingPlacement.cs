using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Yujian.Core;

namespace Yujian.Building
{
    /// <summary>
    /// 放置控制器。职责：把"鼠标屏幕坐标"变成"蓝图的世界坐标"，并判断那里能不能放。
    ///
    /// 每帧：指针位置 → 屏幕坐标 → 射线 → 地面交点 → 移动蓝图 → Physics.CheckBox 重叠检测 → 蓝图变色。
    /// 点击：判定通过后调用 BuildingManager.AnchorBlueprint()，自己既不生成建筑也不记账。
    ///
    /// 阶段 4 新增：蓝图落位（Filling 相位）之后本类完全让开——不再移动蓝图、不再响应确认点击，
    /// 建造改由材料面板上的「确认建造」按钮触发。
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

        [Tooltip("确认放置的输入动作。复用工程的 Assets/InputSystem_Actions.inputactions，" +
                 "选 Player/Attack（已绑定鼠标左键、触摸点击、手柄），不要新建输入资产。" +
                 "放置期间 MiningInput 会被禁用，所以同一次点击不会既放置又挖矿")]
        [SerializeField] private InputActionReference confirmAction;

        [Header("地面")]
        [Tooltip("地面高度。场景里的 Plane 在 y = 0。" +
                 "这里用一个数学平面求交，而不是物理射线——不会被矿石、建筑、玩家挡住")]
        [SerializeField] private float groundHeight = 0f;

        [Header("占地检测")]
        [Tooltip("检测盒从地面向上延伸的高度，必须覆盖建筑碰撞体的高度。" +
                 "建筑之间的重叠检测就用这个盒子做 Physics.CheckBox")]
        [SerializeField] private float overlapBoxHeight = 4f;

        [Header("UI 屏蔽")]
        [Tooltip("勾选后，落在 UI（商店面板、建筑按钮）上的点击不会穿透去放置建筑。" +
                 "Physics.CheckBox 看不见 Canvas，所以必须单独判定一次")]
        [SerializeField] private bool blockClicksOverUI = true;

        [Header("调试")]
        [Tooltip("是否在 Console 打印放置成功日志")]
        [SerializeField] private bool logPlacementFlow = true;

        [Tooltip("是否在 Console 打印「位置不可放置，本次点击被忽略」的日志")]
        [SerializeField] private bool logBlockedPlacements = true;

        /// <summary>UI 射线结果的复用缓冲，避免每次点击都分配新 List。</summary>

        /// <summary>建筑碰撞体查询的复用缓冲，避免蓝图跟随鼠标时每帧分配数组。</summary>
        private readonly Collider[] overlapBuffer = new Collider[128];

        private bool warnedAboutMissingEventSystem;
        private bool warnedAboutMissingBuildingLayers;
        private bool warnedAboutBadFootprint;
        private bool warnedAboutMissingGrid;

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

            if (confirmAction == null)
            {
                Debug.LogError("[BuildingPlacement] Confirm Action 未配置。请把 Assets/InputSystem_Actions.inputactions " +
                               "拖入该字段，并在弹出的动作列表中选中 Player/Attack。", this);
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

        private void OnEnable()
        {
            if (confirmAction == null)
            {
                return;
            }

            // performed 在一次按下中只触发一次：按住不松手不会连续放置
            confirmAction.action.performed += OnConfirmPerformed;
            confirmAction.action.Enable();
        }

        private void OnDisable()
        {
            if (confirmAction == null)
            {
                return;
            }

            confirmAction.action.performed -= OnConfirmPerformed;

            // 这里**故意不调用** confirmAction.action.Disable()。
            // MiningInput 用的是同一个 Player/Attack 动作，而 InputAction.Enable/Disable 不是引用计数的
            // （Enable() 内部就是 `if (enabled) return;`），本类一关就会把 MiningInput 也一起关掉。
            // 本类被禁用时最多是不接收放置点击，动作留给 MiningInput 自己管即可。
        }

        private void LateUpdate()
        {
            EnsureConfirmActionEnabled();
        }

        /// <summary>
        /// 保证确认动作在放置期间是启用的。
        ///
        /// 为什么需要这个：进入放置模式时 BuildingManager 会禁用 MiningInput 组件来屏蔽挖矿，
        /// 而 MiningInput.OnDisable() 会调用该共享动作的 Disable()。由于 Enable/Disable 不是引用计数的，
        /// 这一关会把本类的输入也一起关掉——表现就是蓝图能跟手，但点击永远放不下去。
        ///
        /// 放在 LateUpdate 而不是 Update：MiningInput.OnDisable 是 SelectBuilding 调用栈里同步触发的，
        /// 那时仍在 Update 阶段；等到 LateUpdate，本帧的禁用已经全部发生完了。
        /// </summary>
        private void EnsureConfirmActionEnabled()
        {
            if (confirmAction == null)
            {
                return;
            }

            if (buildingManager == null || !buildingManager.IsPlacing)
            {
                return;
            }

            if (confirmAction.action.enabled)
            {
                return;
            }

            confirmAction.action.Enable();
        }

        private void Update()
        {
            // 没有蓝图时不占用任何资源：不读指针、不发射线、不做物理查询
            if (buildingManager == null || !buildingManager.IsPlacing)
            {
                HideGridPreview();
                return;
            }

            // 蓝图一旦落位就不再跟鼠标。让开这一步是必须的：
            // 玩家要从材料面板往外拖，鼠标会长时间停在面板上，蓝图跟过去就会被面板整个盖住
            if (buildingManager.Phase == BuildingPhase.Filling)
            {
                HideGridPreview();
                return;
            }

            BuildingBlueprint blueprint = buildingManager.CurrentBlueprint;

            if (blueprint == null)
            {
                HideGridPreview();
                return;
            }

            if (!TryGetPointerScreenPosition(out Vector2 screenPosition))
            {
                return;
            }

            if (!TryGetGroundPoint(screenPosition, out Vector3 point))
            {
                // 相机没看向地面（例如鼠标在屏幕上半部分、射线朝天）时保留蓝图上一次的位置
                return;
            }

            if (grid == null ||
                !grid.TryGetSnappedPose(point, blueprint.Footprint, out Vector3 snappedCenter, out RectInt cellRect))
            {
                // 指针在网格外：蓝图上一次的位置保留，只把它标红，避免视觉上突然消失
                blueprint.SetState(BlueprintState.Invalid);
                HideGridPreview();
                return;
            }

            // 先吸附再赋值。吸附后的位置才是蓝图与实体建筑共同的权威坐标
            blueprint.transform.position = snappedCenter;

            bool valid = IsPlacementValid(snappedCenter, cellRect, blueprint, out string reason);
            blueprint.SetState(valid ? BlueprintState.Valid : BlueprintState.Invalid);

            if (gridVisualizer != null)
            {
                gridVisualizer.ShowPreview(cellRect, valid);
            }

            // 这里不打印 reason：指针每帧移动，失败是常态，打印会刷屏。
            // 点击时会在 OnConfirmPerformed 里把具体原因打出来。
        }

        /// <summary>隐藏占地高亮。visualizer 未配置时是空操作。</summary>
        private void HideGridPreview()
        {
            if (gridVisualizer != null)
            {
                gridVisualizer.HidePreview();
            }
        }

        private void OnConfirmPerformed(InputAction.CallbackContext context)
        {
            if (buildingManager == null || !buildingManager.IsPlacing)
            {
                // 没在放置模式时，这次点击归 MiningInput 管，这里直接放行
                return;
            }

            // 已落位的蓝图不该再被点击挪动；建造由材料面板的「确认建造」按钮触发
            if (buildingManager.Phase == BuildingPhase.Filling)
            {
                return;
            }

            if (!TryGetPointerScreenPosition(out Vector2 screenPosition))
            {
                return;
            }

            // 点在 UI 上不确认放置。选中建筑的那一次点击也走这条路被挡掉，
            // 否则点了按钮会立刻在按钮背后的地面上放一座建筑
            if (IsPointerOverUI(screenPosition))
            {
                return;
            }

            BuildingBlueprint blueprint = buildingManager.CurrentBlueprint;

            if (blueprint == null)
            {
                return;
            }

            if (!TryGetGroundPoint(screenPosition, out Vector3 point))
            {
                Debug.LogWarning("[BuildingPlacement] 点击位置转换不到地面上的点，本次放置取消。", this);
                return;
            }

            if (grid == null ||
                !grid.TryGetSnappedPose(point, blueprint.Footprint, out Vector3 snappedCenter, out RectInt cellRect))
            {
                if (logBlockedPlacements)
                {
                    Debug.LogWarning("[BuildingPlacement] 点击位置在建筑网格之外，本次点击被忽略。" +
                                     "蓝图保持跟随鼠标。");
                }

                return;
            }

            if (!IsPlacementValid(snappedCenter, cellRect, blueprint, out string reason))
            {
                if (logBlockedPlacements)
                {
                    Debug.LogWarning($"[BuildingPlacement] 格子 {cellRect} 处不可放置：{reason}。" +
                                     "本次点击被忽略，蓝图保持跟随鼠标。");
                }

                return;
            }

            if (logPlacementFlow)
            {
                Debug.Log($"[BuildingPlacement] 在 {snappedCenter} 确认落位，占地格子 {cellRect}。");
            }

            buildingManager.AnchorBlueprint(snappedCenter, cellRect);
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
        /// 取当前指针的屏幕坐标。用 Pointer 而不是 Mouse，
        /// 这样触摸、触控笔与鼠标走的是同一套逻辑，为将来多指触摸留出空间。
        /// </summary>
        private static bool TryGetPointerScreenPosition(out Vector2 screenPosition)
        {
            Pointer pointer = Pointer.current;

            if (pointer == null)
            {
                screenPosition = default;
                return false;
            }

            screenPosition = pointer.position.ReadValue();
            return true;
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
