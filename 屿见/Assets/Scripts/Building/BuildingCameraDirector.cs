using UnityEngine;

namespace Yujian.Building
{
    /// <summary>
    /// 建造模式的相机调度。职责：在「常规视角」与「建造视角」两个机位之间平滑过渡。
    ///
    /// 进入：只要场上有蓝图（玩家点了建筑图标 / BuildingManager.IsPlacing 为 true）就自动进入。
    /// 退出：**只能由玩家显式触发**（ExitBuildMode）。放置成功不会自动退出——
    ///       这是刻意设计：连摆多座建筑时镜头不会来回飞。
    ///
    /// 本类只读 BuildingManager 的 IsPlacing，并在退出时调用它的 CancelSelection()，
    /// 所以不需要改动阶段 3 已交付的任何脚本。
    ///
    /// 位置/旋转/时长/曲线全部是 Inspector 字段，不硬编码；
    /// 也可以用组件右键菜单「把相机当前位置记录为建筑模式机位」直接把摆好的镜头存下来。
    /// </summary>
    public class BuildingCameraDirector : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("要驱动的相机。留空则依次尝试：本物体上的 Camera → 带 MainCamera 标签的相机")]
        [SerializeField] private Transform cameraTransform;

        [Tooltip("建筑系统。用于判断是否该进入建造模式，以及退出时取消还没放置的蓝图")]
        [SerializeField] private BuildingManager buildingManager;

        [Tooltip("勾选后：场上一出现蓝图就自动进入建造视角，不必再单独点「进入建造」")]
        [SerializeField] private bool enterAutomatically = true;

        [Header("建筑模式机位")]
        [Tooltip("建造视角的相机世界坐标。默认值是按地图中心 (10.217, 0, -9.492) 算的一组俯视机位，" +
                 "建议在 Scene 视图里摆好镜头后，用组件右键菜单直接记录，不要手敲坐标")]
        [SerializeField] private Vector3 buildingModePosition = new Vector3(10.2f, 37f, -35.3f);

        [Tooltip("建造视角的相机欧拉角。俯角就是 X 分量，例如 55 表示向下俯视 55 度")]
        [SerializeField] private Vector3 buildingModeEulerAngles = new Vector3(55f, 0f, 0f);

        [Header("过渡")]
        [Tooltip("两个机位之间过渡的秒数。填 0 表示瞬间切换")]
        [SerializeField] private float transitionDuration = 0.6f;

        [Tooltip("过渡曲线。默认缓入缓出；首尾值为 0/1 即可，中间可以拉出加速或轻微回弹")]
        [SerializeField] private AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("调试")]
        [Tooltip("是否在 Console 打印进入/退出建造视角的日志（便于确认相机到底动没动）")]
        [SerializeField] private bool logCameraFlow = true;

        /// <summary>常规视角机位，Awake 时从相机当前姿态捕获，作为退出的目标。</summary>
        private Vector3 defaultPosition;
        private Quaternion defaultRotation;

        /// <summary>是否处于建造模式。注意它和「场上有蓝图」不是一回事：放置成功后蓝图没了，但仍停留在建造模式。</summary>
        private bool inBuildMode;

        private bool transitioning;
        private float transitionElapsed;
        private Vector3 fromPosition;
        private Quaternion fromRotation;
        private Vector3 toPosition;
        private Quaternion toRotation;

        /// <summary>当前是否处于建造模式。</summary>
        public bool IsInBuildMode => inBuildMode;

        /// <summary>当前是否正在两个机位之间过渡。</summary>
        public bool IsTransitioning => transitioning;

        private void Awake()
        {
            ResolveCameraTransform();

            if (cameraTransform == null)
            {
                Debug.LogError("[BuildingCameraDirector] 找不到相机，机位切换不会生效。" +
                               "请手动把 Main Camera 拖到 Camera Transform 字段。", this);
                return;
            }

            // 场景里相机的初始姿态就是「常规视角」，退出建造模式时回到这里
            defaultPosition = cameraTransform.position;
            defaultRotation = cameraTransform.rotation;

            if (Vector3.Distance(buildingModePosition, defaultPosition) < 0.01f &&
                Quaternion.Angle(Quaternion.Euler(buildingModeEulerAngles), defaultRotation) < 0.5f)
            {
                Debug.LogWarning("[BuildingCameraDirector] 建筑模式机位和常规机位几乎完全一样，" +
                                 "进入建造模式时相机看起来不会有任何移动。" +
                                 "请在 Scene 视图摆好镜头后用组件右键菜单记录建筑模式机位。", this);
            }
        }

        private void Update()
        {
            if (enterAutomatically && !inBuildMode && buildingManager != null && buildingManager.IsPlacing)
            {
                EnterBuildMode();
            }

            UpdateTransition();
        }

        /// <summary>
        /// 进入建造视角。已经在建造模式时重复调用不会重启动画。
        /// </summary>
        public void EnterBuildMode()
        {
            if (inBuildMode)
            {
                return;
            }

            inBuildMode = true;
            StartTransition(buildingModePosition, Quaternion.Euler(buildingModeEulerAngles), "进入建造视角");
        }

        /// <summary>
        /// 退出建造视角，回到 Awake 时记录的常规机位。
        ///
        /// 会顺手取消还没放置的蓝图：如果只移相机而不取消蓝图，
        /// 下一帧「有蓝图就自动进入」的逻辑会立刻把相机又推回去，画面会来回弹。
        /// </summary>
        public void ExitBuildMode()
        {
            if (buildingManager != null && buildingManager.IsPlacing)
            {
                buildingManager.CancelSelection();
            }

            if (!inBuildMode)
            {
                return;
            }

            inBuildMode = false;
            StartTransition(defaultPosition, defaultRotation, "退出建造视角，回到常规机位");
        }

        /// <summary>
        /// 开始一次过渡。起点取相机的**当前实际姿态**而不是上一次的目标，
        /// 这样在过渡途中被打断（例如刚进入就点退出）也能顺接，不会跳一下。
        /// </summary>
        private void StartTransition(Vector3 targetPosition, Quaternion targetRotation, string logMessage)
        {
            if (cameraTransform == null)
            {
                return;
            }

            fromPosition = cameraTransform.position;
            fromRotation = cameraTransform.rotation;
            toPosition = targetPosition;
            toRotation = targetRotation;
            transitionElapsed = 0f;

            bool alreadyThere = (fromPosition - toPosition).sqrMagnitude < 0.0001f
                                && Quaternion.Angle(fromRotation, toRotation) < 0.05f;

            if (transitionDuration <= 0f || alreadyThere)
            {
                cameraTransform.SetPositionAndRotation(toPosition, toRotation);
                transitioning = false;
            }
            else
            {
                transitioning = true;
            }

            if (logCameraFlow)
            {
                Debug.Log($"[BuildingCameraDirector] {logMessage}（过渡 {transitionDuration:0.##} 秒，" +
                          $"目标位置 {toPosition}）");
            }
        }

        private void UpdateTransition()
        {
            if (!transitioning || cameraTransform == null)
            {
                return;
            }

            transitionElapsed += Time.deltaTime;

            float progress = transitionDuration <= 0f
                ? 1f
                : Mathf.Clamp01(transitionElapsed / transitionDuration);

            // 曲线可以做回弹（值 >1 或 <0），所以用 Unclamped 版本，收尾时再精确吸附到目标。
            // 空曲线也要退化处理：Evaluate 在空曲线上恒返回 0，会让相机整个过渡期间一动不动
            float eased = transitionCurve != null && transitionCurve.length > 0
                ? transitionCurve.Evaluate(progress)
                : progress;

            cameraTransform.SetPositionAndRotation(
                Vector3.LerpUnclamped(fromPosition, toPosition, eased),
                Quaternion.SlerpUnclamped(fromRotation, toRotation, eased));

            if (progress >= 1f)
            {
                transitioning = false;
                cameraTransform.SetPositionAndRotation(toPosition, toRotation);
            }
        }

        /// <summary>按「手动指定 → 本物体上的 Camera → Camera.main」的顺序找相机。</summary>
        private void ResolveCameraTransform()
        {
            if (cameraTransform != null)
            {
                return;
            }

            Camera selfCamera = GetComponent<Camera>();

            if (selfCamera != null)
            {
                cameraTransform = selfCamera.transform;
                return;
            }

            Camera mainCamera = Camera.main;

            if (mainCamera != null)
            {
                cameraTransform = mainCamera.transform;
            }
        }

        [ContextMenu("把相机当前位置记录为建筑模式机位")]
        private void CaptureCurrentPoseAsBuildingMode()
        {
            ResolveCameraTransform();

            if (cameraTransform == null)
            {
                Debug.LogError("[BuildingCameraDirector] 找不到相机，无法记录机位。", this);
                return;
            }

            buildingModePosition = cameraTransform.position;
            buildingModeEulerAngles = cameraTransform.eulerAngles;

            Debug.Log($"[BuildingCameraDirector] 已记录建筑模式机位：位置 {buildingModePosition}，" +
                      $"旋转 {buildingModeEulerAngles}。");
        }

        [ContextMenu("调试：进入建造视角")]
        private void DebugEnterBuildMode()
        {
            EnterBuildMode();
        }

        [ContextMenu("调试：退出建造视角")]
        private void DebugExitBuildMode()
        {
            ExitBuildMode();
        }
    }
}
