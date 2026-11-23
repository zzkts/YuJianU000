using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Yujian.Building;
using Yujian.Levels;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 可建面积画笔（2026-11-20）。
    ///
    /// 在 Scene 视图里直接绘制 / 清除建筑网格的可建格子，数据写进
    /// <see cref="BuildableAreaMask"/> 资产，由 <see cref="BuildingGrid"/> 引用。
    ///
    /// 操作：
    ///   · 左键 = 画「可建」，右键 = 清除「不可建」；
    ///   · 笔刷 / 矩形 / 油漆桶三种工具；
    ///   · Ctrl+Z 撤销整笔，Ctrl+S 保存场景（掩码资产会随画笔自动保存）。
    ///
    /// 入口：菜单栏 → 屿见 → 建筑 → 可建面积画笔…
    /// </summary>
    public class BuildingAreaPainterWindow : EditorWindow
    {
        private const string MaskFolder = "Assets/Data/Buildings";
        private const string DefaultMaskPath = "Assets/Data/Buildings/BuildableAreaMask.asset";

        private enum Tool
        {
            Brush = 0,
            Rectangle = 1,
            Bucket = 2,
        }

        private enum PaintLayer
        {
            BuildableArea = 0,
            LevelArea = 1,
        }

        private BuildingGrid grid;
        private BuildableAreaMask mask;
        private BuildableAreaMask lastGridMask;

        private PaintLayer paintLayer = PaintLayer.BuildableArea;
        private LevelEntrance[] levelEntrances = new LevelEntrance[0];
        private LevelEntrance selectedEntrance;
        private int selectedLevelIndex = -1;

        private Tool tool = Tool.Brush;
        private int brushRadius = 0;
        private bool paintingEnabled = true;

        private Vector2Int? hoverCell;
        private bool painting;
        private bool erase;
        private int undoGroup;

        private bool rectActive;
        private Vector2Int rectStart;
        private Vector2Int rectEnd;

        // 2D 面积预览（窗口内直接画）
        private Vector2Int? previewHover;
        private bool showPreview = true;
        private int previewCellPixels = 6;
        private Texture2D previewTexture;
        private bool previewDirty = true;
        private Vector2 mainScroll;

        private Mesh overlayMesh;
        private Mesh boundaryMesh;
        private Material overlayMaterial;
        private bool meshDirty = true;
        private Vector2Int cachedCounts;
        private float cachedSize;
        private Vector3 cachedOrigin;
        private bool showData;

        /// <summary>
        /// Scene 视图当前是否正在显示画笔自己的可建面积叠加层。
        /// 供其他编辑器工具（如 BuildingGridGizmo）避免重复绘制。
        /// </summary>
        public static bool IsSceneOverlayVisible { get; private set; }

        [MenuItem("屿见/建筑/可建面积画笔…", false, 545)]
        public static void Open()
        {
            BuildingAreaPainterWindow window = GetWindow<BuildingAreaPainterWindow>("可建面积画笔");
            window.minSize = new Vector2(420f, 720f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("可建面积画笔");
            wantsMouseMove = true;
            SceneView.duringSceneGui += OnSceneGUI;
            FindGrid();
            RefreshLevelEntrances();
            SyncActiveMask();
            meshDirty = true;
            previewDirty = true;
            SceneView.RepaintAll();
        }

        private void OnDisable()
        {
            IsSceneOverlayVisible = false;
            SceneView.duringSceneGui -= OnSceneGUI;

            if (painting)
            {
                Undo.CollapseUndoOperations(undoGroup);
                painting = false;
                rectActive = false;

                if (mask != null)
                {
                    EditorUtility.SetDirty(mask);
                }
            }

            DestroyOverlay();
            DestroyPreviewTexture();
            AssetDatabase.SaveAssets();
        }

        private void OnFocus()
        {
            FindGrid();
            RefreshLevelEntrances();
            SyncActiveMask();
            meshDirty = true;
            previewDirty = true;
            SceneView.RepaintAll();
            Repaint();
        }

        private void OnHierarchyChange()
        {
            FindGrid();
            RefreshLevelEntrances();
            SyncActiveMask();
            meshDirty = true;
            previewDirty = true;
            SceneView.RepaintAll();
            Repaint();
        }

        // ---------------- 绘画层 ----------------

        private bool IsLevelMode => paintLayer == PaintLayer.LevelArea;

        private bool ActiveMaskReady
        {
            get
            {
                if (grid == null || mask == null || !mask.IsValid)
                {
                    return false;
                }

                if (!mask.IsCompatible(grid.GridCenter, grid.CellSize, grid.CellCounts))
                {
                    return false;
                }

                return IsLevelMode
                    ? selectedEntrance != null
                    : grid.IsAreaMaskActive && grid.HasCompatibleAreaMask;
            }
        }

        /// <summary>把当前绘画层的掩码同步到 mask 字段。</summary>
        private void SyncActiveMask()
        {
            if (grid == null)
            {
                mask = null;
                lastGridMask = null;
                return;
            }

            BuildableAreaMask active = IsLevelMode
                ? (selectedEntrance != null ? selectedEntrance.AreaMask : null)
                : grid.AreaMask;

            lastGridMask = active;
            mask = active;
            meshDirty = true;
            previewDirty = true;
        }

        private void RefreshLevelEntrances()
        {
            LevelEntrance[] found = Object.FindObjectsByType<LevelEntrance>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            System.Array.Sort(found, (a, b) => a.LevelNumber.CompareTo(b.LevelNumber));

            int previousId = selectedEntrance != null ? selectedEntrance.GetInstanceID() : 0;
            levelEntrances = found;
            selectedLevelIndex = found.Length > 0 ? 0 : -1;

            for (int i = 0; i < found.Length; i++)
            {
                if (found[i].GetInstanceID() == previousId)
                {
                    selectedLevelIndex = i;
                    break;
                }
            }

            selectedEntrance = selectedLevelIndex >= 0 ? found[selectedLevelIndex] : null;
        }

        private void SetLayer(PaintLayer layer)
        {
            if (paintLayer == layer)
            {
                return;
            }

            paintLayer = layer;
            hoverCell = null;
            previewHover = null;
            painting = false;
            rectActive = false;

            if (IsLevelMode)
            {
                RefreshLevelEntrances();
            }

            SyncActiveMask();
            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>这一格是否被任意关卡污染掩码标为污染（可排除当前关卡）。</summary>
        private bool IsCellPollutedByAnyLevel(Vector2Int cell, LevelEntrance exclude = null)
        {
            if (grid == null || !grid.IsCellInsideGrid(cell))
            {
                return false;
            }

            for (int i = 0; i < levelEntrances.Length; i++)
            {
                LevelEntrance entrance = levelEntrances[i];

                if (entrance == null || entrance == exclude)
                {
                    continue;
                }

                BuildableAreaMask levelMask = entrance.AreaMask;

                if (levelMask == null || !levelMask.IsValid)
                {
                    continue;
                }

                if (!levelMask.IsCompatible(grid.GridCenter, grid.CellSize, grid.CellCounts))
                {
                    continue;
                }

                if (levelMask.GetCell(cell))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsCellPollutedByActiveMask(Vector2Int cell)
        {
            return IsLevelMode && mask != null && mask.GetCell(cell);
        }

        // ---------------- 窗口 UI ----------------

        private void OnGUI()
        {
            // 鼠标在窗口外松开时也要结束笔画，否则 Undo 组会一直开着
            if (Event.current != null && Event.current.type == EventType.MouseUp && painting)
            {
                if (tool == Tool.Rectangle && rectActive)
                {
                    ApplyRect(rectStart, rectEnd, !erase);
                }

                EndStroke();
                Event.current.Use();
            }

            RefreshFromGrid();

            if (grid == null)
            {
                EditorGUILayout.HelpBox(
                    "当前场景没有 BuildingGrid。请先运行「屿见 → 建筑 → 配置建筑网格与建造相机（阶段 7）」。",
                    MessageType.Error);

                if (GUILayout.Button("找场景里的 BuildingGrid"))
                {
                    FindGrid();
                }

                return;
            }

            mainScroll = EditorGUILayout.BeginScrollView(mainScroll);
            DrawLayerSelector();
            DrawSceneEntry();
            DrawStatus();
            DrawTools();
            DrawPreview();
            DrawStats();

            EditorGUILayout.Space(6f);
            showData = EditorGUILayout.Foldout(showData, "数据源 / 高级操作", true);

            if (showData)
            {
                DrawBinding();
            }

            DrawFooter();
            EditorGUILayout.EndScrollView();
        }

        private void DrawLayerSelector()
        {
            EditorGUILayout.LabelField("绘画层", EditorStyles.boldLabel);

            int newLayer = GUILayout.Toolbar(
                (int)paintLayer, new[] { "可建面积", "关卡污染区" });

            if (newLayer != (int)paintLayer)
            {
                SetLayer((PaintLayer)newLayer);
            }

            if (!IsLevelMode)
            {
                return;
            }

            if (levelEntrances.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "场景里没有 LevelEntrance。关卡污染区需要挂在关卡入口上。",
                    MessageType.Warning);
                return;
            }

            string[] labels = new string[levelEntrances.Length];

            for (int i = 0; i < levelEntrances.Length; i++)
            {
                LevelEntrance entrance = levelEntrances[i];
                string maskState = entrance.AreaMask != null ? "· 已绑定掩码" : "· 矩形占地";
                labels[i] = $"关卡 {entrance.LevelNumber}（{entrance.name}）{maskState}";
            }

            int newIndex = EditorGUILayout.Popup("关卡", Mathf.Max(0, selectedLevelIndex), labels);

            if (newIndex != selectedLevelIndex)
            {
                selectedLevelIndex = newIndex;
                selectedEntrance = levelEntrances[newIndex];
                hoverCell = null;
                previewHover = null;
                SyncActiveMask();
                SceneView.RepaintAll();
                Repaint();
            }

            if (GUILayout.Button("刷新关卡列表"))
            {
                RefreshLevelEntrances();
                SyncActiveMask();
                SceneView.RepaintAll();
                Repaint();
            }
        }

        private void DrawSceneEntry()
        {
            EditorGUILayout.LabelField("Scene 绘画", EditorStyles.boldLabel);

            if (IsLevelMode)
            {
                if (selectedEntrance == null)
                {
                    EditorGUILayout.HelpBox("先在下面选择一个关卡。", MessageType.Info);
                    return;
                }

                bool ready = ActiveMaskReady;

                if (ready)
                {
                    EditorGUILayout.HelpBox(
                        $"关卡 {selectedEntrance.LevelNumber}：回到 Scene 视图左键画「污染」，右键清除。\n" +
                        "橙色 = 污染（未净化时封锁），浅绿 = 当前可建面积；红橙 = 两者重叠（运行时关卡优先）。",
                        MessageType.Info);

                    if (GUILayout.Button("重新创建 / 绑定关卡掩码", GUILayout.Height(24f)))
                    {
                        CreateOrBindLevelMask();
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "这个关卡还没有可用的污染面积掩码。点下面按钮会按当前矩形占地烘焙一份，" +
                        "之后就能在 Scene 视图直接画。",
                        MessageType.Warning);

                    if (GUILayout.Button("创建 / 绑定关卡掩码", GUILayout.Height(30f)))
                    {
                        CreateOrBindLevelMask();
                    }
                }

                return;
            }

            bool buildableReady = grid.IsAreaMaskActive && grid.HasCompatibleAreaMask;

            if (buildableReady)
            {
                EditorGUILayout.HelpBox(
                    "已启用：回到 Scene 视图，左键画「可建」，右键清除。\n" +
                    "绿 = 可建，红 = 不可建；黄色描边是可建区域边界；橙色 = 关卡污染区。",
                    MessageType.Info);

                if (GUILayout.Button("重新创建 / 绑定掩码", GUILayout.Height(24f)))
                {
                    CreateOrBindMask();
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "还没启用。点下面的按钮创建 / 绑定掩码并烘焙当前台地，" +
                    "之后就能在 Scene 视图直接画。",
                    MessageType.Warning);

                if (GUILayout.Button("启用 Scene 绘画（创建 / 绑定掩码）", GUILayout.Height(30f)))
                {
                    CreateOrBindMask();
                }
            }
        }

        private void DrawBinding()
        {
            EditorGUILayout.LabelField("数据源", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            BuildingGrid newGrid = (BuildingGrid)EditorGUILayout.ObjectField(
                "BuildingGrid", grid, typeof(BuildingGrid), true);

            if (EditorGUI.EndChangeCheck())
            {
                grid = newGrid;

                if (IsLevelMode)
                {
                    RefreshLevelEntrances();
                    SyncActiveMask();
                }
                else
                {
                    mask = grid != null ? grid.AreaMask : null;
                    lastGridMask = mask;
                }

                meshDirty = true;
                previewDirty = true;
                SceneView.RepaintAll();
                Repaint();
            }

            if (IsLevelMode)
            {
                DrawLevelMaskBinding();
                return;
            }

            BuildableAreaMask newMask = (BuildableAreaMask)EditorGUILayout.ObjectField(
                "可建面积掩码", mask, typeof(BuildableAreaMask), false);

            if (newMask != mask)
            {
                mask = newMask;
                meshDirty = true;
                previewDirty = true;
                SceneView.RepaintAll();
                Repaint();
            }

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("创建 / 绑定掩码"))
            {
                CreateOrBindMask();
            }

            if (GUILayout.Button("从台地烘焙"))
            {
                BakeFromIsland();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("全部可建"))
            {
                FillMask(true);
            }

            if (GUILayout.Button("全部不可建"))
            {
                FillMask(false);
            }

            if (GUILayout.Button("按当前网格重建"))
            {
                RebuildMask();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawLevelMaskBinding()
        {
            if (selectedEntrance == null)
            {
                EditorGUILayout.HelpBox("先在窗口上方选择一个关卡。", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("关卡污染面积", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "当前关卡",
                $"关卡 {selectedEntrance.LevelNumber}（{selectedEntrance.name}）");

            LevelAreaMask current = selectedEntrance.AreaMask;
            LevelAreaMask newMask = (LevelAreaMask)EditorGUILayout.ObjectField(
                "污染面积掩码", current, typeof(LevelAreaMask), false);

            if (newMask != current)
            {
                AssignLevelMask(newMask);
            }

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("创建 / 绑定关卡掩码"))
            {
                CreateOrBindLevelMask();
            }

            if (GUILayout.Button("从矩形占地烘焙"))
            {
                BakeLevelRectConfirm();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("全部污染"))
            {
                FillMask(true);
            }

            if (GUILayout.Button("全部清除"))
            {
                FillMask(false);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "污染格 = 未净化时不可建；清除关卡后只保留污染核心那一格。\n" +
                "没有绑定掩码时，运行时仍按 footprintCells 矩形封锁。",
                MessageType.None);
        }

        private void DrawStatus()
        {
            if (mask == null)
            {
                EditorGUILayout.HelpBox(
                    IsLevelMode
                        ? "这个关卡还没有绑定污染面积掩码。点「创建 / 绑定关卡掩码」会按当前矩形占地烘焙一份。"
                        : "还没有绑定可建面积掩码。点「创建 / 绑定掩码」会新建一个资产并按当前台地烘焙。",
                    MessageType.Info);
                return;
            }

            if (!mask.IsValid)
            {
                EditorGUILayout.HelpBox(
                    $"掩码数组长度异常（{mask.CellCounts.x}×{mask.CellCounts.y}，需要 {mask.CellCount} 格）。" +
                    "点「按当前网格重建」。", MessageType.Error);
                return;
            }

            if (!mask.IsCompatible(grid.GridCenter, grid.CellSize, grid.CellCounts))
            {
                EditorGUILayout.HelpBox(
                    "掩码的网格签名与当前 BuildingGrid 不一致：\n" +
                    $"掩码 {mask.CellCounts.x}×{mask.CellCounts.y} / 单格 {mask.CellSize:0.##} / " +
                    $"中心 {mask.GridCenter}\n" +
                    $"网格 {grid.CellCounts.x}×{grid.CellCounts.y} / 单格 {grid.CellSize:0.##} / " +
                    $"中心 {grid.GridCenter}\n" +
                    "点「按当前网格重建」会清空旧手绘内容。", MessageType.Error);
            }

            if (!IsLevelMode)
            {
                if (grid.AreaMask != mask)
                {
                    EditorGUILayout.HelpBox(
                        "窗口里的掩码还没有绑定到 BuildingGrid。点「创建 / 绑定掩码」或下面的按钮。",
                        MessageType.Warning);

                    if (GUILayout.Button("绑定这个掩码"))
                    {
                        AssignMask(mask);
                    }
                }

                bool useMask = EditorGUILayout.ToggleLeft(
                    "BuildingGrid 使用手绘掩码（关掉则回到海岛台地判定）", grid.UseAreaMask);

                if (useMask != grid.UseAreaMask)
                {
                    Undo.RecordObject(grid, "切换可建面积数据源");
                    grid.SetAreaMask(mask, useMask);
                    EditorUtility.SetDirty(grid);
                    EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
                    lastGridMask = mask;
                    meshDirty = true;
                    previewDirty = true;
                    SceneView.RepaintAll();
                    Repaint();
                }
            }

            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Play Mode 下不能绘画。请退出 Play Mode。", MessageType.Warning);
            }
        }

        private void DrawTools()
        {
            EditorGUILayout.LabelField("画笔", EditorStyles.boldLabel);

            paintingEnabled = EditorGUILayout.ToggleLeft(
                "绘画模式（关掉后可以正常选择物体）", paintingEnabled);

            tool = (Tool)GUILayout.Toolbar((int)tool, new[] { "笔刷", "矩形", "油漆桶" });

            if (tool == Tool.Brush)
            {
                brushRadius = EditorGUILayout.IntSlider("笔刷半径（格）", brushRadius, 0, 4);
            }

            EditorGUILayout.HelpBox(
                IsLevelMode
                    ? "左键 = 画「污染」；右键 = 清除污染。\n" +
                      "矩形：拖出范围后松开生效；油漆桶：点一下填满相连的同类格子。\n" +
                      "Ctrl+Z 撤销整笔。"
                    : "左键 = 画「可建」；右键 = 清除为「不可建」。\n" +
                      "矩形：拖出范围后松开生效；油漆桶：点一下填满相连的同类格子。\n" +
                      "Ctrl+Z 撤销整笔。", MessageType.None);
        }

        private void DrawPreview()
        {
            showPreview = EditorGUILayout.Foldout(
                showPreview,
                IsLevelMode
                    ? "面积预览（橙 = 污染，浅绿 = 可建；可直接在里面画）"
                    : "面积预览（绿 = 可建，红 = 不可建；可直接在里面画）",
                true);

            if (!showPreview)
            {
                return;
            }

            if (mask == null || !mask.IsValid)
            {
                EditorGUILayout.HelpBox("没有可用的掩码。先「创建 / 绑定掩码」。", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            previewCellPixels = EditorGUILayout.IntSlider("缩放（像素/格）", previewCellPixels, 4, 16);

            if (GUILayout.Button("适应窗口", GUILayout.Width(80f)))
            {
                float available = Mathf.Max(120f, position.width - 90f);
                previewCellPixels = Mathf.Clamp(
                    Mathf.FloorToInt(available / Mathf.Max(1, mask.CellCounts.x)), 4, 16);
                previewDirty = true;
            }

            EditorGUILayout.EndHorizontal();

            Vector2Int counts = mask.CellCounts;
            float width = counts.x * previewCellPixels;
            float height = counts.y * previewCellPixels;

            Rect rect = GUILayoutUtility.GetRect(width, height);

            if (Event.current.type == EventType.Repaint)
            {
                DrawPreviewCanvas(rect, counts);
            }

            HandlePreviewEvents(rect, counts);

            EditorGUILayout.LabelField(
                IsLevelMode
                    ? "→ +X　↑ +Z（下边是 y = 0）　左键画污染 / 右键清除"
                    : "→ +X　↑ +Z（下边是 y = 0）　左键画可建 / 右键清除",
                EditorStyles.miniLabel);

            if (previewHover.HasValue)
            {
                Vector2Int cell = previewHover.Value;
                bool painted = mask.GetCell(cell);
                bool buildable = grid.IsCellBuildable(cell);

                string state;

                if (IsLevelMode)
                {
                    state = painted ? "污染" : "未污染";

                    if (buildable)
                    {
                        state += "（同时是可建区）";
                    }
                }
                else
                {
                    state = painted ? "可建" : "不可建";

                    if (IsCellPollutedByAnyLevel(cell))
                    {
                        state += "（被关卡污染区覆盖）";
                    }
                }

                EditorGUILayout.LabelField(
                    $"格 ({cell.x}, {cell.y}) · {state}", EditorStyles.miniLabel);
            }
        }

        private void DrawPreviewCanvas(Rect rect, Vector2Int counts)
        {
            EnsurePreviewTexture(counts);

            if (previewTexture != null)
            {
                GUI.DrawTexture(rect, previewTexture, ScaleMode.StretchToFill, false);
            }

            float px = previewCellPixels;
            Color lineColor = new Color(0f, 0f, 0f, 0.30f);

            for (int x = 0; x <= counts.x; x++)
            {
                EditorGUI.DrawRect(new Rect(rect.x + x * px, rect.y, 1f, rect.height), lineColor);
            }

            for (int y = 0; y <= counts.y; y++)
            {
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + y * px, rect.width, 1f), lineColor);
            }

            DrawPreviewHover(rect, counts);
        }

        private void DrawPreviewHover(Rect rect, Vector2Int counts)
        {
            if (!previewHover.HasValue && !(tool == Tool.Rectangle && rectActive))
            {
                return;
            }

            int xMin;
            int xMax;
            int yMin;
            int yMax;

            if (tool == Tool.Rectangle && rectActive)
            {
                xMin = Mathf.Min(rectStart.x, rectEnd.x);
                xMax = Mathf.Max(rectStart.x, rectEnd.x);
                yMin = Mathf.Min(rectStart.y, rectEnd.y);
                yMax = Mathf.Max(rectStart.y, rectEnd.y);
            }
            else
            {
                Vector2Int center = previewHover.Value;
                int radius = tool == Tool.Brush ? brushRadius : 0;
                xMin = center.x - radius;
                xMax = center.x + radius;
                yMin = center.y - radius;
                yMax = center.y + radius;
            }

            xMin = Mathf.Clamp(xMin, 0, counts.x - 1);
            xMax = Mathf.Clamp(xMax, 0, counts.x - 1);
            yMin = Mathf.Clamp(yMin, 0, counts.y - 1);
            yMax = Mathf.Clamp(yMax, 0, counts.y - 1);

            Rect hoverRect = PreviewRangeRect(rect, counts, xMin, yMin, xMax, yMax);
            EditorGUI.DrawRect(hoverRect, new Color(1f, 1f, 1f, 0.12f));
            DrawRectOutline(hoverRect, new Color(1f, 1f, 1f, 0.95f), 1f);
        }

        private Rect PreviewRangeRect(
            Rect canvas, Vector2Int counts, int xMin, int yMin, int xMax, int yMax)
        {
            float px = previewCellPixels;
            float x = canvas.x + xMin * px;
            float y = canvas.y + (counts.y - 1 - yMax) * px;
            float width = (xMax - xMin + 1) * px;
            float height = (yMax - yMin + 1) * px;
            return new Rect(x, y, width, height);
        }

        private static void DrawRectOutline(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private void HandlePreviewEvents(Rect rect, Vector2Int counts)
        {
            Event e = Event.current;

            if (e == null)
            {
                return;
            }

            bool inside = TryGetPreviewCell(rect, counts, e.mousePosition, out Vector2Int cell);

            if (e.type == EventType.MouseMove || e.type == EventType.Repaint)
            {
                Vector2Int? newHover = inside ? cell : (Vector2Int?)null;

                if (previewHover != newHover)
                {
                    previewHover = newHover;
                    Repaint();
                }
            }

            if (EditorApplication.isPlaying || !paintingEnabled)
            {
                return;
            }

            if (e.type == EventType.MouseDown && inside && (e.button == 0 || e.button == 1))
            {
                erase = e.button == 1;
                BeginStroke();

                if (tool == Tool.Rectangle)
                {
                    rectStart = cell;
                    rectEnd = cell;
                    rectActive = true;
                }
                else
                {
                    ApplyTool(cell, !erase);
                }

                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDrag && painting)
            {
                if (tool == Tool.Rectangle && rectActive)
                {
                    if (inside)
                    {
                        rectEnd = cell;
                    }
                }
                else if (tool == Tool.Brush && inside)
                {
                    ApplyBrush(cell, !erase);
                }

                if (inside)
                {
                    e.Use();
                }

                Repaint();
            }
        }

        private bool TryGetPreviewCell(
            Rect rect, Vector2Int counts, Vector2 mouse, out Vector2Int cell)
        {
            cell = default;

            if (!rect.Contains(mouse))
            {
                return false;
            }

            float px = Mathf.Max(1f, previewCellPixels);
            int x = Mathf.FloorToInt((mouse.x - rect.x) / px);
            int y = Mathf.FloorToInt((rect.yMax - mouse.y) / px);

            if (x < 0 || x >= counts.x || y < 0 || y >= counts.y)
            {
                return false;
            }

            cell = new Vector2Int(x, y);
            return true;
        }

        private void EnsurePreviewTexture(Vector2Int counts)
        {
            if (previewTexture == null ||
                previewTexture.width != counts.x ||
                previewTexture.height != counts.y)
            {
                DestroyPreviewTexture();
                previewTexture = new Texture2D(counts.x, counts.y, TextureFormat.RGBA32, false)
                {
                    name = "BuildableAreaPreview",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                previewDirty = true;
            }

            if (!previewDirty)
            {
                return;
            }

            Color32[] pixels = new Color32[counts.x * counts.y];
            Color32 buildableColor = new Color32(76, 175, 80, 255);
            Color32 blockedColor = new Color32(120, 70, 70, 255);
            Color32 pollutedColor = new Color32(245, 120, 15, 255);
            Color32 otherPollutedColor = new Color32(170, 90, 25, 255);
            Color32 overlapColor = new Color32(235, 45, 10, 255);
            Color32 faintBuildableColor = new Color32(35, 60, 45, 255);
            Color32 emptyColor = new Color32(45, 45, 50, 255);

            for (int y = 0; y < counts.y; y++)
            {
                for (int x = 0; x < counts.x; x++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    bool active = mask.GetCell(x, y);
                    bool buildable = grid.IsCellBuildable(cell);

                    if (IsLevelMode)
                    {
                        bool otherPolluted = IsCellPollutedByAnyLevel(cell, selectedEntrance);

                        if (active)
                        {
                            pixels[x + y * counts.x] = buildable ? overlapColor : pollutedColor;
                        }
                        else if (otherPolluted)
                        {
                            pixels[x + y * counts.x] = otherPollutedColor;
                        }
                        else
                        {
                            pixels[x + y * counts.x] = buildable
                                ? faintBuildableColor
                                : emptyColor;
                        }
                    }
                    else
                    {
                        bool polluted = IsCellPollutedByAnyLevel(cell);

                        if (polluted)
                        {
                            // 可建模式也强制橙色，避免和绿色混淆。
                            pixels[x + y * counts.x] = new Color32(240, 100, 15, 255);
                        }
                        else
                        {
                            pixels[x + y * counts.x] = active ? buildableColor : blockedColor;
                        }
                    }
                }
            }

            previewTexture.SetPixels32(pixels);
            previewTexture.Apply(false, false);
            previewDirty = false;
        }

        private void DestroyPreviewTexture()
        {
            if (previewTexture != null)
            {
                DestroyImmediate(previewTexture);
                previewTexture = null;
            }
        }

        private void DrawStats()
        {
            if (mask == null || !mask.IsValid)
            {
                return;
            }

            int painted = mask.CountBuildable();
            int total = mask.CellCount;
            float percent = total > 0 ? painted * 100f / total : 0f;

            EditorGUILayout.LabelField("统计", EditorStyles.boldLabel);

            if (IsLevelMode)
            {
                string levelName = selectedEntrance != null
                    ? $"关卡 {selectedEntrance.LevelNumber}"
                    : "未选择关卡";

                EditorGUILayout.LabelField($"{levelName}：污染 {painted} / {total}（{percent:0.0}%）");
                EditorGUILayout.LabelField($"未污染 {total - painted}");
            }
            else
            {
                EditorGUILayout.LabelField($"可建 {painted} / {total}（{percent:0.0}%）");
                EditorGUILayout.LabelField($"不可建 {total - painted}");
            }

            EditorGUILayout.LabelField($"网格 {grid.CellCounts.x}×{grid.CellCounts.y}，单格 {grid.CellSize:0.##}");
        }

        private void DrawFooter()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("保存掩码资产", GUILayout.Height(26f)))
            {
                if (mask != null)
                {
                    EditorUtility.SetDirty(mask);
                }

                AssetDatabase.SaveAssets();
                Debug.Log("[可建面积画笔] 掩码资产已保存。", mask);
            }

            if (GUILayout.Button("重新计算网格", GUILayout.Height(26f)))
            {
                grid.Rebuild();
                meshDirty = true;
                previewDirty = true;
                SceneView.RepaintAll();
                Repaint();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "掩码资产会自动保存；BuildingGrid 的引用 / 开关写在场景里，记得 Ctrl+S 保存场景。",
                MessageType.None);
        }

        // ---------------- 数据操作 ----------------

        private void FindGrid()
        {
            BuildingGrid found = Object.FindFirstObjectByType<BuildingGrid>(FindObjectsInactive.Include);

            if (found != grid)
            {
                grid = found;
                SyncActiveMask();
                meshDirty = true;
            }

            if (grid != null && !grid.IsConfigured)
            {
                grid.Rebuild();
            }
        }

        private void RefreshFromGrid()
        {
            if (grid == null)
            {
                mask = null;
                lastGridMask = null;
                return;
            }

            BuildableAreaMask active = IsLevelMode
                ? (selectedEntrance != null ? selectedEntrance.AreaMask : null)
                : grid.AreaMask;

            if (active != lastGridMask)
            {
                lastGridMask = active;
                mask = active;
                meshDirty = true;
                previewDirty = true;
                SceneView.RepaintAll();
                Repaint();
            }
        }

        private void CreateOrBindMask()
        {
            if (IsLevelMode)
            {
                CreateOrBindLevelMask();
                return;
            }

            if (grid == null)
            {
                return;
            }

            BuildableAreaMask target = mask != null
                ? mask
                : AssetDatabase.LoadAssetAtPath<BuildableAreaMask>(DefaultMaskPath);

            if (target == null)
            {
                if (!AssetDatabase.IsValidFolder(MaskFolder))
                {
                    AssetDatabase.CreateFolder("Assets/Data", "Buildings");
                }

                target = ScriptableObject.CreateInstance<BuildableAreaMask>();
                grid.BakeProceduralMaskInto(target);
                AssetDatabase.CreateAsset(target, DefaultMaskPath);
            }
            else if (!target.IsCompatible(grid.GridCenter, grid.CellSize, grid.CellCounts))
            {
                if (!EditorUtility.DisplayDialog(
                        "重建可建面积掩码",
                        "当前掩码的网格签名与网格不一致。\n\n" +
                        "重建会按当前台地重新烘焙，旧的手绘内容会丢失。继续？",
                        "重建", "取消"))
                {
                    return;
                }

                Undo.RecordObject(target, "重建可建面积掩码");
                grid.BakeProceduralMaskInto(target);
            }

            AssignMask(target);
        }

        private void AssignMask(BuildableAreaMask target)
        {
            if (grid == null || target == null)
            {
                return;
            }

            Undo.RecordObject(grid, "绑定可建面积掩码");
            grid.SetAreaMask(target, true);
            EditorUtility.SetDirty(grid);
            EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);

            mask = target;
            lastGridMask = target;
            meshDirty = true;
            previewDirty = true;
            SceneView.RepaintAll();
            Repaint();

            Debug.Log($"[可建面积画笔] 已绑定掩码 {AssetDatabase.GetAssetPath(target)}，" +
                      $"网格 {grid.CellCounts.x}×{grid.CellCounts.y}。", target);
        }

        // ---------------- 关卡污染面积 ----------------

        private void CreateOrBindLevelMask()
        {
            if (grid == null || selectedEntrance == null)
            {
                return;
            }

            string path = $"{MaskFolder}/LevelArea_{selectedEntrance.LevelNumber}.asset";
            LevelAreaMask target = selectedEntrance.AreaMask;
            bool created = false;

            if (target == null)
            {
                target = AssetDatabase.LoadAssetAtPath<LevelAreaMask>(path);

                if (target == null)
                {
                    if (!AssetDatabase.IsValidFolder(MaskFolder))
                    {
                        AssetDatabase.CreateFolder("Assets/Data", "Buildings");
                    }

                    target = ScriptableObject.CreateInstance<LevelAreaMask>();
                    BakeLevelRectInto(target);
                    AssetDatabase.CreateAsset(
                        target, AssetDatabase.GenerateUniqueAssetPath(path));
                    created = true;
                }
            }

            if (!created && !target.IsCompatible(
                    grid.GridCenter, grid.CellSize, grid.CellCounts))
            {
                if (!EditorUtility.DisplayDialog(
                        "重建关卡污染掩码",
                        $"关卡 {selectedEntrance.LevelNumber} 的污染掩码签名与当前网格不一致。\n\n" +
                        "重建会按当前矩形占地重新烘焙，旧的手绘内容会丢失。继续？",
                        "重建", "取消"))
                {
                    return;
                }

                Undo.RecordObject(target, "重建关卡污染掩码");
                BakeLevelRectInto(target);
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssets();
            }

            AssignLevelMask(target);

            if (created)
            {
                Debug.Log($"[可建面积画笔] 已创建关卡污染掩码 " +
                          $"{AssetDatabase.GetAssetPath(target)}。", target);
            }
        }

        private void BakeLevelRectConfirm()
        {
            if (grid == null || selectedEntrance == null)
            {
                return;
            }

            LevelAreaMask target = selectedEntrance.AreaMask;

            if (target == null)
            {
                CreateOrBindLevelMask();
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "从矩形占地烘焙",
                    "会用当前 footprintCells 矩形覆盖这张掩码上的手绘污染区。继续？",
                    "烘焙", "取消"))
            {
                return;
            }

            Undo.RecordObject(target, "从矩形占地烘焙");
            BakeLevelRectInto(target);
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();

            mask = target;
            lastGridMask = target;
            meshDirty = true;
            previewDirty = true;
            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>把 LevelEntrance 的矩形占地写进掩码（其余格清空）。</summary>
        private void BakeLevelRectInto(LevelAreaMask target)
        {
            if (grid == null || selectedEntrance == null || target == null)
            {
                return;
            }

            target.Initialize(grid.GridCenter, grid.CellSize, grid.CellCounts, false);

            if (!grid.WorldToCell(
                    selectedEntrance.PollutionCore.position, out Vector2Int coreCell))
            {
                return;
            }

            Vector2Int size = selectedEntrance.FootprintCells;
            int xMin = coreCell.x - size.x / 2;
            int yMin = coreCell.y - size.y / 2;

            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    target.SetCell(xMin + x, yMin + y, true);
                }
            }
        }

        private void AssignLevelMask(LevelAreaMask target)
        {
            if (selectedEntrance == null)
            {
                return;
            }

            Undo.RecordObject(selectedEntrance, "绑定关卡污染面积掩码");

            SerializedObject serialized = new SerializedObject(selectedEntrance);
            SerializedProperty property = serialized.FindProperty("areaMask");

            if (property != null)
            {
                property.objectReferenceValue = target;
                serialized.ApplyModifiedProperties();
            }

            EditorUtility.SetDirty(selectedEntrance);

            if (selectedEntrance.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(selectedEntrance.gameObject.scene);
            }

            mask = target;
            lastGridMask = target;
            meshDirty = true;
            previewDirty = true;
            SceneView.RepaintAll();
            Repaint();

            Debug.Log($"[可建面积画笔] 关卡 {selectedEntrance.LevelNumber} 已绑定污染掩码 " +
                      $"{(target != null ? AssetDatabase.GetAssetPath(target) : "（无）")}。",
                      selectedEntrance);
        }

        private void BakeFromIsland()
        {
            if (grid == null || mask == null)
            {
                return;
            }

            Undo.RecordObject(mask, "从台地烘焙可建面积");
            grid.BakeProceduralMaskInto(mask);
            EditorUtility.SetDirty(mask);
            AssetDatabase.SaveAssets();

            meshDirty = true;
            previewDirty = true;
            grid.Rebuild();
            SceneView.RepaintAll();
            Repaint();
        }

        private void FillMask(bool value)
        {
            if (mask == null)
            {
                return;
            }

            string label = IsLevelMode
                ? (value ? "全部污染" : "全部清除污染")
                : (value ? "全部可建" : "全部不可建");

            Undo.RecordObject(mask, label);
            mask.Fill(value);
            EditorUtility.SetDirty(mask);
            AssetDatabase.SaveAssets();

            meshDirty = true;
            previewDirty = true;

            if (!IsLevelMode && grid != null)
            {
                grid.Rebuild();
            }

            SceneView.RepaintAll();
            Repaint();
        }

        private void RebuildMask()
        {
            if (grid == null || mask == null)
            {
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "按当前网格重建",
                    "会按当前网格和台地重新烘焙，现有手绘内容会丢失。继续？",
                    "重建", "取消"))
            {
                return;
            }

            Undo.RecordObject(mask, "重建可建面积掩码");
            grid.BakeProceduralMaskInto(mask);
            EditorUtility.SetDirty(mask);
            AssetDatabase.SaveAssets();

            Undo.RecordObject(grid, "启用重建后的掩码");
            grid.SetAreaMask(mask, true);
            EditorUtility.SetDirty(grid);
            EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
            lastGridMask = mask;

            meshDirty = true;
            previewDirty = true;
            SceneView.RepaintAll();
            Repaint();
        }

        // ---------------- Scene 视图 ----------------

        private void OnSceneGUI(SceneView sceneView)
        {
            IsSceneOverlayVisible = false;

            if (grid == null)
            {
                return;
            }

            if (!grid.IsConfigured)
            {
                grid.Rebuild();
            }

            if (IsLevelMode)
            {
                if (selectedEntrance == null)
                {
                    if (Event.current != null && Event.current.type == EventType.Repaint)
                    {
                        DrawSceneHint("还没有选择关卡：在窗口上方选择「关卡污染区」对应的关卡。");
                    }

                    return;
                }

                if (mask == null)
                {
                    if (Event.current != null && Event.current.type == EventType.Repaint)
                    {
                        DrawSceneHint("这个关卡还没有污染面积掩码：在窗口里点「创建 / 绑定关卡掩码」。");
                    }

                    return;
                }

                if (!mask.IsValid)
                {
                    if (Event.current != null && Event.current.type == EventType.Repaint)
                    {
                        DrawSceneHint("污染掩码数据异常：在窗口里点「按当前网格重建」。");
                    }

                    return;
                }

                if (!mask.IsCompatible(grid.GridCenter, grid.CellSize, grid.CellCounts))
                {
                    if (Event.current != null && Event.current.type == EventType.Repaint)
                    {
                        DrawSceneHint("污染掩码签名不匹配：在窗口里点「按当前网格重建」。");
                    }

                    return;
                }
            }
            else
            {
                if (!grid.IsAreaMaskActive)
                {
                    if (Event.current != null && Event.current.type == EventType.Repaint)
                    {
                        DrawSceneHint("还没启用掩码：在窗口里点「启用 Scene 绘画」。");
                    }

                    return;
                }

                if (mask == null)
                {
                    return;
                }

                if (!grid.HasCompatibleAreaMask)
                {
                    if (Event.current != null && Event.current.type == EventType.Repaint)
                    {
                        DrawSceneHint("掩码签名不匹配：在窗口里点「按当前网格重建」。");
                    }

                    return;
                }
            }

            if (meshDirty || !OverlayMatchesGrid())
            {
                RebuildOverlayMesh();
            }

            Event e = Event.current;

            if (e == null)
            {
                return;
            }

            if (e.type == EventType.Layout)
            {
                if (paintingEnabled && !EditorApplication.isPlaying)
                {
                    HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                }

                return;
            }

            if (e.type == EventType.Repaint)
            {
                IsSceneOverlayVisible = true;
                DrawOverlay();
                DrawHover();
                DrawSceneHint(null, BuildHoverHint());
            }

            if (!paintingEnabled || EditorApplication.isPlaying || e.alt)
            {
                return;
            }

            bool inside = TryGetCellUnderMouse(e.mousePosition, out Vector2Int cell);
            UpdateHover(inside ? cell : (Vector2Int?)null);

            // 鼠标移出网格也要能结束笔画，否则 Undo 组会一直开着
            if (e.type == EventType.MouseUp)
            {
                if (painting)
                {
                    if (tool == Tool.Rectangle && rectActive)
                    {
                        ApplyRect(rectStart, rectEnd, !erase);
                    }

                    EndStroke();
                    e.Use();
                }

                return;
            }

            if (!inside)
            {
                return;
            }

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 || e.button == 1)
                    {
                        erase = e.button == 1;
                        BeginStroke();

                        if (tool == Tool.Rectangle)
                        {
                            rectStart = cell;
                            rectEnd = cell;
                            rectActive = true;
                            SceneView.RepaintAll();
                        }
                        else
                        {
                            ApplyTool(cell, !erase);
                        }

                        e.Use();
                    }

                    break;

                case EventType.MouseDrag:
                    if (!painting)
                    {
                        break;
                    }

                    if (tool == Tool.Rectangle && rectActive)
                    {
                        rectEnd = cell;
                        SceneView.RepaintAll();
                    }
                    else if (tool == Tool.Brush)
                    {
                        ApplyBrush(cell, !erase);
                    }

                    e.Use();
                    break;
            }
        }

        private bool TryGetCellUnderMouse(Vector2 mousePosition, out Vector2Int cell)
        {
            cell = default;

            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            Plane plane = new Plane(Vector3.up, new Vector3(0f, grid.GridPlaneY, 0f));

            if (!plane.Raycast(ray, out float distance))
            {
                return false;
            }

            Vector3 world = ray.GetPoint(distance);

            if (!grid.WorldToCell(world, out cell))
            {
                return false;
            }

            return grid.IsCellInsideGrid(cell);
        }

        private void UpdateHover(Vector2Int? cell)
        {
            if (hoverCell != cell)
            {
                hoverCell = cell;
                SceneView.RepaintAll();
            }
        }

        private void DrawSceneHint(string message = null, string extra = null)
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }

            string instructions = message != null
                ? message
                : (!paintingEnabled
                    ? "绘画模式已关闭（在窗口里打开「绘画模式」）"
                    : IsLevelMode
                        ? "左键画「污染」 / 右键清除 / Ctrl+Z 撤销"
                        : "左键画「可建」 / 右键清除 / Ctrl+Z 撤销");

            string title = IsLevelMode
                ? $"关卡污染面积画笔 · 关卡 {(selectedEntrance != null ? selectedEntrance.LevelNumber : 0)}"
                : "可建面积画笔";
            string legend = IsLevelMode
                ? "橙红 = 污染（盖住可建绿）　深红 = 核心格　浅绿 = 可建区"
                : "绿 = 可建　红 = 不可建　橙 = 关卡污染区（优先显示）";

            Handles.BeginGUI();
            GUILayout.BeginArea(
                new Rect(10f, 10f, 460f, string.IsNullOrEmpty(extra) ? 78f : 96f),
                EditorStyles.helpBox);
            GUILayout.Label(title, EditorStyles.boldLabel);
            GUILayout.Label(instructions, EditorStyles.miniLabel);
            GUILayout.Label(legend, EditorStyles.miniLabel);

            if (!string.IsNullOrEmpty(extra))
            {
                GUILayout.Label(extra, EditorStyles.miniLabel);
            }

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private string BuildHoverHint()
        {
            if (!hoverCell.HasValue || grid == null)
            {
                return null;
            }

            Vector2Int cell = hoverCell.Value;
            bool buildable = grid.IsCellBuildable(cell);
            bool activePolluted = IsCellPollutedByActiveMask(cell);
            bool otherPolluted = IsCellPollutedByAnyLevel(
                cell, IsLevelMode ? selectedEntrance : null);
            bool polluted = activePolluted || otherPolluted;

            if (IsLevelMode)
            {
                if (activePolluted && buildable)
                {
                    return $"格 ({cell.x}, {cell.y})：污染 + 可建区（运行时关卡优先）";
                }

                if (activePolluted)
                {
                    return $"格 ({cell.x}, {cell.y})：污染（未净化时不可建）";
                }

                if (buildable)
                {
                    return $"格 ({cell.x}, {cell.y})：可建区，尚未标污染";
                }

                return null;
            }

            if (polluted)
            {
                return $"格 ({cell.x}, {cell.y})：被关卡污染区覆盖（运行时关卡优先）";
            }

            return null;
        }

        private void DrawOverlay()
        {
            if (overlayMesh == null && boundaryMesh == null)
            {
                return;
            }

            if (overlayMaterial != null)
            {
                overlayMaterial.SetPass(0);

                if (overlayMesh != null)
                {
                    Graphics.DrawMeshNow(overlayMesh, Matrix4x4.identity);
                }

                if (boundaryMesh != null)
                {
                    overlayMaterial.SetPass(0);
                    Graphics.DrawMeshNow(boundaryMesh, Matrix4x4.identity);
                }

                return;
            }

            // 兜底：没有 Hidden/Internal-Colored 时逐格画
            for (int y = 0; y < grid.CellCounts.y; y++)
            {
                for (int x = 0; x < grid.CellCounts.x; x++)
                {
                    DrawCellQuad(new Vector2Int(x, y), GetOverlayColor(new Vector2Int(x, y)), Color.clear);
                }
            }
        }

        private void DrawHover()
        {
            if (!paintingEnabled || EditorApplication.isPlaying || mask == null)
            {
                return;
            }

            Color fill = new Color(1f, 1f, 1f, 0.12f);
            Color outline = new Color(1f, 1f, 1f, 0.9f);

            if (tool == Tool.Rectangle && rectActive)
            {
                int xMin = Mathf.Min(rectStart.x, rectEnd.x);
                int xMax = Mathf.Max(rectStart.x, rectEnd.x);
                int yMin = Mathf.Min(rectStart.y, rectEnd.y);
                int yMax = Mathf.Max(rectStart.y, rectEnd.y);

                for (int y = yMin; y <= yMax; y++)
                {
                    for (int x = xMin; x <= xMax; x++)
                    {
                        DrawCellQuad(new Vector2Int(x, y), fill, outline);
                    }
                }

                return;
            }

            if (!hoverCell.HasValue)
            {
                return;
            }

            Vector2Int center = hoverCell.Value;
            int radius = tool == Tool.Brush ? brushRadius : 0;

            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    Vector2Int cell = new Vector2Int(center.x + dx, center.y + dy);

                    if (grid.IsCellInsideGrid(cell))
                    {
                        DrawCellQuad(cell, fill, outline);
                    }
                }
            }
        }

        private void DrawCellQuad(Vector2Int cell, Color fill, Color outline)
        {
            Vector3 min = grid.CellMinCorner(cell);
            float size = grid.CellSize;
            float y = grid.GridPlaneY + 0.03f;

            Vector3[] verts =
            {
                new Vector3(min.x, y, min.z),
                new Vector3(min.x, y, min.z + size),
                new Vector3(min.x + size, y, min.z + size),
                new Vector3(min.x + size, y, min.z),
            };

            CompareFunction previousZTest = Handles.zTest;
            Handles.zTest = CompareFunction.Always;
            Handles.DrawSolidRectangleWithOutline(verts, fill, outline);
            Handles.zTest = previousZTest;
        }

        // ---------------- 笔刷 ----------------

        private void BeginStroke()
        {
            if (painting)
            {
                return;
            }

            string label = IsLevelMode ? "绘制关卡污染面积" : "绘制可建面积";

            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(label);
            Undo.RecordObject(mask, label);
            painting = true;
        }

        private void EndStroke()
        {
            if (!painting)
            {
                return;
            }

            Undo.CollapseUndoOperations(undoGroup);
            painting = false;
            rectActive = false;

            if (mask != null)
            {
                EditorUtility.SetDirty(mask);
            }

            AssetDatabase.SaveAssets();

            // 关卡掩码不参与 BuildableGrid 的可建判定，不需要重建网格；
            // 它会在下一次 Play 时由 LevelManager 登记。
            if (!IsLevelMode && grid != null)
            {
                grid.Rebuild();
            }

            meshDirty = true;
            previewDirty = true;
            SceneView.RepaintAll();
            Repaint();
        }

        private void ApplyTool(Vector2Int cell, bool value)
        {
            if (tool == Tool.Bucket)
            {
                ApplyBucket(cell, value);
                return;
            }

            ApplyBrush(cell, value);
        }

        private void ApplyBrush(Vector2Int center, bool value)
        {
            if (mask == null)
            {
                return;
            }

            for (int dy = -brushRadius; dy <= brushRadius; dy++)
            {
                for (int dx = -brushRadius; dx <= brushRadius; dx++)
                {
                    mask.SetCell(center.x + dx, center.y + dy, value);
                }
            }

            MarkMaskChanged();
        }

        private void ApplyRect(Vector2Int a, Vector2Int b, bool value)
        {
            if (mask == null)
            {
                return;
            }

            int xMin = Mathf.Min(a.x, b.x);
            int xMax = Mathf.Max(a.x, b.x);
            int yMin = Mathf.Min(a.y, b.y);
            int yMax = Mathf.Max(a.y, b.y);

            for (int y = yMin; y <= yMax; y++)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    mask.SetCell(x, y, value);
                }
            }

            MarkMaskChanged();
        }

        private void ApplyBucket(Vector2Int start, bool value)
        {
            if (mask == null || !grid.IsCellInsideGrid(start))
            {
                return;
            }

            bool target = mask.GetCell(start);

            if (target == value)
            {
                return;
            }

            Stack<Vector2Int> stack = new Stack<Vector2Int>();
            stack.Push(start);

            while (stack.Count > 0)
            {
                Vector2Int cell = stack.Pop();

                if (!grid.IsCellInsideGrid(cell) || mask.GetCell(cell) != target)
                {
                    continue;
                }

                mask.SetCell(cell, value);
                stack.Push(new Vector2Int(cell.x + 1, cell.y));
                stack.Push(new Vector2Int(cell.x - 1, cell.y));
                stack.Push(new Vector2Int(cell.x, cell.y + 1));
                stack.Push(new Vector2Int(cell.x, cell.y - 1));
            }

            MarkMaskChanged();
        }

        private void MarkMaskChanged()
        {
            if (mask != null)
            {
                EditorUtility.SetDirty(mask);
            }

            meshDirty = true;
            previewDirty = true;
            SceneView.RepaintAll();
            Repaint();
        }

        // ---------------- 叠加层 ----------------

        private Color GetOverlayColor(Vector2Int cell)
        {
            if (grid == null || mask == null)
            {
                return Color.clear;
            }

            bool active = mask.GetCell(cell);
            bool buildable = grid.IsCellBuildable(cell);

            if (IsLevelMode)
            {
                bool otherPolluted = IsCellPollutedByAnyLevel(cell, selectedEntrance);

                if (active)
                {
                    // 污染格用高强度橙红，保证盖住下面的可建绿色，不再混成黄绿。
                    return buildable
                        ? new Color(1.00f, 0.20f, 0.02f, 0.78f)   // 污染 + 可建：红橙
                        : new Color(1.00f, 0.48f, 0.02f, 0.72f);  // 污染
                }

                if (otherPolluted)
                {
                    return new Color(1.00f, 0.48f, 0.02f, 0.30f); // 其他关卡污染：橙
                }

                if (buildable)
                {
                    // 只留极淡的绿做参考，避免和污染橙抢视觉。
                    return new Color(0.12f, 0.85f, 0.28f, 0.03f);
                }

                return new Color(0.20f, 0.20f, 0.24f, 0.02f);
            }

            // 可建面积模式：只要被关卡污染覆盖，就直接用橙色，不再和绿 / 红做混色。
            if (IsCellPollutedByAnyLevel(cell))
            {
                return new Color(1.00f, 0.40f, 0.02f, 0.64f);
            }

            return active
                ? new Color(0.12f, 0.85f, 0.28f, 0.38f)
                : new Color(0.92f, 0.18f, 0.12f, 0.28f);
        }

        private bool OverlayMatchesGrid()
        {
            return cachedCounts == grid.CellCounts
                && Mathf.Approximately(cachedSize, grid.CellSize)
                && cachedOrigin == grid.Origin;
        }

        private void RebuildOverlayMesh()
        {
            if (grid == null || mask == null)
            {
                return;
            }

            EnsureOverlayMaterial();

            Vector2Int counts = grid.CellCounts;
            float size = grid.CellSize;
            float inset = Mathf.Clamp(size * 0.04f, 0.005f, size * 0.2f);
            float y = grid.GridPlaneY + 0.02f;

            List<Vector3> vertices = new List<Vector3>(counts.x * counts.y * 4);
            List<Color> colors = new List<Color>(counts.x * counts.y * 4);
            List<int> triangles = new List<int>(counts.x * counts.y * 6);

            for (int cellY = 0; cellY < counts.y; cellY++)
            {
                for (int cellX = 0; cellX < counts.x; cellX++)
                {
                    Color color = GetOverlayColor(new Vector2Int(cellX, cellY));

                    Vector3 min = grid.CellMinCorner(new Vector2Int(cellX, cellY));
                    float x0 = min.x + inset;
                    float x1 = min.x + size - inset;
                    float z0 = min.z + inset;
                    float z1 = min.z + size - inset;

                    int baseIndex = vertices.Count;
                    vertices.Add(new Vector3(x0, y, z0));
                    vertices.Add(new Vector3(x0, y, z1));
                    vertices.Add(new Vector3(x1, y, z1));
                    vertices.Add(new Vector3(x1, y, z0));

                    for (int i = 0; i < 4; i++)
                    {
                        colors.Add(color);
                    }

                    triangles.Add(baseIndex);
                    triangles.Add(baseIndex + 2);
                    triangles.Add(baseIndex + 1);

                    triangles.Add(baseIndex);
                    triangles.Add(baseIndex + 3);
                    triangles.Add(baseIndex + 2);
                }
            }

            if (overlayMesh == null)
            {
                overlayMesh = new Mesh
                {
                    name = "BuildableAreaPainterOverlay",
                    indexFormat = IndexFormat.UInt32,
                };
            }
            else
            {
                overlayMesh.Clear();
            }

            overlayMesh.SetVertices(vertices);
            overlayMesh.SetColors(colors);
            overlayMesh.SetTriangles(triangles, 0);
            overlayMesh.RecalculateBounds();

            RebuildBoundaryMesh(counts, size, y + 0.005f);

            cachedCounts = counts;
            cachedSize = size;
            cachedOrigin = grid.Origin;
            meshDirty = false;
        }

        /// <summary>
        /// 可建区域边界：绿格与非可建格（含网格外）交界处画一圈亮色描边。
        /// 只依赖掩码，不受关卡污染区 / 建筑占用影响。
        /// </summary>
        private void RebuildBoundaryMesh(Vector2Int counts, float size, float y)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Color> colors = new List<Color>();
            List<int> triangles = new List<int>();

            Color lineColor = IsLevelMode
                ? new Color(1f, 0.62f, 0.15f, 0.95f)
                : new Color(1f, 0.95f, 0.60f, 0.90f);
            float thickness = Mathf.Max(0.02f, size * 0.035f);

            for (int cellY = 0; cellY < counts.y; cellY++)
            {
                for (int cellX = 0; cellX < counts.x; cellX++)
                {
                    if (!mask.GetCell(cellX, cellY))
                    {
                        continue;
                    }

                    Vector3 min = grid.CellMinCorner(new Vector2Int(cellX, cellY));
                    float x0 = min.x;
                    float x1 = min.x + size;
                    float z0 = min.z;
                    float z1 = min.z + size;

                    if (!IsMaskBuildable(cellX - 1, cellY, counts))
                    {
                        AddLineQuad(vertices, colors, triangles,
                            new Vector3(x0, y, z0), new Vector3(x0, y, z1), thickness, lineColor);
                    }

                    if (!IsMaskBuildable(cellX + 1, cellY, counts))
                    {
                        AddLineQuad(vertices, colors, triangles,
                            new Vector3(x1, y, z0), new Vector3(x1, y, z1), thickness, lineColor);
                    }

                    if (!IsMaskBuildable(cellX, cellY - 1, counts))
                    {
                        AddLineQuad(vertices, colors, triangles,
                            new Vector3(x0, y, z0), new Vector3(x1, y, z0), thickness, lineColor);
                    }

                    if (!IsMaskBuildable(cellX, cellY + 1, counts))
                    {
                        AddLineQuad(vertices, colors, triangles,
                            new Vector3(x0, y, z1), new Vector3(x1, y, z1), thickness, lineColor);
                    }
                }
            }

            if (boundaryMesh == null)
            {
                boundaryMesh = new Mesh
                {
                    name = "BuildableAreaBoundary",
                    indexFormat = IndexFormat.UInt32,
                };
            }
            else
            {
                boundaryMesh.Clear();
            }

            boundaryMesh.SetVertices(vertices);
            boundaryMesh.SetColors(colors);
            boundaryMesh.SetTriangles(triangles, 0);
            boundaryMesh.RecalculateBounds();
        }

        private bool IsMaskBuildable(int x, int y, Vector2Int counts)
        {
            return x >= 0 && x < counts.x && y >= 0 && y < counts.y && mask.GetCell(x, y);
        }

        private static void AddLineQuad(
            List<Vector3> vertices, List<Color> colors, List<int> triangles,
            Vector3 start, Vector3 end, float thickness, Color color)
        {
            Vector3 direction = end - start;

            if (direction.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            Vector3 perpendicular =
                Vector3.Cross(Vector3.up, direction.normalized) * (thickness * 0.5f);

            int baseIndex = vertices.Count;
            vertices.Add(start - perpendicular);
            vertices.Add(start + perpendicular);
            vertices.Add(end + perpendicular);
            vertices.Add(end - perpendicular);

            for (int i = 0; i < 4; i++)
            {
                colors.Add(color);
            }

            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 1);

            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 3);
            triangles.Add(baseIndex + 2);
        }

        private void EnsureOverlayMaterial()
        {
            if (overlayMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find("Hidden/Internal-Colored");

            if (shader == null)
            {
                Debug.LogWarning("[可建面积画笔] 找不到 Hidden/Internal-Colored，改用逐格绘制。");
                return;
            }

            overlayMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            overlayMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            overlayMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            overlayMaterial.SetInt("_Cull", (int)CullMode.Off);
            overlayMaterial.SetInt("_ZWrite", 0);
            overlayMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
            overlayMaterial.SetInt("_Lighting", 0);
            overlayMaterial.SetColor("_Color", Color.white);
        }

        private void DestroyOverlay()
        {
            if (overlayMesh != null)
            {
                DestroyImmediate(overlayMesh);
                overlayMesh = null;
            }

            if (boundaryMesh != null)
            {
                DestroyImmediate(boundaryMesh);
                boundaryMesh = null;
            }

            if (overlayMaterial != null)
            {
                DestroyImmediate(overlayMaterial);
                overlayMaterial = null;
            }
        }
    }
}
