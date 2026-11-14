using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Island;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 碰撞体海面：海面保持一整块矩形网格，用 Shader + 从 Collider 烘焙的俯视遮罩
    /// 决定哪里裁掉水面、哪里衰减浪高。没有洞、不依赖海岛形状，换一个 Collider 地形也能复用。
    /// </summary>
    public class ColliderOceanWindow : EditorWindow
    {
        private const string MenuPath = "屿见/场景/碰撞体海面（Shader 海岸）";
        private const string LowPolyWindowMenuPath = "屿见/场景/低模海面（三角拓扑 / 还原）";

        private static readonly string[] ResolutionNames = { "128", "256", "512" };
        private static readonly int[] Resolutions = { 128, 256, 512 };

        private GameObject _ocean;
        private GameObject _terrainRoot;
        private Vector2 _scroll;

        private LowPolyOceanMeshSettings _settings = LowPolyOceanMeshSettings.Default;
        private float _waterLevel = -1.4f;
        private int _resolutionIndex = 1;
        private float _margin = ColliderShoreMaskBaker.DefaultMargin;
        private int _maskBlur = 4;
        private float _shoreSmooth = 0.5f;

        private bool _hasTerrainColliders;
        private Vector2 _previewCenter;
        private float _previewSize;
        private int _previewColliderCount;

        [MenuItem(MenuPath, false, 217)]
        public static void OpenWindow()
        {
            ColliderOceanWindow window = GetWindow<ColliderOceanWindow>("碰撞体海面");
            window.minSize = new Vector2(440f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            if (_ocean == null)
            {
                _ocean = LowPolyOceanTool.FindOcean();
            }
            if (_terrainRoot == null)
            {
                _terrainRoot = ColliderShoreMaskBaker.FindTerrainRoot();
            }

            _settings.topology = LowPolyOceanTopology.ShaderSmooth;
            SyncFromScene();
            Selection.selectionChanged += OnSelectionChanged;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                return;
            }

            if (selected.name == LowPolyOceanTool.DefaultOceanName
                || selected.GetComponent<LowPolyOceanState>() != null)
            {
                _ocean = selected;
                SyncFromScene();
                Repaint();
            }
        }

        private void SyncFromScene()
        {
            if (_ocean == null)
            {
                _ocean = LowPolyOceanTool.FindOcean();
            }
            if (_terrainRoot == null)
            {
                _terrainRoot = ColliderShoreMaskBaker.FindTerrainRoot();
            }

            if (_terrainRoot != null)
            {
                IslandDefinition definition = _terrainRoot.GetComponent<IslandDefinition>();
                if (definition != null)
                {
                    _waterLevel = definition.WaterLevel;
                }
                else if (_ocean != null)
                {
                    _waterLevel = _ocean.transform.position.y;
                }
            }

            RefreshTerrainInfo();

            if (_ocean != null)
            {
                LowPolyOceanState state = _ocean.GetComponent<LowPolyOceanState>();
                if (state != null)
                {
                    _settings = LowPolyOceanTool.ReadSettings(state);
                    if (_settings.topology != LowPolyOceanTopology.RefinedGrid)
                    {
                        ApplyRecommendedMeshDefaults();
                    }
                }
                else
                {
                    _settings = LowPolyOceanMeshSettings.Default;
                    ApplyRecommendedMeshDefaults();
                }

                // 整块渐变网格：海岸附近细、远海粗，没有洞；
                // 海岸遮挡仍然交给碰撞体烘焙的 Shader 遮罩。
                _settings.topology = LowPolyOceanTopology.RefinedGrid;

                MeshRenderer renderer = _ocean.GetComponent<MeshRenderer>();
                if (renderer != null
                    && LowPolyOceanTool.IsLowPolyMaterial(renderer.sharedMaterial)
                    && renderer.sharedMaterial.HasProperty("_ShoreSmooth"))
                {
                    _shoreSmooth = renderer.sharedMaterial.GetFloat("_ShoreSmooth");
                }
            }
        }

        private void ApplyRecommendedMeshDefaults()
        {
            _settings.refineCellSize = 3f;
            _settings.refineHalfSize = 120f;
            _settings.cellSize = 20f;
            _settings.coarseGrowth = 1.3f;
        }

        private void RefreshTerrainInfo()
        {
            List<Collider> colliders = ColliderShoreMaskBaker.CollectColliders(_terrainRoot);
            _previewColliderCount = colliders.Count;
            _hasTerrainColliders = colliders.Count > 0;
            if (_hasTerrainColliders)
            {
                Bounds bounds = ColliderShoreMaskBaker.ComputeBounds(colliders);
                _previewCenter = new Vector2(bounds.center.x, bounds.center.z);
                _previewSize = Mathf.Max(bounds.size.x, bounds.size.z)
                    + _margin * 2f;
            }
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("碰撞体海面（整块网格 + Shader 海岸）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "海面保持一整块矩形网格；遮罩从 Terrain Root 下的 Collider 向下投射烘焙，"
                + "运行时只采样一张贴图。没有洞，换成别的地形也能复用。",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            _ocean = (GameObject)EditorGUILayout.ObjectField(
                "海面物体", _ocean, typeof(GameObject), true);
            _terrainRoot = (GameObject)EditorGUILayout.ObjectField(
                "地形根物体（Terrain Root）", _terrainRoot, typeof(GameObject), true);
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

            DrawTerrainSection();
            DrawMaskSection();
            DrawMeshSection();
            DrawActions();

            EditorGUILayout.EndScrollView();
        }

        private void DrawTerrainSection()
        {
            if (_terrainRoot == null)
            {
                EditorGUILayout.HelpBox(
                    "没有指定地形根物体。请把带 Collider 的地形根拖进来，"
                    + "或先运行“屿见/场景/生成低模海岛底图”。",
                    MessageType.Warning);
                return;
            }

            if (!_hasTerrainColliders)
            {
                EditorGUILayout.HelpBox(
                    "地形根下面还没有 Collider。应用时会自动给根物体补一个 MeshCollider；"
                    + "如果地形网格在子物体上，请把 Collider 放到子物体或手动添加。",
                    MessageType.Warning);
            }

            _waterLevel = EditorGUILayout.FloatField("水面高度（世界 Y）", _waterLevel);
            EditorGUILayout.HelpBox(
                "Collider 数量：" + _previewColliderCount
                + (_hasTerrainColliders
                    ? "\n遮罩范围中心：" + _previewCenter
                      + "\n覆盖边长：" + _previewSize.ToString("0.0") + " 世界单位"
                    : ""),
                MessageType.None);
        }

        private void DrawMaskSection()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("碰撞体遮罩", EditorStyles.boldLabel);

            _resolutionIndex = EditorGUILayout.Popup(
                "遮罩分辨率", _resolutionIndex, ResolutionNames);
            EditorGUI.BeginChangeCheck();
            _margin = EditorGUILayout.Slider(
                "遮罩外扩（世界单位）", _margin, 0f, 80f);
            if (EditorGUI.EndChangeCheck())
            {
                RefreshTerrainInfo();
            }
            _maskBlur = EditorGUILayout.IntSlider(
                "遮罩平滑（像素）", _maskBlur, 0, 6);
            _settings.shoreFadeDepth = EditorGUILayout.Slider(
                "岸边浪高淡出深度", _settings.shoreFadeDepth, 0.2f, 10f);

            EditorGUILayout.HelpBox(
                "遮罩 R = 浪高衰减（深水 1 / 水线 0 / 陆地 0），G = 陆地遮罩；"
                + "Shader 用 G 裁掉岛上的水面。",
                MessageType.None);
        }

        private void DrawMeshSection()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("海面网格", EditorStyles.boldLabel);

            _settings.size = EditorGUILayout.Slider(
                "覆盖边长", _settings.size, 1200f, 2600f);
            _settings.refineCellSize = EditorGUILayout.Slider(
                "海岸细网格边长", _settings.refineCellSize, 1.5f, 8f);
            _settings.refineHalfSize = EditorGUILayout.Slider(
                "海岸细网格范围半径", _settings.refineHalfSize, 60f, 200f);
            _settings.cellSize = EditorGUILayout.Slider(
                "远海粗网格边长（越大越省性能）", _settings.cellSize, 8f, 32f);
            _settings.coarseGrowth = EditorGUILayout.Slider(
                "粗格放大倍率", _settings.coarseGrowth, 1.05f, 1.6f);
            _settings.jitter = EditorGUILayout.Slider(
                "顶点抖动", _settings.jitter, 0f, 0.45f);
            _settings.seed = EditorGUILayout.IntField("随机种子", _settings.seed);
            _shoreSmooth = EditorGUILayout.Slider(
                "岸边柔化（Shader）", _shoreSmooth, 0f, 1f);

            long triangles = LowPolyOceanMeshGenerator.CountTriangles(_settings);
            EditorGUILayout.HelpBox(
                "预计 " + triangles.ToString("N0") + " 个三角面 / "
                + (triangles * 3L).ToString("N0") + " 个顶点。"
                + "整块网格没有洞；海岸附近细、远海粗，遮罩由 Collider 烘焙。",
                MessageType.None);
        }

        private void DrawActions()
        {
            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("烘焙遮罩并应用", GUILayout.Height(34f)))
                {
                    BakeAndApply(true);
                }

                if (GUILayout.Button("只重新烘焙遮罩", GUILayout.Height(34f)))
                {
                    BakeOnly();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("还原原海面"))
                {
                    RestoreOriginal();
                }

                if (GUILayout.Button("打开低模外观"))
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

            EditorGUILayout.HelpBox(
                "遮罩贴图 / 海面网格是生成的资产，Ctrl+Z 不会删除它们；"
                + "“还原原海面”会恢复海面的网格、材质和缩放。",
                MessageType.None);
        }

        private bool ValidateScene()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "请先退出 Play Mode", "碰撞体海面只能在编辑模式下烘焙。", "好");
                return false;
            }

            if (_ocean == null)
            {
                EditorUtility.DisplayDialog("没有海面", "请先指定海面物体。", "好");
                return false;
            }

            if (_terrainRoot == null)
            {
                EditorUtility.DisplayDialog(
                    "没有地形根物体",
                    "请把带 Collider 的地形根拖到 Terrain Root 字段。",
                    "好");
                return false;
            }

            return true;
        }

        private void BakeAndApply(bool recordUndo)
        {
            if (!ValidateScene())
            {
                return;
            }

            int undoGroup = BeginUndoGroup("烘焙碰撞体海面", recordUndo);

            ColliderShoreMaskBaker.EnsureTerrainCollider(_terrainRoot, recordUndo);
            List<Collider> colliders = ColliderShoreMaskBaker.CollectColliders(_terrainRoot);
            if (colliders.Count == 0)
            {
                EndUndoGroup(undoGroup, recordUndo);
                EditorUtility.DisplayDialog(
                    "没有 Collider",
                    "地形根下面找不到可用的 Collider，无法烘焙遮罩。",
                    "好");
                return;
            }

            _settings.topology = LowPolyOceanTopology.RefinedGrid;
            _settings.islandOffset = Vector2.zero;

            MeshRenderer renderer = _ocean.GetComponent<MeshRenderer>();
            LowPolyOceanLook look = renderer != null
                && LowPolyOceanTool.IsLowPolyMaterial(renderer.sharedMaterial)
                ? LowPolyOceanTool.ReadLook(renderer.sharedMaterial)
                : LowPolyOceanLook.Default;
            _settings.waveHeight = look.waveHeight;

            EditorUtility.DisplayProgressBar(
                "碰撞体海面", "正在从 Collider 烘焙遮罩 ...", 0.3f);
            Vector2 center;
            float size;
            Texture2D mask;
            try
            {
                mask = ColliderShoreMaskBaker.BakeMaskAsset(
                    colliders,
                    _waterLevel,
                    _settings.shoreFadeDepth,
                    Resolutions[_resolutionIndex],
                    _margin,
                    _maskBlur,
                    out center,
                    out size);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (mask == null)
            {
                EndUndoGroup(undoGroup, recordUndo);
                return;
            }

            EditorUtility.DisplayProgressBar(
                "碰撞体海面", "正在重建整块海面网格 ...", 0.7f);
            Mesh mesh;
            try
            {
                mesh = LowPolyOceanTool.EnsureMeshAsset(_settings, null);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Material material = LowPolyOceanTool.EnsureMaterial(_settings, false);
            if (mesh == null || material == null)
            {
                EndUndoGroup(undoGroup, recordUndo);
                return;
            }

            LowPolyOceanTool.ConfigureShoreMask(material, mask, center, size);
            LowPolyOceanTool.SetShoreSmooth(material, _shoreSmooth, recordUndo);

            LowPolyOceanState state = _ocean.GetComponent<LowPolyOceanState>();
            int preset = state != null ? state.preset : (int)LowPolyOceanPreset.RollingWaves;
            LowPolyOceanTool.ApplyToOcean(
                _ocean, mesh, material, _settings, preset, recordUndo);

            _previewCenter = center;
            _previewSize = size;
            EditorSceneManager.MarkSceneDirty(_ocean.scene);
            AssetDatabase.SaveAssets();
            EndUndoGroup(undoGroup, recordUndo);
            SyncFromScene();
            SceneView.RepaintAll();

            Debug.Log("[屿见] 碰撞体海面已应用：遮罩 " + mask.width + "×" + mask.height
                + "，范围 " + size.ToString("0.0") + "，"
                + (mesh.GetIndexCount(0) / 3u).ToString("N0") + " 三角面。");
        }

        private void BakeOnly()
        {
            if (!ValidateScene())
            {
                return;
            }

            MeshRenderer renderer = _ocean.GetComponent<MeshRenderer>();
            if (renderer == null
                || !LowPolyOceanTool.IsLowPolyMaterial(renderer.sharedMaterial))
            {
                EditorUtility.DisplayDialog(
                    "海面还没应用",
                    "请先点“烘焙遮罩并应用”，之后才能只重新烘焙遮罩。",
                    "好");
                return;
            }

            int undoGroup = BeginUndoGroup("重新烘焙碰撞体遮罩", true);
            ColliderShoreMaskBaker.EnsureTerrainCollider(_terrainRoot, true);
            List<Collider> colliders = ColliderShoreMaskBaker.CollectColliders(_terrainRoot);
            if (colliders.Count == 0)
            {
                EndUndoGroup(undoGroup, true);
                EditorUtility.DisplayDialog(
                    "没有 Collider", "地形根下面找不到可用的 Collider。", "好");
                return;
            }

            EditorUtility.DisplayProgressBar(
                "碰撞体海面", "正在重新烘焙遮罩 ...", 0.5f);
            Vector2 center;
            float size;
            Texture2D mask;
            try
            {
                mask = ColliderShoreMaskBaker.BakeMaskAsset(
                    colliders,
                    _waterLevel,
                    _settings.shoreFadeDepth,
                    Resolutions[_resolutionIndex],
                    _margin,
                    _maskBlur,
                    out center,
                    out size);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (mask == null)
            {
                EndUndoGroup(undoGroup, true);
                return;
            }

            LowPolyOceanTool.ConfigureShoreMask(
                renderer.sharedMaterial, mask, center, size);
            LowPolyOceanTool.SetShoreSmooth(renderer.sharedMaterial, _shoreSmooth, true);
            EditorSceneManager.MarkSceneDirty(_ocean.scene);
            AssetDatabase.SaveAssets();
            EndUndoGroup(undoGroup, true);
            SyncFromScene();
            SceneView.RepaintAll();

            Debug.Log("[屿见] 碰撞体遮罩已重新烘焙：范围 " + size.ToString("0.0")
                + "，分辨率 " + mask.width + "×" + mask.height + "。");
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
