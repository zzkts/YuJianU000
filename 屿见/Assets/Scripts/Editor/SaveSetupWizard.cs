using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Player;
using Yujian.Save;
using Yujian.Shop;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 存档系统的一键配置。
    ///
    /// 做四件事：
    ///   1. 创建 / 复用 Assets/Data/Save/SaveCatalog.asset；
    ///   2. 给 Assets/Data/Materials 下每个 MaterialData 补一个稳定的 Save Id（取资产 GUID）；
    ///   3. 把材料资产登记进存档目录（保留已有条目，只补缺失）；
    ///   4. 在活动场景创建 / 复用 SaveSystem 物体，挂上金币与库存适配器并接线。
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
        private const string SaveObjectName = "SaveSystem";

        [MenuItem("屿见/配置存档系统")]
        public static void Configure()
        {
            SaveCatalog catalog = EnsureCatalog();
            List<MaterialData> materials = EnsureMaterialIds();
            UpdateCatalog(catalog, materials);
            EnsureSaveSystem(catalog);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[存档配置] 已完成。存档目录：{CatalogPath}；" +
                      $"场景物体：{SaveObjectName}。\n" +
                      "下一步：Ctrl+S 保存场景，进 Play Mode 验证金币与材料能否存读。", catalog);
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

        private static bool Contains(SerializedProperty list, MaterialData material)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == material)
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

#if UNITY_2023_1_OR_NEWER
            PlayerCurrency currency = Object.FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            MaterialInventory inventory = Object.FindFirstObjectByType<MaterialInventory>(FindObjectsInactive.Include);
#else
            PlayerCurrency currency = Object.FindObjectOfType<PlayerCurrency>(true);
            MaterialInventory inventory = Object.FindObjectOfType<MaterialInventory>(true);
#endif

            if (currency == null)
            {
                Debug.LogError("[存档配置] 场景里找不到 PlayerCurrency，金币适配器无法接线。");
            }

            if (inventory == null)
            {
                Debug.LogError("[存档配置] 场景里找不到 MaterialInventory，库存适配器无法接线。");
            }

            SetReference(currencyParticipant, "currency", currency);
            SetReference(inventoryParticipant, "inventory", inventory);
            SetReference(inventoryParticipant, "catalog", catalog);

            EnsureParticipants(saveSystem, currencyParticipant, inventoryParticipant);

            EditorUtility.SetDirty(saveSystem);
            EditorUtility.SetDirty(currencyParticipant);
            EditorUtility.SetDirty(inventoryParticipant);
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
