using System;
using System.Collections.Generic;
using UnityEngine;
using Yujian.Core.Events;
using Yujian.Mining;
using Yujian.Shop;

namespace Yujian.Building
{
    /// <summary>
    /// 蓝图的两种相位。新增值一律追加到末尾。
    /// </summary>
    public enum BuildingPhase
    {
        /// <summary>蓝图跟着指针走，等玩家点蓝图右侧的 √ 落位。</summary>
        Placing = 0,

        /// <summary>蓝图已落位冻结，等玩家放入材料、再点 √ 建成。阶段 4 新增。</summary>
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
    /// 阶段 4 的流程：SelectBuilding（按住左键拖动蓝图）→ 蓝图旁的 √ 调 AnchorBlueprint（落位冻结）→
    /// BuildingConstruction 往蓝图里放材料 → 材料齐后 √ 调 BuildAtAnchor（生成实体建筑）。
    /// "点地面"不再是"立刻建出来"，中间多了填料这一步，这是阶段 4 与阶段 3 最大的区别。
    /// 2026-10-26 起，落位与建成都走 BuildingBlueprintActionUI 的按钮，不再复用 Player/Attack。
    /// </summary>
    public class BuildingManager : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("挖矿输入。蓝图在场期间会被临时禁用，" +
                 "避免玩家在建筑区点击时误挖矿石")]
        [SerializeField] private MiningInput miningInput;

        [Tooltip("建筑网格。落位时记录占地矩形，建成时登记占用，移除时释放。留空会报错")]
        [SerializeField] private BuildingGrid grid;

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
        private RectInt anchoredFootprintRect;
        private bool hasAnchor;

        private bool miningSuppressed;
        private bool miningEnabledBeforeSuppression;
        private bool miningRestorePending;

        /// <summary>
        /// 读档清场期间 > 0：此时移除建筑属于「系统清场」，不发布 BuildingRemoved。
        /// 否则音频会为每座被清掉的建筑响一次、等级会跟着往下掉。
        /// </summary>
        private int suppressRemovedEventDepth;

        /// <summary>
        /// 蓝图被销毁时触发（无论是取消、撤回还是建成）。触发时机在蓝图被 Destroy 之前。
        /// 参数 built = true 表示已经生成实体建筑，材料应视为已消耗；
        /// false 表示放弃，订阅方（BuildingConstruction）应把蓝图里的材料退回库存。
        /// </summary>
        public event Action<bool> OnBlueprintClosed;

        /// <summary>
        /// 一座已放置建筑被移除后触发（列表与占格都已释放，实体在本帧末销毁）。
        /// 读档前的批量清场也会触发，订阅方需要自行区分「玩家拆除」与「系统清场」。
        /// </summary>
        public event Action<GameObject> OnBuildingRemoved;

        /// <summary>当前是否处于放置模式（场上有蓝图）。落位后仍为 true，所以相机不会退出建造视角。</summary>
        public bool IsPlacing => currentBlueprint != null;

        /// <summary>当前相位。没有蓝图时恒为 Placing。</summary>
        public BuildingPhase Phase => currentBlueprint == null ? BuildingPhase.Placing : phase;

        /// <summary>蓝图是否已落位冻结、正在等玩家填料。</summary>
        public bool IsFilling => currentBlueprint != null && phase == BuildingPhase.Filling;

        /// <summary>
        /// 蓝图落位的世界坐标（已吸附到格点）。**实体建筑就生成在这个点上**，
        /// 而不是"蓝图销毁前恰好所在的 transform"。
        /// </summary>
        public Vector3 AnchoredPosition => anchoredPosition;

        /// <summary>
        /// 蓝图落位时占用的格子矩形（x 对应世界 X，y 对应世界 Z）。
        /// 建成时按这个矩形登记占用，拆除时按 ownerId 释放。
        /// </summary>
        public RectInt AnchoredFootprintRect => anchoredFootprintRect;

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

