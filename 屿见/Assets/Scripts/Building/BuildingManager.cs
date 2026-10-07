using System;
using System.Collections.Generic;
using UnityEngine;
using Yujian.Mining;
using Yujian.Shop;

namespace Yujian.Building
{
    /// <summary>
    /// 蓝图的两种相位。新增值一律追加到末尾。
    /// </summary>
    public enum BuildingPhase
    {
        /// <summary>蓝图跟着鼠标走，等玩家点击地面落位。阶段 3 的行为。</summary>
        Placing = 0,

        /// <summary>蓝图已落位冻结，等玩家放入材料、再确认建造。阶段 4 新增。</summary>
        Filling = 1,
    }

    /// <summary>
    /// 建筑系统入口。职责：
    /// 1. 记住"玩家当前选中了哪个建筑"、"当前有没有蓝图在场"、"已经放了哪些建筑"、"蓝图落位在哪"；
    /// 2. 生成 / 销毁蓝图；
    /// 3. 蓝图落位（AnchorBlueprint）；
    /// 4. 按材料颜色生成实体建筑（BuildAtAnchor）。
    ///
    /// 不做坐标换算与重叠检测（BuildingPlacement 负责），不决定该放哪几块料（BuildingConstruction 负责）。
    ///
    /// 阶段 4 的流程：SelectBuilding（蓝图跟鼠标）→ AnchorBlueprint（落位冻结）→
    /// BuildingConstruction 往蓝图里放材料 → BuildAtAnchor（生成实体建筑）。
    /// "点地面"不再是"立刻建出来"，中间多了填料这一步，这是阶段 4 与阶段 3 最大的区别。
    /// </summary>
    public class BuildingManager : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("挖矿输入。放置蓝图期间会被临时禁用，" +
                 "否则同一次左键点击会同时触发「放置建筑」和「挖矿」")]
        [SerializeField] private MiningInput miningInput;

        [Header("图层")]
        [Tooltip("建筑所在图层，重叠检测只查这些层。请只勾 Building。" +
                 "整个工程只在这一处配置建筑层，BuildingPlacement 会读这里")]
        [SerializeField] private LayerMask buildingLayers;

        [Header("放置")]
        [Tooltip("蓝图刚生成时的位置，通常是地图中心。场景里 Plane 的中心是 (10.217, 0, -9.492)。" +
                 "蓝图生成后下一帧就会被鼠标接管")]
        [SerializeField] private Vector3 defaultSpawnPosition = Vector3.zero;

        [Tooltip("已放置建筑的父物体，留空则放在场景根层级")]
        [SerializeField] private Transform buildingRoot;

        [Tooltip("蓝图实例的父物体，留空则放在场景根层级。它的子物体会污染 Hierarchy，建议指定一个空物体")]
        [SerializeField] private Transform blueprintRoot;

        [Header("已放置建筑（运行时自动增删，此处仅供查看）")]
        [SerializeField] private List<GameObject> placedBuildings = new List<GameObject>();

        [Header("调试")]
        [Tooltip("是否在 Console 打印选择 / 放置 / 取消的日志")]
        [SerializeField] private bool logBuildingFlow = true;

        [Tooltip("供 Component 右键菜单快速测试用。Inspector 里指定后，" +
                 "在 Play Mode 右键组件标题 → 「调试：选择测试建筑」即可生成蓝图，不必先做 UI")]
        [SerializeField] private BuildingData testBuilding;

        private BuildingData selectedBuilding;
        private BuildingBlueprint currentBlueprint;

        private BuildingPhase phase = BuildingPhase.Placing;
        private Vector3 anchoredPosition;
        private bool hasAnchor;

        private bool miningSuppressed;
        private bool miningEnabledBeforeSuppression;
        private bool miningRestorePending;

        /// <summary>
        /// 蓝图被销毁时触发（无论是取消、撤回还是建成）。触发时机在蓝图被 Destroy 之前。
        /// 参数 built = true 表示已经生成实体建筑，材料应视为已消耗；
        /// false 表示放弃，订阅方（BuildingConstruction）应把蓝图里的材料退回库存。
        /// </summary>
        public event Action<bool> OnBlueprintClosed;

        /// <summary>当前是否处于放置模式（场上有蓝图）。落位后仍为 true，所以相机不会退出建造视角。</summary>
        public bool IsPlacing => currentBlueprint != null;

        /// <summary>当前相位。没有蓝图时恒为 Placing。</summary>
        public BuildingPhase Phase => currentBlueprint == null ? BuildingPhase.Placing : phase;

        /// <summary>蓝图是否已落位冻结、正在等玩家填料。</summary>
        public bool IsFilling => currentBlueprint != null && phase == BuildingPhase.Filling;

        /// <summary>
        /// 蓝图落位的世界坐标。**实体建筑就生成在这个点上**，
        /// 而不是"蓝图销毁前恰好所在的 transform"。将来接入地图方格时，
        /// 吸附与占用登记只需要认这一个值。
        /// </summary>
        public Vector3 AnchoredPosition => anchoredPosition;

        /// <summary>当前选中的建筑数据，没有则为 null。</summary>
        public BuildingData SelectedBuilding => selectedBuilding;

        /// <summary>当前跟随鼠标的蓝图，没有则为 null。</summary>
        public BuildingBlueprint CurrentBlueprint => currentBlueprint;

        /// <summary>已放置的建筑，只读。</summary>
        public IReadOnlyList<GameObject> PlacedBuildings => placedBuildings;

        /// <summary>建筑图层，只读暴露给 BuildingPlacement 做重叠检测，避免两处各配一份。</summary>
        public LayerMask BuildingLayers => buildingLayers;

        private void Awake()
        {
            if (miningInput == null)
            {
                Debug.LogWarning("[BuildingManager] MiningInput 未配置。放置蓝图期间挖矿不会被屏蔽，" +
                                 "同一次点击会既放建筑又挖矿。", this);
            }

            if (buildingLayers.value == 0)
            {
                Debug.LogError("[BuildingManager] Building Layers 没有勾选任何图层，重叠检测会永远查不到建筑。" +
                               "请到 Project Settings → Tags and Layers 新建一个 Building 层（索引 7 起有空位），" +
                               "勾到这里，并把建筑预制体也放到该层。", this);
            }
        }

        private void OnDisable()
        {
            // 组件被禁用时若还留着蓝图，必须把挖矿恢复回去，否则 MiningInput 会一直是关的
            if (currentBlueprint != null)
            {
                CancelSelection();
            }
            else
            {
                SetMiningSuppressed(false);
            }

            // 本组件被禁用后 LateUpdate 不再执行，欠着的那次恢复必须当场落实
            ApplyPendingMiningRestore();
        }

        /// <summary>
        /// 选中一个建筑：生成蓝图并进入放置模式。
        /// 已有蓝图时会先取消旧的（切换建筑），不会同时存在两个蓝图。
        /// </summary>
        public void SelectBuilding(BuildingData data)
        {
            if (data == null)
            {
                Debug.LogError("[BuildingManager] SelectBuilding 收到空的 BuildingData，选择取消。", this);
                return;
            }

            if (data.BlueprintPrefab == null)
            {
                Debug.LogError($"[BuildingManager] 建筑「{data.BuildingName}」没有配置蓝图 Prefab，无法选择。" +
                               "请在 BuildingData 资产里指定 blueprintPrefab。", this);
                return;
            }

            if (currentBlueprint != null)
            {
                CancelSelection();
            }

            GameObject blueprintObject = Instantiate(
                data.BlueprintPrefab, defaultSpawnPosition, Quaternion.identity, blueprintRoot);

            blueprintObject.name = $"[蓝图] {data.BuildingName}";

            BuildingBlueprint blueprint = blueprintObject.GetComponent<BuildingBlueprint>();

            if (blueprint == null)
            {
                Debug.LogError($"[BuildingManager] 蓝图 Prefab「{data.BlueprintPrefab.name}」的根物体上没有 " +
                               "BuildingBlueprint 组件，蓝图无法工作，已销毁这个实例。", this);
                Destroy(blueprintObject);
                return;
            }

            currentBlueprint = blueprint;
            selectedBuilding = data;
            phase = BuildingPhase.Placing;
            hasAnchor = false;
            blueprint.Initialize(data);
            SetMiningSuppressed(true);

            if (logBuildingFlow)
            {
                Debug.Log($"[BuildingManager] 已选中「{data.BuildingName}」（{data.BuildingType.ToChineseName()}），" +
                          $"占地 {data.Footprint.x}×{data.Footprint.y}，蓝图出现在 {defaultSpawnPosition}。" +
                          $"所需材料 {data.GetRequirementText()}。" +
                          "移动鼠标调整位置，点击地面落位，然后在材料面板里把材料拖到蓝图上。");
            }
        }

        /// <summary>
        /// 取消当前选择：销毁蓝图、退出放置模式、恢复挖矿。
        /// 会先广播 OnBlueprintClosed(false)，BuildingConstruction 借此把蓝图里已放入的材料退回库存——
        /// 否则取消一次就凭空吞掉玩家的料。
        /// </summary>
        public void CancelSelection()
        {
            bool hadSelection = selectedBuilding != null || currentBlueprint != null;

            CloseBlueprint(false);

            selectedBuilding = null;
            phase = BuildingPhase.Placing;
            hasAnchor = false;
            SetMiningSuppressed(false);

            if (hadSelection && logBuildingFlow)
            {
                Debug.Log("[BuildingManager] 已取消当前建筑蓝图，蓝图里已放入的材料已退回库存。");
            }
        }

        /// <summary>
        /// 关闭当前蓝图：先广播 OnBlueprintClosed 让订阅方处理材料，再销毁蓝图物体。
        /// 所有销毁蓝图的路径都必须走这里，否则材料结算会被漏掉。
        /// </summary>
        private void CloseBlueprint(bool built)
        {
            OnBlueprintClosed?.Invoke(built);

            if (currentBlueprint != null)
            {
                Destroy(currentBlueprint.gameObject);
                currentBlueprint = null;
            }
        }

        /// <summary>
        /// 把当前蓝图落位到指定世界坐标：蓝图停在这里不再跟鼠标，进入 Filling 相位等玩家填料。
        ///
        /// 阶段 4 起「点地面」不再等于「建出来」——中间多了填料与确认两步。
        /// 这么拆也是为了将来接入地图方格：「选格落位」与「建造」本来就是两件事。
        /// 坐标的有效性由 BuildingPlacement 判定，这里只做前置检查。
        /// </summary>
        /// <param name="position">落位点的世界坐标（地面上的点）。</param>
        /// <returns>是否落位成功。</returns>
        public bool AnchorBlueprint(Vector3 position)
        {
            if (selectedBuilding == null || currentBlueprint == null)
            {
                Debug.LogWarning("[BuildingManager] 当前没有待落位的蓝图，AnchorBlueprint 被忽略。", this);
                return false;
            }

            if (phase == BuildingPhase.Filling)
            {
                // 幂等：已经落位了就别再挪它，免得玩家拖材料时手抖点到地面把蓝图挪走
                return true;
            }

            anchoredPosition = position;
            hasAnchor = true;
            phase = BuildingPhase.Filling;
            currentBlueprint.transform.position = position;

            if (logBuildingFlow)
            {
                Debug.Log($"[BuildingManager] 蓝图已落位在 {position}，" +
                          $"所需材料 {selectedBuilding.GetRequirementText()}。" +
                          "现在把材料拖到蓝图上，凑齐后点「确认建造」。");
            }

            return true;
        }

        /// <summary>
        /// 在落位点生成实体建筑，并把每块材料的颜色刷到对应的楼层上。
        /// 材料够不够由 BuildingConstruction 判定，本类不重复校验。
        /// </summary>
        /// <param name="layerColors">自下而上的楼层颜色，长度 = 放进去的材料块数。</param>
        /// <returns>是否建造成功。</returns>
        public bool BuildAtAnchor(IReadOnlyList<MaterialColor> layerColors)
        {
            if (selectedBuilding == null || currentBlueprint == null)
            {
                Debug.LogWarning("[BuildingManager] 当前没有待建造的蓝图，BuildAtAnchor 被忽略。", this);
                return false;
            }

            if (phase != BuildingPhase.Filling || !hasAnchor)
            {
                Debug.LogWarning("[BuildingManager] 蓝图还没有落位，不能建造。请先点击地面把蓝图放下。", this);
                return false;
            }

            BuildingData data = selectedBuilding;

            if (data.BuildingPrefab == null)
            {
                Debug.LogError($"[BuildingManager] 建筑「{data.BuildingName}」没有配置建筑 Prefab，无法建造。" +
                               "蓝图保持不动，材料还在里面，可以取消后重新选择。", this);
                return false;
            }

            GameObject instance = Instantiate(
                data.BuildingPrefab, anchoredPosition, Quaternion.identity, buildingRoot);

            instance.name = data.BuildingName;
            ApplyBuildingLayer(instance);
            placedBuildings.Add(instance);

            ApplyLayerColors(instance, layerColors);

            if (instance.GetComponentInChildren<Collider>() == null)
            {
                Debug.LogWarning($"[BuildingManager] 建筑「{instance.name}」身上没有任何 Collider。" +
                                 "别的建筑检测不到它，会允许与它重叠放置。请给建筑预制体加一个 Collider。", instance);
            }

            if (logBuildingFlow)
            {
                Debug.Log($"[BuildingManager] 已在 {anchoredPosition} 建成「{data.BuildingName}」，" +
                          $"共 {layerColors?.Count ?? 0} 层，当前场上共 {placedBuildings.Count} 座建筑。");
            }

            // 先广播：材料已被消耗，BuildingConstruction 收到后清空历史，此后不能再通过蓝图撤回
            CloseBlueprint(true);
            selectedBuilding = null;
            phase = BuildingPhase.Placing;
            hasAnchor = false;
            SetMiningSuppressed(false);

            return true;
        }

        /// <summary>
        /// 把材料颜色刷到建筑的楼层上。
        /// 预制体上没挂 BuildingLayerStack 时明确报错，不静默——否则建筑会以预制体原样出现，
        /// 玩家看不出"颜色没生效"和"这块料本来就是这颜色"的区别。
        /// </summary>
        private void ApplyLayerColors(GameObject instance, IReadOnlyList<MaterialColor> layerColors)
        {
            BuildingLayerStack stack = instance.GetComponent<BuildingLayerStack>();

            if (stack == null)
            {
                Debug.LogError($"[BuildingManager] 建筑预制体「{instance.name}」的根物体上没有 BuildingLayerStack 组件，" +
                               "楼层不会显示任何颜色。\n" +
                               "请运行菜单「屿见/配置建造系统（阶段 4）」重建建筑预制体。", instance);
                return;
            }

            stack.Apply(layerColors);
        }

        /// <summary>
        /// 移除一座已放置的建筑。
        /// 只接受本类自己记录过的对象：传入列表外的物体时只警告、不销毁，避免误删场景里的其他东西。
        /// </summary>
        /// <returns>是否移除成功。</returns>
        public bool RemoveBuilding(GameObject building)
        {
            if (building == null)
            {
                Debug.LogWarning("[BuildingManager] RemoveBuilding 收到空引用，未做任何事。", this);
                return false;
            }

            if (!placedBuildings.Remove(building))
            {
                Debug.LogWarning($"[BuildingManager] 「{building.name}」不在已放置建筑列表里，" +
                                 "为避免误删，本次调用不销毁它。", this);
                return false;
            }

            if (logBuildingFlow)
            {
                Debug.Log($"[BuildingManager] 已移除建筑「{building.name}」，当前场上共 {placedBuildings.Count} 座。");
            }

            Destroy(building);
            return true;
        }

        /// <summary>
        /// 放置期间临时关掉挖矿输入。
        /// 用组件的 enabled 而不是去改 MiningInput 的代码：MiningInput 的 OnEnable/OnDisable
        /// 会自行订阅、退订 InputAction，禁用组件就等价于"它不再响应点击"，已有脚本零改动。
        ///
        /// ⚠ 代价：MiningInput.OnDisable() 还会调用那个共享动作的 Disable()，而 InputAction
        /// 的 Enable/Disable **不是引用计数**的（Enable() 内部就是 `if (enabled) return;`）。
        /// 所以这一关会把 BuildingPlacement 对同一个 Player/Attack 动作的订阅一并关掉——
        /// 由 BuildingPlacement.EnsureConfirmActionEnabled() 在 LateUpdate 里补回来。
        /// 这是一处真实的耦合：改这里之前先看那个方法。
        /// </summary>
        private void SetMiningSuppressed(bool suppress)
        {
            if (miningInput == null)
            {
                return;
            }

            if (suppress)
            {
                if (miningSuppressed)
                {
                    return;
                }

                // 记住玩家原本的状态，恢复时不要擅自把玩家自己关掉的组件打开。
                // 若本帧末尾还欠着一次恢复，说明组件此刻仍是"被我们关掉的"，此时不能再采样一次状态
                if (!miningRestorePending)
                {
                    miningEnabledBeforeSuppression = miningInput.enabled;
                }

                miningRestorePending = false;
                miningSuppressed = true;
                miningInput.enabled = false;
                return;
            }

            if (!miningSuppressed)
            {
                return;
            }

            // 不在这里立刻恢复：本方法可能是从 Player/Attack 的一次 performed 回调里被调用的
            // （BuildingPlacement 确认落位 → AnchorBlueprint → 这里）。当场重新启用会让 MiningInput
            // 在同一次回调派发过程中重新订阅该动作，"同一次点击会不会再挖一次矿"取决于 Input System
            // 内部的派发快照实现——这条我无法在本地实证，所以统一延到本帧末尾恢复，
            // 让"确认放置的那一次点击"在时序上不可能同时触发挖矿。
            miningSuppressed = false;
            miningRestorePending = true;
        }

        private void LateUpdate()
        {
            ApplyPendingMiningRestore();
        }

        private void OnDestroy()
        {
            // 组件被销毁时如果还欠着一次恢复，也必须落实，否则 MiningInput 会一直是关的
            ApplyPendingMiningRestore();
        }

        /// <summary>把延后的挖矿恢复真正落实，保证它发生在任何输入回调派发之外。</summary>
        private void ApplyPendingMiningRestore()
        {
            if (!miningRestorePending || miningInput == null)
            {
                return;
            }

            miningRestorePending = false;

            // 本帧内又进入了放置模式，那就继续保持屏蔽
            if (miningSuppressed)
            {
                return;
            }

            miningInput.enabled = miningEnabledBeforeSuppression;
        }

        /// <summary>
        /// 把实体建筑强制放到建筑层：预制体可能被随手放在 Default 层，那样重叠检测查不到它，
        /// 会静默地允许重叠放置。这里不改预制体资产，只改运行时实例。
        /// </summary>
        private void ApplyBuildingLayer(GameObject instance)
        {
            int layer = FirstLayerIndex(buildingLayers);

            if (layer < 0)
            {
                Debug.LogError($"[BuildingManager] Building Layers 未勾选任何图层，" +
                               $"「{instance.name}」不会被放到建筑层，后续的重叠检测查不到它。", this);
                return;
            }

            if (instance.layer != layer && logBuildingFlow)
            {
                Debug.Log($"[BuildingManager] 「{instance.name}」预制体原本在 " +
                          $"{DescribeLayer(instance.layer)} 层，已强制设为 {DescribeLayer(layer)} 层" +
                          "（只改运行时实例，不动预制体资产）。");
            }

            SetLayerRecursively(instance, layer);
        }

        /// <summary>取 LayerMask 里第一个（索引最小的）图层下标，没有勾选则返回 -1。</summary>
        private static int FirstLayerIndex(LayerMask mask)
        {
            for (int i = 0; i < 32; i++)
            {
                if ((mask.value & (1 << i)) != 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void SetLayerRecursively(GameObject target, int layer)
        {
            target.layer = layer;

            Transform transform = target.transform;

            for (int i = 0; i < transform.childCount; i++)
            {
                SetLayerRecursively(transform.GetChild(i).gameObject, layer);
            }
        }

        private static string DescribeLayer(int layer)
        {
            string layerName = LayerMask.LayerToName(layer);
            return string.IsNullOrEmpty(layerName) ? $"Layer {layer}" : layerName;
        }

        [ContextMenu("调试：选择测试建筑")]
        private void DebugSelectTestBuilding()
        {
            if (testBuilding == null)
            {
                Debug.LogError("[BuildingManager] 没有指定测试建筑（Test Building 字段为空），无法快速测试。", this);
                return;
            }

            SelectBuilding(testBuilding);
        }

        [ContextMenu("调试：取消当前蓝图")]
        private void DebugCancelSelection()
        {
            CancelSelection();
        }
    }
}
