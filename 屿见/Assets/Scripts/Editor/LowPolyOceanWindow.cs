using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Island;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 低模海面编辑器：把三角网格生成、分面外观预设、应用和还原放进一个窗口。
    /// 场景物体上的所有修改都走 Undo 组；Restore 按钮会恢复首次应用前的网格 / 材质 / 缩放。
    /// </summary>
    public class LowPolyOceanWindow : EditorWindow
    {
        private const string MenuPath = "屿见/场景/低模海面（三角拓扑 / 还原）";
        private const string WaterWindowMenuPath = "屿见/场景/海面参数调节";

        private static readonly string[] PresetNames =
        {
            "纯平面色",
            "分面明暗",
            "起伏浪面（默认）",
            "分面 + 三角边线",
        };

        private static readonly string[] PresetDescriptions =
        {
            "只给每个三角面轻微色差，不加光照明暗。最接近现在的水面，但能看出三角分块。",
            "每个三角面有轻微色差，再叠一层固定方向光的明暗，块面感明显但仍然是水。",
            "在分面明暗基础上增加明显顶点起伏，海面有大块浪形，最像经典 lowpoly 海。",
            "分面明暗 + 每个三角面的轮廓线，风格最“网格感/低模”，装饰性最强。",
        };

        private GameObject _ocean;
        private Vector2 _scroll;

        private LowPolyOceanPreset _preset = LowPolyOceanPreset.RollingWaves;
        private LowPolyOceanLook _look = LowPolyOceanLook.Default;

        private bool _showLook = true;
        private bool _showMesh = true;
        private bool _hasIslandSettings;

        [MenuItem(MenuPath, false, 215)]
        private static void Open()
        {
            LowPolyOceanWindow window = GetWindow<LowPolyOceanWindow>("低模海面");
            window.minSize = new Vector2(420f, 560f);
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
                _preset = (LowPolyOceanPreset)Mathf.Clamp(state.preset, 0, PresetNames.Length - 1);
            }

            Material material = CurrentLowPolyMaterial();
            if (material != null)
            {
                _look = LowPolyOceanTool.ReadLook(material);
            }
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("低模海面（三角形拓扑）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "应用后海面会换成三角网格 + 低模水面材质。原海面的网格、材质、缩放会被记录，"
                + "随时可以用“还原原海面”一键恢复；场景物体上的改动都支持 Ctrl+Z。",
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
                    "场景里找不到名为 Ocean 的海面物体。请先运行“屿见/场景/生成低模海岛底图”，"
                    + "或把海面物体拖到上面的字段。",
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

            DrawStatus();

            EditorGUILayout.Space(4f);
            _showLook = EditorGUILayout.Foldout(_showLook, "表现预设与分面参数", true,
                EditorStyles.foldoutHeader);
            if (_showLook)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawLookSection();
                }
            }

            EditorGUILayout.Space(4f);
            _showMesh = EditorGUILayout.Foldout(_showMesh, "三角网格参数", true,
                EditorStyles.foldoutHeader);
            if (_showMesh)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawMeshSection();
                }
            }

            EditorGUILayout.Space(8f);
            DrawActions();

            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "注意：网格 / 材质资产是新建的，Ctrl+Z 不会删除它们；"
                + "但“还原原海面”按钮会把海面物体恢复成应用前的状态。",
                MessageType.None);

            EditorGUILayout.EndScrollView();
        }

        private void DrawStatus()
        {
            LowPolyOceanState state = _ocean.GetComponent<LowPolyOceanState>();
            MeshFilter filter = _ocean.GetComponent<MeshFilter>();
            MeshRenderer renderer = _ocean.GetComponent<MeshRenderer>();

            bool hasLowPolyMesh = state != null
                && state.lowPolyMesh != null
                && filter != null
                && filter.sharedMesh == state.lowPolyMesh;
            bool hasLowPolyLook = renderer != null
                && LowPolyOceanTool.IsLowPolyMaterial(renderer.sharedMaterial);

            string mode;
            MessageType type;
            if (hasLowPolyMesh && hasLowPolyLook)
            {
                mode = "当前：低模海面（三角拓扑 + 分面外观）";
                type = MessageType.Info;
            }
            else if (hasLowPolyMesh)
            {
                mode = "当前：三角拓扑 + 原版水面外观";
                type = MessageType.Info;
            }
            else if (state != null)
            {
                mode = "当前：未识别（场景里有低模状态组件，可点“还原原海面”恢复）";
                type = MessageType.Warning;
            }
            else
            {
                mode = "当前：原版海面";
                type = MessageType.None;
            }

            int triangleCount = filter != null && filter.sharedMesh != null
                ? (int)(filter.sharedMesh.GetIndexCount(0) / 3u)
                : 0;
            EditorGUILayout.HelpBox(
                mode + "\n网格：" + triangleCount.ToString("N0") + " 个三角面",
                type);
        }

        private void DrawLookSection()
        {
            int presetIndex = EditorGUILayout.Popup("表现预设", (int)_preset, PresetNames);
            if (presetIndex != (int)_preset)
            {
                _preset = (LowPolyOceanPreset)presetIndex;
                _look = LowPolyOceanLook.FromPreset(_preset);
                PushLookToMaterial(true);
                SceneView.RepaintAll();
            }

            EditorGUILayout.HelpBox(PresetDescriptions[(int)_preset], MessageType.None);

            if (GUILayout.Button("把当前预设的分面 / 去噪值写入材质（保留起伏设置）"))
            {
                // 只强制覆盖分面、去噪、透明度，不打断用户手调的起伏高度 / 速度 / 密度。
                LowPolyOceanLook preset = LowPolyOceanLook.FromPreset(_preset);
                preset.waveHeight = _look.waveHeight;
                preset.waveSpeed = _look.waveSpeed;
                preset.waveFrequency = _look.waveFrequency;
                _look = preset;
                PushLookToMaterial(true);
                SceneView.RepaintAll();
            }

            Material material = CurrentLowPolyMaterial();
            if (material == null)
            {
                EditorGUILayout.HelpBox(
                    "海面还没应用低模材质，下面的滑杆会在点击“应用 / 更新低模海面”时写入材质。",
                    MessageType.None);
            }

            DrawLookSlider("起伏高度", ref _look.waveHeight, 0f, 2f);
            DrawLookSlider("起伏速度", ref _look.waveSpeed, 0f, 2f);
            DrawLookSlider("浪的密度（越大越密）", ref _look.waveFrequency, 0.005f, 0.15f);
            DrawLookSlider("每面色差", ref _look.facetTint, 0f, 0.4f);
            DrawLookSlider("分面明暗", ref _look.facetShade, 0f, 1f);
            DrawLookSlider("三角边线强度", ref _look.edgeStrength, 0f, 1f);
            DrawLookSlider("三角边线宽度（像素）", ref _look.edgeWidth, 0.2f, 4f);
            DrawLookSlider("浅水不透明度", ref _look.shallowAlpha, 0f, 1f);
            DrawLookSlider("深水不透明度", ref _look.deepAlpha, 0f, 1f);
            DrawLookSlider("残留噪点（0 = 纯低模）", ref _look.noiseStrength, 0f, 1f);
            EditorGUILayout.HelpBox(
                "残留噪点是原海面的色斑 / 流线 / 碎光 / 浪线总开关，默认 0，"
                + "关掉后三角分面才看得清。",
                MessageType.None);
        }

        private void DrawMeshSection()
        {
            LowPolyOceanState state = _ocean.GetComponent<LowPolyOceanState>();
            LowPolyOceanMeshSettings settings = state != null
                ? LowPolyOceanTool.ReadSettings(state)
                : LowPolyOceanMeshSettings.Default;
            long triangles = LowPolyOceanMeshGenerator.CountTriangles(settings);

            EditorGUILayout.HelpBox(
                "当前海岸拓扑：" + LowPolyOceanTool.GetTopologyName(settings.topology)
                + "\n约 " + triangles.ToString("N0") + " 个三角面。"
                + "海岸拓扑和网格参数在独立窗口里修改。",
                MessageType.None);

            if (GUILayout.Button("打开碰撞体海面工具（推荐，可复用 / 无洞）"))
            {
                ColliderOceanWindow.OpenWindow();
            }

            if (GUILayout.Button("打开海岸拓扑工具（A/B/C/D 四种方案）"))
            {
                LowPolyOceanTopologyWindow.OpenWindow();
            }

            if (!_hasIslandSettings)
            {
                EditorGUILayout.HelpBox(
                    "找不到 Assets/Data/Island/IslandSettings.asset。旧的按海岛高度生成遮罩的方式不可用；"
                    + "请用“碰撞体海面”工具从 Collider 烘焙遮罩。",
                    MessageType.Warning);
            }
        }

        private void DrawLookSlider(string label, ref float value, float min, float max)
        {
            EditorGUI.BeginChangeCheck();
            float next = EditorGUILayout.Slider(label, value, min, max);
            if (EditorGUI.EndChangeCheck())
            {
                value = next;
                PushLookToMaterial(true);
                SceneView.RepaintAll();
            }
        }

        private void DrawActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("应用 / 更新低模海面", GUILayout.Height(34f)))
                {
                    ApplyLowPoly(true);
                }

                using (new EditorGUI.DisabledScope(!CanRestore()))
                {
                    if (GUILayout.Button("还原原海面", GUILayout.Height(34f)))
                    {
                        RestoreOriginal();
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(
                    _ocean.GetComponent<LowPolyOceanState>() == null))
                {
                    if (GUILayout.Button("关闭低模外观（保留三角网格）"))
                    {
                        DisableLowPolyLook();
                    }
                }

                if (GUILayout.Button("撤销上一步"))
                {
                    Undo.PerformUndo();
                    SyncFromScene();
                    SceneView.RepaintAll();
                }
            }

            if (GUILayout.Button("打开水面颜色 / 泡沫参数窗"))
            {
                Material material = CurrentLowPolyMaterial();
                if (material == null)
                {
                    material = LowPolyOceanTool.LoadOriginalMaterial();
                }

                if (material != null)
                {
                    Selection.activeObject = material;
                    EditorGUIUtility.PingObject(material);
                }

                if (!EditorApplication.ExecuteMenuItem(WaterWindowMenuPath))
                {
                    EditorUtility.DisplayDialog(
                        "找不到水面参数窗",
                        "请确认 " + WaterWindowMenuPath + " 菜单存在，或手动打开“屿见/场景/海面参数调节”。",
                        "好");
                }
            }
        }

        private bool CanRestore()
        {
            if (_ocean == null)
            {
                return false;
            }

            if (_ocean.GetComponent<LowPolyOceanState>() != null)
            {
                return true;
            }

            // 没有状态组件时，只要海面本身还有网格，就能用内置 Plane 兜底还原。
            MeshFilter filter = _ocean.GetComponent<MeshFilter>();
            return filter != null && filter.sharedMesh != null;
        }

        private Material CurrentLowPolyMaterial()
        {
            if (_ocean == null)
            {
                return null;
            }

            MeshRenderer renderer = _ocean.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                return null;
            }

            return LowPolyOceanTool.IsLowPolyMaterial(renderer.sharedMaterial)
                ? renderer.sharedMaterial
                : null;
        }

        private void PushLookToMaterial(bool recordUndo)
        {
            Material material = CurrentLowPolyMaterial();
            if (material != null)
            {
                LowPolyOceanTool.ApplyLook(material, _look, recordUndo);
            }
        }

        private bool ValidateScene()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("请先退出 Play Mode", "低模海面只能在编辑模式下生成。", "好");
                return false;
            }

            if (_ocean == null)
            {
                EditorUtility.DisplayDialog("没有海面", "请先指定海面物体。", "好");
                return false;
            }

            return true;
        }

        private void ApplyLowPoly(bool recordUndo)
        {
            if (!ValidateScene())
            {
                return;
            }

            int undoGroup = BeginUndoGroup("应用低模海面", recordUndo);

            LowPolyOceanState state = _ocean.GetComponent<LowPolyOceanState>();
            LowPolyOceanMeshSettings settings = state != null
                ? LowPolyOceanTool.ReadSettings(state)
                : LowPolyOceanMeshSettings.Default;
            IslandSettings island = LowPolyOceanTool.LoadIslandSettings();
            settings.waveHeight = _look.waveHeight;
            settings.islandOffset = LowPolyOceanTool.ComputeIslandOffset(_ocean, island);

            EditorUtility.DisplayProgressBar("低模海面", "正在重建海面网格 ...", 0.5f);
            Mesh mesh;
            try
            {
                mesh = LowPolyOceanTool.EnsureMeshAsset(settings, island);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Material material = LowPolyOceanTool.EnsureMaterial(settings);
            if (mesh == null || material == null)
            {
                EndUndoGroup(undoGroup, recordUndo);
                return;
            }

            LowPolyOceanTool.ApplyLook(material, _look, recordUndo);
            float shoreSmooth = 0f;
            if (settings.topology == LowPolyOceanTopology.ShaderSmooth)
            {
                shoreSmooth = material.HasProperty("_ShoreSmooth")
                    ? material.GetFloat("_ShoreSmooth")
                    : 1f;
            }
            LowPolyOceanTool.SetShoreSmooth(material, shoreSmooth, recordUndo);
            LowPolyOceanTool.ApplyToOcean(
                _ocean, mesh, material, settings, (int)_preset, recordUndo);

            EditorSceneManager.MarkSceneDirty(_ocean.scene);
            AssetDatabase.SaveAssets();
            EndUndoGroup(undoGroup, recordUndo);
            SyncFromScene();
            SceneView.RepaintAll();

            Debug.Log("[屿见] 低模海面已应用：" + mesh.vertexCount + " 顶点 / "
                + (mesh.GetIndexCount(0) / 3u).ToString("N0") + " 三角面，预设 "
                + PresetNames[(int)_preset] + "。");
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
                // 没有状态组件时，至少把材质恢复到原海面材质。
                LowPolyOceanTool.SetLookMaterial(_ocean, fallbackMaterial, true);
            }

            EditorSceneManager.MarkSceneDirty(_ocean.scene);
            AssetDatabase.SaveAssets();
            EndUndoGroup(undoGroup, true);
            SyncFromScene();
            SceneView.RepaintAll();
            Debug.Log("[屿见] 海面已还原为原始网格 / 材质 / 缩放。");
        }

        private void DisableLowPolyLook()
        {
            if (!ValidateScene())
            {
                return;
            }

            Material original = LowPolyOceanTool.LoadOriginalMaterial();
            if (original == null)
            {
                EditorUtility.DisplayDialog(
                    "找不到原海面材质",
                    "无法加载 " + LowPolyOceanTool.OriginalMaterialPath + "。",
                    "好");
                return;
            }

            int undoGroup = BeginUndoGroup("关闭低模外观", true);
            LowPolyOceanTool.SetLookMaterial(_ocean, original, true);
            EditorSceneManager.MarkSceneDirty(_ocean.scene);
            AssetDatabase.SaveAssets();
            EndUndoGroup(undoGroup, true);
            SyncFromScene();
            SceneView.RepaintAll();
            Debug.Log("[屿见] 已切回原海面材质，三角网格保留。");
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
