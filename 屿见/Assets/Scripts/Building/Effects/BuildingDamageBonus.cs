using UnityEngine;
using Yujian.Core.Events;
using Yujian.Player;
using Yujian.Shop;

namespace Yujian.Building.Effects
{
    /// <summary>
    /// 把「建筑消耗掉的材料」翻译成玩家伤害倍率与建筑伤害倍率加成
    /// （玩家部分为 D1=B，2026-10-29 拍板；建筑部分为 2026-11-05 商店改版新增）。
    ///
    /// 规则：
    ///   · 只有材料被建成消耗才计入——买进背包、拖进蓝图都不加倍率；
    ///   · 每座建筑的加成 = 它的材料需求里每条
    ///     <see cref="MaterialData.DamageMultiplierBonus"/>（玩家）或
    ///     <see cref="MaterialData.BuildingDamageMultiplierBonus"/>（建筑）× 数量；
    ///   · 同一座建筑建两遍算两份（与建筑本身一致）；
    ///   · 拆除建筑后加成自动回落。
    ///
    /// 为什么从 <see cref="BuildingData.RequiredMaterials"/> 反推而不是在建造时记账：
    ///   建成时一定凑齐了全部需求，所以两者等价；反推还能让读档直接得到正确结果，
    ///   不必把倍率写进存档，也不会出现两份真相。
    ///
    /// 2026-11-16 起也负责汇总**全局强化**（策划案 3.4 的「全局」选项）：
    ///   玩家伤害 / 全建筑伤害的全局加成与材料加成写进同一组 PlayerStats 倍率，
    ///   避免出现第二个汇总入口。个体强化不进这里，由建筑效果自己读施工实例。
    ///
    /// 2026-11-23 起同时汇总**暴击点持续时间**：
    ///   每座建筑的 BuildingData.CritPointDurationBonus（比例，0.2 = +20%）
    ///   + 全局强化「暴击点持续时间延长」选项，写入 PlayerStats，CritPointSystem 刷点时读取。
    ///
    /// 2026-11-23 起也汇总**全建筑攻速**（强化选项「建筑攻速提升」，全局）：
    ///   写入 PlayerStats，OrganMuseumAutoAttack 计算攻击间隔时读取。
    ///
    /// 2026-11-23 起也汇总**玩家暴击倍率**（强化选项「玩家暴击倍率提升」，全局）：
    ///   写入 PlayerStats，DamageSystem 计算暴击点时读取。
    ///
    /// 不修改基础倍率、不碰金币、不处理输入。
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildingDamageBonus : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("建筑管理器：读取当前场上已建成的建筑")]
        [SerializeField] private BuildingManager buildingManager;

        [Tooltip("玩家属性：把汇总出的附加倍率写进去")]
        [SerializeField] private PlayerStats playerStats;

        [Header("调试")]
        [SerializeField] private bool logBonusFlow = true;

        private bool subscribed;

        /// <summary>当前建筑带来的附加玩家伤害倍率。</summary>
        public float CurrentBonus { get; private set; }

        /// <summary>当前建筑带来的附加建筑伤害倍率。</summary>
        public float CurrentBuildingBonus { get; private set; }

        /// <summary>当前建筑带来的暴击点持续时间附加秒数。</summary>
        public float CurrentCritPointBonus { get; private set; }

        /// <summary>当前建筑带来的全建筑攻速加成（0.1 = +10%）。</summary>
        public float CurrentAttackSpeedBonus { get; private set; }

        /// <summary>当前建筑带来的玩家暴击倍率绝对加成（+1 = 倍率额外 +1）。</summary>
        public float CurrentCritMultiplierBonus { get; private set; }

        private void Awake()
        {
            ResolveDependencies();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void Start()
        {
            // 读档可能在 Start 之前就恢复了建筑，这里无条件重算一次
            Recalculate();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        /// <summary>按场上建筑重算附加倍率并写回 PlayerStats。</summary>
        public void Recalculate()
        {
            ResolveDependencies();

            if (buildingManager == null || playerStats == null)
            {
                if (logBonusFlow)
                {
                    Debug.LogError("[BuildingDamageBonus] BuildingManager 或 PlayerStats 未配置，" +
                                   "建筑加成不会生效。请运行菜单「屿见/建筑/配置材料倍率加成」。", this);
                }

                return;
            }

            float playerTotal = 0f;
            float buildingTotal = 0f;
            float critPointTotal = 0f;
            float attackSpeedTotal = 0f;
            float critMultiplierTotal = 0f;
            System.Collections.Generic.IReadOnlyList<GameObject> placed = buildingManager.PlacedBuildings;

            for (int i = 0; i < placed.Count; i++)
            {
                GameObject instance = placed[i];

                if (instance == null)
                {
                    continue;
                }

                PlacedBuildingInfo info = instance.GetComponent<PlacedBuildingInfo>();

                if (info == null || info.Data == null)
                {
                    continue;
                }

                playerTotal += CalculateBonus(info.Data) +
                    info.GetEnhancementBonus(
                        BuildingEnhancementScope.Global, BuildingEnhancementStat.PlayerDamage);
                buildingTotal += CalculateBuildingBonus(info.Data) +
                    info.GetEnhancementBonus(
                        BuildingEnhancementScope.Global, BuildingEnhancementStat.BuildingDamage);

                // 暴击点持续时间：建筑基础值 + 全局强化选项，单位秒
                critPointTotal += info.Data.CritPointDurationBonus +
                    info.GetEnhancementBonus(
                        BuildingEnhancementScope.Global, BuildingEnhancementStat.CritPointDuration);

                // 全建筑攻速：只看全局强化（个体攻速由建筑效果自己读施工实例）
                attackSpeedTotal += info.GetEnhancementBonus(
                    BuildingEnhancementScope.Global, BuildingEnhancementStat.AttackSpeed);

                // 玩家暴击倍率：全局强化，绝对值加在照明工具倍率上
                critMultiplierTotal += info.GetEnhancementBonus(
                    BuildingEnhancementScope.Global, BuildingEnhancementStat.PlayerCriticalMultiplier);
            }

            if (Mathf.Approximately(playerTotal, CurrentBonus) &&
                Mathf.Approximately(buildingTotal, CurrentBuildingBonus) &&
                Mathf.Approximately(critPointTotal, CurrentCritPointBonus) &&
                Mathf.Approximately(attackSpeedTotal, CurrentAttackSpeedBonus) &&
                Mathf.Approximately(critMultiplierTotal, CurrentCritMultiplierBonus))
            {
                // 数值没变也要保证 PlayerStats 上是这个值（例如刚被别的系统清零）
                playerStats.SetBonusDamageMultiplier(playerTotal);
                playerStats.SetBonusBuildingDamageMultiplier(buildingTotal);
                playerStats.SetCritPointDurationBonus(critPointTotal);
                playerStats.SetGlobalAttackSpeedBonus(attackSpeedTotal);
                playerStats.SetCritMultiplierBonus(critMultiplierTotal);
                return;
            }

            float previousPlayer = CurrentBonus;
            float previousBuilding = CurrentBuildingBonus;
            float previousCritPoint = CurrentCritPointBonus;
            float previousAttackSpeed = CurrentAttackSpeedBonus;
            float previousCritMultiplier = CurrentCritMultiplierBonus;
            CurrentBonus = playerTotal;
            CurrentBuildingBonus = buildingTotal;
            CurrentCritPointBonus = critPointTotal;
            CurrentAttackSpeedBonus = attackSpeedTotal;
            CurrentCritMultiplierBonus = critMultiplierTotal;
            playerStats.SetBonusDamageMultiplier(playerTotal);
            playerStats.SetBonusBuildingDamageMultiplier(buildingTotal);
            playerStats.SetCritPointDurationBonus(critPointTotal);
            playerStats.SetGlobalAttackSpeedBonus(attackSpeedTotal);
            playerStats.SetCritMultiplierBonus(critMultiplierTotal);

            if (logBonusFlow)
            {
                Debug.Log($"[BuildingDamageBonus] 建筑 {placed.Count} 座，" +
                          $"玩家伤害倍率加成 {previousPlayer:0.##} → {CurrentBonus:0.##}，" +
                          $"建筑伤害倍率加成 {previousBuilding:0.##} → {CurrentBuildingBonus:0.##}，" +
                          $"暴击点持续 +{previousCritPoint * 100f:0.#}% → +{CurrentCritPointBonus * 100f:0.#}%，" +
                          $"全建筑攻速 +{previousAttackSpeed * 100f:0.#}% → +{CurrentAttackSpeedBonus * 100f:0.#}%，" +
                          $"玩家暴击倍率 +{previousCritMultiplier:0.##} → +{CurrentCritMultiplierBonus:0.##}；" +
                          $"最终倍率 玩家 {playerStats.DamageMultiplier:0.##} / " +
                          $"建筑 {playerStats.BuildingDamageMultiplier:0.##}");
            }
        }

        /// <summary>
        /// 单座建筑的材料倍率加成。没有需求时返回 0。
        /// 公开成静态方法，方便编辑器批处理直接验证公式。
        /// </summary>
        public static float CalculateBonus(BuildingData data)
        {
            if (data == null)
            {
                return 0f;
            }

            System.Collections.Generic.IReadOnlyList<BuildingMaterialRequirement> requirements =
                data.RequiredMaterials;
            float total = 0f;

            for (int i = 0; i < requirements.Count; i++)
            {
                BuildingMaterialRequirement requirement = requirements[i];

                if (!requirement.IsValid)
                {
                    continue;
                }

                total += requirement.Material.DamageMultiplierBonus * requirement.Amount;
            }

            return total;
        }

        /// <summary>
        /// 单座建筑的**建筑伤害**倍率加成。没有需求时返回 0。
        /// 与 <see cref="CalculateBonus"/> 是两条独立公式：砖块加玩家伤害，木板加建筑伤害。
        /// </summary>
        public static float CalculateBuildingBonus(BuildingData data)
        {
            if (data == null)
            {
                return 0f;
            }

            System.Collections.Generic.IReadOnlyList<BuildingMaterialRequirement> requirements =
                data.RequiredMaterials;
            float total = 0f;

            for (int i = 0; i < requirements.Count; i++)
            {
                BuildingMaterialRequirement requirement = requirements[i];

                if (!requirement.IsValid)
                {
                    continue;
                }

                total += requirement.Material.BuildingDamageMultiplierBonus * requirement.Amount;
            }

            return total;
        }

        private void ResolveDependencies()
        {
            if (buildingManager == null)
            {
                buildingManager = FindFirstObjectByType<BuildingManager>(FindObjectsInactive.Include);
            }

            if (playerStats == null)
            {
                playerStats = FindFirstObjectByType<PlayerStats>(FindObjectsInactive.Include);
            }
        }

        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            EventBus.Subscribe<BuildingPlaced>(HandleBuildingPlaced);
            EventBus.Subscribe<BuildingRemoved>(HandleBuildingRemoved);
            EventBus.Subscribe<BuildingEnhancementSelected>(HandleBuildingEnhancementSelected);
            EventBus.Subscribe<LoadCompleted>(HandleLoadCompleted);
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            EventBus.Unsubscribe<BuildingPlaced>(HandleBuildingPlaced);
            EventBus.Unsubscribe<BuildingRemoved>(HandleBuildingRemoved);
            EventBus.Unsubscribe<BuildingEnhancementSelected>(HandleBuildingEnhancementSelected);
            EventBus.Unsubscribe<LoadCompleted>(HandleLoadCompleted);
            subscribed = false;
        }

        private void HandleBuildingPlaced(BuildingPlaced _)
        {
            Recalculate();
        }

        private void HandleBuildingRemoved(BuildingRemoved _)
        {
            Recalculate();
        }

        private void HandleBuildingEnhancementSelected(BuildingEnhancementSelected _)
        {
            // 全局强化会改玩家 / 全建筑倍率，必须立刻重算；个体强化不经过这里
            Recalculate();
        }

        private void HandleLoadCompleted(LoadCompleted _)
        {
            Recalculate();
        }
    }
}
