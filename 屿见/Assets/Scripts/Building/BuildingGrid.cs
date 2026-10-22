using System.Collections.Generic;
using UnityEngine;
using Yujian.Island;

namespace Yujian.Building
{
    /// <summary>网格判定的失败原因。日志与 UI 都应说明具体是哪一条，不能只说“不能放”。</summary>
    public enum GridPlacementFailure
    {
        None = 0,
        NotConfigured = 1,
        InvalidFootprint = 2,
        OutOfBounds = 3,
        Occupied = 4,
        NotBuildable = 5,
    }

    /// <summary>GridPlacementFailure 的中文描述。只用于日志与提示文字。</summary>
    public static class GridPlacementFailureUtility
    {
        public static string ToChineseName(this GridPlacementFailure failure)
        {
            switch (failure)
            {
                case GridPlacementFailure.None:
                    return "可放置";
                case GridPlacementFailure.NotConfigured:
                    return "网格未配置";
                case GridPlacementFailure.InvalidFootprint:
                    return "占地尺寸非法";
                case GridPlacementFailure.OutOfBounds:
                    return "超出网格范围";
                case GridPlacementFailure.Occupied:
                    return "格子已被占用";
                case GridPlacementFailure.NotBuildable:
                    return "不在可建台地上";
                default:
                    return failure.ToString();
            }
        }
    }

