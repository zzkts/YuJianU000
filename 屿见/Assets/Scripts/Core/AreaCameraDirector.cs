using UnityEngine;
using Yujian.Building;

namespace Yujian.Core
{
    /// <summary>
    /// 阶段 6：按区域移动相机。
    ///
    /// 三个区域的机位归谁管 —— 这是本类唯一需要说清楚的事：
    ///   · **挖矿 / 商店** → 本类自己平滑插值过去（<see cref="moveSpeed"/> + <see cref="smoothTime"/>，都在 Inspector 上）；
    ///   · **建筑** → 不碰机位，直接调 <see cref="BuildingCameraDirector.EnterBuildMode"/>，
    ///     用阶段 3 已经调好的建造视角。离开建筑区时调它的 ExitBuildMode()。
    ///
    /// 为什么不自己管建筑机位：建造机位连着「蓝图跟随鼠标」「重叠检测」那一整套，
    /// 阶段 3 已经验证过。再写一份只会变成两个导演抢同一个 Transform。
    /// 本类对 BuildingCameraDirector **只读它的 public 属性、只调它的 public 方法，一行都没改它**。
    ///
    /// 让位规则：只要 BuildingCameraDirector 处于建造模式或正在过渡，本类就完全不写相机
    /// （连速度都清零），免得两边各写一帧把镜头抖成筛子。
    ///
    /// ⚠️ 已知的小瑕疵：从建筑区直接切到商店区时，BuildingCameraDirector 会先花
    /// <c>transitionDuration</c>（默认 0.6 秒）退回「常规机位」，本类让位到它退完再动身，
    /// 所以镜头会先经过挖矿视角再拐向商店。想减轻：把那个 transitionDuration 调小一点。
    /// 从建筑区切回挖矿区则完全无感——常规机位本来就等于挖矿机位（见下）。
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

        [Header("过渡（需求三的两个参数）")]
        [Tooltip("相机移动的**最大**速度（单位 / 秒）。数值越大越快，太小会让镜头像在爬")]
        [SerializeField] private float moveSpeed = 25f;

        [Tooltip("平滑时间（秒）。越大越「软」、启动与刹车越慢；越小越干脆。" +
                 "位置用 Vector3.SmoothDamp，旋转用同一条时间常数的指数平滑")]
        [SerializeField] private float smoothTime = 0.35f;

        [Tooltip("进 Play Mode 时直接把相机放到起始区域的机位上（不播过渡动画）。" +
                 "关掉的话开场会从场景里手摆的位置飞过去")]
        [SerializeField] private bool snapOnStart = true;

        [Header("调试")]
        [Tooltip("是否在 Console 打印机位切换日志")]
        [SerializeField] private bool logCameraFlow = true;

        private Vector3 velocity;
        private Vector3 targetPosition;
        private Quaternion targetRotation;
        private bool hasTarget;
        private bool initialised;
        private GameArea appliedArea;

        /// <summary>建造模式（含过渡）期间，相机归 BuildingCameraDirector，本类让位。</summary>
        private bool CameraOwnedByBuildDirector =>
            buildingDirector != null && (buildingDirector.IsInBuildMode || buildingDirector.IsTransitioning);

        private void Awake()
        {
            ResolveCameraTransform();

            if (areas == null)
            {
                Debug.LogError("[AreaCameraDirector] GameAreaController 未配置，" +
                               "相机不会跟着底部导航动。请重新运行菜单「屿见/配置 MVP 整合（阶段 6）」。", this);
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

            // 两个平滑参数为 0 或负数会让 SmoothDamp 算出 NaN / 把镜头钉死在原地，这里先夹住
            if (moveSpeed <= 0f)
            {
                Debug.LogWarning($"[AreaCameraDirector] Move Speed 配置为非正数({moveSpeed})，已按 25 处理。" +
                                 "0 会让相机永远到不了目标机位。", this);
                moveSpeed = 25f;
            }

            if (smoothTime <= 0f)
            {
                Debug.LogWarning($"[AreaCameraDirector] Smooth Time 配置为非正数({smoothTime})，已按 0.01 处理。" +
                                 "0 会让 SmoothDamp 除以 0。", this);
                smoothTime = 0.01f;
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
                // 让位期间连速度也清掉：否则等它退完，本类会带着一大截旧速度冲出去
                velocity = Vector3.zero;
                return;
            }

            if (ReachedTarget())
            {
                // 收尾精确吸附，避免浮点误差留下的几毫米抖动
                cameraTransform.SetPositionAndRotation(targetPosition, targetRotation);
                velocity = Vector3.zero;
                hasTarget = false;
                return;
            }

            Vector3 position = Vector3.SmoothDamp(cameraTransform.position, targetPosition,
                                                  ref velocity, smoothTime, moveSpeed, Time.deltaTime);

            // 旋转用同一条时间常数的指数平滑：smoothTime 越大越慢，和位置的观感一致
            float rotationBlend = 1f - Mathf.Exp(-Time.deltaTime / smoothTime);
            Quaternion rotation = Quaternion.Slerp(cameraTransform.rotation, targetRotation, rotationBlend);

            cameraTransform.SetPositionAndRotation(position, rotation);
        }

        private bool ReachedTarget()
        {
            return (cameraTransform.position - targetPosition).sqrMagnitude < 0.0001f
                   && Quaternion.Angle(cameraTransform.rotation, targetRotation) < 0.05f;
        }

        /// <summary>
        /// 区域变了。挖矿 / 商店自己走，建筑交给 BuildingCameraDirector。
        /// </summary>
        private void HandleAreaChanged(GameArea area)
        {
            bool isFirstApplication = !initialised;
            initialised = true;

            if (area == GameArea.Building)
            {
                // 相机立刻交给建造调度；本类不再持有目标，免得它退完以后又被本类拽回来
                hasTarget = false;
                velocity = Vector3.zero;
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

            // 从建筑区切走：先退出建造模式。它会顺带取消还没放置的蓝图，
            // 蓝图里已放的材料按决策 21 全部退回库存（不吞料）——这是用户 2026-10-08 拍板的 S2
            if (appliedArea == GameArea.Building && buildingDirector != null)
            {
                buildingDirector.ExitBuildMode();
            }

            appliedArea = area;

            Vector3 position = area == GameArea.Shop ? shopPosition : miningPosition;
            Quaternion rotation = Quaternion.Euler(area == GameArea.Shop ? shopEulerAngles : miningEulerAngles);

            if (cameraTransform == null)
            {
                return;
            }

            bool snap = isFirstApplication && snapOnStart;

            targetPosition = position;
            targetRotation = rotation;
            velocity = Vector3.zero;

            if (snap)
            {
                cameraTransform.SetPositionAndRotation(targetPosition, targetRotation);
                hasTarget = false;

                if (logCameraFlow)
                {
                    Debug.Log($"[AreaCameraDirector] 开场直接落到{area.ToChineseName()}区域机位：位置 {targetPosition}");
                }

                return;
            }

            hasTarget = true;

            if (logCameraFlow)
            {
                Debug.Log($"[AreaCameraDirector] 切到{area.ToChineseName()}区域：" +
                          $"目标位置 {targetPosition}，最大速度 {moveSpeed}，平滑时间 {smoothTime}");
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
    }
}