            if (grid == null)
            {
                Debug.LogError("[BuildingManager] BuildingGrid 未配置，蓝图无法落位，建筑也无法登记占用。" +
                               "请重新运行菜单「屿见/建筑/配置建筑网格与建造相机（阶段 7）」。", this);
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

            if (grid == null)
            {
                Debug.LogError("[BuildingManager] BuildingGrid 未配置，无法按格数换算占地并缩放蓝图。" +
                               "请重新运行菜单「屿见/建筑/配置建筑网格与建造相机（阶段 7）」。", this);
                return;
            }

            if (!data.HasValidFootprint)
            {
                Debug.LogError($"[BuildingManager] 建筑「{data.BuildingName}」的占地格数 {data.FootprintCells} 含非正数，" +
                               "无法放置。请在 BuildingData 资产里把两个方向的格数都设成正整数。", this);
                return;
            }

            if (currentBlueprint != null)
            {
                CancelSelection();
            }

            Vector2 worldFootprint = grid.CellsToWorldFootprint(data.FootprintCells);

            GameObject blueprintObject = Instantiate(
                data.BlueprintPrefab, defaultSpawnPosition, Quaternion.identity, blueprintRoot);

            blueprintObject.name = $"[蓝图] {data.BuildingName}";
            ApplyFootprintScale(blueprintObject, worldFootprint);

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
            blueprint.Initialize(data, worldFootprint);
            SetMiningSuppressed(true);

            EventBus.Publish(new BlueprintOpened(data));

            if (logBuildingFlow)
            {
                Debug.Log($"[BuildingManager] 已选中「{data.BuildingName}」（{data.BuildingType.ToChineseName()}），" +
                          $"占地 {data.FootprintCells.x}×{data.FootprintCells.y} 格" +
                          $"（世界尺寸 {worldFootprint.x}×{worldFootprint.y}），蓝图出现在 {defaultSpawnPosition}。" +
                          $"所需材料 {data.GetRequirementText()}。" +
                          "按住左键拖动蓝图到目标位置，松开后点蓝图右侧的 √ 落位，再把材料拖到蓝图上。");
            }
        }

