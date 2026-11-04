using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yujian.Core;
using Yujian.Mining;
using Yujian.Player;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 一键挂上挖矿界面（飞书 2.1 挖矿系统界面设计）。
    /// 入口：菜单栏 → 屿见 → 挖矿 → 配置挖矿界面
    ///
    /// 只做一件事：在场景里放一个挂着 MiningHudUI 的物体并接好引用。
    /// 界面本体在运行时自动生成，不需要往场景里拖任何 UI 控件。
    /// </summary>
    public static class MiningHudSetupWizard
    {
        private const string RootName = "挖矿界面";

        [MenuItem("屿见/挖矿/配置挖矿界面", false, 338)]
        public static void Configure()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("挖矿界面", "请先退出 Play Mode，再运行本命令。", "知道了");
                return;
            }

            GameAreaController areas =
                Object.FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);
            PlayerCurrency currency =
                Object.FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            MiningLightTool lightTool =
                Object.FindFirstObjectByType<MiningLightTool>(FindObjectsInactive.Include);
            MiningToolProgress miningTool =
                Object.FindFirstObjectByType<MiningToolProgress>(FindObjectsInactive.Include);

            if (areas == null)
            {
                Fail("场景里找不到 GameAreaController，请先运行「屿见/整合/配置 MVP 整合（阶段 6）」。");
                return;
            }

            // 照明工具是挖矿界面的依赖。场景里还没有时顺手补上，
            // 免得用户要在两个菜单之间来回点。
            if (lightTool == null)
            {
                if (!MiningLightToolSetupWizard.ConfigureLightTool(false))
                {
                    Debug.LogWarning("[挖矿界面] 自动补照明工具失败，" +
                                     "请先手动运行「屿见/挖矿/配置照明工具」。");
                }

                lightTool = Object.FindFirstObjectByType<MiningLightTool>(FindObjectsInactive.Include);
            }

            // 暴击点系统同理：没有就先配好，保证进矿洞就能玩。
            if (Object.FindFirstObjectByType<CritPointSystem>(FindObjectsInactive.Include) == null)
            {
                CritPointSetupWizard.Configure();
            }

            MiningHudUI hud = Object.FindFirstObjectByType<MiningHudUI>(FindObjectsInactive.Include);

            if (hud == null)
            {
                GameObject root = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(root, "创建挖矿界面");
                hud = Undo.AddComponent<MiningHudUI>(root);
            }

            hud.ConfigureForSetup(areas, currency, lightTool, miningTool);
            EditorUtility.SetDirty(hud);

            // 挖矿界面的两个升级列表共用同一份进度：退出游戏后等级也要保留。
            SaveSetupWizard.EnsureMiningParticipants();

            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();
            Selection.activeGameObject = hud.gameObject;

            string missing = string.Empty;

            if (currency == null)
            {
                missing += "\n· PlayerCurrency（金币）";
            }

            if (lightTool == null)
            {
                missing += "\n· MiningLightTool（照明工具）";
            }

            if (miningTool == null)
            {
                missing += "\n· MiningToolProgress（挖矿工具）";
            }

            if (!string.IsNullOrEmpty(missing))
            {
                Debug.LogWarning("[挖矿界面] 已挂上，但这些依赖没找到，界面会显示「未配置」：" + missing);
            }

            EditorUtility.DisplayDialog("挖矿界面",
                "挖矿界面已经挂好并保存。\n\n" +
                "进入 Play 后，从岛上矿山进入矿洞即可看到：\n" +
                "左侧装备、右侧两个升级列表、金币栏、返回主岛、x1/Max。\n\n" +
                "照明工具与暴击点系统也已一起检查并补好。",
                "知道了");

            Debug.Log("[挖矿界面] 已配置并保存。");
        }

        private static void Fail(string message)
        {
            Debug.LogError("[挖矿界面] " + message);
            EditorUtility.DisplayDialog("挖矿界面未配置", message, "知道了");
        }
    }
}