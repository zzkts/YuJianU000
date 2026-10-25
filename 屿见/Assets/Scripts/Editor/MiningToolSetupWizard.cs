using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Mining;
using Yujian.Player;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 阶段 9：按策划表创建“挖矿工具升级”原型数据，并把工具进度接到伤害系统。
    ///
    /// 入口：菜单栏 → 屿见 → 配置挖矿工具升级（阶段 9）
    /// </summary>
    public static class MiningToolSetupWizard
    {
        private const string ToolFolder = "Assets/Data/Mining/Tools";
        private const string ToolPath = ToolFolder + "/MiningTool_石头.asset";

        [MenuItem("屿见/配置挖矿工具升级（阶段 9）")]
        public static void Configure()
        {
            EnsureFolder("Assets/Data", "Mining");
            EnsureFolder("Assets/Data/Mining", "Tools");

            bool created;
            MiningToolData toolData = LoadOrCreate(out created);

            if (created)
            {
                toolData.ConfigureForSetup("石头", "mine_01", 1, 1, 15, 15);
                EditorUtility.SetDirty(toolData);
            }

            PlayerStats playerStats = Object.FindFirstObjectByType<PlayerStats>(
                FindObjectsInactive.Include);

            if (playerStats == null)
            {
                Debug.LogError("[挖矿工具] 场景里找不到 PlayerStats，无法挂载升级进度。" +
                               "请先确认 Player 物体存在。");
                return;
            }

            PlayerCurrency currency = Object.FindFirstObjectByType<PlayerCurrency>(
                FindObjectsInactive.Include);

            if (currency == null)
            {
                Debug.LogWarning("[挖矿工具] 场景里找不到 PlayerCurrency，升级时无法扣金币。" +
                                 "请先完成金币系统配置。");
            }

            GameObject playerObject = playerStats.gameObject;
            MiningToolProgress progress = playerObject.GetComponent<MiningToolProgress>();
            if (progress == null)
            {
                progress = Undo.AddComponent<MiningToolProgress>(playerObject);
            }

            progress.ConfigureForSetup(toolData, currency, 1);
            EditorUtility.SetDirty(progress);

            DamageSystem damageSystem = Object.FindFirstObjectByType<DamageSystem>(
                FindObjectsInactive.Include);

            if (damageSystem == null)
            {
                Debug.LogWarning("[挖矿工具] 场景里找不到 DamageSystem，伤害不会读取工具攻击力。" +
                                 "请先完成挖矿系统配置。");
            }
            else
            {
                damageSystem.ConfigureForSetup(progress);
                EditorUtility.SetDirty(damageSystem);
            }

            MiningToolUpgradeSetupWizard.ConfigureCard(false);

            EditorSceneManager.MarkSceneDirty(playerObject.scene);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[挖矿工具] 配置完成：石头 mine_01，1 级攻击 1，每级 +1，每级 15 金币。\n" +
                      "商店面板已加入正式的「升级」按钮；进入 Play 后打开底部「商店」即可使用。", progress);
        }

        private static MiningToolData LoadOrCreate(out bool created)
        {
            MiningToolData asset = AssetDatabase.LoadAssetAtPath<MiningToolData>(ToolPath);
            if (asset != null)
            {
                created = false;
                return asset;
            }

            asset = ScriptableObject.CreateInstance<MiningToolData>();
            AssetDatabase.CreateAsset(asset, ToolPath);
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
