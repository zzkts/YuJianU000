using UnityEngine;
using Yujian.Building;

namespace Yujian.Core
{
    /// <summary>
    /// 阶段 6：按区域移动相机。
    ///
    /// 四个区域的机位归谁管 —— 这是本类唯一需要说清楚的事：
    ///   · **挖矿 / 商店** → 本类自己按固定时长平滑插值过去（<see cref="transitionDuration"/> + <see cref="transitionCurve"/>，都在 Inspector 上）；
    ///   · **建筑** → 不碰机位，直接调 <see cref="BuildingCameraDirector.EnterBuildMode"/>，
    ///     用阶段 3 已经调好的建造视角。离开建筑区时调它的 ExitBuildMode()。
    ///
    /// 为什么不自己管建筑机位：建造机位连着「蓝图跟随鼠标」「重叠检测」那一整套，
    /// 阶段 3 已经验证过。再写一份只会变成两个导演抢同一个 Transform。
    /// 本类对 BuildingCameraDirector **只读它的 public 属性、只调它的 public 方法，一行都没改它**。
    ///
    /// 让位规则：只要 BuildingCameraDirector 处于建造模式或正在过渡，本类就完全不写相机
    /// （并把过渡起点重置为相机的当前姿态），免得两边各写一帧把镜头抖成筛子、
    /// 或者在它释放后从旧起点把镜头拽回去。
    ///
    /// 从建筑区切走时，本类把目标机位一起交给 BuildingCameraDirector.ExitBuildMode(position, rotation)，
    /// 由它一次过渡到位，中间不再经过「常规机位」——旧实现从建筑区去商店会明显多绕一下。
    /// 过渡期间本类照常让位，过渡结束后再接管（此时两者目标一致，不会抢镜头）。
    ///
    /// 观察模式（<see cref="GameArea.Observation"/>）：
    ///   · 本类只负责把相机送到「观察机位」，到位后调用
    ///     <see cref="ObservationCameraController.BeginControl"/> 把控制权交出去；
    ///   · 从建筑区过来时，机位直接交给 BuildingCameraDirector 一次过渡到位，
    ///     同时取消还没放置的蓝图（沿用 ExitBuildMode 的既有行为）。
    /// </summary>
    public class AreaCameraDirector : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("区域状态。切区域时它会广播，本类订阅它")]
        [SerializeField] private GameAreaController areas;

        [Tooltip("要驱动的相机。留空则依次尝试：本物体上的 Camera → 带 MainCamera 标签的相机")]
        [SerializeField] private Transform cameraTransform;

        [Tooltip("建造模式相机调度。建筑区的机位交给它，本类不动。留空会报错")]
        [SerializeField] private BuildingCameraDirector buildingDirector;

        [Tooltip("观察模式相机控制。本类把相机送到观察机位后，由它接管旋转 / 平移 / 缩放。留空会报错")]
        [SerializeField] private ObservationCameraController observationCamera;

        [Header("挖矿机位")]
        [Tooltip("挖矿区机位。默认值就是场景里 Main Camera 的原始姿态（(-20.93, 0.82, -12.9)，零旋转），" +
                 "正对着那一簇矿石。建议摆好镜头后用组件右键菜单记录，不要手敲")]
        [SerializeField] private Vector3 miningPosition = new Vector3(-20.93f, 0.82f, -12.9f);

        [Tooltip("挖矿区机位的欧拉角")]
        [SerializeField] private Vector3 miningEulerAngles = Vector3.zero;

        [Header("商店机位")]
        [Tooltip("商店区机位。⚠️ 世界里现在还没有任何商店实体（Shop 只是个放逻辑的空物体，" +
                 "位于 (-23.49, 0, -1.28)），所以这个默认值是**按那个位置推算的俯视机位**，不是实拍。" +
                 "以后在世界里摆好摊位，用组件右键菜单重记一次即可")]
        [SerializeField] private Vector3 shopPosition = new Vector3(-23.49f, 22f, -23.28f);

        [Tooltip("商店区机位的欧拉角。X = 45 表示俯视 45 度")]
        [SerializeField] private Vector3 shopEulerAngles = new Vector3(45f, 0f, 0f);

        [Header("观察机位")]
        [Tooltip("观察模式机位。默认值按海岛中心 (10.21735, 0, -9.49205) 推算：距离 130、俯仰 55°，" +
                 "能看全整座岛。建议在 Scene 视图摆好镜头后，用组件右键菜单记录，不要手敲")]
        [SerializeField] private Vector3 observationPosition = new Vector3(10.21735f, 106.49f, -84.06f);

        [Tooltip("观察模式机位的欧拉角。X = 55 表示俯视 55 度")]
        [SerializeField] private Vector3 observationEulerAngles = new Vector3(55f, 0f, 0f);

        [Header("过渡（文档 2.3「摄像机变化的时间」）")]
        [Tooltip("机位切换的过渡秒数。无论距离远近都在这个时间内平滑到位；填 0 表示瞬间切换。" +
                 "默认 0.6，与建造视角的过渡时长一致")]
        [SerializeField] private float transitionDuration = 0.6f;

        [Tooltip("过渡曲线。默认缓入缓出；首尾值为 0/1 即可，中间可以拉出加速感")]
        [SerializeField] private AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("进 Play Mode 时直接把相机放到起始区域的机位上（不播过渡动画）。" +
                 "关掉的话开场会从场景里手摆的位置飞过去")]
        [SerializeField] private bool snapOnStart = true;

        [Header("调试")]
        [Tooltip("是否在 Console 打印机位切换日志")]
        [SerializeField] private bool logCameraFlow = true;

        private Vector3 fromPosition;
        private Quaternion fromRotation;
        private Vector3 targetPosition;
        private Quaternion targetRotation;
        private float transitionElapsed;
        private bool hasTarget;
        private bool initialised;
        private GameArea appliedArea;
        private bool pendingObservationControl;

        /// <summary>建造模式（含过渡）期间，相机归 BuildingCameraDirector，本类让位。</summary>
        private bool CameraOwnedByBuildDirector =>
            buildingDirector != null && (buildingDirector.IsInBuildMode || buildingDirector.IsTransitioning);

        private void Awake()
        {
            ResolveCameraTransform();

            if (areas == null)
            {
                Debug.LogError("[AreaCameraDirector] GameAreaController 未配置，" +
                               "相机不会跟着底部导航动。请重新运行菜单「屿见/整合/配置 MVP 整合（阶段 6）」。", this);
            }

            if (cameraTransform == null)
            {
                Debug.LogError("[AreaCameraDirector] 找不到相机，机位切换不会生效。" +
                               "请手动把 Main Camera 拖到 Camera Transform 字段。", this);
            }

            if (buildingDirector == null)
            {
                Debug.LogError("[AreaCameraDirector] BuildingCameraDirector 未配置：" +
                               "点「建筑」时相机会原地不动。它挂在场景的 BuildingSystem 上。", this);
            }

            if (observationCamera == null)
            {
                Debug.LogError("[AreaCameraDirector] ObservationCameraController 未配置：" +
                               "退出系统后会切到观察区域，但相机不会响应旋转 / 平移 / 缩放。" +
                               "请重新运行菜单「屿见/场景/配置观察模式系统」。", this);
            }

            // 负数会让进度算出负值、镜头先往反方向退一段；统一按 0（瞬间切换）处理
            if (transitionDuration < 0f)
            {
                Debug.LogWarning($"[AreaCameraDirector] Transition Duration 配置为负数({transitionDuration})，" +
                                 "已按 0（瞬间切换）处理。", this);
                transitionDuration = 0f;
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
        }

        private void Update()
        {
            if (!hasTarget || cameraTransform == null)
            {
                return;
            }

            if (CameraOwnedByBuildDirector)
            {
                // 让位期间把过渡起点改成当前姿态：等建造调度释放后再从当前位置接着走，
                // 不会被旧起点拽回去，也不会带着旧进度冲出去
                fromPosition = cameraTransform.position;
                fromRotation = cameraTransform.rotation;
                transitionElapsed = 0f;
                return;
            }

            transitionElapsed += Time.deltaTime;

            float progress = transitionDuration <= 0f
                ? 1f
                : Mathf.Clamp01(transitionElapsed / transitionDuration);

            // 曲线允许回弹（>1 / <0），用 Unclamped 版本；收尾时再精确吸附到目标，
            // 空曲线也要退化处理（Evaluate 在空曲线上恒返回 0，会让镜头全程不动）
            float eased = transitionCurve != null && transitionCurve.length > 0
                ? transitionCurve.Evaluate(progress)
                : progress;

            cameraTransform.SetPositionAndRotation(
                Vector3.LerpUnclamped(fromPosition, targetPosition, eased),
                Quaternion.SlerpUnclamped(fromRotation, targetRotation, eased));

            if (progress < 1f)
            {
                return;
            }

            cameraTransform.SetPositionAndRotation(targetPosition, targetRotation);
            hasTarget = false;

            if (pendingObservationControl)
            {
                pendingObservationControl = false;
                observationCamera?.BeginControl();
            }
        }

        /// <summary>
        /// 从相机**当前实际姿态**开始一次定时过渡。起点不取上一次的目标，
        /// 这样在过渡途中被打断（例如刚到观察机位又点系统）也能顺接，不会跳一下。
        /// </summary>
        private void BeginTransition(Vector3 position, Quaternion rotation)
        {
            targetPosition = position;
            targetRotation = rotation;
            fromPosition = cameraTransform != null ? cameraTransform.position : position;
            fromRotation = cameraTransform != null ? cameraTransform.rotation : rotation;
            transitionElapsed = 0f;
            hasTarget = cameraTransform != null;
        }

        /// <summary>
        /// 区域变了。挖矿 / 商店自己走，建筑交给 BuildingCameraDirector，
        /// 观察模式先送到观察机位再把控制权交给 ObservationCameraController。
        /// </summary>
        private void HandleAreaChanged(GameArea area)
        {
            bool isFirstApplication = !initialised;
            initialised = true;

            if (area == GameArea.Observation)
            {
                EnterObservationView(isFirstApplication);
                return;
            }

            pendingObservationControl = false;

            if (area == GameArea.Building)
            {
                // 相机立刻交给建造调度；本类不再持有目标，免得它退完以后又被本类拽回来
                hasTarget = false;
                appliedArea = area;

                if (buildingDirector == null)
                {
                    Debug.LogError("[AreaCameraDirector] 没有 BuildingCameraDirector，" +
                                   "点「建筑」时相机不会切到建造视角。", this);
                    return;
                }

                buildingDirector.EnterBuildMode();

                if (logCameraFlow)
                {
                    Debug.Log("[AreaCameraDirector] 切到建筑区：机位交给 BuildingCameraDirector（建造视角）");
                }

                return;
            }

            Vector3 position = area == GameArea.Shop ? shopPosition : miningPosition;
            Quaternion rotation = Quaternion.Euler(area == GameArea.Shop ? shopEulerAngles : miningEulerAngles);

            // 从建筑区切走：先退出建造模式。它会顺带取消还没放置的蓝图，
            // 蓝图里已放的材料按决策 21 全部退回库存（不吞料）——这是用户 2026-10-08 拍板的 S2。
            //
            // 把本次的目标机位一起传进去：直接过渡到商店 / 挖矿机位，
            // 不再先退回常规机位再拐弯（旧实现在「建筑 → 商店」时会明显多绕一下）。
            if (appliedArea == GameArea.Building && buildingDirector != null)
            {
                buildingDirector.ExitBuildMode(position, rotation,
                    $"退出建造视角，直接前往{area.ToChineseName()}机位");
            }

            appliedArea = area;

            if (cameraTransform == null)
            {
                return;
            }

            bool snap = isFirstApplication && snapOnStart;

            if (snap)
            {
                cameraTransform.SetPositionAndRotation(position, rotation);
                hasTarget = false;

                if (logCameraFlow)
                {
                    Debug.Log($"[AreaCameraDirector] 开场直接落到{area.ToChineseName()}区域机位：位置 {position}");
                }

                return;
            }

            BeginTransition(position, rotation);

            if (logCameraFlow)
            {
                Debug.Log($"[AreaCameraDirector] 切到{area.ToChineseName()}区域：" +
                          $"目标位置 {position}，过渡 {transitionDuration:0.##} 秒");
            }
        }

        /// <summary>
        /// 进入观察模式：把相机送到观察机位，然后让 ObservationCameraController 接管输入。
        ///
        /// 观察模式不是导航里的第四个按钮，所以它没有「区域面板」；这里只处理机位。
        /// </summary>
        private void EnterObservationView(bool isFirstApplication)
        {
            hasTarget = false;
            pendingObservationControl = false;

            Vector3 position = observationPosition;
            Quaternion rotation = Quaternion.Euler(observationEulerAngles);

            // 从建筑区退出：让建造调度直接过渡到观察机位。
            // ExitBuildMode 会顺手取消还没放置的蓝图，避免「退出后下一帧又被自动拉回建造模式」。
            // 只有它确实还在建造 / 过渡时才走这条；否则下面照常由本类把相机送到观察机位。
            if (appliedArea == GameArea.Building && buildingDirector != null &&
                (buildingDirector.IsInBuildMode || buildingDirector.IsTransitioning))
            {
                appliedArea = GameArea.Observation;
                buildingDirector.ExitBuildMode(position, rotation, "退出建造视角，进入观察模式");

                // 建造调度还在过渡，观察控制器会等 IsTransitioning 变 false 后再初始化
                observationCamera?.BeginControl();

                if (logCameraFlow)
                {
                    Debug.Log("[AreaCameraDirector] 切到观察模式：建造调度正在把相机过渡到观察机位");
                }

                return;
            }

            appliedArea = GameArea.Observation;

            if (cameraTransform == null)
            {
                return;
            }

            bool snap = isFirstApplication && snapOnStart;

            if (snap)
            {
                cameraTransform.SetPositionAndRotation(position, rotation);
                hasTarget = false;
                observationCamera?.BeginControl();

                if (logCameraFlow)
                {
                    Debug.Log($"[AreaCameraDirector] 开场直接落到观察机位：位置 {position}");
                }

                return;
            }

            BeginTransition(position, rotation);
            pendingObservationControl = observationCamera != null;

            if (logCameraFlow)
            {
                Debug.Log($"[AreaCameraDirector] 切到观察模式：目标位置 {position}，" +
                          $"过渡 {transitionDuration:0.##} 秒，到位后交给 ObservationCameraController");
            }
        }

        /// <summary>按「手动指定 → 本物体上的 Camera → Camera.main」的顺序找相机。</summary>
        private void ResolveCameraTransform()
        {
            cameraTransform = CameraResolver.Resolve(cameraTransform, gameObject);
        }

        [ContextMenu("把相机当前位置记录为挖矿机位")]
        private void CaptureCurrentPoseAsMiningPose()
        {
            ResolveCameraTransform();

            if (cameraTransform == null)
            {
                Debug.LogError("[AreaCameraDirector] 找不到相机，无法记录机位。", this);
                return;
            }

            miningPosition = cameraTransform.position;
            miningEulerAngles = cameraTransform.eulerAngles;

            Debug.Log($"[AreaCameraDirector] 已记录挖矿机位：位置 {miningPosition}，旋转 {miningEulerAngles}。");
        }

        [ContextMenu("把相机当前位置记录为商店机位")]
        private void CaptureCurrentPoseAsShopPose()
        {
            ResolveCameraTransform();

            if (cameraTransform == null)
            {
                Debug.LogError("[AreaCameraDirector] 找不到相机，无法记录机位。", this);
                return;
            }

            shopPosition = cameraTransform.position;
            shopEulerAngles = cameraTransform.eulerAngles;

            Debug.Log($"[AreaCameraDirector] 已记录商店机位：位置 {shopPosition}，旋转 {shopEulerAngles}。");
        }

        [ContextMenu("把相机当前位置记录为观察机位")]
        private void CaptureCurrentPoseAsObservationPose()
        {
            ResolveCameraTransform();

            if (cameraTransform == null)
            {
                Debug.LogError("[AreaCameraDirector] 找不到相机，无法记录机位。", this);
                return;
            }

            observationPosition = cameraTransform.position;
            observationEulerAngles = cameraTransform.eulerAngles;

            Debug.Log($"[AreaCameraDirector] 已记录观察机位：位置 {observationPosition}，" +
                      $"旋转 {observationEulerAngles}。");
        }
    }
}