        /// <summary>
        /// 取消当前选择：销毁蓝图、退出放置模式、恢复挖矿。
        /// 会先广播 OnBlueprintClosed(false)，BuildingConstruction 借此把蓝图里已放入的材料退回库存——
        /// 否则取消一次就凭空吞掉玩家的料。
        /// </summary>
        /// <param name="publishEvent">
        /// 是否向 EventBus 发布 BlueprintClosed。读档时用 false：
        /// 退料是存档恢复的一部分，不是玩家取消，音频不该响。
        /// 注意 OnBlueprintClosed 这个旧事件**始终**会触发，退料逻辑不受影响。
        /// </param>
        public void CancelSelection(bool publishEvent = true)
        {
            bool hadSelection = selectedBuilding != null || currentBlueprint != null;

            CloseBlueprint(false, publishEvent);

            selectedBuilding = null;
            phase = BuildingPhase.Placing;
            hasAnchor = false;
            anchoredFootprintRect = default;
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
        private void CloseBlueprint(bool built, bool publishEvent = true)
        {
            OnBlueprintClosed?.Invoke(built);

            if (publishEvent)
            {
                EventBus.Publish(new BlueprintClosed(built));
            }

            if (currentBlueprint != null)
            {
                Destroy(currentBlueprint.gameObject);
                currentBlueprint = null;
            }
        }

        /// <summary>
        /// 把当前蓝图落位到吸附后的格点上：蓝图停在这里不再跟鼠标，进入 Filling 相位等玩家填料。
        ///
        /// 阶段 4 起「点地面」不再等于「建出来」——中间多了填料与确认两步。
        /// 阶段 7 起落位的权威值是「吸附中心 + 占地格子矩形」，
        /// 建造与拆除都只认这两个值，不再从蓝图 transform 反推。
        ///
        /// 坐标与格子的有效性由 BuildingPlacement 判定，这里只做前置检查。
        /// </summary>
        /// <param name="snappedCenter">吸附后的建筑中心世界坐标。</param>
        /// <param name="footprintRect">该建筑占用的格子矩形。</param>
        /// <returns>是否落位成功。</returns>
        public bool AnchorBlueprint(Vector3 snappedCenter, RectInt footprintRect)
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

            if (grid == null)
            {
                Debug.LogError("[BuildingManager] BuildingGrid 未配置，蓝图不能落位。" +
                               "请重新运行菜单「屿见/建筑/配置建筑网格与建造相机（阶段 7）」。", this);
                return false;
            }

            Vector2Int expectedCells = selectedBuilding.FootprintCells;

            if (footprintRect.width != expectedCells.x || footprintRect.height != expectedCells.y)
            {
                Debug.LogError($"[BuildingManager] 落位矩形 {footprintRect} 与「{selectedBuilding.BuildingName}」" +
                               $"的占地格数 {expectedCells} 不一致，已拒绝落位。" +
                               "这通常意味着调用方没有使用 BuildingGrid.TryGetSnappedPose 的结果。", this);
                return false;
            }

            anchoredPosition = snappedCenter;
            anchoredFootprintRect = footprintRect;
            hasAnchor = true;
            phase = BuildingPhase.Filling;
            currentBlueprint.transform.position = snappedCenter;

            if (logBuildingFlow)
            {
                Debug.Log($"[BuildingManager] 蓝图已落位在 {snappedCenter}，占地格子 {footprintRect}，" +
                          $"所需材料 {selectedBuilding.GetRequirementText()}。" +
                          "现在把材料拖到蓝图上，凑齐后点蓝图右侧的 √ 建成。");
            }

            EventBus.Publish(new BlueprintAnchored(snappedCenter, footprintRect));

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
                Debug.LogWarning("[BuildingManager] 蓝图还没有落位，不能建造。请先点蓝图右侧的 √ 把蓝图放下。", this);
                return false;
            }

            BuildingData data = selectedBuilding;

            if (data.BuildingPrefab == null)
            {
                Debug.LogError($"[BuildingManager] 建筑「{data.BuildingName}」没有配置建筑 Prefab，无法建造。" +
                               "蓝图保持不动，材料还在里面，可以取消后重新选择。", this);
                return false;
            }

            if (grid == null)
            {
                Debug.LogError("[BuildingManager] BuildingGrid 未配置，无法登记占地，建造已取消。" +
                               "蓝图与材料保持不动。", this);
                return false;
            }

            // 落位与建造之间可能隔了很久（玩家慢慢填料），这里再确认一次格子仍然可用
            if (!grid.CanPlace(anchoredFootprintRect, out GridPlacementFailure failure))
            {
                Debug.LogWarning($"[BuildingManager] 落位格 {anchoredFootprintRect} 当前不可用：" +
                                 $"{failure.ToChineseName()}。建造已取消，蓝图与材料保持不动。", this);
                return false;
            }

            GameObject instance = Instantiate(
                data.BuildingPrefab, anchoredPosition, Quaternion.identity, buildingRoot);

            instance.name = data.BuildingName;
            ApplyFootprintScale(instance, grid.CellsToWorldFootprint(data.FootprintCells));
            ApplyBuildingLayer(instance);

            // 先占格再造楼：登记失败就立刻销毁实例，避免出现“看得见但网格里没记录”的建筑
            int ownerId = instance.GetInstanceID();

            if (!grid.Occupy(anchoredFootprintRect, ownerId))
            {
                Debug.LogError($"[BuildingManager] 建筑「{data.BuildingName}」的占地登记失败，已撤销本次建造。" +
                               "蓝图与材料保持不动。", instance);
                Destroy(instance);
                return false;
            }

