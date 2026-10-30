using UnityEngine;
using Yujian.Core.Events;
using Yujian.Player;
using Yujian.Shop;

namespace Yujian.Building.Effects
{
    /// <summary>
    /// 把「建筑消耗掉的材料」翻译成玩家伤害倍率加成（D1=B，2026-10-29 拍板）。
    ///
    /// 规则：
    ///   · 只有材料被建成消耗才计入——买进背包、拖进蓝图都不加倍率；
    ///   · 每座建筑的加成 = 它的材料需求里每条 <see cref="MaterialData.DamageMultiplierBonus"/> × 数量；
    ///   · 同一座建筑建两遍算两份（与建筑本身一致）；
    ///   · 拆除建筑后加成自动回落。
    ///
    /// 为什么从 <see cref="BuildingData.RequiredMaterials"/> 反推而不是在建造时记账：
    ///   建成时一定凑齐了全部需求，所以两者等价；反推还能让读档直接得到正确结果，
    ///   不必把倍率写进存档，也不会出现两份真相。
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

        /// <summary>当前建筑带来的附加倍率。</summary>
        public float CurrentBonus { get; private set; }

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

            float total = 0f;
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

                total += CalculateBonus(info.Data);
            }

            if (Mathf.Approximately(total, CurrentBonus))
            {
                // 数值没变也要保证 PlayerStats 上是这个值（例如刚被别的系统清零）
                playerStats.SetBonusDamageMultiplier(total);
                return;
            }

            float previous = CurrentBonus;
            CurrentBonus = total;
            playerStats.SetBonusDamageMultiplier(total);

            if (logBonusFlow)
            {
                Debug.Log($"[BuildingDamageBonus] 建筑 {placed.Count} 座，伤害倍率加成 " +
                          $"{previous:0.##} → {CurrentBonus:0.##}，最终倍率 {playerStats.DamageMultiplier:0.##}");
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

        private void HandleLoadCompleted(LoadCompleted _)
        {
            Recalculate();
        }
    }
}
