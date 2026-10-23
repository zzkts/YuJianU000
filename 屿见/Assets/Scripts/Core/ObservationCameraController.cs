using UnityEngine;
using UnityEngine.InputSystem;
using Yujian.Building;

namespace Yujian.Core
{
    /// <summary>
    /// 观察模式相机：围绕一个地面焦点做轨道式旋转 / 平移 / 缩放。
    ///
    /// 操作（2026-10-23 用户确认）：
    ///   · 滚轮 → 缩放（改变焦点到相机的距离）；
    ///   · 按住鼠标中键拖动 → 绕焦点旋转；按下后以光标**最先移动的方向**锁定单轴，
    ///     整段拖动只响应那一轴，松开中键解除锁定（参考《双点校园》，减少眩晕）；
    ///   · 按住鼠标右键拖动 → 在地面平面上平移焦点，相机跟着走。
    ///
    /// 相机归属：本类只在 <see cref="GameArea.Observation"/> 且
    /// <see cref="BuildingCameraDirector"/> 不在建造模式 / 过渡时写相机。
    /// 进入观察模式的机位过渡由 <see cref="AreaCameraDirector"/> 负责，到位后调用
    /// <see cref="BeginControl"/>；本类再从相机的最终姿态反推焦点、距离、偏航、俯仰。
    /// 这样「观察机位」只在 AreaCameraDirector 里配一份，不会出现两份数值互相打架。
    ///
    /// 本类不做区域切换，也不管建筑 / 挖矿的输入门控——那些仍然归 GameAreaController。
    /// </summary>
    [DisallowMultipleComponent]
    public class ObservationCameraController : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("区域状态。只有 CurrentArea == Observation 时才接管相机")]
        [SerializeField] private GameAreaController areas;

        [Tooltip("建造模式相机调度。它还在建造 / 过渡时本类让位，避免同帧抢 Transform")]
        [SerializeField] private BuildingCameraDirector buildingDirector;

        [Tooltip("要驱动的相机。留空则依次尝试：本物体上的 Camera → 带 MainCamera 标签的相机")]
        [SerializeField] private Transform cameraTransform;

        [Header("输入")]
        [Tooltip("指针位移。复用 InputSystem_Actions 的 Player/Look（<Pointer>/delta）")]
        [SerializeField] private InputActionReference lookAction;

        [Tooltip("按住哪个键旋转。复用 UI/MiddleClick（<Mouse>/middleButton）")]
        [SerializeField] private InputActionReference rotateAction;

        [Tooltip("按住哪个键平移。复用 UI/RightClick（<Mouse>/rightButton）")]
        [SerializeField] private InputActionReference panAction;

        [Tooltip("滚轮缩放。复用 UI/ScrollWheel（<Mouse>/scroll）")]
        [SerializeField] private InputActionReference scrollAction;

        [Tooltip("指针在 UI 上时不响应旋转 / 平移 / 缩放")]
        [SerializeField] private bool blockWhenPointerOverUI = true;

        [Header("旋转（中键）")]
        [Tooltip("旋转灵敏度：指针每移动 1 像素转多少度")]
        [SerializeField] private float rotateSensitivity = 0.25f;

        [Tooltip("勾选后上下拖动方向反转")]
        [SerializeField] private bool invertRotateY = false;

        [Tooltip("单轴锁定的判定阈值（像素）：按下中键后累计移动超过它才锁定主轴，" +
                 "太小会被手抖误判成另一个方向")]
        [SerializeField] private float rotateAxisLockThreshold = 3f;

        [Tooltip("俯仰角下限（度）。越小越接近水平视角")]
        [SerializeField] private float minPitch = 15f;

        [Tooltip("俯仰角上限（度）。越大越接近垂直俯视；不要到 90，否则平移方向会退化")]
        [SerializeField] private float maxPitch = 85f;

        [Header("平移（右键）")]
        [Tooltip("平移灵敏度，1 = 指针下的地面大致跟手")]
        [SerializeField] private float panSensitivity = 1f;

        [Tooltip("勾选后上下拖动方向反转")]
        [SerializeField] private bool invertPanY = false;

        [Tooltip("是否把焦点限制在观察机位附近，防止把岛拖丢")]
        [SerializeField] private bool clampPanToHome = true;

        [Tooltip("焦点相对观察机位焦点的最大偏移（世界单位）")]
        [SerializeField] private float maxPanDistance = 80f;

        [Header("缩放（滚轮）")]
        [Tooltip("滚轮每格改变的距离（世界单位）")]
        [SerializeField] private float zoomStepPerNotch = 8f;

        [Tooltip("相机到焦点的最小距离")]
        [SerializeField] private float minDistance = 40f;

        [Tooltip("相机到焦点的最大距离")]
        [SerializeField] private float maxDistance = 200f;

        [Header("地面")]
        [Tooltip("焦点所在的地面高度。进入观察模式时用它与相机前向求交，得到轨道焦点")]
        [SerializeField] private float groundY = 0f;

        [Header("调试")]
        [Tooltip("是否在 Console 打印接管 / 初始化日志")]
        [SerializeField] private bool logCameraControlFlow = true;

        private Camera viewCamera;

        // 轨道状态：焦点 + 焦点到相机的距离 + 偏航 / 俯仰。
        // 相机位置永远是「焦点 - 旋转后的前向 * 距离」，所以旋转、平移、缩放不会互相打架。
        private Vector3 pivot;
        private Vector3 homePivot;
        private float distance;
        private float yaw;
        private float pitch;

        private bool controlling;
        private bool activationPending;
        private bool rotateBlockedByUI;
        private bool panBlockedByUI;
        private bool warnedAboutMissingEventSystem;

        // 中键旋转的单轴锁定：按下后先在 Probe 里累计位移，超过阈值时按主轴锁定；
        // 锁定后整段拖动只更新锁定的轴，松开中键（或停止旋转）时复位。
        private RotateAxis rotateAxisLock = RotateAxis.None;
        private Vector2 rotateAxisProbeDelta;

        /// <summary>本类当前是否正在写相机。供调试或其他系统查询。</summary>
        public bool IsControlling => controlling;

        private void Awake()
        {
            cameraTransform = CameraResolver.Resolve(cameraTransform, gameObject);
            viewCamera = cameraTransform != null ? cameraTransform.GetComponent<Camera>() : null;

            if (areas == null)
            {
                Debug.LogError("[ObservationCameraController] GameAreaController 未配置，" +
                               "观察模式相机永远不会接管。请重新运行菜单「屿见/场景/配置观察模式系统」。", this);
            }

            if (cameraTransform == null)
            {
                Debug.LogError("[ObservationCameraController] 找不到相机，" +
                               "观察模式的旋转 / 平移 / 缩放不会生效。请手动指定 Camera Transform。", this);
            }

            if (lookAction == null || rotateAction == null || panAction == null || scrollAction == null)
            {
                Debug.LogError("[ObservationCameraController] 输入动作未配置完整" +
                               "（Look / Rotate / Pan / Scroll），缺失的动作不会响应。" +
                               "请重新运行菜单「屿见/场景/配置观察模式系统」。", this);
            }

            if (buildingDirector == null)
            {
                Debug.LogWarning("[ObservationCameraController] BuildingCameraDirector 未配置，" +
                                 "从建造模式退出时本类无法等待过渡结束，可能和建造调度抢一帧相机。" +
                                 "请把 BuildingSystem 上的它拖到该字段。", this);
            }

            if (minPitch > maxPitch)
            {
                Debug.LogWarning($"[ObservationCameraController] Min Pitch({minPitch}) 大于 Max Pitch({maxPitch})，" +
                                 "已交换两者。", this);
                float temp = minPitch;
                minPitch = maxPitch;
                maxPitch = temp;
            }

            minPitch = Mathf.Clamp(minPitch, 1f, 89f);
            maxPitch = Mathf.Clamp(maxPitch, minPitch, 89f);

            if (minDistance <= 0.1f)
            {
                Debug.LogWarning($"[ObservationCameraController] Min Distance 为 {minDistance}，已按 0.1 处理。", this);
                minDistance = 0.1f;
            }

            if (maxDistance < minDistance)
            {
                Debug.LogWarning($"[ObservationCameraController] Max Distance({maxDistance}) 小于 " +
                                 $"Min Distance({minDistance})，已把最大值抬到最小值。", this);
                maxDistance = minDistance;
            }
        }

        private void OnEnable()
        {
            if (areas != null)
            {
                areas.OnAreaChanged += HandleAreaChanged;
            }
        }

        private void OnDisable()
        {
            if (areas != null)
            {
                areas.OnAreaChanged -= HandleAreaChanged;
            }

            StopControl();
        }

        /// <summary>
        /// 由 <see cref="AreaCameraDirector"/> 在观察机位到位（或交给建造调度过渡）后调用。
        /// 本类不会自己因为「区域变成 Observation」就接管：那样会在区域切换的同一帧
        /// 和正在做机位过渡的导演抢相机。
        /// </summary>
        public void BeginControl()
        {
            if (areas != null && areas.CurrentArea != GameArea.Observation)
            {
                return;
            }

            activationPending = true;
        }

        private void HandleAreaChanged(GameArea area)
        {
            if (area != GameArea.Observation)
            {
                StopControl();
            }
        }

        private void LateUpdate()
        {
            if (areas == null || areas.CurrentArea != GameArea.Observation)
            {
                return;
            }

            if (!controlling && !activationPending)
            {
                return;
            }

            // 从建造模式退出时，BuildingCameraDirector 还在把相机过渡到观察机位；
            // 等它写完再接管，否则两边会各写一帧。
            if (buildingDirector != null &&
                (buildingDirector.IsInBuildMode || buildingDirector.IsTransitioning))
            {
                return;
            }

            // 放置 / 其他系统可能把共享动作关掉，这里兜底补回
            EnableActions();

            if (!controlling)
            {
                if (!TryInitializeFromCurrentPose())
                {
                    return;
                }

                controlling = true;
                activationPending = false;

                if (logCameraControlFlow)
                {
                    Debug.Log("[ObservationCameraController] 已接管相机：中键拖动旋转，右键拖动平移，滚轮缩放。" +
                              $"焦点 {pivot}，距离 {distance:0.##}，偏航 {yaw:0.##}，俯仰 {pitch:0.##}。");
                }
            }

            bool rotating = IsActionPressed(rotateAction);
            bool panning = IsActionPressed(panAction);

            if (!rotating)
            {
                // 松开中键 = 解除单轴锁定，下一次按下重新判断“最先移动的方向”
                ResetRotateAxisLock();
            }

            // 两个键同时按住时优先旋转，避免同一帧的指针位移被旋转和平移各消费一次
            if (rotating)
            {
                HandleRotate();
                panBlockedByUI = false;
            }
            else if (panning)
            {
                HandlePan();
                rotateBlockedByUI = false;
            }
            else
            {
                rotateBlockedByUI = false;
                panBlockedByUI = false;
            }

            HandleZoom();
            ApplyPose();
        }

        /// <summary>
        /// 从相机当前姿态反推轨道参数。进入观察模式时相机已经由 AreaCameraDirector
        /// 放到观察机位，所以这里不需要再读一份机位配置。
        /// </summary>
        private bool TryInitializeFromCurrentPose()
        {
            if (cameraTransform == null)
            {
                return false;
            }

            Vector3 forward = cameraTransform.forward;

            if (forward.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            forward.Normalize();

            yaw = Mathf.Repeat(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 360f);
            pitch = Mathf.Clamp(
                -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg,
                minPitch, maxPitch);

            if (!TryGetGroundFocus(forward, out pivot))
            {
                // 相机没有朝下看时退化为「相机正下方的地面点」，至少不会算出 NaN
                pivot = new Vector3(cameraTransform.position.x, groundY, cameraTransform.position.z);

                Debug.LogWarning("[ObservationCameraController] 相机前向与地面没有交点，" +
                                 "已把焦点放在相机正下方。旋转 / 平移 / 缩放仍可使用。", this);
            }

            pivot.y = groundY;
            homePivot = pivot;

            distance = Vector3.Distance(cameraTransform.position, pivot);

            if (distance < 0.0001f)
            {
                Debug.LogWarning("[ObservationCameraController] 相机几乎落在焦点上，" +
                                 $"已按最小距离 {minDistance} 处理。", this);
                distance = minDistance;
            }

            distance = Mathf.Clamp(distance, minDistance, maxDistance);

            rotateBlockedByUI = false;
            panBlockedByUI = false;

            // 让相机姿态和刚反推出来的状态完全一致：如果俯仰 / 距离被夹过，
            // 这一步会把相机吸附到合法的轨道位置，避免下一帧突然跳一下。
            ApplyPose();
            return true;
        }

        private bool TryGetGroundFocus(Vector3 forward, out Vector3 point)
        {
            point = default;

            if (cameraTransform == null)
            {
                return false;
            }

            Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
            Ray ray = new Ray(cameraTransform.position, forward);

            // enter <= 0 表示交点在相机背后
            if (!groundPlane.Raycast(ray, out float enter) || enter <= 0f)
            {
                return false;
            }

            point = ray.GetPoint(enter);
            return true;
        }

        private void HandleRotate()
        {
            if (rotateAction == null || lookAction == null)
            {
                return;
            }

            InputAction rotate = rotateAction.action;
            InputAction look = lookAction.action;

            if (rotate == null || look == null)
            {
                return;
            }

            if (rotate.WasPressedThisFrame())
            {
                rotateBlockedByUI = blockWhenPointerOverUI && IsPointerOverUI();
                ResetRotateAxisLock();
            }

            if (rotateBlockedByUI)
            {
                return;
            }

            Vector2 delta = look.ReadValue<Vector2>();

            if (delta.sqrMagnitude <= 0f)
            {
                return;
            }

            // 还没锁定方向：先累计位移，够阈值再按主轴锁定。
            // 锁定的这一帧把累计位移一起应用，避免开头几像素被吞掉。
            if (rotateAxisLock == RotateAxis.None)
            {
                rotateAxisProbeDelta += delta;

                float threshold = Mathf.Max(0f, rotateAxisLockThreshold);

                if (rotateAxisProbeDelta.sqrMagnitude < threshold * threshold)
                {
                    return;
                }

                rotateAxisLock = Mathf.Abs(rotateAxisProbeDelta.x) >= Mathf.Abs(rotateAxisProbeDelta.y)
                    ? RotateAxis.Horizontal
                    : RotateAxis.Vertical;

                delta = rotateAxisProbeDelta;
                rotateAxisProbeDelta = Vector2.zero;
            }

            // 整段拖动只响应最先移动的那一轴：另一轴怎么动都不转
            if (rotateAxisLock == RotateAxis.Horizontal)
            {
                yaw = Mathf.Repeat(yaw + delta.x * rotateSensitivity, 360f);
                return;
            }

            float pitchDelta = (invertRotateY ? delta.y : -delta.y) * rotateSensitivity;
            pitch = Mathf.Clamp(pitch + pitchDelta, minPitch, maxPitch);
        }

        private void ResetRotateAxisLock()
        {
            rotateAxisLock = RotateAxis.None;
            rotateAxisProbeDelta = Vector2.zero;
        }

        private void HandlePan()
        {
            if (panAction == null || lookAction == null)
            {
                return;
            }

            InputAction pan = panAction.action;
            InputAction look = lookAction.action;

            if (pan == null || look == null)
            {
                return;
            }

            if (pan.WasPressedThisFrame())
            {
                panBlockedByUI = blockWhenPointerOverUI && IsPointerOverUI();
            }

            if (panBlockedByUI)
            {
                return;
            }

            Vector2 delta = look.ReadValue<Vector2>();

            if (delta.sqrMagnitude <= 0f)
            {
                return;
            }

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 right = rotation * Vector3.right;
            Vector3 forward = rotation * Vector3.forward;
            right.y = 0f;
            forward.y = 0f;

            // 接近垂直俯视时前向在地面上的投影会退化，此时不做平移
            if (right.sqrMagnitude < 0.0001f || forward.sqrMagnitude < 0.0001f)
            {
                return;
            }

            right.Normalize();
            forward.Normalize();

            float deltaY = invertPanY ? -delta.y : delta.y;
            float worldPerPixel = GetWorldPerPixel();

            // 抓取地面式平移：鼠标向右拖，世界向右跟手，焦点向左走
            pivot -= (right * delta.x + forward * deltaY) * (worldPerPixel * panSensitivity);
            pivot.y = groundY;

            ClampPivotToHome();
        }

        private void HandleZoom()
        {
            if (scrollAction == null)
            {
                return;
            }

            InputAction scroll = scrollAction.action;

            if (scroll == null)
            {
                return;
            }

            Vector2 value = scroll.ReadValue<Vector2>();

            if (Mathf.Abs(value.y) < 0.01f)
            {
                return;
            }

            if (blockWhenPointerOverUI && IsPointerOverUI())
            {
                return;
            }

            // 鼠标滚轮在不同平台上报 ±1 或 ±120；这里统一折算成“一格”
            float notches = Mathf.Abs(value.y) >= 1f ? Mathf.Sign(value.y) : value.y;
            distance = Mathf.Clamp(distance - notches * zoomStepPerNotch, minDistance, maxDistance);
        }

        private void ApplyPose()
        {
            if (cameraTransform == null)
            {
                return;
            }

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 position = pivot - rotation * Vector3.forward * distance;
            cameraTransform.SetPositionAndRotation(position, rotation);
        }

        private float GetWorldPerPixel()
        {
            float screenHeight = Mathf.Max(1f, Screen.height);

            if (viewCamera == null)
            {
                // 没拿到 Camera 组件时按默认 60° 视场估算，至少让平移速度量级正确
                return 2f * distance * Mathf.Tan(30f * Mathf.Deg2Rad) / screenHeight;
            }

            return viewCamera.orthographic
                ? 2f * viewCamera.orthographicSize / screenHeight
                : 2f * distance * Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) / screenHeight;
        }

        private void ClampPivotToHome()
        {
            if (!clampPanToHome || maxPanDistance <= 0f)
            {
                return;
            }

            Vector3 offset = pivot - homePivot;
            offset.y = 0f;

            if (offset.sqrMagnitude <= maxPanDistance * maxPanDistance)
            {
                return;
            }

            // 超界时沿原方向拉回边界，保留玩家已经拖出去的方向感
            pivot = homePivot + offset.normalized * maxPanDistance;
            pivot.y = groundY;
        }

        private void StopControl()
        {
            controlling = false;
            activationPending = false;
            rotateBlockedByUI = false;
            panBlockedByUI = false;
            ResetRotateAxisLock();
        }

        private void EnableActions()
        {
            EnableAction(lookAction);
            EnableAction(rotateAction);
            EnableAction(panAction);
            EnableAction(scrollAction);
        }

        private static void EnableAction(InputActionReference reference)
        {
            if (reference == null || reference.action == null)
            {
                return;
            }

            if (!reference.action.enabled)
            {
                reference.action.Enable();
            }
        }

        private static bool IsActionPressed(InputActionReference reference)
        {
            return reference != null && reference.action != null && reference.action.IsPressed();
        }

        /// <summary>
        /// 指针是否落在 UI 上。实现统一在 <see cref="PointerOverUI"/>。
        /// </summary>
        private bool IsPointerOverUI()
        {
            Pointer pointer = Pointer.current;

            if (pointer == null)
            {
                return false;
            }

            return PointerOverUI.IsOver(pointer.position.ReadValue(), this, ref warnedAboutMissingEventSystem);
        }

        /// <summary>中键旋转当前锁定的轴。</summary>
        private enum RotateAxis
        {
            None,
            Horizontal,
            Vertical,
        }
    }
}
