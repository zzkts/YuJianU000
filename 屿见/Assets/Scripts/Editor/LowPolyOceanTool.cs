using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Yujian.Island;

namespace Yujian.EditorTools
{
    public enum LowPolyOceanPreset
    {
        FlatColor = 0,
        FacetShade = 1,
        RollingWaves = 2,
        FacetWireframe = 3,
    }

    /// <summary>
    /// 低模海面的一套外观参数。预设和手动滑杆都落到这些字段上。
    /// </summary>
    [Serializable]
    public struct LowPolyOceanLook
    {
        public float waveHeight;
        public float waveSpeed;
        public float waveFrequency;
        public float facetTint;
        public float facetShade;
        public float edgeStrength;
        public float edgeWidth;
        public float shallowAlpha;
        public float deepAlpha;
        public float noiseStrength;

        public static LowPolyOceanLook Default
        {
            get { return FromPreset(LowPolyOceanPreset.RollingWaves); }
        }

        public static LowPolyOceanLook FromPreset(LowPolyOceanPreset preset)
        {
            switch (preset)
            {
                case LowPolyOceanPreset.FlatColor:
                    return new LowPolyOceanLook
                    {
                        waveHeight = 0.02f,
                        waveSpeed = 0.3f,
                        waveFrequency = 0.035f,
                        facetTint = 0.24f,
                        facetShade = 0f,
                        edgeStrength = 0f,
                        edgeWidth = 1.5f,
                        shallowAlpha = 0.60f,
                        deepAlpha = 0.98f,
                        noiseStrength = 0f,
                    };
                case LowPolyOceanPreset.FacetShade:
                    return new LowPolyOceanLook
                    {
                        waveHeight = 0.30f,
                        waveSpeed = 0.4f,
                        waveFrequency = 0.035f,
                        facetTint = 0.22f,
                        facetShade = 0.80f,
                        edgeStrength = 0f,
                        edgeWidth = 1.5f,
                        shallowAlpha = 0.58f,
                        deepAlpha = 0.98f,
                        noiseStrength = 0f,
                    };
                case LowPolyOceanPreset.FacetWireframe:
                    return new LowPolyOceanLook
                    {
                        waveHeight = 0.50f,
                        waveSpeed = 0.4f,
                        waveFrequency = 0.035f,
                        facetTint = 0.20f,
                        facetShade = 0.75f,
                        edgeStrength = 0.55f,
                        edgeWidth = 1.5f,
                        shallowAlpha = 0.60f,
                        deepAlpha = 0.98f,
                        noiseStrength = 0f,
                    };
                default:
                    // RollingWaves
                    return new LowPolyOceanLook
                    {
                        waveHeight = 1.20f,
                        waveSpeed = 0.55f,
                        waveFrequency = 0.035f,
                        facetTint = 0.22f,
                        facetShade = 0.85f,
                        edgeStrength = 0f,
                        edgeWidth = 1.5f,
                        shallowAlpha = 0.58f,
                        deepAlpha = 0.98f,
                        noiseStrength = 0f,
                    };
            }
        }
    }

    /// <summary>
    /// 低模海面的编辑器操作集合。窗口和自动化验证都调用这里，
    /// 保证“应用 / 还原”只有一份实现。
    /// </summary>
    public static class LowPolyOceanTool
    {
        public const string LowPolyShaderPath = "Assets/Shaders/IslandWaterLowPoly.shader";
        public const string LowPolyShaderName = "Yujian/Island Water Low Poly";
        public const string LowPolyMaterialPath = "Assets/Materials/Island/Ocean_Water_LowPoly.mat";
        public const string LowPolyMeshPath = "Assets/Data/Island/OceanLowPoly.asset";
        public const string ShoreMaskPath = "Assets/Data/Island/OceanShoreMask.asset";
        public const string IslandSettingsPath = "Assets/Data/Island/IslandSettings.asset";
        public const string OriginalMaterialPath = "Assets/Materials/Island/Ocean_Water.mat";
        public const string DefaultOceanName = "Ocean";