    /// <summary>
    /// 建筑网格/占地系统。职责：
    /// 1. 维护格子边长、原点、宽高格数；
    /// 2. 世界坐标 ↔ 格子坐标换算；
    /// 3. 计算每格是否在岛屿的可建台地上；
    /// 4. 维护“哪些格已被建筑占用”；
    /// 5. 提供唯一的“这里能不能放”判定 <see cref="CanPlace"/>。
    ///
    /// 不做输入、不生成/销毁建筑、不画东西。蓝图跟手由 BuildingPlacement 负责，
    /// 占地高亮由 BuildingGridVisualizer 负责。
    ///
    /// 坐标约定：格子用 Vector2Int(x, y) 表示，x 对应世界 X，y 对应世界 Z。
    /// RectInt 的 y 同样对应世界 Z，宽度沿 X、高度沿 Z。
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildingGrid : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("场景里的海岛描述组件，用于自动取台地中心与范围。留空会报错，网格无法自动适配")]
        [SerializeField] private IslandDefinition islandDefinition;

        [Tooltip("海岛生成参数资产，用于按圆角台地精确判定可建区域。" +
                 "留空时退化为 IslandDefinition 的矩形判断，圆角处可能允许放到沙滩上")]
        [SerializeField] private IslandSettingsAsset islandSettings;

        [Header("网格")]
        [Tooltip("网格中心，通常等于海岛可建台地中心")]
        [SerializeField] private Vector3 gridCenter = new Vector3(10.21735f, 0f, -9.49205f);

        [Tooltip("单格边长（世界单位）。风琴博物馆占地 4×4，边长为 2 时占 2×2 格")]
        [SerializeField] private float cellSize = 2f;

        [Tooltip("网格宽高格数。自动适配海岛时由台地半尺寸 / 边长算出")]
        [SerializeField] private Vector2Int cellCounts = new Vector2Int(52, 52);

        [Tooltip("勾选后按 IslandDefinition 的台地中心与半尺寸自动重算中心与格数。" +
                 "手动调整格数时请关掉")]
        [SerializeField] private bool autoFitToIsland = true;

        [Tooltip("可建区域向内收缩的距离。0 = 格子完全落在台地边界内即可建；" +
                 "调大可以把靠边的格子也排除掉")]
        [SerializeField] private float buildableInset = 0f;

        [Header("调试")]
        [Tooltip("是否在 Console 打印网格初始化与占用登记日志")]
        [SerializeField] private bool logGridFlow = true;

        // 占用表：下标 = x + y * cellCounts.x
        private bool[] occupied;
        private bool[] buildableMask;

        // ownerId（建筑实例的 GetInstanceID）→ 占地矩形，用于拆除时精确释放
        private readonly Dictionary<int, RectInt> ownerRects = new Dictionary<int, RectInt>();

        private Vector3 origin;
        private bool configured;
        private bool warnedAboutMissingSettings;
        private bool warnedAboutMissingIsland;

        /// <summary>是否已经完成配置（原点与占用表可用）。</summary>
        public bool IsConfigured => configured;

        /// <summary>单格边长（世界单位）。</summary>
        public float CellSize => cellSize;

        /// <summary>把占地格数换算成世界尺寸（X = 宽，Y = 深）。</summary>
        public Vector2 CellsToWorldFootprint(Vector2Int cells)
        {
            return new Vector2(
                Mathf.Max(0, cells.x) * cellSize,
                Mathf.Max(0, cells.y) * cellSize);
        }

        /// <summary>cell(0,0) 的最小角，世界坐标。</summary>
        public Vector3 Origin => origin;

        /// <summary>网格宽高格数。</summary>
        public Vector2Int CellCounts => cellCounts;

        /// <summary>网格所在水平面的 Y 坐标。</summary>
        public float GridPlaneY => origin.y;

        /// <summary>网格世界范围的最小角（含）。</summary>
        public Vector3 WorldMin => origin;

        /// <summary>网格世界范围的最大角（不含）。</summary>
        public Vector3 WorldMax => origin + new Vector3(
            cellCounts.x * cellSize, 0f, cellCounts.y * cellSize);

        /// <summary>当前被占用的格子数。</summary>
        public int OccupiedCellCount
        {
            get
            {
                if (occupied == null)
                {
                    return 0;
                }

                int count = 0;

                for (int i = 0; i < occupied.Length; i++)
                {
                    if (occupied[i])
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        private void Awake()
        {
            Rebuild();
        }

        /// <summary>
        /// 由编辑器向导或运行时初始化调用。设置参数后立刻重算原点、可建掩码与占用表。
        /// 运行时改变格数会让旧占用索引失效，因此占用表会被清空并警告。
        /// </summary>
        public void Configure(
            Vector3 center, float cell, Vector2Int counts,
            IslandDefinition island, IslandSettingsAsset settings)
        {
            gridCenter = center;
            cellSize = cell;
            cellCounts = counts;
            islandDefinition = island;
            islandSettings = settings;
            autoFitToIsland = false;

            Rebuild();
        }

        /// <summary>
        /// 按当前 Inspector 参数重算原点、可建掩码与占用表。
        /// 关闭游戏时由向导调用也不会有副作用（数组只在运行时使用）。
        /// </summary>
        [ContextMenu("调试：重新计算网格")]
        public void Rebuild()
        {
            if (cellSize <= 0.01f)
            {
                Debug.LogWarning($"[BuildingGrid] Cell Size 为 {cellSize}，已按 0.01 处理。", this);
                cellSize = 0.01f;
            }

            if (autoFitToIsland && islandDefinition != null)
            {
                gridCenter = islandDefinition.BuildableCenter;
                cellCounts = new Vector2Int(
                    Mathf.Max(1, Mathf.FloorToInt(
                        islandDefinition.BuildableHalfExtents.x * 2f / cellSize + 0.0001f)),
                    Mathf.Max(1, Mathf.FloorToInt(
                        islandDefinition.BuildableHalfExtents.y * 2f / cellSize + 0.0001f)));
            }

            cellCounts.x = Mathf.Max(1, cellCounts.x);
            cellCounts.y = Mathf.Max(1, cellCounts.y);

            origin = new Vector3(
                gridCenter.x - cellCounts.x * cellSize * 0.5f,
                gridCenter.y,
                gridCenter.z - cellCounts.y * cellSize * 0.5f);

            bool hadOccupancy = ownerRects.Count > 0;

            if (hadOccupancy)
            {
                Debug.LogWarning("[BuildingGrid] 网格配置在运行中被改变，旧占用索引已失效，" +
                                 "占用表已清空。请确保网格只在初始化时配置一次。", this);
            }

            ownerRects.Clear();
            occupied = new bool[cellCounts.x * cellCounts.y];
            buildableMask = new bool[cellCounts.x * cellCounts.y];

            BuildBuildableMask();

            configured = true;

            if (logGridFlow)
            {
                int buildableCount = 0;

                for (int i = 0; i < buildableMask.Length; i++)
                {
                    if (buildableMask[i])
                    {
                        buildableCount++;
                    }
                }

                Debug.Log($"[BuildingGrid] 网格就绪：原点 {origin}，格数 {cellCounts.x}×{cellCounts.y}，" +
                          $"单格 {cellSize}，可建 {buildableCount} 格，" +
                          $"判定依据 {(islandSettings != null ? "圆角台地" : islandDefinition != null ? "矩形回退" : "无")}。");
            }
        }

        /// <summary>
        /// 计算每个格子是否完全落在可建台地上。
        /// 判定采样 4 个角点 + 中心，全部通过才算可建；只查中心会让半个格子悬在水上的情况被放过。
        /// </summary>
        private void BuildBuildableMask()
        {
            if (islandSettings == null)
            {
                if (islandDefinition == null)
                {
                    if (!warnedAboutMissingIsland)
                    {
                        warnedAboutMissingIsland = true;
                        Debug.LogError("[BuildingGrid] 既没有 IslandSettings 也没有 IslandDefinition，" +
                                       "所有格子都会被判为不可建。请把海岛参数资产拖到该字段。", this);
                    }

                    return;
                }

                if (!warnedAboutMissingSettings)
                {
                    warnedAboutMissingSettings = true;
                    Debug.LogWarning("[BuildingGrid] 没有 IslandSettings，可建区域退化为矩形判断，" +
                                     "圆角台地的四个角可能允许放到沙滩或水边。", this);
                }
            }

            float half = cellSize * 0.5f;

            for (int y = 0; y < cellCounts.y; y++)
            {
                for (int x = 0; x < cellCounts.x; x++)
                {
                    Vector3 min = CellMinCorner(new Vector2Int(x, y));
                    Vector3 center = min + new Vector3(half, 0f, half);
                    Vector3 max = min + new Vector3(cellSize, 0f, cellSize);

                    bool buildable =
                        IsPointBuildable(min) &&
                        IsPointBuildable(new Vector3(max.x, min.y, min.z)) &&
                        IsPointBuildable(new Vector3(min.x, min.y, max.z)) &&
                        IsPointBuildable(max) &&
                        IsPointBuildable(center);

                    buildableMask[x + y * cellCounts.x] = buildable;
                }
            }
        }

        /// <summary>单个世界点是否在可建台地上。优先用圆角台地距离函数，缺资产时退回矩形。</summary>
        private bool IsPointBuildable(Vector3 worldPoint)
        {
            if (islandSettings != null)
            {
                return IslandMeshGenerator.CoastDistance(islandSettings.settings, worldPoint)
                       <= -buildableInset;
            }

            if (islandDefinition != null)
            {
                Vector3 local = worldPoint - islandDefinition.BuildableCenter;

                return Mathf.Abs(local.x) <= islandDefinition.BuildableHalfExtents.x - buildableInset
                    && Mathf.Abs(local.z) <= islandDefinition.BuildableHalfExtents.y - buildableInset;
            }

            return false;
        }

        // ---------- 坐标换算 ----------

        /// <summary>世界坐标 → 所在格。点在网格外时也仍然返回格子坐标（可能越界）。</summary>
        public bool WorldToCell(Vector3 world, out Vector2Int cell)
        {
            if (!configured)
            {
                cell = default;
                return false;
            }

            cell = new Vector2Int(
                Mathf.FloorToInt((world.x - origin.x) / cellSize),
                Mathf.FloorToInt((world.z - origin.z) / cellSize));

            return true;
        }

        /// <summary>格子的最小角（世界坐标）。</summary>
        public Vector3 CellMinCorner(Vector2Int cell)
        {
            return new Vector3(
                origin.x + cell.x * cellSize,
                origin.y,
                origin.z + cell.y * cellSize);
        }

        /// <summary>格子的中心（世界坐标）。</summary>
        public Vector3 CellCenter(Vector2Int cell)
        {
            return CellMinCorner(cell) + new Vector3(cellSize * 0.5f, 0f, cellSize * 0.5f);
        }

        /// <summary>
        /// 一个格子矩形的中心（世界坐标）。供读档恢复建筑时从 RectInt 反推生成点，
        /// 与 <see cref="TryGetSnappedPose"/> 的 snappedCenter 公式一致。
        /// </summary>
        public Vector3 CellRectCenter(RectInt rect)
        {
            Vector3 min = CellMinCorner(new Vector2Int(rect.xMin, rect.yMin));

            return min + new Vector3(
                rect.width * cellSize * 0.5f,
                0f,
                rect.height * cellSize * 0.5f);
        }

        /// <summary>
        /// 占地世界尺寸 → 需要占用的格数。
        /// 用 ceil 并减去一个极小量，避免 4.0000001 / 2 被算成 3 格。
        /// 尺寸含非正数时返回零，由 CanPlace 判为占地非法。
        /// </summary>
        public Vector2Int FootprintCells(Vector2 footprint)
        {
            if (footprint.x <= 0f || footprint.y <= 0f)
            {
                return Vector2Int.zero;
            }

            return new Vector2Int(
                Mathf.Max(1, Mathf.CeilToInt(footprint.x / cellSize - 0.0001f)),
                Mathf.Max(1, Mathf.CeilToInt(footprint.y / cellSize - 0.0001f)));
        }

        // ---------- 吸附 ----------

        /// <summary>
        /// 把地面点吸附成建筑中心与占地矩形。
        ///
        /// 奇数格宽：中心落在鼠标所在格的中心；
        /// 偶数格宽（例如 2×2）：中心落在离鼠标最近的格线交点，建筑围绕鼠标展开。
        ///
        /// 只检查点是否在网格范围内，不检查占用/可建 —— 否则蓝图无法显示红色反馈。
        /// 返回 false 表示点已完全在网格外，调用方应保留蓝图上一次位置并显示红色。
        /// </summary>
        public bool TryGetSnappedPose(
            Vector3 groundPoint, Vector2 footprint, out Vector3 snappedCenter, out RectInt cellRect)
        {
            snappedCenter = default;
            cellRect = default;

            if (!configured)
            {
                return false;
            }

            if (groundPoint.x < WorldMin.x || groundPoint.x >= WorldMax.x ||
                groundPoint.z < WorldMin.z || groundPoint.z >= WorldMax.z)
            {
                return false;
            }

            float u = (groundPoint.x - origin.x) / cellSize;
            float v = (groundPoint.z - origin.z) / cellSize;

            Vector2Int cells = FootprintCells(footprint);

            // 用 floor(x + 0.5) 而不是 Mathf.Round：.5 时行为确定，不会左右不一致
            float cellLineX = (cells.x % 2 == 1) ? Mathf.Floor(u) + 0.5f : Mathf.Floor(u + 0.5f);
            float cellLineZ = (cells.y % 2 == 1) ? Mathf.Floor(v) + 0.5f : Mathf.Floor(v + 0.5f);

            int rectX = Mathf.CeilToInt(cellLineX - cells.x * 0.5f - 0.0001f);
            int rectY = Mathf.CeilToInt(cellLineZ - cells.y * 0.5f - 0.0001f);

            cellRect = new RectInt(rectX, rectY, cells.x, cells.y);

            snappedCenter = new Vector3(
                origin.x + cellLineX * cellSize,
                origin.y,
                origin.z + cellLineZ * cellSize);

            return true;
        }

        // ---------- 判定与占用 ----------

        /// <summary>格子坐标是否在网格内。</summary>
        public bool IsCellInsideGrid(Vector2Int cell)
        {
            return configured
                && cell.x >= 0 && cell.x < cellCounts.x
                && cell.y >= 0 && cell.y < cellCounts.y;
        }

        /// <summary>格子是否在可建台地上。越界一律 false。</summary>
        public bool IsCellBuildable(Vector2Int cell)
        {
            if (!IsCellInsideGrid(cell) || buildableMask == null)
            {
                return false;
            }

            return buildableMask[cell.x + cell.y * cellCounts.x];
        }

        /// <summary>格子是否已被建筑占用。越界一律 false。</summary>
        public bool IsCellOccupied(Vector2Int cell)
        {
            if (!IsCellInsideGrid(cell) || occupied == null)
            {
                return false;
            }

            return occupied[cell.x + cell.y * cellCounts.x];
        }

        /// <summary>
        /// 整个占地矩形能不能放。这是建筑合法性判定的权威入口：
        /// 未配置 → 尺寸非法 → 越界 → 已占用 → 不可建。
        /// </summary>
        public bool CanPlace(RectInt rect, out GridPlacementFailure failure)
        {
            if (!configured || occupied == null || buildableMask == null)
            {
                failure = GridPlacementFailure.NotConfigured;
                return false;
            }

            if (rect.width <= 0 || rect.height <= 0)
            {
                failure = GridPlacementFailure.InvalidFootprint;
                return false;
            }

            if (rect.xMin < 0 || rect.yMin < 0 ||
                rect.xMax > cellCounts.x || rect.yMax > cellCounts.y)
            {
                failure = GridPlacementFailure.OutOfBounds;
                return false;
            }

            for (int y = rect.yMin; y < rect.yMax; y++)
            {
                for (int x = rect.xMin; x < rect.xMax; x++)
                {
                    int index = x + y * cellCounts.x;

                    if (occupied[index])
                    {
                        failure = GridPlacementFailure.Occupied;
                        return false;
                    }

                    if (!buildableMask[index])
                    {
                        failure = GridPlacementFailure.NotBuildable;
                        return false;
                    }
                }
            }

            failure = GridPlacementFailure.None;
            return true;
        }

        /// <summary>
        /// 登记一个占地矩形。ownerId 用建筑实例的 GetInstanceID()。
        /// 同一个 ownerId 重复登记时会先释放旧记录，避免占用泄漏。
        /// </summary>
        public bool Occupy(RectInt rect, int ownerId)
        {
            if (ownerRects.ContainsKey(ownerId))
            {
                Release(ownerId);
            }

            if (!CanPlace(rect, out GridPlacementFailure failure))
            {
                Debug.LogWarning($"[BuildingGrid] 占用登记失败（ownerId={ownerId}，格子 {rect}）：" +
                                 $"{failure.ToChineseName()}。", this);
                return false;
            }

            for (int y = rect.yMin; y < rect.yMax; y++)
            {
                for (int x = rect.xMin; x < rect.xMax; x++)
                {
                    occupied[x + y * cellCounts.x] = true;
                }
            }

            ownerRects[ownerId] = rect;

            if (logGridFlow)
            {
                Debug.Log($"[BuildingGrid] 登记占用：ownerId={ownerId}，格子 {rect}，" +
                          $"当前占用 {OccupiedCellCount} 格。");
            }

            return true;
        }

        /// <summary>按 ownerId 释放占用。找不到记录返回 false，由调用方决定是否警告。</summary>
        public bool Release(int ownerId)
        {
            if (occupied == null || !ownerRects.TryGetValue(ownerId, out RectInt rect))
            {
                return false;
            }

            for (int y = rect.yMin; y < rect.yMax; y++)
            {
                for (int x = rect.xMin; x < rect.xMax; x++)
                {
                    if (x >= 0 && x < cellCounts.x && y >= 0 && y < cellCounts.y)
                    {
                        occupied[x + y * cellCounts.x] = false;
                    }
                }
            }

            ownerRects.Remove(ownerId);

            if (logGridFlow)
            {
                Debug.Log($"[BuildingGrid] 释放占用：ownerId={ownerId}，格子 {rect}，" +
                          $"当前占用 {OccupiedCellCount} 格。");
            }

            return true;
        }

        /// <summary>清空所有占用记录。只用于重置/测试。</summary>
        public void ClearOccupancy()
        {
            ownerRects.Clear();

            if (occupied != null)
            {
                System.Array.Clear(occupied, 0, occupied.Length);
            }
        }

        [ContextMenu("调试：打印网格状态")]
        private void DebugPrintGridState()
        {
            int buildableCount = 0;

            if (buildableMask != null)
            {
                for (int i = 0; i < buildableMask.Length; i++)
                {
                    if (buildableMask[i])
                    {
                        buildableCount++;
                    }
                }
            }

            Debug.Log($"[BuildingGrid] 状态：configured={configured}，原点 {origin}，" +
                      $"格数 {cellCounts.x}×{cellCounts.y}，单格 {cellSize}，" +
                      $"可建 {buildableCount} 格，已占用 {OccupiedCellCount} 格，" +
                      $"登记建筑 {ownerRects.Count} 座。", this);
        }
    }
}
