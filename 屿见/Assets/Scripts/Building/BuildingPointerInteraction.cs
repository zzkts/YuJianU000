using UnityEngine;
using UnityEngine.InputSystem;
using Yujian.Core;

namespace Yujian.Building
{
    /// <summary>
    /// 已建建筑的指针交互分发（策划案 3.3 / 3.4 的入口）。
    ///
    /// 同一次按下有两种结果，靠「按住时长」区分，所以必须由一个组件统一判定，
    /// 不能让迁移和升级各监听一次指针——那样短按会同时触发两套逻辑：
    ///   · 按住 >= holdSeconds：进入**迁移**（拖建筑走）；
    ///   · 短按松开：进入**升级**（选中这座建筑）；
    ///   · 点在空地 / 非本系统物体上：取消升级选中（策划案：点击别处退出）。
    ///
    /// 只在建筑区、且没有蓝图在场时工作；蓝图模式下点击归 <see cref="BuildingPlacement"/>。
    /// 全程不探测矿石，矿洞 / 建造区的挖矿输入本来就被 GameAreaController 关掉了。
    /// </summary>
    public class BuildingPointerInteraction : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("建筑管理器。建筑图层与「是否正在放蓝图」都从它读")]
        [SerializeField] private BuildingManager buildingManager;

        [Tooltip("建筑迁移。长按后交给它拖动")]
        [SerializeField] private BuildingMigration migration;

        [Tooltip("建筑升级。短按后交给它选中")]
        [SerializeField] private BuildingUpgrade upgrade;

        [Tooltip("用于拾取建筑的相机。留空则用 MainCamera")]
        [SerializeField] private Camera viewCamera;

        [Tooltip("区域状态。只在建筑区响应建筑点击；留空则任何区域都响应")]
        [SerializeField] private GameAreaController areas;

        [Header("交互")]
        [Tooltip("按住多久算长按（秒）。策划案要求这个值可调")]
        [SerializeField, Min(0.1f)] private float holdSeconds = 0.4f;

        [Tooltip("拾取建筑的最大距离。俯视机位到地面约 40，留足余量")]
        [SerializeField, Min(1f)] private float maxPickDistance = 200f;

        [Header("调试")]
        [SerializeField] private bool logInteractionFlow = false;

        private bool pressed;
        private bool pressedOnBuilding;
        private GameObject pressedBuilding;
        private float pressStartTime;
        private bool warnedAboutMissingEventSystem;

        private void Awake()
        {
            if (buildingManager == null)
            {
                Debug.LogError("[BuildingPointerInteraction] BuildingManager 未配置，建筑无法点击。", this);
            }

            if (migration == null)
            {
                Debug.LogError("[BuildingPointerInteraction] BuildingMigration 未配置，长按拖不动建筑。", this);
            }

            if (upgrade == null)
            {
                Debug.LogError("[BuildingPointerInteraction] BuildingUpgrade 未配置，点击建筑不会进入升级。", this);
            }

            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }
        }

        private void OnDisable()
        {
            ResetPress();
        }

        private void Update()
        {
            if (buildingManager == null)
            {
                return;
            }

            // 蓝图在场时归 BuildingPlacement，这里完全让开
            if (buildingManager.IsPlacing || !IsInBuildingArea())
            {
                ResetPress();
                return;
            }

            Pointer pointer = Pointer.current;

            if (pointer == null)
            {
                return;
            }

            Vector2 screenPosition = pointer.position.ReadValue();
            bool pendingConfirm = migration != null && migration.IsPendingConfirm;

            if (pointer.press.wasPressedThisFrame)
            {
                if (pendingConfirm)
                {
                    // 待确认时按住建筑 = 继续拖（确认被拒后不用先撤回再长按一次）。
                    // 按在按钮 / 面板上时不接管，交给 uGUI 的确认 / 撤回
                    if (!IsPointerOverUI(screenPosition) &&
                        TryPickBuilding(screenPosition, out GameObject pendingBuilding) &&
                        pendingBuilding == migration.Target)
                    {
                        pressed = true;
                        pressStartTime = Time.unscaledTime;
                        pressedOnBuilding = false;
                        pressedBuilding = null;
                        migration.ResumeDrag(screenPosition);
                    }
                }
                else
                {
                    HandlePress(screenPosition);
                }
            }

            if (!pressed)
            {
                return;
            }

            // 已经在迁移：每帧把建筑拖到指针下
            if (migration != null && migration.IsDragging)
            {
                migration.DragToScreen(screenPosition);
            }
            else if (pressedOnBuilding
                     && pressedBuilding != null
                     && pointer.press.isPressed
                     && Time.unscaledTime - pressStartTime >= holdSeconds)
            {
                // 长按达到阈值 → 进入迁移。把按下时的屏幕点一起传进去，
                // 迁移据此记住抓取偏移，建筑不会在长按瞬间跳到指针下。之后由上面的分支负责拖动
                if (migration != null && migration.Begin(pressedBuilding, screenPosition) && logInteractionFlow)
                {
                    Debug.Log($"[BuildingPointerInteraction] 长按「{pressedBuilding.name}」开始迁移。");
                }
            }

            if (pointer.press.wasReleasedThisFrame)
            {
                HandleRelease(screenPosition);
            }
        }

        private void HandlePress(Vector2 screenPosition)
        {
            // 按在按钮 / 面板上：交给 uGUI，不碰建筑
            if (IsPointerOverUI(screenPosition))
            {
                return;
            }

            pressed = true;
            pressStartTime = Time.unscaledTime;

            if (TryPickBuilding(screenPosition, out GameObject building))
            {
                pressedOnBuilding = true;
                pressedBuilding = building;
            }
            else
            {
                pressedOnBuilding = false;
                pressedBuilding = null;
            }
        }

        private void HandleRelease(Vector2 screenPosition)
        {
            bool wasDragging = migration != null && migration.IsDragging;
            bool wasReleasedOverUI = IsPointerOverUI(screenPosition);

            if (wasDragging)
            {
                // 拖完松手：建筑停在候选位置，等玩家点确认 / 撤回
                migration.EndDrag();
            }
            else if (pressedOnBuilding && pressedBuilding != null)
            {
                if (!wasReleasedOverUI && upgrade != null)
                {
                    upgrade.Select(pressedBuilding);

                    if (logInteractionFlow)
                    {
                        Debug.Log($"[BuildingPointerInteraction] 点击「{pressedBuilding.name}」进入升级。");
                    }
                }
            }
            else if (upgrade != null)
            {
                // 点在空地：策划案要求退出升级系统
                upgrade.Deselect();
            }

            ResetPress();
        }

        private bool IsInBuildingArea()
        {
            return areas == null || areas.CurrentArea == GameArea.Building;
        }

        /// <summary>屏幕坐标下是否有本系统管理的已建建筑。</summary>
        private bool TryPickBuilding(Vector2 screenPosition, out GameObject building)
        {
            building = null;

            if (viewCamera == null)
            {
                return false;
            }

            LayerMask mask = buildingManager.BuildingLayers;

            if (mask.value == 0)
            {
                return false;
            }

            Ray ray = viewCamera.ScreenPointToRay(screenPosition);

            if (!Physics.Raycast(ray, out RaycastHit hit, maxPickDistance, mask,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            PlacedBuildingInfo info = hit.collider.GetComponentInParent<PlacedBuildingInfo>();

            if (info == null || info.Data == null)
            {
                return false;
            }

            building = info.gameObject;
            return true;
        }

        private bool IsPointerOverUI(Vector2 screenPosition)
        {
            return PointerOverUI.IsOver(screenPosition, this, ref warnedAboutMissingEventSystem);
        }

        private void ResetPress()
        {
            pressed = false;
            pressedOnBuilding = false;
            pressedBuilding = null;
        }
    }
}
