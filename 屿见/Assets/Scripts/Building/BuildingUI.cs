using System.Collections.Generic;
using UnityEngine;
using Yujian.Core;
using Yujian.Player;
using Yujian.Shop;

namespace Yujian.Building
{
    /// <summary>
    /// 建筑系统的 UI 入口：把按钮点击翻译成对 BuildingManager / BuildingCameraDirector 的调用。
    ///
    /// 为什么需要这个中间层：Unity 的 Button OnClick 只能传 int / float / string / bool / Object
    /// 这几种参数，而 BuildingManager.SelectBuilding 要的是 BuildingData。
    /// 这里用「下标 + 数组」绕开限制——按钮只传一个整数，
    /// 以后新增建筑只要往 BuildingUI.availableBuildings 里加一项，不用改代码，也不用重新接线。
    ///
    /// 本类不持有任何状态，只做转发。
    /// </summary>
    public class BuildingUI : MonoBehaviour
    {
        [Tooltip("建筑系统入口")]
        [SerializeField] private BuildingManager buildingManager;

        [Tooltip("相机调度。区域引用缺失时的兜底：用它把镜头移回常规机位")]
        [SerializeField] private BuildingCameraDirector cameraDirector;

        [Tooltip("区域状态。点「退出建造」时切到观察模式；留空会报错并退化为只退出相机")]
        [SerializeField] private GameAreaController areas;

        [Tooltip("可供玩家建造的建筑清单。顺序与 UI 按钮传进来的下标一一对应")]
        [SerializeField] private BuildingData[] availableBuildings = new BuildingData[0];

        [Tooltip("特殊建筑清单。用来禁止同一座特殊建筑重复建造；留空则不限制")]
        [SerializeField] private MilestoneProgress milestoneProgress;

        [Tooltip("原料库存。建筑栏位（3.7）用它判断「原料不足」。留空则跳过原料检查")]
        [SerializeField] private MaterialInventory inventory;

        [Tooltip("玩家等级。建筑栏位（3.7）用它判断「需要等级 N」。留空则跳过等级检查")]
        [SerializeField] private PlayerLevel playerLevel;

        /// <summary>可选建筑的数量。</summary>
        public int AvailableBuildingCount =>
            availableBuildings == null ? 0 : availableBuildings.Length;

        /// <summary>可供建造的建筑清单，只读。建筑栏位按这个顺序生成按钮。</summary>
        public IReadOnlyList<BuildingData> AvailableBuildings =>
            availableBuildings ?? System.Array.Empty<BuildingData>();

        /// <summary>
        /// 选中清单里第 index 个建筑，进入放置模式。
        /// 供按钮 OnClick 调用，参数是 Unity 事件原生支持的 int。
        /// </summary>
        public void SelectBuilding(int index)
        {
            if (buildingManager == null)
            {
                Debug.LogError("[BuildingUI] BuildingManager 未配置，无法选择建筑。", this);
                return;
            }

            if (availableBuildings == null || index < 0 || index >= availableBuildings.Length)
            {
                Debug.LogError($"[BuildingUI] 建筑下标 {index} 越界" +
                               $"（清单共 {AvailableBuildingCount} 项）。" +
                               "请检查按钮传的下标和 Inspector 里的 Available Buildings。", this);
                return;
            }

            BuildingData data = availableBuildings[index];

            if (data == null)
            {
                Debug.LogError($"[BuildingUI] 建筑清单第 {index} 项是空的，请在 Inspector 里补上。", this);
                return;
            }

            if (!IsBuildingAvailable(index, out string unavailableReason))
            {
                Debug.LogWarning($"[BuildingUI] 不能建造「{data.BuildingName}」：{unavailableReason}", this);
                return;
            }

            buildingManager.SelectBuilding(data);
        }

        /// <summary>
        /// 清单里第 index 项当前是否可建造（策划案 3.7：等级 / 数量上限 / 原料三道检查）。
        /// 按钮置灰与点击校验共用这一份判断，避免两边规则分叉。
        /// </summary>
        public bool IsBuildingAvailable(int index, out string reason)
        {
            BuildingAvailabilityInfo info = Evaluate(index);
            reason = info.Reason;
            return info.IsAvailable;
        }

