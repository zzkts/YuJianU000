using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Island;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 海岸拓扑编辑器：A 径向海岸网格 / B 矩形网格局部细分 /
    /// C Delaunay 不规则三角化 / D 仅 Shader 柔化。
    /// 换方案会重建海面网格，但保留原海面还原能力和低模外观参数。
    /// </summary>
    public class LowPolyOceanTopologyWindow : EditorWindow
    {
        private const string MenuPath = "屿见/场景/海面海岸拓扑（4 种方案）";
        private const string LowPolyWindowMenuPath = "屿见/场景/低模海面（三角拓扑 / 还原）";

        private static readonly string[] ModeNames =
        {
            "A. 径向海岸网格",
            "B. 矩形网格局部细分",
            "C. Delaunay 不规则三角化",
            "D. 仅 Shader 柔化",
        };

        private static readonly string[] ModeDescriptions =
        {
            "实验：以岛为中心生成同心环网格，会在岛中心挖洞，只适合当前海岛、复用性差。可复用方案请用“碰撞体海面”。",
            "保留方形海面，岛附近细分到细网格边长，越远越大；过渡是渐变矩形网格，比 A 更有行列感。",
            "海岸附近用非均匀散点做 Delaunay 三角化；三角形最有机、最不规则，生成会稍慢。",
            "整块矩形网格 + Shader 遮罩；加宽岸边泡沫/浅水过渡并柔化裁剪边。推荐用“碰撞体海面”工具烘焙遮罩。",
        };

        private GameObject _ocean;
        private Vector2 _scroll;
        private LowPolyOceanMeshSettings _settings = LowPolyOceanMeshSettings.Default;
        private float _shoreSmooth;
        private bool _hasIslandSettings;

        [MenuItem(MenuPath, false, 216)]
        public static void OpenWindow()
        {
            LowPolyOceanTopologyWindow window =
                GetWindow<LowPolyOceanTopologyWindow>("海岸拓扑");
            window.minSize = new Vector2(440f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            if (_ocean == null)
            {
                _ocean = LowPolyOceanTool.FindOcean();
            }

            SyncFromScene();
            Selection.selectionChanged += OnSelectionChanged;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            if (Selection.activeGameObject == null)
            {
                return;
            }

            bool looksLikeOcean = Selection.activeGameObject.name == LowPolyOceanTool.DefaultOceanName
                || Selection.activeGameObject.GetComponent<LowPolyOceanState>() != null;
            if (!looksLikeOcean)
            {
                return;
            }

            _ocean = Selection.activeGameObject;
            SyncFromScene();
            Repaint();
        }

        private void SyncFromScene()
        {
            if (_ocean == null)
            {
                _ocean = LowPolyOceanTool.FindOcean();
            }

            if (_ocean == null)
            {
                return;
            }

            _hasIslandSettings = LowPolyOceanTool.LoadIslandSettings() != null;
            LowPolyOceanState state = _ocean.GetComponent<LowPolyOceanState>();
            if (state != null)
            {
                _settings = LowPolyOceanTool.ReadSettings(state);
            }

            MeshRenderer renderer = _ocean.GetComponent<MeshRenderer>();
            if (renderer != null
                && LowPolyOceanTool.IsLowPolyMaterial(renderer.sharedMaterial)
                && renderer.sharedMaterial.HasProperty("_ShoreSmooth"))
            {
                _shoreSmooth = renderer.sharedMaterial.GetFloat("_ShoreSmooth");
            }
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("海面海岸拓扑", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "四个方案都会重建海面网格；切换前请先确认海面已经应用过低模材质。"
                + "场景物体上的改动支持 Ctrl+Z。\n"
                + "要可复用、不挖洞的方案，请用“屿见/场景/碰撞体海面（Shader 海岸）”。",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            _ocean = (GameObject)EditorGUILayout.ObjectField(
                "海面物体", _ocean, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                SyncFromScene();
            }

            if (_ocean == null)
            {
                EditorGUILayout.HelpBox(
                    "场景里找不到名为 Ocean 的海面物体。请先运行“屿见/场景/生成低模海岛底图”。",
                    MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            if (_ocean.GetComponent<MeshFilter>() == null
                || _ocean.GetComponent<MeshRenderer>() == null)
            {
                EditorGUILayout.HelpBox(
                    "这个物体缺少 MeshFilter 或 MeshRenderer，不能作为海面。",
                    MessageType.Error);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawCurrentState();
            DrawModeSelector();
            DrawParameters();
            DrawPreview();
            DrawActions();

            EditorGUILayout.EndScrollView();
        }

        private void DrawCurrentState()
        {
            LowPolyOceanState state = _ocean.GetComponent<LowPolyOceanState>();
            MeshFilter filter = _ocean.GetComponent<MeshFilter>();
            int triangles = filter != null && filter.sharedMesh != null
                ? (int)(filter.sharedMesh.GetIndexCount(0) / 3u)
                : 0;

            if (state != null)
            {
                EditorGUILayout.HelpBox(
                    "场景当前：" + LowPolyOceanTool.GetTopologyName(state.topology)
                    + "\n网格：" + triangles.ToString("N0") + " 个三角面",
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "场景当前：还没有应用低模海面。应用任一方案会同时生成网格和材质。",
                    MessageType.None);
            }
        }

        private void DrawModeSelector()
        {
            EditorGUI.BeginChangeCheck();
            _settings.topology = (LowPolyOceanTopology)EditorGUILayout.Popup(
                "海岸拓扑方案", (int)_settings.topology, ModeNames);
            if (EditorGUI.EndChangeCheck())
            {
                _shoreSmooth = _settings.topology == LowPolyOceanTopology.ShaderSmooth
                    ? 1f
                    : 0f;
            }

            EditorGUILayout.HelpBox(
                ModeDescriptions[(int)_settings.topology], MessageType.None);
        }

        private void DrawParameters()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("网格参数", EditorStyles.boldLabel);

            _settings.size = EditorGUILayout.Slider(
                "海面覆盖边长", _settings.size, 1200f, 2600f);
            _settings.jitter = EditorGUILayout.Slider(
                "顶点抖动", _settings.jitter, 0f, 0.45f);
            _settings.seed = EditorGUILayout.IntField("随机种子", _settings.seed);
            _settings.shoreFadeDepth = EditorGUILayout.Slider(
                "岸边浪高淡出深度", _settings.shoreFadeDepth, 0.2f, 10f);

            switch (_settings.topology)
            {
                case LowPolyOceanTopology.Radial:
                    _settings.radialSegments = EditorGUILayout.IntSlider(
                        "环向分段数", _settings.radialSegments, 64, 512);
                    _settings.shoreRingSpacing = EditorGUILayout.Slider(
                        "岸边环距", _settings.shoreRingSpacing, 1.5f, 6f);
                    _settings.shoreInset = EditorGUILayout.Slider(
                        "水边内缩（藏进沙滩）", _settings.shoreInset, 0f, 4f);
                    break;

                case LowPolyOceanTopology.RefinedGrid:
                    _settings.refineCellSize = EditorGUILayout.Slider(
                        "海岸细网格边长", _settings.refineCellSize, 1.5f, 6f);
                    _settings.refineHalfSize = EditorGUILayout.Slider(
                        "海岸细网格范围半径", _settings.refineHalfSize, 40f, 200f);
                    _settings.cellSize = EditorGUILayout.Slider(
                        "远海粗网格边长", _settings.cellSize, 6f, 24f);
                    _settings.coarseGrowth = EditorGUILayout.Slider(
                        "粗格放大倍率", _settings.coarseGrowth, 1.05f, 1.6f);
                    break;

                case LowPolyOceanTopology.Delaunay:
                    _settings.delaunayCellSize = EditorGUILayout.Slider(
                        "海岸散点边长", _settings.delaunayCellSize, 2f, 8f);
                    _settings.refineHalfSize = EditorGUILayout.Slider(
                        "海岸散点范围半径", _settings.refineHalfSize, 40f, 200f);
                    _settings.delaunayCoarseCellSize = EditorGUILayout.Slider(
                        "远海散点边长", _settings.delaunayCoarseCellSize, 20f, 120f);
                    _settings.coarseGrowth = EditorGUILayout.Slider(
                        "散点放大倍率", _settings.coarseGrowth, 1.05f, 1.6f);
                    break;

                default:
                    _settings.cellSize = EditorGUILayout.Slider(
                        "矩形网格边长", _settings.cellSize, 6f, 24f);
                    _shoreSmooth = EditorGUILayout.Slider(
                        "岸边柔化强度", _shoreSmooth, 0f, 1f);
                    EditorGUILayout.HelpBox(
                        "方案 D 不改拓扑，只加宽泡沫/浅水过渡并柔化裁剪边；"
                        + "规则三角形仍会保留。",
                        MessageType.None);
                    break;
            }

            if (!_hasIslandSettings)
            {
                EditorGUILayout.HelpBox(
                    "找不到 Assets/Data/Island/IslandSettings.asset。"
                    + "方案 A/B/C 的海岸遮罩和水线无法生成，请先运行“屿见/场景/生成低模海岛底图”。",
                    MessageType.Warning);
            }
        }

        private void DrawPreview()
        {
            long triangles = LowPolyOceanMeshGenerator.CountTriangles(_settings);
            string prefix = _settings.topology == LowPolyOceanTopology.Delaunay ? "约 " : "";
            EditorGUILayout.HelpBox(
                "预计 " + prefix + triangles.ToString("N0") + " 个三角面 / "
                + (triangles * 3L).ToString("N0") + " 个顶点（每面 3 个独立顶点）。",
                MessageType.None);
        }

        private void DrawActions()
        {
            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("应用当前方案", GUILayout.Height(34f)))
                {
                    ApplyCurrent(true);
                }

                if (GUILayout.Button("应用推荐（全水面）", GUILayout.Height(34f)))
                {
                    _settings = LowPolyOceanMeshSettings.Default;
                    _settings.topology = LowPolyOceanTopology.ShaderSmooth;
                    _settings.islandOffset = _ocean != null
                        ? LowPolyOceanTool.ComputeIslandOffset(
                            _ocean, LowPolyOceanTool.LoadIslandSettings())
                        : Vector2.zero;
                    _shoreSmooth = 0.5f;
                    ApplyCurrent(true);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("还原原海面"))
                {
                    RestoreOriginal();
                }

                if (GUILayout.Button("打开低模海面参数"))
                {
                    if (!EditorApplication.ExecuteMenuItem(LowPolyWindowMenuPath))
                    {
                        EditorUtility.DisplayDialog(
                            "找不到窗口",
                            "请手动打开“" + LowPolyWindowMenuPath + "”。",
                            "好");
                    }
                }
            }
        }

        private bool ValidateScene()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "请先退出 Play Mode", "海岸拓扑只能在编辑模式下生成。", "好");
                return false;
            }

            if (_ocean == null)
            {
                EditorUtility.DisplayDialog("没有海面", "请先指定海面物体。", "好");
                return false;
            }

            return true;
        }

        private void ApplyCurrent(bool recordUndo)
        {
            if (!ValidateScene())
            {
                return;
            }

            IslandSettings island = LowPolyOceanTool.LoadIslandSettings();
            if (island == null)
            {
                EditorUtility.DisplayDialog(
                    "缺少海岛参数",
                    "找不到 Assets/Data/Island/IslandSettings.asset，无法生成海岸拓扑。",
                    "好");
                return;
            }

            LowPolyOceanState state = _ocean.GetComponent<LowPolyOceanState>();
            MeshRenderer renderer = _ocean.GetComponent<MeshRenderer>();
            Material currentMaterial = renderer != null ? renderer.sharedMaterial : null;
            LowPolyOceanLook look = LowPolyOceanTool.IsLowPolyMaterial(currentMaterial)
                ? LowPolyOceanTool.ReadLook(currentMaterial)
                : LowPolyOceanLook.Default;

            _settings.waveHeight = look.waveHeight;
            _settings.islandOffset = LowPolyOceanTool.ComputeIslandOffset(_ocean, island);
            int preset = state != null ? state.preset : (int)LowPolyOceanPreset.RollingWaves;

            int undoGroup = BeginUndoGroup("切换海岸拓扑", recordUndo);
            EditorUtility.DisplayProgressBar(
                "海面海岸拓扑",
                "正在生成 " + ModeNames[(int)_settings.topology] + " ...",
                0.5f);

            Mesh mesh;
            try
            {
                mesh = LowPolyOceanTool.EnsureMeshAsset(_settings, island);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Material material = LowPolyOceanTool.EnsureMaterial(_settings);
            if (mesh == null || material == null)
            {
                EndUndoGroup(undoGroup, recordUndo);
                return;
            }

            LowPolyOceanTool.SetShoreSmooth(
                material,
                _settings.topology == LowPolyOceanTopology.ShaderSmooth ? _shoreSmooth : 0f,
                recordUndo);
            LowPolyOceanTool.ApplyToOcean(
                _ocean, mesh, material, _settings, preset, recordUndo);

            EditorSceneManager.MarkSceneDirty(_ocean.scene);
            AssetDatabase.SaveAssets();
            EndUndoGroup(undoGroup, recordUndo);
            SyncFromScene();
            SceneView.RepaintAll();

            Debug.Log("[屿见] 海岸拓扑已切换为 " + ModeNames[(int)_settings.topology]
                + "：" + mesh.vertexCount + " 顶点 / "
                + (mesh.GetIndexCount(0) / 3u).ToString("N0") + " 三角面。");
        }

        private void RestoreOriginal()
        {
            if (!ValidateScene())
            {
                return;
            }

            LowPolyOceanState state = _ocean.GetComponent<LowPolyOceanState>();
            bool hasState = state != null && state.originalCaptured;
            Mesh fallbackMesh = hasState ? null : LowPolyOceanTool.LoadBuiltinPlaneMesh();
            Material fallbackMaterial = hasState ? null : LowPolyOceanTool.LoadOriginalMaterial();

            int undoGroup = BeginUndoGroup("还原原海面", true);
            bool restored = LowPolyOceanTool.RestoreOriginal(
                _ocean, fallbackMesh, fallbackMaterial, true);
            if (!restored)
            {
                EndUndoGroup(undoGroup, true);
                EditorUtility.DisplayDialog(
                    "无法还原",
                    "找不到原始网格。请确认海面物体上有低模状态组件，或场景里存在内置 Plane。",
                    "好");
                return;
            }

            if (!hasState && fallbackMaterial != null)
            {
                LowPolyOceanTool.SetLookMaterial(_ocean, fallbackMaterial, true);
            }

            EditorSceneManager.MarkSceneDirty(_ocean.scene);
            AssetDatabase.SaveAssets();
            EndUndoGroup(undoGroup, true);
            SyncFromScene();
            SceneView.RepaintAll();
            Debug.Log("[屿见] 海面已还原为原始网格 / 材质 / 缩放。");
        }

        private static int BeginUndoGroup(string name, bool recordUndo)
        {
            if (!recordUndo)
            {
                return -1;
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return group;
        }

        private static void EndUndoGroup(int group, bool recordUndo)
        {
            if (recordUndo && group >= 0)
            {
                Undo.CollapseUndoOperations(group);
            }
        }
    }
}
