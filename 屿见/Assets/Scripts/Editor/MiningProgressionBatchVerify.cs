using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Building;
using Yujian.Building.Effects;
using Yujian.Mining;
using Yujian.Player;
using Yujian.Save;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 临时批处理验证（D1 / D7 / D3 / D17）。
    /// 只在无头模式下跑一次，验证完删除本文件。
    /// </summary>
    public static class MiningProgressionBatchVerify
    {
        private static readonly List<string> Failures = new List<string>();
        private static int checks;

        public static void RunAll()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");

            BuildingDamageBonusSetupWizard.Configure();
            MiningLightToolSetupWizard.Configure();
            CritPointSetupWizard.Configure();

            VerifySceneWiring();
            VerifyBuildingBonus();
            VerifyToolCurve();
            VerifyOreCurve();
            VerifyDamageCalculation();
            VerifyCritPoints();
            VerifyLightTool();
            VerifySaveWiring();

            Debug.Log($"[BatchVerify] 完成：{checks - Failures.Count}/{checks} 通过。");

            if (Failures.Count > 0)
            {
                for (int i = 0; i < Failures.Count; i++)
                {
                    Debug.LogError("[BatchVerify] FAIL: " + Failures[i]);
                }

                Debug.LogError($"[BatchVerify] 失败 {Failures.Count} 项。");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[BatchVerify] 全部 PASS。");
            EditorApplication.Exit(0);
        }

        private static void VerifySceneWiring()
        {
            BuildingDamageBonus bonus = Object.FindFirstObjectByType<BuildingDamageBonus>(
                FindObjectsInactive.Include);
            Check(bonus != null, "场景里应有 BuildingDamageBonus");
            Check(Get<BuildingManager>(bonus, "buildingManager") != null,
                "BuildingDamageBonus.buildingManager 已接线");
            Check(Get<PlayerStats>(bonus, "playerStats") != null,
                "BuildingDamageBonus.playerStats 已接线");

            MiningLightTool lightTool = Object.FindFirstObjectByType<MiningLightTool>(
                FindObjectsInactive.Include);
            Check(lightTool != null, "场景里应有 MiningLightTool");

            CritPointSystem system = Object.FindFirstObjectByType<CritPointSystem>(
                FindObjectsInactive.Include);
            Check(system != null, "场景里应有 CritPointSystem");
            Check(Get<Ore>(system, "ore") != null, "CritPointSystem.ore 已接线");
            Check(Get<MiningLightTool>(system, "lightTool") != null, "CritPointSystem.lightTool 已接线");

            MiningInput input = Object.FindFirstObjectByType<MiningInput>(FindObjectsInactive.Include);
            Check(input != null, "场景里应有 MiningInput");
            Check(Get<CritPointSystem>(input, "critPointSystem") != null, "MiningInput.critPointSystem 已接线");

            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            Transform card = canvas != null ? canvas.transform.Find("ShopPanel/MiningLightToolCard") : null;
            Check(card != null, "商店面板里应有照明工具购买卡");
            Check(card != null && card.GetComponent<MiningLightToolUI>() != null,
                "照明工具卡上应有 MiningLightToolUI");

            Check(FindAllCount<BuildingDamageBonus>() == 1, "BuildingDamageBonus 只能有一份");
            Check(FindAllCount<MiningLightTool>() == 1, "MiningLightTool 只能有一份");
            Check(FindAllCount<CritPointSystem>() == 1, "CritPointSystem 只能有一份");
        }

        private static void VerifyBuildingBonus()
        {
            string[] paths =
            {
                "Assets/Data/Buildings/Building_风琴博物馆.asset",
                "Assets/Data/Buildings/Building_八卦楼.asset",
                "Assets/Data/Buildings/Building_菽庄花园.asset",
                "Assets/Data/Buildings/Building_日光岩.asset",
            };

            for (int i = 0; i < paths.Length; i++)
            {
                BuildingData data = AssetDatabase.LoadAssetAtPath<BuildingData>(paths[i]);
                Check(data != null, $"能加载建筑资产 {paths[i]}");

                if (data == null)
                {
                    continue;
                }

                float expected = 0f;

                for (int j = 0; j < data.RequiredMaterials.Count; j++)
                {
                    BuildingMaterialRequirement requirement = data.RequiredMaterials[j];

                    if (requirement.IsValid)
                    {
                        expected += requirement.Material.DamageMultiplierBonus * requirement.Amount;
                    }
                }

                float actual = BuildingDamageBonus.CalculateBonus(data);
                Check(Mathf.Abs(actual - expected) < 0.0001f,
                    $"{data.BuildingName} 加成 {actual} 与需求反推 {expected} 一致");
            }

            PlayerStats stats = Object.FindFirstObjectByType<PlayerStats>(FindObjectsInactive.Include);
            Check(stats != null, "场景里应有 PlayerStats");

            if (stats != null)
            {
                float baseMultiplier = stats.BaseDamageMultiplier;
                stats.SetBonusDamageMultiplier(0.3f);
                Check(Mathf.Abs(stats.DamageMultiplier - (baseMultiplier + 0.3f)) < 0.0001f,
                    "PlayerStats 附加倍率生效");
                stats.SetBonusDamageMultiplier(0f);
                Check(Mathf.Abs(stats.DamageMultiplier - baseMultiplier) < 0.0001f,
                    "PlayerStats 附加倍率可清零");
            }
        }

        private static void VerifyToolCurve()
        {
            MiningToolData tool = AssetDatabase.LoadAssetAtPath<MiningToolData>(
                "Assets/Data/Mining/Tools/MiningTool_石头.asset");
            Check(tool != null, "能加载挖矿工具资产");

            if (tool == null)
            {
                return;
            }

            Check(Mathf.Abs(tool.GetAttackAtLevel(1) - 1f) < 0.0001f, "工具 1 级攻击力 = 1");
            Check(Mathf.Abs(tool.GetAttackAtLevel(2) - 1.35f) < 0.0001f, "工具 2 级攻击力 = 1.35");
            Check(Mathf.Abs(tool.GetAttackAtLevel(3) - 1.8225f) < 0.001f, "工具 3 级攻击力 = 1.8225");
            Check(tool.GetUpgradeCost(1) == 15, "工具 1→2 升级费 = 15");
            Check(tool.GetUpgradeCost(2) == 20, "工具 2→3 升级费 = 20");
            Check(tool.GetUpgradeCost(3) == 27, "工具 3→4 升级费 = 27");
        }

        private static void VerifyOreCurve()
        {
            OreLevelTable table = AssetDatabase.LoadAssetAtPath<OreLevelTable>(
                "Assets/Data/Mining/OreLevelTable.asset");
            Check(table != null, "能加载矿石等级表");

            if (table == null)
            {
                return;
            }

            Check(Mathf.Abs(table.GetHealthMultiplier(1) - 1f) < 0.0001f, "第 1 层血量为基准");
            Check(Mathf.Abs(table.GetHealthMultiplier(2) - 2.2f) < 0.0001f, "第 2 层血量 ×2.2");
            Check(Mathf.Abs(table.GetHealthMultiplier(3) - 4.84f) < 0.001f, "第 3 层血量 ×4.84");
            Check(table.GetRewardAtTier(10, 1) == 10, "第 1 层奖励 10");
            Check(table.GetRewardAtTier(10, 2) == 22, "第 2 层奖励 22");
            Check(table.GetRewardAtTier(10, 3) == 48, "第 3 层奖励 48");

            OreGroup group = AssetDatabase.LoadAssetAtPath<OreGroup>(
                "Assets/Data/Mining/OreGroup_Lv1.asset");
            Check(group != null && group.Entries.Count > 0 && Mathf.Abs(group.Entries[0].MaxHealth - 5f) < 0.0001f,
                "第 1 层基准血量已改成 5（点击 4~5 次）");
        }

        /// <summary>
        /// 真实调用一次伤害计算，专门防「无参重载与 out 重载互相递归」这类
        /// 编译器查不出来、一进 Play 就爆栈的回归。
        /// </summary>
        private static void VerifyDamageCalculation()
        {
            DamageSystem system = Object.FindFirstObjectByType<DamageSystem>(FindObjectsInactive.Include);
            MiningToolProgress progress = Object.FindFirstObjectByType<MiningToolProgress>(
                FindObjectsInactive.Include);
            PlayerStats stats = Object.FindFirstObjectByType<PlayerStats>(FindObjectsInactive.Include);

            Check(system != null, "场景里应有 DamageSystem");
            Check(progress != null, "场景里应有 MiningToolProgress");

            if (system == null || progress == null || stats == null)
            {
                return;
            }

            float normal = system.CalculatePlayerDamage();
            float expected = progress.CurrentAttack * stats.DamageMultiplier;
            Check(normal > 0f, $"普通伤害大于 0（实际 {normal:0.##}）");
            Check(Mathf.Abs(normal - expected) < 0.0001f,
                $"普通伤害 = 当前攻击 {progress.CurrentAttack:0.##} × 倍率 {stats.DamageMultiplier:0.##}");

            float crit = system.CalculateCritPointDamage();
            Check(Mathf.Abs(crit - normal * system.CriticalDamageMultiplier) < 0.0001f,
                $"暴击点伤害 = 普通伤害 × {system.CriticalDamageMultiplier:0.##}");

            float withOut = system.CalculatePlayerDamage(out bool isCritical);
            Check(!isCritical, "普通攻击的 isCritical 恒为 false");
            Check(Mathf.Abs(withOut - normal) < 0.0001f, "两个重载算出的伤害一致");
        }

        private static void VerifyCritPoints()
        {
            CritPointSystem system = Object.FindFirstObjectByType<CritPointSystem>(
                FindObjectsInactive.Include);
            Ore ore = Object.FindFirstObjectByType<Ore>(FindObjectsInactive.Include);
            MiningLightTool tool = Object.FindFirstObjectByType<MiningLightTool>(
                FindObjectsInactive.Include);

            if (system == null || ore == null || tool == null)
            {
                Check(false, "暴击点验证缺少组件");
                return;
            }

            tool.SetLevel(0);
            system.ClearAll();

            Check(Mathf.Abs(system.SpawnChancePercent - 8f) < 0.0001f, "暴击点生成概率 = 8%");
            Check(system.Capacity == 1, "未拥有照明工具时上限 = 1");
            Check(Mathf.Abs(system.Lifetime - 2.5f) < 0.0001f, "未拥有照明工具时停留 = 2.5 秒");

            CritPoint first = system.SpawnAt(ore);
            Check(first != null, "能生成第 1 个暴击点");

            if (first != null)
            {
                Collider oreCollider = ore.GetComponentInChildren<Collider>();
                Check(oreCollider != null, "矿石有碰撞体");
                Check(oreCollider == null ||
                      Mathf.Abs(first.transform.position.y - oreCollider.bounds.center.y) < 0.0001f,
                    "暴击点位于穿过矿石中心的水平面");
                Check(Mathf.Abs(first.RemainingLifetime - 2.5f) < 0.2f, "暴击点停留约 2.5 秒");
            }

            Check(system.SpawnAt(ore) == null, "达到上限后不再生成");
            system.Consume(first);
            Check(system.SpawnAt(ore) != null, "点掉后可以立即补位");
            system.ClearAll();

            tool.SetLevel(2);
            Check(system.Capacity == 5, "手电上限 = 5");
            Check(Mathf.Abs(system.Lifetime - 6f) < 0.0001f, "手电停留 = 6 秒");

            int spawned = 0;

            for (int i = 0; i < 6; i++)
            {
                if (system.SpawnAt(ore) != null)
                {
                    spawned++;
                }
            }

            Check(spawned == 5, $"手电最多同时 5 个（实际 {spawned}）");
            system.ClearAll();
            tool.SetLevel(0);
        }

        private static void VerifyLightTool()
        {
            MiningLightTool tool = Object.FindFirstObjectByType<MiningLightTool>(
                FindObjectsInactive.Include);

            if (tool == null)
            {
                Check(false, "照明工具验证缺少组件");
                return;
            }

            tool.SetLevel(0);
            Check(tool.Level == 0, "照明工具初始等级 0");
            Check(tool.CritPointCapacity == 1, "等级 0 上限 1");
            Check(tool.NextUpgradeCost == 40, "等级 0 → 1 价格 40");
            Check(tool.NextLevelName == "火把", "等级 0 的下一级叫火把");
            Check(tool.NextCritPointCapacity == 3, "火把上限 3");

            tool.SetLevel(1);
            Check(tool.NextUpgradeCost == 120, "等级 1 → 2 价格 120");
            Check(tool.NextLevelName == "手电", "等级 1 的下一级叫手电");

            tool.SetLevel(2);
            Check(tool.IsMaxed, "等级 2 已满级");
            Check(tool.NextUpgradeCost == 0, "满级后价格为 0");

            tool.SetLevel(0);
        }

        private static void VerifySaveWiring()
        {
            SaveSystem save = Object.FindFirstObjectByType<SaveSystem>(FindObjectsInactive.Include);
            Check(save != null, "场景里应有 SaveSystem");

            if (save == null)
            {
                return;
            }

            MiningLightToolSaveParticipant participant =
                save.GetComponent<MiningLightToolSaveParticipant>();
            Check(participant != null, "SaveSystem 上应有 MiningLightToolSaveParticipant");

            SerializedObject serialized = new SerializedObject(save);
            SerializedProperty list = serialized.FindProperty("participants");
            bool found = false;

            for (int i = 0; list != null && i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == participant)
                {
                    found = true;
                    break;
                }
            }

            Check(found, "照明工具参与者已加入 SaveSystem.participants");

            MiningLightTool tool = Object.FindFirstObjectByType<MiningLightTool>(
                FindObjectsInactive.Include);
            Check(Get<MiningLightTool>((Object)participant, "lightTool") == tool,
                "照明工具参与者的 lightTool 已接线");
        }

        private static T Get<T>(Object target, string fieldName) where T : Object
        {
            if (target == null)
            {
                return null;
            }

            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            return property != null ? property.objectReferenceValue as T : null;
        }

        private static int FindAllCount<T>() where T : Component
        {
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        }

        private static void Check(bool condition, string message)
        {
            checks++;

            if (condition)
            {
                Debug.Log("[BatchVerify] PASS: " + message);
                return;
            }

            Failures.Add(message);
        }
    }
}
