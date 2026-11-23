using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Yujian.Building;
using Yujian.Levels;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 在 Scene 视图里用 Gizmos / Handles 显示 <see cref="BuildingGrid"/>，不依赖 Play 模式，
    /// 也不需要打开「可建面积画笔」窗口。
    ///
    /// 显示内容：
    ///   · 整个网格范围（淡白色细线）；
    ///   · 可建格子（半透明绿色填充 + 亮绿色边界）；
    ///   · 已占用格子（红色填充，仅选中网格或 Play 时显示）；
    ///   · 关卡封锁格（灰色填充，仅选中网格或 Play 时显示）；
    ///   · 网格信息（格数 / 单格大小 / 可建格数 / 占用格数）。
    ///
    /// 开关：菜单「屿见 → 建筑 → 显示建筑网格 Gizmo」（EditorPrefs 记忆，默认开启）。
    /// 关闭 Scene 视图右上角的 Gizmos 按钮也会隐藏。
    ///
    /// 纯编辑器工具：不改运行时逻辑、不写场景数据。
    /// </summary>
    internal static class BuildingGridGizmo
    {
        private const string PrefsKey = "Yujian.BuildingGridGizmo.Enabled";
        private const string MenuPath = "屿见/建筑/显示建筑网格 Gizmo";

        private const float YOffset = 0.05f;
        private const float GridLineWidth = 1f;
        private const float BoundaryLineWidth = 3f;

        private static readonly Color GridLineColor = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color FrameColor = new Color(1f, 1f, 1f, 0.45f);
        private static readonly Color BuildableFill = new Color(0.15f, 0.90f, 0.30f, 0.10f);
        private static readonly Color BuildableEdge = new Color(0.20f, 1.00f, 0.35f, 0.90f);
        private static readonly Color OccupiedFill = new Color(0.95f, 0.25f, 0.20f, 0.30f);
        private static readonly Color OccupiedEdge = new Color(1.00f, 0.35f, 0.25f, 0.95f);
        private static readonly Color BlockedFill = new Color(0.55f, 0.55f, 0.62f, 0.25f);

        private static readonly Color LevelFill = new Color(1f, 0.45f, 0.02f, 0.50f);
        private static readonly Color LevelEdge = new Color(1f, 0.30f, 0.00f, 0.95f);
        private static readonly Color LevelFallbackFill = new Color(1f, 0.45f, 0.02f, 0.22f);
        private static readonly Color LevelFallbackEdge = new Color(1f, 0.30f, 0.00f, 0.65f);
        private static readonly Color LevelCoreFill = new Color(1f, 0.08f, 0.02f, 0.70f);
        private static readonly Color LevelCoreEdge = new Color(1f, 0.05f, 0.00f, 1f);

        private static GUIStyle infoStyle;
        private static GUIStyle levelLabelStyle;

        private static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefsKey, true);
            set => EditorPrefs.SetBool(PrefsKey, value);
        }

        [MenuItem(MenuPath, false, 210)]
        private static void ToggleEnabled()
        {
            Enabled = !Enabled;
            SceneView.RepaintAll();
            Debug.Log($"[BuildingGridGizmo] Scene 视图网格 Gizmo 已{(Enabled ? "开启" : "关闭")}。");
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleEnabledValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        [DrawGizmo(
            GizmoType.NonSelected | GizmoType.Selected | GizmoType.InSelectionHierarchy,
            typeof(BuildingGrid))]
        private static void DrawGridGizmo(BuildingGrid grid, GizmoType gizmoType)
        {
            if (!Enabled || grid == null || !grid.isActiveAndEnabled || !grid.IsConfigured)
            {
                return;
            }

            bool selected =
                (gizmoType & (GizmoType.Selected | GizmoType.InSelectionHierarchy)) != 0;

            // Play 时只在选中网格时显示，避免和 Game 视图里的格线叠加得太乱。
            if (Application.isPlaying && !selected)
            {
                return;
            }

            Vector2Int counts = grid.CellCounts;
            float cellSize = grid.CellSize;

            if (counts.x <= 0 || counts.y <= 0 || cellSize <= 0f)
            {
                return;
            }

            Vector3 origin = grid.WorldMin;
            float y = grid.GridPlaneY + YOffset;

            // 画笔窗口正在显示它自己的绿 / 红叠加层时，不再重复画可建区。
            bool painterActive = BuildingAreaPainterWindow.IsSceneOverlayVisible;

            CompareFunction previousZTest = Handles.zTest;

            try
            {
                Handles.zTest = CompareFunction.Always;

                // 1. 整个网格范围的淡色格线
                for (int x = 0; x <= counts.x; x++)
                {
                    float wx = origin.x + x * cellSize;
                    Handles.color = GridLineColor;
                    Handles.DrawAAPolyLine(
                        GridLineWidth,
                        new Vector3(wx, y, origin.z),
                        new Vector3(wx, y, origin.z + counts.y * cellSize));
                }

                for (int z = 0; z <= counts.y; z++)
                {
                    float wz = origin.z + z * cellSize;
                    Handles.color = GridLineColor;
                    Handles.DrawAAPolyLine(
                        GridLineWidth,
                        new Vector3(origin.x, y, wz),
                        new Vector3(origin.x + counts.x * cellSize, y, wz));
                }

                // 2. 可建面积填充
                if (!painterActive)
                {
                    for (int z = 0; z < counts.y; z++)
                    {
                        for (int x = 0; x < counts.x; x++)
                        {
                            Vector2Int cell = new Vector2Int(x, z);

                            if (grid.IsCellBuildable(cell) && !grid.IsCellOccupied(cell))
                            {
                                DrawCellQuad(grid, cell, BuildableFill);
                            }
                        }
                    }

                    DrawBuildableBoundary(grid);
                }

                // 3. 关卡污染面积（橙）。画笔正在显示时跳过，避免重复叠加。
                if (!painterActive)
                {
                    DrawLevelAreas(grid);
                }

                // 4. 占用 / 封锁（选中或 Play 时才有信息价值）
                if (selected)
                {
                    for (int z = 0; z < counts.y; z++)
                    {
                        for (int x = 0; x < counts.x; x++)
                        {
                            Vector2Int cell = new Vector2Int(x, z);
                            bool occupied = grid.IsCellOccupied(cell);
                            bool blocked = grid.IsCellLevelBlocked(cell);

                            if (occupied)
                            {
                                DrawCellQuad(grid, cell, OccupiedFill);
                                DrawCellOutline(grid, cell, OccupiedEdge);
                            }
                            else if (blocked)
                            {
                                DrawCellQuad(grid, cell, BlockedFill);
                            }
                        }
                    }
                }

                // 4. 外框
                Handles.color = FrameColor;
                Vector3 a = new Vector3(origin.x, y, origin.z);
                Vector3 b = new Vector3(origin.x + counts.x * cellSize, y, origin.z);
                Vector3 c = new Vector3(origin.x + counts.x * cellSize, y, origin.z + counts.y * cellSize);
                Vector3 d = new Vector3(origin.x, y, origin.z + counts.y * cellSize);
                Handles.DrawAAPolyLine(GridLineWidth * 2f, a, b, c, d, a);

                // 5. 信息标签
                DrawInfoLabel(grid, selected);
            }
            finally
            {
                Handles.zTest = previousZTest;
            }
        }

        /// <summary>
        /// 把所有关卡的占用/污染面积画成橙色：
        ///   · 有手绘掩码且签名匹配 → 逐格画污染格；
        ///   · 没有掩码 / 签名不匹配 → 按 footprintCells 矩形画（淡橙）；
        ///   · 污染核心格（1 格）永远用更深的红色盖在最上面。
        /// </summary>
        private static void DrawLevelAreas(BuildingGrid grid)
        {
            LevelEntrance[] entrances = Object.FindObjectsByType<LevelEntrance>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (entrances.Length == 0)
            {
                return;
            }

            Vector2Int counts = grid.CellCounts;

            for (int i = 0; i < entrances.Length; i++)
            {
                LevelEntrance entrance = entrances[i];

                if (entrance == null)
                {
                    continue;
                }

                bool hasCore = grid.WorldToCell(
                    entrance.PollutionCore.position, out Vector2Int coreCell);

                LevelAreaMask mask = entrance.AreaMask;
                bool maskUsable = mask != null && mask.IsValid
                    && mask.IsCompatible(grid.GridCenter, grid.CellSize, counts);

                bool drewAny = false;
                Vector3 labelPosition = Vector3.zero;

                if (maskUsable)
                {
                    for (int y = 0; y < counts.y; y++)
                    {
                        for (int x = 0; x < counts.x; x++)
                        {
                            if (!mask.GetCell(x, y))
                            {
                                continue;
                            }

                            Vector2Int cell = new Vector2Int(x, y);
                            DrawLevelCell(grid, cell, LevelFill, LevelEdge);

                            if (!drewAny)
                            {
                                labelPosition = grid.CellCenter(cell);
                            }

                            drewAny = true;
                        }
                    }
                }
                else if (hasCore)
                {
                    Vector2Int size = entrance.FootprintCells;
                    int xMin = coreCell.x - size.x / 2;
                    int yMin = coreCell.y - size.y / 2;

                    for (int y = 0; y < size.y; y++)
                    {
                        for (int x = 0; x < size.x; x++)
                        {
                            Vector2Int cell = new Vector2Int(xMin + x, yMin + y);

                            if (!grid.IsCellInsideGrid(cell))
                            {
                                continue;
                            }

                            DrawLevelCell(
                                grid, cell, LevelFallbackFill, LevelFallbackEdge);

                            if (!drewAny)
                            {
                                labelPosition = grid.CellCenter(cell);
                            }

                            drewAny = true;
                        }
                    }
                }

                // 核心格：1 格，始终高亮。
                if (hasCore && grid.IsCellInsideGrid(coreCell))
                {
                    DrawLevelCell(grid, coreCell, LevelCoreFill, LevelCoreEdge);

                    if (!drewAny)
                    {
                        labelPosition = grid.CellCenter(coreCell);
                    }

                    drewAny = true;
                }

                if (!drewAny)
                {
                    Handles.Label(
                        entrance.PollutionCore.position + new Vector3(0f, 1.5f, 0f),
                        $"关卡 {entrance.LevelNumber}：占地不在网格内",
                        LevelLabelStyle);
                    continue;
                }

                string label = maskUsable
                    ? $"关卡 {entrance.LevelNumber}"
                    : mask != null
                        ? $"关卡 {entrance.LevelNumber}（掩码不匹配，按矩形）"
                        : $"关卡 {entrance.LevelNumber}（矩形）";

                Handles.Label(
                    labelPosition + new Vector3(0f, 1.5f, 0f), label, LevelLabelStyle);
            }
        }

        private static void DrawLevelCell(
            BuildingGrid grid, Vector2Int cell, Color fill, Color outline)
        {
            Vector3[] vertices = BuildCellVertices(grid, cell);

            // 比可建面积填充再高一点，避免与绿色底重叠时闪烁。
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i].y += 0.01f;
            }

            Handles.DrawSolidRectangleWithOutline(vertices, fill, outline);
        }

        private static GUIStyle LevelLabelStyle
        {
            get
            {
                if (levelLabelStyle == null)
                {
                    levelLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        normal = { textColor = new Color(1f, 0.65f, 0.20f, 0.95f) },
                        fontSize = 11,
                        alignment = TextAnchor.MiddleCenter,
                    };
                }

                return levelLabelStyle;
            }
        }

        private static void DrawCellQuad(BuildingGrid grid, Vector2Int cell, Color fill)
        {
            Handles.DrawSolidRectangleWithOutline(
                BuildCellVertices(grid, cell), fill, Color.clear);
        }

        private static void DrawCellOutline(BuildingGrid grid, Vector2Int cell, Color color)
        {
            Vector3[] v = BuildCellVertices(grid, cell);
            Handles.color = color;
            Handles.DrawAAPolyLine(BoundaryLineWidth, v[0], v[1], v[2], v[3], v[0]);
        }

        private static Vector3[] BuildCellVertices(BuildingGrid grid, Vector2Int cell)
        {
            Vector3 min = grid.CellMinCorner(cell);
            float size = grid.CellSize;
            float y = grid.GridPlaneY + YOffset;

            // 俯视顺时针：左下 → 左上 → 右上 → 右下
            return new[]
            {
                new Vector3(min.x, y, min.z),
                new Vector3(min.x, y, min.z + size),
                new Vector3(min.x + size, y, min.z + size),
                new Vector3(min.x + size, y, min.z),
            };
        }

        /// <summary>只描可建区域的外轮廓：和不可建格子相邻的边才画。</summary>
        private static void DrawBuildableBoundary(BuildingGrid grid)
        {
            Vector2Int counts = grid.CellCounts;
            float size = grid.CellSize;
            float y = grid.GridPlaneY + YOffset;

            Handles.color = BuildableEdge;

            for (int z = 0; z < counts.y; z++)
            {
                for (int x = 0; x < counts.x; x++)
                {
                    Vector2Int cell = new Vector2Int(x, z);

                    if (!grid.IsCellBuildable(cell) || grid.IsCellOccupied(cell))
                    {
                        continue;
                    }

                    Vector3 min = grid.CellMinCorner(cell);
                    Vector3 p00 = new Vector3(min.x, y, min.z);
                    Vector3 p10 = new Vector3(min.x + size, y, min.z);
                    Vector3 p01 = new Vector3(min.x, y, min.z + size);
                    Vector3 p11 = new Vector3(min.x + size, y, min.z + size);

                    if (!IsOpenNeighbour(grid, new Vector2Int(x, z - 1)))
                    {
                        Handles.DrawAAPolyLine(BoundaryLineWidth, p00, p10);
                    }

                    if (!IsOpenNeighbour(grid, new Vector2Int(x, z + 1)))
                    {
                        Handles.DrawAAPolyLine(BoundaryLineWidth, p01, p11);
                    }

                    if (!IsOpenNeighbour(grid, new Vector2Int(x - 1, z)))
                    {
                        Handles.DrawAAPolyLine(BoundaryLineWidth, p00, p01);
                    }

                    if (!IsOpenNeighbour(grid, new Vector2Int(x + 1, z)))
                    {
                        Handles.DrawAAPolyLine(BoundaryLineWidth, p10, p11);
                    }
                }
            }
        }

        private static bool IsOpenNeighbour(BuildingGrid grid, Vector2Int cell)
        {
            return grid.IsCellBuildable(cell) && !grid.IsCellOccupied(cell);
        }

        private static void DrawInfoLabel(BuildingGrid grid, bool selected)
        {
            Vector2Int counts = grid.CellCounts;
            int buildable = 0;
            int occupied = 0;

            for (int z = 0; z < counts.y; z++)
            {
                for (int x = 0; x < counts.x; x++)
                {
                    Vector2Int cell = new Vector2Int(x, z);

                    if (grid.IsCellBuildable(cell))
                    {
                        buildable++;
                    }

                    if (grid.IsCellOccupied(cell))
                    {
                        occupied++;
                    }
                }
            }

            string source = grid.HasCompatibleAreaMask ? "手绘掩码" : "海岛台地";
            string text =
                $"建筑网格 {counts.x}×{counts.y} @ {grid.CellSize:0.##}\n" +
                $"可建 {buildable} 格 · 占用 {occupied} 格 · 判定 {source}";

            if (!selected)
            {
                text = $"建筑网格 {counts.x}×{counts.y} @ {grid.CellSize:0.##}";
            }

            if (infoStyle == null)
            {
                infoStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    normal = { textColor = new Color(0.92f, 0.95f, 1f, 0.95f) },
                    fontSize = 11,
                    alignment = TextAnchor.UpperLeft,
                };
            }

            Vector3 labelPos = new Vector3(
                grid.WorldMin.x,
                grid.GridPlaneY + 0.5f,
                grid.WorldMin.z + counts.y * grid.CellSize + 1.5f);

            Handles.Label(labelPos, text, infoStyle);
        }
    }
}
