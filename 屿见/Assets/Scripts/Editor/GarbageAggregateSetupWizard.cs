using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Mining;
using Yujian.Player;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 一键把场景里的矿石改造成「垃圾聚合体」。
    /// 入口：菜单栏 → 屿见 → 清洁 → 配置垃圾聚合体
    ///
    /// 做七件事：
    ///   1. 生成「垃圾」与「垃圾精灵」预制体（Cube 占位，运行时换成程序生成的不规则形状，
    ///      后续换美术只替换预制体）；
    ///   2. 复用场景里唯一的 Ore，把 Visual 上的旧 Cube 换成运行时生成的外壳；
    ///   3. 挂 <see cref="GarbageAggregateVisual"/> 并接好预制体引用；
    ///   4. 挂 <see cref="GarbageDropper"/>（掉血分档掉落）与 <see cref="GarbageCollector"/>（排队收集）；
    ///   5. 生成尾迹材质；
    ///   6. 把点击碰撞体放大到能覆盖外壳；
    ///   7. 把场景里现存的「金币」文案迁移成「垃圾」。
    ///
    /// 幂等：重复运行不会重复挂组件，也不会生成重复模型。
    /// </summary>
    public static class GarbageAggregateSetupWizard
    {
        private const string PrefabFolder = "Assets/Prefabs/Mining";
        private const string MaterialFolder = "Assets/Materials/Mining";
        private const string PiecePrefabPath = PrefabFolder + "/垃圾.prefab";
        private const string SpritePrefabPath = PrefabFolder + "/垃圾精灵.prefab";
        private const string WhiteMaterialPath = MaterialFolder + "/Garbage_White.mat";
        private const string PurpleMaterialPath = MaterialFolder + "/Garbage_Purple.mat";
        private const string TrailMaterialPath = MaterialFolder + "/Garbage_Trail.mat";
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("屿见/清洁/配置垃圾聚合体", false, 345)]
        public static void Configure()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("垃圾聚合体", "请先退出 Play Mode，再运行本命令。", "知道了");
                return;
            }

            EnsureTargetSceneOpen();

            Ore ore = Object.FindFirstObjectByType<Ore>(FindObjectsInactive.Include);

            if (ore == null)
            {
                Fail("场景里找不到 Ore（矿石 / 垃圾聚合体）。请先完成挖矿系统配置。");
                return;
            }

            EnsureFolder(PrefabFolder);
            EnsureFolder(MaterialFolder);

            Material whiteMaterial = EnsureMaterial(WhiteMaterialPath, Color.white);
            Material purpleMaterial = EnsureMaterial(
                PurpleMaterialPath, new Color(0.55f, 0.2f, 0.85f, 1f));

            if (whiteMaterial == null || purpleMaterial == null)
            {
                Fail("材质创建失败，请检查 Console。");
                return;
            }

            GameObject piecePrefab = EnsurePrefab(PiecePrefabPath, "垃圾", whiteMaterial);
            GameObject spritePrefab = EnsurePrefab(SpritePrefabPath, "垃圾精灵", purpleMaterial);

            if (piecePrefab == null || spritePrefab == null)
            {
                Fail("预制体创建失败，请检查 Console。");
                return;
            }

            Transform visual = EnsureVisualRoot(ore);

            if (visual == null)
            {
                Fail("无法创建或取得 Ore 的 Visual 节点。");
                return;
            }

            RemoveBuiltInCube(visual);

            GarbageAggregateVisual view = visual.GetComponent<GarbageAggregateVisual>();

            if (view == null)
            {
                view = Undo.AddComponent<GarbageAggregateVisual>(visual.gameObject);
            }

            view.ConfigureForSetup(piecePrefab, spritePrefab);
            EditorUtility.SetDirty(view);

            if (!EnsureGarbageSystems(ore, view))
            {
                return;
            }

            // 新模型固定为「白壳 + 紫色精灵」，不再按矿石种类整体染色
            OreTypeTag typeTag = ore.GetComponent<OreTypeTag>();

            if (typeTag != null)
            {
                typeTag.Configure(typeTag.Type, false);
                EditorUtility.SetDirty(typeTag);
            }

            EnsureClickCollider(ore);

            // 垃圾已经是货币：把场景里现存的「金币」字样一并换掉
            int migratedLabels = MigrateCoinLabels();

            EditorSceneManager.MarkSceneDirty(ore.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            Debug.Log($"[GarbageAggregateSetupWizard] 已配置垃圾聚合体：{ore.name} / Visual {visual.name}\n" +
                      $"  · 垃圾预制体：{PiecePrefabPath}\n" +
                      $"  · 垃圾精灵预制体：{SpritePrefabPath}\n" +
                      $"  · 尾迹材质：{TrailMaterialPath}\n" +
                      "  · 已挂 GarbageDropper（每 20% 生命掉一档）与 GarbageCollector（每 0.5 秒收一块）\n" +
                      $"  · 已把 {migratedLabels} 处「金币」文案改成「垃圾」\n" +
                      "进 Play 后外壳与垃圾精灵才会生成。");
        }

        /// <summary>
        /// 批处理（-executeMethod）时没有打开场景，自动打开主场景；
        /// 正常编辑器里不替用户切换场景，找不到 Ore 就交给后面的报错提示。
        /// </summary>
        private static void EnsureTargetSceneOpen()
        {
            if (Object.FindFirstObjectByType<Ore>(FindObjectsInactive.Include) != null)
            {
                return;
            }

            if (!Application.isBatchMode || !File.Exists(SampleScenePath))
            {
                return;
            }

            Debug.Log($"[GarbageAggregateSetupWizard] 批处理模式：打开 {SampleScenePath}。");
            EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
        }

        /// <summary>
        /// 把场景里所有「金币」文案换成「垃圾」：
        /// 一是场景里已经保存的 Text，二是 CoinHudUI 的 format 格式串。
        /// 只改显示文字，不动 PlayerCurrency / money 这些内部命名。
        /// </summary>
        private static int MigrateCoinLabels()
        {
            int migrated = 0;

            Text[] texts = Object.FindObjectsByType<Text>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < texts.Length; i++)
            {
                Text text = texts[i];

                if (text == null || string.IsNullOrEmpty(text.text) || !text.text.Contains("金币"))
                {
                    continue;
                }

                Undo.RecordObject(text, "Rename Coin To Garbage");
                text.text = text.text.Replace("金币", "垃圾");
                EditorUtility.SetDirty(text);
                migrated++;
            }

            CoinHudUI[] coinHuds = Object.FindObjectsByType<CoinHudUI>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < coinHuds.Length; i++)
            {
                CoinHudUI hud = coinHuds[i];

                if (hud == null)
                {
                    continue;
                }

                SerializedObject serializedHud = new SerializedObject(hud);
                SerializedProperty format = serializedHud.FindProperty("format");

                if (format == null || string.IsNullOrEmpty(format.stringValue) ||
                    !format.stringValue.Contains("金币"))
                {
                    continue;
                }

                serializedHud.Update();
                format.stringValue = format.stringValue.Replace("金币", "垃圾");
                serializedHud.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(hud);
                migrated++;
            }

            return migrated;
        }

        /// <summary>
        /// 挂上掉落器与收集器，并把引用接好。返回 false 时调用方应中止（已弹过错误）。
        /// </summary>
        private static bool EnsureGarbageSystems(Ore ore, GarbageAggregateVisual view)
        {
            MiningHudUI hud = Object.FindFirstObjectByType<MiningHudUI>(FindObjectsInactive.Include);
            PlayerCurrency currency = Object.FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);

            if (hud == null)
            {
                Fail("场景里找不到 MiningHudUI（挖矿界面）。请先运行「屿见/挖矿/配置挖矿界面」。");
                return false;
            }

            if (currency == null)
            {
                Fail("场景里找不到 PlayerCurrency（玩家货币）。请先完成存档 / 货币系统配置。");
                return false;
            }

            Material trailMaterial = EnsureTrailMaterial();

            if (trailMaterial == null)
            {
                Fail("尾迹材质创建失败，请检查 Console。");
                return false;
            }

            GarbageCollector collector = hud.GetComponent<GarbageCollector>();

            if (collector == null)
            {
                collector = Undo.AddComponent<GarbageCollector>(hud.gameObject);
            }

            collector.ConfigureForSetup(hud, currency, Camera.main, trailMaterial);
            EditorUtility.SetDirty(collector);

            GarbageDropper dropper = ore.GetComponent<GarbageDropper>();

            if (dropper == null)
            {
                dropper = Undo.AddComponent<GarbageDropper>(ore.gameObject);
            }

            dropper.ConfigureForSetup(ore, view, collector);
            EditorUtility.SetDirty(dropper);

            return true;
        }

        private static Transform EnsureVisualRoot(Ore ore)
        {
            SerializedObject serializedOre = new SerializedObject(ore);
            SerializedProperty visualProperty = serializedOre.FindProperty("visualRoot");
            Transform visual = visualProperty != null
                ? visualProperty.objectReferenceValue as Transform
                : null;

            if (visual != null)
            {
                return visual;
            }

            GameObject go = new GameObject("Visual");
            Undo.RegisterCreatedObjectUndo(go, "Create Garbage Visual");
            go.transform.SetParent(ore.transform, false);
            visual = go.transform;

            if (visualProperty != null)
            {
                visualProperty.objectReferenceValue = visual;
                serializedOre.ApplyModifiedPropertiesWithoutUndo();
            }

            return visual;
        }

        /// <summary>
        /// 旧的矿石模型是 Visual 上的一个 Cube。外壳改由子物体生成后，
        /// 这个 Cube 会包住所有东西，必须去掉。
        /// </summary>
        private static void RemoveBuiltInCube(Transform visual)
        {
            MeshFilter filter = visual.GetComponent<MeshFilter>();

            if (filter != null)
            {
                Undo.DestroyObjectImmediate(filter);
            }

            MeshRenderer renderer = visual.GetComponent<MeshRenderer>();

            if (renderer != null)
            {
                Undo.DestroyObjectImmediate(renderer);
            }
        }

        /// <summary>
        /// 外壳比原来的 1×1×1 大，点击碰撞体也要跟着放大；
        /// 已经手动调大过就不覆盖。
        /// </summary>
        private static void EnsureClickCollider(Ore ore)
        {
            BoxCollider box = ore.GetComponent<BoxCollider>();

            if (box == null)
            {
                return;
            }

            if (box.size.x >= 1.5f && box.size.z >= 1.5f)
            {
                return;
            }

            Undo.RecordObject(box, "Resize Garbage Aggregate Collider");
            box.center = new Vector3(0f, 0.25f, 0f);
            box.size = new Vector3(1.8f, 1.5f, 1.8f);
            EditorUtility.SetDirty(box);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path);
            string folderName = Path.GetFileName(path);

            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(folderName))
            {
                return;
            }

            parent = parent.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        private static Material EnsureMaterial(string path, Color color)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");

                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                if (shader == null)
                {
                    Debug.LogError($"[GarbageAggregateSetupWizard] 找不到可用的 Lit / Standard 着色器，" +
                                   $"无法创建 {path}。");
                    return null;
                }

                material = new Material(shader)
                {
                    name = Path.GetFileNameWithoutExtension(path),
                };
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_Color"))
            {
                material.color = color;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.15f);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", 0.15f);
            }

            // 清洁区比较暗，给外壳与精灵一点自发光，保证在阴影里也看得见。
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(
                    color.r * 0.30f, color.g * 0.30f, color.b * 0.30f, 1f));
                material.globalIlluminationFlags =
                    MaterialGlobalIlluminationFlags.BakedEmissive;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// 尾迹需要支持顶点色和透明，不能用 URP Lit。
        /// Sprites/Default 在 URP 下可用，找不到时退回粒子 Unlit。
        /// </summary>
        private static Material EnsureTrailMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(TrailMaterialPath);

            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find("Sprites/Default");

            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            }

            if (shader == null)
            {
                Debug.LogError($"[GarbageAggregateSetupWizard] 找不到可用的尾迹着色器，" +
                               $"无法创建 {TrailMaterialPath}。");
                return null;
            }

            material = new Material(shader)
            {
                name = "Garbage_Trail",
            };

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.white);
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }

            AssetDatabase.CreateAsset(material, TrailMaterialPath);
            return material;
        }

        private static GameObject EnsurePrefab(string path, string prefabName, Material material)
        {
            if (material == null)
            {
                return null;
            }

            // 已有预制体就原样保留，方便后续直接替换成美术模型
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing != null)
            {
                return existing;
            }

            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = prefabName;

            // 外壳块 / 精灵不参与点击拾取，点击由聚合体根节点的碰撞体负责
            Collider collider = root.GetComponent<Collider>();

            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            Renderer renderer = root.GetComponent<Renderer>();

            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void Fail(string message)
        {
            Debug.LogError("[GarbageAggregateSetupWizard] " + message);
            EditorUtility.DisplayDialog("垃圾聚合体未配置", message, "知道了");
        }
    }
}
