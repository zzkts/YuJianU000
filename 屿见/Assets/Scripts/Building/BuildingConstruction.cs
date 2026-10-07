using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Yujian.Shop;

namespace Yujian.Building
{
    /// <summary>
    /// 阶段 4 的建造结算：把材料放进蓝图、撤回、确认建造。
    ///
    /// 职责边界：
    ///   · 记住「蓝图里已经放了哪几块材料」，维护可撤回的顺序（后进先出）；
    ///   · 校验「这块材料能不能放」（种类对不对、放满了没、库存够不够）；
    ///   · 材料进蓝图时扣库存，撤回时退回库存；
    ///   · 确认建造时把颜色列表交给 BuildingManager 生成实体建筑。
    ///
    /// 不做的事：不生成建筑、不销毁蓝图（那是 BuildingManager 的），
    /// 不算屏幕坐标（那是 BuildingPlacement 的），不画 UI（那是 BuildingMaterialPanel 的）。
    ///
    /// 「建筑需要什么」**完全来自 BuildingData.RequiredMaterials**，本类不认识任何具体建筑。
    /// 将来加一个「Wood ×5 + Stone ×2」的建筑，本类一行都不用改。
    /// </summary>
    public class BuildingConstruction : MonoBehaviour
    {
        /// <summary>
        /// 蓝图里已放入的一块材料。颜色只在建成时影响外观，不参与需求匹配——
        /// 「砖块 ×3」指的是任意颜色的砖块合计 3 个。
        /// </summary>
        [Serializable]
        public struct PlacedMaterial
        {
            [SerializeField] private MaterialData material;
            [SerializeField] private MaterialColor color;

            public PlacedMaterial(MaterialData material, MaterialColor color)
            {
                this.material = material;
                this.color = color;
            }

            public MaterialData Material => material;

            public MaterialColor Color => color;

            public bool IsValid => material != null;

            /// <summary>显示名，例如「红色小砖」。</summary>
            public string DisplayName =>
                IsValid ? $"{color.ToChineseName()}{material.MaterialName}" : "无效材料";

            public override string ToString() => DisplayName;
        }

        [Header("依赖")]
        [Tooltip("建筑系统入口。用来读当前蓝图、当前相位、落位坐标，以及提交建造")]
        [SerializeField] private BuildingManager buildingManager;

        [Tooltip("原料库存。材料进蓝图时从这里扣，撤回时退回这里。这是「玩家有多少料」的唯一真相源")]
        [SerializeField] private MaterialInventory inventory;

        [Header("拖拽落点判定")]
        [Tooltip("用于把屏幕坐标换算成世界坐标的相机。留空则用 MainCamera")]
        [SerializeField] private Camera viewCamera;

        [Tooltip("地面高度，场景里的 Plane 在 y = 0。与 BuildingPlacement 的 Ground Height 保持一致")]
        [SerializeField] private float groundHeight = 0f;

        [Tooltip("判定「拖到蓝图上了」时，在占地范围外额外放宽的距离。" +
                 "蓝图占地 4×4，留 0.5 的余量，玩家不必像素级对准")]
        [SerializeField] private float dropMargin = 0.5f;

        [Header("调试")]
        [Tooltip("是否在 Console 打印放入 / 撤回 / 建造的日志")]
        [SerializeField] private bool logConstructionFlow = true;

        /// <summary>
        /// 蓝图里已放入的材料，**按放入顺序排列**，末尾的是最后放进去的。
        /// 撤回永远从末尾取，所以这个列表同时就是规格里要求的 MaterialPlacementHistory。
        /// </summary>
        private readonly List<PlacedMaterial> placementHistory = new List<PlacedMaterial>();

        /// <summary>放入、撤回、确认建造成功之后触发，UI 收到后重新读状态。</summary>
        public event Action OnConstructionChanged;

        /// <summary>给玩家看的提示行。参数：文案、是否成功。没有订阅者时只写 Console。</summary>
        public event Action<string, bool> OnMessage;

