using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StylizedWater2;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 海面方案切换窗口。
    ///
    /// 原方案 = 屿见自研海面（场景里名为 Ocean 的物体，兼容 Ocean_Water.mat、
    ///          低模三角网格、海岸遮罩等现有工具）。
    /// 现方案 = Stylized Water 2 插件海面（独立物体，使用插件预制体 / 材质）。
    ///
    /// 两个海面同时留在场景里，切换只改“哪个物体处于启用状态”，
    /// 不删除网格 / 材质，原方案的低模状态也不会被破坏。
    /// </summary>
    public class OceanSchemeSwitchWindow : EditorWindow
    {
        private const string MenuPath = "屿见/场景/海面方案切换（原方案 / Stylized Water 2）";

        private const string OriginalOceanName = "Ocean";
        private const string StylizedOceanName = "Ocean_StylizedWater2";

        private const string LowPolyWindowMenuPath = "屿见/场景/低模海面（三角拓扑 / 还原）";
        private const string TopologyWindowMenuPath = "屿见/场景/海面海岸拓扑（4 种方案）";
        private const string WaterSettingsMenuPath = "屿见/场景/海面参数调节";
        private const string ColliderOceanMenuPath = "屿见/场景/碰撞体海面（Shader 海岸）";

        private const string Sw2PrefabFolder = "Assets/StylizedWater2/Prefabs/";
        private const string ProjectMaterialFolder = "Assets/Materials/Island";
        private const string ProjectMaterialName = "Ocean_StylizedWater2";

        private const string DefineSymbol = "URP_COMPATIBILITY_MODE";
        private const string CompatFieldName = "m_EnableRenderCompatibilityMode";
        private const string CompatUpgradeMethod = "SetCompatibilityModeFromUpgrade";

        private const string PresetPrefKey = "Yujian.OceanScheme.Preset";
        private const string SizePrefKey = "Yujian.OceanScheme.SurfaceSize";
        private const string LevelPrefKey = "Yujian.OceanScheme.WaterLevel";

        private const float DefaultSurfaceSize = 2000f;
        private const float DefaultWaterLevel = -1.4f;

        private sealed class Sw2Preset
        {
            public readonly string label;
            public readonly string prefabFile;
            public readonly string description;

            public Sw2Preset(string label, string prefabFile, string description)
            {
                this.label = label;
                this.prefabFile = prefabFile;
                this.description = description;
            }

            public string PrefabPath
            {
                get { return Sw2PrefabFolder + prefabFile; }
            }
        }

        private static readonly Sw2Preset[] Presets =
        {
            new Sw2Preset("Ocean（海洋）", "StylizedWater2_Ocean.prefab",
                "插件的默认海洋：完整海浪、折射、泡沫和反射，效果最丰富。"),
            new Sw2Preset("Lowpoly（低模分面）", "StylizedWater_Lowpoly.prefab",
                "低模平面网格 + 分面材质，和屿见现有的低模风格最接近。"),
            new Sw2Preset("Desktop（桌面高配）", "StylizedWater_Desktop.prefab",
                "桌面端高精度网格，适合作为近景海面。"),
            new Sw2Preset("Mobile（移动端）", "StylizedWater_Mobile.prefab",
                "面向移动端的轻量网格与材质。"),
            new Sw2Preset("Toon（卡通）", "StylizedWater_Toon.prefab",
                "卡通着色风格的水面。"),
        };

        private GameObject _originalOcean;
        private GameObject _stylizedOcean;

        private Vector2 _scroll;
        private bool _showPrerequisites = true;
        private bool _showStylized = true;
        private bool _showOriginal = true;

        private float _surfaceSize = DefaultSurfaceSize;
        private float _waterLevel = DefaultWaterLevel;
        private int _presetIndex;

        [MenuItem(MenuPath, false, 213)]
        private static void Open()
        {
            OceanSchemeSwitchWindow window = GetWindow<OceanSchemeSwitchWindow>("海面方案切换");
            window.minSize = new Vector2(460f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            _presetIndex = Mathf.Clamp(
                EditorPrefs.GetInt(PresetPrefKey, 0), 0, Presets.Length - 1);
            _surfaceSize = Mathf.Max(
                10f, EditorPrefs.GetFloat(SizePrefKey, DefaultSurfaceSize));
            _waterLevel = EditorPrefs.GetFloat(LevelPrefKey, DefaultWaterLevel);

            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            FindOceans();
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        }

        private void OnHierarchyChanged()
        {
            FindOceans();
            Repaint();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("海面方案切换", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "在「原方案（屿见自研海面）」和「现方案（Stylized Water 2）」之间切换。"
                + "两个海面会同时留在场景里，切换只改变哪个物体启用，不会删除网格或材质；"
                + "原方案的低模网格 / 海岸遮罩状态会原样保留。",
                MessageType.Info);

            FindOceansIfNeeded();
            DrawSchemeStatus();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox(
                    "Play 模式下的切换不会保存到场景，退出 Play 后会恢复为进入前的方案。",
                    MessageType.Warning);
            }

            DrawSwitchButtons();
            DrawObjectFields();

            EditorGUILayout.Space(6f);
            DrawPrerequisites();

            EditorGUILayout.Space(6f);
            DrawStylizedSection();

            EditorGUILayout.Space(6f);
            DrawOriginalSection();

            EditorGUILayout.Space(8f);
            DrawFooter();

            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------
        // 场景查找 / 状态
        // ------------------------------------------------------------------

        private void FindOceansIfNeeded()
        {
            if (_originalOcean == null || _stylizedOcean == null)
            {
                FindOceans();
            }
        }

        private void FindOceans()
        {
            if (_originalOcean == null)
            {
                _originalOcean = FindRootByName(OriginalOceanName);
            }

            if (_stylizedOcean == null)
            {
                _stylizedOcean = FindRootByName(StylizedOceanName);
                if (_stylizedOcean == null)
                {
                    _stylizedOcean = FindRootWithWaterObject();
                }
            }
        }

        private static GameObject FindRootByName(string name)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return null;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root != null && root.name == name)
                {
                    return root;
                }
            }

            return null;
        }

        private static GameObject FindRootWithWaterObject()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return null;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root != null && root.GetComponent<WaterObject>() != null)
                {
                    return root;
                }
            }

            return null;
        }

        private bool OriginalActive
        {
            get { return _originalOcean != null && _originalOcean.activeInHierarchy; }
        }

        private bool StylizedActive
        {
            get { return _stylizedOcean != null && _stylizedOcean.activeInHierarchy; }
        }

        private void DrawSchemeStatus()
        {
            string text;
            MessageType type;

            if (StylizedActive && OriginalActive)
            {
                text = "当前：两个海面同时启用，画面会重叠。请用下面的按钮二选一。";
                type = MessageType.Warning;
            }
            else if (StylizedActive)
            {
                text = "当前：现方案 — Stylized Water 2";
                type = MessageType.Info;
            }
            else if (OriginalActive)
            {
                text = "当前：原方案 — 屿见自研海面";
                type = MessageType.Info;
            }
            else
            {
                text = "当前：没有启用的海面物体。请先切换到其中一个方案。";
                type = MessageType.Error;
            }

            EditorGUILayout.HelpBox(text, type);
        }

        private void DrawSwitchButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(OriginalActive && !StylizedActive))
                {
                    if (GUILayout.Button("使用原方案", GUILayout.Height(34f)))
                    {
                        SwitchToOriginal();
                    }
                }

                using (new EditorGUI.DisabledScope(StylizedActive && !OriginalActive))
                {
                    if (GUILayout.Button("使用现方案（Stylized Water 2）", GUILayout.Height(34f)))
                    {
                        SwitchToStylized();
                    }
                }
            }
        }

        private void DrawObjectFields()
        {
            EditorGUI.BeginChangeCheck();
            _originalOcean = (GameObject)EditorGUILayout.ObjectField(
                "原方案海面", _originalOcean, typeof(GameObject), true);
            _stylizedOcean = (GameObject)EditorGUILayout.ObjectField(
                "现方案海面", _stylizedOcean, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                Repaint();
            }

            if (_originalOcean == null)
            {
                EditorGUILayout.HelpBox(
                    "场景里找不到名为 Ocean 的原方案海面。可以运行“屿见/场景/生成低模海岛底图”"
                    + "或手动把海面物体拖到上面的字段。",
                    MessageType.Warning);
            }
        }

        // ------------------------------------------------------------------
        // 前置条件
        // ------------------------------------------------------------------

        private void DrawPrerequisites()
        {
            _showPrerequisites = EditorGUILayout.Foldout(
                _showPrerequisites, "现方案前置条件（Stylized Water 2）", true,
                EditorStyles.foldoutHeader);

            if (!_showPrerequisites)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                UniversalRenderPipelineAsset urp = GetUrpAsset();

                DrawCheckRow(
                    "URP 渲染管线",
                    urp != null,
                    urp != null ? urp.name : "当前渲染管线不是 URP",
                    null, null);

                if (urp == null)
                {
                    EditorGUILayout.HelpBox(
                        "请先在 Project Settings > Graphics 里指定 URP 管线资产。",
                        MessageType.Error);
                    return;
                }

                DrawCheckRow(
                    "深度纹理（Depth Texture）",
                    urp.supportsCameraDepthTexture,
                    urp.supportsCameraDepthTexture ? "已开启" : "水下效果 / 岸线泡沫需要它",
                    EnsureUrpTextures,
                    "一键开启");

                DrawCheckRow(
                    "不透明纹理（Opaque Texture）",
                    urp.supportsCameraOpaqueTexture,
                    urp.supportsCameraOpaqueTexture ? "已开启" : "折射效果需要它",
                    EnsureUrpTextures,
                    "一键开启");

                bool defineCompiled = IsDefineCompiled();
                bool defineInSettings = HasDefineInSettings();
                DrawCheckRow(
                    "URP_COMPATIBILITY_MODE 宏",
                    defineCompiled,
                    defineCompiled
                        ? "已编译"
                        : defineInSettings
                            ? "已写入 Player Settings，等待 Unity 重新编译"
                            : "Unity 6.3 需要它才能启用兼容模式",
                    defineInSettings ? null : (Action)(() => AddDefineSymbol()),
                    "添加宏");

                bool compatKnown = TryReadCompatFlag(out bool compatEnabled);
                bool compatEffective = defineCompiled && compatKnown && compatEnabled;
                DrawCheckRow(
                    "兼容模式（Compatibility Mode）",
                    compatEffective,
                    !compatKnown
                        ? "找不到 RenderGraphSettings"
                        : compatEnabled && !defineCompiled
                            ? "开关已保存，但需要先添加宏并重新编译才会生效"
                            : compatEnabled
                                ? "已启用"
                                : "插件的自定义 Pass / 焦散 / 位移需要它",
                    compatEnabled ? null : (Action)(() => SetCompatFlag(true)),
                    "启用");

                List<ScriptableRendererData> renderers = GetRendererDataList(urp);
                int missing = renderers.Count(data => !HasSw2Feature(data));
                DrawCheckRow(
                    "Stylized Water 2 渲染特性",
                    missing == 0,
                    renderers.Count == 0
                        ? "找不到渲染器数据"
                        : missing == 0
                            ? "已安装到 " + renderers.Count + " 个渲染器"
                            : missing + " / " + renderers.Count + " 个渲染器缺少",
                    InstallSw2Features,
                    "一键安装");

                bool prefabExists = AssetDatabase.LoadAssetAtPath<GameObject>(
                    Presets[_presetIndex].PrefabPath) != null;
                DrawCheckRow(
                    "插件海面预制体",
                    prefabExists,
                    prefabExists ? Presets[_presetIndex].PrefabPath : "找不到预制体",
                    null, null);

                EditorGUILayout.Space(2f);
                if (GUILayout.Button("一键修复全部前置条件"))
                {
                    FixAllPrerequisites();
                }

                EditorGUILayout.HelpBox(
                    "兼容模式是 Unity 6+ 的弃用方案：Stylized Water 2 v1.6.9 不支持 Render Graph，"
                    + "只能通过兼容模式完整运行。长期建议升级到 Stylized Water 3，或把 Unity 降到 6000.2 及以下。",
                    MessageType.None);
            }
        }

        private static void DrawCheckRow(
            string label, bool ok, string detail, Action onFix, string fixLabel)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Color previous = GUI.color;
                GUI.color = ok
                    ? new Color(0.55f, 0.92f, 0.55f)
                    : new Color(1f, 0.62f, 0.45f);
                GUILayout.Label(ok ? "✓" : "✗", GUILayout.Width(18f));
                GUI.color = previous;

                EditorGUILayout.LabelField(
                    label + (string.IsNullOrEmpty(detail) ? string.Empty : "  —  " + detail));

                if (!ok && onFix != null)
                {
                    if (GUILayout.Button(fixLabel, GUILayout.Width(120f)))
                    {
                        onFix();
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // 现方案海面
        // ------------------------------------------------------------------

        private void DrawStylizedSection()
        {
            _showStylized = EditorGUILayout.Foldout(
                _showStylized, "现方案（Stylized Water 2）", true, EditorStyles.foldoutHeader);

            if (!_showStylized)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                int presetIndex = EditorGUILayout.Popup("海面预设", _presetIndex,
                    Presets.Select(p => p.label).ToArray());
                if (presetIndex != _presetIndex)
                {
                    _presetIndex = presetIndex;
                    EditorPrefs.SetInt(PresetPrefKey, _presetIndex);
                }

                EditorGUILayout.HelpBox(Presets[_presetIndex].description, MessageType.None);

                if (_stylizedOcean == null)
                {
                    EditorGUILayout.HelpBox(
                        "场景里还没有现方案海面。点击下面的按钮会按所选预设创建一个独立的海面物体，"
                        + "位置和水线高度对齐原方案海面。",
                        MessageType.None);

                    if (GUILayout.Button("创建现方案海面", GUILayout.Height(30f)))
                    {
                        _stylizedOcean = CreateStylizedOcean();
                        if (_stylizedOcean != null)
                        {
                            Selection.activeGameObject = _stylizedOcean;
                            MarkSceneDirty();
                        }

                        Repaint();
                    }

                    return;
                }

                MeshRenderer renderer = _stylizedOcean.GetComponent<MeshRenderer>();
                MeshFilter filter = _stylizedOcean.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                Material material = renderer != null ? renderer.sharedMaterial : null;

                EditorGUILayout.HelpBox(
                    "网格：" + (mesh != null ? mesh.name + "（" + Mathf.RoundToInt(mesh.bounds.size.x)
                        + " 单位）" : "缺失")
                    + "\n材质：" + (material != null ? material.name : "缺失")
                    + "\n物体启用状态：" + (_stylizedOcean.activeInHierarchy ? "启用" : "隐藏"),
                    MessageType.None);

                using (new EditorGUI.DisabledScope(renderer == null))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("选中材质"))
                        {
                            Selection.activeObject = material;
                            EditorGUIUtility.PingObject(material);
                        }

                        if (GUILayout.Button("复制材质到项目"))
                        {
                            CopyMaterialToProject();
                        }
                    }
                }

                EditorGUI.BeginChangeCheck();
                _surfaceSize = EditorGUILayout.FloatField("海面覆盖尺寸", _surfaceSize);
                _waterLevel = EditorGUILayout.FloatField("水线高度 Y", _waterLevel);
                if (EditorGUI.EndChangeCheck())
                {
                    _surfaceSize = Mathf.Max(10f, _surfaceSize);
                    EditorPrefs.SetFloat(SizePrefKey, _surfaceSize);
                    EditorPrefs.SetFloat(LevelPrefKey, _waterLevel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("应用位置与尺寸"))
                    {
                        ApplyStylizedTransform();
                    }

                    if (GUILayout.Button("从原方案读取水线 / 尺寸"))
                    {
                        ReadTransformFromOriginal();
                    }
                }

                EditorGUILayout.Space(2f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("套用所选预设（网格 + 材质）"))
                    {
                        ApplyPresetToExisting();
                    }

                    if (GUILayout.Button("删除现方案海面"))
                    {
                        DeleteStylizedOcean();
                    }
                }

                EditorGUILayout.HelpBox(
                    "提示：插件自带材质位于 Assets/StylizedWater2 下，"
                    + "“复制材质到项目”会生成 Assets/Materials/Island/Ocean_StylizedWater2*.mat，"
                    + "以后调参请改项目内的副本，避免插件更新时被覆盖。",
                    MessageType.None);
            }
        }

        private GameObject CreateStylizedOcean()
        {
            Sw2Preset preset = Presets[_presetIndex];
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(preset.PrefabPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog(
                    "找不到预制体",
                    "无法加载 " + preset.PrefabPath + "，请确认 Stylized Water 2 已完整导入。",
                    "好");
                return null;
            }

            Scene scene = SceneManager.GetActiveScene();
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null)
            {
                return null;
            }

            Undo.RegisterCreatedObjectUndo(instance, "创建 Stylized Water 2 海面");
            instance.name = StylizedOceanName;

            if (_originalOcean != null)
            {
                Vector3 position = _originalOcean.transform.position;
                position.y = _waterLevel;
                instance.transform.position = position;
                instance.transform.rotation = Quaternion.identity;
                instance.layer = _originalOcean.layer;
            }
            else
            {
                int waterLayer = LayerMask.NameToLayer("Water");
                if (waterLayer >= 0)
                {
                    instance.layer = waterLayer;
                }
            }

            ApplyScaleForSurfaceSize(instance);
            RefreshWaterObject(instance);

            EditorUtility.SetDirty(instance);
            MarkSceneDirty();
            return instance;
        }

        private void ApplyStylizedTransform()
        {
            if (_stylizedOcean == null)
            {
                return;
            }

            Undo.RecordObject(_stylizedOcean.transform, "调整 Stylized Water 2 海面位置");
            Vector3 position = _stylizedOcean.transform.position;
            if (_originalOcean != null)
            {
                position.x = _originalOcean.transform.position.x;
                position.z = _originalOcean.transform.position.z;
            }

            position.y = _waterLevel;
            _stylizedOcean.transform.position = position;

            ApplyScaleForSurfaceSize(_stylizedOcean);

            EditorUtility.SetDirty(_stylizedOcean);
            MarkSceneDirty();
            SceneView.RepaintAll();
        }

        private void ApplyScaleForSurfaceSize(GameObject ocean)
        {
            if (ocean == null)
            {
                return;
            }

            MeshFilter filter = ocean.GetComponent<MeshFilter>();
            float meshSize = filter != null && filter.sharedMesh != null
                ? Mathf.Max(filter.sharedMesh.bounds.size.x, 1f)
                : 100f;

            float scale = Mathf.Max(_surfaceSize, 10f) / meshSize;
            ocean.transform.localScale = new Vector3(scale, ocean.transform.localScale.y, scale);
        }

        private void ReadTransformFromOriginal()
        {
            if (_originalOcean == null)
            {
                return;
            }

            _waterLevel = _originalOcean.transform.position.y;

            MeshFilter filter = _originalOcean.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                Vector3 scale = _originalOcean.transform.lossyScale;
                float size = Mathf.Max(
                    filter.sharedMesh.bounds.size.x * Mathf.Abs(scale.x),
                    filter.sharedMesh.bounds.size.z * Mathf.Abs(scale.z));
                _surfaceSize = Mathf.Max(10f, size);
            }

            EditorPrefs.SetFloat(SizePrefKey, _surfaceSize);
            EditorPrefs.SetFloat(LevelPrefKey, _waterLevel);
            Repaint();
        }

        private void ApplyPresetToExisting()
        {
            if (_stylizedOcean == null)
            {
                return;
            }

            Sw2Preset preset = Presets[_presetIndex];
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(preset.PrefabPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog(
                    "找不到预制体",
                    "无法加载 " + preset.PrefabPath + "，请确认 Stylized Water 2 已完整导入。",
                    "好");
                return;
            }

            MeshFilter sourceFilter = prefab.GetComponent<MeshFilter>();
            MeshRenderer sourceRenderer = prefab.GetComponent<MeshRenderer>();
            MeshFilter targetFilter = _stylizedOcean.GetComponent<MeshFilter>();
            MeshRenderer targetRenderer = _stylizedOcean.GetComponent<MeshRenderer>();

            if (targetFilter != null && sourceFilter != null)
            {
                Undo.RecordObject(targetFilter, "套用 Stylized Water 2 预设");
                targetFilter.sharedMesh = sourceFilter.sharedMesh;
                EditorUtility.SetDirty(targetFilter);
                RecordPrefabModifications(targetFilter);
            }

            if (targetRenderer != null && sourceRenderer != null)
            {
                Undo.RecordObject(targetRenderer, "套用 Stylized Water 2 预设");
                targetRenderer.sharedMaterial = sourceRenderer.sharedMaterial;
                EditorUtility.SetDirty(targetRenderer);
                RecordPrefabModifications(targetRenderer);
            }

            RefreshWaterObject(_stylizedOcean);
            ApplyScaleForSurfaceSize(_stylizedOcean);
            MarkSceneDirty();
            SceneView.RepaintAll();
        }

        private static void RefreshWaterObject(GameObject ocean)
        {
            if (ocean == null)
            {
                return;
            }

            WaterObject water = ocean.GetComponent<WaterObject>();
            if (water != null)
            {
                water.FetchWaterMaterial();
                EditorUtility.SetDirty(water);
                RecordPrefabModifications(water);
            }
        }

        /// <summary>
        /// 修改预制体实例上的组件后，显式记录覆盖，确保保存场景时不会丢失。
        /// </summary>
        private static void RecordPrefabModifications(UnityEngine.Object target)
        {
            if (target != null && PrefabUtility.IsPartOfPrefabInstance(target))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            }
        }

        private void CopyMaterialToProject()
        {
            if (_stylizedOcean == null)
            {
                return;
            }

            MeshRenderer renderer = _stylizedOcean.GetComponent<MeshRenderer>();
            if (renderer == null || renderer.sharedMaterial == null)
            {
                return;
            }

            string sourcePath = AssetDatabase.GetAssetPath(renderer.sharedMaterial);
            if (string.IsNullOrEmpty(sourcePath))
            {
                EditorUtility.DisplayDialog(
                    "无法复制材质",
                    "当前材质的源文件不在 Assets 目录下，无法复制。",
                    "好");
                return;
            }

            EnsureFolder(ProjectMaterialFolder);

            string targetPath = AssetDatabase.GenerateUniqueAssetPath(
                ProjectMaterialFolder + "/" + ProjectMaterialName + ".mat");
            if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
            {
                EditorUtility.DisplayDialog(
                    "复制失败",
                    "无法把材质复制到 " + targetPath + "。",
                    "好");
                return;
            }

            Material copy = AssetDatabase.LoadAssetAtPath<Material>(targetPath);
            Undo.RecordObject(renderer, "更换 Stylized Water 2 材质");
            renderer.sharedMaterial = copy;
            EditorUtility.SetDirty(renderer);
            RecordPrefabModifications(renderer);
            RefreshWaterObject(_stylizedOcean);

            Selection.activeObject = copy;
            EditorGUIUtility.PingObject(copy);
        }

        private void DeleteStylizedOcean()
        {
            if (_stylizedOcean == null)
            {
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "删除现方案海面",
                "将删除场景里的 " + _stylizedOcean.name + "。原方案海面不受影响，"
                + "插件材质 / 预制体资产也不会被删除。",
                "删除",
                "取消");

            if (!confirmed)
            {
                return;
            }

            Undo.DestroyObjectImmediate(_stylizedOcean);
            _stylizedOcean = null;
            MarkSceneDirty();
            Repaint();
        }

        // ------------------------------------------------------------------
        // 原方案
        // ------------------------------------------------------------------

        private void DrawOriginalSection()
        {
            _showOriginal = EditorGUILayout.Foldout(
                _showOriginal, "原方案（屿见自研海面）", true, EditorStyles.foldoutHeader);

            if (!_showOriginal)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                if (_originalOcean == null)
                {
                    EditorGUILayout.HelpBox(
                        "找不到原方案海面。原方案工具都依赖场景里名为 Ocean 的物体。",
                        MessageType.Warning);
                }
                else
                {
                    MeshRenderer renderer = _originalOcean.GetComponent<MeshRenderer>();
                    Material material = renderer != null ? renderer.sharedMaterial : null;
                    bool lowPoly = LowPolyOceanTool.IsLowPolyMaterial(material);

                    string meshInfo = "缺失";
                    MeshFilter filter = _originalOcean.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null)
                    {
                        meshInfo = filter.sharedMesh.name + "（"
                            + (filter.sharedMesh.GetIndexCount(0) / 3u).ToString("N0")
                            + " 三角面）";
                    }

                    EditorGUILayout.HelpBox(
                        "物体启用状态：" + (_originalOcean.activeInHierarchy ? "启用" : "隐藏")
                        + "\n网格：" + meshInfo
                        + "\n材质：" + (material != null ? material.name : "缺失")
                        + "\n低模状态：" + (lowPoly ? "是（三角拓扑 + 低模材质）" : "否"),
                        MessageType.None);
                }

                if (GUILayout.Button("打开低模海面（三角拓扑 / 还原）"))
                {
                    EditorApplication.ExecuteMenuItem(LowPolyWindowMenuPath);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("海岸拓扑（4 种方案）"))
                    {
                        EditorApplication.ExecuteMenuItem(TopologyWindowMenuPath);
                    }

                    if (GUILayout.Button("碰撞体海面"))
                    {
                        EditorApplication.ExecuteMenuItem(ColliderOceanMenuPath);
                    }
                }

                if (GUILayout.Button("海面参数调节"))
                {
                    EditorApplication.ExecuteMenuItem(WaterSettingsMenuPath);
                }

                EditorGUILayout.HelpBox(
                    "原方案被隐藏时，上面的工具仍然可以正常编辑它（它们按名字查找 Ocean）。"
                    + "切回原方案后即可看到改动。",
                    MessageType.None);
            }
        }

        // ------------------------------------------------------------------
        // 切换
        // ------------------------------------------------------------------

        private void SwitchToOriginal()
        {
            FindOceans();
            if (_originalOcean == null)
            {
                EditorUtility.DisplayDialog(
                    "找不到原方案海面",
                    "场景里找不到名为 Ocean 的海面物体。请先把原方案海面找回来。",
                    "好");
                return;
            }

            SetActive(_stylizedOcean, false);
            SetActive(_originalOcean, true);

            MarkSceneDirty();
            Selection.activeGameObject = _originalOcean;
            SceneView.RepaintAll();
            Repaint();
        }

        private void SwitchToStylized()
        {
            FindOceans();

            UniversalRenderPipelineAsset urp = GetUrpAsset();
            if (urp == null)
            {
                EditorUtility.DisplayDialog(
                    "当前不是 URP",
                    "Stylized Water 2 只支持 Universal Render Pipeline。"
                    + "请先在 Project Settings > Graphics 里指定 URP 管线资产。",
                    "好");
                return;
            }

            int addedFeatures = 0;
            bool texturesChanged = false;
            bool defineAdded = false;
            bool defineAlreadyCompiled = IsDefineCompiled();

            // 1) 兼容模式开关（通过反射写入；宏编译后才会真正生效）。
            if (!TryReadCompatFlag(out bool compatEnabled) || !compatEnabled)
            {
                SetCompatFlag(true);
            }

            // 2) 深度 / 不透明纹理。
            if (!urp.supportsCameraDepthTexture || !urp.supportsCameraOpaqueTexture)
            {
                EnsureUrpTextures();
                texturesChanged = true;
            }

            // 3) 渲染特性。
            foreach (ScriptableRendererData data in GetRendererDataList(urp))
            {
                if (!HasSw2Feature(data) && AddSw2Feature(data))
                {
                    addedFeatures++;
                }
            }

            // 4) 海面物体。
            FindOceans();
            if (_stylizedOcean == null)
            {
                _stylizedOcean = CreateStylizedOcean();
            }

            if (_stylizedOcean == null)
            {
                return;
            }

            SetActive(_stylizedOcean, true);
            SetActive(_originalOcean, false);
            MarkSceneDirty();
            Selection.activeGameObject = _stylizedOcean;
            SceneView.RepaintAll();

            // 5) 宏最后加，写入后 Unity 会重新编译。
            if (!defineAlreadyCompiled && !HasDefineInSettings())
            {
                defineAdded = AddDefineSymbol();
            }

            string message = "已切换到 Stylized Water 2 海面。";
            if (addedFeatures > 0)
            {
                message += "\n已为 " + addedFeatures + " 个渲染器安装 Stylized Water 2 渲染特性。";
            }

            if (texturesChanged)
            {
                message += "\n已开启 URP 深度 / 不透明纹理。";
            }

            if (defineAdded)
            {
                message += "\n\n已添加 URP_COMPATIBILITY_MODE 宏，Unity 正在重新编译脚本。"
                    + "编译完成后请再点一次“使用现方案”，确保兼容模式生效。";
            }
            else if (!defineAlreadyCompiled)
            {
                message += "\n\n注意：缺少 URP_COMPATIBILITY_MODE 宏，兼容模式暂时不可用。"
                    + "请点上方前置条件里的“添加宏”。";
            }

            message += "\n\n记得 Ctrl+S 保存场景。";

            EditorUtility.DisplayDialog("海面方案切换", message, "好");
            Repaint();
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go == null || go.activeSelf == active)
            {
                return;
            }

            Undo.RecordObject(go, active ? "启用海面" : "隐藏海面");
            go.SetActive(active);
            EditorUtility.SetDirty(go);
        }

        private void MarkSceneDirty()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        // ------------------------------------------------------------------
        // URP / 插件辅助
        // ------------------------------------------------------------------

        private static UniversalRenderPipelineAsset GetUrpAsset()
        {
            return GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        }

        private static List<ScriptableRendererData> GetRendererDataList(
            UniversalRenderPipelineAsset asset)
        {
            List<ScriptableRendererData> result = new List<ScriptableRendererData>();
            if (asset == null)
            {
                return result;
            }

            ReadOnlySpan<ScriptableRendererData> renderers = asset.rendererDataList;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    result.Add(renderers[i]);
                }
            }

            return result;
        }

        private static bool HasSw2Feature(ScriptableRendererData data)
        {
            if (data == null || data.rendererFeatures == null)
            {
                return false;
            }

            for (int i = 0; i < data.rendererFeatures.Count; i++)
            {
                if (data.rendererFeatures[i] is StylizedWaterRenderFeature)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AddSw2Feature(ScriptableRendererData data)
        {
            if (data == null)
            {
                return false;
            }

            if (HasSw2Feature(data))
            {
                return true;
            }

            SerializedObject serializedData = new SerializedObject(data);
            serializedData.Update();

            SerializedProperty features = serializedData.FindProperty("m_RendererFeatures");
            SerializedProperty featureMap = serializedData.FindProperty("m_RendererFeatureMap");
            if (features == null || featureMap == null)
            {
                Debug.LogError("[屿见] 无法访问 " + data.name + " 的渲染特性列表。");
                return false;
            }

            StylizedWaterRenderFeature feature =
                CreateInstance<StylizedWaterRenderFeature>();
            feature.name = "Stylized Water 2";
            Undo.RegisterCreatedObjectUndo(feature, "添加 Stylized Water 2 渲染特性");

            if (EditorUtility.IsPersistent(data))
            {
                AssetDatabase.AddObjectToAsset(feature, data);
            }

            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;

            featureMap.arraySize++;
            featureMap.GetArrayElementAtIndex(featureMap.arraySize - 1).longValue = localId;

            serializedData.ApplyModifiedProperties();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return true;
        }

        private void InstallSw2Features()
        {
            UniversalRenderPipelineAsset urp = GetUrpAsset();
            if (urp == null)
            {
                return;
            }

            foreach (ScriptableRendererData data in GetRendererDataList(urp))
            {
                AddSw2Feature(data);
            }

            AssetDatabase.SaveAssets();
            Repaint();
        }

        private void EnsureUrpTextures()
        {
            UniversalRenderPipelineAsset urp = GetUrpAsset();
            if (urp == null)
            {
                return;
            }

            Undo.RecordObject(urp, "开启 URP 深度 / 不透明纹理");
            urp.supportsCameraDepthTexture = true;
            urp.supportsCameraOpaqueTexture = true;
            EditorUtility.SetDirty(urp);
            AssetDatabase.SaveAssets();
            Repaint();
        }

        private void FixAllPrerequisites()
        {
            UniversalRenderPipelineAsset urp = GetUrpAsset();
            if (urp == null)
            {
                EditorUtility.DisplayDialog(
                    "当前不是 URP",
                    "请先在 Project Settings > Graphics 里指定 URP 管线资产。",
                    "好");
                return;
            }

            if (!urp.supportsCameraDepthTexture || !urp.supportsCameraOpaqueTexture)
            {
                EnsureUrpTextures();
            }

            if (!TryReadCompatFlag(out bool compatEnabled) || !compatEnabled)
            {
                SetCompatFlag(true);
            }

            foreach (ScriptableRendererData data in GetRendererDataList(urp))
            {
                AddSw2Feature(data);
            }

            if (!IsDefineCompiled() && !HasDefineInSettings())
            {
                AddDefineSymbol();
            }

            AssetDatabase.SaveAssets();
            Repaint();
        }

        // ------------------------------------------------------------------
        // 兼容模式 / 宏 / 渲染图设置
        // ------------------------------------------------------------------

        private static bool IsDefineCompiled()
        {
#if URP_COMPATIBILITY_MODE
            return true;
#else
            return false;
#endif
        }

        private static NamedBuildTarget GetActiveNamedBuildTarget()
        {
            BuildTargetGroup group =
                BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            return NamedBuildTarget.FromBuildTargetGroup(group);
        }

        private static bool HasDefineInSettings()
        {
            try
            {
                string defines = PlayerSettings.GetScriptingDefineSymbols(
                    GetActiveNamedBuildTarget());
                return defines
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(symbol => symbol.Trim() == DefineSymbol);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[屿见] 读取 Scripting Define Symbols 失败：" + exception.Message);
                return false;
            }
        }

        private static bool AddDefineSymbol()
        {
            if (HasDefineInSettings())
            {
                return false;
            }

            try
            {
                NamedBuildTarget target = GetActiveNamedBuildTarget();
                string defines = PlayerSettings.GetScriptingDefineSymbols(target);
                List<string> list = defines
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(symbol => symbol.Trim())
                    .Where(symbol => symbol.Length > 0)
                    .ToList();

                list.Add(DefineSymbol);
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", list));
                AssetDatabase.SaveAssets();

                Debug.Log("[屿见] 已为 " + target + " 添加宏 " + DefineSymbol
                    + "，等待 Unity 重新编译。");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[屿见] 添加宏 " + DefineSymbol + " 失败：" + exception.Message);
                return false;
            }
        }

        private static RenderGraphSettings GetRenderGraphSettings()
        {
            return GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>();
        }

        private static bool TryReadCompatFlag(out bool enabled)
        {
            enabled = false;

            RenderGraphSettings settings = GetRenderGraphSettings();
            if (settings == null)
            {
                return false;
            }

            // 没有 URP_COMPATIBILITY_MODE 宏时，getter 固定返回 false，
            // 所以直接读序列化字段才能知道真实保存值。
            FieldInfo field = typeof(RenderGraphSettings).GetField(
                CompatFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(bool))
            {
                return false;
            }

            enabled = (bool)field.GetValue(settings);
            return true;
        }

        private static bool SetCompatFlag(bool enabled)
        {
            RenderGraphSettings settings = GetRenderGraphSettings();
            if (settings == null)
            {
                Debug.LogWarning("[屿见] 找不到 RenderGraphSettings，项目可能没有使用 URP。");
                return false;
            }

            try
            {
                // 始终存在（不受宏影响），直接写序列化字段。
                MethodInfo upgradeMethod = typeof(RenderGraphSettings).GetMethod(
                    CompatUpgradeMethod, BindingFlags.Instance | BindingFlags.NonPublic);
                if (upgradeMethod != null)
                {
                    upgradeMethod.Invoke(settings, new object[] { enabled });
                }
                else
                {
                    FieldInfo field = typeof(RenderGraphSettings).GetField(
                        CompatFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                    if (field == null)
                    {
                        Debug.LogWarning("[屿见] 找不到 " + CompatFieldName + " 字段。");
                        return false;
                    }

                    field.SetValue(settings, enabled);
                }

                // 宏已编译时走真正的 setter，让 URP 收到通知并重建管线。
                PropertyInfo property = typeof(RenderGraphSettings).GetProperty(
                    "enableRenderCompatibilityMode", BindingFlags.Instance | BindingFlags.Public);
                if (property != null)
                {
                    try
                    {
                        property.SetValue(settings, enabled);
                    }
                    catch (Exception)
                    {
                        // 没有宏时 setter 是空实现，忽略即可。
                    }
                }

                // RenderGraphSettings 是普通序列化类，不是 UnityEngine.Object；
                // 需要把承载它的 URP Global Settings 资产标脏才能落盘。
                RenderPipelineGlobalSettings globalSettings =
                    GraphicsSettings.GetSettingsForRenderPipeline<UniversalRenderPipeline>();
                if (globalSettings != null)
                {
                    EditorUtility.SetDirty(globalSettings);
                }

                AssetDatabase.SaveAssets();
                return true;
            }
            catch (Exception exception)
            {
                Exception inner = exception.InnerException ?? exception;
                Debug.LogWarning("[屿见] 设置兼容模式失败：" + inner.Message);
                return false;
            }
        }

        // ------------------------------------------------------------------
        // 其他
        // ------------------------------------------------------------------

        private void DrawFooter()
        {
            EditorGUILayout.LabelField(
                "场景改动需要保存后才会在下次打开场景时保持。", EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("保存当前场景", GUILayout.Height(26f)))
                {
                    Scene scene = SceneManager.GetActiveScene();
                    if (scene.IsValid())
                    {
                        EditorSceneManager.SaveScene(scene);
                    }
                }

                if (GUILayout.Button("刷新场景状态", GUILayout.Height(26f)))
                {
                    _originalOcean = null;
                    _stylizedOcean = null;
                    FindOceans();
                    Repaint();
                }
            }
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(folder);
            string name = System.IO.Path.GetFileName(folder);
            if (!string.IsNullOrEmpty(parent))
            {
                EnsureFolder(parent.Replace('\\', '/'));
            }

            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
