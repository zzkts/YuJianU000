using System.Collections.Generic;
using UnityEngine;
using Yujian.Mining;

namespace Yujian.Building
{
    /// <summary>
    /// 建筑系统入口。职责：
    /// 1. 记住"玩家当前选中了哪个建筑"、"当前有没有蓝图在场"、"已经放了哪些建筑"；
    /// 2. 生成 / 销毁蓝图；
    /// 3. 确认放置时生成实体建筑。
    ///
    /// 不做坐标换算与重叠检测（BuildingPlacement 负责），不消耗材料（阶段 4 才做）。
    /// 阶段 3 的建材是"只记录不结算"的，即使玩家零原料也允许放置。
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

        private bool miningSuppressed;
        private bool miningEnabledBeforeSuppression;
        private bool miningRestorePending;

        /// <summary>当前是否处于放置模式（场上有蓝图）。</summary>
        public bool IsPlacing => currentBlueprint != null;

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
            blueprint.Initialize(data);
            SetMiningSuppressed(true);

            if (logBuildingFlow)
            {
                Debug.Log($"[BuildingManager] 已选中「{data.BuildingName}」（{data.BuildingType.ToChineseName()}），" +
                          $"占地 {data.Footprint.x}×{data.Footprint.y}，蓝图出现在 {defaultSpawnPosition}。" +
                          $"所需材料 {data.GetRequirementText()} —— 阶段 3 不消耗材料，零原料也可放置。" +
                          "移动鼠标调整位置，点击确认放置。");
            }
        }

        /// <summary>
        /// 取消当前选择：销毁蓝图、退出放置模式、恢复挖矿。
        /// 阶段 3 的取消不涉及任何材料退回——因为阶段 3 根本没扣过材料。
        /// </summary>
        public void CancelSelection()
        {
            bool hadSelection = selectedBuilding != null || currentBlueprint != null;

            if (currentBlueprint != null)
            {
                Destroy(currentBlueprint.gameObject);
                currentBlueprint = null;
            }

            selectedBuilding = null;
            SetMiningSuppressed(false);

            if (hadSelection && logBuildingFlow)
            {
                Debug.Log("[BuildingManager] 已取消当前建筑蓝图，未消耗也未退回任何材料（阶段 3 不结算材料）。");
            }
        }

        /// <summary>
        /// 在指定世界坐标放置当前选中的建筑：销毁蓝图 → 生成实体建筑 → 退出放置模式。
        /// 坐标的有效性由 BuildingPlacement 判定，这里只做"有没有蓝图可放"的前置检查，
        /// 以及"建筑预制体存在吗"这类只有本类才知道的检查。
        /// </summary>
        /// <param name="position">放置点的世界坐标（地面上的点）。</param>
        /// <returns>是否放置成功。</returns>
        public bool PlaceBuilding(Vector3 position)
        {
            if (selectedBuilding == null || currentBlueprint == null)
            {
                Debug.LogWarning("[BuildingManager] 当前没有待放置的蓝图，PlaceBuilding 被忽略。", this);
                return false;
            }

            BuildingData data = selectedBuilding;

            if (data.BuildingPrefab == null)
            {
                Debug.LogError($"[BuildingManager] 建筑「{data.BuildingName}」没有配置建筑 Prefab，无法放置。" +
                               "蓝图保持不动，可以取消后重新选择。", this);
                return false;
            }

            GameObject instance = Instantiate(
                data.BuildingPrefab, position, Quaternion.identity, buildingRoot);

            instance.name = data.BuildingName;
            ApplyBuildingLayer(instance);
            placedBuildings.Add(instance);

            if (instance.GetComponentInChildren<Collider>() == null)
            {
                Debug.LogWarning($"[BuildingManager] 建筑「{instance.name}」身上没有任何 Collider。" +
                                 "别的建筑检测不到它，会允许与它重叠放置。请给建筑预制体加一个 Collider。", instance);
            }

            if (logBuildingFlow)
            {
                Debug.Log($"[BuildingManager] 已在 {position} 放置「{data.BuildingName}」，" +
                          $"当前场上共 {placedBuildings.Count} 座建筑。未消耗任何材料（阶段 4 才扣料）。");
            }

            Destroy(currentBlueprint.gameObject);
            currentBlueprint = null;
            selectedBuilding = null;
            SetMiningSuppressed(false);

            return true;
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
            // （BuildingPlacement 确认放置 → PlaceBuilding → 这里）。当场重新启用会让 MiningInput
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
