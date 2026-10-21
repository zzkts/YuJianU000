using UnityEditor;
using UnityEngine;
using Yujian.Mining;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 挖矿 2.1-3「伤害 UI 显示功能」的一键配置。
    ///
    /// 做两件事：
    ///   1. 创建 / 复用 Assets/Data/Mining/DamageNumberTable.asset；
    ///   2. 表为空时按策划案填默认两行，并把表接到 Assets/Prefabs/DamagePopup.prefab 上。
    ///
    /// 幂等：已有数据一律不覆盖，可重复运行。
    /// 不直接改 .unity / .prefab，由 Unity 自己写盘（项目规则 13）。
    ///
    /// 入口：菜单栏 → 屿见 → 配置伤害数字配置表（挖矿 2.1-3）
    /// </summary>
    public static class DamageNumberSetupWizard
    {
        private const string DataFolder = "Assets/Data/Mining";
        private const string TablePath = DataFolder + "/DamageNumberTable.asset";
        private const string PopupPrefabPath = "Assets/Prefabs/DamagePopup.prefab";

        [MenuItem("屿见/配置伤害数字配置表（挖矿 2.1-3）")]
        public static void Configure()
        {
            DamageNumberTable table = EnsureTable();
            AssignTableToPopupPrefab(table);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[伤害数字配置] 已完成：配置表 {TablePath}；预制体 {PopupPrefabPath} 已接线。" +
                      "改颜色 / 字号 / 粗体直接编辑配置表即可，不需要改代码。", table);
        }

        private static DamageNumberTable EnsureTable()
        {
            DamageNumberTable table = AssetDatabase.LoadAssetAtPath<DamageNumberTable>(TablePath);

            if (table == null)
            {
                EnsureFolder();
                table = ScriptableObject.CreateInstance<DamageNumberTable>();
                AssetDatabase.CreateAsset(table, TablePath);
                Debug.Log($"[伤害数字配置] 已创建配置表：{TablePath}", table);
            }

            if (table.Styles == null || table.Styles.Count == 0)
            {
                FillDefaultStyles(table);
            }

            return table;
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(DataFolder))
            {
                AssetDatabase.CreateFolder("Assets/Data", "Mining");
            }
        }

        /// <summary>按策划案 2.1-3 的表格填默认两行。</summary>
        private static void FillDefaultStyles(DamageNumberTable table)
        {
            SerializedObject serializedTable = new SerializedObject(table);
            SerializedProperty styles = serializedTable.FindProperty("styles");

            if (styles == null)
            {
                Debug.LogError("[伤害数字配置] DamageNumberTable 上找不到 styles 字段（脚本是否已编译？）。");
                return;
            }

            styles.arraySize = 2;

            ConfigureStyle(styles.GetArrayElementAtIndex(0), DamageSource.Building, "Type_1",
                new Color(0.25f, 0.55f, 1f, 1f), 16, false);

            ConfigureStyle(styles.GetArrayElementAtIndex(1), DamageSource.Player, "Type_2",
                new Color(1f, 0.85f, 0.3f, 1f), 18, true);

            serializedTable.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(table);
        }

        private static void ConfigureStyle(
            SerializedProperty element,
            DamageSource source,
            string typeName,
            Color color,
            int fontSize,
            bool bold)
        {
            element.FindPropertyRelative("source").enumValueIndex = (int)source;
            element.FindPropertyRelative("typeName").stringValue = typeName;
            element.FindPropertyRelative("color").colorValue = color;
            element.FindPropertyRelative("fontSize").intValue = fontSize;
            element.FindPropertyRelative("bold").boolValue = bold;
        }

        private static void AssignTableToPopupPrefab(DamageNumberTable table)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PopupPrefabPath);

            if (prefab == null)
            {
                Debug.LogError($"[伤害数字配置] 找不到预制体 {PopupPrefabPath}，配置表没有接线。");
                return;
            }

            DamagePopup popup = prefab.GetComponent<DamagePopup>();

            if (popup == null)
            {
                Debug.LogError($"[伤害数字配置] {PopupPrefabPath} 上没有 DamagePopup 组件。");
                return;
            }

            SerializedObject serializedPopup = new SerializedObject(popup);
            SerializedProperty tableProperty = serializedPopup.FindProperty("styleTable");

            if (tableProperty == null)
            {
                Debug.LogError("[伤害数字配置] DamagePopup 上找不到 styleTable 字段（脚本是否已编译？）。");
                return;
            }

            if (tableProperty.objectReferenceValue == table)
            {
                Debug.Log("[伤害数字配置] 预制体已经接好配置表，跳过。", popup);
                return;
            }

            tableProperty.objectReferenceValue = table;
            serializedPopup.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(popup);
            PrefabUtility.SavePrefabAsset(prefab);

            Debug.Log("[伤害数字配置] 已把配置表接到 DamagePopup 预制体。", popup);
        }
    }
}