        public static bool IsLowPolyMaterial(Material material)
        {
            return material != null
                && material.shader != null
                && material.shader.name == LowPolyShaderName;
        }

        public static string GetTopologyName(LowPolyOceanTopology topology)
        {
            switch (topology)
            {
                case LowPolyOceanTopology.Radial:
                    return "A. 径向海岸网格";
                case LowPolyOceanTopology.RefinedGrid:
                    return "B. 矩形网格局部细分";
                case LowPolyOceanTopology.Delaunay:
                    return "C. Delaunay 不规则三角化";
                default:
                    return "D. 仅 Shader 柔化";
            }
        }

        public static GameObject FindOcean()
        {
            UnityEngine.SceneManagement.Scene scene =
                UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return null;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root != null && root.name == DefaultOceanName)
                {
                    return root;
                }
            }

            return null;
        }

        public static LowPolyOceanMeshSettings ReadSettings(LowPolyOceanState state)
        {
            LowPolyOceanMeshSettings settings = LowPolyOceanMeshSettings.Default;
            if (state != null)
            {
                settings.topology = state.topology;
                settings.size = state.meshSize;
                settings.cellSize = state.cellSize;
                settings.jitter = state.jitter;
                settings.seed = state.seed;
                settings.waveHeight = state.waveHeight;
                settings.shoreFadeDepth = state.shoreFadeDepth;
                settings.islandOffset = state.islandOffset;
                settings.radialSegments = state.radialSegments;
                settings.shoreRingSpacing = state.shoreRingSpacing;
                settings.shoreInset = state.shoreInset;
                settings.refineCellSize = state.refineCellSize;
                settings.refineHalfSize = state.refineHalfSize;
                settings.coarseGrowth = state.coarseGrowth;
                settings.delaunayCellSize = state.delaunayCellSize;
                settings.delaunayCoarseCellSize = state.delaunayCoarseCellSize;
            }
            return settings;
        }

        public static LowPolyOceanLook ReadLook(Material material)
        {
            LowPolyOceanLook look = LowPolyOceanLook.Default;
            if (material == null)
            {
                return look;
            }

            if (material.HasProperty("_LowPolyWaveHeight"))
            {
                look.waveHeight = material.GetFloat("_LowPolyWaveHeight");
            }
            if (material.HasProperty("_LowPolyWaveSpeed"))
            {
                look.waveSpeed = material.GetFloat("_LowPolyWaveSpeed");
            }
            if (material.HasProperty("_LowPolyWaveScale"))
            {
                look.waveFrequency = material.GetFloat("_LowPolyWaveScale");
            }
            if (material.HasProperty("_FacetTint"))
            {
                look.facetTint = material.GetFloat("_FacetTint");
            }
            if (material.HasProperty("_FacetShade"))
            {
                look.facetShade = material.GetFloat("_FacetShade");
            }
            if (material.HasProperty("_EdgeStrength"))
            {
                look.edgeStrength = material.GetFloat("_EdgeStrength");
            }
            if (material.HasProperty("_EdgeWidth"))
            {
                look.edgeWidth = material.GetFloat("_EdgeWidth");
            }
            if (material.HasProperty("_ShallowAlpha"))
            {
                look.shallowAlpha = material.GetFloat("_ShallowAlpha");
            }
            if (material.HasProperty("_DeepAlpha"))
            {
                look.deepAlpha = material.GetFloat("_DeepAlpha");
            }
            if (material.HasProperty("_NoiseStrength"))
            {
                look.noiseStrength = material.GetFloat("_NoiseStrength");
            }
            return look;
        }

        public static void ApplyLook(Material material, LowPolyOceanLook look, bool recordUndo)
        {
            if (material == null)
            {
                return;
            }

            if (recordUndo)
            {
                Undo.RecordObject(material, "调整低模海面外观");
            }

            material.SetFloat("_LowPolyWaveHeight", look.waveHeight);
            material.SetFloat("_LowPolyWaveSpeed", look.waveSpeed);
            material.SetFloat("_LowPolyWaveScale", look.waveFrequency);
            material.SetFloat("_FacetTint", look.facetTint);
            material.SetFloat("_FacetShade", look.facetShade);
            material.SetFloat("_EdgeStrength", look.edgeStrength);
            material.SetFloat("_EdgeWidth", look.edgeWidth);
            material.SetFloat("_ShallowAlpha", look.shallowAlpha);
            material.SetFloat("_DeepAlpha", look.deepAlpha);
            material.SetFloat("_NoiseStrength", look.noiseStrength);
            EditorUtility.SetDirty(material);
        }

        public static Mesh EnsureMeshAsset(
            LowPolyOceanMeshSettings settings, IslandSettings island)
        {
            EnsureFolder("Assets/Data/Island");

            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(LowPolyMeshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "OceanLowPoly" };
                AssetDatabase.CreateAsset(mesh, LowPolyMeshPath);
            }

            LowPolyOceanMeshGenerator.Fill(mesh, settings, island);
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssets();
            return mesh;
        }

        public static Vector2 ComputeIslandOffset(GameObject ocean, IslandSettings island)
        {
            if (ocean == null || island == null)
            {
                return Vector2.zero;
            }

            Vector3 position = ocean.transform.position;
            return new Vector2(
                position.x - island.center.x,
                position.z - island.center.z);
        }

        public static void SetShoreSmooth(Material material, float value, bool recordUndo)
        {
            if (material == null || !material.HasProperty("_ShoreSmooth"))
            {
                return;
            }

            if (recordUndo)
            {
                Undo.RecordObject(material, "设置海岸柔化");
            }

            material.SetFloat("_ShoreSmooth", Mathf.Clamp01(value));
            EditorUtility.SetDirty(material);
        }

        public static Material EnsureMaterial(
            LowPolyOceanMeshSettings settings, bool configureIslandMask = true)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(LowPolyShaderPath);
            if (shader == null)
            {
                Debug.LogError("[屿见] 找不到 " + LowPolyShaderPath + "，无法创建低模海面材质。");
                return null;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(LowPolyMaterialPath);
            if (material == null)
            {
                EnsureFolder("Assets/Materials/Island");
                material = new Material(shader) { name = "Ocean_Water_LowPoly" };
                AssetDatabase.CreateAsset(material, LowPolyMaterialPath);

                // 把原海面的颜色 / 泡沫 / 潮汐参数抄过来，切过去时不会突然换一个色调。
                CopyWaterProperties(LoadOriginalMaterial(), material);
                ApplyLook(material, LowPolyOceanLook.Default, false);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }

            bool hasMask = material.GetTexture("_ShoreMask") != null;
            if (configureIslandMask && !hasMask)
            {
                // 旧方案：用 IslandSettings 的高度函数生成遮罩。
                // 新材质才自动配；已经有遮罩（比如碰撞体烘焙的）就保留。
                IslandSettings island = LoadIslandSettings();
                if (island != null)
                {
                    Texture2D shoreMask = EnsureShoreMaskAsset(
                        island, settings.shoreFadeDepth);
                    if (shoreMask != null)
                    {
                        ConfigureShoreMask(
                            material,
                            shoreMask,
                            new Vector2(island.center.x, island.center.z),
                            LowPolyOceanShoreMask.ComputeHalfExtent(island) * 2f);
                    }
                }
                else
                {
                    Debug.LogWarning("[屿见] 找不到 " + IslandSettingsPath
                        + "，海岸遮罩未配置，海面浪仍可能盖到岛上。");
                }
            }

            AssetDatabase.SaveAssets();
            return material;
        }

        public static void ConfigureShoreMask(
            Material material, Texture2D mask, Vector2 center, float size)
        {
            if (material == null || mask == null)
            {
                return;
            }

            material.SetTexture("_ShoreMask", mask);
            material.SetVector("_ShoreMaskCenter",
                new Vector4(center.x, center.y, 0f, 0f));
            material.SetFloat("_ShoreMaskSize", Mathf.Max(size, 1f));
            EditorUtility.SetDirty(material);
        }

        public static IslandSettings LoadIslandSettings()
        {
            IslandSettingsAsset asset =
                AssetDatabase.LoadAssetAtPath<IslandSettingsAsset>(IslandSettingsPath);
            return asset != null ? asset.settings : null;
        }

        /// <summary>
        /// 加载或创建遮罩贴图资产（不写像素）。碰撞体烘焙和海岛高度烘焙共用这一份资产。
        /// </summary>
        public static Texture2D GetOrCreateShoreMaskTexture(int resolution)
        {
            EnsureFolder("Assets/Data/Island");
            resolution = Mathf.Clamp(resolution, 16, 1024);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ShoreMaskPath);
            bool recreate = texture == null
                || !texture.isReadable
                || texture.width != resolution
                || texture.height != resolution;

            if (recreate)
            {
                if (texture != null)
                {
                    AssetDatabase.DeleteAsset(ShoreMaskPath);
                }

                texture = new Texture2D(
                    resolution, resolution, TextureFormat.RGBA32, false, true)
                {
                    name = "OceanShoreMask",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    anisoLevel = 1,
                };
                AssetDatabase.CreateAsset(texture, ShoreMaskPath);
            }

            return texture;
        }

        /// <summary>
        /// 旧方案：用海岛高度函数烘焙遮罩。
        /// 新的碰撞体海面工具走 ColliderShoreMaskBaker，不调用这里。
        /// </summary>
        public static Texture2D EnsureShoreMaskAsset(IslandSettings island, float fadeDepth)
        {
            if (island == null)
            {
                return null;
            }

            Texture2D texture = GetOrCreateShoreMaskTexture(
                LowPolyOceanShoreMask.DefaultResolution);
            if (texture == null)
            {
                return null;
            }

            LowPolyOceanShoreMask.Fill(texture, island, fadeDepth);
            EditorUtility.SetDirty(texture);
            AssetDatabase.SaveAssets();
            return texture;
        }

        public static Material LoadOriginalMaterial()
        {
            return AssetDatabase.LoadAssetAtPath<Material>(OriginalMaterialPath);
        }

        public static Mesh LoadBuiltinPlaneMesh()
        {
            GameObject temporary = GameObject.CreatePrimitive(PrimitiveType.Plane);
            MeshFilter filter = temporary.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            UnityEngine.Object.DestroyImmediate(temporary);
            return mesh;
        }

        /// <summary>
        /// 把海面切换到低模网格 + 低模材质，并记录原始状态。
        /// 重复调用不会覆盖第一次捕获的原始网格 / 材质 / 缩放。
        /// </summary>
        public static LowPolyOceanState ApplyToOcean(
            GameObject ocean,
            Mesh mesh,
            Material material,
            LowPolyOceanMeshSettings settings,
            int preset,
            bool recordUndo)
        {
            if (ocean == null)
            {
                throw new ArgumentNullException(nameof(ocean));
            }

            MeshFilter filter = ocean.GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = recordUndo
                    ? Undo.AddComponent<MeshFilter>(ocean)
                    : ocean.AddComponent<MeshFilter>();
            }

            MeshRenderer renderer = ocean.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = recordUndo
                    ? Undo.AddComponent<MeshRenderer>(ocean)
                    : ocean.AddComponent<MeshRenderer>();
            }

            LowPolyOceanState state = ocean.GetComponent<LowPolyOceanState>();
            if (state == null)
            {
                state = recordUndo
                    ? Undo.AddComponent<LowPolyOceanState>(ocean)
                    : ocean.AddComponent<LowPolyOceanState>();
            }

            if (!state.originalCaptured)
            {
                if (recordUndo)
                {
                    Undo.RecordObject(state, "记录原始海面");
                }

                state.originalMesh = filter.sharedMesh;
                state.originalMaterial = renderer.sharedMaterial;
                state.originalScale = ocean.transform.localScale;
                state.originalCaptured = true;
            }

            if (recordUndo)
            {
                Undo.RecordObject(filter, "应用低模海面");
                Undo.RecordObject(renderer, "应用低模海面");
                Undo.RecordObject(ocean.transform, "应用低模海面");
                Undo.RecordObject(state, "应用低模海面");
            }

            filter.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ocean.transform.localScale = Vector3.one;

            state.lowPolyMesh = mesh;
            state.lowPolyMaterial = material;
            state.topology = settings.topology;
            state.meshSize = settings.size;
            state.cellSize = settings.cellSize;
            state.jitter = settings.jitter;
            state.seed = settings.seed;
            state.waveHeight = settings.waveHeight;
            state.shoreFadeDepth = settings.shoreFadeDepth;
            state.islandOffset = settings.islandOffset;
            state.radialSegments = settings.radialSegments;
            state.shoreRingSpacing = settings.shoreRingSpacing;
            state.shoreInset = settings.shoreInset;
            state.refineCellSize = settings.refineCellSize;
            state.refineHalfSize = settings.refineHalfSize;
            state.coarseGrowth = settings.coarseGrowth;
            state.delaunayCellSize = settings.delaunayCellSize;
            state.delaunayCoarseCellSize = settings.delaunayCoarseCellSize;
            state.preset = preset;

            EditorUtility.SetDirty(filter);
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(state);
            return state;
        }

        /// <summary>
        /// 只切换外观，保留三角形网格（用于对比“三角拓扑 + 原版水面效果”）。
        /// </summary>
        public static void SetLookMaterial(GameObject ocean, Material material, bool recordUndo)
        {
            if (ocean == null)
            {
                return;
            }

            MeshRenderer renderer = ocean.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                return;
            }

            if (recordUndo)
            {
                Undo.RecordObject(renderer, "切换海面外观");
            }

            renderer.sharedMaterial = material;
            EditorUtility.SetDirty(renderer);
        }

        /// <summary>
        /// 还原成原始海面：网格、材质、缩放全部回到首次应用前，并移除状态组件。
        /// 没有状态组件时用调用方给的兜底值（内置 Plane + 原材质）。
        /// </summary>
        public static bool RestoreOriginal(
            GameObject ocean,
            Mesh fallbackMesh,
            Material fallbackMaterial,
            bool recordUndo)
        {
            if (ocean == null)
            {
                return false;
            }

            MeshFilter filter = ocean.GetComponent<MeshFilter>();
            MeshRenderer renderer = ocean.GetComponent<MeshRenderer>();
            if (filter == null || renderer == null)
            {
                return false;
            }

            LowPolyOceanState state = ocean.GetComponent<LowPolyOceanState>();
            bool hasState = state != null && state.originalCaptured;
            Mesh mesh = hasState ? state.originalMesh : fallbackMesh;
            if (mesh == null)
            {
                return false;
            }

            Material material = hasState ? state.originalMaterial : fallbackMaterial;
            Vector3 scale = hasState && state.originalScale != Vector3.zero
                ? state.originalScale
                : new Vector3(200f, 1f, 200f);

            if (recordUndo)
            {
                Undo.RecordObject(filter, "还原原海面");
                Undo.RecordObject(renderer, "还原原海面");
                Undo.RecordObject(ocean.transform, "还原原海面");
            }

            filter.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            ocean.transform.localScale = scale;

            EditorUtility.SetDirty(filter);
            EditorUtility.SetDirty(renderer);

            if (state != null)
            {
                if (recordUndo)
                {
                    Undo.DestroyObjectImmediate(state);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(state);
                }
            }

            return true;
        }

        private static void CopyWaterProperties(Material source, Material target)
        {
            if (source == null || target == null || source.shader == null)
            {
                return;
            }

            Shader shader = source.shader;
            int propertyCount = shader.GetPropertyCount();
            for (int i = 0; i < propertyCount; i++)
            {
                string name = shader.GetPropertyName(i);
                if (!target.HasProperty(name))
                {
                    continue;
                }

                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color:
                        target.SetColor(name, source.GetColor(name));
                        break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        target.SetFloat(name, source.GetFloat(name));
                        break;
                    case ShaderPropertyType.Vector:
                        target.SetVector(name, source.GetVector(name));
                        break;
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
