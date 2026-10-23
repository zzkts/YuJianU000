using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Building;
using Yujian.Mining;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 阶段 8 的一键配置：把场景改成“单一矿位 + 数据表驱动”。
    ///
    /// 做这些事：
    ///   1. 只保留 Ore_01 激活，其余 Ore_02～Ore_05 停用；
    ///   2. 在 Assets/Data/Mining 下创建矿石定义、等级、生成表、建筑等级表；
    ///   3. 给 Ore_01 补齐矿种标签、闪白、碎裂、文化碎片组件；
    ///   4. 在场景里创建 OreSpawner，并接好所有引用。
    ///
    /// 幂等：重复执行不会覆盖你手调过的资产数值。
    /// 不直接改 .unity / .prefab，由 Unity 自己写盘（项目规则 13）。
    ///
    /// 入口：菜单栏 → 屿见 → 配置矿石生成（阶段 8）
    /// </summary>
    public static class OreSpawnSetupWizard
    {
        private const string DataRoot = "Assets/Data/Mining";
        private const string OreFolder = DataRoot + "/Ores";
        private const string LevelFolder = DataRoot + "/Levels";
        private const string SpawnTablePath = DataRoot + "/OreSpawnTable.asset";
        private const string BuildingLevelPath = DataRoot + "/BuildingLevelTable.asset";

        [MenuItem("屿见/配置矿石生成（阶段 8）")]
        public static void Configure()
        {
            Ore[] ores = Object.FindObjectsByType<Ore>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (ores.Length == 0)
            {
                Debug.LogWarning("[矿石生成] 当前打开的场景里没有 Ore。请先打开 SampleScene 再执行本命令。");
                return;
            }

            Ore selectedOre = FindPreferredOre(ores);
            if (selectedOre == null)
            {
                Debug.LogError("[矿石生成] 找不到可用的 Ore。");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("配置矿石生成");

            SetOnlyOneActiveOre(ores, selectedOre);

            EnsureFolder("Assets/Data", "Mining");
            EnsureFolder(DataRoot, "Ores");
            EnsureFolder(DataRoot, "Levels");

            OreDefinition qingShi = CreateOrLoadOre(
                OreFolder + "/Ore_青石.asset", "青石", OreType.QingShi, 10f, 10);
            OreDefinition hongZhuan = CreateOrLoadOre(
                OreFolder + "/Ore_红砖.asset", "红砖", OreType.HongZhuan, 10f, 10);
            OreDefinition muLiao = CreateOrLoadOre(
                OreFolder + "/Ore_木料.asset", "木料", OreType.MuLiao, 10f, 10);
            OreDefinition huaGangYan = CreateOrLoadOre(
                OreFolder + "/Ore_花岗岩.asset", "花岗岩", OreType.HuaGangYan, 10f, 10);

            OreLevelData level1 = CreateOrLoadLevel(
                LevelFolder + "/OreLevel_1.asset", 1, "第 1 级 · 初到岛上",
                new List<OreGroup>
                {
                    new OreGroup(qingShi, 8, 70f),
                    new OreGroup(hongZhuan, 4, 30f),
                });

            OreLevelData level2 = CreateOrLoadLevel(
                LevelFolder + "/OreLevel_2.asset", 2, "第 2 级 · 街巷渐起",
                new List<OreGroup>
                {
                    new OreGroup(qingShi, 8, 40f),
                    new OreGroup(hongZhuan, 8, 35f),
                    new OreGroup(muLiao, 4, 25f),
                });

            OreLevelData level3 = CreateOrLoadLevel(
                LevelFolder + "/OreLevel_3.asset", 3, "第 3 级 · 万国建筑",
                new List<OreGroup>
                {
                    new OreGroup(hongZhuan, 6, 30f),
                    new OreGroup(muLiao, 8, 40f),
                    new OreGroup(huaGangYan, 4, 30f),
                });

            OreSpawnTable spawnTable = CreateOrLoadSpawnTable(level1, level2, level3);
            BuildingLevelTable buildingLevelTable = CreateOrLoadBuildingLevelTable();

            EnsureMiningComponents(selectedOre.gameObject, qingShi);

            BuildingManager buildingManager = Object.FindFirstObjectByType<BuildingManager>(
                FindObjectsInactive.Include);

            OreSpawner spawner = Object.FindFirstObjectByType<OreSpawner>(
                FindObjectsInactive.Include);

            if (spawner == null)
            {
                GameObject spawnerObject = new GameObject("OreSpawner");
                Undo.RegisterCreatedObjectUndo(spawnerObject, "创建矿石生成器");
                spawner = Undo.AddComponent<OreSpawner>(spawnerObject);
            }

            spawner.ConfigureForSetup(selectedOre, spawnTable, buildingLevelTable,
                buildingManager, selectedOre.transform);
            EditorUtility.SetDirty(spawner);

            Undo.CollapseUndoOperations(undoGroup);

            EditorSceneManager.MarkSceneDirty(selectedOre.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[矿石生成] 配置完成：只保留 " + selectedOre.name + " 一个矿位；" +
                      "已创建 4 种矿石、3 个等级、建筑等级表，并挂好 OreSpawner。\n" +
                      "进入 Play 后：矿石会自动出现在 Ore_01 的位置，打碎后立刻生成下一块。", spawner);
        }

        private static Ore FindPreferredOre(Ore[] ores)
        {
            for (int i = 0; i < ores.Length; i++)
            {
                if (ores[i] != null && ores[i].name == "Ore_01")
                {
                    return ores[i];
                }
            }

            for (int i = 0; i < ores.Length; i++)
            {
                if (ores[i] != null)
                {
                    return ores[i];
                }
            }

            return null;
        }

        private static void SetOnlyOneActiveOre(Ore[] ores, Ore selected)
        {
            for (int i = 0; i < ores.Length; i++)
            {
                Ore ore = ores[i];
                if (ore == null)
                {
                    continue;
                }

                bool shouldBeActive = ore == selected;
                if (ore.gameObject.activeSelf == shouldBeActive)
                {
                    continue;
                }

                Undo.RecordObject(ore.gameObject, "切换矿石显示");
                ore.gameObject.SetActive(shouldBeActive);
                EditorUtility.SetDirty(ore.gameObject);
            }
        }

        private static void EnsureMiningComponents(GameObject target, OreDefinition initialOre)
        {
            OreTypeTag tag = EnsureComponent<OreTypeTag>(target);
            tag.Configure(initialOre.Type, initialOre.UseTint);

            EnsureComponent<OreHitFlash>(target);
            EnsureComponent<OreShatter>(target);
            EnsureComponent<CulturalFragmentDropper>(target);

            EditorUtility.SetDirty(target);
        }

        private static T EnsureComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            if (component == null)
            {
                component = Undo.AddComponent<T>(target);
            }

            return component;
        }

        private static OreDefinition CreateOrLoadOre(string path, string displayName,
            OreType type, float maxHealth, int reward)
        {
            bool created;
            OreDefinition asset = LoadOrCreate<OreDefinition>(path, out created);

            if (created)
            {
                asset.ConfigureForSetup(displayName, type, maxHealth, reward,
                    true, false, OreTypeInfo.Tint(type));
                EditorUtility.SetDirty(asset);
            }

            return asset;
        }

        private static OreLevelData CreateOrLoadLevel(string path, int level,
            string displayName, List<OreGroup> defaultGroups)
        {
            bool created;
            OreLevelData asset = LoadOrCreate<OreLevelData>(path, out created);

            if (created || asset.Groups == null || asset.Groups.Count == 0)
            {
                asset.ConfigureForSetup(level, displayName, defaultGroups);
                EditorUtility.SetDirty(asset);
            }

            return asset;
        }

        private static OreSpawnTable CreateOrLoadSpawnTable(params OreLevelData[] levels)
        {
            bool created;
            OreSpawnTable asset = LoadOrCreate<OreSpawnTable>(SpawnTablePath, out created);

            if (created || asset.LevelCount == 0)
            {
                asset.ConfigureForSetup(new List<OreLevelData>(levels));
                EditorUtility.SetDirty(asset);
            }

            return asset;
        }

        private static BuildingLevelTable CreateOrLoadBuildingLevelTable()
        {
            bool created;
            BuildingLevelTable asset = LoadOrCreate<BuildingLevelTable>(BuildingLevelPath, out created);

            if (created || asset.Entries == null || asset.Entries.Count == 0)
            {
                asset.ConfigureForSetup(new List<BuildingLevelEntry>
                {
                    new BuildingLevelEntry(0, 1),
                    new BuildingLevelEntry(3, 2),
                    new BuildingLevelEntry(6, 3),
                });
                EditorUtility.SetDirty(asset);
            }

            return asset;
        }

        private static T LoadOrCreate<T>(string path, out bool created) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                created = false;
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            created = true;
            return asset;
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
