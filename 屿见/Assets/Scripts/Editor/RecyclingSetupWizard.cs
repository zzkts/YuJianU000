using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.Building.Effects;
using Yujian.Building.Recycling;
using Yujian.Core;
using Yujian.Mining;
using Yujian.Player;
using Yujian.Save;
using Yujian.Shop;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 垃圾回收站系统的一键配置工具（策划案「垃圾回收站系统」第 2 步）。
    ///
    /// 入口：菜单栏 → 屿见 → 建筑 → 配置垃圾回收站系统
    ///
    /// 做这些事：
    ///   1. 生成 / 复用回收站数值资产 RecyclingSettings；
    ///   2. 生成 / 复用 HuishouLevelUP_01 升级表与强化节点表
    ///      （Node_Huishouzhan_01 回收速度、Node_Huishouzhan_02 回收效率）；
    ///   3. 以现有建筑 Prefab 为模板生成回收站占位（去掉自动攻击、挂回收站效果）；
    ///   4. 生成 Building_回收站.asset（8 格占地、上限 3 座）并登记进 SaveCatalog；
    ///   5. 给 Player 补 PlayerGarbage，创建 RecyclingSystem 并接线；
    ///   6. 把回收站追加到 BuildingUI 清单与建筑面板按钮。
    ///
    /// 幂等：已存在的资产 / 组件 / 按钮不会重复创建；已调过的数值不会被覆盖。
    /// 不直接改 .unity / .prefab —— 全部通过 Unity 的编辑器 API 写入（项目规则 13）。
    /// </summary>
    public static class RecyclingSetupWizard
    {
        private const string BuildingFolder = "Assets/Data/Buildings";
        private const string SettingsPath = BuildingFolder + "/RecyclingSettings.asset";
        private const string UpgradePath = BuildingFolder + "/HuishouLevelUP_01.asset";
        private const string EnhancementPath = BuildingFolder + "/Enhancement_Huishou_01.asset";
        private const string StationDataPath = BuildingFolder + "/Building_回收站.asset";
        private const string StationPrefabPath = "Assets/Prefabs/建筑/建筑_回收站.prefab";
        private const string StationBlueprintPath = "Assets/Prefabs/建筑/蓝图_回收站.prefab";
        private const string SourceBuildingPrefab = "Assets/Prefabs/建筑/建筑.prefab";
        private const string SourceBlueprintPrefab = "Assets/Prefabs/建筑/蓝图_建筑.prefab";
        private const string SaveCatalogPath = "Assets/Data/Save/SaveCatalog.asset";
        private const string EconomyFolder = "Assets/Data/Economy";
        private const string EconomySettingsPath = EconomyFolder + "/EconomySettings.asset";
        private const string RecyclingHudObjectName = "RecyclingHud";
        private const string PanelObjectName = "BuildingPanel";
        private const string StationButtonName = "Button_回收站";
        private const float ButtonHeight = 80f;
        private const float ButtonSpacing = 10f;

        [MenuItem("屿见/建筑/配置垃圾回收站系统", false, 534)]
        public static void Configure()
        {
            PlayerCurrency currency = Object.FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);

            if (currency == null)
            {
                Fail("场景里找不到 PlayerCurrency，无法接回收站。\n请先运行菜单「屿见/整合/配置 MVP 整合（阶段 6）」。");
                return;
            }

            PlayerGarbage garbage = currency.GetComponent<PlayerGarbage>();

            if (garbage == null)
            {
                garbage = Undo.AddComponent<PlayerGarbage>(currency.gameObject);
                Debug.Log("[回收站配置] 已给 Player 添加 PlayerGarbage 组件（独立垃圾资源）。", currency);
            }

            RecyclingSettings settings = EnsureSettings();
            BuildingEnhancementNodeTable enhancement = EnsureEnhancementTable();
            BuildingUpgradeTable upgrade = EnsureUpgradeTable(enhancement);
            GameObject stationPrefab = EnsureStationPrefab();
            GameObject blueprintPrefab = EnsureBlueprintPrefab();
            BuildingData stationData = EnsureStationData(settings, upgrade, stationPrefab, blueprintPrefab);

            EnsureSaveCatalogEntry(stationData);

            EconomySettings economy = EnsureEconomySettings();
            EnsureLegacyPricesConverted(economy);

            RecyclingSystem system = EnsureRecyclingSystem(settings, garbage, currency);
            EnsureSceneEconomyConverted(economy, system);

            EnsureEconomyWiring(garbage, system);
            SaveSetupWizard.EnsureEconomyParticipants();

            GameAreaController areas = Object.FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);
            RecyclingHudUI recyclingHud = EnsureRecyclingHud(areas, garbage, currency, system);

            BuildingUI ui = Object.FindFirstObjectByType<BuildingUI>(FindObjectsInactive.Include);

            if (ui == null)
            {
                Debug.LogWarning("[回收站配置] 场景里找不到 BuildingUI，回收站暂时没有建造入口。" +
                                 "请先运行菜单「屿见/建筑/配置建筑系统（阶段 3）」。", system);
            }
            else
            {
                int index = EnsureAvailableBuilding(ui, stationData);
                EnsurePanelButton(ui, stationData, index);
            }

            EditorSceneManager.MarkSceneDirty(currency.gameObject.scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = stationData;
            EditorGUIUtility.PingObject(stationData);

            Debug.Log("[回收站配置] 完成：\n" +
                      $"  · 数值配置：{SettingsPath}\n" +
                      $"  · 升级表：{UpgradePath}（上限 100 级，跃迁间隔 25）\n" +
                      $"  · 强化节点：{EnhancementPath}\n" +
                      $"  · 建筑资产：{StationDataPath}（占地 4×2，上限 3 座）\n" +
                      $"  · 占位 Prefab：{StationPrefabPath}\n" +
                      $"  · 经济设置：{EconomySettingsPath}（旧价格已按转换比迁移，只执行一次）\n" +
                      "  · Player 上已补 PlayerGarbage；场景里已创建 RecyclingSystem 与 RecyclingHud\n" +
                      "  · 采矿 / 收集器 / 清洁界面已改为产出与显示垃圾\n" +
                      "  · 旧 CoinHud 已隐藏；关卡内外只保留 RecyclingHud 一个「垃圾 + 金币」资源栏\n" +
                      "请按 Ctrl+S 保存场景。\n" +
                      "注意：建筑部位表未给回收站定义原料，当前 requiredMaterials 为空（落位即可建造）。");
        }

        // ---------- 资产 ----------

        private static RecyclingSettings EnsureSettings()
        {
            RecyclingSettings existing = AssetDatabase.LoadAssetAtPath<RecyclingSettings>(SettingsPath);

            if (existing != null)
            {
                return existing;
            }

            RecyclingSettings created = ScriptableObject.CreateInstance<RecyclingSettings>();
            created.name = "RecyclingSettings";
            AssetDatabase.CreateAsset(created, SettingsPath);
            Debug.Log($"[回收站配置] 已创建数值配置：{SettingsPath}", created);
            return created;
        }

        private static BuildingEnhancementNodeTable EnsureEnhancementTable()
        {
            BuildingEnhancementNodeTable existing =
                AssetDatabase.LoadAssetAtPath<BuildingEnhancementNodeTable>(EnhancementPath);

            if (existing != null)
            {
                return existing;
            }

            BuildingEnhancementNodeTable table = ScriptableObject.CreateInstance<BuildingEnhancementNodeTable>();
            table.name = "Enhancement_Huishou_01";
            AssetDatabase.CreateAsset(table, EnhancementPath);

            SerializedObject so = new SerializedObject(table);
            so.FindProperty("tableId").stringValue = "Enhancement_Huishou_01";

            SerializedProperty options = so.FindProperty("options");
            options.arraySize = 2;

            ConfigureOption(
                options.GetArrayElementAtIndex(0),
                "Node_Huishouzhan_01", "回收速度提升",
                "垃圾的转化速度提高20%。",
                BuildingEnhancementScope.Global, BuildingEnhancementStat.RecyclingSpeed,
                true, 0.2f);

            ConfigureOption(
                options.GetArrayElementAtIndex(1),
                "Node_Huishouzhan_02", "回收效率提升",
                "每批回收产出的金币 +10%（基础每批 25 垃圾 → 5 金币，现为 5.5 金币；小数部分累计到下一批）。",
                BuildingEnhancementScope.Individual, BuildingEnhancementStat.RecyclingEfficiency,
                true, 0.1f);

            SerializedProperty nodes = so.FindProperty("nodes");
            nodes.arraySize = 1;

            SerializedProperty node = nodes.GetArrayElementAtIndex(0);
            node.FindPropertyRelative("nodeId").stringValue = "Node_Huishou_01";
            node.FindPropertyRelative("triggerLevel").intValue = 25;

            SerializedProperty nodeOptions = node.FindPropertyRelative("optionIds");
            nodeOptions.arraySize = 2;
            nodeOptions.GetArrayElementAtIndex(0).stringValue = "Node_Huishouzhan_01";
            nodeOptions.GetArrayElementAtIndex(1).stringValue = "Node_Huishouzhan_02";

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(table);
            Debug.Log($"[回收站配置] 已创建强化节点表：{EnhancementPath}", table);
            return table;
        }

        private static void ConfigureOption(
            SerializedProperty option, string id, string displayName, string description,
            BuildingEnhancementScope scope, BuildingEnhancementStat stat, bool stackable, float magnitude)
        {
            option.FindPropertyRelative("optionId").stringValue = id;
            option.FindPropertyRelative("displayName").stringValue = displayName;
            option.FindPropertyRelative("description").stringValue = description;
            option.FindPropertyRelative("scope").enumValueIndex = (int)scope;
            option.FindPropertyRelative("stat").enumValueIndex = (int)stat;
            option.FindPropertyRelative("stackable").boolValue = stackable;
            option.FindPropertyRelative("magnitude").floatValue = magnitude;
        }

        private static BuildingUpgradeTable EnsureUpgradeTable(BuildingEnhancementNodeTable enhancement)
        {
            BuildingUpgradeTable existing = AssetDatabase.LoadAssetAtPath<BuildingUpgradeTable>(UpgradePath);

            if (existing == null)
            {
                existing = ScriptableObject.CreateInstance<BuildingUpgradeTable>();
                existing.name = "HuishouLevelUP_01";
                AssetDatabase.CreateAsset(existing, UpgradePath);

                SerializedObject so = new SerializedObject(existing);

                // 建筑系统配置表：HuishouLevelUP_01，跃迁间隔 25、等级上限 100。
                // 其余成长参数沿用 BuildingUpgradeTable 的默认值（每级 +8% 基础值）。
                so.FindProperty("tableId").stringValue = "HuishouLevelUP_01";
                so.FindProperty("maxLevel").intValue = 100;
                so.FindProperty("interval").intValue = 25;
                so.ApplyModifiedPropertiesWithoutUndo();

                Debug.Log($"[回收站配置] 已创建升级表：{UpgradePath}", existing);
            }

            // 只补空引用，不改用户已经调过的数值
            SerializedObject wired = new SerializedObject(existing);
            SerializedProperty nodeTable = wired.FindProperty("enhancementNodeTable");

            if (nodeTable != null && nodeTable.objectReferenceValue == null)
            {
                nodeTable.objectReferenceValue = enhancement;
                wired.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(existing);
            }

            return existing;
        }

        // ---------- 占位 Prefab ----------

        private static GameObject EnsureStationPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(StationPrefabPath) != null)
            {
                return TouchUpStationPrefab();
            }

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceBuildingPrefab);

            if (source == null)
            {
                Fail($"找不到建筑模板 Prefab：{SourceBuildingPrefab}");
                return null;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(source) as GameObject;

            if (instance == null)
            {
                Fail("无法实例化建筑模板 Prefab。");
                return null;
            }

            try
            {
                instance.name = "回收站";
                RemoveAutoAttack(instance);
                EnsureStationEffect(instance);
                ApplyBuildingLayer(instance);

                PrefabUtility.SaveAsPrefabAsset(instance, StationPrefabPath, out bool saved);

                if (!saved)
                {
                    Fail($"保存回收站 Prefab 失败：{StationPrefabPath}");
                    return null;
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StationPrefabPath);
            Debug.Log($"[回收站配置] 已生成回收站占位 Prefab：{StationPrefabPath}", savedPrefab);
            return savedPrefab;
        }

        /// <summary>已存在时只补齐组件 / 图层，不重建模型，避免冲掉美术替换。</summary>
        private static GameObject TouchUpStationPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(StationPrefabPath);

            if (root == null)
            {
                Fail($"无法加载回收站 Prefab 内容：{StationPrefabPath}");
                return null;
            }

            try
            {
                RemoveAutoAttack(root);

                if (EnsureStationEffect(root))
                {
                    Debug.Log($"[回收站配置] 已给 {StationPrefabPath} 补上 RecyclingStationEffect。", root);
                }

                ApplyBuildingLayer(root);
                PrefabUtility.SaveAsPrefabAsset(root, StationPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(StationPrefabPath);
        }

        private static void RemoveAutoAttack(GameObject root)
        {
            OrganMuseumAutoAttack[] attacks = root.GetComponentsInChildren<OrganMuseumAutoAttack>(true);

            for (int i = 0; i < attacks.Length; i++)
            {
                Object.DestroyImmediate(attacks[i], true);
            }
        }

        private static bool EnsureStationEffect(GameObject root)
        {
            // 允许美术把效果组件拖到子物体上；只要整棵树里有一个就不要再加
            if (root.GetComponentInChildren<RecyclingStationEffect>(true) != null)
            {
                return false;
            }

            root.AddComponent<RecyclingStationEffect>();
            return true;
        }

        private static GameObject EnsureBlueprintPrefab()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(StationBlueprintPath);

            if (existing != null)
            {
                return existing;
            }

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceBlueprintPrefab);

            if (source == null)
            {
                Fail($"找不到蓝图模板 Prefab：{SourceBlueprintPrefab}");
                return null;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(source) as GameObject;

            if (instance == null)
            {
                Fail("无法实例化蓝图模板 Prefab。");
                return null;
            }

            try
            {
                instance.name = "蓝图_回收站";
                PrefabUtility.SaveAsPrefabAsset(instance, StationBlueprintPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(StationBlueprintPath);
            Debug.Log($"[回收站配置] 已生成回收站蓝图 Prefab：{StationBlueprintPath}", saved);
            return saved;
        }

        private static void ApplyBuildingLayer(GameObject root)
        {
            int layer = LayerMask.NameToLayer("Building");

            if (layer < 0)
            {
                Debug.LogWarning("[回收站配置] 工程里没有 Building 图层，Prefab 保持原图层。" +
                                 "运行时 BuildingManager 会强制改到 Building 层。", root);
                return;
            }

            SetLayerRecursively(root, layer);
        }

        private static void SetLayerRecursively(GameObject target, int layer)
        {
            target.layer = layer;

            for (int i = 0; i < target.transform.childCount; i++)
            {
                SetLayerRecursively(target.transform.GetChild(i).gameObject, layer);
            }
        }

        // ---------- 建筑数据 ----------

        private static BuildingData EnsureStationData(
            RecyclingSettings settings, BuildingUpgradeTable upgrade,
            GameObject stationPrefab, GameObject blueprintPrefab)
        {
            if (stationPrefab == null || blueprintPrefab == null)
            {
                Fail("回收站 Prefab 或蓝图 Prefab 生成失败，建筑资产未创建。");
                return null;
            }

            BuildingData existing = AssetDatabase.LoadAssetAtPath<BuildingData>(StationDataPath);

            if (existing != null)
            {
                return existing;
            }

            BuildingData data = ScriptableObject.CreateInstance<BuildingData>();
            data.name = "Building_回收站";
            AssetDatabase.CreateAsset(data, StationDataPath);

            string saveId = AssetDatabase.AssetPathToGUID(StationDataPath);

            SerializedObject so = new SerializedObject(data);
            so.FindProperty("buildingName").stringValue = "回收站";
            so.FindProperty("buildingType").enumValueIndex = (int)BuildingType.RecyclingStation;
            so.FindProperty("buildingPrefab").objectReferenceValue = stationPrefab;
            so.FindProperty("blueprintPrefab").objectReferenceValue = blueprintPrefab;
            so.FindProperty("footprintCells").vector2IntValue = new Vector2Int(4, 2);
            so.FindProperty("upgradeTable").objectReferenceValue = upgrade;
            so.FindProperty("buildLimit").intValue = 3;
            so.FindProperty("saveId").stringValue = saveId;

            // 建筑部位表没有定义回收站的原料，保持为空（落位即可建造）
            SerializedProperty requirements = so.FindProperty("requiredMaterials");

            if (requirements != null)
            {
                requirements.arraySize = 0;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);

            Debug.Log($"[回收站配置] 已创建建筑资产：{StationDataPath}（saveId {saveId}）", data);
            return data;
        }

        private static void EnsureSaveCatalogEntry(BuildingData data)
        {
            if (data == null)
            {
                return;
            }

            SaveCatalog catalog = AssetDatabase.LoadAssetAtPath<SaveCatalog>(SaveCatalogPath);

            if (catalog == null)
            {
                Debug.LogWarning($"[回收站配置] 找不到存档目录 {SaveCatalogPath}，" +
                                 "回收站不会写进存档。请运行菜单「屿见/通用模块/配置存档系统」。", data);
                return;
            }

            SerializedObject so = new SerializedObject(catalog);
            SerializedProperty buildings = so.FindProperty("buildings");

            if (buildings == null)
            {
                Debug.LogWarning("[回收站配置] SaveCatalog 上找不到 buildings 字段，回收站未登记。", catalog);
                return;
            }

            for (int i = 0; i < buildings.arraySize; i++)
            {
                if (buildings.GetArrayElementAtIndex(i).objectReferenceValue == data)
                {
                    return;
                }
            }

            buildings.arraySize++;
            buildings.GetArrayElementAtIndex(buildings.arraySize - 1).objectReferenceValue = data;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            Debug.Log($"[回收站配置] 已把回收站登记进存档目录：{SaveCatalogPath}", catalog);
        }

        // ---------- 经济拆分 ----------

        private static EconomySettings EnsureEconomySettings()
        {
            EconomySettings existing = AssetDatabase.LoadAssetAtPath<EconomySettings>(EconomySettingsPath);

            if (existing != null)
            {
                return existing;
            }

            if (!AssetDatabase.IsValidFolder(EconomyFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Data"))
                {
                    AssetDatabase.CreateFolder("Assets", "Data");
                }

                AssetDatabase.CreateFolder("Assets/Data", "Economy");
            }

            EconomySettings created = ScriptableObject.CreateInstance<EconomySettings>();
            created.name = "EconomySettings";
            AssetDatabase.CreateAsset(created, EconomySettingsPath);
            Debug.Log($"[回收站配置] 已创建经济设置：{EconomySettingsPath}", created);
            return created;
        }

        /// <summary>
        /// 一次性把旧经济（只有一种货币）里的价格 ÷ 转换比。
        /// 用 EconomySettings.LegacyPricesConverted 做标记，重复运行不会二次除。
        /// 全部字段先校验、再一起 Apply，避免中途失败留下「一半除以 5」的资产。
        /// </summary>
        private static void EnsureLegacyPricesConverted(EconomySettings economy)
        {
            if (economy == null || economy.LegacyPricesConverted)
            {
                return;
            }

            int divisor = economy.GarbagePerCoin;
            List<SerializedObject> pending = new List<SerializedObject>();
            List<string> summary = new List<string>();

            string[] materialGuids = AssetDatabase.FindAssets("t:MaterialData");

            for (int i = 0; i < materialGuids.Length; i++)
            {
                MaterialData material = AssetDatabase.LoadAssetAtPath<MaterialData>(
                    AssetDatabase.GUIDToAssetPath(materialGuids[i]));

                if (material == null)
                {
                    continue;
                }

                SerializedObject so = new SerializedObject(material);
                SerializedProperty price = so.FindProperty("price");

                if (price == null)
                {
                    Fail($"MaterialData「{material.name}」上找不到 price 字段，经济迁移已中止（未写入任何价格）。");
                    return;
                }

                int before = price.intValue;
                price.intValue = ConvertPrice(before, divisor);
                pending.Add(so);
                summary.Add($"{material.MaterialName} {before}→{price.intValue}");
            }

            string[] upgradeGuids = AssetDatabase.FindAssets("t:BuildingUpgradeTable");

            for (int i = 0; i < upgradeGuids.Length; i++)
            {
                BuildingUpgradeTable table = AssetDatabase.LoadAssetAtPath<BuildingUpgradeTable>(
                    AssetDatabase.GUIDToAssetPath(upgradeGuids[i]));

                if (table == null)
                {
                    continue;
                }

                SerializedObject so = new SerializedObject(table);
                SerializedProperty baseCost = so.FindProperty("baseCost");

                if (baseCost == null)
                {
                    Fail($"升级表「{table.name}」上找不到 baseCost 字段，经济迁移已中止。");
                    return;
                }

                int before = baseCost.intValue;
                baseCost.intValue = ConvertPrice(before, divisor);
                pending.Add(so);
                summary.Add($"{table.name}.baseCost {before}→{baseCost.intValue}");
            }

            string[] toolGuids = AssetDatabase.FindAssets("t:MiningToolData");

            for (int i = 0; i < toolGuids.Length; i++)
            {
                MiningToolData tool = AssetDatabase.LoadAssetAtPath<MiningToolData>(
                    AssetDatabase.GUIDToAssetPath(toolGuids[i]));

                if (tool == null)
                {
                    continue;
                }

                SerializedObject so = new SerializedObject(tool);
                SerializedProperty perLevel = so.FindProperty("goldCostPerLevel");
                SerializedProperty increase = so.FindProperty("goldCostIncreasePerLevel");

                if (perLevel == null || increase == null)
                {
                    Fail($"挖矿工具「{tool.name}」上找不到金币花费字段，经济迁移已中止。");
                    return;
                }

                int before = perLevel.intValue;
                perLevel.intValue = ConvertPrice(before, divisor);
                increase.intValue = ConvertPrice(increase.intValue, divisor);
                pending.Add(so);
                summary.Add($"{tool.DisplayName}.goldCostPerLevel {before}→{perLevel.intValue}");
            }

            // 场景里的数值（开局金币 / 照明价格）不能靠 EconomySettings 资产标记：
            // 资产会立刻落盘，但场景可能没保存。它们的标记放在场景物体 RecyclingSystem 上，
            // 见 EnsureSceneEconomyConverted。

            // 全部字段都找到了，才真正写入
            for (int i = 0; i < pending.Count; i++)
            {
                pending[i].ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(pending[i].targetObject);
            }

            economy.MarkLegacyPricesConverted();
            EditorUtility.SetDirty(economy);
            AssetDatabase.SaveAssets();

            Debug.Log("[回收站配置] 旧经济价格已按 ÷" + divisor + " 迁移（只执行一次）：\n  · " +
                      string.Join("\n  · ", summary.ToArray()));
        }

        /// <summary>
        /// 迁移**场景里**的旧经济数值（开局金币、照明升级价格）。
        ///
        /// 为什么不能复用 EconomySettings 的资产标记：资产是立刻 SaveAssets 的，场景却要用户 Ctrl+S。
        /// 用户跑完向导没存场景时，资产标记已 true，但场景磁盘值还是旧的；再跑就会跳过。
        /// 所以场景标记放在场景物体 RecyclingSystem 上，没存场景 = 标记也没落盘 = 下次会重新迁移。
        /// </summary>
        private static void EnsureSceneEconomyConverted(EconomySettings economy, RecyclingSystem system)
        {
            if (economy == null || system == null || system.LegacyScenePricesConverted)
            {
                return;
            }

            int divisor = economy.GarbagePerCoin;

            PlayerCurrency currency = Object.FindFirstObjectByType<PlayerCurrency>(
                FindObjectsInactive.Include);

            if (currency == null)
            {
                Fail("场景里找不到 PlayerCurrency，场景数值迁移已中止。");
                return;
            }

            SerializedObject currencySo = new SerializedObject(currency);
            SerializedProperty startingMoney = currencySo.FindProperty("startingMoney");

            if (startingMoney == null)
            {
                Fail("PlayerCurrency 上找不到 startingMoney 字段，场景数值迁移已中止。");
                return;
            }

            MiningLightTool lightTool = Object.FindFirstObjectByType<MiningLightTool>(
                FindObjectsInactive.Include);
            SerializedObject lightSo = null;
            SerializedProperty costs = null;

            if (lightTool != null)
            {
                lightSo = new SerializedObject(lightTool);
                costs = lightSo.FindProperty("upgradeCosts");

                if (costs == null)
                {
                    Fail("MiningLightTool 上找不到 upgradeCosts 字段，场景数值迁移已中止。");
                    return;
                }
            }

            int previousStart = startingMoney.intValue;
            startingMoney.intValue = economy.StartingCoins;
            currencySo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(currency);

            string lightSummary = "未找到 MiningLightTool";

            if (lightSo != null)
            {
                // 旧版向导在「场景已保存」的情况下可能已经除过一次，但当时还没有场景标记字段。
                // 本项目旧照明价格从 286 起，除完是 58；首项 <= 100 视为已经是金币值，避免二次除。
                bool alreadyConverted = costs.arraySize > 0 &&
                                        costs.GetArrayElementAtIndex(0).intValue <= 100;

                if (alreadyConverted)
                {
                    lightSummary = "照明价格已是金币值，跳过再除";
                }
                else
                {
                    for (int i = 0; i < costs.arraySize; i++)
                    {
                        SerializedProperty cost = costs.GetArrayElementAtIndex(i);
                        cost.intValue = ConvertPrice(cost.intValue, divisor);
                    }

                    lightSo.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(lightTool);
                    lightSummary = $"照明价格已 ÷{divisor}";
                }
            }

            system.MarkLegacyScenePricesConverted();
            EditorUtility.SetDirty(system);
            EditorSceneManager.MarkSceneDirty(system.gameObject.scene);

            Debug.Log($"[回收站配置] 场景经济数值已迁移（只执行一次）：开局金币 {previousStart}→" +
                      $"{economy.StartingCoins}；{lightSummary}。\n" +
                      "请 Ctrl+S 保存场景，否则下次运行向导会重新迁移。");
        }

        private static int ConvertPrice(int legacyPrice, int divisor)
        {
            if (legacyPrice <= 0)
            {
                return 0;
            }

            return Mathf.Max(1, Mathf.CeilToInt(legacyPrice / (float)Mathf.Max(1, divisor)));
        }

        // ---------- 场景 ----------

        private static RecyclingSystem EnsureRecyclingSystem(
            RecyclingSettings settings, PlayerGarbage garbage, PlayerCurrency currency)
        {
            RecyclingSystem existing = Object.FindFirstObjectByType<RecyclingSystem>(FindObjectsInactive.Include);
            RecyclingSystem system;

            if (existing != null)
            {
                system = existing;
            }
            else
            {
                GameObject host = new GameObject("RecyclingSystem");
                Undo.RegisterCreatedObjectUndo(host, "创建 RecyclingSystem");
                system = host.AddComponent<RecyclingSystem>();
                Debug.Log("[回收站配置] 已创建场景物体 RecyclingSystem。", host);
            }

            SerializedObject so = new SerializedObject(system);
            so.FindProperty("settings").objectReferenceValue = settings;
            so.FindProperty("garbage").objectReferenceValue = garbage;
            so.FindProperty("currency").objectReferenceValue = currency;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(system.gameObject.scene);
            return system;
        }

        /// <summary>
        /// 把经济拆分后的奖励 / 显示路径接到新组件上：
        /// 采矿与收集器发垃圾；「垃圾 · 金币」只由全局唯一的 RecyclingHud 显示；
        /// 旧的 CoinHud 隐藏，避免再出现第二块资源 UI。
        /// </summary>
        private static void EnsureEconomyWiring(PlayerGarbage garbage, RecyclingSystem system)
        {
            if (garbage == null || system == null)
            {
                Debug.LogWarning("[回收站配置] PlayerGarbage 或 RecyclingSystem 缺失，经济接线被跳过。");
                return;
            }

            MiningInput input = Object.FindFirstObjectByType<MiningInput>(FindObjectsInactive.Include);

            if (input != null)
            {
                SerializedObject so = new SerializedObject(input);
                SetObject(so, "playerGarbage", garbage);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(input);
            }
            else
            {
                Debug.LogWarning("[回收站配置] 场景里找不到 MiningInput，采矿奖励仍会发到旧路径。", garbage);
            }

            GarbageCollector collector = Object.FindFirstObjectByType<GarbageCollector>(
                FindObjectsInactive.Include);

            if (collector != null)
            {
                SerializedObject so = new SerializedObject(collector);
                SetObject(so, "garbage", garbage);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(collector);
            }

            CoinHudUI coinHud = Object.FindFirstObjectByType<CoinHudUI>(FindObjectsInactive.Include);

            if (coinHud != null)
            {
                // 2026-11-19 用户要求：只保留一个同时显示垃圾与金币的资源栏。
                // 岛上由 RecyclingHudUI 统一显示，旧的 CoinHud 隐藏，避免两块资源 UI 叠在一起。
                Undo.RecordObject(coinHud.gameObject, "隐藏旧金币 HUD");
                coinHud.gameObject.SetActive(false);

                SerializedObject so = new SerializedObject(coinHud);
                SetObject(so, "recycling", system);
                SetString(so, "format", "金币：{0}");
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(coinHud);
                EditorSceneManager.MarkSceneDirty(coinHud.gameObject.scene);
                Debug.Log("[回收站配置] 已隐藏旧的 CoinHud；岛上资源栏改由 RecyclingHud 统一显示垃圾与金币。", coinHud);
            }
        }

        private static RecyclingHudUI EnsureRecyclingHud(
            GameAreaController areas, PlayerGarbage garbage, PlayerCurrency currency, RecyclingSystem system)
        {
            RecyclingHudUI existing = Object.FindFirstObjectByType<RecyclingHudUI>(
                FindObjectsInactive.Include);
            RecyclingHudUI hud = existing;

            if (hud == null)
            {
                GameObject host = new GameObject(RecyclingHudObjectName);
                Undo.RegisterCreatedObjectUndo(host, "创建 " + RecyclingHudObjectName);
                hud = host.AddComponent<RecyclingHudUI>();
                Debug.Log("[回收站配置] 已创建回收站 HUD（UI 控件在运行时生成）。", host);
            }

            hud.ConfigureForSetup(areas, garbage, currency, system);
            EditorUtility.SetDirty(hud);
            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            return hud;
        }

        private static int EnsureAvailableBuilding(BuildingUI ui, BuildingData data)
        {
            SerializedObject so = new SerializedObject(ui);
            SerializedProperty list = so.FindProperty("availableBuildings");

            if (list == null)
            {
                Debug.LogWarning("[回收站配置] BuildingUI 上找不到 availableBuildings 字段，回收站没有建造入口。", ui);
                return -1;
            }

            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == data)
                {
                    return i;
                }
            }

            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = data;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ui);

            Debug.Log($"[回收站配置] 已把回收站加入 BuildingUI 清单（下标 {list.arraySize - 1}）。", ui);
            return list.arraySize - 1;
        }

        private static void EnsurePanelButton(BuildingUI ui, BuildingData data, int index)
        {
            if (ui == null || data == null || index < 0)
            {
                return;
            }

            // 场景里可能不止一个 Canvas（例如 MiningHUDRuntime），
            // 不能取「第一个 Canvas」，必须找真正挂着 BuildingPanel 的那个。
            Transform panel = FindBuildingPanel();

            if (panel == null)
            {
                Debug.LogWarning($"[回收站配置] 场景里找不到 {PanelObjectName}，" +
                                 "已加入建筑清单但还没有按钮。请运行「屿见/建筑/配置建筑系统（阶段 3）」。", ui);
                return;
            }

            Transform existing = panel.Find(StationButtonName);

            if (existing != null)
            {
                // 重跑时按最新下标重写点击事件，避免清单顺序变化后点到别的建筑
                WireButton(existing.GetComponent<Button>(), ui, index);
                return;
            }

            Transform template = FindButtonTemplate(panel);

            if (template == null)
            {
                Debug.LogWarning("[回收站配置] 建筑面板里找不到可复制的按钮模板，" +
                                 "已加入建筑清单但还没有按钮。请运行「屿见/建筑/配置建筑系统（阶段 3）」。", ui);
                return;
            }

            float top = GetNextButtonTop(panel);

            GameObject clone = Object.Instantiate(template.gameObject, panel, false);
            clone.name = StationButtonName;
            clone.SetActive(true);
            Undo.RegisterCreatedObjectUndo(clone, "创建回收站按钮");

            RectTransform rect = clone.GetComponent<RectTransform>();

            if (rect != null)
            {
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -top);
                rect.sizeDelta = new Vector2(0f, ButtonHeight);
            }

            Text text = clone.GetComponentInChildren<Text>(true);

            if (text != null)
            {
                text.text = data.BuildingName;
            }

            WireButton(clone.GetComponent<Button>(), ui, index);

            RectTransform panelRect = panel.GetComponent<RectTransform>();

            if (panelRect != null)
            {
                panelRect.sizeDelta = new Vector2(
                    panelRect.sizeDelta.x,
                    Mathf.Max(panelRect.sizeDelta.y, top + ButtonHeight + 30f));
            }

            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            Debug.Log("[回收站配置] 已在建筑面板追加「回收站」按钮。", clone);
        }

        private static void WireButton(Button button, BuildingUI ui, int index)
        {
            if (button == null)
            {
                return;
            }

            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                UnityEventTools.RemovePersistentListener(button.onClick, i);
            }

            UnityEventTools.AddIntPersistentListener(button.onClick, ui.SelectBuilding, index);
        }

        /// <summary>
        /// 找真正挂着 BuildingPanel 的 Canvas。场景里的 Canvas 不止一个
        /// （MiningHUDRuntime 也是 Canvas），直接取第一个会找不到建筑面板。
        /// </summary>
        private static Transform FindBuildingPanel()
        {
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i] == null)
                {
                    continue;
                }

                Transform panel = canvases[i].transform.Find(PanelObjectName);

                if (panel != null)
                {
                    return panel;
                }
            }

            return null;
        }

        private static Transform FindButtonTemplate(Transform panel)
        {
            Transform fallback = null;

            for (int i = 0; i < panel.childCount; i++)
            {
                Transform child = panel.GetChild(i);

                if (!child.name.StartsWith("Button_") || child.GetComponent<Button>() == null)
                {
                    continue;
                }

                if (child.name == "Button_风琴博物馆")
                {
                    return child;
                }

                if (fallback == null &&
                    child.name != "Button_取消蓝图" &&
                    child.name != "Button_退出建造")
                {
                    fallback = child;
                }
            }

            return fallback;
        }

        private static float GetNextButtonTop(Transform panel)
        {
            float maxTop = 54f; // 进度行 44 + 间距 10

            for (int i = 0; i < panel.childCount; i++)
            {
                RectTransform rect = panel.GetChild(i) as RectTransform;

                if (rect == null || rect.gameObject.name == StationButtonName)
                {
                    continue;
                }

                if (!rect.gameObject.name.StartsWith("Button_"))
                {
                    continue;
                }

                maxTop = Mathf.Max(maxTop, -rect.anchoredPosition.y);
            }

            return maxTop + ButtonHeight + ButtonSpacing;
        }

        private static void SetObject(SerializedObject so, string fieldName, Object value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogWarning($"[回收站配置] {so.targetObject.GetType().Name} 上找不到字段 {fieldName}，" +
                                 "该引用没有接上。");
                return;
            }

            property.objectReferenceValue = value;
        }

        private static void SetString(SerializedObject so, string fieldName, string value)
        {
            SerializedProperty property = so.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogWarning($"[回收站配置] {so.targetObject.GetType().Name} 上找不到字段 {fieldName}。");
                return;
            }

            property.stringValue = value;
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[回收站配置] {message}");
        }

        // 保留给未来的批量校验脚本：列出向导会生成的资产
        internal static IReadOnlyList<string> GeneratedAssetPaths => new[]
        {
            SettingsPath,
            UpgradePath,
            EnhancementPath,
            StationDataPath,
            StationPrefabPath,
            StationBlueprintPath,
        };
    }
}
