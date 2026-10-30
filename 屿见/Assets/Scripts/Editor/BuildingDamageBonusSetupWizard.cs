using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Building;
using Yujian.Building.Effects;
using Yujian.Player;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 给场景接上「建筑材料 → 伤害倍率加成」组件（D1=B）。
    /// 入口：菜单栏 → 屿见 → 建筑 → 配置材料倍率加成
    ///
    /// 幂等：重复运行只复用已有的 BuildingDamageBonus，不会叠加第二份。
    /// </summary>
    public static class BuildingDamageBonusSetupWizard
    {
        [MenuItem("屿见/建筑/配置材料倍率加成")]
        public static void Configure()
        {
            PlayerStats playerStats = Object.FindFirstObjectByType<PlayerStats>(FindObjectsInactive.Include);
            BuildingManager buildingManager = Object.FindFirstObjectByType<BuildingManager>(FindObjectsInactive.Include);

            if (playerStats == null)
            {
                Fail("场景里找不到 PlayerStats。请先完成玩家属性配置。");
                return;
            }

            if (buildingManager == null)
            {
                Fail("场景里找不到 BuildingManager。请先运行建筑系统配置向导。");
                return;
            }

            GameObject host = playerStats.gameObject;
            BuildingDamageBonus bonus = host.GetComponent<BuildingDamageBonus>();

            if (bonus == null)
            {
                bonus = Undo.AddComponent<BuildingDamageBonus>(host);
            }

            SerializedObject serialized = new SerializedObject(bonus);
            serialized.FindProperty("buildingManager").objectReferenceValue = buildingManager;
            serialized.FindProperty("playerStats").objectReferenceValue = playerStats;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(host.scene);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            Debug.Log($"[BuildingDamageBonusSetupWizard] 已在「{host.name}」上配置 BuildingDamageBonus：" +
                      $"建筑管理器 = {buildingManager.name}，玩家属性 = {playerStats.name}。" +
                      "建成建筑后材料倍率才会生效。");
        }

        private static void Fail(string message)
        {
            Debug.LogError("[BuildingDamageBonusSetupWizard] " + message);
            EditorUtility.DisplayDialog("材料倍率加成未配置", message, "知道了");
        }
    }
}
