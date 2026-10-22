using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Building;
using Yujian.Player;
using Yujian.Save;
using Yujian.Shop;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 存档系统的一键配置。
    ///
    /// 做五件事：
    ///   1. 创建 / 复用 Assets/Data/Save/SaveCatalog.asset；
    ///   2. 给 Assets/Data/Materials 与 Assets/Data/Buildings 下的资产补稳定 Save Id（取资产 GUID）；
    ///   3. 把材料与建筑资产登记进存档目录（保留已有条目，只补缺失）；
    ///   4. 在活动场景创建 / 复用 SaveSystem 物体，挂上全部适配器并接线；
    ///   5. 保存资产与场景。
    ///
    /// 幂等：已有组件、目录条目、Save Id 一律不覆盖，可重复运行。
    /// 不直接改 .unity / .prefab，由 Unity 自己写盘（项目规则 13）。
    ///
    /// 入口：菜单栏 → 屿见 → 配置存档系统
    /// </summary>
    public static class SaveSetupWizard
    {
        private const string DataFolder = "Assets/Data/Save";
        private const string CatalogPath = DataFolder + "/SaveCatalog.asset";
        private const string MaterialFolder = "Assets/Data/Materials";
        private const string BuildingFolder = "Assets/Data/Buildings";
        private const string SaveObjectName = "SaveSystem";

        [MenuItem("屿见/配置存档系统")]
        public static void Configure()
        {
            SaveCatalog catalog = EnsureCatalog();
            List<MaterialData> materials = EnsureMaterialIds();
            List<BuildingData> buildings = EnsureBuildingIds();
            UpdateCatalog(catalog, materials);
            UpdateBuildingCatalog(catalog, buildings);
            EnsureSaveSystem(catalog);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[存档配置] 已完成。存档目录：{CatalogPath}；" +
                      $"场景物体：{SaveObjectName}。\n" +
                      "下一步：Ctrl+S 保存场景，进 Play Mode 验证金币、材料与建筑能否存读。", catalog);
        }

        private static SaveCatalog EnsureCatalog()
        {
            if (!AssetDatabase.IsValidFolder(DataFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Data"))
                {
                    AssetDatabase.CreateFolder("Assets", "Data");
                }

                AssetDatabase.CreateFolder("Assets/Data", "Save");
            }

            SaveCatalog catalog = AssetDatabase.LoadAssetAtPath<SaveCatalog>(CatalogPath);

            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<SaveCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
                Debug.Log($"[存档配置] 已创建存档目录：{CatalogPath}", catalog);
            }

            return catalog;
        }

        private static List<MaterialData> EnsureMaterialIds()
        {
            List<MaterialData> result = new List<MaterialData>();
            string searchFolder = AssetDatabase.IsValidFolder(MaterialFolder) ? MaterialFolder : "Assets";
            string[] guids = AssetDatabase.FindAssets("t:MaterialData", new[] { searchFolder });

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                MaterialData material = AssetDatabase.LoadAssetAtPath<MaterialData>(path);

                if (material == null)
                {
                    Debug.LogError($"[存档配置] 读取 MaterialData 失败：{path}");
                    continue;
                }

                SerializedObject serialized = new SerializedObject(material);
                SerializedProperty saveId = serialized.FindProperty("saveId");

                if (saveId == null)
                {
                    Debug.LogError($"[存档配置] {path} 上找不到 saveId 字段（脚本是否已编译？）。");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(saveId.stringValue))
                {
                    string id = AssetDatabase.AssetPathToGUID(path);
                    saveId.stringValue = id;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(material);
                    Debug.Log($"[存档配置] 原料「{material.name}」生成 Save Id：{id}", material);
                }

                result.Add(material);
            }

            if (result.Count == 0)
            {
                Debug.LogWarning($"[存档配置] {searchFolder} 下没有找到任何 MaterialData 资产。" +
                                 "库存存档会一直是空的。");
            }

            return result;
        }

        private static List<BuildingData> EnsureBuildingIds()
        {
            List<BuildingData> result = new List<BuildingData>();
            string searchFolder = AssetDatabase.IsValidFolder(BuildingFolder) ? BuildingFolder : "Assets";
            string[] guids = AssetDatabase.FindAssets("t:BuildingData", new[] { searchFolder });

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                BuildingData building = AssetDatabase.LoadAssetAtPath<BuildingData>(path);

                if (building == null)
                {
                    Debug.LogError($"[存档配置] 读取 BuildingData 失败：{path}");
                    continue;
                }

                SerializedObject serialized = new SerializedObject(building);
                SerializedProperty saveId = serialized.FindProperty("saveId");

                if (saveId == null)
                {
                    Debug.LogError($"[存档配置] {path} 上找不到 saveId 字段（脚本是否已编译？）。");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(saveId.stringValue))
                {
                    string id = AssetDatabase.AssetPathToGUID(path);
                    saveId.stringValue = id;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(building);
                    Debug.Log($"[存档配置] 建筑「{building.name}」生成 Save Id：{id}", building);
                }

                result.Add(building);
            }

            if (result.Count == 0)
            {
                Debug.LogWarning($"[存档配置] {searchFolder} 下没有找到任何 BuildingData 资产。" +
                                 "建筑存档会一直是空的。");
            }

            return result;
        }

        private static void UpdateCatalog(SaveCatalog catalog, List<MaterialData> materials)
        {
            SerializedObject serialized = new SerializedObject(catalog);
            SerializedProperty list = serialized.FindProperty("materials");

            if (list == null)
            {
                Debug.LogError("[存档配置] SaveCatalog 上找不到 materials 字段（脚本是否已编译？）。", catalog);
                return;
            }

            for (int i = 0; i < materials.Count; i++)
            {
                MaterialData material = materials[i];

                if (Contains(list, material))
                {
                    continue;
                }

                int index = list.arraySize;
                list.InsertArrayElementAtIndex(index);
                list.GetArrayElementAtIndex(index).objectReferenceValue = material;
                Debug.Log($"[存档配置] 存档目录加入原料「{material.name}」。", catalog);
            }

            // 清掉目录里可能存在的空引用，避免读档时刷一堆无意义的报错
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == null)
                {
                    list.DeleteArrayElementAtIndex(i);
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        private static void UpdateBuildingCatalog(SaveCatalog catalog, List<BuildingData> buildings)
        {
            SerializedObject serialized = new SerializedObject(catalog);
            SerializedProperty list = serialized.FindProperty("buildings");

            if (list == null)
            {
                Debug.LogError("[存档配置] SaveCatalog 上找不到 buildings 字段（脚本是否已编译？）。", catalog);
                return;
            }

            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingData building = buildings[i];

                if (Contains(list, building))
                {
                    continue;
                }

                int index = list.arraySize;
                list.InsertArrayElementAtIndex(index);
                list.GetArrayElementAtIndex(index).objectReferenceValue = building;
                Debug.Log($"[存档配置] 存档目录加入建筑「{building.name}」。", catalog);
            }

            // 清掉目录里可能存在的空引用，避免读档时刷一堆无意义的报错
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == null)
                {
                    list.DeleteArrayElementAtIndex(i);
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        private static bool Contains(SerializedProperty list, Object reference)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == reference)
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureSaveSystem(SaveCatalog catalog)
        {
#if UNITY_2023_1_OR_NEWER
            SaveSystem saveSystem = Object.FindFirstObjectByType<SaveSystem>(FindObjectsInactive.Include);
#else
            SaveSystem saveSystem = Object.FindObjectOfType<SaveSystem>(true);
#endif

            GameObject saveObject;

            if (saveSystem == null)
            {
                saveObject = new GameObject(SaveObjectName);
                Undo.RegisterCreatedObjectUndo(saveObject, "创建存档系统");
                saveSystem = saveObject.AddComponent<SaveSystem>();
                Debug.Log($"[存档配置] 已创建场景物体 {SaveObjectName}。", saveObject);
            }
            else
            {
                saveObject = saveSystem.gameObject;
            }

            CurrencySaveParticipant currencyParticipant = EnsureComponent<CurrencySaveParticipant>(saveObject);
            InventorySaveParticipant inventoryParticipant = EnsureComponent<InventorySaveParticipant>(saveObject);
            BlueprintSaveParticipant blueprintParticipant = EnsureComponent<BlueprintSaveParticipant>(saveObject);
            BuildingSaveParticipant buildingParticipant = EnsureComponent<BuildingSaveParticipant>(saveObject);

#if UNITY_2023_1_OR_NEWER
            PlayerCurrency currency = Object.FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            MaterialInventory inventory = Object.FindFirstObjectByType<MaterialInventory>(FindObjectsInactive.Include);
            BuildingConstruction construction = Object.FindFirstObjectByType<BuildingConstruction>(FindObjectsInactive.Include);
            BuildingManager buildingManager = Object.FindFirstObjectByType<BuildingManager>(FindObjectsInactive.Include);
            PlayerLevel playerLevel = Object.FindFirstObjectByType<PlayerLevel>(FindObjectsInactive.Include);
#else
            PlayerCurrency currency = Object.FindObjectOfType<PlayerCurrency>(true);
            MaterialInventory inventory = Object.FindObjectOfType<MaterialInventory>(true);
            BuildingConstruction construction = Object.FindObjectOfType<BuildingConstruction>(true);
            BuildingManager buildingManager = Object.FindObjectOfType<BuildingManager>(true);
            PlayerLevel playerLevel = Object.FindObjectOfType<PlayerLevel>(true);
#endif

            if (currency == null)
            {
                Debug.LogError("[存档配置] 场景里找不到 PlayerCurrency，金币适配器无法接线。");
            }

            if (inventory == null)
            {
                Debug.LogError("[存档配置] 场景里找不到 MaterialInventory，库存适配器无法接线。");
            }

            if (construction == null)
            {
                Debug.LogError("[存档配置] 场景里找不到 BuildingConstruction，蓝图退款无法接线。");
            }

            if (buildingManager == null)
            {
                Debug.LogError("[存档配置] 场景里找不到 BuildingManager，建筑适配器无法接线。");
            }

            if (playerLevel == null)
            {
                Debug.LogError("[存档配置] 场景里找不到 PlayerLevel，读档后等级不会立即重算。");
            }

            SetReference(currencyParticipant, "currency", currency);
            SetReference(inventoryParticipant, "inventory", inventory);
            SetReference(inventoryParticipant, "catalog", catalog);
            SetReference(blueprintParticipant, "construction", construction);
            SetReference(blueprintParticipant, "inventory", inventory);
            SetReference(blueprintParticipant, "catalog", catalog);
            SetReference(buildingParticipant, "buildingManager", buildingManager);
            SetReference(buildingParticipant, "catalog", catalog);
            SetReference(buildingParticipant, "playerLevel", playerLevel);

            // 自动保存：建造 / 拆除即时保存依赖这个引用；失去焦点保存是第 3 步才打开的
            SetReference(saveSystem, "buildingManager", buildingManager);
            SetBool(saveSystem, "saveOnFocusLost", true);
            SetBool(saveSystem, "autoSaveEnabled", true);

            // 顺序 = 执行顺序：金币 → 库存 → 蓝图退款 → 建筑
            EnsureParticipants(
                saveSystem, currencyParticipant, inventoryParticipant, blueprintParticipant, buildingParticipant);

            EditorUtility.SetDirty(saveSystem);
            EditorUtility.SetDirty(currencyParticipant);
            EditorUtility.SetDirty(inventoryParticipant);
            EditorUtility.SetDirty(blueprintParticipant);
            EditorUtility.SetDirty(buildingParticipant);
            EditorSceneManager.MarkSceneDirty(saveObject.scene);
        }

        private static T EnsureComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();

            if (component == null)
            {
                component = target.AddComponent<T>();
                Debug.Log($"[存档配置] 已给「{target.name}」添加 {typeof(T).Name}。", target);
            }

            return component;
        }

        private static void SetReference(Object target, string fieldName, Object value)
        {
            if (target == null)
            {
                Debug.LogError($"[存档配置] 无法写入字段 {fieldName}：目标组件为空。");
                return;
            }

            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogError($"[存档配置] {target.GetType().Name} 上找不到字段 {fieldName}（脚本是否已编译？）。", target);
                return;
            }

            if (value == null)
            {
                // 保留字段原值更危险（可能是上一次的旧引用），这里明确报错并跳过
                Debug.LogError($"[存档配置] {target.GetType().Name}.{fieldName} 没有可接的引用，" +
                               "请手动在 Inspector 里指定。", target);
                return;
            }

            if (property.objectReferenceValue == value)
            {
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetBool(Object target, string fieldName, bool value)
        {
            if (target == null)
            {
                Debug.LogError($"[存档配置] 无法写入字段 {fieldName}：目标组件为空。");
                return;
            }

            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogError($"[存档配置] {target.GetType().Name} 上找不到字段 {fieldName}（脚本是否已编译？）。", target);
                return;
            }

            if (property.boolValue == value)
            {
                return;
            }

            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
            Debug.Log($"[存档配置] {target.GetType().Name}.{fieldName} 已设为 {value}。", target);
        }

        private static void EnsureParticipants(SaveSystem saveSystem, params SaveParticipant[] required)
        {
            SerializedObject serialized = new SerializedObject(saveSystem);
            SerializedProperty list = serialized.FindProperty("participants");

            if (list == null)
            {
                Debug.LogError("[存档配置] SaveSystem 上找不到 participants 字段（脚本是否已编译？）。", saveSystem);
                return;
            }

            for (int i = 0; i < required.Length; i++)
            {
                SaveParticipant participant = required[i];

                if (participant == null)
                {
                    continue;
                }

                bool found = false;

                for (int j = 0; j < list.arraySize; j++)
                {
                    if (list.GetArrayElementAtIndex(j).objectReferenceValue == participant)
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
                {
                    continue;
                }

                int index = list.arraySize;
                list.InsertArrayElementAtIndex(index);
                list.GetArrayElementAtIndex(index).objectReferenceValue = participant;
                Debug.Log($"[存档配置] 把 {participant.GetType().Name} 加入 SaveSystem 的执行列表。", saveSystem);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(saveSystem);
        }
    }
}
