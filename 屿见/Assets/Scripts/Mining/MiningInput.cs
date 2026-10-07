using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Yujian.Player;

namespace Yujian.Mining
{
    /// <summary>
    /// 挖矿输入。
    /// 职责：接收点击输入 → 射线找到矿石 → 请求 DamageSystem 计算伤害 → 调用 Ore.TakeDamage()
    /// → 矿石死亡则发放奖励。
    /// 不扣血、不显示伤害数字、不播放受击表现，这些全部由 Ore 自己负责。
    /// </summary>
    public class MiningInput : MonoBehaviour
    {
        [Header("输入")]
        [Tooltip("复用工程已有的 Assets/InputSystem_Actions.inputactions 中的 Player/Attack 动作，" +
                 "该动作已绑定鼠标左键、触摸点击、手柄等，无需新建输入资产")]
        [SerializeField] private InputActionReference clickAction;

        [Header("拾取")]
        [Tooltip("用于屏幕坐标转射线的相机")]
        [SerializeField] private Camera viewCamera;

        [Tooltip("可被点击的图层，应只勾选 Ore 层，避免射线命中地面或建筑")]
        [SerializeField] private LayerMask oreLayers = ~0;

        [Tooltip("射线最大检测距离")]
        [SerializeField] private float maxRayDistance = 200f;

        [Header("UI 屏蔽")]
        [Tooltip("勾选后，落在 UI（商店面板等）上的点击不会穿透到矿石。" +
                 "Physics.Raycast 看不见 Canvas，所以必须单独判定一次")]
        [SerializeField] private bool blockClicksOverUI = true;

        [Header("依赖")]
        [Tooltip("玩家金币，矿石击碎后在此发放奖励")]
        [SerializeField] private PlayerCurrency playerCurrency;

        [Tooltip("伤害计算系统")]
        [SerializeField] private DamageSystem damageSystem;

        [Header("调试")]
        [Tooltip("是否在 Console 打印击碎与奖励发放日志")]
        [SerializeField] private bool logMiningFlow = true;

        [Tooltip("是否在 Console 打印点击落空的原因。阶段 1 排错用，正式游玩时建议关闭")]
        [SerializeField] private bool logMissedClicks = true;

        /// <summary>UI 射线结果的复用缓冲，避免每次点击都分配新 List。</summary>
        private readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>();

        /// <summary>EventSystem 缺失的警告只打一次，避免每次点击刷屏。</summary>
        private bool warnedAboutMissingEventSystem;

        private void Awake()
        {
            if (clickAction == null)
            {
                Debug.LogError("[MiningInput] Click Action 未配置。请把 Assets/InputSystem_Actions.inputactions " +
                               "拖入该字段，并在弹出的动作列表中选中 Player/Attack。", this);
            }

            if (viewCamera == null)
            {
                viewCamera = Camera.main;
                if (viewCamera == null)
                {
                    Debug.LogError("[MiningInput] 未配置 View Camera，且场景中没有带 MainCamera 标签的相机，点击将无效。", this);
                }
            }

            if (damageSystem == null)
            {
                Debug.LogError("[MiningInput] DamageSystem 未配置，点击无法造成伤害。", this);
            }

            if (playerCurrency == null)
            {
                Debug.LogError("[MiningInput] PlayerCurrency 未配置，击碎奖励无法发放。", this);
            }

            if (oreLayers.value == 0)
            {
                Debug.LogWarning("[MiningInput] Ore Layers 没有勾选任何图层，射线不会命中任何矿石。", this);
            }
        }

        private void OnEnable()
        {
            if (clickAction == null)
            {
                return;
            }

            // performed 在一次按下中只触发一次：按住不松手不会重复攻击
            clickAction.action.performed += OnClickPerformed;
            clickAction.action.Enable();
        }

        private void OnDisable()
        {
            if (clickAction == null)
            {
                return;
            }

            clickAction.action.performed -= OnClickPerformed;
            clickAction.action.Disable();
        }

        private void OnClickPerformed(InputAction.CallbackContext context)
        {
            if (Pointer.current == null)
            {
                if (logMissedClicks)
                {
                    Debug.LogWarning("[MiningInput] Pointer.current 为空，本次点击没有可用的屏幕坐标。", this);
                }

                return;
            }

            ProcessMiningAt(Pointer.current.position.ReadValue());
        }