        private bool warnedAboutMissingCamera;

        /// <summary>蓝图里已放入的材料，按放入顺序，只读。</summary>
        public IReadOnlyList<PlacedMaterial> PlacementHistory => placementHistory;

        /// <summary>蓝图里已放入的材料块数。</summary>
        public int PlacedCount => placementHistory.Count;

        /// <summary>当前建筑是否已经凑齐材料，可以确认建造。</summary>
        public bool IsSatisfied
        {
            get
            {
                BuildingData data = SelectedData;

                if (data == null)
                {
                    return false;
                }

                IReadOnlyList<BuildingMaterialRequirement> requirements = data.RequiredMaterials;

                for (int i = 0; i < requirements.Count; i++)
                {
                    BuildingMaterialRequirement requirement = requirements[i];

                    if (!requirement.IsValid)
                    {
                        continue;
                    }

                    if (PlacedCountOf(requirement.Material) < requirement.Amount)
                    {
                        return false;
                    }
                }

                // 没有需求（或需求全是无效条目）的建筑，一落位就能建
                return true;
            }
        }

        /// <summary>当前选中的建筑数据，没有则为 null。只在 Filling 阶段之外也返回，供 UI 读需求文案。</summary>
        public BuildingData SelectedData =>
            buildingManager != null ? buildingManager.SelectedBuilding : null;

        private void Awake()
        {
            if (buildingManager == null)
            {
                Debug.LogError("[BuildingConstruction] BuildingManager 未配置，建造功能完全不会工作。", this);
            }

            if (inventory == null)
            {
                Debug.LogError("[BuildingConstruction] MaterialInventory 未配置，材料无法从库存扣减也无法退回。" +
                               "请在场景里把挂 MaterialInventory 的物体拖到该字段。", this);
            }

            if (viewCamera == null)
            {
                viewCamera = Camera.main;

                if (viewCamera == null)
                {
                    Debug.LogError("[BuildingConstruction] 未配置 View Camera，且场景中没有带 MainCamera 标签的相机，" +
                                   "无法判断材料被拖到了哪里。", this);
                }
            }
        }

        private void OnEnable()
        {
            if (buildingManager != null)
            {
                buildingManager.OnBlueprintClosed += HandleBlueprintClosed;
            }
        }

        private void OnDisable()
        {
            if (buildingManager != null)
            {
                buildingManager.OnBlueprintClosed -= HandleBlueprintClosed;
            }
        }

        // ---------------- 放入材料 ----------------

