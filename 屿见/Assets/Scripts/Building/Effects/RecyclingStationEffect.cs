using UnityEngine;
using Yujian.Building.Recycling;

namespace Yujian.Building.Effects
{
    /// <summary>
    /// 回收站效果：把这座建筑接入共享的 <see cref="RecyclingSystem"/>。
    ///
    /// 本组件只负责「我是回收站、我几点几级、我选了什么强化」，不做队列、不算金币：
    /// 容量 / 转换 / 拆站退料全部由 RecyclingSystem 统一处理（策划案：所有回收站共享同一队列）。
    ///
    /// 与风琴博物馆的效果一样挂在建筑预制体上，建筑建成 / 读档恢复时由 BuildingEffect 基类初始化，
    /// 拆毁时自动注销。占位 prefab 由编辑器向导生成。
    /// </summary>
    public class RecyclingStationEffect : BuildingEffect
    {
        [Header("表现")]
        [Tooltip("站点上方进度条 / 金币图标的世界高度（相对建筑根物体）。第 3 步 UI 使用")]
        [SerializeField, Min(0f)] private float progressAnchorHeight = 3f;

        private RecyclingSystem system;
        private PlacedBuildingInfo ownerInfo;
        private bool registered;

        /// <summary>本建筑实例的运行时信息（等级 / 强化）。可能为 null。</summary>
        public PlacedBuildingInfo OwnerInfo
        {
            get
            {
                if (ownerInfo == null)
                {
                    ownerInfo = GetComponentInParent<PlacedBuildingInfo>();
                }

                return ownerInfo;
            }
        }

        /// <summary>站点进度条 / 金币图标的锚点世界坐标。</summary>
        public Vector3 ProgressAnchor => transform.position + Vector3.up * progressAnchorHeight;

        /// <summary>本建筑等级对应的转换速度倍率（升级表，1 级 = ×1）。</summary>
        public float GetLevelSpeedMultiplier()
        {
            PlacedBuildingInfo info = OwnerInfo;

            if (info == null || info.Data == null || info.Data.UpgradeTable == null)
            {
                return 1f;
            }

            return info.Data.UpgradeTable.GetEffectMultiplier(info.Level);
        }

        /// <summary>本建筑选过的「个体·回收效率」倍率（1 + 加成）。</summary>
        public float GetEfficiencyMultiplier()
        {
            PlacedBuildingInfo info = OwnerInfo;

            if (info == null)
            {
                return 1f;
            }

            float bonus = info.GetEnhancementBonus(
                BuildingEnhancementScope.Individual, BuildingEnhancementStat.RecyclingEfficiency);

            return Mathf.Max(0.01f, 1f + bonus);
        }

        /// <summary>本建筑选过的「全局·回收速度」加成（所有回收站共享，由系统汇总）。</summary>
        public float GetGlobalSpeedBonus()
        {
            PlacedBuildingInfo info = OwnerInfo;

            if (info == null)
            {
                return 0f;
            }

            return info.GetEnhancementBonus(
                BuildingEnhancementScope.Global, BuildingEnhancementStat.RecyclingSpeed);
        }

        protected override void OnInitialized()
        {
            if (OwnerInfo == null)
            {
                Debug.LogWarning($"[RecyclingStationEffect] 「{name}」的父级上找不到 PlacedBuildingInfo，" +
                                 "等级与强化加成会按 1 级 / 无强化处理。", this);
            }

            system = RecyclingSystem.Instance;

            if (system == null)
            {
                system = FindFirstObjectByType<RecyclingSystem>(FindObjectsInactive.Include);
            }

            if (system == null)
            {
                Debug.LogError("[RecyclingStationEffect] 场景里找不到 RecyclingSystem，回收站不会工作。" +
                               "请运行菜单「屿见/建筑/配置垃圾回收站系统」创建并接线。", this);
                return;
            }

            Register();
        }

        protected override void OnShutdown()
        {
            Unregister();
        }

        /// <summary>
        /// 建筑被临时禁用（例如整座建筑 SetActive(false)）时暂停产能：
        /// 注销后系统不再把它计入速度，重新启用时再注册。
        /// </summary>
        private void OnDisable()
        {
            Unregister();
        }

        private void OnEnable()
        {
            // 初始化之前 OnEnable 先跑；真正注册由 OnInitialized 负责
            if (IsInitialized)
            {
                Register();
            }
        }

        private void Register()
        {
            if (registered || system == null)
            {
                return;
            }

            system.RegisterStation(this);
            registered = true;
        }

        private void Unregister()
        {
            if (!registered || system == null)
            {
                return;
            }

            registered = false;
            system.UnregisterStation(this);
        }
    }
}