        /// <summary>
        /// 判定清单里第 index 项现在能不能建，并给出给玩家看的原因（策划案 3.7）。
        ///
        /// 判定顺序：等级 → 数量上限 → 原料。
        /// 顺序有意义：一座同时「等级不够」和「原料不够」的建筑，
        /// 提示「需要等级 5」比「原料不足」更有用（玩家知道该去干什么）。
        ///
        /// 按钮置灰、按钮里的原因文案、原料刚好够的微光，全部读这一份结论。
        /// </summary>
        public BuildingAvailabilityInfo Evaluate(int index)
        {
            if (availableBuildings == null || index < 0 || index >= availableBuildings.Length)
            {
                return BuildingAvailabilityInfo.Invalid(
                    $"建筑下标 {index} 越界（清单共 {AvailableBuildingCount} 项）");
            }

            BuildingData data = availableBuildings[index];

            if (data == null)
            {
                return BuildingAvailabilityInfo.Invalid($"建筑清单第 {index} 项是空的");
            }

            int builtCount = CountPlaced(data);
            int limit = ResolveLimit(data);

            // CurrentLevel 为 0 表示 PlayerLevel 还没跑起来（它在 Start 里才第一次算）。
            // 这种情况下不做等级判定，否则开局一瞬间所有建筑都会显示「需要等级 1」。
            if (playerLevel != null && playerLevel.CurrentLevel > 0 &&
                playerLevel.CurrentLevel < data.UnlockLevel)
            {
                return new BuildingAvailabilityInfo
                {
                    State = BuildingAvailabilityState.LevelLocked,
                    Reason = $"需要等级 {data.UnlockLevel}",
                    BuiltCount = builtCount,
                    Limit = limit,
                };
            }

            if (limit > 0 && builtCount >= limit)
            {
                return new BuildingAvailabilityInfo
                {
                    State = BuildingAvailabilityState.LimitReached,
                    Reason = $"已达上限 {builtCount}/{limit}",
                    BuiltCount = builtCount,
                    Limit = limit,
                };
            }

            if (inventory != null)
            {
                bool missing = false;
                bool exactlyEnough = true;
                int requirementCount = 0;

                IReadOnlyList<BuildingMaterialRequirement> requirements = data.RequiredMaterials;

                for (int i = 0; i < requirements.Count; i++)
                {
                    BuildingMaterialRequirement requirement = requirements[i];

                    if (!requirement.IsValid)
                    {
                        continue;
                    }

                    requirementCount++;
                    int held = inventory.GetMaterialCount(requirement.Material);

                    if (held < requirement.Amount)
                    {
                        missing = true;
                    }

                    if (held != requirement.Amount)
                    {
                        exactlyEnough = false;
                    }
                }

                if (missing)
                {
                    return new BuildingAvailabilityInfo
                    {
                        State = BuildingAvailabilityState.MissingMaterial,
                        Reason = "原料不足",
                        BuiltCount = builtCount,
                        Limit = limit,
                    };
                }

                return new BuildingAvailabilityInfo
                {
                    State = BuildingAvailabilityState.Available,
                    Reason = string.Empty,
                    JustEnoughMaterials = requirementCount > 0 && exactlyEnough,
                    BuiltCount = builtCount,
                    Limit = limit,
                };
            }

            return new BuildingAvailabilityInfo
            {
                State = BuildingAvailabilityState.Available,
                Reason = string.Empty,
                BuiltCount = builtCount,
                Limit = limit,
            };
        }

        /// <summary>场上已有多少座这个建筑。没有 PlacedBuildingInfo 的实例不计入。</summary>
        public int CountPlaced(BuildingData data)
        {
            if (buildingManager == null || data == null)
            {
                return 0;
            }

            IReadOnlyList<GameObject> placed = buildingManager.PlacedBuildings;
            int count = 0;

            for (int i = 0; i < placed.Count; i++)
            {
                GameObject instance = placed[i];

                if (instance == null)
                {
                    continue;
                }

                PlacedBuildingInfo info = instance.GetComponent<PlacedBuildingInfo>();

                if (info != null && info.Data == data)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 数量上限：主要建筑（里程碑）恒为 1 座，其余读 BuildingData.BuildLimit（0 = 不限）。
        /// </summary>
        private int ResolveLimit(BuildingData data)
        {
            int configured = data.BuildLimit;

            if (milestoneProgress != null && milestoneProgress.IsMilestone(data))
            {
                return configured <= 0 ? 1 : Mathf.Min(configured, 1);
            }

            return configured;
        }

        /// <summary>
        /// 补接线：建筑栏位在运行时发现这两个引用是空的就调一次。
        /// 已经有引用时不覆盖（Inspector 里手接的优先）。
        /// </summary>
        public void EnsureAvailabilitySources(MaterialInventory materialInventory, PlayerLevel level)
        {
            if (inventory == null)
            {
                inventory = materialInventory;
            }

            if (playerLevel == null)
            {
                playerLevel = level;
            }
        }

        /// <summary>取消当前蓝图，但仍留在建造模式（镜头不动）。</summary>
        public void CancelSelection()
        {
            if (buildingManager == null)
            {
                Debug.LogWarning("[BuildingUI] BuildingManager 未配置，取消操作被忽略。", this);
                return;
            }

            buildingManager.CancelSelection();
        }

        /// <summary>
        /// 退出建造模式：退出建筑系统，落到观察模式。
        ///
        /// 优先走区域切换，而不是只调 cameraDirector.ExitBuildMode()：
        /// 后者只动相机，底部导航仍会高亮「建筑」、建筑面板仍会被区域系统当成打开状态。
        /// 区域切到 Observation 后，AreaCameraDirector 会负责退出建造视角并取消未放置蓝图。
        /// </summary>
        public void ExitBuildMode()
        {
            if (areas != null)
            {
                areas.EnterObservationMode();
                return;
            }

            Debug.LogError("[BuildingUI] GameAreaController 未配置：「退出建造」只会退出相机，" +
                           "底部导航仍会停在建筑区。请重新运行菜单「屿见/场景/配置观察模式系统」" +
                           "把 GameAreaSystem 接上。", this);

            if (cameraDirector != null)
            {
                // 兜底：cameraDirector 内部会顺手取消还没放置的蓝图，这里不要重复取消
                cameraDirector.ExitBuildMode();
                return;
            }

            // 没接相机调度时退化为「只取消蓝图」，至少不会把玩家卡在放置模式里
            CancelSelection();
        }
    }
}