            placedBuildings.Add(instance);

            ApplyLayerColors(instance, layerColors);
            AttachPlacedBuildingInfo(instance, data, anchoredFootprintRect, layerColors, 1);

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

            // 落位值马上会被清空，先留一份给事件用
            Vector3 placedPosition = anchoredPosition;
            RectInt placedRect = anchoredFootprintRect;

            // 先广播：材料已被消耗，BuildingConstruction 收到后清空历史，此后不能再通过蓝图撤回
            CloseBlueprint(true);
            selectedBuilding = null;
            phase = BuildingPhase.Placing;
            hasAnchor = false;
            anchoredFootprintRect = default;
            SetMiningSuppressed(false);

            // 状态全部提交后再发布：订阅者读到的 BuildingManager 已经是「建造完成」的状态
            EventBus.Publish(new BuildingPlaced(data, placedPosition, placedRect));

            return true;
        }

        /// <summary>
        /// 读档恢复：在指定格子矩形上直接生成一座已建成的建筑，并登记占地。
        ///
        /// 与 <see cref="BuildAtAnchor"/> 的区别：不经过蓝图、不触发 OnBlueprintClosed，
        /// 也不需要 selectedBuilding。生成后由调用方统一重算玩家等级。
        /// </summary>
        /// <param name="data">建筑静态数据。</param>
        /// <param name="rect">存档里的占地格子矩形。</param>
        /// <param name="layerColors">自下而上的楼层颜色。</param>
        /// <param name="level">存档里的建筑等级，旧存档传 0 或 1 都按 1 级处理。</param>
        /// <param name="error">失败原因，供存档参与者写日志。</param>
        public bool RestoreBuilding(
            BuildingData data, RectInt rect, IReadOnlyList<MaterialColor> layerColors,
            int level, out string error)
        {
            error = null;

            if (data == null)
            {
                error = "BuildingData 为空。";
                return false;
            }

            if (data.BuildingPrefab == null)
            {
                error = $"建筑「{data.BuildingName}」没有配置建筑 Prefab。";
                return false;
            }

            if (!data.HasValidFootprint)
            {
                error = $"建筑「{data.BuildingName}」的占地格数 {data.FootprintCells} 非法。";
                return false;
            }

            if (grid == null)
            {
                error = "BuildingGrid 未配置。";
                return false;
            }

            Vector2Int expectedCells = data.FootprintCells;

            if (rect.width != expectedCells.x || rect.height != expectedCells.y)
            {
                error = $"存档占格 {rect.width}×{rect.height} 与建筑「{data.BuildingName}」" +
                        $"配置的 {expectedCells.x}×{expectedCells.y} 不一致。";
                return false;
            }

            if (!grid.CanPlace(rect, out GridPlacementFailure failure))
            {
                error = $"占地格子 {rect} 当前不可用：{failure.ToChineseName()}。";
                return false;
            }

            Vector3 position = grid.CellRectCenter(rect);
            GameObject instance = Instantiate(
                data.BuildingPrefab, position, Quaternion.identity, buildingRoot);

            instance.name = data.BuildingName;
            ApplyFootprintScale(instance, grid.CellsToWorldFootprint(expectedCells));
            ApplyBuildingLayer(instance);

            int ownerId = instance.GetInstanceID();

            if (!grid.Occupy(rect, ownerId))
            {
                Destroy(instance);
                error = $"占地格子 {rect} 登记失败。";
                return false;
            }

            placedBuildings.Add(instance);
            ApplyLayerColors(instance, layerColors);
            AttachPlacedBuildingInfo(instance, data, rect, layerColors, level);

            if (instance.GetComponentInChildren<Collider>() == null)
            {
                Debug.LogWarning($"[BuildingManager] 恢复的建筑「{instance.name}」身上没有任何 Collider。" +
                                 "别的建筑检测不到它，会允许与它重叠放置。请给建筑预制体加一个 Collider。", instance);
            }

            if (logBuildingFlow)
            {
                Debug.Log($"[BuildingManager] 已恢复建筑「{data.BuildingName}」于 {position}，" +
                          $"占地 {rect}，共 {layerColors?.Count ?? 0} 层，" +
                          $"当前场上共 {placedBuildings.Count} 座。");
            }

            return true;
        }

        /// <summary>
        /// 移除全部已放置建筑并释放占地。读档前清场 / 重置存档时使用。
        /// 走 <see cref="RemoveBuilding"/> 统一释放占用，不直接 Destroy。
        /// </summary>
        /// <returns>实际移除的建筑数。</returns>
        public int ClearPlacedBuildings()
        {
            int removed = 0;
            suppressRemovedEventDepth++;

            try
            {
                for (int i = placedBuildings.Count - 1; i >= 0; i--)
                {
                    GameObject building = placedBuildings[i];

                    if (building == null)
                    {
                        placedBuildings.RemoveAt(i);
                        continue;
                    }

                    if (RemoveBuilding(building))
                    {
                        removed++;
                    }
                }
            }
            finally
            {
                suppressRemovedEventDepth--;
            }

            placedBuildings.Clear();

            if (logBuildingFlow)
            {
                Debug.Log($"[BuildingManager] 清场完成，共移除 {removed} 座建筑（读档 / 重置，不发布移除事件）。", this);
            }

            return removed;
        }

        /// <summary>把「属于哪座建筑、占哪几格、每层什么颜色、几级」写到实例上，供存档与升级读取。</summary>
        private static void AttachPlacedBuildingInfo(
            GameObject instance, BuildingData data, RectInt rect,
            IReadOnlyList<MaterialColor> layerColors, int level)
        {
            PlacedBuildingInfo info = instance.GetComponent<PlacedBuildingInfo>();

            if (info == null)
            {
                info = instance.AddComponent<PlacedBuildingInfo>();
            }

            info.Initialize(data, rect, layerColors, level);
        }

        /// <summary>
        /// 把一座已建成的建筑迁移到新位置（策划案 3.3）。
        ///
        /// 顺序：校验新占格（把自己的旧占格排除）→ 释放旧占格 → 登记新占格 →
        /// 更新实例信息与 transform → 广播 <see cref="BuildingMoved"/>。
        /// 任何一步失败都保持建筑在原位、占格不变。
        /// </summary>
        /// <param name="building">已放置在列表里的建筑实例。</param>
        /// <param name="snappedCenter">吸附后的新中心世界坐标。</param>
        /// <param name="newRect">新的占地格子矩形。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>是否迁移成功。</returns>
        public bool TryMoveBuilding(
            GameObject building, Vector3 snappedCenter, RectInt newRect, out string error)
        {
            error = null;

            if (building == null)
            {
                error = "建筑为空。";
                return false;
            }

            if (!placedBuildings.Contains(building))
            {
                error = $"「{building.name}」不在已放置建筑列表里。";
                return false;
            }

            if (grid == null)
            {
                error = "BuildingGrid 未配置。";
                return false;
            }

            PlacedBuildingInfo info = building.GetComponent<PlacedBuildingInfo>();

            if (info == null || info.Data == null)
            {
                error = $"「{building.name}」上没有 PlacedBuildingInfo 或建筑数据为空。";
                return false;
            }

            Vector2Int expectedCells = info.Data.FootprintCells;

            if (newRect.width != expectedCells.x || newRect.height != expectedCells.y)
            {
                error = $"新占格 {newRect.width}×{newRect.height} 与「{info.Data.BuildingName}」" +
                        $"配置的 {expectedCells.x}×{expectedCells.y} 不一致。";
                return false;
            }

            int ownerId = building.GetInstanceID();

            if (!grid.CanPlaceIgnoringOwner(newRect, ownerId, out GridPlacementFailure failure))
            {
                error = $"新位置不可放置：{failure.ToChineseName()}。";
                return false;
            }

            RectInt previousRect = info.FootprintRect;

            if (!grid.Release(ownerId))
            {
                Debug.LogWarning($"[BuildingManager] 建筑「{building.name}」没有网格占用记录，" +
                                 "迁移时跳过释放步骤。", this);
            }

            if (!grid.Occupy(newRect, ownerId))
            {
                // 恢复旧占格，别让建筑变成"看得见但网格里没记录"
                grid.Occupy(previousRect, ownerId);
                error = $"新占格 {newRect} 登记失败，已回滚到原位置。";
                return false;
            }

            info.SetFootprintRect(newRect);
            building.transform.position = snappedCenter;

            if (logBuildingFlow)
            {
                Debug.Log($"[BuildingManager] 已迁移「{building.name}」：{previousRect} → {newRect}，" +
                          $"新位置 {snappedCenter}。");
            }

            EventBus.Publish(new BuildingMoved(info.Data, previousRect, newRect, snappedCenter));

            return true;
        }

        /// <summary>
        /// 按占地世界尺寸缩放预制体实例：让它的渲染包围盒在 X/Z 上正好等于 <paramref name="worldFootprint"/>。
        ///
        /// 为什么不能直接拿 <c>transform.localScale</c> 当基准：
        /// <c>BuildingSpawnPop</c> 在 Instantiate 时的 OnEnable 里就把根节点缩到了 0.8 倍，
        /// 此时 <c>Renderer.bounds</c> 量到的是「弹出中的大小」。所以先读它记住的原始缩放
        /// （<c>BaseScale</c>）反推出自然尺寸，再算最终缩放，并同步更新 BaseScale，
        /// 否则弹出结束后建筑会跳回未按占地缩放的尺寸。
        ///
        /// 高度（Y）保持原样：占地只约束平面尺寸，建筑该多高由美术决定。
        /// 目前两条调用路径都用 Quaternion.identity 生成，所以世界 AABB 的 X/Z 就是占地轴；
        /// 若将来允许旋转根节点，这里需要改成按局部包围盒计算。
        /// </summary>
        private static void ApplyFootprintScale(GameObject target, Vector2 worldFootprint)
        {
            if (target == null)
            {
                return;
            }

            if (worldFootprint.x <= 0f || worldFootprint.y <= 0f)
            {
                Debug.LogError($"[BuildingManager] 「{target.name}」的占地世界尺寸 {worldFootprint} 无效，" +
                               "已跳过自动缩放。请检查 BuildingData.FootprintCells。", target);
                return;
            }

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds bounds = default;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];

                // 粒子/拖尾/线段是表现件，包围盒随特效变化，不能代表建筑本体尺寸
                if (renderer == null ||
                    renderer is ParticleSystemRenderer ||
                    renderer is TrailRenderer ||
                    renderer is LineRenderer)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds || bounds.size.x <= 0f || bounds.size.z <= 0f)
            {
                Debug.LogWarning($"[BuildingManager] 「{target.name}」上找不到可测量尺寸的 Renderer，" +
                                 "已跳过按占地自动缩放，建筑外观可能与占地格数不符。", target);
                return;
            }

            // Instantiate 时 BuildingSpawnPop 可能已经把根节点缩到 fromScale 倍，
            // 所以要用它记住的原始缩放反推自然尺寸，而不是用当前 localScale。
            Vector3 applied = target.transform.localScale;
            BuildingSpawnPop pop = target.GetComponent<BuildingSpawnPop>();
            Vector3 reference = pop != null ? pop.BaseScale : applied;

            if (Mathf.Approximately(applied.x, 0f) || Mathf.Approximately(applied.z, 0f))
            {
                Debug.LogWarning($"[BuildingManager] 「{target.name}」的 localScale X/Z 为 0，无法换算自然尺寸，" +
                                 "已跳过按占地自动缩放。", target);
                return;
            }

            Vector2 naturalSize = new Vector2(
                bounds.size.x * reference.x / applied.x,
                bounds.size.z * reference.z / applied.z);

            if (naturalSize.x <= 0f || naturalSize.y <= 0f)
            {
                Debug.LogWarning($"[BuildingManager] 「{target.name}」的自然尺寸 {naturalSize} 无效，" +
                                 "已跳过按占地自动缩放。", target);
                return;
            }

            Vector3 newScale = new Vector3(
                reference.x * (worldFootprint.x / naturalSize.x),
                reference.y,
                reference.z * (worldFootprint.y / naturalSize.y));

            target.transform.localScale = newScale;

            if (pop != null)
            {
                pop.SetBaseScale(newScale);
            }
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
                               "请运行菜单「屿见/建筑/配置建造系统（阶段 4）」重建建筑预制体。", instance);
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

            // 释放占格。找不到记录只警告、不阻止移除：旧建筑或外部生成的建筑也应该能被拆掉
            if (grid != null && !grid.Release(building.GetInstanceID()))
            {
                Debug.LogWarning($"[BuildingManager] 「{building.name}」没有网格占用记录" +
                                 "（可能是旧建筑或外部生成），仍然移除。", this);
            }

            if (logBuildingFlow)
            {
                Debug.Log($"[BuildingManager] 已移除建筑「{building.name}」，当前占地已释放，" +
                          $"场上共 {placedBuildings.Count} 座。");
            }

            // 广播放在 Destroy 之前：此时列表与占格都已经更新，订阅方立刻存档也不会写进旧状态
            OnBuildingRemoved?.Invoke(building);

            if (suppressRemovedEventDepth == 0)
            {
                PlacedBuildingInfo info = building.GetComponent<PlacedBuildingInfo>();
                EventBus.Publish(new BuildingRemoved(
                    info != null ? info.Data : null, building.transform.position));
            }

            Destroy(building);
            return true;
        }

        /// <summary>
        /// 蓝图在场期间临时关掉挖矿输入。
        /// 用组件的 enabled 而不是去改 MiningInput 的代码：MiningInput 的 OnEnable/OnDisable
        /// 会自行订阅、退订 InputAction，禁用组件就等价于"它不再响应点击"，已有脚本零改动。
        ///
        /// 2026-10-26 之后建筑落位不再复用 Player/Attack（改为蓝图旁的 √），
        /// 因此这里禁用的就只是挖矿本身，不再有共享 InputAction 被连带关掉的问题。
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

            // 不在这里立刻恢复：本方法通常是在 UI 点击回调里被调用的
            // （蓝图旁的 √ → AnchorBlueprint → 这里）。当场重新启用会让 MiningInput
            // 在同一次输入事件派发过程中重新订阅 Player/Attack，可能让这次点击又落成一次挖矿。
            // 统一延到本帧末尾恢复，时序上就不可能"同一次点击既落位又挖矿"。
            miningSuppressed = false;

            // 只有“屏蔽时它本来是开着的”才需要由本类打开。
            //
            // 反例（阶段 7 实测撞到）：玩家在建筑区开着蓝图，直接点底部「挖矿」切走。
            // GameAreaController 会先把 MiningInput 关掉（那是区域门关的，不是本类），
            // 随后 AreaCameraDirector 退出建造视角 → CancelSelection → 走到这里。
            // 若照旧记下 pending = true 并在 LateUpdate 里恢复成“屏蔽前”的 false，
            // 就会把刚切回挖矿区、已被区域门重新打开的 MiningInput 又关掉，
            // 表现是回来以后怎么点都挖不了矿，且 Console 一句都不报。
            //
            // 屏蔽前本来就是关的 → 本类没有关过它 → 释放时也不要替别的系统写值。
            miningRestorePending = miningEnabledBeforeSuppression;
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
