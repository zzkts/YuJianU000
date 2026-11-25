using System.Text;
using UnityEngine;
using Yujian.Player;
using Yujian.Shop;

namespace Yujian.Building
{
    /// <summary>
    /// 已建建筑拆除的规则层（策划案 3.5）。
    ///
    /// 只处理「这座建筑能不能拆、拆完返还什么、怎么收尾」：
    ///   · 特殊建筑（MilestoneProgress 清单中的建筑）不能拆；
    ///   · 先退出迁移预览，再调用 <see cref="BuildingManager.RemoveBuilding"/>；
    ///   · 占格释放、存档、尘埃、音效和等级重算继续走现有事件链；
    ///   · 金币与建造原料是否返还在确认弹窗里勾选，Inspector 只提供默认值。
    ///
    /// 不处理按钮与弹窗，玩家入口在 <see cref="Yujian.UI.BuildingDemolitionUI"/>。
    /// </summary>
    public class BuildingDemolition : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("建筑管理器。拆除时由它释放占格并广播移除事件")]
        [SerializeField] private BuildingManager buildingManager;

        [Tooltip("建筑迁移。拆除前用它退出迁移预览")]
        [SerializeField] private BuildingMigration migration;

        [Tooltip("建筑升级。若被拆建筑正处于升级选中状态，拆除前先取消选中")]
        [SerializeField] private BuildingUpgrade upgrade;

        [Tooltip("特殊建筑清单。清单中的建筑只能迁移，不能拆除")]
        [SerializeField] private MilestoneProgress milestoneProgress;

        [Tooltip("玩家金币，返还升级费用时使用")]
        [SerializeField] private PlayerCurrency currency;

        [Tooltip("原料库存，返还建造原料时使用")]
        [SerializeField] private MaterialInventory inventory;

        [Tooltip("建筑金币商店。金币建造模式下，拆除返还的是建造费而不是原料")]
        [SerializeField] private BuildingGoldShop goldShop;

        [Header("返还")]
        [Tooltip("是否返还升到当前等级花掉的全部金币")]
        [SerializeField] private bool refundUpgradeMoney = true;

        [Tooltip("是否返还建造时消耗的全部原料")]
        [SerializeField] private bool refundBuildMaterials = true;

        [Header("调试")]
        [SerializeField] private bool logDemolitionFlow = true;

        /// <summary>运行时接线；场景里不需要额外挂组件。</summary>
        public void Configure(
            BuildingManager manager,
            BuildingMigration migrationSource,
            BuildingUpgrade upgradeSource,
            MilestoneProgress milestones,
            PlayerCurrency playerCurrency,
            MaterialInventory materialInventory,
            BuildingGoldShop goldShopSource = null)
        {
            buildingManager = manager;
            migration = migrationSource;
            upgrade = upgradeSource;
            milestoneProgress = milestones;
            currency = playerCurrency;
            inventory = materialInventory;
            goldShop = goldShopSource;
        }

        /// <summary>
        /// 当前是否处于金币建造模式。拆除弹窗据此把「返还原料」显示成「返还建造金币」。
        /// 原料被全局剔除时同样算金币模式（金币是唯一建造路径）。
        /// </summary>
        public bool UsesGoldPurchase => goldShop != null && goldShop.IsGoldModeActive;

        /// <summary>弹窗默认勾选状态：是否返还升级金币。</summary>
        public bool DefaultRefundUpgradeMoney => refundUpgradeMoney;

        /// <summary>弹窗默认勾选状态：是否返还建造原料。</summary>
        public bool DefaultRefundBuildMaterials => refundBuildMaterials;

        /// <summary>该建筑是否属于策划案里的「特殊建筑」。</summary>
        public bool IsSpecialBuilding(GameObject building)
        {
            if (building == null || milestoneProgress == null)
            {
                return false;
            }

            PlacedBuildingInfo info = building.GetComponent<PlacedBuildingInfo>();
            return info != null && info.Data != null && milestoneProgress.IsMilestone(info.Data);
        }

        /// <summary>检查指定建筑是否可以拆除。</summary>
        public bool CanDemolish(GameObject building, out string reason)
        {
            if (buildingManager == null)
            {
                reason = "建筑系统未配置";
                return false;
            }

            if (building == null)
            {
                reason = "没有选中的建筑";
                return false;
            }

            PlacedBuildingInfo info = building.GetComponent<PlacedBuildingInfo>();

            if (info == null || info.Data == null)
            {
                reason = "只能拆除已建成的建筑";
                return false;
            }

            if (IsSpecialBuilding(building))
            {
                reason = "特殊建筑无法拆除";
                return false;
            }

            if (buildingManager.IsPlacing)
            {
                reason = "建造中不能拆除建筑";
                return false;
            }

            if (!IsPlaced(building))
            {
                reason = "这座建筑不在已放置列表中";
                return false;
            }

            if (migration == null || !migration.IsActive || migration.Target != building)
            {
                reason = "请长按建筑进入迁移后再拆除";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>拆除指定建筑，并按 Inspector 默认开关返还资源。</summary>
        public bool TryDemolish(GameObject building, out string message)
        {
            return TryDemolish(building, refundUpgradeMoney, refundBuildMaterials, out message);
        }

        /// <summary>按本次弹窗的勾选结果拆除指定建筑。</summary>
        public bool TryDemolish(
            GameObject building,
            bool refundMoney,
            bool refundMaterials,
            out string message)
        {
            if (!CanDemolish(building, out message))
            {
                return false;
            }

            PlacedBuildingInfo info = building.GetComponent<PlacedBuildingInfo>();
            BuildingData data = info.Data;
            int level = info.Level;
            string displayName = string.IsNullOrWhiteSpace(data.BuildingName)
                ? building.name
                : data.BuildingName;

            // 先退出迁移。EndMigration 会恢复预览材质，但不会把建筑移回原位，
            // 所以尘埃、音效和拆除点都留在玩家最后放置的位置。
            if (migration != null && migration.Target == building)
            {
                migration.ExitForRemoval(building);
            }

            if (upgrade != null && upgrade.SelectedBuilding == building)
            {
                upgrade.Deselect();
            }

            if (!buildingManager.RemoveBuilding(building))
            {
                message = $"拆除「{displayName}」失败";
                return false;
            }

            string refundText = ApplyRefunds(data, level, refundMoney, refundMaterials);
            message = string.IsNullOrEmpty(refundText)
                ? $"已拆除「{displayName}」"
                : $"已拆除「{displayName}」，返还 {refundText}";

            if (logDemolitionFlow)
            {
                Debug.Log($"[BuildingDemolition] {message}。占格已释放，等待存档与尘埃事件结算。");
            }

            return true;
        }

        /// <summary>给确认弹窗使用的返还预览，不会修改任何资源。</summary>
        public string GetRefundPreview(GameObject building)
        {
            return GetRefundPreview(building, refundUpgradeMoney, refundBuildMaterials);
        }

        /// <summary>按指定勾选状态生成返还预览，不会修改任何资源。</summary>
        public string GetRefundPreview(
            GameObject building,
            bool refundMoney,
            bool refundMaterials)
        {
            if (building == null)
            {
                return string.Empty;
            }

            PlacedBuildingInfo info = building.GetComponent<PlacedBuildingInfo>();

            if (info == null || info.Data == null)
            {
                return string.Empty;
            }

            return BuildRefundDescription(info.Data, info.Level, refundMoney, refundMaterials);
        }

        private string ApplyRefunds(
            BuildingData data,
            int level,
            bool refundMoney,
            bool refundMaterials)
        {
            int money = GetRefundMoney(data, level, refundMoney);

            if (money > 0 && currency != null)
            {
                currency.AddMoney(money);
            }

            if (refundMaterials && UsesGoldPurchase)
            {
                // 金币建造模式：原料从未被消耗，改成退还建造费
                if (data != null && goldShop.TryGetPrice(data, out int buildPrice) && buildPrice > 0 && currency != null)
                {
                    currency.AddMoney(buildPrice);
                }
            }
            else if (refundMaterials && inventory != null && data != null)
            {
                var requirements = data.RequiredMaterials;

                for (int i = 0; i < requirements.Count; i++)
                {
                    BuildingMaterialRequirement requirement = requirements[i];

                    if (requirement.IsValid)
                    {
                        inventory.AddMaterial(requirement.Material, requirement.Amount);
                    }
                }
            }

            return BuildRefundDescription(data, level, refundMoney, refundMaterials);
        }

        private string BuildRefundDescription(
            BuildingData data,
            int level,
            bool refundMoney,
            bool refundMaterials)
        {
            int money = GetRefundMoney(data, level, refundMoney);
            string materials = GetRefundMaterialsText(data, refundMaterials);
            StringBuilder builder = new StringBuilder();

            if (money > 0)
            {
                builder.Append(money).Append(" 金币");
            }

            if (!string.IsNullOrEmpty(materials))
            {
                if (builder.Length > 0)
                {
                    builder.Append('、');
                }

                builder.Append(materials);
            }

            return builder.ToString();
        }

        private int GetRefundMoney(BuildingData data, int level, bool refundMoney)
        {
            if (!refundMoney || data == null || data.UpgradeTable == null || level <= 1)
            {
                return 0;
            }

            return data.UpgradeTable.GetTotalCost(1, level);
        }

        private string GetRefundMaterialsText(BuildingData data, bool refundMaterials)
        {
            if (!refundMaterials || data == null)
            {
                return string.Empty;
            }

            if (UsesGoldPurchase)
            {
                return goldShop.TryGetPrice(data, out int buildPrice) && buildPrice > 0
                    ? $"建造费 {buildPrice} 金币"
                    : string.Empty;
            }

            var requirements = data.RequiredMaterials;
            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < requirements.Count; i++)
            {
                BuildingMaterialRequirement requirement = requirements[i];

                if (!requirement.IsValid)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('、');
                }

                builder.Append(requirement.Material.MaterialName)
                       .Append(" ×")
                       .Append(requirement.Amount);
            }

            return builder.ToString();
        }

        private bool IsPlaced(GameObject building)
        {
            var placed = buildingManager.PlacedBuildings;

            for (int i = 0; i < placed.Count; i++)
            {
                if (placed[i] == building)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
