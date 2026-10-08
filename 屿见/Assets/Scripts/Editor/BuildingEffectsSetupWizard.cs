using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Yujian.Building;
using Yujian.Building.Effects;
using Yujian.Core;
using Yujian.Mining;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 阶段 5 建筑效果的一键配置工具。
    ///
    /// 做三件事：
    ///   1. 往建筑预制体上挂 <see cref="OrganMuseumAutoAttack"/>，并把 Ore Layers 勾成 Ore 层；
    ///   2. 校验场景依赖（GameManager / DamageSystem / PlayerCurrency）齐不齐——
    ///      效果是运行时从 GameManager 拿服务的，这几样缺一个效果就起不来；
    ///   3. 校验场景里的矿石是不是真的在 Ore 层上（不在就永远打不到，而且不会有任何报错）。
    ///
    /// 与阶段 3 / 4 的向导一样：直接改 .unity / .prefab 会让 Unity 重载场景并丢改动，
    /// 所以统一让 Unity 自己写（见 开发进度.md 踩坑 2）。
    ///
    /// **本工具幂等，且不会覆盖已经调过的数值**：重复运行只补缺的东西。
    /// 入口：菜单栏 → 屿见 → 配置建筑效果（阶段 5）
    /// </summary>
    public static class BuildingEffectsSetupWizard
    {
        private const string BuildingPrefabPath = "Assets/Prefabs/建筑/建筑.prefab";
        private const string OreLayerName = "Ore";

        private const string OreLayersField = "oreLayers";
        private const string AttackIntervalField = "attackInterval";

        [MenuItem("屿见/配置建筑效果（阶段 5）")]
        public static void Configure()
        {
            int oreLayer = LayerMask.NameToLayer(OreLayerName);

            if (oreLayer < 0)
            {
                Fail($"工程里没有名为 {OreLayerName} 的图层。\n\n" +
                     "请到 Project Settings → Tags and Layers 新建一个 Ore 层，再运行本命令。");
                return;
            }

            if (!ValidateSceneServices())
            {
                return;
            }

            bool prefabChanged = ConfigureBuildingPrefab(oreLayer, out string prefabReport);

            if (prefabChanged)
            {
                AssetDatabase.SaveAssets();
            }

            string oreLayerReport = ReportOffLayerOres(oreLayer);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildingPrefabPath);

            if (prefab != null)
            {
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
            }

            Debug.Log("[BuildingEffectsSetupWizard] 配置完成：\n" +
                      $"  · {BuildingPrefabPath}：{prefabReport}\n" +
                      $"  · 场景依赖：GameManager / DamageSystem / PlayerCurrency 齐全\n" +
                      $"  · 场景矿石图层：{oreLayerReport}\n\n" +
                      "接下来：进 Play Mode，先挖矿攒钱买砖，建成风琴博物馆，" +
                      "Console 会每 1 秒出现一条自动攻击日志（不限距离，打场景里现存的矿石）。\n" +
                      "数值都在建筑预制体的 OrganMuseumAutoAttack 上，直接改 Inspector 即可。");
        }

        /// <summary>
        /// 往建筑预制体上挂效果组件、勾好 Ore 层。
        /// </summary>
        /// <returns>预制体是否真的被改过（没改就不存盘，免得白白产生一次文件差异）。</returns>
        private static bool ConfigureBuildingPrefab(int oreLayer, out string report)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(BuildingPrefabPath);

            if (root == null)
            {
                report = "加载失败，未做任何改动";
                Debug.LogError($"[BuildingEffectsSetupWizard] 加载不了建筑预制体：{BuildingPrefabPath}");
                return false;
            }

            try
            {
                bool changed = false;
                StringBuilder builder = new StringBuilder();

                OrganMuseumAutoAttack effect = root.GetComponent<OrganMuseumAutoAttack>();

                if (effect == null)
                {
                    effect = root.AddComponent<OrganMuseumAutoAttack>();
                    changed = true;
                    builder.Append("新增 OrganMuseumAutoAttack");
                }
                else
                {
                    builder.Append("已有 OrganMuseumAutoAttack");
                }

                if (root.GetComponent<BuildingLayerStack>() == null)
                {
                    Debug.LogWarning($"[BuildingEffectsSetupWizard] 建筑预制体上没有 BuildingLayerStack，" +
                                     "楼层不会显示颜色。可运行菜单「屿见/配置建造系统（阶段 4）」修复。");
                }

                if (root.GetComponent<Collider>() == null)
                {
                    Debug.LogWarning($"[BuildingEffectsSetupWizard] 建筑预制体根物体上没有 Collider，" +
                                     "别的建筑检测不到它、会允许重叠放置。" +
                                     "可运行菜单「屿见/配置建造系统（阶段 4）」修复。");
                }

                SerializedObject so = new SerializedObject(effect);
                SerializedProperty oreLayers = so.FindProperty(OreLayersField);

                if (oreLayers == null)
                {
                    builder.Append($"，⚠ 找不到字段 {OreLayersField}，图层未设置");
                    Debug.LogError($"[BuildingEffectsSetupWizard] OrganMuseumAutoAttack 上找不到字段 " +
                                   $"{OreLayersField}，Ore 层没有被勾上。");
                }
                else if ((oreLayers.intValue & (1 << oreLayer)) != 0)
                {
                    builder.Append($"，{OreLayerName} 层已勾选（未改动）");
                }
                else
                {
                    bool wasEmpty = oreLayers.intValue == 0;
                    oreLayers.intValue |= 1 << oreLayer;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                    builder.Append(wasEmpty
                        ? $"，Ore Layers 勾上 {OreLayerName} 层"
                        : $"，Ore Layers 补上缺失的 {OreLayerName} 层");
                }

                // 读出最终数值写进日志：验收时要能一眼确认间隔是 1 秒
                builder.Append($"，攻击间隔 {ReadFloat(so, AttackIntervalField)} 秒" +
                               "，无射程限制（打场景里现存的矿石）");

                if (!changed)
                {
                    report = builder.ToString();
                    return false;
                }

                PrefabUtility.SaveAsPrefabAsset(root, BuildingPrefabPath);
                report = builder.ToString();
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// 校验效果运行时要用到的三个服务。效果拿不到它们就不会启动，所以这里直接拦下来。
        /// </summary>
        private static bool ValidateSceneServices()
        {
            GameManager manager = Object.FindFirstObjectByType<GameManager>();

            if (manager == null)
            {
                Fail("场景里找不到 GameManager。\n\n" +
                     "建筑效果是在运行时从 GameManager 取 DamageSystem 与 PlayerCurrency 的" +
                     "（建筑预制体不可能直接引用场景里的物体），没有它效果一律不启动。");
                return false;
            }

            bool ok = true;

            if (manager.Damage == null)
            {
                Debug.LogError("[BuildingEffectsSetupWizard] GameManager 的 DamageSystem 字段是空的，" +
                               "建筑拿不到伤害公式、不会攻击。请在 GameManager 上补上。");
                ok = false;
            }
            else
            {
                SerializedObject damageSo = new SerializedObject(manager.Damage);

                if (damageSo.FindProperty("playerStats")?.objectReferenceValue == null)
                {
                    Debug.LogError($"[BuildingEffectsSetupWizard] DamageSystem「{manager.Damage.name}」的 " +
                                   "PlayerStats 字段是空的，伤害会算成 0、建筑与玩家都打不动矿石。" +
                                   "请在 DamageSystem 上补上 PlayerStats。");
                    ok = false;
                }
            }

            if (manager.Currency == null)
            {
                Debug.LogError("[BuildingEffectsSetupWizard] GameManager 的 PlayerCurrency 字段是空的，" +
                               "建筑击碎矿石时发不出奖励。请在 GameManager 上补上。");
                ok = false;
            }

            if (!ok)
            {
                Fail("场景依赖不齐，配置未执行。具体缺哪一项见 Console 的报错。");
            }

            return ok;
        }

        /// <summary>
        /// 数一遍场景里的矿石有没有站在 Ore 层上。不在的话效果一个都找不到，而且运行时不会有任何报错。
        /// </summary>
        private static string ReportOffLayerOres(int oreLayer)
        {
            Ore[] ores = Object.FindObjectsByType<Ore>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (ores.Length == 0)
            {
                Debug.LogWarning("[BuildingEffectsSetupWizard] 场景里一个 Ore 都没有，没有东西可以被自动攻击。");
                return "场景里没有矿石";
            }

            List<string> offLayer = new List<string>();

            for (int i = 0; i < ores.Length; i++)
            {
                if (ores[i].gameObject.layer != oreLayer)
                {
                    offLayer.Add(ores[i].name);
                }
            }

            if (offLayer.Count == 0)
            {
                return $"{ores.Length} 个矿石全部在 {OreLayerName} 层";
            }

            string names = string.Join("、", offLayer);
            Debug.LogError($"[BuildingEffectsSetupWizard] 有 {offLayer.Count} 个矿石不在 {OreLayerName} 层：" +
                           $"{names}。建筑永远不会攻击它们，而且运行时不会有任何报错" +
                           "（图层不匹配时物理查询就是静默查不到）。请把这些物体的 Layer 改成 Ore。");

            return $"{ores.Length} 个矿石里有 {offLayer.Count} 个不在 {OreLayerName} 层（{names}），已在 Console 报错";
        }

        private static float ReadFloat(SerializedObject so, string fieldName)
        {
            SerializedProperty property = so.FindProperty(fieldName);
            return property != null ? property.floatValue : 0f;
        }

        private static void Fail(string message)
        {
            EditorUtility.DisplayDialog("建筑效果配置未执行", message, "知道了");
            Debug.LogError("[BuildingEffectsSetupWizard] " + message);
        }
    }
}
