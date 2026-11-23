using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 海面参数总控窗口：把海面材质上的常用 shader 参数集中到一个窗口里，
    /// 拖动滑块即可在 Scene/Game 视图实时看到变化，不用来回切到材质 Inspector。
    /// 同时支持项目自制的 Yujian/Island Water（及低模版）和 Stylized Water 2 插件材质。
    /// </summary>
    public class IslandWaterSettingsWindow : EditorWindow
    {
        private const string MenuPath = "屿见/场景/海面参数调节";
        private const string DefaultMaterialPath = "Assets/Materials/Island/Ocean_Water.mat";
        private const string WaterShaderName = "Yujian/Island Water";
        private const string LowPolyWaterShaderName = "Yujian/Island Water Low Poly";
        private const string StylizedWater2ShaderPrefix = "Stylized Water 2/";

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
                { "_ShallowAlpha", 0.65f },
                { "_DeepAlpha", 0.98f },

                { "_ShoreFoamDistance", 2.5f },
                { "_ShoreFoamCore", 0.60f },
                { "_FoamStrength", 1f },
                { "_FoamNoiseScale", 0.25f },
                { "_FoamPulseSpeed", 0.5f },

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
                { "_SurfZoneDepth", 2.5f },
                { "_SurfWaveRepeat", 2f },
                { "_SurfWaveSpeed", 0.30f },
                { "_SurfWaveWidth", 0.12f },
                { "_SurfWaveStrength", 0.8f },
            };

        private static readonly Dictionary<string, Color> RecommendedColors =
            new Dictionary<string, Color>
            {
                { "_ShallowColor", new Color(0.45f, 0.85f, 0.85f, 1f) },
                { "_DeepColor", new Color(0.12f, 0.42f, 0.55f, 1f) },
                { "_FoamColor", new Color(0.94f, 0.98f, 1f, 1f) },
                { "_PatchColor", new Color(0.28f, 0.70f, 0.72f, 1f) },
            };

        // Stylized Water 2 的岸线泡沫推荐值（与 Ocean 材质当前调好的参数一致）。
        private static readonly Dictionary<string, float> StylizedWater2RecommendedFloats =
            new Dictionary<string, float>
            {
                { "_IntersectionLength", 3f },
                { "_IntersectionFalloff", 0.8f },
                { "_IntersectionClipping", 0.5f },
                { "_IntersectionTiling", 0.2f },
                { "_IntersectionSpeed", 0.15f },
                { "_IntersectionRippleStrength", 0.35f },
            };

        private static readonly Dictionary<string, Color> StylizedWater2RecommendedColors =
            new Dictionary<string, Color>
            {
                { "_FoamColor", Color.white },
                { "_IntersectionColor", Color.white },
            };

        [MenuItem(MenuPath, false, 220)]
        private static void Open()
        {
            IslandWaterSettingsWindow window = GetWindow<IslandWaterSettingsWindow>("海面参数");
            window.minSize = new Vector2(380f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            // 优先使用当前选中的水面材质，方便从低模海面窗口直接打开并调色。
            if (Selection.activeObject is Material selected && IsWaterMaterial(selected))
            {
                _material = selected;
            }
            else if (_material == null || !IsWaterMaterial(_material))
            {
                // 没有选中材质时，直接找场景里正在使用的水面材质（现在默认是插件海面），
                // 避免窗口停留在旧的 Ocean_Water.mat 上、改了参数却看不到变化。
                _material = FindSceneWaterMaterial();
                if (_material == null)
                {
                    _material = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
                }
            }

            Selection.selectionChanged += OnSelectionChanged;
        }

        private static Material FindSceneWaterMaterial()
        {
            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Material fallback = null;

            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = renderers[i].sharedMaterials;
                for (int m = 0; m < materials.Length; m++)
                {
                    Material candidate = materials[m];
                    if (!IsWaterMaterial(candidate))
                    {
                        continue;
                    }

                    // 场景里新旧海面同时存在时，优先用 Stylized Water 2 的。
                    if (IsStylizedWater2Material(candidate))
                    {
                        return candidate;
                    }

                    if (fallback == null)
                    {
                        fallback = candidate;
                    }
                }
            }

            return fallback;
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
            if (material == null || material.shader == null)
            {
                return false;
            }

            string shaderName = material.shader.name;
            return shaderName == WaterShaderName
                || shaderName == LowPolyWaterShaderName
                || IsStylizedWater2Shader(shaderName);
        }

        private static bool IsStylizedWater2Shader(string shaderName)
        {
            return !string.IsNullOrEmpty(shaderName)
                && shaderName.StartsWith(StylizedWater2ShaderPrefix);
        }

        private static bool IsStylizedWater2Material(Material material)
        {
            return material != null
                && material.shader != null
                && IsStylizedWater2Shader(material.shader.name);
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
                    "未找到海面材质。请先运行“屿见/场景/生成低模海岛底图”，或手动拖入使用 " +
                    "Yujian/Island Water 或 Stylized Water 2 的材质。",
                    MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            if (!IsWaterMaterial(_material))
            {
                EditorGUILayout.HelpBox(
                    "这个材质没有使用 Yujian/Island Water 或 Stylized Water 2，参数不会生效。",
                    MessageType.Error);
                EditorGUILayout.EndScrollView();
                return;
            }

            if (IsStylizedWater2Material(_material))
            {
                EditorGUILayout.HelpBox(
                    "正在编辑 Stylized Water 2 材质。这里收拢了常用的颜色 / 水深 / 岸线泡沫 / " +
                    "表面泡沫 / 波浪参数；贴图、开关和法线等完整参数仍可选中材质在 Inspector 中编辑。",
                    MessageType.Info);
                DrawStylizedWater2Sections();
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "拖动参数即可实时预览。Scene 视图右上角建议保持 Animated Materials（动画材质）开启，才能看到随时间流动的效果。",
                    MessageType.Info);
                DrawYujianWaterSections();
            }

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

        private void DrawYujianWaterSections()
        {
            DrawSection(ref _showColors, "水体颜色", () =>
            {
                DrawColor("_ShallowColor", "浅水颜色");
                DrawColor("_DeepColor", "深水颜色");
                DrawColor("_FoamColor", "泡沫颜色");
            });

            DrawSection(ref _showDepth, "深度与透明度", () =>
            {
                DrawFloat("_DepthRange", "水深范围（世界单位）", 0.1f, 10f);
                DrawFloat("_ShallowAlpha", "浅水不透明度", 0f, 1f);
                DrawFloat("_DeepAlpha", "深水不透明度", 0f, 1f);
            });

            DrawSection(ref _showShore, "岸线泡沫", () =>
            {
                DrawFloat("_ShoreFoamDistance", "泡沫宽度（世界单位）", 0f, 6f);
                DrawFloat("_ShoreFoamCore", "贴岸实心白沫宽度", 0f, 2f);
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
        }

        private void DrawStylizedWater2Sections()
        {
            DrawSection(ref _showColors, "水体颜色", () =>
            {
                DrawColorHDR("_BaseColor", "深水颜色");
                DrawColorHDR("_ShallowColor", "浅水颜色");
                DrawColorHDR("_HorizonColor", "地平线颜色");
                DrawColor("_FoamColor", "表面泡沫颜色");
                DrawColor("_IntersectionColor", "岸线泡沫颜色");
            });

            DrawSection(ref _showDepth, "深度与透明", () =>
            {
                DrawFloat("_DepthVertical", "纵向水深", 0.01f, 16f);
                DrawFloat("_DepthHorizontal", "水平水深", 0.01f, 8f);
                DrawFloat("_EdgeFade", "边缘淡出", 0f, 10f);
                DrawFloat("_ColorAbsorption", "颜色吸收", 0f, 1f);
            });

            DrawSection(ref _showShore, "岸线泡沫（岛岸交界）", () =>
            {
                DrawIntersectionStyle();
                DrawFloat("_IntersectionLength", "泡沫宽度（水深，米）", 0.01f, 5f);
                DrawFloat("_IntersectionClipping", "破洞阈值（越小越实）", 0.01f, 1f);
                DrawFloat("_IntersectionFalloff", "外缘衰减（越小越宽）", 0.01f, 1f);
                DrawFloat("_IntersectionTiling", "泡沫花纹密度", 0.01f, 2f);
                DrawFloat("_IntersectionSpeed", "流动速度", 0f, 1f);
                DrawFloat("_IntersectionRippleStrength", "向岸浪强度", 0f, 1f);
                DrawFloat("_IntersectionDistortion", "泡沫扭曲", 0f, 1f);
            });

            DrawSection(ref _showPatches, "表面泡沫", () =>
            {
                DrawFloat("_FoamBaseAmount", "基础泡沫量", 0f, 1f);
                DrawFloat("_FoamWaveAmount", "浪尖泡沫量", 0f, 2f);
                DrawFloat("_FoamClipping", "破洞阈值", 0f, 0.999f);
                DrawFloat("_FoamSubTiling", "子层密度", 0.01f, 4f);
                DrawFloat("_FoamSpeed", "流动速度", 0f, 1f);
                DrawFloat("_FoamDistortion", "泡沫扭曲", 0f, 3f);
            });

            DrawSection(ref _showWaves, "波浪", () =>
            {
                DrawFloat("_WaveHeight", "浪高", 0f, 10f);
                DrawFloat("_WaveSpeed", "浪速", 0f, 10f);
                DrawFloat("_WaveNormalStr", "法线强度", 0f, 32f);
                DrawFloat("_WaveSteepness", "浪陡度", 0f, 5f);
                DrawInt("_WaveCount", "波浪层数", 1, 5);
                DrawFloat("_WaveDistance", "波浪距离衰减", 0f, 1f);
            });

            DrawSection(ref _showStreaks, "法线与反射", () =>
            {
                DrawFloat("_NormalStrength", "法线贴图强度", 0f, 1f);
                DrawFloat("_NormalSpeed", "法线流动速度", 0f, 5f);
                DrawFloat("_ReflectionStrength", "环境反射强度", 0f, 1f);
                DrawFloat("_SunReflectionStrength", "太阳反光强度", 0f, 50f);
                DrawFloat("_SunReflectionSize", "太阳反光大小", 0f, 1f);
            });
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

        private void DrawInt(string propertyName, string label, int min, int max)
        {
            if (!_material.HasProperty(propertyName))
            {
                return;
            }

            int current = Mathf.RoundToInt(_material.GetFloat(propertyName));
            EditorGUI.BeginChangeCheck();
            int value = EditorGUILayout.IntSlider(label, current, min, max);
            if (EditorGUI.EndChangeCheck())
            {
                SetFloat(propertyName, value);
            }
        }

        // Stylized Water 2 的岸线泡沫样式同时靠 float 参数和两个 local keyword 切换。
        private void DrawIntersectionStyle()
        {
            if (!_material.HasProperty("_IntersectionStyle"))
            {
                return;
            }

            int current = Mathf.Clamp(
                Mathf.RoundToInt(_material.GetFloat("_IntersectionStyle")), 0, 2);
            string[] options = { "关闭", "Sharp（块状白沫）", "Smooth（柔和）" };
            EditorGUI.BeginChangeCheck();
            int value = EditorGUILayout.Popup("样式", current, options);
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            Undo.RecordObject(_material, "调整海面参数");
            _material.SetFloat("_IntersectionStyle", value);
            _material.DisableKeyword("_SHARP_INERSECTION");
            _material.DisableKeyword("_SMOOTH_INTERSECTION");
            if (value == 1)
            {
                _material.EnableKeyword("_SHARP_INERSECTION");
            }
            else if (value == 2)
            {
                _material.EnableKeyword("_SMOOTH_INTERSECTION");
            }

            EditorUtility.SetDirty(_material);
            SceneView.RepaintAll();
        }

        // Stylized Water 2 的 _BaseColor / _ShallowColor / _HorizonColor 是 HDR 颜色，
        // 用普通 ColorField 会把超过 1 的强度裁掉，这里单独开 HDR。
        private void DrawColorHDR(string propertyName, string label)
        {
            if (!_material.HasProperty(propertyName))
            {
                return;
            }

            Color current = _material.GetColor(propertyName);
            EditorGUI.BeginChangeCheck();
            Color value = EditorGUILayout.ColorField(
                new GUIContent(label), current, true, true, true);
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

            bool stylized = IsStylizedWater2Material(_material);
            Dictionary<string, float> floatTable =
                stylized ? StylizedWater2RecommendedFloats : RecommendedFloats;
            Dictionary<string, Color> colorTable =
                stylized ? StylizedWater2RecommendedColors : RecommendedColors;

            foreach (KeyValuePair<string, float> pair in floatTable)
            {
                if (_material.HasProperty(pair.Key))
                {
                    _material.SetFloat(pair.Key, pair.Value);
                }
            }

            foreach (KeyValuePair<string, Color> pair in colorTable)
            {
                if (_material.HasProperty(pair.Key))
                {
                    _material.SetColor(pair.Key, pair.Value);
                }
            }

            // 低模水面需要更实的水体、并默认关掉原海面噪点，否则分面会被盖住。
            if (!stylized && _material.shader != null
                && _material.shader.name == LowPolyWaterShaderName)
            {
                _material.SetFloat("_ShallowAlpha", 0.58f);
                _material.SetFloat("_DeepAlpha", 0.98f);
                if (_material.HasProperty("_NoiseStrength"))
                {
                    _material.SetFloat("_NoiseStrength", 0f);
                }
            }

            EditorUtility.SetDirty(_material);
            SceneView.RepaintAll();
            Debug.Log("[屿见] 海面参数已恢复为推荐值。");
        }
    }
}
