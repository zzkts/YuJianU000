using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yujian.UI;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 把运行时生成的清洁界面落成场景里真实存在的 UI，方便直接在 Hierarchy 拖控件调位置。
    /// 入口：菜单栏 → 屿见 → 挖矿 → 生成可编辑清洁界面
    ///
    /// 生成后 <see cref="MiningHudUI"/> 运行时优先绑定这份场景 UI；
    /// 如果以后想回到纯代码生成，删掉「清洁界面」下的 MiningHUDRuntime 子物体即可。
    /// </summary>
    public static class MiningHudBakeWizard
    {
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string CanvasName = "MiningHUDRuntime";

        [MenuItem("屿见/挖矿/生成可编辑清洁界面", false, 339)]
        public static void Bake()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("清洁界面", "请先退出 Play Mode，再运行本命令。", "知道了");
                return;
            }

            Scene scene = EnsureSceneOpen();

            if (!scene.IsValid())
            {
                Fail("当前没有打开场景，无法生成清洁界面。");
                return;
            }

            MiningHudUI hud =
                Object.FindFirstObjectByType<MiningHudUI>(FindObjectsInactive.Include);

            if (hud == null)
            {
                Fail("场景里找不到清洁界面（MiningHudUI）。\n\n" +
                     "请先运行菜单「屿见/挖矿/配置挖矿界面」。");
                return;
            }

            Transform existing = FindAuthoredRoot(hud.transform);

            if (existing != null &&
                !EditorUtility.DisplayDialog("生成可编辑清洁界面",
                    "场景里已经有生成好的清洁界面。\n\n" +
                    "重新生成会覆盖你在 Hierarchy 里调过的位置、大小和颜色。\n" +
                    "继续？",
                    "重新生成", "取消"))
            {
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(hud.gameObject, "生成可编辑清洁界面");

            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            hud.BuildAuthoredHierarchyForEditor();
            EditorUtility.SetDirty(hud);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = hud.gameObject;

            Debug.Log("[清洁界面] 已生成可编辑 UI 并保存场景。\n" +
                      "展开「清洁界面」下的 MiningHUDRuntime，直接拖控件调整位置；" +
                      "运行时不会再生成第二份。");

            EditorUtility.DisplayDialog("清洁界面",
                "已生成并保存。\n\n" +
                "在 Hierarchy 展开「清洁界面 → MiningHUDRuntime → Hud」，\n" +
                "里面的返回按钮、垃圾栏、装备栏、升级面板、血条、\n" +
                "剩余波次 / BOSS 倒计时都可以直接拖拽调整。\n\n" +
                "改完保存即可；进入 Play 后会自动使用这份 UI。",
                "知道了");
        }

        private static Transform FindAuthoredRoot(Transform hudTransform)
        {
            MiningHudAuthoredRoot marker =
                hudTransform.GetComponentInChildren<MiningHudAuthoredRoot>(true);

            if (marker != null)
            {
                return marker.transform;
            }

            for (int i = 0; i < hudTransform.childCount; i++)
            {
                Transform child = hudTransform.GetChild(i);

                if (child.name == CanvasName)
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>批处理时没有打开场景，先打开主场景再找。</summary>
        private static Scene EnsureSceneOpen()
        {
            Scene active = EditorSceneManager.GetActiveScene();

            if (Object.FindFirstObjectByType<MiningHudUI>(FindObjectsInactive.Include) != null)
            {
                return active;
            }

            if (!File.Exists(SampleScenePath))
            {
                return active;
            }

            if (Application.isBatchMode || string.IsNullOrEmpty(active.path))
            {
                return EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
            }

            return active;
        }

        private static void Fail(string message)
        {
            Debug.LogError("[清洁界面] " + message);
            EditorUtility.DisplayDialog("清洁界面未生成", message, "知道了");
        }
    }
}
