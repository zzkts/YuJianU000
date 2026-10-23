using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Audio;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 通用模块的一键配置。
    ///
    /// 做四件事：
    ///   1. 创建 / 复用 Assets/Data/Audio/AudioCatalog.asset；
    ///   2. 在活动场景里创建 / 复用 CommonModules 物体，挂 AudioManager 与 UIManager；
    ///   3. 把配置表接到 AudioManager；
    ///   4. 给现有的 Canvas 面板补 UIPanel 组件并填好 ID（已有组件不覆盖已有 ID）。
    ///
    /// 幂等：可重复运行。不直接改 .unity / .prefab，由 Unity 自己写盘（项目规则 13）。
    /// 入口：菜单栏 → 屿见 → 配置通用模块
    /// </summary>
    public static class CommonModulesSetupWizard
    {
        private const string DataFolder = "Assets/Data/Audio";
        private const string CatalogPath = DataFolder + "/AudioCatalog.asset";
        private const string RootName = "CommonModules";

        private static readonly (string path, UIPanelId id)[] PanelMappings =
        {
            ("Canvas/ShopPanel", UIPanelId.Shop),
            ("Canvas/BuildingPanel", UIPanelId.Building),
            ("Canvas/材料面板", UIPanelId.Material),
            ("Canvas/BottomNav", UIPanelId.BottomNav),
            ("Canvas/CoinHud", UIPanelId.CoinHud),
        };

        [MenuItem("屿见/通用模块/配置通用模块（事件·音频·UI）", false, 120)]
        public static void Configure()
        {
            AudioCatalog catalog = EnsureCatalog();
            GameObject root = EnsureRoot();
            AudioManager audioManager = EnsureComponent<AudioManager>(root);
            UIManager uiManager = EnsureComponent<UIManager>(root);

            SetObjectReference(audioManager, "catalog", catalog);
            EnsurePanels();

            EditorUtility.SetDirty(audioManager);
            EditorUtility.SetDirty(uiManager);
            EditorSceneManager.MarkSceneDirty(root.scene);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[通用模块] 已完成。音频配置：{CatalogPath}；场景物体：{RootName}" +
                      $"（AudioManager / UIManager），已扫描并补齐 {PanelMappings.Length} 个面板的 UIPanel 组件。\n" +
                      "下一步：Ctrl+S 保存场景。音频条目需要自己往 AudioCatalog 里拖素材；" +
                      "没有素材时所有音效都是空操作，不影响玩法。", catalog);
        }

        private static AudioCatalog EnsureCatalog()
        {
            if (!AssetDatabase.IsValidFolder(DataFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Data"))
                {
                    AssetDatabase.CreateFolder("Assets", "Data");
                }

                AssetDatabase.CreateFolder("Assets/Data", "Audio");
            }

            AudioCatalog catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);

            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<AudioCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
                Debug.Log($"[通用模块] 已创建音频配置表：{CatalogPath}", catalog);
            }

            return catalog;
        }

        private static GameObject EnsureRoot()
        {
            GameObject existing = GameObject.Find(RootName);

            if (existing != null)
            {
                return existing;
            }

            GameObject root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "创建通用模块");
            Debug.Log($"[通用模块] 已创建场景物体 {RootName}。", root);
            return root;
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

        /// <summary>给场景里的面板补 UIPanel。已有组件只补空 ID，不覆盖用户手动填过的值。</summary>
        private static void EnsurePanels()
        {
            for (int i = 0; i < PanelMappings.Length; i++)
            {
                (string path, UIPanelId id) = PanelMappings[i];
                GameObject target = GameObject.Find(path);

                if (target == null)
                {
                    Debug.Log($"[通用模块] 场景里没有「{path}」，跳过（该面板可能还没做）。");
                    continue;
                }

                UIPanel panel = target.GetComponent<UIPanel>();

                if (panel == null)
                {
                    panel = Undo.AddComponent<UIPanel>(target);
                }

                SerializedObject serialized = new SerializedObject(panel);
                SerializedProperty panelId = serialized.FindProperty("panelId");

                if (panelId == null)
                {
                    Debug.LogError($"[通用模块] 「{path}」的 UIPanel 上找不到 panelId 字段（脚本是否已编译？）。");
                    continue;
                }

                if (panelId.enumValueIndex == (int)UIPanelId.None)
                {
                    panelId.enumValueIndex = (int)id;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                else if (panelId.enumValueIndex != (int)id)
                {
                    Debug.LogWarning($"[通用模块]「{path}」的 UIPanel 已填成 " +
                                     $"{(UIPanelId)panelId.enumValueIndex}，与向导预期的 {id} 不同，已保留原值。", panel);
                }

                EditorUtility.SetDirty(panel);
            }
        }

        private static void SetObjectReference(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogError($"[通用模块] {target.GetType().Name} 上找不到字段 {fieldName}（脚本是否已编译？）。", target);
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
