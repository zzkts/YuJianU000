using System;
using UnityEngine;
using Yujian.Core.Events;
using Yujian.Player;

namespace Yujian.Building
{
    /// <summary>升级方式（策划案 3.4 左下角切换模块）。</summary>
    public enum BuildingUpgradeMode
    {
        /// <summary>点一次升一级。</summary>
        OneLevel = 0,

        /// <summary>点一次升到下一个等级间隔（10 的倍数），钱不够就停在买得起的那级。</summary>
        MaxToInterval = 1,
    }

    /// <summary>
    /// 建筑升级系统（策划案 3.4）的规则层。
    ///
    /// 职责：
    ///   1. 记住「玩家当前选中了哪座已建建筑」；
    ///   2. 按 <see cref="BuildingUpgradeTable"/> 算下一次/下一次间隔的费用与目标等级；
    ///   3. 扣金币、写等级、广播 <see cref="BuildingUpgraded"/>。
    ///
    /// 不做的事：不处理输入（点击谁的判定在 <see cref="BuildingPointerInteraction"/>）、
    /// 不投影 UI（<see cref="Yujian.UI.BuildingUpgradeUI"/>）、不建建筑、不碰存档
    /// （等级由 PlacedBuildingInfo 带进 BuildingSaveParticipant）。
    ///
    /// 每座建筑的等级互相独立，存在实例的 <see cref="PlacedBuildingInfo.Level"/> 上。
    /// </summary>
    public class BuildingUpgrade : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("建筑管理器。选中对象必须来自它的已放置建筑列表")]
        [SerializeField] private BuildingManager buildingManager;

        [Tooltip("玩家金币。升级从这里扣钱；留空会报错，升级按钮点了不生效")]
        [SerializeField] private PlayerCurrency currency;

        [Header("调试")]
        [SerializeField] private bool logUpgradeFlow = true;

        private GameObject selectedBuilding;

        /// <summary>选中对象变化（选中、切换、取消）时触发。</summary>
        public event Action OnSelectionChanged;

        /// <summary>等级或金币导致的可升级状态变化时触发。</summary>
        public event Action OnUpgradeStateChanged;

        /// <summary>当前选中的建筑实例，没有则为 null。</summary>
        public GameObject SelectedBuilding => selectedBuilding;

        /// <summary>当前选中建筑的运行时信息，没有则为 null。</summary>
        public PlacedBuildingInfo SelectedInfo =>
            selectedBuilding != null ? selectedBuilding.GetComponent<PlacedBuildingInfo>() : null;

        /// <summary>当前选中建筑的升级表，没有则为 null（不可升级）。</summary>
        public BuildingUpgradeTable SelectedTable => SelectedInfo?.Data?.UpgradeTable;

        /// <summary>当前等级。没有选中建筑时为 0。</summary>
        public int CurrentLevel => SelectedInfo != null ? SelectedInfo.Level : 0;

        /// <summary>等级上限。没有升级表时为 0。</summary>
        public int MaxLevel => SelectedTable != null ? SelectedTable.MaxLevel : 0;

        /// <summary>是否已满级。</summary>
        public bool IsMaxed => SelectedTable != null && CurrentLevel >= MaxLevel;

        /// <summary>是否选中了一座可以升级的建筑。</summary>
        public bool HasSelection => SelectedInfo != null && SelectedTable != null;

        /// <summary>下一级升级费用。满级或没选中时返回 0。</summary>
        public int NextCost
        {
            get
            {
                BuildingUpgradeTable table = SelectedTable;
                return table != null ? table.GetUpgradeCost(CurrentLevel) : 0;
            }
        }

        /// <summary>玩家当前金币。</summary>
        public int CurrentMoney => currency != null ? currency.CurrentMoney : 0;

        private void Awake()
        {
            if (buildingManager == null)
            {
                Debug.LogError("[BuildingUpgrade] BuildingManager 未配置，建筑无法被选中升级。", this);
            }

            if (currency == null)
            {
                Debug.LogError("[BuildingUpgrade] PlayerCurrency 未配置，升级不会扣钱也不会生效。", this);
            }
        }

        /// <summary>
        /// 选中一座建筑。传 null 取消选中（策划案：点击建筑以外的地方退出升级系统）。
        /// </summary>
        /// <returns>选中状态是否发生变化。</returns>
        public bool Select(GameObject building)
        {
            if (building == selectedBuilding)
            {
                return false;
            }

            if (building != null)
            {
                PlacedBuildingInfo info = building.GetComponent<PlacedBuildingInfo>();

                if (info == null || info.Data == null)
                {
                    // 不是本系统的建筑（例如手摆的场景物体），当作"点了空地"处理
                    building = null;
                }
            }

            selectedBuilding = building;

            if (logUpgradeFlow)
            {
                if (building == null)
                {
                    Debug.Log("[BuildingUpgrade] 已取消建筑选中。");
                }
                else
                {
                    PlacedBuildingInfo info = building.GetComponent<PlacedBuildingInfo>();
                    Debug.Log($"[BuildingUpgrade] 选中「{info.Data.BuildingName}」" +
                              $"（{info.Level}/{MaxLevel} 级）。");
                }
            }

            OnSelectionChanged?.Invoke();
            OnUpgradeStateChanged?.Invoke();
            return true;
        }

        /// <summary>取消选中。</summary>
        public void Deselect()
        {
            Select(null);
        }

        /// <summary>
        /// 当前模式下点一次能升几级（用来显示按钮上的「可升级次数」）。
        /// x1 恒为 0/1；Max 为金币够连升到目标等级的次数。
        /// </summary>
        public int GetAffordableLevelCount(BuildingUpgradeMode mode)
        {
            BuildingUpgradeTable table = SelectedTable;

            if (table == null || currency == null || IsMaxed)
            {
                return 0;
            }

            int target = GetTargetLevel(mode);
            return table.GetAffordableLevelCount(CurrentLevel, target, currency.CurrentMoney);
        }

        /// <summary>当前模式下的目标等级：x1 = 下一级，Max = 下一个 10 级边界（不超过上限）。</summary>
        public int GetTargetLevel(BuildingUpgradeMode mode)
        {
            BuildingUpgradeTable table = SelectedTable;

            if (table == null)
            {
                return 0;
            }

            return mode == BuildingUpgradeMode.MaxToInterval
                ? table.GetMaxModeTargetLevel(CurrentLevel)
                : Mathf.Min(CurrentLevel + 1, table.MaxLevel);
        }

        /// <summary>
        /// 按当前模式升级一次。x1 升 1 级；Max 尽量升到下一个间隔，钱不够就停在买得起的那级。
        /// </summary>
        /// <param name="mode">升级方式。</param>
        /// <param name="message">给玩家看的提示。</param>
        /// <returns>是否至少升了 1 级。</returns>
        public bool TryUpgrade(BuildingUpgradeMode mode, out string message)
        {
            PlacedBuildingInfo info = SelectedInfo;
            BuildingUpgradeTable table = SelectedTable;

            if (info == null || table == null)
            {
                message = "请先点击一座建筑";
                return false;
            }

            if (currency == null)
            {
                message = "金币系统未配置";
                return false;
            }

            if (info.Level >= table.MaxLevel)
            {
                message = "已经满级了";
                return false;
            }

            int previousLevel = info.Level;
            int target = GetTargetLevel(mode);
            int upgraded = 0;

            while (info.Level < target)
            {
                int cost = table.GetUpgradeCost(info.Level);

                if (cost <= 0)
                {
                    break;
                }

                if (!currency.TrySpendMoney(cost))
                {
                    break;
                }

                info.SetLevel(info.Level + 1);
                upgraded++;
            }

            if (upgraded <= 0)
            {
                int nextCost = table.GetUpgradeCost(info.Level);
                message = nextCost > 0 ? $"金币不足（需要 {nextCost}）" : "已经满级了";
                OnUpgradeStateChanged?.Invoke();
                return false;
            }

            message = $"升级到 {info.Level} 级（+{upgraded}）";

            if (logUpgradeFlow)
            {
                Debug.Log($"[BuildingUpgrade]「{info.Data.BuildingName}」{previousLevel} → {info.Level} 级" +
                          $"（{mode}，本次升 {upgraded} 级），剩余金币 {currency.CurrentMoney}。");
            }

            EventBus.Publish(new BuildingUpgraded(info.Data, previousLevel, info.Level));
            OnUpgradeStateChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 每帧由 UI 调用：金币会因为挖矿随时变化，按钮上的「可升级次数」需要跟着刷新。
        /// </summary>
        public void NotifyMoneyChanged()
        {
            OnUpgradeStateChanged?.Invoke();
        }
    }
}
