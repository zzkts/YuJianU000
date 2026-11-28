using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Yujian.Core;

namespace Yujian.Building
{
    /// <summary>
    /// 建造模式下的相机旋转/平移/缩放。
    ///
    /// 相机归属：本类只在 <see cref="BuildingCameraDirector.IsInBuildMode"/> 为 true 且
    /// 不处于机位过渡时写相机。过渡期间让位给 BuildingCameraDirector，
    /// 非建筑区则由 AreaCameraDirector 负责。这样三个脚本不会同帧抢同一个 Transform。
    ///
    /// 操作：
    ///   · 按住鼠标中键拖动 → 绕地面焦点旋转（和观察模式一致：按下后锁定最先移动的那一轴）；
    ///   · 按住鼠标右键拖动 → 平移（左键保留给蓝图拖动 / ×/√，绝不复用）；
    ///   · 滚轮 → 沿相机前向缩放。
    /// 本轮不做触屏。
    ///
    /// 本类不负责进入/退出建造模式，也不读 BuildingManager 的状态。
    /// </summary>
    public class BuildingCameraController : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("建造模式相机调度。本类只在它处于建造模式且不在过渡时控制相机。留空会报错")]
        [SerializeField] private BuildingCameraDirector cameraDirector;

        [Tooltip("建筑网格。平移焦点会被限制在网格世界范围内；留空则只限制缩放距离")]
        [SerializeField] private BuildingGrid grid;

        [Tooltip("要驱动的相机。留空则依次尝试：本物体上的 Camera → 带 MainCamera 标签的相机")]
        [SerializeField] private Transform cameraTransform;

        [Header("输入")]
        [Tooltip("指针位移。复用 InputSystem_Actions 的 Player/Look（<Pointer>/delta）")]
        [SerializeField] private InputActionReference lookAction;

        [Tooltip("按住哪个键旋转。复用 InputSystem_Actions 的 UI/MiddleClick。留空会从 Pan 所在的同一份 InputActionAsset 里自动找")]
        [SerializeField] private InputActionReference rotateAction;

        [Tooltip("按住哪个键平移。复用 InputSystem_Actions 的 UI/RightClick。不要用左键")]
        [SerializeField] private InputActionReference panAction;

        [Tooltip("滚轮缩放。复用 InputSystem_Actions 的 UI/ScrollWheel")]
        [SerializeField] private InputActionReference scrollAction;

        [Tooltip("指针在 UI 上时不响应旋转/平移/缩放")]
        [SerializeField] private bool blockWhenPointerOverUI = true;

        [Header("旋转（中键）")]
        [Tooltip("旋转灵敏度：指针每移动 1 像素转多少度")]
        [SerializeField] private float rotateSensitivity = 0.25f;

        [Tooltip("勾选后上下拖动方向反转")]
        [SerializeField] private bool invertRotateY = false;

        [Tooltip("单轴锁定的判定阈值（像素）")]
        [SerializeField] private float rotateAxisLockThreshold = 3f;

        [Tooltip("俯仰角下限（度）。越小越接近水平视角")]
        [SerializeField] private float minPitch = 15f;

        [Tooltip("俯仰角上限（度）。不要到 90，否则平移方向会退化")]
        [SerializeField] private float maxPitch = 85f;

        [Header("平移")]
        [Tooltip("平移灵敏度，1 = 指针下的地面大致跟手")]
        [SerializeField] private float panSensitivity = 1f;

        [Tooltip("勾选后上下拖动方向反转")]
        [SerializeField] private bool invertPanY = false;

        [Tooltip("平移焦点距网格边界的最小距离，防止镜头贴边")]
        [SerializeField] private float edgePadding = 4f;

        [Tooltip("是否把地面焦点限制在网格范围内")]
        [SerializeField] private bool clampToGridBounds = true;

        [Header("缩放")]
        [Tooltip("滚轮每格改变的距离（世界单位）")]
        [SerializeField] private float zoomStepPerNotch = 4f;

        [Tooltip("相机到地面焦点的最小距离")]
        [SerializeField] private float minDistance = 20f;

        [Tooltip("相机到地面焦点的最大距离")]
        [SerializeField] private float maxDistance = 100f;

        [Header("调试")]
        [Tooltip("是否在 Console 打印进入/退出相机控制的日志")]
        [SerializeField] private bool logCameraControlFlow = true;

        [Tooltip("进入建造视角后，在左上角显示中键 / 右键 / 滚轮的操作提示")]
        [SerializeField] private bool showCameraControlHint = true;

        /// <summary>UI 射线结果的复用缓冲。</summary>

        // 地面焦点 + 相机相对焦点的偏移。平移只改焦点，缩放只改偏移长度，旋转改偏移方向。
        private Vector3 focusPoint;
        private Vector3 cameraOffset;
        private float yaw;
        private float pitch;
        private Camera viewCamera;
        private bool initialized;
        private bool wasControlling;

        // 中键旋转的单轴锁定：按下后先累计位移，超过阈值再按主轴锁定，松开中键复位。
        private RotateAxis rotateAxisLock = RotateAxis.None;
        private Vector2 rotateAxisProbeDelta;

        // 一次右键拖动如果从 UI 上开始，整个拖动过程都不平移，避免拖出 UI 后镜头突然跳一下
        private bool panBlockedByUI;
        private bool rotateBlockedByUI;

        private bool warnedAboutMissingEventSystem;
        private bool warnedAboutBadZoomRange;
        private GameObject cameraHintRoot;

        private bool CanControlCamera =>
            cameraDirector != null &&
            cameraDirector.IsInBuildMode &&
            !cameraDirector.IsTransitioning;

        private void Awake()
        {
            ResolveCameraTransform();
            viewCamera = cameraTransform != null ? cameraTransform.GetComponent<Camera>() : null;
            ResolveRotateAction();

            if (cameraDirector == null)
            {
                Debug.LogError("[BuildingCameraController] BuildingCameraDirector 未配置，" +
                               "本组件永远不会写相机。请把 BuildingSystem 上的它拖到该字段。", this);
            }

            if (cameraTransform == null)
            {
                Debug.LogError("[BuildingCameraController] 找不到相机，" +
                               "建造模式内的旋转/平移/缩放不会生效。请手动指定 Camera Transform。", this);
            }

            if (lookAction == null || rotateAction == null || panAction == null || scrollAction == null)
            {
                Debug.LogError("[BuildingCameraController] 输入动作未配置完整（Look / Rotate / Pan / Scroll），" +
                               "缺失的动作不会响应。请重新运行菜单「屿见/建筑/配置建筑网格与建造相机（阶段 7）」。", this);
            }

            if (minDistance <= 0.1f)
            {
                Debug.LogWarning($"[BuildingCameraController] Min Distance 为 {minDistance}，已按 0.1 处理。", this);
                minDistance = 0.1f;
            }

            if (maxDistance < minDistance)
            {
                Debug.LogWarning($"[BuildingCameraController] Max Distance({maxDistance}) 小于 Min Distance({minDistance})，" +
                                 "已把最大值抬到最小值。", this);
                maxDistance = minDistance;
            }

            BuildCameraControlHint();
        }

        private void OnEnable()
        {
            EnableActions();
        }

        private void OnDisable()
        {
            // 这里**故意不调用** Disable()：InputAction.Enable/Disable 不是引用计数的，
            // 本组件一关就会把共享同一动作的其他系统一起关掉（见开发进度.md 踩坑 1）。
            // 本组件被禁用时最多是不再写相机，动作留给其他消费者自己管。
            initialized = false;
            wasControlling = false;
            panBlockedByUI = false;
            rotateBlockedByUI = false;
            ResetRotateAxisLock();
            SetCameraHintVisible(false);
        }

        private void OnDestroy()
        {
            if (cameraHintRoot != null)
            {
                Destroy(cameraHintRoot);
            }
        }

        private void LateUpdate()
        {
            // 过渡由 BuildingCameraDirector 在 Update 里插值，本类必须完全让开
            if (!CanControlCamera)
            {
                if (wasControlling && logCameraControlFlow)
                {
                    Debug.Log("[BuildingCameraController] 已停止控制相机（退出建造模式或开始机位过渡）。");
                }

                wasControlling = false;
                initialized = false;
                panBlockedByUI = false;
                rotateBlockedByUI = false;
                ResetRotateAxisLock();
                SetCameraHintVisible(false);
                return;
            }

            SetCameraHintVisible(showCameraControlHint);

            // 放置期间 MiningInput 可能会把共享动作关掉，这里兜底补回
            EnableActions();

            if (!initialized)
            {
                if (!TryInitializeFocus())
                {
                    return;
                }

                wasControlling = true;

                if (logCameraControlFlow)
                {
                    Debug.Log("[BuildingCameraController] 已接管相机：中键拖动旋转，右键拖动平移，滚轮缩放。" +
                              $"焦点 {focusPoint}，距离 {cameraOffset.magnitude:0.##}。");
                }
            }

            bool rotating = IsActionPressed(rotateAction);
            bool panning = IsActionPressed(panAction);

            if (!rotating)
            {
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
        /// 建筑模式的操作提示。独立 Canvas 避免受建筑栏显隐影响；
        /// 所有图形都不接收射线，不会挡住建筑点击。
        /// </summary>
        private void BuildCameraControlHint()
        {
            if (!showCameraControlHint || cameraHintRoot != null)
            {
                return;
            }

            cameraHintRoot = new GameObject(
                "BuildingCameraControlHint",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));

            Canvas canvas = cameraHintRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 150;

            CanvasScaler scaler = cameraHintRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.layer = 5;
            panel.transform.SetParent(cameraHintRoot.transform, false);

            RectTransform panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(24f, -24f);
            panelRect.sizeDelta = new Vector2(278f, 118f);

            Image background = panel.GetComponent<Image>();
            background.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            background.type = Image.Type.Sliced;
            background.color = new Color(0.04f, 0.05f, 0.07f, 0.84f);
            background.raycastTarget = false;

            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 12, 12);
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            Text title = CreateHintText(panel.transform, "Title", "视角操作", 18,
                                        new Color(1f, 0.86f, 0.48f, 1f));
            title.fontStyle = FontStyle.Bold;

            CreateHintText(panel.transform, "Body",
                "中键拖动    旋转\n右键拖动    平移\n滚轮        缩放",
                17, new Color(0.92f, 0.94f, 0.97f, 1f));

            cameraHintRoot.SetActive(false);
        }

        private static Text CreateHintText(Transform parent, string name, string content,
                                           int fontSize, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);

            Text text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.lineSpacing = 1.15f;
            return text;
        }

        private void SetCameraHintVisible(bool visible)
        {
            if (cameraHintRoot == null || cameraHintRoot.activeSelf == visible)
            {
                return;
            }

            cameraHintRoot.SetActive(visible);
        }

        /// <summary>
        /// 第一次获得控制时，用相机前向与地面求交得到焦点，并记录相机相对焦点的偏移。
        /// 之后平移只改焦点、缩放只改偏移长度、旋转只改偏移方向。
        /// </summary>
        private bool TryInitializeFocus()
        {
            if (cameraTransform == null)
            {
                return false;
            }

            float planeY = grid != null ? grid.GridPlaneY : 0f;

            if (!TryGetGroundFocus(planeY, out focusPoint))
            {
                // 相机没有朝下看时退化为“相机正下方的地面点”，至少不会算出 NaN
                focusPoint = new Vector3(cameraTransform.position.x, planeY, cameraTransform.position.z);

                Debug.LogWarning("[BuildingCameraController] 相机前向与地面没有交点，" +
                                 "已把焦点放在相机正下方。平移/缩放仍可使用。", this);
            }

            cameraOffset = cameraTransform.position - focusPoint;

            if (cameraOffset.sqrMagnitude < 0.0001f)
            {
                Debug.LogError("[BuildingCameraController] 相机几乎落在地面上，无法建立缩放偏移。", this);
                return false;
            }

            Vector3 forward = cameraTransform.forward;

            if (forward.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            forward.Normalize();
            yaw = Mathf.Repeat(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 360f);
            pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg,
                                minPitch, maxPitch);

            float distance = Mathf.Clamp(cameraOffset.magnitude, minDistance, maxDistance);
            cameraOffset = Quaternion.Euler(pitch, yaw, 0f) * Vector3.back * distance;

            rotateBlockedByUI = false;
            panBlockedByUI = false;
            ResetRotateAxisLock();

            initialized = true;
            return true;
        }

        private bool TryGetGroundFocus(float planeY, out Vector3 point)
        {
            point = default;

            if (cameraTransform == null)
            {
                return false;
            }

            Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);

            // enter <= 0 表示交点在相机背后
            if (!groundPlane.Raycast(ray, out float enter) || enter <= 0f)
            {
                return false;
            }

            point = ray.GetPoint(enter);
            return true;
        }

        private void HandlePan()
        {
            if (panAction == null || lookAction == null || cameraTransform == null)
            {
                return;
            }

            InputAction pan = panAction.action;

            if (pan == null)
            {
                return;
            }

            if (pan.WasPressedThisFrame())
            {
                panBlockedByUI = blockWhenPointerOverUI && IsPointerOverUI();
            }

            if (!pan.IsPressed())
            {
                panBlockedByUI = false;
                return;
            }

            if (panBlockedByUI)
            {
                return;
            }

            InputAction look = lookAction.action;

            if (look == null)
            {
                return;
            }

            Vector2 delta = look.ReadValue<Vector2>();

            if (delta.sqrMagnitude <= 0f)
            {
                return;
            }

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 right = Vector3.ProjectOnPlane(rotation * Vector3.right, Vector3.up);
            Vector3 forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);

            // 相机完全垂直向下时，前向在地面上的投影为零，无法判断平移方向
            if (right.sqrMagnitude < 0.0001f || forward.sqrMagnitude < 0.0001f)
            {
                return;
            }

            right.Normalize();
            forward.Normalize();

            if (viewCamera == null)
            {
                return;
            }

            float distance = cameraOffset.magnitude;
            float screenHeight = Mathf.Max(1f, Screen.height);

            // 透视：该距离处一像素对应的世界尺寸；正交：直接由 orthographicSize 换算
            float worldPerPixel = viewCamera.orthographic
                ? 2f * viewCamera.orthographicSize / screenHeight
                : 2f * distance * Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) / screenHeight;

            float deltaY = invertPanY ? -delta.y : delta.y;

            // 抓取地面式平移：鼠标向右拖，世界向右跟手，相机焦点向左走
            focusPoint -= (right * delta.x + forward * deltaY) * (worldPerPixel * panSensitivity);

            ClampFocusToGrid();
        }

        private void HandleZoom()
        {
            if (scrollAction == null || cameraTransform == null)
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
            float distance = cameraOffset.magnitude;

            if (distance < 0.0001f)
            {
                return;
            }

            if (!warnedAboutBadZoomRange && minDistance > maxDistance)
            {
                warnedAboutBadZoomRange = true;
                Debug.LogWarning("[BuildingCameraController] Min Distance 大于 Max Distance，缩放会被夹死。", this);
            }

            float targetDistance = Mathf.Clamp(
                distance - notches * zoomStepPerNotch, minDistance, maxDistance);

            cameraOffset = cameraOffset / distance * targetDistance;
        }

        /// <summary>中键拖动绕焦点旋转。按下后按最先移动的主轴锁定，整段拖动只响应那一轴。</summary>
        private void HandleRotate()
        {
            if (rotateAction == null || lookAction == null || cameraTransform == null)
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

            if (rotateAxisLock == RotateAxis.Horizontal)
            {
                yaw = Mathf.Repeat(yaw + delta.x * rotateSensitivity, 360f);
            }
            else
            {
                float pitchDelta = (invertRotateY ? delta.y : -delta.y) * rotateSensitivity;
                pitch = Mathf.Clamp(pitch + pitchDelta, minPitch, maxPitch);
            }

            cameraOffset = Quaternion.Euler(pitch, yaw, 0f) * Vector3.back * cameraOffset.magnitude;
        }

        private void ResetRotateAxisLock()
        {
            rotateAxisLock = RotateAxis.None;
            rotateAxisProbeDelta = Vector2.zero;
        }

        private void ApplyPose()
        {
            if (cameraTransform == null)
            {
                return;
            }

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            cameraOffset = rotation * Vector3.back * cameraOffset.magnitude;
            cameraTransform.SetPositionAndRotation(focusPoint + cameraOffset, rotation);
        }

        /// <summary>
        /// 把地面焦点夹在网格范围内。夹的是焦点，不是相机位置：
        /// 高俯角下相机本来就在焦点后上方，夹相机位置会让镜头被无谓地推近。
        /// </summary>
        private void ClampFocusToGrid()
        {
            if (!clampToGridBounds || grid == null)
            {
                return;
            }

            float padding = Mathf.Max(0f, edgePadding);
            Vector3 min = grid.WorldMin;
            Vector3 max = grid.WorldMax;

            focusPoint.x = ClampAxis(focusPoint.x, min.x, max.x, padding);
            focusPoint.z = ClampAxis(focusPoint.z, min.z, max.z, padding);
            focusPoint.y = grid.GridPlaneY;
        }

        private static float ClampAxis(float value, float min, float max, float padding)
        {
            // 网格比两倍 padding 还小时不做内缩，直接夹在中心，避免 Min > Max
            if (max - min <= padding * 2f)
            {
                return (min + max) * 0.5f;
            }

            return Mathf.Clamp(value, min + padding, max - padding);
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
        /// 场景里的旧版接线可能还没有 rotateAction。这里从 Pan 所在的同一份 InputActionAsset
        /// 找 UI/MiddleClick，避免玩家必须手动补线才能旋转。
        /// </summary>
        private void ResolveRotateAction()
        {
            if (rotateAction != null)
            {
                return;
            }

            InputActionMap map = panAction != null && panAction.action != null
                ? panAction.action.actionMap
                : null;
            InputActionAsset asset = map != null ? map.asset : null;

            if (asset == null)
            {
                return;
            }

            InputAction action = asset.FindAction("UI/MiddleClick", false);

            if (action == null)
            {
                Debug.LogWarning("[BuildingCameraController] 找不到 UI/MiddleClick，" +
                                 "建造模式内无法用中键旋转视角。请手动指定 Rotate Action。", this);
                return;
            }

            rotateAction = InputActionReference.Create(action);
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

        /// <summary>按「手动指定 → 本物体上的 Camera → Camera.main」的顺序找相机。</summary>
        private void ResolveCameraTransform()
        {
            cameraTransform = CameraResolver.Resolve(cameraTransform, gameObject);
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
