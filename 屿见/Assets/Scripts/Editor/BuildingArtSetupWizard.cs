using System.IO;
using UnityEditor;
using UnityEngine;
using Yujian.Building;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 把 Assets/Modles/Buildings 下导入的六件真实模型接到可建造建筑上。
    ///
    /// 2026-11-20 用户拍板：
    ///   · 映射：风琴博物馆=Fengqing、八卦楼=Shuzhuang_04、菽庄花园=Shuzhuang_03、
    ///     日光岩=Shuzhuang_01；**新增「海天堂构」= Haitian**；Shuzhuang_02 暂不用；
    ///   · **原料的样式不影响建筑外观**：建成后就是真实模型，不再按材料分层刷色；
    ///   · 蓝图 / 放材料阶段也显示半透明真实模型（绿 / 红整体染色，不显示材料色）；
    ///   · 回收站保持现有占位外观。
    ///
    /// 为什么用编辑器向导而不是直接改 .prefab / .asset：
    /// 与工程里其它向导同一理由（见 开发进度.md 踩坑 2）——让 Unity 自己写，
    /// 避免外部改文件导致编辑器重载、引用丢失。
    ///
    /// 运行入口：菜单栏 → 屿见 → 建筑 → 替换建筑美术资产（模型版）
    /// 本工具可重复运行：材质、预制体、数据资产都按路径覆盖，不会越堆越多。
    /// </summary>
    public static class BuildingArtSetupWizard
    {
        // ---------------- 路径 ----------------

        private const string BuildingDataFolder = "Assets/Data/Buildings";
        private const string PrefabFolder = "Assets/Prefabs/建筑";
        private const string SourceBuildingPrefab = PrefabFolder + "/建筑.prefab";
        private const string SourceBlueprintPrefab = PrefabFolder + "/蓝图_建筑.prefab";
        private const string ValidMaterialPath = "Assets/Materials/Valid.mat";
        private const string InvalidMaterialPath = "Assets/Materials/InValid.mat";
        private const string ModelMaterialsFolder = "Assets/Materials/Buildings";
        private const string TemplateBuildingAsset = BuildingDataFolder + "/Building_风琴博物馆.asset";

        /// <summary>模型根目录。工程里美术用的是拼写错误的 Modles；两种都认，方便以后改名。</summary>
        private static readonly string[] ModelRootCandidates =
        {
            "Assets/Modles/Buildings",
            "Assets/Models/Buildings",
        };

        /// <summary>
        /// 建筑整体缩放倍数。原来是 2×2 格（世界 4×4），用户要求「整体放大 3 倍，
        /// 且实际大小与网格占地匹配」→ 世界尺寸 12×12，占地格数 6×6。
        /// </summary>
        private const int ModelScaleMultiplier = 3;

        /// <summary>基准占地：2×2 格 × 单格 2 = 4×4 世界单位。</summary>
        private const float BaseFootprintWorld = 4f;

        /// <summary>归一化目标世界尺寸 = 4 × 3 = 12。模型子物体与 BoxCollider 都按这个尺寸生成。</summary>
        private const float FootprintWorld = BaseFootprintWorld * ModelScaleMultiplier;

        /// <summary>网格单格边长。2026-11-20 用户定义一单位面积长度 = 10 世界单位。</summary>
        private const float CellSize = 10f;

        /// <summary>兜底占地：6×6。正常会按每件模型的实际宽深算成非正方形（见 ComputeFootprintCells）。</summary>
        private static readonly Vector2Int ScaledFootprintCells =
            new Vector2Int(2 * ModelScaleMultiplier, 2 * ModelScaleMultiplier);

        private const string LayerNodeName = "楼层";
        private const string ModelNodeName = "模型";

        /// <summary>建筑 → 模型。新增建筑（资产不存在时）只允许海天堂构。</summary>
        private static readonly Mapping[] Mappings =
        {
            new Mapping("Building_风琴博物馆.asset", "风琴博物馆", "Fengqing"),
            new Mapping("Building_八卦楼.asset", "八卦楼", "Shuzhuang_04"),
            new Mapping("Building_菽庄花园.asset", "菽庄花园", "Shuzhuang_03"),
            new Mapping("Building_日光岩.asset", "日光岩", "Shuzhuang_01"),
            new Mapping("Building_海天堂构.asset", "海天堂构", "Haitian", true),
        };

        private sealed class Mapping
        {
            public readonly string DataFileName;
            public readonly string DisplayName;
            public readonly string ModelName;
            public readonly bool MayCreateDataAsset;

            public Mapping(string dataFileName, string displayName, string modelName, bool mayCreateDataAsset = false)
            {
                DataFileName = dataFileName;
                DisplayName = displayName;
                ModelName = modelName;
                MayCreateDataAsset = mayCreateDataAsset;
            }

            public string DataPath => BuildingDataFolder + "/" + DataFileName;
            public string BuildingPrefabPath => PrefabFolder + "/建筑_" + DisplayName + ".prefab";
            public string BlueprintPrefabPath => PrefabFolder + "/蓝图_" + DisplayName + ".prefab";
        }

        [MenuItem("屿见/建筑/替换建筑美术资产（模型版）", false, 515)]
        public static void Configure()
        {
            Configure(true);
        }

        /// <summary>自动修复/批处理用：showDialog = false 时不弹完成提示。</summary>
        internal static void Configure(bool showDialog)
        {
            GameObject sourceBuilding = AssetDatabase.LoadAssetAtPath<GameObject>(SourceBuildingPrefab);
            GameObject sourceBlueprint = AssetDatabase.LoadAssetAtPath<GameObject>(SourceBlueprintPrefab);
            Material validMaterial = AssetDatabase.LoadAssetAtPath<Material>(ValidMaterialPath);
            Material invalidMaterial = AssetDatabase.LoadAssetAtPath<Material>(InvalidMaterialPath);
            BuildingData template = AssetDatabase.LoadAssetAtPath<BuildingData>(TemplateBuildingAsset);

            if (sourceBuilding == null || sourceBlueprint == null)
            {
                Fail($"找不到源预制体：\n{SourceBuildingPrefab}\n{SourceBlueprintPrefab}\n\n" +
                     "请先确认阶段 3 / 阶段 4 的向导已经跑过。");
                return;
            }

            if (validMaterial == null || invalidMaterial == null)
            {
                Fail($"找不到蓝图材质：\n{ValidMaterialPath}\n{InvalidMaterialPath}");
                return;
            }

            if (template == null)
            {
                Fail($"找不到模板建筑数据：{TemplateBuildingAsset}\n" +
                     "海天堂构的材料需求要从它复制。");
                return;
            }

            EnsureFolder(ModelMaterialsFolder);

            int built = 0;

            for (int i = 0; i < Mappings.Length; i++)
            {
                Mapping mapping = Mappings[i];

                if (!TryResolveModel(mapping.ModelName, out string modelPath, out string modelFolder))
                {
                    Debug.LogError($"[建筑美术] 找不到模型「{mapping.ModelName}」的 FBX，" +
                                   $"跳过「{mapping.DisplayName}」。已找过：{string.Join("、", ModelRootCandidates)}");
                    continue;
                }

                ConfigureTextureImporters(modelFolder);

                Material material = EnsureModelMaterial(mapping.ModelName, modelFolder);

                if (material == null)
                {
                    Debug.LogError($"[建筑美术] 为「{mapping.ModelName}」创建材质失败，跳过「{mapping.DisplayName}」。");
                    continue;
                }

                GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

                GameObject buildingPrefab = BuildBuildingPrefab(mapping, modelAsset, material);

                if (buildingPrefab == null)
                {
                    continue;
                }

                GameObject blueprintPrefab = BuildBlueprintPrefab(
                    mapping, modelAsset, material, validMaterial, invalidMaterial);

                if (blueprintPrefab == null)
                {
                    continue;
                }

                if (!AssignToDataAsset(mapping, template, buildingPrefab, blueprintPrefab))
                {
                    continue;
                }

                built++;
                Debug.Log($"[建筑美术] 「{mapping.DisplayName}」→ {mapping.ModelName}" +
                          $"\n  · 建筑预制体：{mapping.BuildingPrefabPath}" +
                          $"\n  · 蓝图预制体：{mapping.BlueprintPrefabPath}" +
                          $"\n  · 数据资产：{mapping.DataPath}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (built == 0)
            {
                Fail("没有成功接入任何建筑。请看 Console 里的具体错误。");
                return;
            }

            const string nextSteps =
                "还需要在 Unity 里跑两个现成向导（都可重复运行）：\n" +
                "  1. 屿见/通用模块/配置存档系统 —— 给「海天堂构」生成保存目录条目；\n" +
                "  2. 屿见/建筑/配置建筑系统（阶段 3）—— 建筑面板多出「海天堂构」按钮并接好 BuildingUI。\n" +
                "\n" +
                "完成后 Play：\n" +
                "  · 点建筑按钮，蓝图是半透明真实模型，可放置绿 / 不可放置红；\n" +
                "  · 放材料只在建造 UI 里计数，建筑外观不再随材料变色；\n" +
                "  · 确认建造后显示不透明的真实模型；回收站保持原占位外观。";

            if (showDialog)
            {
                EditorUtility.DisplayDialog("建筑美术接入完成", $"已处理 {built} 座建筑。\n\n{nextSteps}", "知道了");
            }

            Debug.Log("[建筑美术] 完成，已处理 " + built + " 座建筑。\n" + nextSteps);
        }

        // ---------------- 材质 ----------------

        /// <summary>
        /// 把贴图导入设置改对：法线贴图必须是 NormalMap，法线 / 金属度 / 粗糙度必须关 sRGB。
        /// 不改的话 NormalMap 会被当成普通贴图画进 BaseMap，模型看起来会发灰、没有细节。
        /// </summary>
        private static void ConfigureTextureImporters(string modelFolder)
        {
            string textureFolder = modelFolder + "/textures";

            if (!AssetDatabase.IsValidFolder(textureFolder))
            {
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { textureFolder });

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

                if (importer == null)
                {
                    continue;
                }

                string fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                bool isNormal = fileName.Contains("normal") || fileName.Contains("bump");
                bool isData = isNormal || fileName.Contains("metallic") || fileName.Contains("roughness") ||
                              fileName.Contains("occlusion") || fileName.Contains("ao");

                TextureImporterType wantedType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                bool wantedSrgb = !isData;
                bool changed = false;

                if (importer.textureType != wantedType)
                {
                    importer.textureType = wantedType;
                    changed = true;
                }

                if (importer.sRGBTexture != wantedSrgb)
                {
                    importer.sRGBTexture = wantedSrgb;
                    changed = true;
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                }
            }
        }

        /// <summary>
        /// 为一件模型建一个 URP/Lit 材质：BaseMap = basecolor，BumpMap = normal。
        /// metallic / roughness 贴图是 glTF 打包格式（G=粗糙度、B=金属度），与 URP 的
        /// MetallicGlossMap 通道约定不同，这里不硬接，先用常量近似——低模外观够用。
        /// </summary>
        private static Material EnsureModelMaterial(string modelName, string modelFolder)
        {
            string path = ModelMaterialsFolder + "/" + modelName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");

                if (shader == null)
                {
                    Debug.LogError("[建筑美术] 找不到 URP/Lit 着色器，无法创建模型材质。");
                    return null;
                }

                material = new Material(shader) { name = modelName };
                AssetDatabase.CreateAsset(material, path);
            }

            Texture2D baseMap = FindTexture(modelFolder, "basecolor", "base_color", "albedo", "diffuse");
            Texture2D normal = FindTexture(modelFolder, "normal", "bump");

            if (baseMap != null)
            {
                material.SetTexture("_BaseMap", baseMap);
                material.SetTexture("_MainTex", baseMap);
            }

            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", 1f);
                material.EnableKeyword("_NORMALMAP");
            }
            else
            {
                material.DisableKeyword("_NORMALMAP");
            }

            material.SetFloat("_Metallic", 0.05f);
            material.SetFloat("_Smoothness", 0.25f);

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D FindTexture(string modelFolder, params string[] keywords)
        {
            string textureFolder = modelFolder + "/textures";

            if (!AssetDatabase.IsValidFolder(textureFolder))
            {
                return null;
            }

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { textureFolder });

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                string fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

                for (int k = 0; k < keywords.Length; k++)
                {
                    if (fileName.Contains(keywords[k]))
                    {
                        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    }
                }
            }

            return null;
        }

        // ---------------- 预制体 ----------------

        private static GameObject BuildBuildingPrefab(Mapping mapping, GameObject modelAsset, Material material)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(SourceBuildingPrefab);

            if (root == null)
            {
                Debug.LogError($"[建筑美术] 加载源建筑预制体失败：{SourceBuildingPrefab}");
                return null;
            }

            try
            {
                root.name = "建筑_" + mapping.DisplayName;
                ResetTransform(root.transform);

                RemoveExistingVisuals(root.transform);

                // 真实模型外观不再需要楼层组件；留着它会在运行时把隐藏的方块重新打开
                BuildingLayerStack stack = root.GetComponent<BuildingLayerStack>();

                if (stack != null)
                {
                    Object.DestroyImmediate(stack);
                }

                GameObject model = CreateModelInstance(root.transform, modelAsset, material, out Bounds bounds);

                if (model == null)
                {
                    return null;
                }

                BuildingModelVisual visual = root.GetComponent<BuildingModelVisual>();

                if (visual == null)
                {
                    visual = root.AddComponent<BuildingModelVisual>();
                }

                SerializedObject visualSo = new SerializedObject(visual);
                SerializedProperty modelRoot = visualSo.FindProperty("modelRoot");

                if (modelRoot != null)
                {
                    modelRoot.objectReferenceValue = model.transform;
                }

                visualSo.ApplyModifiedPropertiesWithoutUndo();

                BoxCollider collider = root.GetComponent<BoxCollider>();

                if (collider == null)
                {
                    collider = root.AddComponent<BoxCollider>();
                }

                collider.center = bounds.center;
                collider.size = bounds.size;

                return PrefabUtility.SaveAsPrefabAsset(root, mapping.BuildingPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static GameObject BuildBlueprintPrefab(
            Mapping mapping,
            GameObject modelAsset,
            Material material,
            Material validMaterial,
            Material invalidMaterial)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(SourceBlueprintPrefab);

            if (root == null)
            {
                Debug.LogError($"[建筑美术] 加载源蓝图预制体失败：{SourceBlueprintPrefab}");
                return null;
            }

            try
            {
                root.name = "蓝图_" + mapping.DisplayName;
                ResetTransform(root.transform);

                RemoveExistingVisuals(root.transform);

                // 蓝图不刷材料色：把楼层组件和接线一起去掉，只保留绿 / 红整体染色
                BuildingLayerStack stack = root.GetComponent<BuildingLayerStack>();

                if (stack != null)
                {
                    Object.DestroyImmediate(stack);
                }

                BuildingBlueprint blueprint = root.GetComponent<BuildingBlueprint>();

                if (blueprint == null)
                {
                    blueprint = root.AddComponent<BuildingBlueprint>();
                }

                SerializedObject blueprintSo = new SerializedObject(blueprint);
                SetObject(blueprintSo, "validMaterial", validMaterial);
                SetObject(blueprintSo, "invalidMaterial", invalidMaterial);
                SetObject(blueprintSo, "materialLayers", null);
                blueprintSo.ApplyModifiedPropertiesWithoutUndo();

                if (CreateModelInstance(root.transform, modelAsset, material, out _) == null)
                {
                    return null;
                }

                // 蓝图不应带任何 Collider；新模型不建碰撞体，源预制体里若有也一并去掉
                Collider[] colliders = root.GetComponentsInChildren<Collider>(true);

                for (int i = 0; i < colliders.Length; i++)
                {
                    Object.DestroyImmediate(colliders[i]);
                }

                return PrefabUtility.SaveAsPrefabAsset(root, mapping.BlueprintPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>删掉占位阶段留下的「楼层」与「模型」节点，避免和新模型叠在一起。</summary>
        private static void RemoveExistingVisuals(Transform root)
        {
            Transform layers = root.Find(LayerNodeName);

            if (layers != null)
            {
                Object.DestroyImmediate(layers.gameObject);
            }

            Transform oldModel = root.Find(ModelNodeName);

            if (oldModel != null)
            {
                Object.DestroyImmediate(oldModel.gameObject);
            }
        }

        /// <summary>
        /// 实例化模型，按占地宽深归一化，水平居中、底部贴地（y = 0）。
        /// 返回模型实例；bounds 是模型在预制体根空间里的包围盒。
        /// </summary>
        private static GameObject CreateModelInstance(
            Transform parent, GameObject modelAsset, Material material, out Bounds bounds)
        {
            bounds = default;

            if (modelAsset == null)
            {
                Debug.LogError("[建筑美术] 模型资产为空，无法生成外观。");
                return null;
            }

            GameObject model = Object.Instantiate(modelAsset, parent, false);
            model.name = ModelNodeName;
            model.transform.localPosition = Vector3.zero;
            model.transform.localScale = Vector3.one;

            // 注意：**不要**把 rotation 重置成 identity。
            // Meshy 的 FBX 是 Z-up，根节点带 -90° X 旋转把它转成 Y-up；
            // 之前这里重置成 identity，模型就向后倒下了。保留导入时自带的旋转。
            // 缩放 / 贴地偏移都在保留旋转之后量包围盒，所以尺寸仍然正确。

            ApplyMaterial(model, material);

            // 先按原始尺寸量一遍，再缩放到目标占地；模型来自 Meshy，单位是 cm，
            // 不归一化的话放进 4×4 的格子里会大几十倍
            bounds = MeasureBounds(model.transform, parent);
            float horizontal = Mathf.Max(bounds.size.x, bounds.size.z);

            if (horizontal > 0.0001f)
            {
                float scale = FootprintWorld / horizontal;
                model.transform.localScale = Vector3.one * scale;
                bounds = MeasureBounds(model.transform, parent);
            }
            else
            {
                Debug.LogWarning($"[建筑美术] 模型「{model.name}」的包围盒宽深为 0，跳过自动缩放。");
            }

            // 水平居中、底部贴地
            Vector3 offset = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            model.transform.localPosition += offset;
            bounds.center += offset;

            return model;
        }

        private static void ApplyMaterial(GameObject model, Material material)
        {
            if (material == null)
            {
                return;
            }

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];

                if (renderer == null ||
                    renderer is ParticleSystemRenderer ||
                    renderer is TrailRenderer ||
                    renderer is LineRenderer)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;

                if (materials.Length == 0)
                {
                    renderer.sharedMaterial = material;
                    continue;
                }

                for (int k = 0; k < materials.Length; k++)
                {
                    materials[k] = material;
                }

                renderer.sharedMaterials = materials;
            }
        }

        /// <summary>
        /// 用 Renderer.localBounds 的八个角点换算到 space 空间，
        /// 不依赖 Renderer.bounds 的延迟刷新，预制体预览场景里也稳定。
        /// </summary>
        private static Bounds MeasureBounds(Transform target, Transform space)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds result = default;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];

                if (renderer == null ||
                    renderer is ParticleSystemRenderer ||
                    renderer is TrailRenderer ||
                    renderer is LineRenderer)
                {
                    continue;
                }

                Bounds local = renderer.localBounds;

                for (int x = 0; x < 2; x++)
                {
                    for (int y = 0; y < 2; y++)
                    {
                        for (int z = 0; z < 2; z++)
                        {
                            Vector3 corner = new Vector3(
                                x == 0 ? local.min.x : local.max.x,
                                y == 0 ? local.min.y : local.max.y,
                                z == 0 ? local.min.z : local.max.z);

                            Vector3 point = space.InverseTransformPoint(renderer.transform.TransformPoint(corner));

                            if (!hasBounds)
                            {
                                result = new Bounds(point, Vector3.zero);
                                hasBounds = true;
                            }
                            else
                            {
                                result.Encapsulate(point);
                            }
                        }
                    }
                }
            }

            if (!hasBounds)
            {
                Debug.LogWarning($"[建筑美术] 「{target.name}」下找不到可测量尺寸的 Renderer。");
                result = new Bounds(Vector3.zero, Vector3.zero);
            }

            return result;
        }

        private static void ResetTransform(Transform transform)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        // ---------------- 数据资产 ----------------

        private static bool AssignToDataAsset(
            Mapping mapping, BuildingData template, GameObject buildingPrefab, GameObject blueprintPrefab)
        {
            BuildingData data = AssetDatabase.LoadAssetAtPath<BuildingData>(mapping.DataPath);

            if (data == null)
            {
                if (!mapping.MayCreateDataAsset)
                {
                    Debug.LogError($"[建筑美术] 找不到建筑数据资产：{mapping.DataPath}，跳过「{mapping.DisplayName}」。");
                    return false;
                }

                data = ScriptableObject.CreateInstance<BuildingData>();
                AssetDatabase.CreateAsset(data, mapping.DataPath);

                SerializedObject created = new SerializedObject(data);
                created.FindProperty("buildingName").stringValue = mapping.DisplayName;
                created.FindProperty("buildingType").enumValueIndex = (int)BuildingType.Museum;
                created.FindProperty("footprintCells").vector2IntValue = ComputeFootprintCells(buildingPrefab);
                created.FindProperty("buildLimit").intValue = 0;
                created.FindProperty("upgradeTable").objectReferenceValue = template.UpgradeTable;
                CopyRequiredMaterials(created, template);

                // 与存档向导相同的规则：Save Id = 资产 GUID
                created.FindProperty("saveId").stringValue = AssetDatabase.AssetPathToGUID(mapping.DataPath);
                created.ApplyModifiedPropertiesWithoutUndo();

                Debug.Log($"[建筑美术] 新建建筑数据资产：{mapping.DataPath}");
            }

            SerializedObject so = new SerializedObject(data);
            SetObject(so, "buildingPrefab", buildingPrefab);
            SetObject(so, "blueprintPrefab", blueprintPrefab);

            // 占地格数按**这件模型自己的宽深**算，而不是所有建筑统一 6×6：
            // 管理器会按占地把模型拉开，正方形占地把非正方形模型压变形。
            SerializedProperty footprintCells = so.FindProperty("footprintCells");

            if (footprintCells != null)
            {
                footprintCells.vector2IntValue = ComputeFootprintCells(buildingPrefab);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);

            return true;
        }

        /// <summary>
        /// 用建筑预制体上的 BoxCollider（= 归一化后的模型包围盒）算占地格数：
        /// 宽深 ÷ 单格 2，四舍五入到最近的整数格，至少 1 格。
        /// 例：12×8.17 → 6×4；8.60×12 → 4×6；12×10.37 → 6×5。
        /// </summary>
        private static Vector2Int ComputeFootprintCells(GameObject buildingPrefab)
        {
            if (buildingPrefab == null)
            {
                return ScaledFootprintCells;
            }

            BoxCollider collider = buildingPrefab.GetComponent<BoxCollider>();

            if (collider == null)
            {
                return ScaledFootprintCells;
            }

            int x = Mathf.Max(1, Mathf.RoundToInt(collider.size.x / CellSize));
            int z = Mathf.Max(1, Mathf.RoundToInt(collider.size.z / CellSize));

            return new Vector2Int(x, z);
        }

        private static void CopyRequiredMaterials(SerializedObject target, BuildingData template)
        {
            SerializedProperty source = new SerializedObject(template).FindProperty("requiredMaterials");
            SerializedProperty destination = target.FindProperty("requiredMaterials");

            if (source == null || destination == null)
            {
                Debug.LogWarning("[建筑美术] 复制材料需求失败：找不到 requiredMaterials 字段。");
                return;
            }

            destination.arraySize = source.arraySize;

            for (int i = 0; i < source.arraySize; i++)
            {
                SerializedProperty sourceItem = source.GetArrayElementAtIndex(i);
                SerializedProperty destinationItem = destination.GetArrayElementAtIndex(i);

                destinationItem.FindPropertyRelative("material").objectReferenceValue =
                    sourceItem.FindPropertyRelative("material").objectReferenceValue;
                destinationItem.FindPropertyRelative("amount").intValue =
                    sourceItem.FindPropertyRelative("amount").intValue;
            }
        }

        // ---------------- 工具 ----------------

        private static bool TryResolveModel(string modelName, out string modelPath, out string modelFolder)
        {
            for (int i = 0; i < ModelRootCandidates.Length; i++)
            {
                string root = ModelRootCandidates[i];
                string candidate = root + "/" + modelName + "/" + modelName + ".fbx";

                if (AssetDatabase.LoadAssetAtPath<GameObject>(candidate) != null)
                {
                    modelPath = candidate;
                    modelFolder = root + "/" + modelName;
                    return true;
                }
            }

            modelPath = null;
            modelFolder = null;
            return false;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static void SetObject(SerializedObject so, string fieldName, Object value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.objectReferenceValue = value;
            }
            else
            {
                Debug.LogWarning($"[建筑美术] {so.targetObject.GetType().Name} 上找不到字段 {fieldName}，该项跳过。");
            }
        }

        private static void Fail(string message)
        {
            EditorUtility.DisplayDialog("建筑美术接入未执行", message, "知道了");
            Debug.LogError("[建筑美术] " + message);
        }
    }
}
