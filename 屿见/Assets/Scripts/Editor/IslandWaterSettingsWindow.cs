using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 海面参数总控窗口：把 Ocean_Water 材质上的所有 shader 参数集中到一个窗口里，
    /// 拖动滑块即可在 Scene/Game 视图实时看到变化，不用来回切到材质 Inspector。
    /// </summary>
    public class IslandWaterSettingsWindow : EditorWindow
    {
        private const string MenuPath = "屿见/海面参数调节";
        private const string DefaultMaterialPath = "Assets/Materials/Island/Ocean_Water.mat";
        private const string WaterShaderName = "Yujian/Island Water";

        private Material _material;
        private Vector2 _scroll;

        private bool _showColors = true;
        private bool _showDepth = true;
        private bool _showShore = true;
        private bool _showWaves = true;
        private bool _showPatches = true;
        private bool _showStreaks = true;
        private bool _showSparkles = true;
        private bool _showTide = true;

        private static readonly Dictionary<string, float> RecommendedFloats =
            new Dictionary<string, float>
            {
                { "_DepthRange", 3f },
                { "_ShallowAlpha", 0.35f },
                { "_DeepAlpha", 0.92f },

                { "_ShoreFoamDistance", 0.30f },
                { "_FoamStrength", 0.85f },
                { "_FoamNoiseScale", 1.2f },
                { "_FoamPulseSpeed", 1.1f },

                { "_WaveScale", 0.08f },
                { "_WaveSpeed", 0.15f },
                { "_WaveStrength", 0.20f },
                { "_WaveStartDepth", 0.4f },

                { "_PatchScale", 0.035f },
                { "_PatchSpeed", 0.06f },
                { "_PatchStrength", 0.35f },

                { "_StreakScale", 0.015f },
                { "_StreakSpeed", 0.06f },
                { "_StreakWarp", 0.8f },
                { "_StreakWidth", 0.06f },
                { "_StreakStrength", 0.15f },

                { "_SparkleScale", 1.4f },
                { "_SparkleSpeed", 0.08f },
                { "_SparkleThreshold", 0.58f },
                { "_SparkleStrength", 0.35f },

                { "_TideAmplitude", 0.18f },
                { "_TideSpeed", 0.5f },
                { "_TideSpatialScale", 0.02f },
                { "_SurfZoneDepth", 1.2f },
                { "_SurfWaveRepeat", 3f },
                { "_SurfWaveSpeed", 0.35f },
                { "_SurfWaveWidth", 0.08f },
                { "_SurfWaveStrength", 0.6f },
            };

        private static readonly Dictionary<string, Color> RecommendedColors =
            new Dictionary<string, Color>
            {
                { "_ShallowColor", new Color(0.45f, 0.85f, 0.85f, 1f) },
                { "_DeepColor", new Color(0.12f, 0.42f, 0.55f, 1f) },
                { "_FoamColor", new Color(0.94f, 0.98f, 1f, 1f) },
                { "_PatchColor", new Color(0.28f, 0.70f, 0.72f, 1f) },
            };

        [MenuItem(MenuPath)]
        private static void Open()
        {
            IslandWaterSettingsWindow window = GetWindow<IslandWaterSettingsWindow>("海面参数");
            window.minSize = new Vector2(380f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            if (_material == null)
            {
                _material = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
            }

            Selection.selectionChanged += OnSelectionChanged;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            if (Selection.activeObject is Material selected && IsWaterMaterial(selected))
            {
                _material = selected;
                Repaint();
            }
        }

        private static bool IsWaterMaterial(Material material)
        {
            return material != null && material.shader != null && material.shader.name == WaterShaderName;
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.Space(4f);
            EditorGUI.BeginChangeCheck();
            _material = (Material)EditorGUILayout.ObjectField(
                "水面材质", _material, typeof(Material), false);
            EditorGUI.EndChangeCheck();

            if (_material == null)
            {
                EditorGUILayout.HelpBox(
                    "未找到海面材质。请先运行“屿见/生成低模海岛底图”，或手动拖入使用 Yujian/Island Water 的材质。",
                    MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            if (!IsWaterMaterial(_material))
            {
                EditorGUILayout.HelpBox(
                    "这个材质没有使用 Yujian/Island Water，参数不会生效。",
                    MessageType.Error);
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.HelpBox(
                "拖动参数即可实时预览。Scene 视图右上角建议保持 Animated Materials（动画材质）开启，才能看到随时间流动的效果。",
                MessageType.Info);

            DrawSection(ref _showColors, "水体颜色", () =>
            {
                DrawColor("_ShallowColor", "浅水颜色");
                DrawColor("_DeepColor", "深水颜色");
                DrawColor("_FoamColor", "泡沫颜色");
            });

            DrawSection(ref _showDepth, "深度与透明度", () =>
            {
                DrawFloat("_DepthRange", "水深范围（世界单位）", 0.1f, 10f);
                DrawFloat("_ShallowAlpha", "浅水透明度", 0f, 1f);
                DrawFloat("_DeepAlpha", "深水透明度", 0f, 1f);
            });

            DrawSection(ref _showShore, "岸线泡沫", () =>
            {
                DrawFloat("_ShoreFoamDistance", "泡沫宽度（世界单位）", 0f, 3f);
                DrawFloat("_FoamStrength", "泡沫强度", 0f, 1f);
                DrawFloat("_FoamNoiseScale", "噪声密度", 0.05f, 10f);
                DrawFloat("_FoamPulseSpeed", "脉动速度", 0f, 5f);
            });

            DrawSection(ref _showWaves, "深水浪线", () =>
            {
                DrawFloat("_WaveScale", "浪线密度（越小越疏）", 0.005f, 0.2f);
                DrawFloat("_WaveSpeed", "浪线速度", 0f, 2f);
                DrawFloat("_WaveStrength", "浪线强度", 0f, 1f);
                DrawFloat("_WaveStartDepth", "出现水深", 0f, 3f);
            });

            DrawSection(ref _showPatches, "噪点色斑", () =>
            {
                DrawColor("_PatchColor", "色斑颜色");
                DrawFloat("_PatchScale", "色斑密度（越大越碎）", 0.005f, 0.2f);
                DrawFloat("_PatchSpeed", "生灭速度", 0f, 0.5f);
                DrawFloat("_PatchStrength", "色斑强度", 0f, 1f);
            });

            DrawSection(ref _showStreaks, "噪点流线", () =>
            {
                DrawFloat("_StreakScale", "流线密度（越小越疏）", 0.002f, 0.1f);
                DrawFloat("_StreakSpeed", "流线速度", 0f, 0.5f);
                DrawFloat("_StreakWarp", "扭曲程度", 0f, 3f);
                DrawFloat("_StreakWidth", "流线粗细", 0.01f, 0.5f);
                DrawFloat("_StreakStrength", "流线强度", 0f, 1f);
            });

            DrawSection(ref _showSparkles, "水面碎光", () =>
            {
                DrawFloat("_SparkleScale", "碎光密度", 0.1f, 10f);
                DrawFloat("_SparkleSpeed", "漂移速度", 0f, 1f);
                DrawFloat("_SparkleThreshold", "亮度阈值（越高越少）", 0.3f, 0.95f);
                DrawFloat("_SparkleStrength", "碎光强度", 0f, 1f);
            });

            DrawSection(ref _showTide, "动态海岸线", () =>
            {
                DrawFloat("_TideAmplitude", "潮汐幅度", 0f, 1f);
                DrawFloat("_TideSpeed", "潮汐速度", 0f, 3f);
                DrawFloat("_TideSpatialScale", "南北岸相位差", 0f, 0.2f);
                DrawFloat("_SurfZoneDepth", "冲浪区水深", 0.05f, 5f);
                DrawFloat("_SurfWaveRepeat", "向岸浪线条数", 1f, 8f);
                DrawFloat("_SurfWaveSpeed", "向岸浪线速度", 0f, 2f);
                DrawFloat("_SurfWaveWidth", "向岸浪线粗细", 0.01f, 0.5f);
                DrawFloat("_SurfWaveStrength", "向岸浪线强度", 0f, 1f);
            });

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("恢复推荐值"))
                {
                    ApplyRecommended();
                }

                if (GUILayout.Button("保存材质"))
                {
                    AssetDatabase.SaveAssets();
                }

                if (GUILayout.Button("选中材质"))
                {
                    Selection.activeObject = _material;
                    EditorGUIUtility.PingObject(_material);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSection(ref bool open, string title, System.Action body)
        {
            open = EditorGUILayout.Foldout(open, title, true, EditorStyles.foldoutHeader);
            if (!open)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                body();
            }
        }

        private void DrawFloat(string propertyName, string label, float min, float max)
        {
            if (!_material.HasProperty(propertyName))
            {
                return;
            }

            float current = _material.GetFloat(propertyName);
            EditorGUI.BeginChangeCheck();
            float value = EditorGUILayout.Slider(label, current, min, max);
            if (EditorGUI.EndChangeCheck())
            {
                SetFloat(propertyName, value);
            }
        }

        private void DrawColor(string propertyName, string label)
        {
            if (!_material.HasProperty(propertyName))
            {
                return;
            }

            Color current = _material.GetColor(propertyName);
            EditorGUI.BeginChangeCheck();
            Color value = EditorGUILayout.ColorField(label, current);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_material, "调整海面参数");
                _material.SetColor(propertyName, value);
                EditorUtility.SetDirty(_material);
                SceneView.RepaintAll();
            }
        }

        private void SetFloat(string propertyName, float value)
        {
            Undo.RecordObject(_material, "调整海面参数");
            _material.SetFloat(propertyName, value);
            EditorUtility.SetDirty(_material);
            SceneView.RepaintAll();
        }

        private void ApplyRecommended()
        {
            Undo.RecordObject(_material, "恢复海面推荐参数");

            foreach (KeyValuePair<string, float> pair in RecommendedFloats)
            {
                if (_material.HasProperty(pair.Key))
                {
                    _material.SetFloat(pair.Key, pair.Value);
                }
            }

            foreach (KeyValuePair<string, Color> pair in RecommendedColors)
            {
                if (_material.HasProperty(pair.Key))
                {
                    _material.SetColor(pair.Key, pair.Value);
                }
            }

            EditorUtility.SetDirty(_material);
            SceneView.RepaintAll();
            Debug.Log("[屿见] 海面参数已恢复为推荐值。");
        }
    }
}
