using UnityEngine;
using Yujian.Core;

namespace Yujian.Mining
{
    /// <summary>
    /// 岛上的矿洞入口标记。
    /// 职责：被 MiningEntranceRouter 命中后，把玩家从观察模式切到挖矿区。
    /// 不处理输入、不移动相机、不生成矿石——这些仍由各自的系统负责。
    /// </summary>
    [DisallowMultipleComponent]
    public class MiningEntrance : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("区域控制器。进入挖矿区要经过它，才能同时切相机、开面板、开挖矿输入")]
        [SerializeField] private GameAreaController areaController;

        [Header("显示")]
        [Tooltip("进入挖矿区后隐藏岛上的矿山模型，避免从矿洞视角看到入口本身")]
        [SerializeField] private bool hideInMiningArea = true;

        private Renderer[] cachedRenderers;
        private Collider[] cachedColliders;

        private void Awake()
        {
            cachedRenderers = GetComponentsInChildren<Renderer>(true);
            cachedColliders = GetComponentsInChildren<Collider>(true);
        }

        private void OnEnable()
        {
            if (areaController != null)
            {
                areaController.OnAreaChanged += HandleAreaChanged;
            }
        }

        private void Start()
        {
            if (areaController != null)
            {
                ApplyVisibility(areaController.CurrentArea);
            }
        }

        private void OnDisable()
        {
            if (areaController != null)
            {
                areaController.OnAreaChanged -= HandleAreaChanged;
            }
        }

        private void HandleAreaChanged(GameArea area)
        {
            ApplyVisibility(area);
        }

        private void ApplyVisibility(GameArea area)
        {
            bool visible = !hideInMiningArea || area != GameArea.Mining;

            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                if (cachedRenderers[i] != null)
                {
                    cachedRenderers[i].enabled = visible;
                }
            }

            for (int i = 0; i < cachedColliders.Length; i++)
            {
                if (cachedColliders[i] != null)
                {
                    cachedColliders[i].enabled = visible;
                }
            }
        }

        /// <summary>进入挖矿区。重复点击或已经在挖矿区时不做任何事。</summary>
        public void Activate()
        {
            if (areaController == null)
            {
                Debug.LogError("[MiningEntrance] GameAreaController 未配置，无法进入挖矿区。" +
                               "请运行菜单「屿见/挖矿/配置矿洞入口（阶段 11）」。", this);
                return;
            }

            areaController.SetArea(GameArea.Mining);
        }

        /// <summary>编辑器向导调用，接好区域控制器。</summary>
        public void ConfigureForSetup(GameAreaController controller)
        {
            areaController = controller;
        }

        /// <summary>
        /// 美术换完模型后，右键组件执行这一项，就能按新的子物体重新生成碰撞框。
        /// </summary>
        [ContextMenu("按子物体重新适配碰撞框")]
        private void FitColliderToRenderers()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

            if (box == null || renderers.Length == 0)
            {
                Debug.LogWarning("[MiningEntrance] 没有 BoxCollider 或子物体渲染器，无法适配碰撞框。", this);
                return;
            }

            Bounds bounds = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            Vector3 lossyScale = transform.lossyScale;
            Vector3 localCenter = transform.InverseTransformPoint(bounds.center);

            box.center = localCenter;
            box.size = new Vector3(
                SafeDivide(bounds.size.x, lossyScale.x),
                SafeDivide(bounds.size.y, lossyScale.y),
                SafeDivide(bounds.size.z, lossyScale.z));
        }

        private static float SafeDivide(float value, float divisor)
        {
            return Mathf.Approximately(divisor, 0f) ? value : value / divisor;
        }
    }
}