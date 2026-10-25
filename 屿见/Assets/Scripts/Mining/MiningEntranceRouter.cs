using UnityEngine;
using UnityEngine.InputSystem;
using Yujian.Core;

namespace Yujian.Mining
{
    /// <summary>
    /// 观察模式下点击岛上的矿洞入口，进入挖矿区。
    ///
    /// 只在 Observation 区域处理点击：进入挖矿区后本组件立刻放手，
    /// 点击交给 MiningInput，避免同一次点击同时触发「进矿区」和「打矿石」。
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
        [Tooltip("区域控制器。只有处于观察模式时才允许通过入口进矿区")]
        [SerializeField] private GameAreaController areaController;

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

        private void OnEnable()
        {
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
            if (areaController == null || viewCamera == null)
            {
                WarnMissingSetupOnce();
                return;
            }

            // 只有「站在岛上观察」时，矿山才是可点击的入口。
            // 在挖矿 / 建筑 / 商店里，这一次点击属于各自的系统。
            if (areaController.CurrentArea != GameArea.Observation)
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

        /// <summary>编辑器向导调用，一次接好相机和区域控制器。</summary>
        public void ConfigureForSetup(Camera camera, GameAreaController controller)
        {
            viewCamera = camera;
            areaController = controller;
        }

        private void WarnMissingSetupOnce()
        {
            if (warnedAboutMissingSetup)
            {
                return;
            }

            warnedAboutMissingSetup = true;
            Debug.LogWarning("[MiningEntranceRouter] 相机 / 区域控制器未接好，" +
                             "矿山点了不会进矿区。请运行菜单「屿见/挖矿/配置矿洞入口（阶段 11）」。", this);
        }
    }
}