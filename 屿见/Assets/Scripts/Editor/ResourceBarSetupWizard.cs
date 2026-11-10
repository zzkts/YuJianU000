using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 只保留挖矿界面里的资源栏。
    ///
    /// 场景里的 Canvas/CoinHud 是早期常驻的「垃圾：N」栏；
    /// 挖矿界面（MiningHudUI）自带一份资源栏，两者都锚在右上角，
    /// 进清洁区后会叠成两层。
    ///
    /// 本命令把常驻垃圾栏所在的 GameObject 停用并保存场景；
    /// 挖矿界面里的资源栏是运行时生成的，不受影响。
    ///
    /// 入口：菜单栏 → 屿见 → 界面 → 只保留挖矿资源栏
    /// </summary>
    public static class ResourceBarSetupWizard
    {
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("屿见/界面/只保留挖矿资源栏", false, 250)]
        public static void KeepOnlyMiningResourceBar()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("资源栏", "请先退出 Play Mode，再运行本命令。", "知道了");
                return;
            }

            CoinHudUI[] huds = FindCoinHuds();

            if (huds.Length == 0)
            {
                EditorUtility.DisplayDialog("资源栏",
                    "场景里没有找到常驻垃圾栏（CoinHudUI）。\n" +
                    "如果画面里仍然有两层资源栏，请把它的物体名告诉我。", "知道了");
                return;
            }

            int changed = 0;
            Scene scene = default;

            for (int i = 0; i < huds.Length; i++)
            {
                CoinHudUI hud = huds[i];

                if (hud == null || !hud.gameObject.activeSelf)
                {
                    continue;
                }

                Undo.RecordObject(hud.gameObject, "Hide Standalone Coin Hud");
                hud.gameObject.SetActive(false);
                EditorUtility.SetDirty(hud.gameObject);
                scene = hud.gameObject.scene;
                changed++;
            }

            if (changed > 0 && scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            Debug.Log($"[资源栏] 已停用 {changed} 个常驻垃圾栏，场景已保存；" +
                      "挖矿界面的资源栏保持不变。");

            if (changed == 0)
            {
                EditorUtility.DisplayDialog("资源栏", "常驻垃圾栏已经处于停用状态。", "知道了");
            }
        }

        /// <summary>
        /// 批处理时没有打开场景，自动打开主场景再找；
        /// 正常编辑器里找不到就直接返回，交给调用方提示。
        /// </summary>
        private static CoinHudUI[] FindCoinHuds()
        {
            CoinHudUI[] huds = Object.FindObjectsByType<CoinHudUI>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (huds.Length > 0 || !Application.isBatchMode || !File.Exists(SampleScenePath))
            {
                return huds;
            }

            EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
            return Object.FindObjectsByType<CoinHudUI>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
        }
    }
}