        /// <summary>
        /// 判断这块材料现在能不能放进蓝图。
        /// 拖拽开始时就调用一次：不合法就不生成拖拽残影，直接给提示
        /// （规格第三条要求「库存没有对应材料 → 不能生成拖拽物」）。
        /// </summary>
        /// <param name="reason">不能放的原因，可以直接显示给玩家。</param>
        public bool CanAcceptMaterial(MaterialData material, MaterialColor color, out string reason)
        {
            reason = null;

            if (buildingManager == null)
            {
                reason = "建筑系统未配置";
                return false;
            }

            if (buildingManager.CurrentBlueprint == null ||
                buildingManager.Phase != BuildingPhase.Filling)
            {
                reason = "请先把蓝图放到地面上";
                return false;
            }

            BuildingData data = buildingManager.SelectedBuilding;

            if (data == null)
            {
                reason = "没有选中的建筑";
                return false;
            }

            if (material == null)
            {
                reason = "材料数据为空";
                return false;
            }

            if (!IsRequiredBy(data, material))
            {
                reason = $"「{data.BuildingName}」不需要{material.MaterialName}";
                return false;
            }

            if (PlacedCountOf(material) >= RequiredCountOf(data, material))
            {
                reason = $"「{data.BuildingName}」的{material.MaterialName}已经放满了";
                return false;
            }

            if (inventory == null)
            {
                reason = "库存未配置";
                return false;
            }

            if (!inventory.HasMaterial(material, color, 1))
            {
                // 规格指定的文案，不要改写
                reason = "原料不足，请购买";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 把一块材料放进蓝图：扣 1 个库存 → 记入放入历史 → 刷新蓝图预览。
        /// 这是最底层的入口，不关心材料是从哪块 UI 拖过来的。
        /// </summary>
        /// <returns>是否放入成功。</returns>
        public bool TryPlaceMaterial(MaterialData material, MaterialColor color)
        {
            if (!CanAcceptMaterial(material, color, out string reason))
            {
                Report(reason, false);
                return false;
            }

            // 先扣库存再记账：扣失败就什么都不做，不会出现「账上多了一块、库存没少」的状态
            if (!inventory.RemoveMaterial(material, color, 1))
            {
                Report("原料不足，请购买", false);
                return false;
            }

            placementHistory.Add(new PlacedMaterial(material, color));
            RefreshBlueprintPreview();

            Report($"放入{color.ToChineseName()}{material.MaterialName}", true);

            if (logConstructionFlow)
            {
                Debug.Log($"[BuildingConstruction] 放入 {color.ToChineseName()}{material.MaterialName}，" +
                          $"蓝图内共 {placementHistory.Count} 块，进度 {GetProgressText()}");
            }

            OnConstructionChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 把一块材料放到屏幕上某个位置。拖拽松手时调这个：
        /// 先判断松手的地方是不是蓝图，是才真正放入。
        /// </summary>
        /// <returns>是否放入成功。</returns>
        public bool TryPlaceMaterialAt(Vector2 screenPosition, MaterialData material, MaterialColor color)
        {
            if (!IsOverBlueprint(screenPosition))
            {
                Report("请把材料拖到蓝图上", false);
                return false;
            }

            return TryPlaceMaterial(material, color);
        }

        // ---------------- 撤回 ----------------

        /// <summary>
        /// 撤回。严格按策划案分两种情况：
        ///   蓝图里有材料 → 撤回最后放入的那一块，退回库存，蓝图保留；
        ///   蓝图里没有材料 → 撤回建筑蓝图本身。
        /// </summary>
        public void Undo()
        {
            if (buildingManager == null)
            {
                Debug.LogError("[BuildingConstruction] BuildingManager 未配置，撤回无法执行。", this);
                return;
            }

            if (placementHistory.Count > 0)
            {
                int lastIndex = placementHistory.Count - 1;
                PlacedMaterial last = placementHistory[lastIndex];
                placementHistory.RemoveAt(lastIndex);

                ReturnToInventory(last);
                RefreshBlueprintPreview();

                Report($"撤回{last.DisplayName}", true);

                if (logConstructionFlow)
                {
                    Debug.Log($"[BuildingConstruction] 撤回 {last.DisplayName}，已退回库存，" +
                              $"蓝图内还剩 {placementHistory.Count} 块");
                }

                OnConstructionChanged?.Invoke();
                return;
            }

            // 蓝图里一块材料都没有 → 撤回蓝图
            if (buildingManager.CurrentBlueprint == null)
            {
                Report("现在没有可撤回的蓝图", false);
                return;
            }

            if (logConstructionFlow)
            {
                Debug.Log("[BuildingConstruction] 蓝图里没有材料，改为撤回建筑蓝图本身。");
            }

            Report("蓝图里没有材料，撤回建筑蓝图", true);

            // 会触发 OnBlueprintClosed(false)，历史此时已经是空的，退料是空操作
            buildingManager.CancelSelection();
        }

        // ---------------- 确认建造 ----------------

        /// <summary>
        /// 确认建造：材料齐了就生成实体建筑。
        /// 一旦成功，蓝图消失、材料已被消耗，**不能**再通过蓝图系统撤回。
        /// </summary>
        /// <returns>是否建造成功。</returns>
        public bool ConfirmBuild()
        {
            if (buildingManager == null)
            {
                Debug.LogError("[BuildingConstruction] BuildingManager 未配置，无法建造。", this);
                return false;
            }

            if (buildingManager.Phase != BuildingPhase.Filling || buildingManager.CurrentBlueprint == null)
            {
                Report("请先把蓝图放到地面上", false);
                return false;
            }

            if (!IsSatisfied)
            {
                Report($"材料还不够：{GetProgressText()}", false);
                return false;
            }

            // 先把颜色列表拷出来：BuildAtAnchor 会销毁蓝图并触发 OnBlueprintClosed，
            // 那时 placementHistory 已经被清空
            List<MaterialColor> layerColors = new List<MaterialColor>(placementHistory.Count);

            for (int i = 0; i < placementHistory.Count; i++)
            {
                layerColors.Add(placementHistory[i].Color);
            }

            if (logConstructionFlow)
            {
                Debug.Log($"[BuildingConstruction] 确认建造，共 {layerColors.Count} 块材料：" +
                          $"{DescribeColors(layerColors)}");
            }

            bool built = buildingManager.BuildAtAnchor(layerColors);

            if (!built)
            {
                Report("建造失败，蓝图保持不动", false);
            }

            return built;
        }

        // ---------------- 查询 ----------------

        /// <summary>蓝图里已放入的某类材料数量（不分颜色）。</summary>
        public int PlacedCountOf(MaterialData material)
        {
            if (material == null)
            {
                return 0;
            }

            int count = 0;

            for (int i = 0; i < placementHistory.Count; i++)
            {
                if (placementHistory[i].Material == material)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>当前建筑对某类材料的需求数量，不需要则返回 0。</summary>
        public int RequiredCountOf(MaterialData material)
        {
            return RequiredCountOf(SelectedData, material);
        }

        /// <summary>当前建筑对某类材料的需求总量（所有需求条目相加），没有需求则为 0。</summary>
        public int TotalRequiredCount()
        {
            BuildingData data = SelectedData;

            if (data == null)
            {
                return 0;
            }

            IReadOnlyList<BuildingMaterialRequirement> requirements = data.RequiredMaterials;
            int total = 0;

            for (int i = 0; i < requirements.Count; i++)
            {
                if (requirements[i].IsValid)
                {
                    total += requirements[i].Amount;
                }
            }

            return total;
        }

        /// <summary>进度文案，例如「小砖 2/3、原木 0/2」。没有需求时返回「无需材料」。</summary>
        public string GetProgressText()
        {
            BuildingData data = SelectedData;

            if (data == null)
            {
                return string.Empty;
            }

            IReadOnlyList<BuildingMaterialRequirement> requirements = data.RequiredMaterials;
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
                       .Append(' ')
                       .Append(PlacedCountOf(requirement.Material))
                       .Append('/')
                       .Append(requirement.Amount);
            }

            return builder.Length == 0 ? "无需材料" : builder.ToString();
        }

        // ---------------- 内部 ----------------

        /// <summary>蓝图被销毁（取消 / 撤回 / 建成）时的收尾。参数 built=true 表示材料已被消耗。</summary>
        private void HandleBlueprintClosed(bool built)
        {
            if (built)
            {
                // 材料已经变成建筑了，历史直接清掉；此后不允许再撤回
                placementHistory.Clear();
            }
            else
            {
                ReturnAllToInventory();
            }

            OnConstructionChanged?.Invoke();
        }

        /// <summary>把蓝图里剩下的材料全部退回库存。取消蓝图 / 退出建造走这条路，避免凭空吞掉玩家的料。</summary>
        private void ReturnAllToInventory()
        {
            if (placementHistory.Count == 0)
            {
                return;
            }

            if (logConstructionFlow)
            {
                Debug.Log($"[BuildingConstruction] 蓝图被取消，退回 {placementHistory.Count} 块材料。");
            }

            for (int i = 0; i < placementHistory.Count; i++)
            {
                ReturnToInventory(placementHistory[i]);
            }

            placementHistory.Clear();
        }

        private void ReturnToInventory(PlacedMaterial placed)
        {
            if (!placed.IsValid || inventory == null)
            {
                return;
            }

            inventory.AddMaterial(placed.Material, placed.Color, 1);
        }

        /// <summary>把当前的放入历史画到蓝图的楼层上：放进去了几块就亮几层。</summary>
        private void RefreshBlueprintPreview()
        {
            if (buildingManager == null)
            {
                return;
            }

            BuildingBlueprint blueprint = buildingManager.CurrentBlueprint;

            if (blueprint == null || blueprint.MaterialLayers == null)
            {
                return;
            }

            BuildingLayerStack stack = blueprint.MaterialLayers;
            int total = Mathf.Max(1, TotalRequiredCount());

            stack.EnsureLayout(total);

            for (int i = 0; i < total; i++)
            {
                if (i < placementHistory.Count)
                {
                    stack.SetLayerColor(i, placementHistory[i].Color);
                }
                else
                {
                    stack.SetLayerVisible(i, false);
                }
            }
        }

        /// <summary>屏幕坐标是否落在当前蓝图占地范围内。</summary>
        private bool IsOverBlueprint(Vector2 screenPosition)
        {
            BuildingBlueprint blueprint = buildingManager != null ? buildingManager.CurrentBlueprint : null;

            if (blueprint == null)
            {
                return false;
            }

            if (viewCamera == null)
            {
                if (!warnedAboutMissingCamera)
                {
                    warnedAboutMissingCamera = true;
                    Debug.LogError("[BuildingConstruction] 没有相机，无法把屏幕坐标换算成世界坐标，" +
                                   "材料永远放不进蓝图。", this);
                }

                return false;
            }

            // 与 BuildingPlacement 用同一套数学平面求交：不会被矿石、建筑、玩家挡住
            if (!BuildingPlacement.TryGetGroundPoint(viewCamera, groundHeight, screenPosition, out Vector3 point))
            {
                return false;
            }

            // 蓝图落位后不再移动，所以直接拿它的当前位置当中心
            Vector3 center = blueprint.transform.position;
            Vector2 footprint = blueprint.Footprint;
            float halfX = footprint.x * 0.5f + dropMargin;
            float halfZ = footprint.y * 0.5f + dropMargin;

            return Mathf.Abs(point.x - center.x) <= halfX
                && Mathf.Abs(point.z - center.z) <= halfZ;
        }

        private static bool IsRequiredBy(BuildingData data, MaterialData material)
        {
            return RequiredCountOf(data, material) > 0;
        }

        private static int RequiredCountOf(BuildingData data, MaterialData material)
        {
            if (data == null || material == null)
            {
                return 0;
            }

            IReadOnlyList<BuildingMaterialRequirement> requirements = data.RequiredMaterials;
            int total = 0;

            for (int i = 0; i < requirements.Count; i++)
            {
                if (requirements[i].IsValid && requirements[i].Material == material)
                {
                    total += requirements[i].Amount;
                }
            }

            return total;
        }

        private static string DescribeColors(IReadOnlyList<MaterialColor> colors)
        {
            if (colors == null || colors.Count == 0)
            {
                return "无";
            }

            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < colors.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append("、");
                }

                builder.Append(colors[i].ToChineseName());
            }

            return builder.ToString();
        }

        /// <summary>给玩家看的提示。有订阅者走事件，没有就写 Console，绝不静默吞掉。</summary>
        private void Report(string message, bool success)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (OnMessage != null)
            {
                OnMessage.Invoke(message, success);
                return;
            }

            if (success)
            {
                if (logConstructionFlow)
                {
                    Debug.Log($"[BuildingConstruction] {message}");
                }
            }
            else
            {
                Debug.LogWarning($"[BuildingConstruction] {message}", this);
            }
        }

        [ContextMenu("调试：撤回")]
        private void DebugUndo()
        {
            Undo();
        }

        [ContextMenu("调试：确认建造")]
        private void DebugConfirmBuild()
        {
            ConfirmBuild();
        }
    }
}
