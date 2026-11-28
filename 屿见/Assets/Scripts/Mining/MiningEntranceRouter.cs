using UnityEngine;
using UnityEngine.InputSystem;
using Yujian.Building;
using Yujian.Core;
using Yujian.Levels;

namespace Yujian.Mining
{
    /// <summary>
    /// 观察模式下点击岛上的矿洞入口，进入挖矿区。
    ///
    /// 只在 Observation 区域处理点击：进入挖矿区后本组件立刻放手，
    /// 点击交给 MiningInput，避免同一次点击同时触发「进矿区」和「打矿石」。
    ///
    /// 从关卡系统（阶段 1）开始，入口命中后先查 <see cref="LevelEntrance"/>：
    /// 有就交给它判断解锁等级并走黑场转场；没有才退回旧的 MiningEntrance 直接进矿区。
    /// </summary>
    [DisallowMultipleComponent]
    public class MiningEntranceRouter : MonoBehaviour
    {
        [Header("输入")]
        [Tooltip("用于屏幕坐标转射线的相机，通常与 MiningInput 的 View Camera 相同")]
        [SerializeField] private Camera viewCamera;

        [Header("拾取")]
        [Tooltip("参与入口检测的图层。矿洞入口在 Default 层时保持默认值即可")]
        [SerializeField] private LayerMask entranceLayers = ~0;

        [Tooltip("射线最大检测距离")]
        [SerializeField] private float maxRayDistance = 500f;

        [Header("依赖")]
        [Tooltip("区域控制器。观察模式与建筑模式下都允许通过入口进关卡")]
        [SerializeField] private GameAreaController areaController;

        [Tooltip("建筑管理器。建筑区正在放蓝图 / 填料时不抢这一次点击；留空会自动查找")]
        [SerializeField] private BuildingManager buildingManager;

        [Tooltip("建筑迁移。正在迁移建筑时不抢这一次点击；留空会自动查找")]
        [SerializeField] private BuildingMigration buildingMigration;

        [Header("调试")]
        [Tooltip("是否在 Console 打印进入矿洞的日志")]
        [SerializeField] private bool logClicks = true;

        private bool warnedAboutMissingEventSystem;
        private bool warnedAboutMissingSetup;

        /// <summary>
        /// 入口使用自己独立的点击动作。
        /// 不能和 MiningInput 共用同一个 InputAction：离开矿区时 MiningInput 会把它 Disable，
        /// 共用的话入口会在观察模式下一起失灵。
        /// </summary>
        private InputAction clickAction;

        private void Awake()
        {
            ResolveDependencies();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            clickAction = new InputAction("MiningEntranceClick", InputActionType.Button, "<Pointer>/press");
            clickAction.performed += HandleClick;
            clickAction.Enable();
        }

        private void OnDisable()
        {
            if (clickAction == null)
            {
                return;
            }

            clickAction.performed -= HandleClick;
            clickAction.Disable();
            clickAction.Dispose();
            clickAction = null;
        }

        private void HandleClick(InputAction.CallbackContext context)
        {
            ResolveDependencies();

            if (areaController == null || viewCamera == null)
            {
                WarnMissingSetupOnce();
                return;
            }

            // 观察模式与建筑模式都能点关卡模型进关；
            // 挖矿 / 商店里这一次点击属于各自的系统。
            if (!IsEntranceClickAllowed())
            {
                return;
            }

            if (Pointer.current == null)
            {
                return;
            }

            Vector2 screenPosition = Pointer.current.position.ReadValue();

            if (PointerOverUI.IsOver(screenPosition, this, ref warnedAboutMissingEventSystem))
            {
                return;
            }

            Ray ray = viewCamera.ScreenPointToRay(screenPosition);

            if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, entranceLayers,
                    QueryTriggerInteraction.Ignore))
            {
                return;
            }

            // 关卡入口优先：它负责解锁判断、黑场转场与暂停，旧的 MiningEntrance 只是它的执行器。
            LevelEntrance levelEntrance = hit.collider.GetComponentInParent<LevelEntrance>();

            if (levelEntrance != null)
            {
                if (logClicks)
                {
                    Debug.Log($"[LevelEntrance] 点击「{levelEntrance.name}」，交给关卡系统处理。", levelEntrance);
                }

                levelEntrance.TryEnter();
                return;
            }

            MiningEntrance entrance = hit.collider.GetComponentInParent<MiningEntrance>();

            if (entrance == null)
            {
                return;
            }

            if (logClicks)
            {
                Debug.Log($"[MiningEntrance] 点击「{entrance.name}」，进入挖矿区。", entrance);
            }

            entrance.Activate();
        }

        /// <summary>
        /// 当前区域的这一次点击能不能当作「点关卡入口」。
        /// 建筑区里如果正在放蓝图、填料或迁移建筑，左键属于建造操作，必须让开。
        /// </summary>
        private bool IsEntranceClickAllowed()
        {
            GameArea area = areaController.CurrentArea;

            if (area == GameArea.Observation)
            {
                return true;
            }

            if (area != GameArea.Building)
            {
                return false;
            }

            ResolveBuildingDependencies();

            if (buildingManager != null &&
                (buildingManager.IsPlacing || buildingManager.IsFilling))
            {
                return false;
            }

            if (buildingMigration != null && buildingMigration.IsActive)
            {
                return false;
            }

            return true;
        }

        /// <summary>编辑器向导调用，一次接好相机和区域控制器。</summary>
        public void ConfigureForSetup(Camera camera, GameAreaController controller)
        {
            viewCamera = camera;
            areaController = controller;
        }

        private void ResolveDependencies()
        {
            if (areaController == null)
            {
                areaController = FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);
            }

            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }

            ResolveBuildingDependencies();
        }

        private void ResolveBuildingDependencies()
        {
            if (buildingManager == null)
            {
                buildingManager = FindFirstObjectByType<BuildingManager>(FindObjectsInactive.Include);
            }

            if (buildingMigration == null)
            {
                buildingMigration = FindFirstObjectByType<BuildingMigration>(FindObjectsInactive.Include);
            }
        }

        private void WarnMissingSetupOnce()
        {
            if (warnedAboutMissingSetup)
            {
                return;
            }

            warnedAboutMissingSetup = true;
            Debug.LogWarning("[MiningEntranceRouter] 相机 / 区域控制器未接好，" +
                             "矿山点了不会进矿区。请运行菜单「屿见/挖矿/一键修复矿洞入口与照明」。", this);
        }
    }
}