        /// <summary>
        /// 对指定屏幕坐标执行一次挖矿。
        /// 独立成方法是为了后续支持多指触摸：每个触点传入各自的屏幕坐标即可复用同一套逻辑。
        /// </summary>
        private void ProcessMiningAt(Vector2 screenPosition)
        {
            if (damageSystem == null || playerCurrency == null)
            {
                // Awake 已经打印过明确错误，这里不再重复刷屏
                return;
            }

            if (viewCamera == null)
            {
                Debug.LogError("[MiningInput] View Camera 为空，无法发射射线。", this);
                return;
            }

            // 商店面板等 UI 上的点击不应该穿透到矿石
            if (IsPointerOverUI(screenPosition))
            {
                return;
            }

            Ray ray = viewCamera.ScreenPointToRay(screenPosition);

            if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, oreLayers, QueryTriggerInteraction.Ignore))
            {
                if (logMissedClicks)
                {
                    Debug.Log($"[MiningInput] 点击落空：射线没有命中任何碰撞体。" +
                              $"当前 Ore Layers = [{DescribeLayerMask(oreLayers)}]。" +
                              $"请依次检查：(1) 矿石根物体是否在 Ore 层 " +
                              $"(2) 矿石是否挂了 Collider " +
                              $"(3) 相机是否朝向矿石所在位置");
                }

                return;
            }

            Ore ore = hit.collider.GetComponentInParent<Ore>();

            if (ore == null)
            {
                if (logMissedClicks)
                {
                    string hitLayer = LayerMask.LayerToName(hit.collider.gameObject.layer);
                    Debug.Log($"[MiningInput] 命中了 {hit.collider.name}（层 {hitLayer}），" +
                              $"但它的父级上没有 Ore 组件。射线确实在发射，只是打到了别的东西。");
                }

                return;
            }

            // 先取出名字与奖励：TakeDamage 可能击碎并销毁矿石
            string oreName = ore.name;
            int reward = ore.Reward;

            float damage = damageSystem.CalculatePlayerDamage();
            if (damage <= 0f)
            {
                // DamageSystem 已经打印了明确错误，这里不再重复刷屏
                return;
            }

            bool destroyed = ore.TakeDamage(damage);

            if (!destroyed)
            {
                return;
            }

            playerCurrency.AddMoney(reward);

            if (logMiningFlow)
            {
                Debug.Log($"[MiningInput] 击碎 {oreName}，发放 {reward} 金币，当前金币 {playerCurrency.CurrentMoney}");
            }
        }

        /// <summary>
        /// 判断指定屏幕坐标是否落在 UI 上。
        /// 用 EventSystem.RaycastAll 对给定坐标直接做一次 UI 射线，而不是用
        /// EventSystem.IsPointerOverGameObject()——后者只认当前鼠标位置，触屏要额外传 fingerId，
        /// 且结果受 EventSystem 与调用方的执行顺序影响。
        /// 这里按坐标判定，将来每个指尖传入各自的屏幕坐标即可同时支持多指触摸。
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
                // 没有 EventSystem 就说明场景里没有能吃掉点击的 UI，放行即可
                if (!warnedAboutMissingEventSystem)
                {
                    warnedAboutMissingEventSystem = true;
                    Debug.LogWarning("[MiningInput] 场景中没有 EventSystem，无法判断点击是否落在 UI 上；" +
                                     "后续点击一律不做 UI 屏蔽。若已创建商店面板，请补一个 EventSystem。", this);
                }

                return false;
            }

            // 每次点击新建一份，避免缓存后 EventSystem 被重建时留下悬空引用。点击频次极低，开销可忽略
            PointerEventData eventData = new PointerEventData(eventSystem)
            {
                position = screenPosition,
            };

            uiRaycastResults.Clear();
            eventSystem.RaycastAll(eventData, uiRaycastResults);

            return uiRaycastResults.Count > 0;
        }

        /// <summary>把 LayerMask 里的图层名拼成可读文本，供报错信息使用。</summary>
        private static string DescribeLayerMask(LayerMask mask)
        {
            if (mask.value == 0)
            {
                return "未勾选任何图层";
            }

            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < 32; i++)
            {
                if ((mask.value & (1 << i)) == 0)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                string layerName = LayerMask.LayerToName(i);
                builder.Append(string.IsNullOrEmpty(layerName) ? $"Layer {i}" : layerName);
            }

            return builder.ToString();
        }
    }
}
