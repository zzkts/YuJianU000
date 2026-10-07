using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

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
        private readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>();

        private bool warnedAboutMissingEventSystem;
        private bool warnedAboutMissingBuildingLayers;
        private bool warnedAboutBadFootprint;

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
                return;
            }

            // 蓝图一旦落位就不再跟鼠标。让开这一步是必须的：
            // 玩家要从材料面板往外拖，鼠标会长时间停在面板上，蓝图跟过去就会被面板整个盖住
            if (buildingManager.Phase == BuildingPhase.Filling)
            {
                return;
            }

            BuildingBlueprint blueprint = buildingManager.CurrentBlueprint;

            if (blueprint == null)
            {
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

            blueprint.transform.position = point;
            blueprint.SetState(IsPlacementValid(point, blueprint) ? BlueprintState.Valid : BlueprintState.Invalid);
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

            if (!IsPlacementValid(point, blueprint))
            {
                if (logBlockedPlacements)
                {
                    Debug.LogWarning($"[BuildingPlacement] {point} 处不可放置（与已有建筑重叠，或占地/图层配置无效），" +
                                     "本次点击被忽略。蓝图保持跟随鼠标。");
                }

                return;
            }

            if (logPlacementFlow)
            {
                Debug.Log($"[BuildingPlacement] 在 {point} 确认落位。");
            }

            buildingManager.AnchorBlueprint(point);
        }

        /// <summary>
        /// 判断指定位置能否放置该蓝图。
        /// 用 Physics.CheckBox 而不是 OverlapBox：这里只需要一个"有没有"的布尔值，
        /// CheckBox 不分配数组、语义最直接。
        /// </summary>
        private bool IsPlacementValid(Vector3 point, BuildingBlueprint blueprint)
        {
            LayerMask buildingLayers = buildingManager.BuildingLayers;

            if (buildingLayers.value == 0)
            {
                if (!warnedAboutMissingBuildingLayers)
                {
                    warnedAboutMissingBuildingLayers = true;
                    Debug.LogError("[BuildingPlacement] Building Layers 没有勾选任何图层，重叠检测无法进行，" +
                                   "一律判定为不可放置。请在 BuildingManager 上勾选 Building 层。", this);
                }

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

                return false;
            }

            Vector3 halfExtents = new Vector3(
                footprint.x * 0.5f,
                overlapBoxHeight * 0.5f,
                footprint.y * 0.5f);

            // 检测盒从地面(点)向上延伸到 overlapBoxHeight 高度。
            // 不用以点为中心：那样盒子会有一半埋在地下，对"建筑只向上长"的情况没有额外收益
            Vector3 center = point + Vector3.up * (overlapBoxHeight * 0.5f);

            bool blocked = Physics.CheckBox(
                center, halfExtents, Quaternion.identity, buildingLayers, QueryTriggerInteraction.Ignore);

            return !blocked;
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
        /// 判断指定屏幕坐标是否落在 UI 上。
        /// 与 MiningInput 用的是同一套做法：EventSystem.RaycastAll 按坐标判定，
        /// 而不是 EventSystem.IsPointerOverGameObject()——后者只认当前鼠标位置，触屏要额外传 fingerId，
        /// 且结果受 EventSystem 与调用方的执行顺序影响。
        /// </summary>
        private bool IsPointerOverUI(Vector2 screenPosition)
        {
            if (!blockClicksOverUI)
            {
                return false;
            }

            EventSystem eventSystem = EventSystem.current;

            if (eventSystem == null)
            {
                if (!warnedAboutMissingEventSystem)
                {
                    warnedAboutMissingEventSystem = true;
                    Debug.LogWarning("[BuildingPlacement] 场景中没有 EventSystem，无法判断点击是否落在 UI 上；" +
                                     "后续点击一律不做 UI 屏蔽。", this);
                }

                return false;
            }

            PointerEventData eventData = new PointerEventData(eventSystem)
            {
                position = screenPosition,
            };

            uiRaycastResults.Clear();
            eventSystem.RaycastAll(eventData, uiRaycastResults);

            return uiRaycastResults.Count > 0;
        }
    }
}
