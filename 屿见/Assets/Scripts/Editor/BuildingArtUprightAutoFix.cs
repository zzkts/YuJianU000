using UnityEditor;
using UnityEngine;

namespace Yujian.EditorTools
{
    /// <summary>
    /// 一次性自动修复：Meshy 模型是 Z-up，FBX 根节点带 -90° X 旋转；
    /// 之前 <see cref="BuildingArtSetupWizard"/> 把实例化出来的模型 rotation 重置成了 identity，
    /// 模型于是向后倒下。向导已经改成保留导入旋转，这个脚本负责把现有预制体重新生成一遍。
    ///
    /// 只跑一次（EditorPrefs 标记），之后不会在每次域重载时重复生成。
    /// 需要重跑时用菜单 屿见/建筑/修复模型朝向（一次性）。
    /// </summary>
    [InitializeOnLoad]
    internal static class BuildingArtUprightAutoFix
    {
        private const string PrefKey = "Yujian.BuildingArtUprightFix.v1";
        private const string ProbePrefab = "Assets/Prefabs/建筑/建筑_风琴博物馆.prefab";
        private const string ModelNodeName = "模型";

        static BuildingArtUprightAutoFix()
        {
            EditorApplication.delayCall += TryRun;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            // 如果脚本编译时正好在 Play 里，等退出 Play 后再修
            if (change == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.delayCall += TryRun;
            }
        }

        [MenuItem("屿见/建筑/修复模型朝向（一次性）", false, 516)]
        private static void MenuRun()
        {
            EditorPrefs.DeleteKey(PrefKey);
            TryRun();
        }

        private static void TryRun()
        {
            if (EditorPrefs.GetBool(PrefKey, false))
            {
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // 进 Play 后再改预制体容易出问题；交给用户退出 Play 后手动点菜单
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryRun;
                return;
            }

            if (!NeedsFix())
            {
                EditorPrefs.SetBool(PrefKey, true);
                return;
            }

            EditorPrefs.SetBool(PrefKey, true);
            Debug.Log("[BuildingArtUpright] 检测到模型被重置为水平，正在重新生成建筑/蓝图预制体...");

            BuildingArtSetupWizard.Configure(false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[BuildingArtUpright] 完成：模型朝向已恢复为 FBX 自带的 -90° X。");
        }

        private static bool NeedsFix()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(ProbePrefab);

            if (root == null)
            {
                return false;
            }

            try
            {
                Transform model = root.transform.Find(ModelNodeName);

                if (model == null)
                {
                    return false;
                }

                // 被重置过 = identity；已修复后应该是 FBX 根节点的 -90° X
                return Quaternion.Angle(model.localRotation, Quaternion.identity) < 0.01f;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
