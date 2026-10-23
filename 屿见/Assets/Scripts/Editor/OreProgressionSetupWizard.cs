using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Building;
using Yujian.Core;
using Yujian.Mining;
using Yujian.Player;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 挖矿 2.2.2 的一键配置：矿石组数据、玩家等级、单块顺序生成。
    ///
    /// 做四件事：
    ///   1. 创建缺失的数据资产（建筑数-等级表、矿石等级表、每级一个占位矿石组）；
    ///   2. 场景里保留一块矿石作为固定生成点，停用其余矿石；
    ///   3. 给生成点打开外部生成控制，关闭旧的整批刷新；
    ///   4. 挂 PlayerLevel / OreSpawner 并接线。
    ///
    /// 幂等：已存在的资产不覆盖，已挂的组件不重复添加，已接好的引用不会被清空。
    /// 不直接改 .unity / .prefab，由 Unity 自己写盘（项目规则 13）。
    ///
    /// 入口：菜单栏 → 屿见 → 配置矿石生成系统（挖矿 2.2.2）
    /// </summary>
    public static class OreProgressionSetupWizard
    {
        private const string DataFolder = "Assets/Data/Mining";
        private const string PlayerLevelTablePath = DataFolder + "/PlayerLevelTable.asset";
        private const string OreLevelTablePath = DataFolder + "/OreLevelTable.asset";

        private const string SpawnPointName = "Ore_01";

        private static readonly OreType[] PlaceholderTypes =
        {
            OreType.QingShi,
            OreType.HongZhuan,
            OreType.MuLiao,
            OreType.HuaGangYan,
        };

        [MenuItem("屿见/挖矿/配置矿石生成系统（挖矿 2.2.2）", false, 320)]
        public static void Configure()
        {
            if (!EnsureDataFolder())
            {
                return;
            }

            BuildingManager buildingManager = Object.FindFirstObjectByType<BuildingManager>();

            if (buildingManager == null)
            {
                Fail("场景里找不到 BuildingManager，无法统计建筑数。\n\n" +
                     "请先运行菜单「屿见 → 配置建筑系统（阶段 3）」。");
                return;
            }

            PlayerStats playerStats = Object.FindFirstObjectByType<PlayerStats>();

            if (playerStats == null)
            {
                Fail("场景里找不到 PlayerStats，无法确定玩家物体。");
                return;
            }

            GameAreaController areaController = Object.FindFirstObjectByType<GameAreaController>();

            if (areaController == null)
            {
                Fail("场景里找不到 GameAreaController（GameAreaSystem），无法确定生成器挂载位置。");
                return;
            }

            Ore[] ores = FindAllOres();

            if (ores.Length == 0)
            {
                Fail("当前打开的场景里没有找到 Ore 组件。请先打开 SampleScene 再执行本命令。");
                return;
            }

            // ---------- 1. 数据资产 ----------

            PlayerLevelTable playerLevelTable = LoadOrCreatePlayerLevelTable(out bool playerLevelTableCreated);
            OreLevelTable oreLevelTable = LoadOrCreateOreLevelTable(out bool oreLevelTableCreated);

            // ---------- 2. 选择固定生成点，停用其余矿石 ----------

            Ore spawnOre = ChooseSpawnOre(ores);
            int disabledCount = 0;

            for (int i = 0; i < ores.Length; i++)
            {
                Ore ore = ores[i];

                if (ore == spawnOre)
                {
                    SetActiveWithUndo(ore.gameObject, true);
                    continue;
                }

                if (ore.gameObject.activeSelf)
                {
                    SetActiveWithUndo(ore.gameObject, false);
                    disabledCount++;
                }
            }

            if (spawnOre.GetComponent<OreTypeTag>() == null)
            {
                Undo.AddComponent<OreTypeTag>(spawnOre.gameObject);
            }

            // 打开外部生成控制，关掉 Ore 自带的整批刷新
            SerializedObject spawnOreSo = new SerializedObject(spawnOre);
            SetBool(spawnOreSo, "externalSpawnControl", true);
            SetFloat(spawnOreSo, "respawnDelay", 0f);
            SetBool(spawnOreSo, "randomizeTypeOnRespawn", false);
            spawnOreSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 3. 玩家等级 ----------

            PlayerLevel playerLevel = EnsureComponent<PlayerLevel>(playerStats.gameObject);
            SerializedObject playerLevelSo = new SerializedObject(playerLevel);
            SetObject(playerLevelSo, "buildingManager", buildingManager);
            SetObject(playerLevelSo, "levelTable", playerLevelTable);
            playerLevelSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 4. 矿石生成器 ----------

            OreSpawner spawner = EnsureComponent<OreSpawner>(areaController.gameObject);
            SerializedObject spawnerSo = new SerializedObject(spawner);
            SetObject(spawnerSo, "ore", spawnOre);
            SetObject(spawnerSo, "playerLevel", playerLevel);
            SetObject(spawnerSo, "levelTable", oreLevelTable);
            spawnerSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 5. 收尾 ----------

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(spawnOre.gameObject.scene);
            Selection.activeGameObject = spawnOre.gameObject;
            EditorGUIUtility.PingObject(spawnOre.gameObject);

            Debug.Log($"[OreProgressionWizard] 配置完成：\n" +
                      $"  · 固定生成点：{spawnOre.name}（已开启外部生成控制），停用其余 {disabledCount} 块矿石\n" +
                      $"  · {playerStats.name}：PlayerLevel → {PlayerLevelTablePath}\n" +
                      $"  · {areaController.name}：OreSpawner → {OreLevelTablePath}\n" +
                      $"  · 数据资产：建筑等级表{(playerLevelTableCreated ? "（新建）" : "（复用）")}、" +
                      $"矿石等级表{(oreLevelTableCreated ? "（新建）" : "（复用）")}\n" +
                      "  · 占位数据：每级 1 组、4 种矿等权重、HP 10 / 奖励 10，请策划按成长曲线修改\n" +
                      "请按 Ctrl+S 保存场景，然后再进入 Play Mode。");
        }

        /// <summary>确保 Assets/Data/Mining 存在。</summary>
        private static bool EnsureDataFolder()
        {
            if (AssetDatabase.IsValidFolder(DataFolder))
            {
                return true;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Data"))
            {
                AssetDatabase.CreateFolder("Assets", "Data");
            }

            string guid = AssetDatabase.CreateFolder("Assets/Data", "Mining");

            if (string.IsNullOrEmpty(guid))
            {
                Fail($"创建文件夹 {DataFolder} 失败，无法生成数据资产。");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 加载建筑数-等级表；不存在时新建并按文档默认值填 0→1、3→2、6→3。
        /// 已存在的资产一律不覆盖。
        /// </summary>
        private static PlayerLevelTable LoadOrCreatePlayerLevelTable(out bool created)
        {
            PlayerLevelTable existing = AssetDatabase.LoadAssetAtPath<PlayerLevelTable>(PlayerLevelTablePath);

            if (existing != null)
            {
                created = false;
                return existing;
            }

            PlayerLevelTable table = ScriptableObject.CreateInstance<PlayerLevelTable>();
            AssetDatabase.CreateAsset(table, PlayerLevelTablePath);

            SerializedObject so = new SerializedObject(table);
            SerializedProperty thresholds = so.FindProperty("thresholds");

            if (thresholds != null)
            {
                thresholds.arraySize = 3;
                ConfigureThreshold(thresholds.GetArrayElementAtIndex(0), 0, 1);
                ConfigureThreshold(thresholds.GetArrayElementAtIndex(1), 3, 2);
                ConfigureThreshold(thresholds.GetArrayElementAtIndex(2), 6, 3);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("[OreProgressionWizard] PlayerLevelTable 上找不到 thresholds 字段，默认等级表未填充。");
            }

            EditorUtility.SetDirty(table);
            created = true;
            return table;
        }

        private static void ConfigureThreshold(SerializedProperty element, int buildingCount, int level)
        {
            SerializedProperty countProperty = element.FindPropertyRelative("buildingCount");
            SerializedProperty levelProperty = element.FindPropertyRelative("level");

            if (countProperty != null)
            {
                countProperty.intValue = buildingCount;
            }

            if (levelProperty != null)
            {
                levelProperty.intValue = level;
            }
        }

        /// <summary>
        /// 加载矿石等级表；不存在时创建每级一个矿石组，并填入占位条目。
        /// 已存在的资产（包括里面的组）一律不覆盖。
        /// </summary>
        private static OreLevelTable LoadOrCreateOreLevelTable(out bool created)
        {
            OreLevelTable existing = AssetDatabase.LoadAssetAtPath<OreLevelTable>(OreLevelTablePath);

            if (existing != null)
            {
                created = false;
                return existing;
            }

            OreGroup[] groups = new OreGroup[3];

            for (int i = 0; i < groups.Length; i++)
            {
                groups[i] = CreatePlaceholderGroup(i + 1);
            }

            OreLevelTable table = ScriptableObject.CreateInstance<OreLevelTable>();
            AssetDatabase.CreateAsset(table, OreLevelTablePath);

            SerializedObject so = new SerializedObject(table);
            SerializedProperty levels = so.FindProperty("levels");

            if (levels != null)
            {
                levels.arraySize = groups.Length;

                for (int i = 0; i < groups.Length; i++)
                {
                    SerializedProperty levelElement = levels.GetArrayElementAtIndex(i);
                    SerializedProperty levelProperty = levelElement.FindPropertyRelative("level");
                    SerializedProperty groupsProperty = levelElement.FindPropertyRelative("groups");

                    if (levelProperty != null)
                    {
                        levelProperty.intValue = i + 1;
                    }

                    if (groupsProperty != null)
                    {
                        groupsProperty.arraySize = 1;
                        groupsProperty.GetArrayElementAtIndex(0).objectReferenceValue = groups[i];
                    }
                }

                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("[OreProgressionWizard] OreLevelTable 上找不到 levels 字段，默认等级表未填充。");
            }

            EditorUtility.SetDirty(table);
            created = true;
            return table;
        }

        /// <summary>创建一个占位矿石组：4 种矿等权重，HP 10 / 奖励 10。</summary>
        private static OreGroup CreatePlaceholderGroup(int level)
        {
            string assetName = $"OreGroup_Lv{level}";
            string path = $"{DataFolder}/{assetName}.asset";
            OreGroup existing = AssetDatabase.LoadAssetAtPath<OreGroup>(path);

            if (existing != null)
            {
                return existing;
            }

            OreGroup group = ScriptableObject.CreateInstance<OreGroup>();
            group.name = assetName;
            AssetDatabase.CreateAsset(group, path);

            SerializedObject so = new SerializedObject(group);
            SerializedProperty entries = so.FindProperty("entries");

            if (entries != null)
            {
                entries.arraySize = PlaceholderTypes.Length;

                for (int i = 0; i < PlaceholderTypes.Length; i++)
                {
                    SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                    SetEnum(entry, "type", (int)PlaceholderTypes[i]);
                    SetFloat(entry, "weight", 1f);
                    SetFloat(entry, "maxHealth", 10f);
                    SetInt(entry, "reward", 10);
                }

                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning($"[OreProgressionWizard] OreGroup 上找不到 entries 字段，{assetName} 为空组。");
            }

            EditorUtility.SetDirty(group);
            return group;
        }

        /// <summary>优先用 Ore_01 当固定生成点；找不到时按名字排序取第一块，保证重复运行结果一致。</summary>
        private static Ore ChooseSpawnOre(Ore[] ores)
        {
            for (int i = 0; i < ores.Length; i++)
            {
                if (ores[i].name == SpawnPointName)
                {
                    return ores[i];
                }
            }

            System.Array.Sort(ores, (a, b) => string.CompareOrdinal(a.name, b.name));

            Debug.LogWarning($"[OreProgressionWizard] 场景里没有名为 {SpawnPointName} 的矿石，" +
                             $"改用按名字排序的第一块「{ores[0].name}」。");
            return ores[0];
        }

        private static Ore[] FindAllOres()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindObjectsByType<Ore>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<Ore>(true);
#endif
        }

        private static T EnsureComponent<T>(GameObject target) where T : Component
        {
            T existing = target.GetComponent<T>();

            if (existing != null)
            {
                return existing;
            }

            return Undo.AddComponent<T>(target);
        }

        private static void SetActiveWithUndo(GameObject target, bool active)
        {
            if (target.activeSelf == active)
            {
                return;
            }

            Undo.RecordObject(target, active ? "启用矿石生成点" : "停用多余矿石");
            target.SetActive(active);
        }

        // ---- SerializedProperty 写入助手 ----
        // 目标字段都是 private [SerializeField]，只能走 SerializedObject 改。
        // 每个都判空：字段被改名时只警告、跳过，不抛异常中断整个配置。

        private static void SetObject(SerializedObject so, string fieldName, Object value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogWarning($"[OreProgressionWizard] {so.targetObject.GetType().Name} 上找不到字段 " +
                                 $"{fieldName}，该项跳过。");
                return;
            }

            property.objectReferenceValue = value;
        }

        private static void SetBool(SerializedObject so, string fieldName, bool value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static void SetFloat(SerializedObject so, string fieldName, float value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property != null)
            {
                property.floatValue = value;
            }
        }

        private static void SetFloat(SerializedProperty parent, string fieldName, float value)
        {
            SerializedProperty property = parent.FindPropertyRelative(fieldName);

            if (property != null)
            {
                property.floatValue = value;
            }
        }

        private static void SetInt(SerializedProperty parent, string fieldName, int value)
        {
            SerializedProperty property = parent.FindPropertyRelative(fieldName);

            if (property != null)
            {
                property.intValue = value;
            }
        }

        private static void SetEnum(SerializedProperty parent, string fieldName, int value)
        {
            SerializedProperty property = parent.FindPropertyRelative(fieldName);

            if (property != null)
            {
                property.enumValueIndex = value;
            }
        }

        private static void Fail(string message)
        {
            EditorUtility.DisplayDialog("矿石生成配置未执行", message, "知道了");
            Debug.LogError("[OreProgressionWizard] " + message);
        }
    }
}
